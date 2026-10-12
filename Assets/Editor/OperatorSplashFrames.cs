using System.Collections.Generic;
using System.IO;
using UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 立绘框 / Turns every Image that shows an operator standing into a <see cref="SplashFrame"/>: the
/// splash is drawn whole by a child the frame scales and places, never squeezed into the Image's own
/// rectangle and never cut to fit it. Where text sits on the art, a <see cref="SplashShade"/> darkens
/// just that part; the art itself is drawn white, in its own colours.
///
///   Roster card          Cover  - fills the card, framed on the face, clipped to the card
///   Operator detail      Figure - the figure as tall as the frame, the scene spilling past it,
///                                 clipped only to its own page so a neighbour never shows it
///   Operator picker      Figure - clipped to the centre column, between the grid and the stats
///   Headhunting          Figure - the featured operator and the lineup cards, each clipped to its card;
///                        Cover  - the result cards
///   Dialogue             Figure - unclipped, over the scene's background
///
/// 找到就不动 / Find-or-create: a frame or shade that exists keeps whatever was tuned on it by hand.
/// Reset Operator Splash Frames To Default puts the values below back. The builders for the picker,
/// headhunting and dialogue screens call <see cref="FitInto"/> too, so a rebuilt screen keeps its frames.
/// </summary>
public static class OperatorSplashFrames
{
    private const string UiPrefabDir = "Assets/Arknights/Resources/Prefab/UI";
    private const string SplashName = "Splash";

    private sealed class Slot
    {
        public string Prefab;            // the prefab under UiPrefabDir, without extension
        public string Path;              // the Image showing the art, from the prefab root
        public SplashFrame.Mode Mode;
        public string Clip;              // what clips the splash: "" the frame itself, null nothing
        public Shade[] Shades = new Shade[0];
    }

    private sealed class Shade
    {
        public string Parent;            // from the prefab root; "" the frame
        public string Name;
        public SplashShade.Edge Edge;
        public float Size;               // how far in from that edge, canvas units
        public float Strength;
        public float Hold;

        public Shade(string parent, string name, SplashShade.Edge edge, float size, float strength, float hold)
        {
            Parent = parent;
            Name = name;
            Edge = edge;
            Size = size;
            Strength = strength;
            Hold = hold;
        }
    }

    private const string InfoPage = "Viewport/Content/prefab";

    private static readonly Slot[] Slots =
    {
        // 干员列表的卡 / A little shade under the class icon and the stars along the top, and into the
        // dark name strip at the bottom where the level ring overlaps the art.
        new Slot
        {
            Prefab = "CharUI", Path = "ScrollView/Viewport/Content/cardPrefab/img_char_card",
            Mode = SplashFrame.Mode.Cover, Clip = "",
            Shades = new[]
            {
                new Shade("", "ShadeTop", SplashShade.Edge.Top, 72f, 0.45f, 0.2f),
                new Shade("", "ShadeBottom", SplashShade.Edge.Bottom, 96f, 0.55f, 0.15f),
            },
        },
        // 干员详情 / The stat panels sit left and right of the operator: each gets a shade under it.
        new Slot
        {
            Prefab = "CharInfoUI", Path = InfoPage + "/img_char",
            Mode = SplashFrame.Mode.Figure, Clip = InfoPage,
            Shades = new[]
            {
                new Shade(InfoPage, "ShadeLeft", SplashShade.Edge.Left, 1000f, 0.6f, 0.6f),
                new Shade(InfoPage, "ShadeRight", SplashShade.Edge.Right, 900f, 0.6f, 0.6f),
            },
        },
        // 选人 / The name plate along the bottom of the column.
        new Slot
        {
            Prefab = "CharSelectUI", Path = "Portrait/Image",
            Mode = SplashFrame.Mode.Figure, Clip = "Portrait",
            Shades = new[] { new Shade("Portrait", "ShadeBottom", SplashShade.Edge.Bottom, 260f, 0.55f, 0.3f) },
        },
        new Slot { Prefab = "GachaUI", Path = "CardPager/CardA/Card/Art/Featured", Mode = SplashFrame.Mode.Figure, Clip = "" },
        new Slot { Prefab = "GachaUI", Path = "CardPager/CardB/Card/Art/Featured", Mode = SplashFrame.Mode.Figure, Clip = "" },
        new Slot { Prefab = "GachaUI", Path = "CardPager/CardA/Card/Art/LineupBehind/Template", Mode = SplashFrame.Mode.Figure, Clip = "" },
        new Slot { Prefab = "GachaUI", Path = "CardPager/CardA/Card/Art/LineupFull/Template", Mode = SplashFrame.Mode.Figure, Clip = "" },
        new Slot { Prefab = "GachaUI", Path = "CardPager/CardB/Card/Art/LineupBehind/Template", Mode = SplashFrame.Mode.Figure, Clip = "" },
        new Slot { Prefab = "GachaUI", Path = "CardPager/CardB/Card/Art/LineupFull/Template", Mode = SplashFrame.Mode.Figure, Clip = "" },
        new Slot { Prefab = "GachaUI", Path = "ResultOverlay/Grid/CardTemplate/Face/Portrait", Mode = SplashFrame.Mode.Cover, Clip = "" },
        new Slot { Prefab = "StoryUI", Path = "Portrait", Mode = SplashFrame.Mode.Figure, Clip = null },
    };

