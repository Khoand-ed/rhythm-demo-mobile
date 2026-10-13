using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Api.Missions;
using Promuse.Api.Notices;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Mail;
using Promuse.Contracts.Notices;
using Promuse.Contracts.Players;
using Promuse.Contracts.SignIn;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>What the mail, notice and sign-in tests share: players, an administrator, raw SQL.</summary>
public abstract class HomeContentTestBase(PromuseApiFactory factory)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly PromuseApiFactory Factory = factory;
    protected readonly HttpClient Client = factory.CreateClient();

    protected static string Unique => Guid.NewGuid().ToString("N");

    protected async Task<(AuthSession Session, string Username)> PlayerAsync(bool admin = false)
    {
        string username = (admin ? "hcadmin_" : "hcplayer_") + Unique[..10];
        var register = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(username, "correct horse battery")),
        };
        register.Headers.Add("Idempotency-Key", Unique);
        (await Client.SendAsync(register)).EnsureSuccessStatusCode();

        if (admin) await SqlAsync($"UPDATE accounts SET is_admin = TRUE WHERE username = {username}");

        HttpResponseMessage login = await Client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(username, "correct horse battery"));
        login.EnsureSuccessStatusCode();
        return ((await login.Content.ReadFromJsonAsync<AuthSession>(Json))!, username);
    }

    protected async Task SqlAsync(FormattableString sql)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().Database.ExecuteSqlAsync(sql);
    }

    protected async Task<int> BalanceAsync(Guid accountId, int itemId)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().PlayerItems
            .Where(i => i.AccountId == accountId && i.ItemId == itemId).Select(i => i.Amount).FirstOrDefaultAsync();
    }

    protected static HttpRequestMessage Send(HttpMethod method, string url, AuthSession session, object? body = null,
                                             string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method == HttpMethod.Post) request.Headers.Add("Idempotency-Key", Unique);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    protected async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Json))!;

    protected async Task<string> CodeAsync(HttpResponseMessage response) => (await ReadAsync<ApiProblem>(response)).Code;
}

// ======================================================================================= mail

