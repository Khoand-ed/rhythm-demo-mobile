// The smallest possible stand-in for the parts of UnityEngine that the game's
// scoring rules touch, so that those rules compile and run on the server.
//
// 为什么要有这个文件 / Why this exists
// -----------------------------------
// Promuse.Shared links Assets/Source/Script/Core/*.cs by relative path rather
// than copying it. The server therefore judges a run with the *same source* the
// device does, which is the only version of replay validation that stays true
// after someone retunes a passive and forgets the server.
//
// Those files open with `using UnityEngine;`. Six Mathf calls, two types, one
// enum and five attributes are the entire surface they actually need - so this
// file supplies exactly that and nothing else. It is not a Unity
// reimplementation and must never grow into one: if a linked file starts
// needing GameObject, Coroutines or Time, that is the signal that the rule has
// drifted out of "pure logic" and belongs behind an argument instead.
//
// 不会进 Unity / Unity never sees this
// -----------------------------------
// It lives under server/, and Unity only compiles what is under Assets/. If it
// were ever copied into the project it would collide with the real UnityEngine
// on every single type here.
//
// 行为必须一致 / Behaviour has to match, not merely compile
// -------------------------------------------------------
// A shim that compiles but rounds differently is worse than no shim at all: it
// makes the server disagree with the client only at the boundaries, which is
// exactly where a cheat check reports a false positive. Approximately and
// RoundToInt below reproduce Unity's implementations deliberately - see the
// comments on each.

using System;

namespace UnityEngine
{
    /// <summary>
    /// Only the six members the linked scoring code calls.
    /// </summary>
    public static class Mathf
    {
        /// <summary>Unity exposes float.Epsilon under this name.</summary>
        public const float Epsilon = float.Epsilon;

        public static int Max(int a, int b) => a > b ? a : b;

        public static float Max(float a, float b) => a > b ? a : b;

        public static int Min(int a, int b) => a < b ? a : b;

        public static float Min(float a, float b) => a < b ? a : b;

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>
        /// Unity's relative-epsilon comparison, reproduced rather than replaced by
        /// a fixed tolerance. RunState.AddFever uses it as an early-out for "no
        /// change", so a looser or tighter rule here changes which calls do
        /// nothing - and the two sides would disagree about the fever gauge.
        /// </summary>
        public static bool Approximately(float a, float b)
        {
            return Math.Abs(b - a) < Max(1E-06f * Max(Math.Abs(a), Math.Abs(b)), Epsilon * 8f);
        }

        /// <summary>
        /// Banker's rounding, because that is what Unity does: Mathf.RoundToInt is
        /// (int)Math.Round(f), and Math.Round defaults to MidpointRounding.ToEven.
        ///
        /// 不要改成 AwayFromZero / Do not "fix" this to AwayFromZero. ScoreFor
        /// rounds every award through it, so at a .5 boundary the two sides would
        /// return different scores and a clean run would be flagged as tampered.
        /// </summary>
        public static int RoundToInt(float f) => (int)Math.Round((double)f);
    }

    /// <summary>
    /// A deterministic stand-in for UnityEngine.Random, needed by
    /// JudgeUpgradePassive: `Random.value &lt; chance` promotes a Great to a
    /// Perfect, so a passive puts a coin flip inside the scoring rules.
    ///
    /// ⚠ 不是位级相同 / NOT bit-compatible with Unity's generator.
    /// -----------------------------------------------------------
    /// Unity seeds an xorshift128 in a way it does not document, so matching it
    /// exactly is guesswork, and guesswork that is wrong only sometimes is the
    /// worst possible outcome for a cheat check. This is an honest, documented
    /// generator instead - which means the server can REPLAY a run only once it
    /// is told the sequence to replay.
    ///
    /// That is a Phase 4 decision, not something this shim can paper over:
    /// the server must issue the run's seed (never accept one from the client,
    /// or a player can roll for a favourable one) and the client must seed from
    /// it. Until then, any run whose operator carries a rolling passive cannot
    /// be validated by re-simulation.
    /// </summary>
    public static class Random
    {
        // xorshift128. The constants are Marsaglia's; the point here is that the
        // sequence is reproducible from a seed and written down, not that it is
        // the same one Unity produces.
        private static uint x = 123456789, y = 362436069, z = 521288629, w = 88675123;

