using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Everything the generator needs, so the window is just a form over this.
public class ChartGeneratorSettings
{
    public AudioClip clip;
    public string outputFolder = "Assets/Beatmaps/song_001";
    public string fileName = "generated";

    public string stageId = "stage_001";
    public string songId = "song_001";
    public string songName = "";
    public string author = "";

    [Range(1, 10)]
    public int difficulty = 3;

    // 0 means "detect it"; anything else overrides the estimate.
    public float bpmOverride = 0f;

    // Higher rejects more onsets. 1.4-1.6 suits most music.
    public float sensitivity = 1.5f;

    public int windowSize = 1024;
    public int hopSize = 512;

    // How many notes may share a lane before alternation is forced.
    public int maxSameLaneRun = 3;

    public int seed = 12345;

    [Header("Note types")]
    public bool enableHolds = true;

    // 0..1. Higher accepts sounds that fade more before they stop counting
    // as held, so more onsets become holds.
    public float holdAmount = 0.5f;

    // Shortest sustain, in beats, worth turning into a hold.
    public float minHoldBeats = 1f;

    public bool enableTwins = true;

    // Scales the difficulty's twin share. 1 is the default density.
    public float twinAmount = 1f;

    [Header("Charting style")]

    // 0 spreads notes evenly over the song; 1 makes loud sections dense and
    // quiet ones sparse, the way hand-made charts follow the music's energy.
    public float energyFollow = 0.7f;

    // A bar with the same rhythm as an earlier bar gets the same lane pattern.
    public bool repeatPatterns = true;

    // Every other repeat of a pattern is mirrored (call and response).
    public bool mirrorRepeats = true;

    // Fast runs alternate hands instead of following the instrument's lane.
    public bool handFlow = true;

    // A quiet opening is left unmapped, the way osu! and most rhythm games
    // chart it, rather than dotted with a lonely note every few seconds.
    // The game offers a Skip button over it.
    public bool leaveQuietIntro = true;
}

public class ChartGeneratorResult
{
    public bool ok;
    public string message;
    public string jsonPath;

    public TempoEstimate tempo;
    public int downbeatOffset;
    public int onsetsFound;
    public int notesKept;
    public float notesPerSecond;
    public float impliedNoteSpeed;
    public int leftLaneCount;
    public int rightLaneCount;

    public int tapCount;
    public int holdCount;
    public int twinCount;

    public int barCount;
    public int reusedPatternBars;
    public int restsInserted;

    // Seconds of quiet opening left without notes; 0 when the song starts
    // straight away.
    public float introSeconds;
}

// Turns an audio clip into a beatmap.json with Tap, Hold and Twin notes,
// charted the way rhythm games are charted by hand:
//
//  1. Find the beat grid and the bar line, then rank every attack by how much
//     it matters musically - where it falls in the bar, and how loud it is
//     against its own neighbourhood rather than against the whole song.
//  2. Give each bar a note budget that follows the song's energy, so verses
//     breathe and choruses hit hard, instead of the loudest section taking
//     every note.
//  3. Pick note types: Twins on phrase-opening accents, Holds on sounds that
//     keep ringing.
//  4. Leave breathing room: long unbroken runs get a rest.
//  5. Assign lanes for two thumbs: fast runs alternate hands, repeated rhythms
//     reuse (or mirror) the pattern they had before, and the instrument's
//     register decides the rest - kicks left, snares and hats right.
//
// The importer then converts the json into a SongChart, so generated and
// hand-written charts travel exactly the same path.
public static class ChartGenerator
{
    // Grid subdivisions per beat, and density targets, indexed by difficulty.
    // Index 0 is unused so the tables read as difficulty 1..10.
    private static readonly int[] SubdivisionByDifficulty = { 0, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4 };
    private static readonly float[] NotesPerSecondByDifficulty = { 0f, 0.8f, 1.1f, 1.5f, 1.9f, 2.3f, 2.8f, 3.3f, 3.9f, 4.5f, 5.2f };
    private static readonly float[] MinSameLaneGapByDifficulty = { 0f, 0.6f, 0.6f, 0.5f, 0.5f, 0.4f, 0.4f, 0.28f, 0.28f, 0.2f, 0.2f };

