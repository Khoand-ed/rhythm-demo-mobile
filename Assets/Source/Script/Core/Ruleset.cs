using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

// 一份规则, 两边都读 / Every number that decides a score, as plain data both sides can hold.
//
// The device keeps its tuning where it always has - three assets, a handful of GameManager
// fields and each operator's CharMeta - because that is where designers edit it. The server
// cannot load a Unity asset, so it reads the same numbers from server/data/ruleset.json
// instead. Two copies of anything drift; what keeps these two honest is twofold:
//
//   - RulesetParityTests (EditMode, run in CI) loads the assets and the scene and fails, field
//     by field, if the JSON disagrees with them.
//   - RulesetFingerprint hashes the rules a run was actually played with. The device sends it
//     with every result and the server compares it against its own, so a retune that reached
//     only one side is named on the first run rather than showing up as a score the server
//     thinks is impossible.
//
// 没有初值 / The numeric fields deliberately carry no initialisers. A field missing from the
// JSON then loads as 0, which the parity test reports, instead of quietly keeping a default
// that happens to look right.
//
// JsonUtility 能读 / Plain [Serializable] classes with public fields and no dictionaries, so
// Unity's JsonUtility reads the file in the EditMode test and System.Text.Json reads it on the
// server, from the one definition.
[System.Serializable]
public class Ruleset
{
    public const int CurrentVersion = 1;

    public int version;

    public ScoreRules score = new ScoreRules();
    public JudgeRules judge = new JudgeRules();
    public HealthRules health = new HealthRules();
    public FeverRules fever = new FeverRules();

    public List<OperatorRules> operators = new List<OperatorRules>();

    /// <summary>Null when the id is unknown. Ordinal: ids are data, not prose.</summary>
    public OperatorRules FindOperator(string id)
    {
        if (string.IsNullOrEmpty(id) || operators == null) return null;

        for (int i = 0; i < operators.Count; i++)
        {
            if (operators[i] != null && string.Equals(operators[i].id, id, System.StringComparison.Ordinal))
            {
                return operators[i];
            }
        }

        return null;
    }
}

/// <summary>
/// The awards GameManager pays per judgement and per held tick, and the multiplier ladder.
///
/// 名字照判定 / Named after the Judgement each one pays for rather than after GameManager's own
/// fields (scorePerNote, scorePerGoodNote): those names predate the enum, and "good" in the
/// scene is what the code calls Great.
/// </summary>
[System.Serializable]
public class ScoreRules
{
    public int perfect;
    public int great;
    public int hit;

    public int holdTick;
    public float holdTickInterval;

    public int[] multiplierThresholds = new int[0];

    public int BaseFor(Judgement judgement)
    {
        switch (judgement)
        {
            case Judgement.Perfect: return perfect;
            case Judgement.Great: return great;
            case Judgement.Hit: return hit;
            default: return 0;
        }
    }
}

[System.Serializable]
public class JudgeRules
{
    public float tapPerfect;
    public float tapGreat;
    public float tapHit;
    public float holdPerfect;
    public float holdGreat;

    public static JudgeRules From(JudgeSettings settings)
    {
        return new JudgeRules
        {
            tapPerfect = settings.tapPerfect,
            tapGreat = settings.tapGreat,
            tapHit = settings.tapHit,
            holdPerfect = settings.holdPerfect,
            holdGreat = settings.holdGreat,
        };
    }

    public void ApplyTo(JudgeSettings settings)
    {
        settings.tapPerfect = tapPerfect;
        settings.tapGreat = tapGreat;
        settings.tapHit = tapHit;
        settings.holdPerfect = holdPerfect;
        settings.holdGreat = holdGreat;
    }

    /// <summary>Same rule as JudgeSettings.MaxWindow, for code that holds only the rules.</summary>
    public float MaxWindow(NoteType type)
    {
        return type == NoteType.Hold ? holdGreat : tapHit;
    }
}

[System.Serializable]
public class HealthRules
{
    public int maxHp;
    public int tapMissDamage;
    public int holdMissDamage;
    public int twinMissDamage;

    public static HealthRules From(HealthSettings settings)
    {
        return new HealthRules
        {
            maxHp = settings.maxHp,
            tapMissDamage = settings.tapMissDamage,
            holdMissDamage = settings.holdMissDamage,
            twinMissDamage = settings.twinMissDamage,
        };
    }

