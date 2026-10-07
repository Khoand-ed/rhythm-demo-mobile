using Promuse.Scoring.Rules;

namespace Promuse.Api.Runs;

public sealed class GameDataOptions
{
    public const string SectionName = "GameData";

    /// <summary>
    /// Where ruleset.json and charts/ live. Empty means the copy the build places beside the
    /// binaries - see the Content items in Promuse.Api.csproj.
    /// </summary>
    public string? Root { get; set; }
}

/// <summary>
/// The rules and charts a result is checked against, loaded once.
///
/// 启动就读完 / Read at startup and never again, and a file that fails to load stops the server
/// from starting. A validator that discovers its rules are unreadable on the first submitted run
/// would have to choose between rejecting an honest player and waving through anything - and
/// neither is a decision to make at request time.
///
/// 同一份文件 / The files are the repository's own: server/data/ruleset.json and the beatmap JSON
/// under Assets/Beatmaps, copied beside the binaries by the build. Nothing here is a second copy
/// someone has to keep in step.
/// </summary>
public sealed class GameData
{
    private readonly Dictionary<string, ScoringChart> charts;

    private GameData(Ruleset rules, Dictionary<string, ScoringChart> charts, string root)
    {
        Rules = rules;
        this.charts = charts;
        Root = root;
    }

    public Ruleset Rules { get; }

    public string Root { get; }

    public IReadOnlyCollection<string> StageIds => charts.Keys;

    public ScoringChart? ChartFor(string stageId) =>
        charts.TryGetValue(stageId, out ScoringChart? chart) ? chart : null;

    public static GameData Load(string root, ILogger logger)
    {
        string rulesPath = Path.Combine(root, "ruleset.json");
        Ruleset rules = RulesetLoader.Load(rulesPath);

        var charts = new Dictionary<string, ScoringChart>(StringComparer.Ordinal);
        string chartDir = Path.Combine(root, "charts");

        if (Directory.Exists(chartDir))
        {
            foreach (string file in Directory.EnumerateFiles(chartDir, "*.json", SearchOption.AllDirectories)
                                             .OrderBy(f => f, StringComparer.Ordinal))
            {
                ScoringChart chart = ScoringChart.Load(file);

                if (!charts.TryAdd(chart.StageId, chart))
                {
                    throw new InvalidDataException(
                        $"Two charts claim stage '{chart.StageId}'; the second is {file}.");
                }

                if (chart.SkippedNotes > 0)
                {
                    logger.LogWarning(
                        "Chart {StageId} has {Skipped} note(s) the importer also refuses; they are left out on both sides.",
                        chart.StageId, chart.SkippedNotes);
                }
            }
        }

        logger.LogInformation(
            "Game data loaded from {Root}: ruleset v{Version} with {Operators} operator(s), {Charts} chart(s) [{Stages}].",
            root, rules.version, rules.operators.Count, charts.Count, string.Join(", ", charts.Keys.Order()));

        return new GameData(rules, charts, root);
    }
}
