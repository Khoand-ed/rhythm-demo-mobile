using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Promuse.Api.Economy;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Mail;
using Promuse.Contracts.Players;
using Promuse.Persistence;
using Promuse.Persistence.Entities;

namespace Promuse.Api.Mailbox;

/// <summary>
/// The mailbox: what is in a player's box, opening a mail (which collects what it carries),
/// collecting everything, and clearing the opened ones; and sending, for administrators.
///
/// 全有或全无 / Opening records the receipt and pays the attachments in one transaction, and the
/// receipt is an insert that only one of two racing opens can make - so a mail pays exactly once.
/// </summary>
public sealed class MailService(PromuseDbContext db, TimeProvider clock, PlayerService players, Inventory inventory)
{
    /// <summary>A box holds this many; past it the oldest are not shown, as the screen says.</summary>
    public const int Capacity = 100;

    public const int MaxAttachments = 10;
    public const int MaxAmount = 1_000_000;

    // ------------------------------------------------------------------ player

    public async Task<MailBox> BoxAsync(Guid accountId, CancellationToken ct)
    {
        List<(MailMessage Mail, bool Opened)> mails = await VisibleAsync(accountId, clock.GetUtcNow(), ct);
        return ToBox(mails);
    }

    public async Task<Outcome<MailActionResult>> OpenAsync(Guid accountId, Guid mailId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        List<(MailMessage Mail, bool Opened)> box = await VisibleAsync(accountId, now, ct);

        (MailMessage Mail, bool Opened) found = box.FirstOrDefault(m => m.Mail.Id == mailId);
        if (found.Mail is null) return Outcome<MailActionResult>.Fail(ApiProblems.MailNotFound());

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        List<ItemStack> granted = await OpenOneAsync(accountId, found.Mail, now, ct);
        if (granted.Count > 0) await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        return await ResultAsync(accountId, granted, ct);
    }

    /// <summary>Opens every new mail in the box and collects all of it, totalled per item.</summary>
    public async Task<Outcome<MailActionResult>> CollectAllAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        List<(MailMessage Mail, bool Opened)> box = await VisibleAsync(accountId, now, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var totals = new Dictionary<int, int>();
        foreach ((MailMessage mail, bool opened) in box)
        {
            if (opened) continue;
            foreach (ItemStack stack in await OpenOneAsync(accountId, mail, now, ct))
            {
                totals[stack.ItemId] = totals.GetValueOrDefault(stack.ItemId) + stack.Amount;
            }
        }

        if (totals.Count > 0) await inventory.BumpStateVersionAsync(accountId, now, ct);
        await transaction.CommitAsync(ct);

        return await ResultAsync(accountId, [.. totals.OrderBy(t => t.Key).Select(t => new ItemStack(t.Key, t.Value))], ct);
    }