    // Share of notes that become Twins. None on the easiest charts: pressing
    // both sides at once is its own skill.
    private static readonly float[] TwinShareByDifficulty = { 0f, 0f, 0f, 0.02f, 0.03f, 0.04f, 0.05f, 0.06f, 0.07f, 0.075f, 0.08f };

    // Longest hold, in beats. Easy charts keep holds short, because nothing
    // else is placed while one is held.
    private static readonly float[] MaxHoldBeatsByDifficulty = { 0f, 4f, 4f, 4f, 6f, 6f, 6f, 8f, 8f, 8f, 8f };

    // Longest unbroken run (notes no more than half a beat apart) before a
    // rest is forced. Stamina is part of difficulty.
    private static readonly int[] MaxRunByDifficulty = { 0, 4, 4, 6, 8, 12, 16, 24, 32, 48, 64 };

    // At or below this difficulty nothing is placed while a hold is held, so
    // an easy chart never asks for a hold and a tap at the same time.
    private const int NoOverlapMaxDifficulty = 3;

    // Mirroring repeats is a variety device for players who can read it.
    private const int MirrorMinDifficulty = 5;

    // Holds end on this fraction of a beat.
    private const float HoldStepBeats = 0.5f;

    // Bars quieter than this share of the song's loud level get no notes.
    private const float SilentBarEnergy = 0.12f;

    // The music has "come in" at the first run of IntroConfirmBars bars at
    // least this loud; everything before is the intro.
    private const float IntroEnergy = 0.4f;
    private const int IntroConfirmBars = 2;

    // Seconds either side of an onset used to judge how loud it is locally.
    private const float LocalWindowSeconds = 4f;

    // A bar may take at most this much more than its fair budget when
    // earlier bars could not use theirs.
    private const float MaxBudgetCarry = 1.6f;

    private class Candidate
    {
        public Onset onset;
        public int bar;
        public int slot;
        public float salience;
        public NoteType type = NoteType.Tap;
        public float endTime;
        public int lane = -1;

        public float Time
        {
            get { return onset.time; }
        }
    }

