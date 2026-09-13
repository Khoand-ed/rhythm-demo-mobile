using System.ComponentModel;

namespace Data.Char {
    
    // 角色标签 / Description 是显示给玩家的, 枚举名保持原样不动
    // The Description strings are what the UI shows (via Expand.GetDescription), so they are in
    // English; the pinyin enum names are left alone because saved PlayerData refers to them.
    public enum CharTag {
        [Description("Healing")]
        ZHI_LIAO,
        [Description("Support")]
        ZHI_YUAN,
        [Description("DPS")]
        SHU_CHU,
        [Description("AoE")]
        QUN_GONG,
        [Description("Slow")]
        JIAN_SU,
        [Description("Survival")]
        SHENG_CUN,
        [Description("Defense")]
        FAGN_HU,
        [Description("Debuff")]
        XUE_RUO,
        [Description("Shift")]
        WEI_YI,
        [Description("Crowd Control")]
        KONG_CHANG,
        [Description("Nuker")]
        BAO_FA,
        [Description("Summon")]
        ZHAO_HUAN,
        [Description("Fast-Redeploy")]
        KUAI_SU_FU_HUO,
        [Description("DP-Recovery")]
        FEI_YONG_HUI_FU,
        [Description("Starter")]
        XIN_SHOU
    }

    // 角色职业
    public enum CharProfession {
        [Description("Vanguard")]
        XIAN_FENG,
        [Description("Guard")]
        JIN_WEI,
        [Description("Sniper")]
        JU_JI,
        [Description("Defender")]
        ZHONG_ZHUANG,
        [Description("Medic")]
        YI_LIAO,
        [Description("Supporter")]
        FU_ZHU,
        [Description("Caster")]
        SHU_SHI,
        [Description("Specialist")]
        TE_ZHONG
    }

    public enum CharPosition {
        [Description("Melee")]
        JIN_ZHAN,
        [Description("Ranged")]
        YUAN_CHENG
    }

    public enum CharCamp {
        NULL,
        [Description("Babel")]
        BBT,
        [Description("Blacksteel")]
        HG,
        [Description("Kazimierz")]
        KXME,
        [Description("Rhodes Island")]
        LDD,
        [Description("Lungmen")]
        LM,
        [Description("Rim Billiton")]
        LMBT,
        [Description("Laterano")]
        LTL,
        [Description("Leithanien")]
        LTNY,
        [Description("Rhine Lab")]
        LYSM,
        [Description("Penguin Logistics")]
        QEWL,
        [Description("Abyssal Hunters")]
        SHLR,
        [Description("Victoria")]
        WDLY,
        [Description("Ursus")]
        WSS,
        [Description("Kjerag")]
        XLG,
        [Description("Siesta")]
        XST
    }
}