    // ------------------------------------------------------------------- menu

    [MenuItem("Arknights/Art/Fit Operator Splash Frames", false, 40)]
    public static void FitAll() => Run(false);

    [MenuItem("Arknights/Art/Reset Operator Splash Frames To Default", false, 41)]
    public static void ResetAll() => Run(true);

    private static void Run(bool reset)
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        var report = new List<string>();
        var prefabs = new List<string>();
        foreach (Slot slot in Slots)
        {
            if (!prefabs.Contains(slot.Prefab)) prefabs.Add(slot.Prefab);
        }

        foreach (string prefab in prefabs)
        {
            string path = $"{UiPrefabDir}/{prefab}.prefab";
            if (!File.Exists(path))
            {
                report.Add($"  {prefab}: SKIPPED, no prefab at {path} - build that screen first");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var lines = new List<string>();
                if (Apply(root, prefab, reset, lines)) PrefabUtility.SaveAsPrefabAsset(root, path);
                report.Add($"  {prefab}:");
                report.AddRange(lines);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[OperatorSplashFrames] {(reset ? "Reset to default" : "Fitted")}\n" + string.Join("\n", report) +
                  "\n  CharUI's card is filled from Lua (CharUI.lua.txt): rebuild the AssetBundles before testing on a device.");
    }

    /// <summary>
    /// 建界面时调用 / For a screen builder, on the root it is about to save: fits that prefab's frames
    /// the same way the menu does.
    /// </summary>
    public static void FitInto(GameObject root, string prefab)
    {
        var lines = new List<string>();
        Apply(root, prefab, false, lines);
        Debug.Log($"[OperatorSplashFrames] {prefab}:\n" + string.Join("\n", lines));
    }

    private static bool Apply(GameObject root, string prefab, bool reset, List<string> lines)
    {
        bool changed = false;
        foreach (Slot slot in Slots)
        {
            if (slot.Prefab != prefab) continue;

            Transform node = root.transform.Find(slot.Path);
            UnityEngine.UI.Image image = node != null ? node.GetComponent<UnityEngine.UI.Image>() : null;
            if (image == null)
            {
                lines.Add($"    {slot.Path}: NOT FOUND, no Image there");
                continue;
            }

            RectTransform clip = slot.Clip == null ? null
                : slot.Clip.Length == 0 ? (RectTransform)node
                : root.transform.Find(slot.Clip) as RectTransform;

            string fitted = Fit(image, slot.Mode, clip, reset, out bool frameChanged);
            changed |= frameChanged;

            var shades = new List<string>();
            foreach (Shade shade in slot.Shades)
            {
                Transform parent = shade.Parent.Length == 0 ? node : root.transform.Find(shade.Parent);
                if (parent == null)
                {
                    shades.Add($"{shade.Name} NOT FOUND ({shade.Parent})");
                    continue;
                }

                // 压在立绘上, 字底下 / Over the splash and under everything after it: inside the frame,
                // right after the splash child; beside it, right after the frame.
                Transform after = parent == node ? node.Find(SplashName) : node;
                shades.Add(PlaceShade((RectTransform)parent, after, shade, reset, out bool shadeChanged));
                changed |= shadeChanged;
            }

            lines.Add($"    {slot.Path}: {fitted}" + (shades.Count > 0 ? "; " + string.Join(", ", shades) : ""));
        }

        return changed;
    }

