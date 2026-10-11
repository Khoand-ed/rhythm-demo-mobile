using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Promuse.Api.Infrastructure;
using Promuse.Api.Players;
using Promuse.Contracts;
using Promuse.Contracts.Notices;
using Promuse.Persistence;
using NoticeRow = Promuse.Persistence.Entities.Notice;
using NoticeView = Promuse.Contracts.Notices.Notice;

namespace Promuse.Api.Notices;

public sealed class NoticeOptions
{
    public const string SectionName = "Notices";

    /// <summary>
    /// Where uploaded banners are kept. Relative paths are under the content root; the default is
    /// App_Data/banners, which is gitignored - uploads are data, not source.
    /// </summary>
    public string BannerDirectory { get; set; } = Path.Combine("App_Data", "banners");

    public long MaxBannerBytes { get; set; } = 2 * 1024 * 1024;
}

/// <summary>
/// The announcement board: notices players read, and the admin side that writes them and their
/// banners.
/// </summary>
public sealed class NoticeService(PromuseDbContext db, TimeProvider clock, IOptions<NoticeOptions> options, IWebHostEnvironment env)
{
    public const string BannerRoute = "/v1/notices/banners/";

    // ------------------------------------------------------------------ player

    public async Task<NoticeList> ListAsync(Guid accountId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();

        var rows = await Showing(now)
            .OrderByDescending(n => n.SortOrder).ThenByDescending(n => n.StartsAt)
            .Select(n => new { Notice = n, Read = db.NoticeReads.Any(r => r.AccountId == accountId && r.NoticeId == n.Id) })
            .ToListAsync(ct);

        return new NoticeList([.. rows.Select(r => ToView(r.Notice, r.Read))], now);
    }

