using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace Data.Char {
    /// <summary>
    /// 本地美术覆盖 / Art that exists only on this machine, laid over what CharMeta carries.
    ///
    /// 为什么要有它 / Why it exists: this repository is public, and some art used while the game is
    /// built - reference sprites from another game - must never be committed. Those assets live
    /// under Resources/LocalArt/, which is gitignored, and this one registry is how the game finds
    /// them. CharMeta asks it first and falls back to its own serialized fields, so a clone that does
    /// not have the folder simply shows the tracked placeholders.
    ///
    /// 不能反过来 / The direction matters. Nothing tracked ever points INTO the ignored folder:
    /// CharMeta.asset keeps its placeholder sprites, and only this untracked registry points at the
    /// local ones. A tracked reference to an ignored asset would be a missing reference on every
    /// fresh clone.
    ///
    /// 换成自己的美术 / When real art exists: put it in CharMeta's own fields (tracked) and delete
    /// the local folder - Arknights/Art/Remove Local Character Art. No code changes.
    ///
    /// Written by Arknights/Art/Install Placeholder Operator Art.
    /// </summary>
    public class CharArtOverrides : ScriptableObject {
        public const string ResourcePath = "LocalArt/CharArtOverrides";

        [Serializable]
        public class Entry {
            [Tooltip("The CharMeta asset name, e.g. AMIYA.")]
            public string charId;

            [Tooltip("Tall portrait: CharSelect, the Gacha card, the results screen. CharMeta.GetImage.")]
            public Sprite portrait;

            [Tooltip("Card art. CharMeta.GetCharImage.")]
            public Sprite card;

            [Tooltip("Square face: the CharSelect grid and the Gacha tabs. CharMeta.GetAvatar.")]
            public Sprite avatar;

            [Tooltip("The operator on the rhythm stage. CharMeta.GetBattleRig.")]
            public SkeletonDataAsset battleRig;

            [Tooltip("Which of battleRig's animations answer which event. CharMeta.GetBattleMotion.")]
            public OperatorMotion battleMotion;

            [Tooltip("The operator on the Home screen. CharMeta.GetHomeRig.")]
            public SkeletonDataAsset homeRig;
        }

        public List<Entry> entries = new List<Entry>();

        private static CharArtOverrides loaded;

        /// <summary>
        /// 找某个干员的本地美术, 没有就返回 null / The local art for one operator, or null when this
        /// machine has none. Looked up on every call rather than remembered when absent, so installing
        /// the art while the Editor is open takes effect without a restart; only a hit is kept.
        /// </summary>
        public static Entry For(string charId) {
            if (string.IsNullOrEmpty(charId)) return null;

            // 不用 ?? / Explicit check: a destroyed asset compares equal to null but is not null.
            if (loaded == null) loaded = Resources.Load<CharArtOverrides>(ResourcePath);
            if (loaded == null) return null;

            foreach (Entry entry in loaded.entries) {
                if (entry != null && string.Equals(entry.charId, charId, StringComparison.OrdinalIgnoreCase)) {
                    return entry;
                }
            }

            return null;
        }
    }
}