[Collection(ApiCollection.Name)]
public class MailEndpointTests(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    private const int Orundum = 1, Lmd = 2;

    private async Task<AdminMail> SendMailAsync(AuthSession admin, string? to, params ItemStack[] attachments)
    {
        HttpResponseMessage sent = await Client.SendAsync(Send(HttpMethod.Post, "/v1/admin/mail", admin,
            new MailSendRequest("Greetings", "Doctor,\nthank you.", "Closure", "AMIYA", attachments, 30, to)));
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        return await ReadAsync<AdminMail>(sent);
    }

    private async Task<MailBox> BoxAsync(AuthSession player) =>
        await ReadAsync<MailBox>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/mail", player)));

    [Fact]
    public async Task Opening_a_mail_collects_its_attachments_once()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, string name) = await PlayerAsync();
        int before = await BalanceAsync(player.PlayerId, Orundum);

        AdminMail mail = await SendMailAsync(admin, name, new ItemStack(Orundum, 300), new ItemStack(Lmd, 5000));

        MailItem waiting = (await BoxAsync(player)).Mails.Single(m => m.MailId == mail.MailId);
        Assert.False(waiting.IsOpened);
        Assert.Equal(("Closure", "AMIYA", 2), (waiting.Sender, waiting.SenderCharacterId, waiting.Attachments.Count));

        HttpResponseMessage opened = await Client.SendAsync(Send(HttpMethod.Post, $"/v1/mail/{mail.MailId}/open", player));
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        MailActionResult result = await ReadAsync<MailActionResult>(opened);
        Assert.Equal(2, result.Granted.Count);
        Assert.True(result.Box.Mails.Single(m => m.MailId == mail.MailId).IsOpened);
        Assert.Equal(before + 300, await BalanceAsync(player.PlayerId, Orundum));

        // 再开一次不给 / Opening it again gives nothing more.
        MailActionResult again = await ReadAsync<MailActionResult>(
            await Client.SendAsync(Send(HttpMethod.Post, $"/v1/mail/{mail.MailId}/open", player)));
        Assert.Empty(again.Granted);
        Assert.Equal(before + 300, await BalanceAsync(player.PlayerId, Orundum));
    }

    [Fact]
    public async Task A_mail_to_one_player_is_in_nobody_elses_box()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (_, string name) = await PlayerAsync();
        (AuthSession other, _) = await PlayerAsync();

        AdminMail mail = await SendMailAsync(admin, name);

        Assert.DoesNotContain((await BoxAsync(other)).Mails, m => m.MailId == mail.MailId);
        HttpResponseMessage stolen = await Client.SendAsync(Send(HttpMethod.Post, $"/v1/mail/{mail.MailId}/open", other));
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Assert.Equal(ErrorCodes.MailNotFound, await CodeAsync(stolen));
    }

    [Fact]
    public async Task A_mail_to_everyone_reaches_the_players_who_existed_then()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession early, _) = await PlayerAsync();

        AdminMail mail = await SendMailAsync(admin, null, new ItemStack(Orundum, 50));
        (AuthSession late, _) = await PlayerAsync();

        Assert.Contains((await BoxAsync(early)).Mails, m => m.MailId == mail.MailId);
        Assert.DoesNotContain((await BoxAsync(late)).Mails, m => m.MailId == mail.MailId);
    }

    [Fact]
    public async Task Collect_all_then_delete_read_empties_the_box_of_opened_mail()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, string name) = await PlayerAsync();
        int before = await BalanceAsync(player.PlayerId, Lmd);

        AdminMail first = await SendMailAsync(admin, name, new ItemStack(Lmd, 1000));
        AdminMail second = await SendMailAsync(admin, name, new ItemStack(Lmd, 2000));

        MailActionResult all = await ReadAsync<MailActionResult>(
            await Client.SendAsync(Send(HttpMethod.Post, "/v1/mail/collect-all", player)));
        Assert.Equal(3000, all.Granted.Where(g => g.ItemId == Lmd).Sum(g => g.Amount));
        Assert.Equal(before + 3000, await BalanceAsync(player.PlayerId, Lmd));

        MailActionResult cleared = await ReadAsync<MailActionResult>(
            await Client.SendAsync(Send(HttpMethod.Post, "/v1/mail/delete-opened", player)));
        Assert.DoesNotContain(cleared.Box.Mails, m => m.MailId == first.MailId || m.MailId == second.MailId);
    }

    [Fact]
    public async Task Expired_and_retracted_mail_leaves_the_box()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, string name) = await PlayerAsync();

        AdminMail old = await SendMailAsync(admin, name, new ItemStack(Orundum, 10));
        AdminMail recalled = await SendMailAsync(admin, name, new ItemStack(Orundum, 10));

        await SqlAsync($"UPDATE mails SET expires_at = now() - interval '1 minute' WHERE id = {old.MailId}");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client.SendAsync(Send(HttpMethod.Post, $"/v1/admin/mail/{recalled.MailId}/retract", admin))).StatusCode);

        MailBox box = await BoxAsync(player);
        Assert.DoesNotContain(box.Mails, m => m.MailId == old.MailId || m.MailId == recalled.MailId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.SendAsync(Send(HttpMethod.Post, $"/v1/mail/{old.MailId}/open", player))).StatusCode);
    }

    [Fact]
    public async Task Sending_checks_the_mail_and_needs_an_admin()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();

        HttpResponseMessage nobody = await Client.SendAsync(Send(HttpMethod.Post, "/v1/admin/mail", admin,
            new MailSendRequest("Hi", "Body", "Ops", null, [new ItemStack(Orundum, 0)], 0, "no_such_player_" + Unique[..6])));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nobody.StatusCode);
        ApiProblem problem = await ReadAsync<ApiProblem>(nobody);
        Assert.Contains("toUsername", problem.Errors!.Keys);
        Assert.Contains("expiresInDays", problem.Errors.Keys);
        Assert.Contains("attachments[0].amount", problem.Errors.Keys);

        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(Send(HttpMethod.Post, "/v1/admin/mail", player,
            new MailSendRequest("Hi", "Body", "Ops", null, [], 7, null)))).StatusCode);
    }
}

