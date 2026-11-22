using Data.Player;
using DG.Tweening;
using Settings;
using Manager;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    public class SettingUI : UIBase {
        public Image blur;

        /*
        * There's a missing class named "Toggle Controller".
        * I can't find it anywhere on this project.
        * For now, I replace it with Toggle class and commented out
        * those Initialization() method to "fix" this issue.
        *
        * The code that referencing Toggle Controller is just this class.
        */

        public Toggle game_keep_speed;
        public Toggle game_performance;
        public Slider game_profileScreen;
        public Button game_exit;

        public Toggle sound_soundEffect;
        public Slider sound_soundEffect_value;
        public Toggle sound_music;
        public Slider sound_music_value;
        public Toggle sound_voice;
        public Slider sound_voice_value;

        // 两行由工具加 / Added to the prefab by Arknights/Settings/Build Offset And Tilt Rows.
        public Toggle game_tilt;
        public Slider sound_offset;
        public Button sound_offset_minus;
        public Button sound_offset_plus;
        public Text sound_offset_readout;

        private const int OffsetStepMs = 5;

        public override void Init() {
            // game_keep_speed.Initialization(GameSettings.GAME_KEEP_SPEED);
            // game_performance.Initialization(GameSettings.GAME_PERFORMANCE);
            game_profileScreen.value = GameSettings.GAME_PROFILED_SCREEN;

            // 开关显示存下来的值 / The sound switches show the saved settings, not the prefab's "on".
            // Without this a screen rebuilt after signing out, or opened after a relaunch, said "on"
            // while Music was off - and the first tap on it switched nothing.
            SetSwitch(sound_soundEffect, GameSettings.SOUND_SOUND_EFFECT_ENABLE);
            sound_soundEffect_value.value = GameSettings.SOUND_SOUND_EFFECT_VALUE;
            SetSwitch(sound_music, GameSettings.SOUND_MUSIC_ENABLE);
            sound_music_value.value = GameSettings.SOUND_MUSIC_VALUE;
            SetSwitch(sound_voice, GameSettings.SOUND_VOICE_ENABLE);
            sound_voice_value.value = GameSettings.SOUND_VOICE_VALUE;

            // 新的两行可以不存在 / The two newer rows are null-checked: a tool adds them, and Init must not
            // throw on a prefab without them, or the whole screen - LOG OUT included - fails to open.
            if (game_tilt != null) {
                SetSwitch(game_tilt, GameSettings.HOME_TILT_ENABLE);
                game_tilt.onValueChanged.AddListener(value => GameSettings.HOME_TILT_ENABLE = value);
            }

            if (sound_offset != null) InitOffset();

            game_keep_speed.onValueChanged.AddListener(value => GameSettings.GAME_KEEP_SPEED = value);
            game_performance.onValueChanged.AddListener(value => GameSettings.GAME_PERFORMANCE = value);
            game_profileScreen.onValueChanged.AddListener(value => {
                GameSettings.GAME_PROFILED_SCREEN = (int) value;
            });

            sound_soundEffect.onValueChanged.AddListener(value => {
                GameSettings.SOUND_SOUND_EFFECT_ENABLE = value;
                SoundManager.Inst().UpdateData();
            });
            sound_soundEffect_value.onValueChanged.AddListener(value => {
                GameSettings.SOUND_SOUND_EFFECT_VALUE = (int) value;
                SoundManager.Inst().UpdateData();
            });
            sound_music.onValueChanged.AddListener(value => {
                GameSettings.SOUND_MUSIC_ENABLE = value;
                SoundManager.Inst().UpdateData();
            });
            sound_music_value.onValueChanged.AddListener(value => {
                GameSettings.SOUND_MUSIC_VALUE = (int) value;
                SoundManager.Inst().UpdateData();
            });
            sound_voice.onValueChanged.AddListener(value => {
                GameSettings.SOUND_VOICE_ENABLE = value;
                SoundManager.Inst().UpdateData();
            });
            sound_voice_value.onValueChanged.AddListener(value => {
                GameSettings.SOUND_VOICE_VALUE = (int) value;
                SoundManager.Inst().UpdateData();
            });

            game_exit.onClick.AddListener(() => {
                CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE,"You are about to return to the login screen. Continue?",() => {
                    PlayerManager.Inst().Exit();
                    UIManager.Inst().Hide("HomeUI",true);
                    UIManager.Inst().Hide("SettingUI",true);
                    UIManager.Inst().Hide("DepotUI",true);
                    UIManager.Inst().Hide("CharUI",true);
                    float v = 0;
                    DOTween.To(() => v,value => v = value,3,2f).OnComplete(() => UIManager.Inst().Show("LoginUI"));
                });
            });
        }

        public override void Show() {
            base.Show();
            Material blurryShader = blur.material;
            blurryShader.SetFloat("_Size",0);
            float value = 0;
            DOTween.To(() => value,v => value = v,160,0.5f).OnUpdate(() => blurryShader.SetFloat("_Size",value));
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1,0.3f);
        }

        public override void Hide(bool destroy = false) {
            // 关界面时存 / Saved as the screen closes - every way out (its close button, Android Back,
            // signing out) comes through here - rather than on every step of a slider drag.
            GameSettings.SaveSound();
            GameSettings.SaveGame();

            Material blurryShader = blur.material;
            float value = blurryShader.GetFloat("_Size");
            DOTween.To(() => value,v => value = v,0,0.2f).OnUpdate(() => blurryShader.SetFloat("_Size",value));
            canvasGroup.DOFade(0,0.2f).OnComplete(() => { base.Hide(destroy); });
        }

        // 切到后台也存 / And when the app goes to the background with this screen still open. Unity
        // otherwise writes PlayerPrefs to disk only as the app quits, and Android can end an app in
        // the background without it ever quitting.
        private void OnApplicationPause(bool paused) {
            if (!paused) return;
            GameSettings.SaveSound();
            GameSettings.SaveGame();
        }

        /// <summary>
        /// 音频偏移 / The audio offset: a slider in 1 ms steps, -/+ buttons for 5, and the value spelled
        /// out. Takes effect from the next song - the gameplay scene reads it as a song starts.
        /// </summary>
        private void InitOffset() {
            // 范围以代码为准 / The range comes from GameSettings, whatever the prefab was built with.
            // Set before any listener exists, so clamping here tells nobody.
            sound_offset.wholeNumbers = true;
            sound_offset.minValue = -GameSettings.AudioOffsetLimitMs;
            sound_offset.maxValue = GameSettings.AudioOffsetLimitMs;
            sound_offset.SetValueWithoutNotify(GameSettings.AUDIO_OFFSET_MS);
            ShowOffset(GameSettings.AUDIO_OFFSET_MS);

            sound_offset.onValueChanged.AddListener(value => {
                GameSettings.AUDIO_OFFSET_MS = GameSettings.AudioOffset(Mathf.RoundToInt(value));
                ShowOffset(GameSettings.AUDIO_OFFSET_MS);
            });

            if (sound_offset_minus != null) sound_offset_minus.onClick.AddListener(() => StepOffset(-OffsetStepMs));
            if (sound_offset_plus != null) sound_offset_plus.onClick.AddListener(() => StepOffset(OffsetStepMs));
        }

        // 经过滑条 / Through the slider, so the setting and the readout follow from its one listener.
        private void StepOffset(int milliseconds) {
            sound_offset.value = GameSettings.AudioOffset(Mathf.RoundToInt(sound_offset.value) + milliseconds);
        }

        private void ShowOffset(int milliseconds) {
            if (sound_offset_readout == null) return;
            sound_offset_readout.text = (milliseconds > 0 ? "+" : "") + milliseconds + " ms";
        }

        private static void SetSwitch(Toggle toggle, bool on) {
            PillSwitch pill = toggle.GetComponent<PillSwitch>();
            if (pill != null) pill.SetWithoutNotify(on);
            else toggle.SetIsOnWithoutNotify(on);
        }
    }
}