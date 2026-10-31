using System.Collections.Generic;
using System.Reflection;
using Manager;
using NUnit.Framework;
using Settings;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;

// 关掉的音乐不能自己回来 / Music switched off stays off. It used to come back by itself: a clip played
// without a fade - the loop an intro hands over to, Home's intro on every return to Home - set the volume
// from the slider whether or not Music was off, and the switch itself was forgotten at every launch.
// These pin what the fix rests on: the music is as loud as the setting allows, whatever is playing, and
// the setting is still there after a relaunch.
//
// 编辑模式里的 SoundManager / Built by hand rather than through Inst(): DontDestroyOnLoad works only in
// Play Mode, and Awake does not run in Edit Mode, so Initialization is called directly. No test here
// takes the fading path - a fade only plays in Play Mode - so every clip is either looping or played
// with Music off: the two straight cuts the bug lived in.
public class SoundSettingsTests
{
    // 键名就是存档的地址 / The keys are where every device's saved settings live. Renaming one quietly
    // resets every player's choice to the default, so they are spelled out here rather than shared.
    private static readonly string[] Keys =
    {
        "Promuse.Settings.SoundEffect.Enabled", "Promuse.Settings.SoundEffect.Volume",
        "Promuse.Settings.Music.Enabled", "Promuse.Settings.Music.Volume",
        "Promuse.Settings.Voice.Enabled", "Promuse.Settings.Voice.Volume",
    };

    private const string SettingsScreen = "Assets/Arknights/Resources/Prefab/UI/SettingUI.prefab";

    private bool effectOn, musicOn, voiceOn;
    private int effectValue, musicValue, voiceValue;
    private readonly Dictionary<string, int?> editorPrefs = new Dictionary<string, int?>();
    private readonly List<Object> made = new List<Object>();

