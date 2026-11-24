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

        // ------------------------------------------------------------------ saved play settings

        /// <summary>
        /// 主界面跟着手机倾斜 / Home's parallax follows the phone's tilt. Off, Home stands still on a
        /// phone - for a player the motion bothers. Read every frame by UI.HomeTilt, so switching it
        /// takes effect as the settings screen closes.
        /// </summary>
        public static bool HOME_TILT_ENABLE = true;

        /// <summary>
        /// 音频偏移, 毫秒 / The audio offset in milliseconds: how much later than its sound a note
        /// reaches the line and is judged. Positive when the sound reaches the player late - Bluetooth
        /// headphones, a slow speaker. The gameplay scene hands it to Conductor.latencyOffset once,
        /// before the song starts, so it applies from the next song.
        /// </summary>
        public static int AUDIO_OFFSET_MS = 0;

        /// <summary>
        /// 偏移上下限 / How far the offset goes either way. A lag over 200 ms - the widest judging window -
        /// misses every tap, and Bluetooth often adds that much, so the range reaches past it.
        /// </summary>
        public const int AudioOffsetLimitMs = 300;

        // 同样按设备存 / Per device too, for the reason above: a phone and its headphones are this
        // device's, whichever server a build talks to.
        private const string HomeTiltEnableKey = "Promuse.Settings.HomeTilt.Enabled";
        private const string AudioOffsetKey = "Promuse.Settings.AudioOffset.Ms";

        /// <summary>
        /// 读取存下的玩法设置 / Puts the saved settings that are not volumes - Home's tilt and the audio
        /// offset - over the defaults above. GameStart calls it as the front-end boots, and the gameplay
        /// scene again before it reads the offset, since that scene can be opened straight from the
        /// Editor without GameStart. Reading it twice is harmless: the settings screen saves as it
        /// closes, so what is saved is what is in memory.
        /// </summary>
        public static void LoadGame() {
            HOME_TILT_ENABLE = PlayerPrefs.GetInt(HomeTiltEnableKey, HOME_TILT_ENABLE ? 1 : 0) != 0;
            AUDIO_OFFSET_MS = AudioOffset(PlayerPrefs.GetInt(AudioOffsetKey, AUDIO_OFFSET_MS));
        }

        /// <summary>Writes them and flushes to disk, at the same moments as <see cref="SaveSound"/>.</summary>
        public static void SaveGame() {
            PlayerPrefs.SetInt(HomeTiltEnableKey, HOME_TILT_ENABLE ? 1 : 0);
            PlayerPrefs.SetInt(AudioOffsetKey, AUDIO_OFFSET_MS);
            PlayerPrefs.Save();
        }

        /// <summary>The offset kept within <see cref="AudioOffsetLimitMs"/> either way.</summary>
        public static int AudioOffset(int milliseconds) =>
            Mathf.Clamp(milliseconds, -AudioOffsetLimitMs, AudioOffsetLimitMs);
    }
}