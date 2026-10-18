using System.Net;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Mail;
using Promuse.Contracts.Moderation;
using Promuse.Contracts.Players;

namespace Promuse.Api.Tests;

/// <summary>
/// The admin page's Signatures tab: the list of recent lines, finding one player, taking a line
/// down with a lock and a mail, lifting the lock, and the history of all of it.
/// </summary>
[Collection(ApiCollection.Name)]
public class SignatureModerationEndpointTests(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    // --------------------------------------------------------------- helpers

    private async Task<(PlayerState State, string ETag)> StateAsync(AuthSession player)
    {
        HttpResponseMessage response = await Client.SendAsync(Send(HttpMethod.Get, "/v1/players/me", player));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<PlayerState>(response), response.Headers.ETag!.ToString());
    }

    private async Task<HttpResponseMessage> WriteAsync(AuthSession player, string? line, string? etag = null)
    {
        etag ??= (await StateAsync(player)).ETag;
        return await Client.SendAsync(Send(HttpMethod.Put, "/v1/players/me/signature", player,
            new SignatureRequest(line), etag));
    }

    private async Task<AdminSignatureList> ListAsync(AuthSession admin, string? query = null)
    {
        string url = "/v1/admin/signatures?limit=200" + (query is null ? "" : "&query=" + Uri.EscapeDataString(query));
        HttpResponseMessage response = await Client.SendAsync(Send(HttpMethod.Get, url, admin));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<AdminSignatureList>(response);
    }

    private async Task<HttpResponseMessage> RemoveAsync(AuthSession admin, Guid playerId, SignatureRemoveRequest request) =>
        await Client.SendAsync(Send(HttpMethod.Post, $"/v1/admin/signatures/{playerId}/remove", admin, request));

    private async Task<HttpResponseMessage> UnlockAsync(AuthSession admin, Guid playerId) =>
        await Client.SendAsync(Send(HttpMethod.Post, $"/v1/admin/signatures/{playerId}/unlock", admin,
            new SignatureUnlockRequest("Appeal accepted")));

    private async Task<SignatureModerationHistory> HistoryAsync(AuthSession admin, Guid playerId) =>
        await ReadAsync<SignatureModerationHistory>(await Client.SendAsync(
            Send(HttpMethod.Get, $"/v1/admin/signatures/history?playerId={playerId}", admin)));

    private async Task<MailBox> BoxAsync(AuthSession player) =>
        await ReadAsync<MailBox>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/mail", player)));

    // ----------------------------------------------------------------- tests

    [Fact]
    public async Task Only_administrators_moderate()
    {
        (AuthSession player, _) = await PlayerAsync();

        HttpResponseMessage list = await Client.SendAsync(Send(HttpMethod.Get, "/v1/admin/signatures", player));
        HttpResponseMessage remove = await RemoveAsync(player, player.PlayerId,
            new SignatureRemoveRequest("No", 0, false, false));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
    }

    [Fact]
    public async Task The_newest_lines_come_first_and_a_name_with_its_tag_finds_one_player()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession first, string firstName) = await PlayerAsync();
        (AuthSession second, _) = await PlayerAsync();

        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(first, "first line")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(second, "second line")).StatusCode);

        // Relative order only: other tests write lines too.
        List<Guid> order = (await ListAsync(admin)).Players.Select(p => p.PlayerId)
            .Where(id => id == first.PlayerId || id == second.PlayerId).ToList();
        Assert.Equal([second.PlayerId, first.PlayerId], order);

        PlayerState state = (await StateAsync(first)).State;
        AdminSignature found = Assert.Single((await ListAsync(admin, firstName + PlayerTag.Format(state.Tag))).Players);
        Assert.Equal((first.PlayerId, "first line", 0), (found.PlayerId, found.Signature, found.Removals));
        Assert.NotNull(found.SignatureUpdatedAt);

        // Part of a name finds it too, written in any case.
        Assert.Contains((await ListAsync(admin, firstName[3..].ToUpperInvariant())).Players, p => p.PlayerId == first.PlayerId);
    }

    [Fact]
    public async Task Removing_takes_the_line_down_locks_it_mails_the_player_and_is_recorded()
    {
        (AuthSession admin, string adminName) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(player, "buy gold at example dot com")).StatusCode);
        (_, string staleETag) = await StateAsync(player);

        HttpResponseMessage removed = await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("  Advertising  ", 7, false, true));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        AdminSignature row = await ReadAsync<AdminSignature>(removed);
        Assert.Null(row.Signature);
        Assert.Equal(1, row.Removals);
        Assert.InRange(row.LockedUntil!.Value, DateTimeOffset.UtcNow.AddDays(6.9), DateTimeOffset.UtcNow.AddDays(7.1));

        // The save moved: a device holding the old copy is told so instead of writing over it.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await WriteAsync(player, "again", staleETag)).StatusCode);

        PlayerState state = (await StateAsync(player)).State;
        Assert.Null(state.Signature);
        Assert.Equal(row.LockedUntil, state.SignatureLockedUntil);

        HttpResponseMessage refused = await WriteAsync(player, "again");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ErrorCodes.SignatureLocked, await CodeAsync(refused));

        MailItem mail = Assert.Single((await BoxAsync(player)).Mails, m => m.Title == "About your profile signature");
        Assert.Contains("Reason: Advertising", mail.Body);
        Assert.Contains("You can write a new one after", mail.Body);

        SignatureModerationEntry entry = Assert.Single((await HistoryAsync(admin, player.PlayerId)).Entries);
        Assert.Equal(SignatureModerationAction.Removed, entry.Action);
        Assert.Equal(("buy gold at example dot com", "Advertising", true, adminName),
                     (entry.RemovedSignature, entry.Reason, entry.Mailed, entry.By));
        Assert.Equal(row.LockedUntil, entry.LockedUntil);
    }

    [Fact]
    public async Task A_permanent_lock_holds_until_a_moderator_lifts_it()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(player, "rude words")).StatusCode);

        AdminSignature locked = await ReadAsync<AdminSignature>(await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("Abuse", 0, true, false)));
        Assert.Equal(SignatureLock.Permanent, locked.LockedUntil);
        Assert.DoesNotContain((await BoxAsync(player)).Mails, m => m.Title == "About your profile signature");

        // A shorter lock on top never cuts the permanent one short.
        AdminSignature again = await ReadAsync<AdminSignature>(await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("Still abuse", 1, false, false)));
        Assert.Equal(SignatureLock.Permanent, again.LockedUntil);

        HttpResponseMessage unlocked = await UnlockAsync(admin, player.PlayerId);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.Null((await ReadAsync<AdminSignature>(unlocked)).LockedUntil);

        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(player, "sorry")).StatusCode);

        List<SignatureModerationEntry> entries = [.. (await HistoryAsync(admin, player.PlayerId)).Entries];
        Assert.Equal([SignatureModerationAction.Unlocked, SignatureModerationAction.Removed, SignatureModerationAction.Removed],
                     entries.Select(e => e.Action));
        Assert.Equal("Appeal accepted", entries[0].Reason);
        Assert.Null(entries[1].RemovedSignature);
        Assert.Equal("rude words", entries[2].RemovedSignature);
    }

    [Fact]
    public async Task A_lock_whose_day_has_passed_holds_nothing()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(player, "line")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("Test", 3, false, false))).StatusCode);

        await SqlAsync($"UPDATE players SET signature_locked_until = now() - interval '1 minute' WHERE account_id = {player.PlayerId}");

        Assert.Null((await StateAsync(player)).State.SignatureLockedUntil);
        Assert.Equal(HttpStatusCode.OK, (await WriteAsync(player, "a new line")).StatusCode);

        // Nothing left to lift.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UnlockAsync(admin, player.PlayerId)).StatusCode);
    }

    [Fact]
    public async Task A_removal_needs_a_reason_and_something_to_do()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest(" ", 1, false, false))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("Spam", 400, false, false))).StatusCode);

        // No line and no lock: nothing would change.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("Spam", 0, false, false))).StatusCode);

        // ...but a lock alone is something.
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, player.PlayerId,
            new SignatureRemoveRequest("Keeps writing spam", 1, false, false))).StatusCode);

        HttpResponseMessage nobody = await RemoveAsync(admin, Guid.NewGuid(), new SignatureRemoveRequest("Spam", 1, false, false));
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
        Assert.Equal(ErrorCodes.PlayerNotFound, await CodeAsync(nobody));
    }
}