    // ------------------------------------------------------------------ frame

    private static string Fit(UnityEngine.UI.Image slot, SplashFrame.Mode mode, RectTransform clip, bool reset, out bool changed)
    {
        changed = false;
        var did = new List<string>();

        SplashFrame frame = slot.GetComponent<SplashFrame>();
        bool made = frame == null;
        if (made)
        {
            frame = slot.gameObject.AddComponent<SplashFrame>();
            did.Add("frame added");
        }

        if (made || reset)
        {
            frame.mode = mode;
            frame.figureHeight = SplashFrame.DefaultFigureHeight;
            frame.faceWidth = SplashFrame.DefaultFaceWidth;
            frame.faceTop = SplashFrame.DefaultFaceTop;
            if (!made) did.Add("values reset");
        }

        Transform child = slot.transform.Find(SplashName);
        UnityEngine.UI.Image splash = child != null ? child.GetComponent<UnityEngine.UI.Image>() : null;
        if (splash == null)
        {
            var go = new GameObject(SplashName, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            go.layer = slot.gameObject.layer;
            go.transform.SetParent(slot.transform, false);
            go.transform.SetAsFirstSibling();
            splash = go.GetComponent<UnityEngine.UI.Image>();
            splash.raycastTarget = false;
            splash.enabled = false;

            // 不再染灰 / White, in the art's own colours: the Image it replaces was tinted grey, which is
            // why the roster looked darker than the art. Its transparency is kept - the lineup behind a
            // featured operator is drawn a little see-through on purpose.
            splash.color = new Color(1f, 1f, 1f, slot.color.a > 0f ? slot.color.a : 1f);
            did.Add("splash child added");
        }
        else if (reset)
        {
            splash.color = new Color(1f, 1f, 1f, splash.color.a);
        }

        if (frame.splash != splash)
        {
            frame.splash = splash;
            if (!made) did.Add("splash relinked");
        }

        // 框只是框 / The frame's own Image draws nothing from now on.
        if (slot.sprite != null || slot.color != Color.clear || slot.preserveAspect)
        {
            slot.sprite = null;
            slot.color = Color.clear;
            slot.preserveAspect = false;
            did.Add("own Image cleared");
        }

        if (clip != null && clip.GetComponent<RectMask2D>() == null && clip.GetComponent<Mask>() == null)
        {
            clip.gameObject.AddComponent<RectMask2D>();
            did.Add(clip == slot.transform ? "clipped to itself" : $"clipped to {clip.name}");
        }

        changed = did.Count > 0;
        return $"{frame.mode}" + (did.Count > 0 ? " (" + string.Join(", ", did) + ")" : ", kept");
    }

    // ------------------------------------------------------------------ shade

    private static string PlaceShade(RectTransform parent, Transform after, Shade spec, bool reset, out bool changed)
    {
        Transform found = parent.Find(spec.Name);
        SplashShade shade = found != null ? found.GetComponent<SplashShade>() : null;
        if (found != null && shade == null)
        {
            changed = false;
            return $"{spec.Name} SKIPPED, a node of that name is not a shade";
        }

        bool made = shade == null;
        if (made)
        {
            var go = new GameObject(spec.Name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SplashShade));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            if (after != null) go.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            shade = go.GetComponent<SplashShade>();
        }

        changed = made || reset;
        if (!changed) return spec.Name + " kept";

        shade.color = Color.black;
        shade.raycastTarget = false;
        shade.darkest = spec.Edge;
        shade.strength = spec.Strength;
        shade.hold = spec.Hold;

        RectTransform rect = shade.rectTransform;
        switch (spec.Edge)
        {
            case SplashShade.Edge.Left:
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(spec.Size, 0f);
                break;
            case SplashShade.Edge.Right:
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(spec.Size, 0f);
                break;
            case SplashShade.Edge.Top:
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, spec.Size);
                break;
            default:
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(0f, spec.Size);
                break;
        }

        rect.anchoredPosition = Vector2.zero;
        return spec.Name + (made ? " added" : " reset");
    }
}
