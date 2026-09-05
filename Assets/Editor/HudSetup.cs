using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the HP and Fever gauges in the Canvas and wires them to GameManager.
// Find-or-create throughout, so re-running never moves or restyles a bar you
// have already adjusted - use Reset HUD To Default for that.
public class HudSetup
{
    private const string HpBarName = "HPBar";
    private const string FeverBarName = "FeverBar";

    // Free HUD slots below the existing ScoreText (y=400) and ComboText (y=340).
    private static readonly Vector2 HpDefaultPosition = new Vector2(0f, 280f);
    private static readonly Vector2 FeverDefaultPosition = new Vector2(0f, 230f);
    private static readonly Vector2 DefaultSize = new Vector2(600f, 28f);

    [MenuItem("Tools/Rhythm/Set Up HUD")]
    static void SetUp()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("No GameManager in the open scene.");
            return;
        }

        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            Debug.LogError("No Canvas in the open scene to put the gauges on.");
            return;
        }

        GaugeBar hp = EnsureBar(canvas.transform, HpBarName, HpDefaultPosition, HpGradient(), false);
        GaugeBar feverGauge = EnsureBar(canvas.transform, FeverBarName, FeverDefaultPosition, FeverGradient(), true);

        Undo.RecordObject(gameManager, "Set Up HUD");
        gameManager.hpBar = hp;
        gameManager.feverBar = feverGauge;

        // The scene already carries a disabled "TOO MANY NOTES MISSED!" object
        // that nothing referenced; that is exactly the fail banner.
        if (gameManager.failedText == null)
        {
            Transform failed = FindDeep(canvas.transform, "FailedText");
            if (failed != null) gameManager.failedText = failed.gameObject;
            else Debug.LogWarning("No FailedText object found - assign GameManager.failedText by hand.");
        }

        EditorUtility.SetDirty(gameManager);
        EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);

        Debug.Log($"HUD set up: {HpBarName} and {FeverBarName} under {canvas.name}. " +
                  "Drag them in the Scene view or the Inspector to reposition; re-running this " +
                  "tool will not move them again. Fill direction lives on each bar's Fill image " +
                  "(Image Type: Filled -> Fill Method).");
    }

    [MenuItem("Tools/Rhythm/Reset HUD To Default")]
    static void ResetHud()
    {
        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            Debug.LogError("No Canvas in the open scene.");
            return;
        }

        MoveBack(canvas.transform, HpBarName, HpDefaultPosition);
        MoveBack(canvas.transform, FeverBarName, FeverDefaultPosition);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Debug.Log("HUD bars returned to their default positions and sizes.");
    }

    private static void MoveBack(Transform canvas, string name, Vector2 position)
    {
        Transform bar = canvas.Find(name);
        if (bar == null) return;

        RectTransform rect = (RectTransform)bar;
        Undo.RecordObject(rect, "Reset HUD");
        rect.anchoredPosition = position;
        rect.sizeDelta = DefaultSize;
        EditorUtility.SetDirty(rect);
    }

    // A bar is a dark background Image with a Filled fill Image inside it, plus
    // a GaugeBar driving the fill.
    private static GaugeBar EnsureBar(Transform canvas, string name, Vector2 position,
                                      Gradient gradient, bool pulseWhenFull)
    {
        Transform existing = canvas.Find(name);
        if (existing != null)
        {
            GaugeBar found = existing.GetComponent<GaugeBar>();
            if (found != null) return found;
        }

        GameObject barObject = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
        if (existing == null) Undo.RegisterCreatedObjectUndo(barObject, "Set Up HUD");

        RectTransform rect = (RectTransform)barObject.transform;
        rect.SetParent(canvas, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = DefaultSize;

        Image background = barObject.GetComponent<Image>();
        if (background == null) background = Undo.AddComponent<Image>(barObject);
        background.color = new Color(0f, 0f, 0f, 0.45f);

        // Fill stretches to the background, so resizing the bar resizes the fill.
        GameObject fillObject = new GameObject("Fill", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(fillObject, "Set Up HUD");

        RectTransform fillRect = (RectTransform)fillObject.transform;
        fillRect.SetParent(rect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image fill = fillObject.AddComponent<Image>();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        fill.color = gradient.Evaluate(1f);

        // A plain white sprite so the fill is visible without any art; swap it
        // for your own on the Fill image whenever you like.
        fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        background.sprite = fill.sprite;

        GaugeBar gauge = barObject.GetComponent<GaugeBar>();
        if (gauge == null) gauge = Undo.AddComponent<GaugeBar>(barObject);

        Undo.RecordObject(gauge, "Set Up HUD");
        gauge.fill = fill;
        gauge.colorOverValue = gradient;
        gauge.pulseWhenFull = pulseWhenFull;
        EditorUtility.SetDirty(gauge);

        return gauge;
    }

    // Green while healthy, ramping through amber to red as HP drains.
    private static Gradient HpGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.15f, 0.15f), 0f),
                new GradientColorKey(new Color(0.95f, 0.75f, 0.10f), 0.35f),
                new GradientColorKey(new Color(0.20f, 0.80f, 0.25f), 0.7f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        return gradient;
    }

    private static Gradient FeverGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.35f, 0.55f, 1f), 0f),
                new GradientColorKey(new Color(1f, 0.45f, 0.85f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        return gradient;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }

        return null;
    }
}