    public void ApplyTo(HealthSettings settings)
    {
        settings.maxHp = maxHp;
        settings.tapMissDamage = tapMissDamage;
        settings.holdMissDamage = holdMissDamage;
        settings.twinMissDamage = twinMissDamage;
    }
}

/// <summary>
/// 不含按键 / Everything on FeverSettings except manualActivateKey, which is an input binding
/// and cannot change a score.
/// </summary>
[System.Serializable]
public class FeverRules
{
    public float maxFever;
    public float perfectGain;
    public float greatGain;
    public float hitGain;
    public float missLoss;
    public bool autoActivate;
    public float feverDuration;
    public int feverScoreMultiplier;

    public static FeverRules From(FeverSettings settings)
    {
        return new FeverRules
        {
            maxFever = settings.maxFever,
            perfectGain = settings.perfectGain,
            greatGain = settings.greatGain,
            hitGain = settings.hitGain,
            missLoss = settings.missLoss,
            autoActivate = settings.autoActivate,
            feverDuration = settings.feverDuration,
            feverScoreMultiplier = settings.feverScoreMultiplier,
        };
    }

    public void ApplyTo(FeverSettings settings)
    {
        settings.maxFever = maxFever;
        settings.perfectGain = perfectGain;
        settings.greatGain = greatGain;
        settings.hitGain = hitGain;
        settings.missLoss = missLoss;
        settings.autoActivate = autoActivate;
        settings.feverDuration = feverDuration;
        settings.feverScoreMultiplier = feverScoreMultiplier;
    }
}

/// <summary>
/// One operator as the scoring rules see them.
///
/// 存生效值 / The modifiers are stored as the values a run actually uses. GameManager treats a
/// modifier of 0 as "this CharMeta predates the rhythm fields" and plays it at 1, so a 0 here
/// would describe a run that never happens. maxHp is the exception and is stored raw, because
/// RunState itself owns that fallback (see RunState.MaxHp) and applies it on both sides.
/// </summary>
[System.Serializable]
public class OperatorRules
{
    public string id;
    public float scoreModifier;
    public float feverModifier;
    public int maxHp;
    public PassiveRules passive = new PassiveRules();
}

/// <summary>
/// A passive as a type name and its numbers. An empty <see cref="type"/> means none:
/// JsonUtility cannot represent a null object field, so absence has to be spelled.
/// </summary>
[System.Serializable]
public class PassiveRules
{
    public string type;
    public List<RuleParam> parameters = new List<RuleParam>();

    public bool IsNone
    {
        get { return string.IsNullOrEmpty(type); }
    }

    public static PassiveRules From(PassiveSO passive)
    {
        PassiveRules rules = new PassiveRules();
        if (passive == null) return rules;

        rules.type = passive.GetType().Name;
        passive.WriteRules(rules.parameters);
        return rules;
    }
}

/// <summary>
/// One named number. Floats throughout: every parameter today is a small integer or a plain
/// decimal, and a float holds integers exactly far beyond any charge count or HP value.
/// </summary>
[System.Serializable]
public class RuleParam
{
    public string name;
    public float value;

    public RuleParam()
    {
    }

    public RuleParam(string name, float value)
    {
        this.name = name;
        this.value = value;
    }

    public static float Find(IReadOnlyList<RuleParam> parameters, string name, float fallback)
    {
        if (parameters == null) return fallback;

        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] != null && string.Equals(parameters[i].name, name, System.StringComparison.Ordinal))
            {
                return parameters[i].value;
            }
        }

        return fallback;
    }
}

