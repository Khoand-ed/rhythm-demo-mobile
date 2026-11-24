using System.Collections.Generic;
using NUnit.Framework;
using Settings;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEngine;

// 倾斜开关和音频偏移 / Home's tilt switch and the audio offset: saved across a relaunch under keys that do
// not move, kept within range, and shown on the settings screen as saved - the switch drawn, the offset
// spelled out, and -/+ stepping it by 5 ms through the slider. The Editor's own settings and saved
// prefs are put back after each test.
public class PlaySettingsTests
{
    // 键名就是存档的地址 / Renaming a key quietly resets every player's choice, so they are spelled out.
    private const string TiltKey = "Promuse.Settings.HomeTilt.Enabled";
    private const string OffsetKey = "Promuse.Settings.AudioOffset.Ms";
    private static readonly string[] Keys = { TiltKey, OffsetKey };

    private const string SettingsScreen = "Assets/Arknights/Resources/Prefab/UI/SettingUI.prefab";

    private bool tiltOn;
    private int offsetMs;
    private readonly Dictionary<string, int?> editorPrefs = new Dictionary<string, int?>();
    private readonly List<Object> made = new List<Object>();

    [SetUp]
    public void RememberTheEditorsOwnSettings()
    {
        tiltOn = GameSettings.HOME_TILT_ENABLE;
        offsetMs = GameSettings.AUDIO_OFFSET_MS;

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

        GameSettings.HOME_TILT_ENABLE = tiltOn;
        GameSettings.AUDIO_OFFSET_MS = offsetMs;

        foreach (KeyValuePair<string, int?> pref in editorPrefs)
        {
            if (pref.Value.HasValue) PlayerPrefs.SetInt(pref.Key, pref.Value.Value);
            else PlayerPrefs.DeleteKey(pref.Key);
        }
        PlayerPrefs.Save();
    }

    private SettingUI OpenSettings()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsScreen);
        Assert.IsNotNull(prefab, SettingsScreen);
        GameObject screen = Object.Instantiate(prefab);
        made.Add(screen);

        SettingUI ui = screen.GetComponent<SettingUI>();
        ui.Init();
        return ui;
    }

    // --- saved across a relaunch --------------------------------------------------------------------

    [Test]
    public void SavedSettings_ComeBackOnTheNextLaunch()
    {
        GameSettings.HOME_TILT_ENABLE = false;
        GameSettings.AUDIO_OFFSET_MS = 120;
        GameSettings.SaveGame();

        GameSettings.HOME_TILT_ENABLE = true;
        GameSettings.AUDIO_OFFSET_MS = 0;
        GameSettings.LoadGame();

        Assert.IsFalse(GameSettings.HOME_TILT_ENABLE, "the tilt stays off");
        Assert.AreEqual(120, GameSettings.AUDIO_OFFSET_MS);
    }

    [Test]
    public void SavedSettings_KeepTheirKeys()
    {
        GameSettings.HOME_TILT_ENABLE = false;
        GameSettings.AUDIO_OFFSET_MS = -45;
        GameSettings.SaveGame();

        Assert.AreEqual(0, PlayerPrefs.GetInt(TiltKey, -1));
        Assert.AreEqual(-45, PlayerPrefs.GetInt(OffsetKey, 0));
    }

    [Test]
    public void AnOffsetOutOfRange_IsReadBackWithinIt()
    {
        PlayerPrefs.SetInt(OffsetKey, 5000);
        GameSettings.LoadGame();
        Assert.AreEqual(GameSettings.AudioOffsetLimitMs, GameSettings.AUDIO_OFFSET_MS);

        PlayerPrefs.SetInt(OffsetKey, -5000);
        GameSettings.LoadGame();
        Assert.AreEqual(-GameSettings.AudioOffsetLimitMs, GameSettings.AUDIO_OFFSET_MS);
    }

    // --- the settings screen --------------------------------------------------------------------------

    [Test]
    public void TheSettingsScreen_HasBothRows()
    {
        SettingUI ui = OpenSettings();

        Assert.IsNotNull(ui.game_tilt, "run Arknights/Settings/Build Offset And Tilt Rows");
        Assert.IsNotNull(ui.sound_offset);
        Assert.IsNotNull(ui.sound_offset_minus);
        Assert.IsNotNull(ui.sound_offset_plus);
        Assert.IsNotNull(ui.sound_offset_readout);

        Assert.AreEqual(-GameSettings.AudioOffsetLimitMs, ui.sound_offset.minValue);
        Assert.AreEqual(GameSettings.AudioOffsetLimitMs, ui.sound_offset.maxValue);
        Assert.IsTrue(ui.sound_offset.wholeNumbers, "1 ms steps");
    }

    [Test]
    public void TheSettingsScreen_OpensOnWhatWasSaved()
    {
        GameSettings.HOME_TILT_ENABLE = false;
        GameSettings.AUDIO_OFFSET_MS = 35;

        SettingUI ui = OpenSettings();

        Assert.IsFalse(ui.game_tilt.isOn, "the tilt shows off, as saved - the prefab says on");
        PillSwitch pill = ui.game_tilt.GetComponent<PillSwitch>();
        Assert.IsNotNull(pill, "the tilt switch is a PillSwitch");
        Assert.AreEqual(pill.travel, pill.knob.anchoredPosition.x, 1e-3f, "drawn off, not only set");

        Assert.AreEqual(35f, ui.sound_offset.value);
        Assert.AreEqual("+35 ms", ui.sound_offset_readout.text);
    }

    [Test]
    public void PlusAndMinus_StepTheOffsetByFive_AndStopAtTheLimit()
    {
        GameSettings.AUDIO_OFFSET_MS = 0;
        SettingUI ui = OpenSettings();

        ui.sound_offset_plus.onClick.Invoke();
        Assert.AreEqual(5, GameSettings.AUDIO_OFFSET_MS);
        Assert.AreEqual("+5 ms", ui.sound_offset_readout.text);

        ui.sound_offset_minus.onClick.Invoke();
        ui.sound_offset_minus.onClick.Invoke();
        Assert.AreEqual(-5, GameSettings.AUDIO_OFFSET_MS);
        Assert.AreEqual("-5 ms", ui.sound_offset_readout.text);

        ui.sound_offset.value = GameSettings.AudioOffsetLimitMs - 2;
        ui.sound_offset_plus.onClick.Invoke();
        Assert.AreEqual(GameSettings.AudioOffsetLimitMs, GameSettings.AUDIO_OFFSET_MS, "held at the limit");
    }

    [Test]
    public void TheTiltSwitch_ChangesTheSetting()
    {
        GameSettings.HOME_TILT_ENABLE = true;
        SettingUI ui = OpenSettings();

        ui.game_tilt.isOn = false;

        Assert.IsFalse(GameSettings.HOME_TILT_ENABLE);
    }
}
