using System;
using UnityEngine;

namespace Data.Char {
    /// <summary>
    /// 一个干员的动作表 / How one operator's rig acts out the run: which of its animations answers a
    /// tap, a hold, a miss, fever, and where in each animation to start and stop.
    ///
    /// 为什么按干员分 / Rigs are not interchangeable. One has Stun and another does not; one strikes
    /// 0.47s into its Attack and another 0.63s in; a hold reads as a held pose on one rig and as a loop
    /// built for the purpose on another. The stage keeps a default table, and a clip set here replaces
    /// the matching default for this operator only - a clip left without names falls through to it.
    ///
    /// 只是数据 / Data only: CharacterPresenter on the rhythm stage reads it, and nothing in scoring,
    /// HP or fever does.
    /// </summary>
    [CreateAssetMenu(fileName = "OperatorMotion", menuName = "ArkNight/OperatorMotion")]
    public class OperatorMotion : ScriptableObject {
        /// <summary>One reaction: the first animation the rig has from names, and how to play it.</summary>
        [Serializable]
        public class Clip {
            [Tooltip("Animation names to try in order; the first one the rig has is used. None found = skipped.")]
            public string[] names = new string[0];

            [Tooltip("Where in the animation to begin, seconds. Skips a wind-up that would make a reaction land late.")]
            public float from;

            [Tooltip("Where to stop, seconds. 0 plays to the end.")]
            public float to;

            [Tooltip("Playback speed.")]
            public float speed = 1f;

            [Tooltip("Seconds to cross-fade in. Short: the next hit is often 0.2s away.")]
            public float blend = 0.04f;

            public Clip() { }

            public Clip(float from, float to, float speed, params string[] names) {
                this.from = from;
                this.to = to;
                this.speed = speed;
                this.names = names;
            }

            /// <summary>Whether this clip says anything. An empty one defers to the stage's default.</summary>
            public bool IsSet => names != null && names.Length > 0;
        }

        [Tooltip("Multiplies the height the stage gives every operator. 1 = as tall as the others.")]
        public float heightScale = 1f;

        public Clip idle = new Clip();
        public Clip enter = new Clip();
        public Clip tap = new Clip();
        public Clip twin = new Clip();
        public Clip holdLoop = new Clip();
        public Clip holdEnd = new Clip();

        [Tooltip("Left empty on a rig with no flinch of its own; the stage then shakes and reddens it instead.")]
        public Clip miss = new Clip();

        public Clip fever = new Clip();
        public Clip fail = new Clip();
        public Clip win = new Clip();
    }
}