/// <summary>
/// A short hash of the rules one run is played under: the global tuning plus the operator the
/// run used.
///
/// 不是防作弊 / Not an anti-cheat measure - the device computes it, so a modified client can send
/// whatever value it likes. It exists to catch honest skew: a designer retunes a window in the
/// Editor, the JSON is not updated, and the server would otherwise judge every run against
/// numbers that no longer exist.
///
/// 位而不是文本 / Floats are hashed by their IEEE bits, never formatted. Unity's runtime and
/// .NET 10 do not agree on how to print every float as text, and a fingerprint that differed
/// between an honest device and the server would reject every run. The bits are the value.
///
/// FNV-1a / A plain 64-bit FNV-1a rather than SHA-256, deliberately: this needs to tell versions
/// apart, not resist an adversary, and FNV is a dozen lines with no dependency on a crypto
/// provider that IL2CPP's stripping might remove.
/// </summary>
public static class RulesetFingerprint
{
    public static string Compute(Ruleset rules, OperatorRules op)
    {
        StringBuilder text = new StringBuilder(512);

        text.Append("promuse-ruleset/").Append(Int(rules.version)).Append('\n');

        ScoreRules score = rules.score;
        Line(text, "score.perfect", Int(score.perfect));
        Line(text, "score.great", Int(score.great));
        Line(text, "score.hit", Int(score.hit));
        Line(text, "score.holdTick", Int(score.holdTick));
        Line(text, "score.holdTickInterval", Bits(score.holdTickInterval));

        StringBuilder ladder = new StringBuilder();
        if (score.multiplierThresholds != null)
        {
            for (int i = 0; i < score.multiplierThresholds.Length; i++)
            {
                if (i > 0) ladder.Append(',');
                ladder.Append(Int(score.multiplierThresholds[i]));
            }
        }
        Line(text, "score.multiplierThresholds", ladder.ToString());

        JudgeRules judge = rules.judge;
        Line(text, "judge.tapPerfect", Bits(judge.tapPerfect));
        Line(text, "judge.tapGreat", Bits(judge.tapGreat));
        Line(text, "judge.tapHit", Bits(judge.tapHit));
        Line(text, "judge.holdPerfect", Bits(judge.holdPerfect));
        Line(text, "judge.holdGreat", Bits(judge.holdGreat));

        HealthRules health = rules.health;
        Line(text, "health.maxHp", Int(health.maxHp));
        Line(text, "health.tapMissDamage", Int(health.tapMissDamage));
        Line(text, "health.holdMissDamage", Int(health.holdMissDamage));
        Line(text, "health.twinMissDamage", Int(health.twinMissDamage));

        FeverRules fever = rules.fever;
        Line(text, "fever.maxFever", Bits(fever.maxFever));
        Line(text, "fever.perfectGain", Bits(fever.perfectGain));
        Line(text, "fever.greatGain", Bits(fever.greatGain));
        Line(text, "fever.hitGain", Bits(fever.hitGain));
        Line(text, "fever.missLoss", Bits(fever.missLoss));
        Line(text, "fever.autoActivate", fever.autoActivate ? "1" : "0");
        Line(text, "fever.feverDuration", Bits(fever.feverDuration));
        Line(text, "fever.feverScoreMultiplier", Int(fever.feverScoreMultiplier));

        if (op != null)
        {
            Line(text, "op.scoreModifier", Bits(op.scoreModifier));
            Line(text, "op.feverModifier", Bits(op.feverModifier));
            Line(text, "op.maxHp", Int(op.maxHp));

            PassiveRules passive = op.passive ?? new PassiveRules();
            Line(text, "op.passive", passive.IsNone ? "none" : passive.type);

            // 按名字排 / Sorted by name, so the order a passive happens to write its numbers in
            // is not part of the identity of the rules.
            List<RuleParam> sorted = new List<RuleParam>();
            if (!passive.IsNone && passive.parameters != null) sorted.AddRange(passive.parameters);
            sorted.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            for (int i = 0; i < sorted.Count; i++)
            {
                Line(text, "op.passive." + sorted[i].name, Bits(sorted[i].value));
            }
        }

        return Fnv1a64(text.ToString()).ToString("x16", CultureInfo.InvariantCulture);
    }

    private static void Line(StringBuilder text, string key, string value)
    {
        text.Append(key).Append('=').Append(value).Append('\n');
    }

    private static string Int(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Bits(float value)
    {
        FloatBits bits = default(FloatBits);
        bits.Float = value;
        return bits.Int.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static ulong Fnv1a64(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        unchecked
        {
            ulong hash = 14695981039346656037UL;

            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }
}

/// <summary>
/// A float's IEEE bits without an allocation. BitConverter.GetBytes would allocate per call,
/// and the input trace calls this once per frame on a phone.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct FloatBits
{
    [FieldOffset(0)] public float Float;
    [FieldOffset(0)] public int Int;
}
