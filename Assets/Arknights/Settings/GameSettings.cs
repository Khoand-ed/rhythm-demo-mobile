using Tools;
using UnityEngine;

namespace Settings {
    public class GameSettings {
        public const string CHAR_META_PATH = @"Meta/Char";
        
        public const string MONSTER_META_PATH = @"Meta/Monster";
        
        public const string DUNGEON_META_PATH = @"Meta/Dungeon";

        public const string ITEM_META_PATH = @"Meta/Item";

        public const string UI_PREFAB_PATH = @"Prefab/UI";

        public static bool GAME_KEEP_SPEED = true;
        
        public static bool GAME_PERFORMANCE = false;
        
        public static int GAME_PROFILED_SCREEN = 0;
        
        public static bool SOUND_SOUND_EFFECT_ENABLE = true;
        
        public static int SOUND_SOUND_EFFECT_VALUE = 100;
        
        public static bool SOUND_MUSIC_ENABLE = true;
        
        public static int SOUND_MUSIC_VALUE = 100;
        
        public static bool SOUND_VOICE_ENABLE = true;

        public static int SOUND_VOICE_VALUE = 100;

        // ------------------------------------------------------------------ saved sound settings

        // 存在这台设备上, 不分服务器 / Kept per device, deliberately NOT Api.Scoped: how loud the game
        // plays is the player's choice for this phone, the same whichever server (Local, Staging,
        // Production) a build talks to. Nothing here belongs to one server the way a session, a queue
        // or a "seen" flag does.
        private const string SoundEffectEnableKey = "Promuse.Settings.SoundEffect.Enabled";
        private const string SoundEffectValueKey = "Promuse.Settings.SoundEffect.Volume";
        private const string MusicEnableKey = "Promuse.Settings.Music.Enabled";
        private const string MusicValueKey = "Promuse.Settings.Music.Volume";
        private const string VoiceEnableKey = "Promuse.Settings.Voice.Enabled";
        private const string VoiceValueKey = "Promuse.Settings.Voice.Volume";

        /// <summary>
        /// 读取存下的声音设置 / Puts the saved sound settings over the defaults above. SoundManager calls
        /// it as it starts, before it applies them, so the first clip already plays at what the player
        /// chose. A setting never saved keeps its default.
        ///
        /// 以前不存 / These used to live only in memory: Music switched off came back on at the next
        /// launch, with the settings screen showing "on" whatever had been chosen.
        /// </summary>
        public static void LoadSound() {
            SOUND_SOUND_EFFECT_ENABLE = PlayerPrefs.GetInt(SoundEffectEnableKey, SOUND_SOUND_EFFECT_ENABLE ? 1 : 0) != 0;
            SOUND_SOUND_EFFECT_VALUE = Volume(PlayerPrefs.GetInt(SoundEffectValueKey, SOUND_SOUND_EFFECT_VALUE));
            SOUND_MUSIC_ENABLE = PlayerPrefs.GetInt(MusicEnableKey, SOUND_MUSIC_ENABLE ? 1 : 0) != 0;
            SOUND_MUSIC_VALUE = Volume(PlayerPrefs.GetInt(MusicValueKey, SOUND_MUSIC_VALUE));
            SOUND_VOICE_ENABLE = PlayerPrefs.GetInt(VoiceEnableKey, SOUND_VOICE_ENABLE ? 1 : 0) != 0;
            SOUND_VOICE_VALUE = Volume(PlayerPrefs.GetInt(VoiceValueKey, SOUND_VOICE_VALUE));
        }

        /// <summary>
        /// 写盘 / Writes the sound settings and flushes them to disk. The settings screen calls it as it
        /// closes, and as the app goes to the background while it is open - not on every step of a
        /// slider drag.
        /// </summary>
        public static void SaveSound() {
            PlayerPrefs.SetInt(SoundEffectEnableKey, SOUND_SOUND_EFFECT_ENABLE ? 1 : 0);
            PlayerPrefs.SetInt(SoundEffectValueKey, SOUND_SOUND_EFFECT_VALUE);
            PlayerPrefs.SetInt(MusicEnableKey, SOUND_MUSIC_ENABLE ? 1 : 0);
            PlayerPrefs.SetInt(MusicValueKey, SOUND_MUSIC_VALUE);
            PlayerPrefs.SetInt(VoiceEnableKey, SOUND_VOICE_ENABLE ? 1 : 0);
            PlayerPrefs.SetInt(VoiceValueKey, SOUND_VOICE_VALUE);
            PlayerPrefs.Save();
        }

        // 滑条是 0-100 / The sliders run 0-100; a value outside that is a damaged pref, not a choice.
        private static int Volume(int value) => Mathf.Clamp(value, 0, 100);
    }
}