    /// <summary>Delete Read: every opened mail leaves the box. Nothing is lost - opened means collected.</summary>
    public async Task<Outcome<MailActionResult>> DeleteOpenedAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        await db.MailReceipts
            .Where(r => r.AccountId == accountId && r.DeletedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.DeletedAt, now), ct);

        return await ResultAsync(accountId, [], ct);
    }

    /// <summary>
    /// The receipt is the claim: inserted only if absent, so of two opens racing for one mail exactly
    /// one inserts - and only that one pays.
    /// </summary>
    private async Task<List<ItemStack>> OpenOneAsync(Guid accountId, MailMessage mail, DateTimeOffset now, CancellationToken ct)
    {
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO mail_receipts (account_id, mail_id, opened_at)
             VALUES ({accountId}, {mail.Id}, {now})
             ON CONFLICT (account_id, mail_id) DO NOTHING
             """, ct);

        if (inserted == 0) return [];

        List<ItemStack> attachments = ParseAttachments(mail.Attachments);
        foreach (ItemStack stack in attachments)
        {
            await inventory.CreditAsync(accountId, stack.ItemId, stack.Amount, ct);
        }

        return attachments;
    }

    /// <summary>
    /// The box: mails to this player, and mails to everyone sent since they joined; not expired, not
    /// retracted, not deleted by them. Newest first, the first <see cref="Capacity"/>.
    /// </summary>
    private async Task<List<(MailMessage Mail, bool Opened)>> VisibleAsync(Guid accountId, DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset joined = await db.Players.AsNoTracking()
            .Where(p => p.AccountId == accountId).Select(p => p.CreatedAt).FirstOrDefaultAsync(ct);

        var rows = await (
                from m in db.Mails.AsNoTracking()
                where (m.ToAccountId == accountId || (m.ToAccountId == null && m.SentAt >= joined))
                      && m.RetractedAt == null && m.ExpiresAt > now
                let receipt = db.MailReceipts.FirstOrDefault(r => r.AccountId == accountId && r.MailId == m.Id)
                where receipt == null || receipt.DeletedAt == null
                orderby m.SentAt descending
                select new { Mail = m, Opened = receipt != null })
            .Take(Capacity)
            .ToListAsync(ct);

        return [.. rows.Select(r => (r.Mail, r.Opened))];
    }

    private MailBox ToBox(List<(MailMessage Mail, bool Opened)> mails) =>
        new([.. mails.Select(m => new MailItem(
                m.Mail.Id, m.Mail.Title, m.Mail.Body, m.Mail.Sender, m.Mail.SenderCharacterId,
                ParseAttachments(m.Mail.Attachments), m.Mail.SentAt, m.Mail.ExpiresAt, m.Opened))],
            mails.Count(m => !m.Opened), Capacity, clock.GetUtcNow());

    private async Task<Outcome<MailActionResult>> ResultAsync(Guid accountId, List<ItemStack> granted, CancellationToken ct)
    {
        MailBox box = await BoxAsync(accountId, ct);
        Outcome<PlayerState> state = await players.GetStateAsync(accountId, ct);
        if (!state.IsSuccess) return Outcome<MailActionResult>.Fail(state.Problem!);

        return Outcome<MailActionResult>.Ok(new MailActionResult(granted, box, state.Value!), created: true);
    }

    public static List<ItemStack> ParseAttachments(string json) =>
        JsonSerializer.Deserialize<List<ItemStack>>(json, SeedEvents.Json) ?? [];

    // ------------------------------------------------------------------ admin

    public async Task<Outcome<AdminMail>> SendAsync(Guid adminId, MailSendRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = [message];

        string title = request.Title?.Trim() ?? "";
        string body = request.Body?.Trim() ?? "";
        string sender = request.Sender?.Trim() ?? "";
        string? character = string.IsNullOrWhiteSpace(request.SenderCharacterId) ? null : request.SenderCharacterId.Trim();
        IReadOnlyList<ItemStack> attachments = request.Attachments ?? [];

        if (title.Length is 0 or > 60) Add("title", "1-60 characters.");
        if (body.Length is 0 or > 2000) Add("body", "1-2000 characters.");
        if (sender.Length is 0 or > 40) Add("sender", "1-40 characters.");
        if (character is { Length: > 32 }) Add("senderCharacterId", "At most 32 characters.");
        if (request.ExpiresInDays is < 1 or > 365) Add("expiresInDays", "Between 1 and 365.");
        if (attachments.Count > MaxAttachments) Add("attachments", $"At most {MaxAttachments}.");
        for (int i = 0; i < attachments.Count; i++)
        {
            if (attachments[i] is null || attachments[i].ItemId < 0) Add($"attachments[{i}].itemId", "Must not be negative.");
            else if (attachments[i].Amount is < 1 or > MaxAmount) Add($"attachments[{i}].amount", $"Between 1 and {MaxAmount}.");
        }

        Guid? to = null;
        string? toName = string.IsNullOrWhiteSpace(request.ToUsername) ? null : request.ToUsername.Trim();
        if (toName is not null)
        {
            to = await db.Accounts.Where(a => a.Username == toName).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            if (to is null) Add("toUsername", $"No player '{toName}'.");
        }

        if (errors.Count > 0) return Outcome<AdminMail>.Fail(ApiProblems.ValidationFailed(errors));

        DateTimeOffset now = clock.GetUtcNow();
        var mail = new MailMessage
        {
            Id = Guid.NewGuid(),
            ToAccountId = to,
            Title = title,
            Body = body,
            Sender = sender,
            SenderCharacterId = character,
            Attachments = JsonSerializer.Serialize(attachments, SeedEvents.Json),
            SentAt = now,
            ExpiresAt = now.AddDays(request.ExpiresInDays),
            SentBy = adminId,
        };

        db.Mails.Add(mail);
        await db.SaveChangesAsync(ct);

        string? author = await db.Accounts.Where(a => a.Id == adminId).Select(a => a.Username).FirstOrDefaultAsync(ct);
        return Outcome<AdminMail>.Ok(ToAdmin(mail, toName, author, 0), created: true);
    }

    /// <summary>The last hundred sent, newest first.</summary>
    public async Task<AdminMailList> ListAsync(CancellationToken ct)
    {
        var rows = await db.Mails.AsNoTracking()
            .OrderByDescending(m => m.SentAt)
            .Take(100)
            .Select(m => new
            {
                Mail = m,
                To = db.Accounts.Where(a => a.Id == m.ToAccountId).Select(a => a.Username).FirstOrDefault(),
                By = db.Accounts.Where(a => a.Id == m.SentBy).Select(a => a.Username).FirstOrDefault(),
                Opened = db.MailReceipts.Count(r => r.MailId == m.Id),
            })
            .ToListAsync(ct);

        return new AdminMailList([.. rows.Select(r => ToAdmin(r.Mail, r.To, r.By, r.Opened))]);
    }

    /// <summary>Takes a mail out of every box. What was already collected stays collected.</summary>
    public async Task<Outcome<bool>> RetractAsync(Guid mailId, CancellationToken ct)
    {
        int changed = await db.Mails
            .Where(m => m.Id == mailId && m.RetractedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.RetractedAt, clock.GetUtcNow()), ct);

        return changed == 0 ? Outcome<bool>.Fail(ApiProblems.MailNotFound()) : Outcome<bool>.Ok(true);
    }

    private static AdminMail ToAdmin(MailMessage m, string? to, string? by, int opened) =>
        new(m.Id, m.Title, m.Body, m.Sender, m.SenderCharacterId, ParseAttachments(m.Attachments),
            m.SentAt, m.ExpiresAt, to, by, opened, m.RetractedAt is not null);
}
