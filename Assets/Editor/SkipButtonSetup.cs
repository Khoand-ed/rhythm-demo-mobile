using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the osu!-style intro Skip button in the gameplay Canvas: bottom
// right, a dark plate with "SKIP >>" and a thin bar that fills as the notes
// approach. Find-or-create, so once built it can be moved and restyled freely
// and rerunning only re-wires what is missing.
public class SkipButtonSetup
{
    private const string ButtonName = "SkipButton";

    [MenuItem("Tools/Rhythm/Set Up Skip Button")]
    static void SetUp()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager == null)
        {
            Debug.LogError("No GameManager in the open scene - open Main.unity first.");
            return;
        }

        Canvas canvas = gameManager.scoreText != null ? gameManager.scoreText.canvas : null;
        if (canvas == null) canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            Debug.LogError("No Canvas in the open scene to put the Skip button on.");
            return;
        }

        canvas = canvas.rootCanvas;
        Transform existing = canvas.transform.Find(ButtonName);
        GameObject root = existing != null ? existing.gameObject : BuildButton(canvas, gameManager);

        SkipIntro skip = root.GetComponent<SkipIntro>();
        if (skip == null) skip = Undo.AddComponent<SkipIntro>(root);

        Undo.RecordObject(skip, "Set Up Skip Button");
        if (skip.group == null) skip.group = root.GetComponent<CanvasGroup>();

        if (skip.progress == null)
        {
            Transform bar = root.transform.Find("Progress");
            if (bar != null) skip.progress = bar.GetComponent<Image>();
        }

        EditorUtility.SetDirty(skip);

        // A persistent listener, so the wiring is visible (and editable) in
        // the Button's OnClick list rather than hidden in code.
        Button button = root.GetComponent<Button>();
        if (button != null && !HasSkipListener(button, skip))
        {
            Undo.RecordObject(button, "Set Up Skip Button");
            UnityEventTools.AddPersistentListener(button.onClick, skip.Skip);
            EditorUtility.SetDirty(button);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeObject = root;

        Debug.Log($"Skip button ready under {canvas.name}/{ButtonName}. It stays hidden in play unless a chart's " +
                  "first note is far enough away to be worth skipping (SkipIntro.minimumSkip).", root);
    }

    private static GameObject BuildButton(Canvas canvas, GameManager gameManager)
    {
        GameObject root = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer),
                                         typeof(Image), typeof(Button), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(root, "Set Up Skip Button");

        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(canvas.transform, false);
        rect.SetAsLastSibling();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-48f, 48f);
        rect.sizeDelta = new Vector2(280f, 96f);

        Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        Image plate = root.GetComponent<Image>();
        plate.sprite = rounded;
        plate.type = Image.Type.Sliced;
        plate.color = new Color(0f, 0f, 0f, 0.6f);

        Button button = root.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(0f, 10f);
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        if (gameManager.scoreText != null) label.font = gameManager.scoreText.font;
        label.text = "SKIP  >>";
        label.fontSize = 44f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;

        GameObject barObject = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform barRect = (RectTransform)barObject.transform;
        barRect.SetParent(rect, false);
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.offsetMin = new Vector2(14f, 10f);
        barRect.offsetMax = new Vector2(-14f, 16f);

        Image bar = barObject.GetComponent<Image>();
        bar.sprite = rounded;
        bar.type = Image.Type.Filled;
        bar.fillMethod = Image.FillMethod.Horizontal;
        bar.fillOrigin = (int)Image.OriginHorizontal.Left;
        bar.fillAmount = 0f;
        bar.color = new Color(1f, 0.84f, 0.3f, 0.9f);
        bar.raycastTarget = false;

        return root;
    }

    private static bool HasSkipListener(Button button, SkipIntro skip)
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentTarget(i) == skip && button.onClick.GetPersistentMethodName(i) == nameof(SkipIntro.Skip))
            {
                return true;
            }
        }

        return false;
    }
}