// ==================================================================================== notices

[Collection(ApiCollection.Name)]
public class NoticeEndpointTests(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    // The smallest PNG there is: one transparent pixel.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static NoticeDefinition Definition(string? banner = null, DateTimeOffset? starts = null, bool active = true) =>
        new(NoticeCategory.Event, "Rate Up", "Displayed Operators Rate Up", "<b>OPEN TIME:</b> now", banner,
            starts ?? DateTimeOffset.UtcNow.AddMinutes(-1), null, active, 5);

    private async Task<HttpResponseMessage> UploadAsync(AuthSession admin, byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/notices/banners") { Content = form };
        request.Headers.Authorization = new("Bearer", admin.Tokens.AccessToken);
        return await Client.SendAsync(request);
    }

    private async Task<AdminNotice> CreateAsync(AuthSession admin, NoticeDefinition d)
    {
        HttpResponseMessage created = await Client.SendAsync(Send(HttpMethod.Post, "/v1/admin/notices", admin, new NoticeUpsertRequest(d)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await ReadAsync<AdminNotice>(created);
    }

    private async Task<NoticeList> ListAsync(AuthSession player) =>
        await ReadAsync<NoticeList>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/notices", player)));

    [Fact]
    public async Task A_notice_with_a_banner_shows_and_can_be_marked_read()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();

        HttpResponseMessage uploaded = await UploadAsync(admin, Png, "banner.png");
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        BannerUpload banner = await ReadAsync<BannerUpload>(uploaded);
        Assert.EndsWith(".png", banner.Banner);

        // 匿名可取 / The picture is served without a token, bytes as uploaded.
        HttpResponseMessage image = await Factory.CreateClient().GetAsync(banner.Url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        AdminNotice notice = await CreateAsync(admin, Definition(banner.Banner));

        Notice shown = (await ListAsync(player)).Notices.Single(n => n.NoticeId == notice.NoticeId);
        Assert.Equal((NoticeCategory.Event, banner.Url, false), (shown.Category, shown.BannerUrl, shown.IsRead));

        HttpResponseMessage read = await Client.SendAsync(Send(HttpMethod.Post, $"/v1/notices/{notice.NoticeId}/read", player));
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.True((await ListAsync(player)).Notices.Single(n => n.NoticeId == notice.NoticeId).IsRead);
    }

    [Fact]
    public async Task Only_showing_notices_are_listed()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();

        AdminNotice later = await CreateAsync(admin, Definition(starts: DateTimeOffset.UtcNow.AddDays(2)));
        AdminNotice off = await CreateAsync(admin, Definition(active: false));

        NoticeList list = await ListAsync(player);
        Assert.DoesNotContain(list.Notices, n => n.NoticeId == later.NoticeId || n.NoticeId == off.NoticeId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.SendAsync(Send(HttpMethod.Post, $"/v1/notices/{later.NoticeId}/read", player))).StatusCode);
    }

    [Fact]
    public async Task Editing_needs_the_version_and_a_banner_that_was_uploaded()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        AdminNotice notice = await CreateAsync(admin, Definition());

        HttpResponseMessage stale = await Client.SendAsync(Send(HttpMethod.Put, $"/v1/admin/notices/{notice.NoticeId}", admin,
            new NoticeUpsertRequest(Definition() with { Title = "Edited" }), ifMatch: "W/\"99\""));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        HttpResponseMessage saved = await Client.SendAsync(Send(HttpMethod.Put, $"/v1/admin/notices/{notice.NoticeId}", admin,
            new NoticeUpsertRequest(Definition() with { Title = "Edited" }), ifMatch: $"W/\"{notice.Version}\""));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Edited", (await ReadAsync<AdminNotice>(saved)).Definition.Title);

        HttpResponseMessage missing = await Client.SendAsync(Send(HttpMethod.Post, "/v1/admin/notices", admin,
            new NoticeUpsertRequest(Definition("0123456789abcdef0123.png"))));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

        // 看内容不看名字 / Judged by the bytes: a text file called .png is refused.
        HttpResponseMessage fake = await UploadAsync(admin, "not an image"u8.ToArray(), "evil.png");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fake.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/v1/notices/banners/..%2F..%2Fappsettings.json")).StatusCode);
    }

    private static NoticeBlockDefinition Text(string text) => new(NoticeBlockType.Text, text, null);

    private static NoticeBlockDefinition Picture(string image) => new(NoticeBlockType.Image, null, image);

    [Fact]
    public async Task A_notice_in_blocks_shows_paragraphs_and_pictures_in_order()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        BannerUpload picture = await ReadAsync<BannerUpload>(await UploadAsync(admin, Png, "picture.png"));

        // 正文被忽略 / The body sent alongside blocks is ignored; the paragraphs are written into it.
        AdminNotice notice = await CreateAsync(admin, Definition() with
        {
            Body = "",
            Blocks = [Text("Rate up <b>now</b>."), Picture(picture.Banner), Text("Until Sunday.")],
        });

        NoticeBlockDefinition[] written = [Text("Rate up <b>now</b>."), Picture(picture.Banner), Text("Until Sunday.")];
        Assert.Equal(written, notice.Definition.Blocks!);

        Notice shown = (await ListAsync(player)).Notices.Single(n => n.NoticeId == notice.NoticeId);
        NoticeBlock[] seen =
        [
            new(NoticeBlockType.Text, "Rate up <b>now</b>.", null),
            new(NoticeBlockType.Image, null, picture.Url),
            new(NoticeBlockType.Text, "Until Sunday.", null),
        ];
        Assert.Equal(seen, shown.Blocks!);

        // 旧版本只看正文 / What a build from before blocks shows: the paragraphs, without the pictures.
        Assert.Equal("Rate up <b>now</b>.\n\nUntil Sunday.", shown.Body);
    }

    [Fact]
    public async Task A_notice_without_blocks_is_its_body_alone()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();

        AdminNotice notice = await CreateAsync(admin, Definition() with { Blocks = [] });

        Assert.Null(notice.Definition.Blocks);
        Notice shown = (await ListAsync(player)).Notices.Single(n => n.NoticeId == notice.NoticeId);
        Assert.Null(shown.Blocks);
        Assert.Equal("<b>OPEN TIME:</b> now", shown.Body);
    }

    [Fact]
    public async Task Blocks_are_checked_one_by_one()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        BannerUpload picture = await ReadAsync<BannerUpload>(await UploadAsync(admin, Png, "picture.png"));

        async Task<IReadOnlyDictionary<string, string[]>> RefusedAsync(params NoticeBlockDefinition[] blocks)
        {
            HttpResponseMessage response = await Client.SendAsync(Send(HttpMethod.Post, "/v1/admin/notices", admin,
                new NoticeUpsertRequest(Definition() with { Blocks = blocks })));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            return (await ReadAsync<ApiProblem>(response)).Errors!;
        }

        var errors = await RefusedAsync(Text("Fine."), Text("   "), Picture("0123456789abcdef0123.png"));
        Assert.Equal(new[] { "blocks[1].text", "blocks[2].image" }, errors.Keys.Order());

        Assert.Contains("blocks", (await RefusedAsync(Picture(picture.Banner))).Keys);
        Assert.Contains("blocks", (await RefusedAsync([Text("Many pictures."), .. Enumerable.Repeat(Picture(picture.Banner), NoticeService.MaxImages + 1)])).Keys);
        Assert.Contains("blocks", (await RefusedAsync(Text(new string('a', 4000)), Text(new string('b', 4000)), Text("c"))).Keys);
    }
}