    public static ChartGeneratorResult Generate(ChartGeneratorSettings settings)
    {
        ChartGeneratorResult result = new ChartGeneratorResult();

        if (settings.clip == null)
        {
            result.message = "No audio clip selected.";
            return result;
        }

        float[] mono;
        string loadError;
        if (!TryLoadMono(settings.clip, out mono, out loadError))
        {
            result.message = loadError;
            return result;
        }

        int sampleRate = settings.clip.frequency;
        float duration = mono.Length / (float)sampleRate;

        OnsetDetector detector = new OnsetDetector(settings.windowSize, settings.hopSize);
        SpectralFrames frames;

        try
        {
            frames = detector.Analyse(mono, sampleRate, progress =>
                !EditorUtility.DisplayCancelableProgressBar(
                    "Generating chart", $"Analysing {settings.clip.name}...", progress * 0.8f));
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (frames == null)
        {
            result.message = "Cancelled.";
            return result;
        }

        List<Onset> onsets = detector.PickPeaks(frames.flux, frames.lowRatio, frames.frameRate, settings.sensitivity, 0.05f);
        result.onsetsFound = onsets.Count;

        if (onsets.Count == 0)
        {
            result.message = "No onsets detected. Try lowering the sensitivity.";
            return result;
        }

        TempoEstimate tempo = new TempoEstimator().Estimate(frames.flux, frames.frameRate);

        if (settings.bpmOverride > 0f)
        {
            // Keep the detected phase: the override is about tempo, and the
            // phase estimate is independent and usually the reliable half.
            tempo.bpm = settings.bpmOverride;
        }

        result.tempo = tempo;

        int downbeat = BeatGrid.DetectDownbeat(onsets, tempo.bpm, tempo.phase);
        BeatGrid grid = new BeatGrid(tempo.bpm, tempo.phase, downbeat);
        result.downbeatOffset = downbeat;

        int difficulty = Mathf.Clamp(settings.difficulty, 1, 10);
        float gridStep = grid.beatPeriod / SubdivisionByDifficulty[difficulty];
        float minSameLaneGap = MinSameLaneGapByDifficulty[difficulty];

        List<Onset> quantised = Quantise(onsets, tempo.phase, gridStep);
        List<Candidate> candidates = Score(quantised, grid);

        float[] barEnergy = BarEnergy(frames, grid, duration);
        result.barCount = barEnergy.Length;

        int firstCharted = settings.leaveQuietIntro ? QuietIntroLength(barEnergy) : 0;
        result.introSeconds = firstCharted > 0 ? Mathf.Max(0f, grid.BarStart(firstCharted - 1)) : 0f;

        List<Candidate> kept = SelectByBar(candidates, barEnergy, NotesPerSecondByDifficulty[difficulty] * duration,
                                           settings.energyFollow, firstCharted);

        if (settings.enableTwins) MarkTwins(kept, grid, barEnergy, difficulty, settings.twinAmount);
        if (settings.enableHolds) MarkHolds(kept, frames, grid, difficulty, settings);

        result.restsInserted = AddBreathingRoom(kept, grid, MaxRunByDifficulty[difficulty]);

        List<Candidate> placed = AssignLanes(kept, grid, settings, difficulty, minSameLaneGap, result);
        List<BeatmapNoteJson> notes = ToJson(placed, result);

        result.notesKept = notes.Count;
        result.notesPerSecond = duration > 0f ? notes.Count / duration : 0f;
        result.impliedNoteSpeed = ChartSpacing.ImpliedSpeed(notes, tempo.bpm);

        BeatmapJson header = new BeatmapJson
        {
            stageId = settings.stageId,
            songId = settings.songId,
            name = settings.songName,
            author = settings.author,
            bpm = tempo.bpm,
            difficulty = difficulty,
            beatPhase = grid.FirstDownbeat,

            // The detected beat phase lives in the note times, not here:
            // offset is an audio-sync correction, which generation knows
            // nothing about.
            offset = 0f,
        };

        string path = $"{settings.outputFolder}/{settings.fileName}.json";

        System.IO.Directory.CreateDirectory(settings.outputFolder);
        System.IO.File.WriteAllText(path, BeatmapJsonWriter.Write(header, notes));
        AssetDatabase.ImportAsset(path);

        result.ok = true;
        result.jsonPath = path;
        result.message = $"Wrote {notes.Count} notes to {path} " +
                         $"({result.tapCount} Tap, {result.holdCount} Hold, {result.twinCount} Twin)" +
                         (result.introSeconds > 0f ? $", quiet intro of {result.introSeconds:0.#}s left empty." : ".");
        return result;
    }

    // GetData fails on Streaming clips, which is exactly what BeatmapImporter
    // sets demo clips to - so this fixes the setting rather than failing with
    // an opaque error, and only ever on the clip the user picked.
    private static bool TryLoadMono(AudioClip clip, out float[] mono, out string error)
    {
        mono = null;
        error = null;

        string path = AssetDatabase.GetAssetPath(clip);
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;

        if (importer != null)
        {
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;

            if (sample.loadType != AudioClipLoadType.DecompressOnLoad || !sample.preloadAudioData)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant() == "demo")
                {
                    error = $"{path} is a demo clip, which the beatmap importer keeps streaming for the " +
                            "select screen. Point the generator at the song's main clip instead.";
                    return false;
                }

                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                sample.preloadAudioData = true;
                importer.defaultSampleSettings = sample;
                importer.SaveAndReimport();

                Debug.Log($"{path}: set to Decompress On Load so its samples can be read.");
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }

        int channels = Mathf.Max(1, clip.channels);
        float[] interleaved = new float[clip.samples * channels];

        if (!clip.GetData(interleaved, 0))
        {
            error = $"Could not read samples from {clip.name}.";
            return false;
        }

        mono = new float[clip.samples];

        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }

