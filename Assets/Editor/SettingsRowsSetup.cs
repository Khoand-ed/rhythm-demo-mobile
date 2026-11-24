using UI.Sub;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置界面的两行 / Adds two rows to the settings screen (SettingUI.prefab):
///   GAME page,  y 80:  HOME TILT    - a switch, in the free slot over the hidden notch row;
///   SOUND page, y -30: AUDIO OFFSET - a readout, -/+ buttons and a slider, under VOICE, with a note.
/// Each is a copy of a row the screen already has - PERFORMANCE for the switch, VOICE for the slider,
/// LOG OUT's button for -/+ - so fonts, colours and the sliding switch match without restating them.
///
/// 找到就不动 / Find-or-create: a row already in the prefab is kept where it is and only linked to
/// SettingUI. Reset Offset And Tilt Rows To Default deletes both and builds them again. Generate
/// Bootstrap Assets calls BuildInto too, so a screen built from nothing has them.
/// </summary>
public static class SettingsRowsSetup
{
    private const string PrefabPath = "Assets/Arknights/Resources/Prefab/UI/SettingUI.prefab";

    public const string TiltRowName = "game_tiltRow";
    public const string TiltSwitchName = "game_tilt";
    public const string OffsetRowName = "sound_offsetRow";
    public const string OffsetSliderName = "sound_offset";
    public const string OffsetMinusName = "sound_offset_minus";
    public const string OffsetPlusName = "sound_offset_plus";
    public const string OffsetReadoutName = "sound_offset_readout";

    private const float TiltRowY = 80f;
    private const float OffsetRowY = -30f;

    private static readonly Color NoteGrey = new Color(0.42f, 0.43f, 0.45f);

    // ------------------------------------------------------------------ menu items

    [MenuItem("Arknights/Settings/Build Offset And Tilt Rows", false, 60)]
    public static void Build()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SettingUI ui = root.GetComponent<SettingUI>();
            if (ui == null)
            {
                Debug.LogError($"[SettingsRowsSetup] {PrefabPath} has no SettingUI.");
                return;
            }

