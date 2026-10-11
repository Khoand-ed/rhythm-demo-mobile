using System.Text.Json;
using System.Text.Json.Serialization;
using Promuse.Contracts.Events;

namespace Promuse.Persistence;

/// <summary>
/// 示例活动 / The event the database starts with, so the event screens have something to show
/// the moment the migration runs. It runs alongside the event_amiya headhunting banner, on the same
/// dates, and uses every module - an operator can edit or switch it off from /admin like any other.
/// </summary>
public static class SeedEvents
{
    public const string ResonantDawnId = "resonant_dawn";

    /// <summary>The Event Token item: the seeded event's currency.</summary>
    public const int EventTokenItemId = 20;

    /// <summary>
    /// The same JSON shape the API reads and writes: camelCase, enums by name. Fixed here rather
    /// than borrowed from the API, so the seed in the migration does not change if those options
    /// ever do - a migration that rewrites its own seed is a migration that fights the database.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private const int Orundum = 1, Lmd = 2, Orirock = 6, Certificate = 8, Permit = 10;

    public static EventDefinition ResonantDawn() => new(
        Title: "Resonant Dawn",
        Subtitle: "Festival event",
        Description: "AMIYA's headhunting banner is open, and Rhodes Island is celebrating. Check in every " +
                     "day, clear the festival charts for Event Tokens, and spend them in the festival shop.",
        StartsAt: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        EndsAt: new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero),
        IsActive: true,
        CurrencyItemId: EventTokenItemId,
        BannerId: "event_amiya",
        CheckIn:
        [
            new(1, new(Orundum, 100)),
            new(2, new(Lmd, 2000)),
            new(3, new(EventTokenItemId, 50)),
            new(4, new(Orirock, 20)),
            new(5, new(Orundum, 200)),
            new(6, new(EventTokenItemId, 100)),
            new(7, new(Permit, 1)),
        ],
        Missions:
        [
            new("clear3", "Clear any song 3 times", EventGoal.PlaySong, 3, 10, null, null),
            new("hard1", "Clear the festival chart AIW (Hard) once", EventGoal.ClearStage, 1, 20, "stage_AIW_hard", null),
            new("score100k", "Score 100,000 or more on any song", EventGoal.ReachScore, 1, 20, null, 100000),
            new("pull10", "Headhunt 10 times", EventGoal.Headhunt, 10, 10, null, null),
            new("shop1", "Buy anything from the Store once", EventGoal.BuyShopItem, 1, 5, null, null),
        ],
        Milestones:
        [
            new("m20", 20, new(EventTokenItemId, 100)),
            new("m40", 40, new(Orundum, 300)),
            new("m65", 65, new(Permit, 2)),
        ],
        Shop:
        [
            new("permit", new(Permit, 1), 150, 3),
            new("orundum", new(Orundum, 100), 40, 10),
            new("certificates", new(Certificate, 10), 30, 5),
            new("lmd", new(Lmd, 5000), 20, null),
        ],
        Stages:
        [
            new("stage_AIW", 30, "RD-1", "resonant_dawn_1_before", "resonant_dawn_1_after"),
            new("stage_AIW_hard", 60, "RD-2", "resonant_dawn_2_before", "resonant_dawn_2_after"),
        ]);
}