    // 测试会改这些, 结束时放回 / The tests change the settings and write the saved ones; the Editor's own
    // are put back afterwards, so running the suite does not switch anyone's music off.
    [SetUp]
    public void RememberTheEditorsOwnSettings()
    {
        effectOn = GameSettings.SOUND_SOUND_EFFECT_ENABLE;
        effectValue = GameSettings.SOUND_SOUND_EFFECT_VALUE;
        musicOn = GameSettings.SOUND_MUSIC_ENABLE;
        musicValue = GameSettings.SOUND_MUSIC_VALUE;
        voiceOn = GameSettings.SOUND_VOICE_ENABLE;
        voiceValue = GameSettings.SOUND_VOICE_VALUE;

        editorPrefs.Clear();
        foreach (string key in Keys) editorPrefs[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
    }

    [TearDown]
    public void PutThemBack()
    {
        foreach (Object thing in made)
        {
            if (thing != null) Object.DestroyImmediate(thing);
        }
        made.Clear();

        GameSettings.SOUND_SOUND_EFFECT_ENABLE = effectOn;
        GameSettings.SOUND_SOUND_EFFECT_VALUE = effectValue;
        GameSettings.SOUND_MUSIC_ENABLE = musicOn;
        GameSettings.SOUND_MUSIC_VALUE = musicValue;
        GameSettings.SOUND_VOICE_ENABLE = voiceOn;
        GameSettings.SOUND_VOICE_VALUE = voiceValue;

        foreach (KeyValuePair<string, int?> pref in editorPrefs)
        {
            if (pref.Value.HasValue) PlayerPrefs.SetInt(pref.Key, pref.Value.Value);
            else PlayerPrefs.DeleteKey(pref.Key);
        }
        PlayerPrefs.Save();
    }

    private AudioSource MusicOf(out SoundManager sound)
    {
        GameObject host = new GameObject("SoundManager (test)");
        made.Add(host);
        sound = host.AddComponent<SoundManager>();

        MethodInfo initialization = typeof(SoundManager).GetMethod("Initialization", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(initialization, "SoundManager.Initialization");
        initialization.Invoke(sound, null);

        return host.transform.Find("Music").GetComponent<AudioSource>();
    }

    private AudioClip Clip()
    {
        AudioClip clip = AudioClip.Create("test clip", 4410, 1, 44100, false);
        made.Add(clip);
        return clip;
    }

    private static void SetMusic(bool on, int volume)
    {
        GameSettings.SOUND_MUSIC_ENABLE = on;
        GameSettings.SOUND_MUSIC_VALUE = volume;
    }

    // --- the music's volume ------------------------------------------------------------------------

    // 引子结束后接上的循环 / The loop an intro hands over to - the music that came back minutes after
    // Music was switched off. Leaving the shop for any screen but Home takes this path too.
    [Test]
    public void MusicOff_ALoopingClip_PlaysSilently()
    {
        AudioSource music = MusicOf(out SoundManager sound);
        SetMusic(false, 80);

        sound.PlayMusic(Clip(), true);

        Assert.AreEqual(0f, music.volume, "silent while Music is off");
    }

    // 回到主界面 / Home's intro on every return to Home - from a song, from the shop - and Login's after
    // signing out. With Music off it skips the fade and cuts straight in, which is where the slider's
    // value used to be set.
    [Test]
    public void MusicOff_AnIntro_PlaysSilently()
    {
        AudioSource music = MusicOf(out SoundManager sound);
        SetMusic(false, 80);

        sound.PlayMusic(Clip(), false);

        Assert.AreEqual(0f, music.volume);
    }

    // 静音而不是停 / Muted, not stopped: switching Music back on is heard at once, from the clip that
    // kept playing all along.
    [Test]
    public void MusicBackOn_TheClipThatKeptPlaying_IsHeard()
    {
        AudioSource music = MusicOf(out SoundManager sound);
        AudioClip clip = Clip();
        SetMusic(false, 80);
        sound.PlayMusic(clip, true);

        SetMusic(true, 80);
        sound.UpdateData();

        Assert.AreSame(clip, music.clip);
        Assert.AreEqual(0.8f, music.volume, 1e-5f);
    }

    [Test]
    public void MusicOn_TheSliderStillSetsHowLoud()
    {
        AudioSource music = MusicOf(out SoundManager sound);
        SetMusic(true, 40);

        sound.PlayMusic(Clip(), true);

        Assert.AreEqual(0.4f, music.volume, 1e-5f);
    }

    // --- saved across a relaunch ---------------------------------------------------------------------

    // 重开游戏, 只是不用真的重开 / A relaunch without the relaunch: what was saved comes back over the
    // defaults the next launch starts from.
    [Test]
    public void SavedSettings_ComeBackOnTheNextLaunch()
    {
        GameSettings.SOUND_SOUND_EFFECT_ENABLE = true;
        GameSettings.SOUND_SOUND_EFFECT_VALUE = 70;
        GameSettings.SOUND_MUSIC_ENABLE = false;
        GameSettings.SOUND_MUSIC_VALUE = 35;
        GameSettings.SOUND_VOICE_ENABLE = false;
        GameSettings.SOUND_VOICE_VALUE = 0;
        GameSettings.SaveSound();

        GameSettings.SOUND_SOUND_EFFECT_ENABLE = false;
        GameSettings.SOUND_SOUND_EFFECT_VALUE = 100;
        GameSettings.SOUND_MUSIC_ENABLE = true;
        GameSettings.SOUND_MUSIC_VALUE = 100;
        GameSettings.SOUND_VOICE_ENABLE = true;
        GameSettings.SOUND_VOICE_VALUE = 100;
        GameSettings.LoadSound();

        Assert.IsTrue(GameSettings.SOUND_SOUND_EFFECT_ENABLE);
        Assert.AreEqual(70, GameSettings.SOUND_SOUND_EFFECT_VALUE);
        Assert.IsFalse(GameSettings.SOUND_MUSIC_ENABLE, "Music stays off across a relaunch");
        Assert.AreEqual(35, GameSettings.SOUND_MUSIC_VALUE);
        Assert.IsFalse(GameSettings.SOUND_VOICE_ENABLE);
        Assert.AreEqual(0, GameSettings.SOUND_VOICE_VALUE);
    }

    [Test]
    public void SavedSettings_KeepTheirKeys()
    {
        SetMusic(false, 35);
        GameSettings.SaveSound();

        foreach (string key in Keys) Assert.IsTrue(PlayerPrefs.HasKey(key), key);
        Assert.AreEqual(0, PlayerPrefs.GetInt("Promuse.Settings.Music.Enabled"));
        Assert.AreEqual(35, PlayerPrefs.GetInt("Promuse.Settings.Music.Volume"));
    }

    // --- the settings screen -------------------------------------------------------------------------

    // 界面画的是存下来的 / The settings screen opens on what was saved, not on the prefab's "on" - and
    // draws it so: a switch only set, with its knob still on the other side, says the opposite of what
    // it does, and the first tap on it changes nothing.
    [Test]
    public void TheSettingsScreen_OpensOnWhatWasSaved()
    {
        GameSettings.SOUND_SOUND_EFFECT_ENABLE = true;
        GameSettings.SOUND_MUSIC_ENABLE = false;
        GameSettings.SOUND_VOICE_ENABLE = false;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsScreen);
        Assert.IsNotNull(prefab, SettingsScreen);
        GameObject screen = Object.Instantiate(prefab);
        made.Add(screen);
        SettingUI ui = screen.GetComponent<SettingUI>();

        ui.Init();

        Assert.IsTrue(ui.sound_soundEffect.isOn);
        Assert.IsFalse(ui.sound_music.isOn, "Music shows off, as saved - the prefab says on");
        Assert.IsFalse(ui.sound_voice.isOn);

        PillSwitch pill = ui.sound_music.GetComponent<PillSwitch>();
        Assert.IsNotNull(pill, "the Music switch is a PillSwitch");
        Assert.AreEqual(pill.travel, pill.knob.anchoredPosition.x, 1e-3f, "knob on the off side");
        Assert.AreEqual(0f, pill.onLabel.color.a, 1e-3f, "ON hidden");
        Assert.AreEqual(1f, pill.offLabel.color.a, 1e-3f, "OFF showing");
    }
}
