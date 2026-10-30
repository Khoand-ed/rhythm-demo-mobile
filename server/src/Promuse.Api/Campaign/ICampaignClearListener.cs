using Promuse.Contracts.Players;

namespace Promuse.Api.Campaign;

/// <summary>
/// 首通的另一份奖励 / Something else that pays on a campaign stage's first clear - Manufacture's
/// blueprints today. The campaign tells it when a stage is cleared for the first time and shows what
/// it paid on the results screen, without knowing what it is.
/// </summary>
public interface ICampaignClearListener
{
    /// <summary>
    /// The stages it pays on, each with what it pays there. The campaign refuses to be saved without
    /// one of them: what the stage paid could then never be had.
    /// </summary>
    IReadOnlyDictionary<string, string> StagesPaidOn { get; }

    /// <summary>
    /// <paramref name="stageId"/> was just cleared for the first time. Runs inside the transaction that
    /// closes the run, so what it writes rolls back with it - and it counts nothing itself: a count
    /// belongs in <see cref="FirstClearPayout.Committed"/>, which runs only once that transaction has.
    /// </summary>
    Task<FirstClearPayout> OnFirstClearAsync(Guid accountId, string stageId, DateTimeOffset now, CancellationToken ct);
}

/// <param name="Items">What went into the bag, for the results screen.</param>
/// <param name="Committed">Run after the run's transaction commits - where a metric is counted.</param>
public sealed record FirstClearPayout(IReadOnlyList<ItemStack> Items, Action? Committed = null)
{
    public static readonly FirstClearPayout Nothing = new([]);
}

/// <summary>What a cleared campaign stage paid, and what to run once that is committed.</summary>
/// <param name="Orundum">The stage's own first-clear Orundum; 0 on a repeat clear.</param>
/// <param name="Drops">Rolled on every clear.</param>
/// <param name="FirstClearItems">What the listeners paid on a first clear.</param>
/// <param name="AfterCommit">The listeners' <see cref="FirstClearPayout.Committed"/>s.</param>
public sealed record CampaignPayout(
    int Orundum,
    List<ItemStack> Drops,
    List<ItemStack> FirstClearItems,
    List<Action> AfterCommit)
{
    public static CampaignPayout None => new(0, [], [], []);

    public bool PaidAnything => Orundum > 0 || Drops.Count > 0 || FirstClearItems.Count > 0;
}
