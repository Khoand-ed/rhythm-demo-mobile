using System.Diagnostics.Metrics;
using Promuse.Contracts;

namespace Promuse.Api.Infrastructure;

/// <summary>
/// 游戏在做什么 / What the game is doing, as counts a dashboard can draw: sign-ins, runs and how
/// their results were judged, headhunting, the shop, replayed requests and LiveOps refusals.
///
/// 请求指标不在这里 / Requests, latency and errors are not counted here: ASP.NET Core, the runtime
/// and Npgsql publish those themselves, and Program.cs sends them along with these. This is only
/// what the framework cannot know - what a request meant to the game.
///
/// 标签都是有限集 / Every tag takes a small, fixed set of values - a method, a verdict, an error
/// code, a banner, a route pattern - never a player id or a path with an id in it. Each distinct
/// tag value is a separate series in the database behind the dashboards.
///
/// One instance for the process, made from the host's <see cref="IMeterFactory"/>, so a test
/// host's measurements stay its own.
/// </summary>
public sealed class PromuseMetrics
{
    public const string MeterName = "Promuse.Api";

    private readonly Counter<long> _signIns;
    private readonly Counter<long> _runsStarted;
    private readonly Counter<long> _runsCompleted;
    private readonly Counter<long> _runFindings;
    private readonly Counter<long> _gachaPulls;
    private readonly Counter<long> _purchases;
    private readonly Counter<long> _spent;
    private readonly Counter<long> _replays;
    private readonly Counter<long> _liveOpsRefusals;

    public PromuseMetrics(IMeterFactory meters)
    {
        Meter meter = meters.Create(MeterName);

        _signIns = meter.CreateCounter<long>("promuse.auth.sign_ins", "{attempt}",
            "Sign-in attempts, by method (login, register, guest, link, refresh) and outcome.");
        _runsStarted = meter.CreateCounter<long>("promuse.runs.started", "{run}",
            "Runs opened, by kind: a song from the list, an event stage or a campaign stage.");
        _runsCompleted = meter.CreateCounter<long>("promuse.runs.completed", "{run}",
            "Runs closed, by how their result was judged and whether the player won.");
        _runFindings = meter.CreateCounter<long>("promuse.runs.review_findings", "{finding}",
            "Why results were rejected or flagged: one per reason a rejection gave, one per flag raised.");
        _gachaPulls = meter.CreateCounter<long>("promuse.gacha.pulls", "{pull}",
            "Operators drawn from headhunting, by banner and rarity. A ten-pull counts ten.");
        _purchases = meter.CreateCounter<long>("promuse.shop.purchases", "{purchase}",
            "Shop checkouts, by the item paid with.");
        _spent = meter.CreateCounter<long>("promuse.shop.spent", "{item}",
            "Currency spent in the shop, by the item paid with.");
        _replays = meter.CreateCounter<long>("promuse.idempotency.replays", "{request}",
            "Requests answered from a stored response because their Idempotency-Key was seen before, by route.");
        _liveOpsRefusals = meter.CreateCounter<long>("promuse.liveops.refusals", "{request}",
            "Requests refused before reaching the game: maintenance, or a build older than the minimum.");
    }

    // ----------------------------------------------------------------- sign-in

    /// <param name="method">login, register, guest, link or refresh.</param>
    /// <param name="problem">Null for a success; the refusal otherwise, whose code is tagged.</param>
    public void SignIn(string method, ApiProblem? problem) =>
        _signIns.Add(1,
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("outcome", problem is null ? "success" : "failure"),
            new KeyValuePair<string, object?>("error", problem?.Code ?? "none"));

    // -------------------------------------------------------------------- runs

    /// <param name="kind">song, event or campaign.</param>
    public void RunStarted(string kind) =>
        _runsStarted.Add(1, new KeyValuePair<string, object?>("kind", kind));

    /// <param name="verdict">accepted, flagged or rejected; none for a run closed without a result.</param>
    public void RunCompleted(string verdict, bool won) =>
        _runsCompleted.Add(1,
            new KeyValuePair<string, object?>("verdict", verdict),
            new KeyValuePair<string, object?>("won", won));

    /// <param name="kind">rejection or flag.</param>
    /// <param name="code">The review's own code, such as SCORE_ABOVE_CEILING or REPLAY_MISMATCH.</param>
    public void RunFinding(string kind, string code) =>
        _runFindings.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("code", code));

    // ------------------------------------------------------------- economy

    public void GachaPulled(string bannerId, int rarity) =>
        _gachaPulls.Add(1,
            new KeyValuePair<string, object?>("banner", bannerId),
            new KeyValuePair<string, object?>("rarity", rarity));

    public void Purchased(int priceItemId, int priceTotal)
    {
        var currency = new KeyValuePair<string, object?>("currency", priceItemId);
        _purchases.Add(1, currency);
        _spent.Add(priceTotal, currency);
    }

    // ---------------------------------------------------------- the plumbing

    /// <param name="route">The route pattern, such as /v1/runs/{runId:guid}/complete - never the path itself.</param>
    /// <param name="conflict">The key came back with a different request, and was refused.</param>
    public void Replayed(string route, bool conflict) =>
        _replays.Add(1,
            new KeyValuePair<string, object?>("route", route),
            new KeyValuePair<string, object?>("outcome", conflict ? "conflict" : "replayed"));

    /// <param name="reason">maintenance or client_outdated.</param>
    public void LiveOpsRefused(string reason) =>
        _liveOpsRefusals.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