// ==================================================================================== sign-in

[Collection(ApiCollection.Name)]
public class SignInEndpointTests(PromuseApiFactory factory) : HomeContentTestBase(factory)
{
    private async Task<SignInMonth> MonthAsync(AuthSession player) =>
        await ReadAsync<SignInMonth>(await Client.SendAsync(Send(HttpMethod.Get, "/v1/sign-in", player)));

    private Task<HttpResponseMessage> SignAsync(AuthSession player) =>
        Client.SendAsync(Send(HttpMethod.Post, "/v1/sign-in", player));

    [Fact]
    public async Task One_square_a_day_in_order()
    {
        (AuthSession player, _) = await PlayerAsync();

        SignInMonth fresh = await MonthAsync(player);
        Assert.True(fresh.CanSignToday);
        Assert.Equal(0, fresh.Signed);
        Assert.Equal(DateTime.DaysInMonth(fresh.Year, fresh.Month), fresh.Rewards.Count);

        int before = await BalanceAsync(player.PlayerId, fresh.Rewards[0].ItemId);
        HttpResponseMessage first = await SignAsync(player);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        SignInResult result = await ReadAsync<SignInResult>(first);
        Assert.Equal((1, fresh.Rewards[0]), (result.Day, result.Granted));
        Assert.False(result.Month.CanSignToday);
        Assert.Equal(before + fresh.Rewards[0].Amount, await BalanceAsync(player.PlayerId, fresh.Rewards[0].ItemId));

        HttpResponseMessage again = await SignAsync(player);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.NothingToClaim, await CodeAsync(again));