            string done = BuildInto(ui);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[SettingsRowsSetup] " + done +
                      "\n  SettingUI loads from Resources: rebuild the player before testing on a phone.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Arknights/Settings/Reset Offset And Tilt Rows To Default", false, 61)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Delete(root.transform.Find("Body/GamePage/" + TiltRowName));
            Delete(root.transform.Find("Body/SoundPage/" + OffsetRowName));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Build();
    }

    /// <summary>Finds or builds both rows and links them to <paramref name="ui"/>. Returns what it did.</summary>
    public static string BuildInto(SettingUI ui)
    {
        // UIBase hides transform behind a field it fills in Awake, which never runs on a prefab being
        // edited - so the component's own transform, asked for as a Component.
        Transform root = ((Component)ui).transform;
        Transform gamePage = root.Find("Body/GamePage");
        Transform soundPage = root.Find("Body/SoundPage");

        string tilt = gamePage != null ? BuildTilt(ui, gamePage) : "HOME TILT: not built - no Body/GamePage";
        string offset = soundPage != null && gamePage != null
            ? BuildOffset(ui, soundPage, gamePage)
            : "AUDIO OFFSET: not built - no Body/SoundPage or Body/GamePage";
        return tilt + "\n  " + offset;
    }

    // ------------------------------------------------------------------ HOME TILT

    private static string BuildTilt(SettingUI ui, Transform gamePage)
    {
        Transform row = gamePage.Find(TiltRowName);
        string what = "kept";

        if (row == null)
        {
            Transform template = gamePage.Find("game_performanceRow");
            if (template == null) return "HOME TILT: not built - the GAME page has no game_performanceRow to copy";

            row = Copy(template, TiltRowName, TiltRowY);
            Rename(row, "game_performance", TiltSwitchName);
            SetText(row, "Label", "HOME TILT");
            SetText(row, "Note", "*Tilt the phone to move the Home screen. Off, Home stands still");
            what = "built";
        }

        Transform toggle = row.Find(TiltSwitchName);
        ui.game_tilt = toggle != null ? toggle.GetComponent<Toggle>() : null;
        return $"HOME TILT ({TiltRowName}): {what}, " +
               (ui.game_tilt != null ? "linked to SettingUI.game_tilt" : "NOT linked - its switch is missing; Reset rebuilds it");
    }

    // ------------------------------------------------------------------ AUDIO OFFSET

    private static string BuildOffset(SettingUI ui, Transform soundPage, Transform gamePage)
    {
        Transform row = soundPage.Find(OffsetRowName);
        string what = "kept";

        if (row == null)
        {
            Transform template = soundPage.Find("sound_voiceRow");
            Transform button = gamePage.Find("ExitRow/game_exit");
            if (template == null || button == null)
            {
                return "AUDIO OFFSET: not built - the SOUND page has no sound_voiceRow, or the GAME page no ExitRow/game_exit, to copy";
            }

            row = Copy(template, OffsetRowName, OffsetRowY);
            SetText(row, "Label", "AUDIO OFFSET");

            // 偏移没有开关 / An offset is a number and never "off": the copied switch goes.
            Delete(row.Find("sound_voice"));

            // 读数放到开关那一列 / The readout takes the switch's column, so the value sits by the label.
            Transform readout = row.Find("Readout");
            if (readout != null)
            {
                readout.name = OffsetReadoutName;
                Place(readout, new Vector2(-160, 0), new Vector2(180, 46));
                Text text = readout.GetComponent<Text>();
                text.alignment = TextAnchor.MiddleCenter;
                text.text = "0 ms";
            }

            Transform sliderNode = row.Find("sound_voice_value");
            if (sliderNode != null)
            {
                sliderNode.name = OffsetSliderName;
                Place(sliderNode, new Vector2(310, 0), new Vector2(460, 34));

                // 没有"多少"的填充 / No fill: the bar is a position either side of 0, not an amount.
                Slider slider = sliderNode.GetComponent<Slider>();
                Transform fillArea = sliderNode.Find("Fill Area");
                if (fillArea != null) fillArea.gameObject.SetActive(false);
                slider.fillRect = null;
                slider.wholeNumbers = true;
                slider.minValue = -300;
                slider.maxValue = 300;
                slider.value = 0;
            }

            CopyButton(button, row, OffsetMinusName, "-", new Vector2(20, 0));
            CopyButton(button, row, OffsetPlusName, "+", new Vector2(600, 0));

            // 说明 / Which way to move it, in the words of what the player hears.
            Text label = row.Find("Label").GetComponent<Text>();
            Text note = Object.Instantiate(label, row);
            note.name = "Note";
            note.text = "*Raise it if the music sounds late, e.g. over Bluetooth. It applies from the next song";
            note.fontSize = 19;
            note.color = NoteGrey;
            note.alignment = TextAnchor.MiddleLeft;
            Place(note.transform, new Vector2(-220, -52), new Vector2(1100, 40));

            what = "built";
        }

        ui.sound_offset = Find<Slider>(row, OffsetSliderName);
        ui.sound_offset_minus = Find<Button>(row, OffsetMinusName);
        ui.sound_offset_plus = Find<Button>(row, OffsetPlusName);
        ui.sound_offset_readout = Find<Text>(row, OffsetReadoutName);

        bool linked = ui.sound_offset != null && ui.sound_offset_minus != null &&
                      ui.sound_offset_plus != null && ui.sound_offset_readout != null;
        return $"AUDIO OFFSET ({OffsetRowName}): {what}, " +
               (linked
                   ? "linked to SettingUI.sound_offset, sound_offset_minus, sound_offset_plus and sound_offset_readout"
                   : "NOT fully linked - a part is missing; Reset rebuilds it");
    }

    // ------------------------------------------------------------------ helpers

    private static Transform Copy(Transform template, string name, float y)
    {
        GameObject copy = Object.Instantiate(template.gameObject, template.parent);
        copy.name = name;
        copy.SetActive(true);
        RectTransform rect = (RectTransform)copy.transform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        return copy.transform;
    }

    private static void CopyButton(Transform template, Transform row, string name, string caption, Vector2 position)
    {
        GameObject copy = Object.Instantiate(template.gameObject, row);
        copy.name = name;
        Place(copy.transform, position, new Vector2(64, 52));

        Text text = copy.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = caption;
            text.fontSize = 32;
            ((RectTransform)text.transform).sizeDelta = new Vector2(64, 52);
        }
    }

    private static void Place(Transform node, Vector2 position, Vector2 size)
    {
        RectTransform rect = (RectTransform)node;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Rename(Transform row, string from, string to)
    {
        Transform child = row.Find(from);
        if (child != null) child.name = to;
    }

    private static void SetText(Transform row, string child, string text)
    {
        Transform node = row.Find(child);
        Text label = node != null ? node.GetComponent<Text>() : null;
        if (label != null) label.text = text;
    }

    private static T Find<T>(Transform row, string child) where T : Component
    {
        Transform node = row.Find(child);
        return node != null ? node.GetComponent<T>() : null;
    }

    private static void Delete(Transform node)
    {
        if (node != null) Object.DestroyImmediate(node.gameObject);
    }
}
