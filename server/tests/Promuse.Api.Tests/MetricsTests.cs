using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Promuse.Api.Infrastructure;
using Promuse.Contracts.Auth;
using Promuse.Contracts.Runs;

namespace Promuse.Api.Tests;

/// <summary>
/// 指标数对了 / The game's counters count what happened, with the tags the dashboards group by.
/// Read straight from the meter, the way the exporter would, so no collector is needed: the
/// test host's own <see cref="IMeterFactory"/> keeps its measurements apart from any other host.
/// </summary>
[Collection(ApiCollection.Name)]
public class MetricsTests(PromuseApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private static string Unique => Guid.NewGuid().ToString("N");

    private MetricCollector<long> Collect(string instrument) =>
        new(factory.Services.GetRequiredService<IMeterFactory>(), PromuseMetrics.MeterName, instrument);

    private static object? Tag(CollectedMeasurement<long> measurement, string name) =>
        measurement.Tags.TryGetValue(name, out object? value) ? value : null;

    private static HttpRequestMessage Send(HttpMethod method, string url, object? body, AuthSession? session = null,
                                           string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (session is not null) request.Headers.Authorization = new("Bearer", session.Tokens.AccessToken);
        if (method == HttpMethod.Post) request.Headers.Add("Idempotency-Key", key ?? Unique);
        return request;
    }

    private async Task<AuthSession> GuestAsync()
    {
        HttpResponseMessage response = await _client.SendAsync(
            Send(HttpMethod.Post, "/v1/auth/guest", new GuestSignInRequest(Unique)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSession>(Json))!;
    }

    [Fact]
    public async Task An_accepted_run_is_counted_as_started_and_accepted()
    {
        AuthSession player = await GuestAsync();
        using MetricCollector<long> started = Collect("promuse.runs.started");
        using MetricCollector<long> completed = Collect("promuse.runs.completed");
        using MetricCollector<long> findings = Collect("promuse.runs.review_findings");

        RunTicket ticket = (await (await _client.SendAsync(Send(HttpMethod.Post, "/v1/runs",
            new StartRunRequest(Played.Stage, Played.Operator), player))).Content.ReadFromJsonAsync<RunTicket>(Json))!;
        await Played.AgeAsync(factory, ticket.RunId, TimeSpan.FromMinutes(5));

        HttpResponseMessage done = await _client.SendAsync(Send(HttpMethod.Post, $"/v1/runs/{ticket.RunId}/complete",
            new CompleteRunRequest(true, Played.Win(factory)), player));
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);

        CollectedMeasurement<long> start = Assert.Single(started.GetMeasurementSnapshot());
        Assert.Equal((1L, "song"), (start.Value, Tag(start, "kind")));

        CollectedMeasurement<long> close = Assert.Single(completed.GetMeasurementSnapshot());
        Assert.Equal((1L, "accepted", true), (close.Value, Tag(close, "verdict"), Tag(close, "won")));

        // An accepted result raised nothing to count.
        Assert.Empty(findings.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task A_wrong_password_is_counted_as_a_failed_login_with_its_code()
    {
        using MetricCollector<long> signIns = Collect("promuse.auth.sign_ins");

        HttpResponseMessage response = await _client.PostAsJsonAsync("/v1/auth/login",
            new LoginRequest("nobody_" + Unique[..10], "not the password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        CollectedMeasurement<long> attempt = Assert.Single(signIns.GetMeasurementSnapshot());
        Assert.Equal(("login", "failure", "INVALID_CREDENTIALS"),
            (Tag(attempt, "method"), Tag(attempt, "outcome"), Tag(attempt, "error")));
    }

    [Fact]
    public async Task A_retried_request_is_counted_as_a_replay_of_its_route_not_as_a_second_sign_up()
    {
        using MetricCollector<long> signIns = Collect("promuse.auth.sign_ins");
        using MetricCollector<long> replays = Collect("promuse.idempotency.replays");

        var body = new RegisterRequest("metrics_" + Unique[..10], "correct horse battery");
        string key = Unique;

        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(Send(HttpMethod.Post, "/v1/auth/register", body, key: key))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(Send(HttpMethod.Post, "/v1/auth/register", body, key: key))).StatusCode);

        CollectedMeasurement<long> signUp = Assert.Single(signIns.GetMeasurementSnapshot());
        Assert.Equal(("register", "success"), (Tag(signUp, "method"), Tag(signUp, "outcome")));

        CollectedMeasurement<long> replay = Assert.Single(replays.GetMeasurementSnapshot());
        Assert.Equal(("/v1/auth/register", "replayed"), (Tag(replay, "route"), Tag(replay, "outcome")));
    }
}