        // 第二天 / Tomorrow: move today's claim back a day and square 2 pays.
        await SqlAsync($"UPDATE sign_in_claims SET period_key = '2000-01-01' WHERE account_id = {player.PlayerId}");
        SignInResult second = await ReadAsync<SignInResult>(await SignAsync(player));
        Assert.Equal(2, second.Day);
        Assert.Equal(2, second.Month.Signed);
    }

    [Fact]
    public async Task A_months_own_table_overrides_the_default_until_removed()
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);
        (AuthSession player, _) = await PlayerAsync();
        SignInMonth month = await MonthAsync(player);

        List<ItemStack> table = [.. Enumerable.Range(1, month.Rewards.Count).Select(d => new ItemStack(2, d * 111))];
        HttpResponseMessage saved = await Client.SendAsync(Send(HttpMethod.Put, $"/v1/admin/sign-in/{month.MonthKey}", admin,
            new SignInTableRequest(table)));
        try
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(111, (await MonthAsync(player)).Rewards[0].Amount);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await Client.SendAsync(Send(HttpMethod.Delete, $"/v1/admin/sign-in/{month.MonthKey}", admin))).StatusCode);
        }

        Assert.Equal(month.Rewards[0], (await MonthAsync(player)).Rewards[0]);
    }

    [Theory]
    [InlineData("default", 30)]
    [InlineData("2026-02", 27)]
    [InlineData("2026-13", 31)]
    [InlineData("october", 31)]
    public async Task A_table_that_cannot_cover_its_month_is_refused(string key, int squares)
    {
        (AuthSession admin, _) = await PlayerAsync(admin: true);

        HttpResponseMessage response = await Client.SendAsync(Send(HttpMethod.Put, $"/v1/admin/sign-in/{key}", admin,
            new SignInTableRequest([.. Enumerable.Repeat(new ItemStack(1, 10), squares)])));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-10-31T20:59:00Z", "2026-10")]
    [InlineData("2026-10-31T21:00:00Z", "2026-11")]
    [InlineData("2026-12-31T21:00:00Z", "2027-01")]
    public void The_month_turns_over_at_the_daily_reset(string instant, string expected)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        MissionPeriod period = scope.ServiceProvider.GetRequiredService<MissionPeriod>();

        // UTC+7 with the reset at 04:00 local: 21:00 UTC is the start of the next server day.
        Assert.Equal(expected, period.MonthlyKey(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