        return true;
    }

    // Snaps onsets to the grid and collapses each slot to its strongest hit.
    // Anything landing more than half a step away was never on the grid.
    private static List<Onset> Quantise(List<Onset> onsets, float phase, float gridStep)
    {
        Dictionary<int, Onset> bySlot = new Dictionary<int, Onset>();

        for (int i = 0; i < onsets.Count; i++)
        {
            Onset onset = onsets[i];

            int slot = Mathf.RoundToInt((onset.time - phase) / gridStep);
            float snapped = phase + slot * gridStep;

            if (snapped < 0f) continue;
            if (Mathf.Abs(snapped - onset.time) > gridStep * 0.5f) continue;

            onset.time = snapped;

            Onset existing;
            if (bySlot.TryGetValue(slot, out existing) && existing.strength >= onset.strength) continue;

            bySlot[slot] = onset;
        }

        List<Onset> result = new List<Onset>(bySlot.Values);
        result.Sort((a, b) => a.time.CompareTo(b.time));
        return result;
    }

    // Salience = how loud the attack is against its own neighbourhood, times
    // how much its position in the bar matters. Local rather than global
    // loudness is what keeps a quiet verse from losing every note to the
    // chorus; the metric weight is what makes easy charts land on the beat.
    private static List<Candidate> Score(List<Onset> onsets, BeatGrid grid)
    {
        List<Candidate> candidates = new List<Candidate>(onsets.Count);
        List<float> window = new List<float>();
        int from = 0;
        int to = 0;

        for (int i = 0; i < onsets.Count; i++)
        {
            float t = onsets[i].time;

            while (from < onsets.Count && onsets[from].time < t - LocalWindowSeconds) from++;
            while (to < onsets.Count && onsets[to].time <= t + LocalWindowSeconds) to++;

            window.Clear();
            for (int j = from; j < to; j++) window.Add(onsets[j].strength);
            window.Sort();
            float localMedian = Mathf.Max(0.0001f, window[window.Count / 2]);

            float loudness = Mathf.Clamp(onsets[i].strength / localMedian, 0f, 4f);

            candidates.Add(new Candidate
            {
                onset = onsets[i],
                bar = grid.BarIndex(t),
                slot = grid.SlotInBar(t),
                salience = loudness * grid.MetricWeight(t),
            });
        }

        return candidates;
    }

    // How busy each bar sounds, 0..~1.2, from its level and its attack rate.
    // Index is bar + 1, so the pickup before the first bar line has a slot.
    private static float[] BarEnergy(SpectralFrames frames, BeatGrid grid, float duration)
    {
        int bars = Mathf.Max(1, grid.BarIndex(duration) + 2);
        float[] level = new float[bars];
        float[] activity = new float[bars];
        int[] count = new int[bars];

        for (int frame = 0; frame < frames.Count; frame++)
        {
            float t = frame / frames.frameRate;
            int index = Mathf.Clamp(grid.BarIndex(t) + 1, 0, bars - 1);

            level[index] += frames.level[frame];
            activity[index] += frames.flux[frame];
            count[index]++;
        }

        for (int i = 0; i < bars; i++)
        {
            if (count[i] == 0) continue;
            level[i] /= count[i];
            activity[i] /= count[i];
        }

        float levelRef = Mathf.Max(0.0001f, Percentile(level, 0.9f));
        float activityRef = Mathf.Max(0.0001f, Percentile(activity, 0.9f));

        float[] energy = new float[bars];
        for (int i = 0; i < bars; i++)
        {
            energy[i] = 0.6f * (level[i] / levelRef) + 0.4f * (activity[i] / activityRef);
        }

        return energy;
    }

    // How many bar slots (BarEnergy indices) of quiet opening come before the
    // music comes in. 0 when it is loud from the start, or when no part of
    // the first half ever gets loud - then there is no "intro" to speak of.
    private static int QuietIntroLength(float[] barEnergy)
    {
        for (int i = 0; i + IntroConfirmBars <= barEnergy.Length / 2; i++)
        {
            bool loud = true;
            for (int k = 0; k < IntroConfirmBars; k++)
            {
                if (barEnergy[i + k] < IntroEnergy) { loud = false; break; }
            }

            if (loud) return i;
        }

        return 0;
    }

    // Splits the song's note budget across bars by energy, then fills each
    // bar with its most salient candidates. Budget a bar cannot use (too few
    // attacks) carries forward, within limits, so the total stays on target.
    // Bars before firstCharted (a quiet intro) get none, and their share goes
    // to the rest of the song.
    private static List<Candidate> SelectByBar(List<Candidate> candidates, float[] barEnergy, float totalNotes,
                                               float energyFollow, int firstCharted)
    {
        float exponent = Mathf.Lerp(0f, 2f, Mathf.Clamp01(energyFollow));

        float[] weight = new float[barEnergy.Length];
        float weightSum = 0f;

        for (int i = 0; i < barEnergy.Length; i++)
        {
            bool unmapped = i < firstCharted || barEnergy[i] < SilentBarEnergy;
            weight[i] = unmapped ? 0f : Mathf.Pow(barEnergy[i], exponent);
            weightSum += weight[i];
        }

        Dictionary<int, List<Candidate>> byBar = new Dictionary<int, List<Candidate>>();
        foreach (Candidate candidate in candidates)
        {
            List<Candidate> list;
            if (!byBar.TryGetValue(candidate.bar, out list))
            {
                list = new List<Candidate>();
                byBar[candidate.bar] = list;
            }

            list.Add(candidate);
        }

        List<Candidate> kept = new List<Candidate>();
        float carry = 0f;

        for (int index = 0; index < barEnergy.Length; index++)
        {
            float fair = weightSum > 0f ? totalNotes * weight[index] / weightSum : 0f;
            float budget = Mathf.Min(fair + carry, fair * MaxBudgetCarry + 1f);

            List<Candidate> inBar;
            int take = 0;

            if (fair > 0f && byBar.TryGetValue(index - 1, out inBar))
            {
                take = Mathf.Min(inBar.Count, Mathf.FloorToInt(budget + 0.5f));

                inBar.Sort((a, b) => b.salience.CompareTo(a.salience));
                for (int i = 0; i < take; i++) kept.Add(inBar[i]);
            }

            carry = Mathf.Max(0f, fair + carry - take);
        }

        kept.Sort((a, b) => a.Time.CompareTo(b.Time));
        return kept;
    }

    // Twins go on the accents a charter would mark: the strongest hits on
    // beat 1 or 3, with weight at both ends of the spectrum (a kick with a
    // crash, not a lone hi-hat), favouring the first beat of a four-bar
    // phrase and bars where the energy jumps - a drop or a chorus coming in.
    private static void MarkTwins(List<Candidate> kept, BeatGrid grid, float[] barEnergy, int difficulty, float amount)
    {
        int target = Mathf.RoundToInt(TwinShareByDifficulty[difficulty] * Mathf.Max(0f, amount) * kept.Count);
        if (target <= 0 || kept.Count == 0) return;

        List<float> saliences = new List<float>(kept.Count);
        foreach (Candidate c in kept) saliences.Add(c.salience);
        saliences.Sort();
        float minSalience = saliences[Mathf.Clamp(Mathf.FloorToInt(saliences.Count * 0.7f), 0, saliences.Count - 1)];

        List<Candidate> candidates = new List<Candidate>();
        Dictionary<Candidate, float> priority = new Dictionary<Candidate, float>();

        foreach (Candidate c in kept)
        {
            bool strongBeat = c.slot == 0 || c.slot == 8;
            bool broadband = c.onset.lowRatio > 0.2f && c.onset.lowRatio < 0.85f;
            if (!strongBeat || !broadband || c.salience < minSalience) continue;

            float score = c.salience;
            if (c.slot == 0) score *= 1.3f;
            if (c.slot == 0 && ((c.bar % 4) + 4) % 4 == 0) score *= 1.6f;
            if (EnergyJump(barEnergy, c.bar)) score *= 1.5f;

            candidates.Add(c);
            priority[c] = score;
        }

        candidates.Sort((a, b) => priority[b].CompareTo(priority[a]));

        // Twins are landmarks; crowded together they stop reading as accents.
        float minGap = (difficulty <= 5 ? 2f : 1f) * grid.beatPeriod - 0.001f;
        List<float> chosen = new List<float>();

        foreach (Candidate c in candidates)
        {
            if (chosen.Count >= target) break;

            bool clear = true;
            foreach (float t in chosen)
            {
                if (Mathf.Abs(t - c.Time) < minGap) { clear = false; break; }
            }

            if (!clear) continue;

            chosen.Add(c.Time);
            c.type = NoteType.Twin;
        }
    }

    private static bool EnergyJump(float[] barEnergy, int bar)
    {
        int index = bar + 1;
        if (index <= 0 || index >= barEnergy.Length) return false;

        return barEnergy[index] > 0.5f && barEnergy[index] > barEnergy[index - 1] * 1.25f;
    }

    // A hold spans however long the sound rings, snapped down to half beats
    // and capped by difficulty. Anything shorter than minHoldBeats stays a Tap,
    // which is where staccato hits and plucks end up.
    private static void MarkHolds(List<Candidate> kept, SpectralFrames frames, BeatGrid grid, int difficulty,
                                  ChartGeneratorSettings settings)
    {
        SustainAnalyzer analyzer = new SustainAnalyzer(frames);

        // How much of its new energy a sound must keep to still count as held:
        // holdAmount 0 asks for 70%, 1 accepts 30%, the default half.
        float ratio = Mathf.Lerp(0.7f, 0.3f, Mathf.Clamp01(settings.holdAmount));

        float step = grid.beatPeriod * HoldStepBeats;
        float minLength = Mathf.Max(step, settings.minHoldBeats * grid.beatPeriod) - 0.001f;
        float maxLength = MaxHoldBeatsByDifficulty[difficulty] * grid.beatPeriod;

        foreach (Candidate c in kept)
        {
            if (c.type != NoteType.Tap) continue;

            // Easy charts only start holds on the beat, where they are
            // easiest to catch.
            if (difficulty <= NoOverlapMaxDifficulty && c.slot % BeatGrid.SlotsPerBeat != 0) continue;

            float sustain = analyzer.SustainSeconds(c.onset, ratio, maxLength + step);
            float length = Mathf.Min(maxLength, Mathf.Floor(sustain / step + 0.001f) * step);

            if (length < minLength) continue;

            c.type = NoteType.Hold;
            c.endTime = c.Time + length;
        }
    }

    // Breaks runs longer than the difficulty allows by dropping notes until
    // there is more than half a beat of air - a rest, the way charters give
    // players a moment to breathe. Twins and holds are never dropped for it.
    private static int AddBreathingRoom(List<Candidate> kept, BeatGrid grid, int maxRun)
    {
        float runGap = grid.beatPeriod * 0.5f + 0.001f;
        int rests = 0;
        int run = 1;

        for (int i = 1; i < kept.Count; i++)
        {
            if (kept[i].Time - kept[i - 1].Time > runGap)
            {
                run = 1;
                continue;
            }

            run++;
            if (run <= maxRun) continue;

            // Clear notes from here until the gap back to the last kept note
            // is a real rest.
            float lastKept = kept[i - 1].Time;
            int removed = 0;

            while (i < kept.Count && kept[i].Time - lastKept <= runGap && kept[i].type == NoteType.Tap)
            {
                kept.RemoveAt(i);
                removed++;
            }

            if (removed > 0) rests++;
            run = 1;
        }

        return rests;
    }

    // Lane assignment, one bar at a time. A bar whose rhythm matches an
    // earlier bar reuses that bar's lanes (mirrored on alternate repeats), so
    // repeated music plays the same way - the single biggest thing that makes
    // a chart feel authored rather than random. Anything that does not fit a
    // remembered pattern falls back to the flow rules in RuleLane.
    private static List<Candidate> AssignLanes(List<Candidate> kept, BeatGrid grid, ChartGeneratorSettings settings,
                                               int difficulty, float minSameLaneGap, ChartGeneratorResult result)
    {
        List<Candidate> placed = new List<Candidate>();
        System.Random random = new System.Random(settings.seed);

        LaneState state = new LaneState();

        // "Low" and "bright" are relative to this song: split at its own
        // median register, or a bright mix would pile everything on the right.
        List<float> registers = new List<float>(kept.Count);
        foreach (Candidate c in kept) registers.Add(c.onset.lowRatio);
        registers.Sort();
        state.registerSplit = registers.Count > 0 ? registers[registers.Count / 2] : 0.5f;

        Dictionary<string, int[]> patterns = new Dictionary<string, int[]>();
        Dictionary<string, int> repeats = new Dictionary<string, int>();

        int index = 0;
        while (index < kept.Count)
        {
            int bar = kept[index].bar;
            int end = index;
            while (end < kept.Count && kept[end].bar == bar) end++;

            List<Candidate> inBar = kept.GetRange(index, end - index);
            index = end;

            string signature = Signature(inBar, grid);
            int[] template = null;
            bool mirror = false;

            if (settings.repeatPatterns && patterns.TryGetValue(signature, out template))
            {
                int seen = repeats.ContainsKey(signature) ? repeats[signature] : 0;
                repeats[signature] = seen + 1;
                mirror = settings.mirrorRepeats && difficulty >= MirrorMinDifficulty && seen % 2 == 1;
            }

            int[] assigned = new int[inBar.Count];
            bool followedTemplate = template != null;
            bool allPlaced = true;

            for (int k = 0; k < inBar.Count; k++)
            {
                Candidate c = inBar[k];
                int preferred = -1;

                if (template != null && k < template.Length && template[k] >= 0)
                {
                    preferred = mirror ? 1 - template[k] : template[k];
                }

                int lane = Place(c, preferred, state, grid, settings, difficulty, minSameLaneGap, random);

                if (lane == int.MinValue)
                {
                    allPlaced = false;
                    followedTemplate = false;
                    continue;
                }

                if (template != null && k < template.Length && lane != preferred && c.type != NoteType.Twin)
                {
                    followedTemplate = false;
                }

                assigned[k] = lane;
                placed.Add(c);
            }

            if (template == null && allPlaced && inBar.Count > 0) patterns[signature] = assigned;
            else if (template != null && followedTemplate) result.reusedPatternBars++;
        }

        return placed;
    }

    private class LaneState
    {
        public readonly float[] freeAt = { float.NegativeInfinity, float.NegativeInfinity };
        public float anyHoldUntil = float.NegativeInfinity;
        public float lastTime = float.NegativeInfinity;
        public int lastLane = -1;
        public int sameLaneRun;
        public float registerSplit = 0.5f;
    }

    // Places one note. Returns its lane (-1 for a Twin, which takes both), or
    // int.MinValue if it had to be dropped.
    private static int Place(Candidate c, int preferred, LaneState state, BeatGrid grid,
                             ChartGeneratorSettings settings, int difficulty, float minSameLaneGap,
                             System.Random random)
    {
        float time = c.Time;

        if (difficulty <= NoOverlapMaxDifficulty && time < state.anyHoldUntil + minSameLaneGap) return int.MinValue;

        if (c.type == NoteType.Twin)
        {
            if (time - state.freeAt[0] >= minSameLaneGap && time - state.freeAt[1] >= minSameLaneGap)
            {
                state.freeAt[0] = time;
                state.freeAt[1] = time;
                state.lastTime = time;
                state.lastLane = -1;
                state.sameLaneRun = 0;
                c.lane = -1;
                return -1;
            }

            // One side is still busy - keep the accent as a single tap.
            c.type = NoteType.Tap;
        }

        int lane = preferred >= 0 && time - state.freeAt[preferred] >= minSameLaneGap
            ? preferred
            : RuleLane(c, state, grid, settings, random);

        // Too close in this lane: try the other one, and drop the note if
        // that is crowded too.
        if (time - state.freeAt[lane] < minSameLaneGap)
        {
            lane = 1 - lane;
            if (time - state.freeAt[lane] < minSameLaneGap) return int.MinValue;
        }

        state.sameLaneRun = lane == state.lastLane ? state.sameLaneRun + 1 : 1;
        state.lastLane = lane;
        state.lastTime = time;
        c.lane = lane;

        if (c.type == NoteType.Hold)
        {
            state.freeAt[lane] = c.endTime;
            state.anyHoldUntil = Mathf.Max(state.anyHoldUntil, c.endTime);
        }
        else
        {
            state.freeAt[lane] = time;
        }

        return lane;
    }

    // The flow rules for a note with no remembered pattern:
    //  - fast runs (half a beat or less apart) alternate hands, which is how
    //    streams are written for two thumbs;
    //  - otherwise the sound's register decides: hits lower than the song's
    //    median go left, brighter ones right;
    //  - ambiguous sounds alternate rather than clumping;
    //  - a lane that has had maxSameLaneRun notes in a row hands over.
    private static int RuleLane(Candidate c, LaneState state, BeatGrid grid, ChartGeneratorSettings settings,
                                System.Random random)
    {
        float gap = c.Time - state.lastTime;
        bool fast = gap <= grid.beatPeriod * 0.5f + 0.001f;

        int lane;

        if (settings.handFlow && fast && state.lastLane >= 0)
        {
            lane = 1 - state.lastLane;
        }
        else if (Mathf.Abs(c.onset.lowRatio - state.registerSplit) < 0.03f)
        {
            lane = state.lastLane >= 0 ? 1 - state.lastLane : random.Next(2);
        }
        else
        {
            lane = c.onset.lowRatio >= state.registerSplit ? 0 : 1;
        }

        if (lane == state.lastLane && state.sameLaneRun >= settings.maxSameLaneRun) lane = 1 - lane;

        return lane;
    }

    // A bar's rhythm: which sixteenths have notes, and of what kind. Two bars
    // with the same signature are the same rhythm, whatever the audio.
    private static string Signature(List<Candidate> inBar, BeatGrid grid)
    {
        System.Text.StringBuilder signature = new System.Text.StringBuilder();

        foreach (Candidate c in inBar)
        {
            signature.Append(c.slot);

            switch (c.type)
            {
                case NoteType.Twin:
                    signature.Append('w');
                    break;

                case NoteType.Hold:
                    signature.Append('h');
                    signature.Append(Mathf.RoundToInt((c.endTime - c.Time) / grid.beatPeriod * 2f));
                    break;

                default:
                    signature.Append('t');
                    break;
            }

            signature.Append(',');
        }

        return signature.ToString();
    }

    private static List<BeatmapNoteJson> ToJson(List<Candidate> placed, ChartGeneratorResult result)
    {
        List<BeatmapNoteJson> notes = new List<BeatmapNoteJson>(placed.Count);

        foreach (Candidate c in placed)
        {
            int id = notes.Count + 1;

            if (c.type == NoteType.Twin)
            {
                result.twinCount++;
                notes.Add(new BeatmapNoteJson { id = id, type = "Twin", time = c.Time });
                continue;
            }

            string lane = c.lane == 0 ? "Left" : "Right";
            if (c.lane == 0) result.leftLaneCount++;
            else result.rightLaneCount++;

            if (c.type == NoteType.Hold)
            {
                result.holdCount++;
                notes.Add(new BeatmapNoteJson { id = id, type = "Hold", lane = lane, startTime = c.Time, endTime = c.endTime });
            }
            else
            {
                result.tapCount++;
                notes.Add(new BeatmapNoteJson { id = id, type = "Tap", lane = lane, time = c.Time });
            }
        }

        return notes;
    }

    private static float Percentile(float[] values, float p)
    {
        if (values.Length == 0) return 0f;

        float[] sorted = (float[])values.Clone();
        System.Array.Sort(sorted);
        return sorted[Mathf.Clamp(Mathf.FloorToInt((sorted.Length - 1) * p), 0, sorted.Length - 1)];
    }
}

