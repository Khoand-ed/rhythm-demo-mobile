#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Promuse.Scoring.Rules;

/// <summary>
/// A chart as the server sees it: the notes, and the handful of totals a result is checked
/// against.
///
/// 读 JSON 而不是资产 / Built from the beatmap JSON under Assets/Beatmaps, which BeatmapImporter
/// already treats as the source of truth - the .asset beside it is generated from the JSON, not
/// the other way round.
///
/// 和导入器一致 / The conversion repeats BeatmapImporter.ConvertNotes, deliberately note for
/// note: Tap and Twin carry `time`, Hold carries startTime/endTime and becomes hitTime plus a
/// duration, an unparseable note is skipped exactly where the importer skips it, and the result
/// is sorted by time. The importer is Editor code and cannot be linked here, so this is the one
/// place game logic is repeated rather than shared - which is why it is kept this small, and why
/// ScoringChartTests pins the note counts the device sees for each shipped chart.
/// </summary>
public sealed class ScoringChart
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private ScoringChart(string stageId, List<NoteData> notes, int skipped)
    {
        StageId = stageId;
        Notes = notes;
        SkippedNotes = skipped;

        foreach (NoteData note in notes)
        {
            bool isHold = note.type == NoteType.Hold && note.duration > 0f;

            // The same count GameManager.CountJudgements makes: a Twin is two halves judged
            // separately, a Hold is judged at its head and its tail.
            JudgementCount += note.type == NoteType.Twin || isHold ? 2 : 1;

            if (isHold) HoldCount++;

            LastJudgementTime = Math.Max(LastJudgementTime, note.hitTime + (isHold ? note.duration : 0f));
        }

        FirstNoteTime = notes.Count > 0 ? notes[0].hitTime : 0f;
    }

    public string StageId { get; }

    /// <summary>Sorted by hitTime. A stable sort, so notes on the same instant keep file order.</summary>
    public IReadOnlyList<NoteData> Notes { get; }

    /// <summary>Notes the importer would also have refused. Zero for every shipped chart.</summary>
    public int SkippedNotes { get; }

    /// <summary>Every judgement a full run makes. A won run's counts must add up to this.</summary>
    public int JudgementCount { get; }

    /// <summary>Holds with a body. Each has a tail judgement that needs no press.</summary>
    public int HoldCount { get; }

    /// <summary>Judgements that can only be earned by pressing: everything but hold tails.</summary>
    public int PressedJudgementCount => JudgementCount - HoldCount;

    public float FirstNoteTime { get; }

    /// <summary>When the last judgement falls due: a tap's time, or a hold's tail.</summary>
    public float LastJudgementTime { get; }

    /// <summary>
    /// The shortest stretch of song time a full run can take. Not the song's length: the intro
    /// before the first note can be skipped, and on stage_AIW that is 53 seconds.
    /// </summary>
    public float PlayableSpan => Math.Max(0f, LastJudgementTime - FirstNoteTime);

    public static ScoringChart Parse(string json)
    {
        BeatmapFile file = JsonSerializer.Deserialize<BeatmapFile>(json, Options)
            ?? throw new InvalidDataException("The beatmap is empty.");

        if (string.IsNullOrWhiteSpace(file.stageId))
        {
            throw new InvalidDataException("The beatmap has no stageId.");
        }

        var notes = new List<NoteData>();
        int skipped = 0;

        foreach (BeatmapNote source in file.notes ?? [])
        {
            if (TryConvert(source, out NoteData note)) notes.Add(note);
            else skipped++;
        }

        // Stable, unlike List.Sort: equal times keep their file order, so two loads of one
        // file always produce the same sequence.
        List<NoteData> sorted = notes.OrderBy(n => n.hitTime).ToList();

        return new ScoringChart(file.stageId.Trim(), sorted, skipped);
    }

    public static ScoringChart Load(string path) => Parse(File.ReadAllText(path));

    private static bool TryConvert(BeatmapNote source, out NoteData note)
    {
        note = default;

        NoteType type;
        switch ((source.type ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "tap": type = NoteType.Tap; break;
            case "hold": type = NoteType.Hold; break;
            case "twin": type = NoteType.Twin; break;
            default: return false;
        }

        // Twin occupies both lanes and ignores this field, as on the device.
        int lane = 0;
        if (type != NoteType.Twin)
        {
            switch ((source.lane ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "left": lane = 0; break;
                case "right": lane = 1; break;
                default: return false;
            }
        }

        float hitTime;
        float duration = 0f;

        if (type == NoteType.Hold)
        {
            if (source.endTime <= source.startTime) return false;

            hitTime = source.startTime;
            duration = source.endTime - source.startTime;
        }
        else
        {
            hitTime = source.time;

            // The importer's fallback for a Tap written with startTime instead of time.
            if (source.time == 0f && source.startTime != 0f) hitTime = source.startTime;
        }

        if (hitTime < 0f) return false;

        note = new NoteData { hitTime = hitTime, lane = lane, duration = duration, type = type };
        return true;
    }

    // The on-disk shape, mirroring BeatmapJson. Only what the conversion reads.
    private sealed class BeatmapFile
    {
        public string? stageId { get; set; }
        public List<BeatmapNote>? notes { get; set; }
    }

    private sealed class BeatmapNote
    {
        public string? type { get; set; }
        public string? lane { get; set; }
        public float time { get; set; }
        public float startTime { get; set; }
        public float endTime { get; set; }
    }
}