        public static void InitState(int seed)
        {
            x = (uint)seed;
            y = 362436069;
            z = 521288629;
            w = 88675123;

            // A low seed leaves the state nearly zero and the first few draws
            // badly correlated. Discard a short warm-up rather than hand those
            // out as a judgement roll.
            for (int i = 0; i < 16; i++) NextUInt();
        }

        private static uint NextUInt()
        {
            uint t = x ^ (x << 11);

            x = y;
            y = z;
            z = w;
            w = w ^ (w >> 19) ^ t ^ (t >> 8);

            return w;
        }

        /// <summary>
        /// [0, 1). Unity's is inclusive of 1, which matters for exactly one
        /// comparison in the game: `Random.value &lt; chance` at chance = 1 must
        /// always promote. It does here too, since the result is never 1.
        /// </summary>
        public static float value => NextUInt() / 4294967296f;
    }

    /// <summary>
    /// Base for the settings and passive assets. Empty on purpose: on the server
    /// these are plain objects built from configuration, never loaded from an
    /// asset database.
    ///
    /// `name` is here because everything deriving from UnityEngine.Object has it
    /// and a linked file may reference it for a log line.
    /// </summary>
    public abstract class ScriptableObject
    {
        public string name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Marker only. PassiveSO carries an icon for the character select screen;
    /// the server never renders anything, so the field just has to have a type.
    /// </summary>
    public sealed class Sprite
    {
    }

    /// <summary>
    /// Only the member the linked code names. FeverSettings.manualActivateKey is
    /// input, which the server has no notion of - it is carried so the type
    /// lines up, never read.
    /// </summary>
    public enum KeyCode
    {
        None = 0,
        Space = 32,
    }

    // ---------------------------------------------------------------- attributes
    //
    // Inspector decoration. They have to exist for the linked files to compile
    // and they do nothing here. Each one's constructor matches how the game
    // actually writes it - see the call sites named in the comments.

    /// <summary>`[Header("...")]` - PassiveSO, the settings assets.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class HeaderAttribute : Attribute
    {
        public readonly string header;

        public HeaderAttribute(string header) => this.header = header;
    }

    /// <summary>`[Tooltip("...")]` - NoteData, the settings assets.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class TooltipAttribute : Attribute
    {
        public readonly string tooltip;

        public TooltipAttribute(string tooltip) => this.tooltip = tooltip;
    }

    /// <summary>`[TextArea(2, 4)]` - PassiveSO.description.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class TextAreaAttribute : Attribute
    {
        public readonly int minLines;
        public readonly int maxLines;

        public TextAreaAttribute() : this(3, 3)
        {
        }

        public TextAreaAttribute(int minLines, int maxLines)
        {
            this.minLines = minLines;
            this.maxLines = maxLines;
        }
    }

    /// <summary>
    /// `[Range(0f, 1f)]` - JudgeUpgradePassive. A hint to the Inspector, not a
    /// constraint: Unity does not enforce it at runtime and neither does this.
    /// Anything the server must actually guarantee is validated explicitly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class RangeAttribute : Attribute
    {
        public readonly float min;
        public readonly float max;

        public RangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    /// <summary>
    /// `[CreateAssetMenu(...)]` - every settings asset and all four passives.
    /// Named arguments only, which is how the game writes it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName { get; set; } = string.Empty;
        public string menuName { get; set; } = string.Empty;
        public int order { get; set; }
    }

    /// <summary>
    /// `[SerializeField]`. Not used by anything linked today, but it is the one
    /// attribute that appears the moment a private field is added to a settings
    /// asset, and leaving it out would break the link for a one-word change.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SerializeFieldAttribute : Attribute
    {
    }
}
