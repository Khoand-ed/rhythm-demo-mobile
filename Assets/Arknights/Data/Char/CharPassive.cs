using System;
using UnityEngine;

namespace Data.Char {
    /// <summary>
    /// 被动技能 / An operator's one passive.
    ///
    /// 一个角色只有一个被动 / Exactly one per operator, by design. The GDD is explicit that the
    /// detail panel shows a single skill and no empty slots, so this is one field of one type
    /// rather than three loose strings on CharMeta - "exactly one" becomes a property of the type
    /// instead of a convention nobody is holding.
    ///
    /// 反序列化后永远不为 null / Unity never deserialises a [Serializable] class field as null: it
    /// materialises a default instance instead. So `passive == null` is never true for a CharMeta
    /// loaded from disk, and a check written that way silently never fires. <see cref="IsEmpty"/>
    /// is the real question to ask.
    /// </summary>
    [Serializable]
    public class CharPassive {
        [Header("技能名"),SerializeField]
        private string name;

        [Header("技能描述"),SerializeField]
        private string description;

        [Header("技能图标"),SerializeField]
        private Sprite icon;

        public string GetName() => name;
        public string GetDescription() => description;
        public Sprite GetIcon() => icon;

        /// <summary>没有被动 / True when there is nothing worth drawing a skill block for.</summary>
        public bool IsEmpty() => string.IsNullOrEmpty(name);
    }
}