    /// <summary>Marks a showing notice read. Reading it again changes nothing.</summary>
    public async Task<Outcome<bool>> ReadAsync(Guid accountId, Guid noticeId, CancellationToken ct)
    {
        DateTimeOffset now = clock.GetUtcNow();
        if (!await Showing(now).AnyAsync(n => n.Id == noticeId, ct)) return Outcome<bool>.Fail(ApiProblems.NoticeNotFound());

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notice_reads (account_id, notice_id, read_at)
             VALUES ({accountId}, {noticeId}, {now})
             ON CONFLICT (account_id, notice_id) DO NOTHING
             """, ct);

        return Outcome<bool>.Ok(true);
    }

    private IQueryable<NoticeRow> Showing(DateTimeOffset now) =>
        db.Notices.AsNoTracking().Where(n => n.IsActive && n.StartsAt <= now && (n.EndsAt == null || n.EndsAt > now));

    private static NoticeView ToView(NoticeRow n, bool read) =>
        new(n.Id, Enum.Parse<NoticeCategory>(n.Category), n.ListTitle, n.Title, n.Body,
            n.Banner is null ? null : BannerRoute + n.Banner, n.StartsAt, n.EndsAt, n.SortOrder, read);

    // ------------------------------------------------------------------ admin

    public async Task<AdminNoticeList> AdminListAsync(CancellationToken ct)
    {
        var rows = await db.Notices.AsNoTracking()
            .OrderByDescending(n => n.StartsAt)
            .Select(n => new { Notice = n, By = db.Accounts.Where(a => a.Id == n.UpdatedBy).Select(a => a.Username).FirstOrDefault() })
            .ToListAsync(ct);

        return new AdminNoticeList([.. rows.Select(r => ToAdmin(r.Notice, r.By))]);
    }

    /// <summary>Creates a notice (no id) or saves a new version of one (id and If-Match).</summary>
    public async Task<Outcome<AdminNotice>> SaveAsync(
        Guid adminId, Guid? noticeId, string? ifMatch, NoticeDefinition? definition, CancellationToken ct)
    {
        Dictionary<string, string[]> errors = Validate(definition);
        if (errors.Count > 0) return Outcome<AdminNotice>.Fail(ApiProblems.ValidationFailed(errors));

        NoticeDefinition d = definition!;
        DateTimeOffset now = clock.GetUtcNow();
        NoticeRow? row;

        if (noticeId is null)
        {
            row = new NoticeRow { Id = Guid.NewGuid(), CreatedAt = now };
            db.Notices.Add(row);
        }
        else
        {
            row = await db.Notices.FirstOrDefaultAsync(n => n.Id == noticeId, ct);
            if (row is null) return Outcome<AdminNotice>.Fail(ApiProblems.NoticeNotFound());
            if (!PlayerService.TryParseETag(ifMatch, out int expected)) return Outcome<AdminNotice>.Fail(ApiProblems.PreconditionRequired());
            if (expected != row.Version) return Outcome<AdminNotice>.Fail(Conflict(row.Version));
        }

        row.Version += 1;
        row.Category = d.Category.ToString();
        row.ListTitle = d.ListTitle.Trim();
        row.Title = d.Title.Trim();
        row.Body = d.Body;
        row.Banner = string.IsNullOrWhiteSpace(d.Banner) ? null : d.Banner;
        row.StartsAt = d.StartsAt;
        row.EndsAt = d.EndsAt;
        row.IsActive = d.IsActive;
        row.SortOrder = d.SortOrder;
        row.UpdatedAt = now;
        row.UpdatedBy = adminId;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            int latest = await db.Notices.Where(n => n.Id == noticeId).Select(n => n.Version).FirstOrDefaultAsync(ct);
            return Outcome<AdminNotice>.Fail(Conflict(latest));
        }

        string? author = await db.Accounts.Where(a => a.Id == adminId).Select(a => a.Username).FirstOrDefaultAsync(ct);
        return Outcome<AdminNotice>.Ok(ToAdmin(row, author), created: noticeId is null);
    }

    private Dictionary<string, string[]> Validate(NoticeDefinition? d)
    {
        var errors = new Dictionary<string, string[]>();
        if (d is null)
        {
            errors["definition"] = ["Required."];
            return errors;
        }

        if (!Enum.IsDefined(d.Category)) errors["category"] = ["Event, System or News."];
        if (string.IsNullOrWhiteSpace(d.ListTitle) || d.ListTitle.Trim().Length > 40) errors["listTitle"] = ["1-40 characters."];
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Trim().Length > 80) errors["title"] = ["1-80 characters."];
        if (string.IsNullOrWhiteSpace(d.Body) || d.Body.Length > 8000) errors["body"] = ["1-8000 characters."];
        if (d.EndsAt is { } end && end <= d.StartsAt) errors["endsAt"] = ["Must be after startsAt, or empty."];
        if (!string.IsNullOrWhiteSpace(d.Banner) && (!IsBannerName(d.Banner) || !File.Exists(BannerPath(d.Banner))))
        {
            errors["banner"] = [$"No uploaded banner '{d.Banner}'."];
        }

        return errors;
    }

    private static ApiProblem Conflict(int version) =>
        ApiProblems.Create(412, ErrorCodes.StateConflict, "Notice has moved on",
            $"Someone saved this notice first; it is now at version {version}. Reload it and make the change again.");

    private static AdminNotice ToAdmin(NoticeRow n, string? by) =>
        new(n.Id, n.Version,
            new NoticeDefinition(Enum.Parse<NoticeCategory>(n.Category), n.ListTitle, n.Title, n.Body, n.Banner,
                n.StartsAt, n.EndsAt, n.IsActive, n.SortOrder),
            n.UpdatedAt, by);

    // ------------------------------------------------------------------ banners

    /// <summary>
    /// Stores an uploaded banner. PNG or JPEG only, judged by the bytes rather than the file name,
    /// and named by its content hash - the same image uploaded twice is one file, and a name can
    /// never point at anything but what it was.
    /// </summary>
    public async Task<Outcome<BannerUpload>> UploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Fail("Choose an image.");
        if (file.Length > options.Value.MaxBannerBytes) return Fail($"At most {options.Value.MaxBannerBytes / 1024} KB.");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        byte[] bytes = buffer.ToArray();

        string? extension = Sniff(bytes);
        if (extension is null) return Fail("PNG or JPEG only.");

        string name = Convert.ToHexStringLower(SHA256.HashData(bytes))[..20] + extension;
        Directory.CreateDirectory(BannerDirectory);
        string path = BannerPath(name);
        if (!File.Exists(path)) await File.WriteAllBytesAsync(path, bytes, ct);

        return Outcome<BannerUpload>.Ok(new BannerUpload(name, BannerRoute + name), created: true);

        static Outcome<BannerUpload> Fail(string message) =>
            Outcome<BannerUpload>.Fail(ApiProblems.ValidationFailed(new Dictionary<string, string[]> { ["file"] = [message] }));
    }

    /// <summary>The stored file and its type, or null for a name that is not one of ours.</summary>
    public (string Path, string ContentType)? Banner(string name)
    {
        if (!IsBannerName(name)) return null;
        string path = BannerPath(name);
        if (!File.Exists(path)) return null;
        return (path, name.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
    }

    /// <summary>Only names this service writes: hex, then .png or .jpg. Nothing that could walk the disk.</summary>
    public static bool IsBannerName(string? name) =>
        name is { Length: > 4 and <= 64 }
        && (name.EndsWith(".png", StringComparison.Ordinal) || name.EndsWith(".jpg", StringComparison.Ordinal))
        && name[..^4].All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private string BannerDirectory => Path.IsPathRooted(options.Value.BannerDirectory)
        ? options.Value.BannerDirectory
        : Path.Combine(env.ContentRootPath, options.Value.BannerDirectory);

    private string BannerPath(string name) => Path.Combine(BannerDirectory, name);

    private static string? Sniff(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        return null;
    }
}
