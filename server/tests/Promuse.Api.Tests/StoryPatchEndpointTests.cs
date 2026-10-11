using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Promuse.Contracts;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Stories;
using Promuse.Persistence;

namespace Promuse.Api.Tests;

/// <summary>
/// Story corrections end to end: an administrator fixes a line, the game picks the fix up - and
/// only asks for the list again when it has changed.
///
/// 每个测试自己的剧情 / Each test corrects lines of its own story id, so what the others save never
/// shows in its assertions; the list's revision is shared, so only its movement is asserted.
/// </summary>
[Collection(ApiCollection.Name)]
public class StoryPatchEndpointTests(PromuseApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    private static string StoryId => "t_" + Unique[..12];

    // ---------------------------------------------------------------- helpers

    private async Task<AuthSession> GuestAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/guest")
        {
            Content = JsonContent.Create(new GuestSignInRequest(Unique)),
        };
        request.Headers.Add("Idempotency-Key", Unique);
        HttpResponseMessage response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSession>(Json))!;
    }

    private async Task<AuthSession> AdminAsync()
    {
        string username = "stadmin_" + Unique[..10];
        var register = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest(username, "correct horse battery")),
        };
        register.Headers.Add("Idempotency-Key", Unique);
        (await _client.SendAsync(register)).EnsureSuccessStatusCode();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PromuseDbContext>().Database
                .ExecuteSqlAsync($"UPDATE accounts SET is_admin = TRUE WHERE username = {username}");
        }

        HttpResponseMessage login = await _client.PostAsJsonAsync("/v1/auth/login", new LoginRequest(username, "correct horse battery"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthSession>(Json))!;
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, AuthSession session, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        return request;
    }

    private Task<HttpResponseMessage> SaveAsync(AuthSession admin, string story, string line, string original, string text) =>
        _client.SendAsync(Send(HttpMethod.Put, $"/v1/admin/stories/patches/{story}/{line}", admin,
            new StoryPatchRequest(original, text)));

    private async Task<(StoryPatchList List, string ETag)> ListAsync(AuthSession player)
    {
        HttpResponseMessage response = await _client.SendAsync(Send(HttpMethod.Get, "/v1/stories/patches", player));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<StoryPatchList>(Json))!, response.Headers.ETag!.ToString());
    }

    private async Task<HttpStatusCode> ListIfChangedAsync(AuthSession player, string etag)
    {
        HttpRequestMessage request = Send(HttpMethod.Get, "/v1/stories/patches", player);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return (await _client.SendAsync(request)).StatusCode;
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task A_saved_correction_reaches_the_game_and_taking_it_back_removes_it()
    {
        AuthSession admin = await AdminAsync();
        AuthSession player = await GuestAsync();
        string story = StoryId;

        (StoryPatchList before, string etag) = await ListAsync(player);
        Assert.DoesNotContain(before.Patches, p => p.StoryId == story);
        Assert.Equal(HttpStatusCode.NotModified, await ListIfChangedAsync(player, etag));

        HttpResponseMessage saved = await SaveAsync(admin, story, "a1f3", "Doctr, the signal is back.", "  Doctor, the signal is back.  ");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        AdminStoryPatch row = (await saved.Content.ReadFromJsonAsync<AdminStoryPatch>(Json))!;
        Assert.Equal("Doctor, the signal is back.", row.Text);
        Assert.StartsWith("stadmin_", row.UpdatedBy);

        // 版本变了就重拿 / The list moved on, so the game's copy is stale.
        Assert.Equal(HttpStatusCode.OK, await ListIfChangedAsync(player, etag));
        (StoryPatchList after, string etag2) = await ListAsync(player);
        StoryPatch patch = after.Patches.Single(p => p.StoryId == story);
        Assert.Equal(("a1f3", "Doctr, the signal is back.", "Doctor, the signal is back."), (patch.LineId, patch.Original, patch.Text));
        Assert.True(after.Revision > before.Revision);

        // Saving the same line again replaces the correction rather than adding a second one.
        (await SaveAsync(admin, story, "a1f3", "Doctr, the signal is back.", "Doctor - the signal is back.")).EnsureSuccessStatusCode();
        Assert.Equal("Doctor - the signal is back.", (await ListAsync(player)).List.Patches.Single(p => p.StoryId == story).Text);

        HttpResponseMessage reverted = await _client.SendAsync(Send(HttpMethod.Delete, $"/v1/admin/stories/patches/{story}/a1f3", admin));
        Assert.Equal(HttpStatusCode.NoContent, reverted.StatusCode);

        (StoryPatchList gone, _) = await ListAsync(player);
        Assert.DoesNotContain(gone.Patches, p => p.StoryId == story);
        Assert.True(gone.Revision > after.Revision);
        Assert.Equal(HttpStatusCode.OK, await ListIfChangedAsync(player, etag2));

        HttpResponseMessage again = await _client.SendAsync(Send(HttpMethod.Delete, $"/v1/admin/stories/patches/{story}/a1f3", admin));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(ErrorCodes.StoryPatchNotFound, (await again.Content.ReadFromJsonAsync<ApiProblem>(Json))!.Code);
    }

    [Theory]
    [InlineData("Bad Story", "a1f3", "old", "new", "storyId")]
    [InlineData("ok_story", "A1", "old", "new", "lineId")]
    [InlineData("ok_story", "a1f3", "", "new", "original")]
    [InlineData("ok_story", "a1f3", "same", "same", "text")]
    public async Task A_correction_that_cannot_apply_is_refused(string story, string line, string original, string text, string field)
    {
        AuthSession admin = await AdminAsync();

        HttpResponseMessage response = await SaveAsync(admin, Uri.EscapeDataString(story), line, original, text);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        ApiProblem problem = (await response.Content.ReadFromJsonAsync<ApiProblem>(Json))!;
        Assert.Equal(ErrorCodes.ValidationFailed, problem.Code);
        Assert.Contains(field, problem.Errors!.Keys);
    }

    [Fact]
    public async Task Only_admins_correct_stories()
    {
        AuthSession player = await GuestAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await SaveAsync(player, StoryId, "a1f3", "old", "new")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.SendAsync(Send(HttpMethod.Get, "/v1/admin/stories/patches", player))).StatusCode);
    }
}
