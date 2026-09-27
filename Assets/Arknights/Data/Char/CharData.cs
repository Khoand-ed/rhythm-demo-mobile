using System;
using UnityEngine;

namespace Data.Char {
    // 角色数据
    [Serializable]
    public class CharData {
        
        // 角色ID
        [SerializeField]
        private string id;

        // 阶级(0-2)
        [SerializeField]
        private int elite;

        // 等级 (0 - getMaxLevel)
        [SerializeField]
        private int level;

        // 经验 
        [SerializeField]
        private int exp;

        // 信赖值
        [SerializeField]
        private int trust;
        
        // 元数据 (不存储）
        private CharMeta data;

        public CharData(string id) {
            this.id = id;
        }

        /// <summary>
        /// 服务端说了算 / Builds one from what the server holds. The only way
        /// progression is set from outside this class: there are deliberately no
        /// setters for elite, level, exp or trust, because nothing on the device
        /// is allowed to decide them.
        /// </summary>
        public CharData(string id, int elite, int level, int exp, int trust) {
            this.id = id;
            this.elite = elite;
            this.level = level;
            this.exp = exp;
            this.trust = trust;
        }

        public string GetId() => id;
        public int GetElite() => elite;
        public int GetLevel() => level;
        public int GetExp() => exp;
        public int GetTrust() => trust;
        public CharMeta GetCharMeta() => data != null ? data : data = CharManager.Inst().GetMeta(id);

        public CharAttribute GetAttribute() {
            return GetCharMeta().GetLevelAttributes()[elite] * level + GetCharMeta().GetEliteAttributes()[elite];
        }

        // 最大精英等级
        public int GetMaxElite() {
            return CharMeta.GetMaxElite(data.GetRarity());
        }
        
        // 最大等级
        public int GetMaxLevel() {
            return CharMeta.GetMaxLevel(data.GetRarity(),elite);
        }
        
        // 获得最大经验值
        public int GetMaxExp() {
            return CharMeta.GetMaxExp(elite,level);
        }
    }
}