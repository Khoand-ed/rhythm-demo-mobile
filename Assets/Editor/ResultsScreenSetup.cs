using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 结算页 / Rebuilds the gameplay scene's Results panel as a full-screen results screen in the layout of
/// a Muse Dash one, for every canvas from 16:9 (1920x1080) to 21:9 (1920x823) - the Canvas scales to
/// 1920 wide, so only the height changes, and every group here is pinned to the edge it belongs to:
///
///   centre        the operator's splash, behind everything, shaded under the two blocks and the bar
///   top left      RESULT, the song and its difficulty, ACCURACY, MAX COMBO, the four judgements,
///                 EARLY and LATE
///   top right     SCORE, HI-SCORE with NEW BEST, the ranking line, the rewards line and its cells
///   bottom centre the rank in a ring, FULL COMBO / FAILED above it
///   bottom        RESTART on the left, CONTINUE on the right
///
/// 原地改 / Converted in place: the text nodes GameManager already points at are moved into the new
/// groups rather than recreated, so every reference - and the back button's persistent
/// ReturnToSongSelect, which is what Android Back presses - survives. Only what is missing is created.
///
/// 找到就不动 / Find-or-create: a node is placed and styled when it is moved into its group or created,
/// and left alone after that, so a re-run never undoes a hand adjustment. Reset Results Screen To
/// Default places and styles everything again. Either way the panel is put back where it must draw:
/// after the HUD (HPBar, FeverBar, the buttons) and just before Notice.
///
/// Tools/Rhythm/Wire Song Flow and Add Leaderboard And Retry find their nodes anywhere under the panel,
/// and hand anything they add to <see cref="Refit"/>, so the two kinds of tool can run in any order.
/// </summary>
public static class ResultsScreenSetup
{
    private const string UndoName = "Build Results Screen";

    // 版面 / The layout, in canvas units. The two blocks are pinned to the top and the bar to the bottom,
    // and at the shortest canvas (21:9, 823 tall) the blocks still end above the bar's buttons.
    private const float Margin = 72f;
    private const float Top = 40f;
    private const float LeftWidth = 560f;
    private const float LeftHeight = 530f;
    private const float RightWidth = 620f;
    private const float RightHeight = 600f;
    private const float BadgeSize = 240f;
    private const float BadgeBottom = 36f;
    private const float TagsBottom = BadgeBottom + BadgeSize + 12f;
    private const float ButtonWidth = 400f;
    private const float ButtonHeight = 84f;
    private const float ButtonBottom = 40f;
    private const float ArtWidth = 960f;
    private const float ArtFigureHeight = 1.05f;
    private const int Columns = 6;
    private static readonly Vector2 CellSize = new Vector2(96f, 128f);
    private const float CellGap = 8f;

    // 不透明 / Opaque: the project blends in linear space, where even 6% of the white HUD behind
    // (Score, combo, the pause button) reads as a grey ghost through a dark ground.
    private static readonly Color Backdrop = new Color(0.06f, 0.07f, 0.10f, 1f);
    private static readonly Color Caption = new Color(1f, 1f, 1f, 0.70f);
    private static readonly Color Ink = new Color(0.07f, 0.08f, 0.11f, 1f);
    private static readonly Color Gold = new Color(0.96f, 0.73f, 0.05f, 1f);
    private static readonly Color Red = new Color(0.86f, 0.22f, 0.24f, 1f);
    private static readonly Color Line = new Color(1f, 1f, 1f, 0.15f);
    private static readonly Color Well = new Color(0.07f, 0.08f, 0.11f, 0.96f);
    private static readonly Color Plate = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color NoGround = new Color(0.22f, 0.23f, 0.26f, 1f);

    // 和打击反馈同色 / The judgement counts in the colours HitFeedback bursts in, so PERFECT on the results
    // reads as the gold the player just saw on the lane.
    private static readonly Color PerfectInk = new Color(1f, 0.84f, 0.3f, 1f);
    private static readonly Color GreatInk = new Color(0.4f, 0.85f, 1f, 1f);
    private static readonly Color GoodInk = new Color(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color MissInk = new Color(0.92f, 0.35f, 0.35f, 1f);
    private static readonly Color AccuracyInk = new Color(0.55f, 0.95f, 1f, 1f);
    private static readonly Color EarlyInk = new Color(0.45f, 0.75f, 1f, 1f);
    private static readonly Color LateInk = new Color(1f, 0.62f, 0.32f, 1f);
    private static readonly Color DifficultyInk = new Color(0.55f, 0.80f, 1f, 1f);

    [MenuItem("Tools/Rhythm/Build Results Screen")]
    static void Build() => Run(false);

    [MenuItem("Tools/Rhythm/Reset Results Screen To Default")]
    static void ResetToDefault() => Run(true);

    private static void Run(bool reset)
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager == null)
        {
            Debug.LogError("[ResultsScreenSetup] No GameManager in the open scene - open Assets/Scenes/Main.unity first.");
            return;
        }

        if (gameManager.resultsScreen == null)
        {
            Debug.LogError("[ResultsScreenSetup] GameManager.resultsScreen is not assigned, so there is no panel to convert.");
            return;
        }

        List<string> report = Convert(gameManager, reset);

        Debug.Log($"[ResultsScreenSetup] Results screen {(reset ? "reset to default" : "built")} in " +
                  $"{gameManager.gameObject.scene.name}. Save the scene to keep it.\n  " +
                  string.Join("\n  ", report), gameManager.resultsScreen);
    }

    /// <summary>
    /// 给 SongSelectUISetup 用 / For SongSelectUISetup, after it has added a node to the panel: puts it
    /// into its group on a screen already converted, and does nothing to one that is not.
    /// </summary>
    public static string Refit(GameManager gameManager)
    {
        if (gameManager == null || gameManager.resultsScreen == null) return "";
        if (gameManager.resultsScreen.GetComponent<ResultsScreen>() == null) return "";

        List<string> report = Convert(gameManager, false);
        return "results screen refitted (" + report.Count + " notes)";
    }

    // ------------------------------------------------------------------ the pass

    private static List<string> Convert(GameManager gameManager, bool reset)
    {
        Pass pass = new Pass(gameManager, reset);
        pass.Run();
        return pass.Report;
    }

    private sealed class Pass
    {
        public readonly List<string> Report = new List<string>();

        private readonly GameManager gm;
        private readonly bool reset;
        private readonly RectTransform results;
        private readonly TMP_FontAsset font;
        private ResultsScreen screen;

        private readonly List<string> added = new List<string>();
        private readonly List<string> moved = new List<string>();

        public Pass(GameManager gameManager, bool reset)
        {
            gm = gameManager;
            this.reset = reset;
            results = (RectTransform)gameManager.resultsScreen.transform;

            // 跟 HUD 同一套字 / The HUD's own font, so the results read as part of the same screen.
            font = gm.scoreText != null && gm.scoreText.font != null ? gm.scoreText.font
                 : gm.finalScoreText != null && gm.finalScoreText.font != null ? gm.finalScoreText.font
                 : TMP_Settings.defaultFontAsset;
        }

        public void Run()
        {
            Transform canvas = results.parent;
            Undo.RegisterFullObjectHierarchyUndo(canvas != null ? canvas.gameObject : results.gameObject, UndoName);
            Undo.RecordObject(gm, UndoName);

            screen = results.GetComponent<ResultsScreen>();
            bool first = screen == null;
            if (first)
            {
                screen = Undo.AddComponent<ResultsScreen>(results.gameObject);
                added.Add("ResultsScreen component");
            }
            else
            {
                Undo.RecordObject(screen, UndoName);
            }

            if (first || reset) StylePanel();

            Order(canvas);

            BuildArt();
            BuildLeft();
            BuildRight();
            BuildBadge();
            BuildTags();
            BuildBar();

            if (reset || gm.resultsView == null) gm.resultsView = screen;

            Leftovers();

            EditorUtility.SetDirty(screen);
            EditorUtility.SetDirty(gm);
            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);

            Report.Insert(0, added.Count > 0 ? "Added: " + string.Join(", ", added) : "Added: nothing, every piece was there");
            Report.Insert(1, moved.Count > 0 ? "Moved into the layout: " + string.Join(", ", moved) : "Moved: nothing");
            Report.Add($"Wired: GameManager.resultsView, maxComboText ({Name(gm.maxComboText)}), " +
                       $"fullComboText ({Name(gm.fullComboText)}); ResultsScreen's art, song line, EARLY/LATE, " +
                       "ring, tags, HI-SCORE, NEW BEST, rewards line, row and cell template");
        }

        // ------------------------------------------------------------- panel

        private void StylePanel()
        {
            Stretch(results);

            UnityEngine.UI.Image ground = results.GetComponent<UnityEngine.UI.Image>();
            if (ground != null)
            {
                // 挡住身后的游戏 / Still a raycast target on purpose: the results are modal, and a tap on
                // them must not reach the lanes or the pause button behind.
                ground.sprite = null;
                ground.color = Backdrop;
                ground.raycastTarget = true;
            }
        }

        /// <summary>
        /// 画在 HUD 之上, Notice 之下 / The panel draws after everything the HUD has - HPBar and FeverBar
        /// came after it and drew over the results - and just before Notice, which must stay over all.
        /// </summary>
        private void Order(Transform canvas)
        {
            if (canvas == null) return;

            Transform notice = canvas.Find("Notice");
            int want = notice != null ? notice.GetSiblingIndex() - (results.GetSiblingIndex() < notice.GetSiblingIndex() ? 1 : 0)
                                      : canvas.childCount - 1;

            if (results.GetSiblingIndex() != want)
            {
                results.SetSiblingIndex(want);
                moved.Add("Results to sibling " + want + (notice != null ? " (just before Notice)" : " (last)"));
            }

            Report.Add($"Draw order: Results is child {results.GetSiblingIndex()} of {canvas.name}, " +
                       $"after the HUD{(notice != null ? ", before Notice" : "")}");
        }

        // --------------------------------------------------------------- art

        private void BuildArt()
        {
            RectTransform art = Group(results, "OperatorArt", 0, out bool made, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            UnityEngine.UI.Image slot = art.GetComponent<UnityEngine.UI.Image>();
            if (slot == null) slot = art.gameObject.AddComponent<UnityEngine.UI.Image>();

            UI.SplashFrame frame = art.GetComponent<UI.SplashFrame>();
            bool framed = frame == null;
            if (framed) frame = art.gameObject.AddComponent<UI.SplashFrame>();

            if (made || reset)
            {
                art.anchorMin = new Vector2(0.5f, 0f);
                art.anchorMax = new Vector2(0.5f, 1f);
                art.pivot = new Vector2(0.5f, 0.5f);
                art.anchoredPosition = Vector2.zero;
                art.sizeDelta = new Vector2(ArtWidth, 0f);
            }

            if (framed || reset)
            {
                frame.mode = UI.SplashFrame.Mode.Figure;
                frame.figureHeight = ArtFigureHeight;
                frame.faceWidth = UI.SplashFrame.DefaultFaceWidth;
                frame.faceTop = UI.SplashFrame.DefaultFaceTop;
            }

            // 框只是框 / The frame's own Image draws nothing and catches nothing - the splash is its child.
            slot.sprite = null;
            slot.color = Color.clear;
            slot.raycastTarget = false;

            Transform child = art.Find("Splash");
            UnityEngine.UI.Image splash = child != null ? child.GetComponent<UnityEngine.UI.Image>() : null;
            if (splash == null)
            {
                RectTransform rect = NewNode("Splash", art, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                rect.SetAsFirstSibling();
                splash = rect.GetComponent<UnityEngine.UI.Image>();
                splash.color = Color.white;
                splash.raycastTarget = false;
                splash.enabled = false;
                added.Add("OperatorArt/Splash");
            }

            frame.splash = splash;
            if (reset || screen.operatorArt == null) screen.operatorArt = slot;

            // 只在字下面压暗 / Dark under the two blocks and the bar, the operator clear between them.
            Shade("ShadeLeft", 1, UI.SplashShade.Edge.Left, 820f, 0.9f, 0.5f);
            Shade("ShadeRight", 2, UI.SplashShade.Edge.Right, 820f, 0.9f, 0.5f);
            Shade("ShadeBottom", 3, UI.SplashShade.Edge.Bottom, 340f, 0.85f, 0.25f);
        }

        private void Shade(string name, int index, UI.SplashShade.Edge edge, float size, float strength, float hold)
        {
            Transform found = results.Find(name);
            UI.SplashShade shade = found != null ? found.GetComponent<UI.SplashShade>() : null;

            if (found != null && shade == null)
            {
                Report.Add($"{name}: SKIPPED, a node of that name is not a SplashShade");
                return;
            }

            bool made = shade == null;
            if (made)
            {
                RectTransform rect = NewNode(name, results, typeof(CanvasRenderer), typeof(UI.SplashShade));
                rect.SetSiblingIndex(index);
                shade = rect.GetComponent<UI.SplashShade>();
                added.Add(name);
            }

            if (!made && !reset) return;

            if (reset) shade.transform.SetSiblingIndex(index);

            shade.color = Color.black;
            shade.raycastTarget = false;
            shade.darkest = edge;
            shade.strength = strength;
            shade.hold = hold;

            RectTransform r = shade.rectTransform;
            switch (edge)
            {
                case UI.SplashShade.Edge.Left: Place(r, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(size, 0f)); break;
                case UI.SplashShade.Edge.Right: Place(r, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(size, 0f)); break;
                case UI.SplashShade.Edge.Top: Place(r, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, size)); break;
                default: Place(r, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, size)); break;
            }
        }

        // -------------------------------------------------------------- left

        private void BuildLeft()
        {
            RectTransform left = Block("LeftBlock", 4, new Vector2(0f, 1f), new Vector2(Margin, -Top),
                                       new Vector2(LeftWidth, LeftHeight), TextAnchor.UpperLeft, 5f);
            int i = 0;

            TextMeshProUGUI title = Text(left, "Title Text", null, i++, out bool place);
            if (place && title != null)
            {
                Style(title, "RESULT", 26f, Caption, TextAlignmentOptions.Left, true);
                title.characterSpacing = 8f;
                Fixed(title.rectTransform, 32f);
            }

            TextMeshProUGUI song = Text(left, "SongName", screen.songNameText, i++, out place);
            if (place && song != null)
            {
                Style(song, "", 46f, Color.white, TextAlignmentOptions.Left, true);
                Fixed(song.rectTransform, 58f);
            }
            if (song != null && (reset || screen.songNameText == null)) screen.songNameText = song;

            TextMeshProUGUI difficulty = Text(left, "Difficulty", screen.difficultyText, i++, out place);
            if (place && difficulty != null)
            {
                Style(difficulty, "", 24f, DifficultyInk, TextAlignmentOptions.Left, true);
                Fixed(difficulty.rectTransform, 34f);
            }
            if (difficulty != null && (reset || screen.difficultyText == null)) screen.difficultyText = difficulty;

            gm.percentHitText = Pair(left, "AccuracyRow", i++, 60f, "Percent Hit", "ACCURACY", "Percent Hit Value", gm.percentHitText, 46f, AccuracyInk);
            gm.maxComboText = Pair(left, "MaxComboRow", i++, 44f, "Max Combo", "MAX COMBO", "Max Combo Value", gm.maxComboText, 32f, Color.white);

            RectTransform divider = Group(left, "Divider", i++, out bool made, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            if (made || reset)
            {
                UnityEngine.UI.Image rule = divider.GetComponent<UnityEngine.UI.Image>();
                if (rule != null)
                {
                    rule.color = Line;
                    rule.raycastTarget = false;
                }
                Fixed(divider, 2f);
            }

            // 只改看得见的字 / Only the captions change: the fields keep their names, so goodsText is still
            // the Great count and normalsText the Hit count.
            gm.perfectsText = Pair(left, "PerfectRow", i++, 40f, "Perfect Hits", "PERFECT", "Perfect Hits Value", gm.perfectsText, 30f, PerfectInk);
            gm.goodsText = Pair(left, "GreatRow", i++, 40f, "Good Hits", "GREAT", "Good Hits Value", gm.goodsText, 30f, GreatInk);
            gm.normalsText = Pair(left, "GoodRow", i++, 40f, "Normal Hits", "GOOD", "Normal Hits Value", gm.normalsText, 30f, GoodInk);
            gm.missesText = Pair(left, "MissRow", i++, 40f, "Missed Hits", "MISS", "Missed Hits Value", gm.missesText, 30f, MissInk);

            TextMeshProUGUI early = Pair(left, "EarlyRow", i++, 36f, "Early", "EARLY", "Early Value", screen.earlyText, 26f, EarlyInk);
            if (early != null && (reset || screen.earlyText == null)) screen.earlyText = early;

            TextMeshProUGUI late = Pair(left, "LateRow", i++, 36f, "Late", "LATE", "Late Value", screen.lateText, 26f, LateInk);
            if (late != null && (reset || screen.lateText == null)) screen.lateText = late;
        }

        /// <summary>
        /// A caption on the left of a row and its number on the right. Returns the value, so the field
        /// that pointed at it keeps pointing at it - or at the new one, when it had to be created.
        /// </summary>
        private TextMeshProUGUI Pair(RectTransform block, string rowName, int index, float height,
                                     string labelName, string caption, string valueName,
                                     TextMeshProUGUI known, float valueSize, Color valueInk)
        {
            RectTransform row = Group(block, rowName, index, out bool made);
            if (made || reset) Fixed(row, height);

            TextMeshProUGUI label = Text(row, labelName, null, -1, out bool place);
            if (place && label != null)
            {
                Style(label, caption, 26f, Caption, TextAlignmentOptions.Left, false);
                Place(label.rectTransform, Vector2.zero, new Vector2(0.62f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            }

            TextMeshProUGUI value = Text(row, valueName, known, -1, out place);
            if (place && value != null)
            {
                Style(value, null, valueSize, valueInk, TextAlignmentOptions.Right, true);
                Place(value.rectTransform, new Vector2(0.38f, 0f), Vector2.one, new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
            }

            return value != null ? value : known;
        }

        // ------------------------------------------------------------- right

        private void BuildRight()
        {
            RectTransform right = Block("RightBlock", 5, new Vector2(1f, 1f), new Vector2(-Margin, -Top),
                                        new Vector2(RightWidth, RightHeight), TextAnchor.UpperRight, 6f);
            int i = 0;

            // 'Final Score' 原来挂在 'Rank Value' 下面 / The caption used to hang under 'Rank Value' by
            // mistake; it moves here, beside its number.
            TextMeshProUGUI caption = Text(right, "Final Score", null, i++, out bool place);
            if (place && caption != null)
            {
                Style(caption, "SCORE", 26f, Caption, TextAlignmentOptions.Right, true);
                caption.characterSpacing = 8f;
                Fixed(caption.rectTransform, 30f);
            }

            TextMeshProUGUI score = Text(right, "Final Score Value", gm.finalScoreText, i++, out place);
            if (place && score != null)
            {
                Style(score, null, 88f, Color.white, TextAlignmentOptions.Right, true);
                Fixed(score.rectTransform, 110f);

                // 行高放得下才画 / A line taller than its box is dropped whole under Ellipsis - at 92 in a
                // 100-high row the score drew nothing at all. The row is taller than the font's line, and
                // a number too wide runs over rather than vanishing.
                score.overflowMode = TextOverflowModes.Overflow;

                // 白字不要白描边 / Its white Outline was there to lift black text off grey; on white text
                // it only blurs the number.
                Outline outline = score.GetComponent<Outline>();
                if (outline != null) outline.enabled = false;
            }
            if (score != null) gm.finalScoreText = score;

            RectTransform hiRow = Group(right, "HiScoreRow", i++, out bool made);
            if (made || reset) Fixed(hiRow, 40f);

            TextMeshProUGUI hi = Text(hiRow, "HiScoreText", screen.hiScoreText, -1, out place);
            if (place && hi != null)
            {
                Style(hi, "<color=#FFFFFF8C>HI-SCORE</color>   " + ResultsScreen.Dash, 28f, Color.white, TextAlignmentOptions.Right, true);
                Stretch(hi.rectTransform);
            }
            if (hi != null && (reset || screen.hiScoreText == null)) screen.hiScoreText = hi;

            RectTransform best = Tag(hiRow, "NewBestTag", "NEW BEST", Gold, Ink, new Vector2(170f, 36f));
            if (best != null)
            {
                if (reset || screen.newBestTag == null) screen.newBestTag = best.gameObject;
            }

            TextMeshProUGUI ranking = Text(right, "RankingText", gm.rankingText, i++, out place);
            if (place && ranking != null)
            {
                Style(ranking, null, 22f, new Color(1f, 1f, 1f, 0.9f), TextAlignmentOptions.Right, true);
                Fixed(ranking.rectTransform, 32f);
            }
            if (ranking != null) gm.rankingText = ranking;

            RectTransform gap = Group(right, "RewardsGap", i++, out made);
            if (made || reset) Fixed(gap, 10f);

            TextMeshProUGUI rewards = Text(right, "RewardsText", gm.rewardsText, i++, out place);
            if (place && rewards != null)
            {
                Style(rewards, null, 22f, Color.white, TextAlignmentOptions.TopRight, false);
                rewards.textWrappingMode = TextWrappingModes.Normal;
                rewards.overflowMode = TextOverflowModes.Truncate;
                Fixed(rewards.rectTransform, 60f);
            }
            if (rewards != null) gm.rewardsText = rewards;
            if (rewards != null && (reset || screen.rewardsLine == null)) screen.rewardsLine = rewards;

            BuildRow(right, i++);
        }

        private void BuildRow(RectTransform right, int index)
        {
            RectTransform row = Group(right, "RewardsRow", index, out bool made);

            GridLayoutGroup grid = row.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                grid = row.gameObject.AddComponent<GridLayoutGroup>();
                made = true;
            }

            if (made || reset)
            {
                // 靠右, 两行六格 / Right-aligned under the score, six to a row, at most two rows
                // (ResultsScreen.MaxCells).
                grid.cellSize = CellSize;
                grid.spacing = new Vector2(CellGap, CellGap);
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment = TextAnchor.UpperRight;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = Columns;
            }

            if (reset || screen.rewardsRow == null) screen.rewardsRow = row;

            Transform template = row.Find("CellTemplate");
            if (template != null && reset)
            {
                Undo.DestroyObjectImmediate(template.gameObject);
                template = null;
            }

            if (template == null)
            {
                template = BuildCell(row);
                added.Add("RewardsRow/CellTemplate");
            }

            if (reset || screen.cellTemplate == null) screen.cellTemplate = (RectTransform)template;
        }

        /// <summary>
        /// 一格 / One reward: the rarity ground with the icon on it, "×amount" along its foot, a FIRST tag
        /// for what a first clear paid, and the name under it. ResultsScreen.AddCell binds these names.
        /// </summary>
        private RectTransform BuildCell(RectTransform row)
        {
            RectTransform cell = NewNode("CellTemplate", row);
            cell.sizeDelta = CellSize;

            RectTransform groundRect = NewNode("Ground", cell, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            Place(groundRect, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, CellSize.x));
            UnityEngine.UI.Image ground = groundRect.GetComponent<UnityEngine.UI.Image>();
            ground.color = NoGround;
            ground.raycastTarget = false;

            RectTransform iconRect = NewNode("Icon", groundRect, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            Stretch(iconRect);
            iconRect.offsetMin = new Vector2(8f, 8f);
            iconRect.offsetMax = new Vector2(-8f, -8f);
            UnityEngine.UI.Image icon = iconRect.GetComponent<UnityEngine.UI.Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            RectTransform plateRect = NewNode("AmountPlate", groundRect, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            Place(plateRect, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 24f));
            UnityEngine.UI.Image plate = plateRect.GetComponent<UnityEngine.UI.Image>();
            plate.color = Plate;
            plate.raycastTarget = false;

            TextMeshProUGUI amount = NewText("Amount", groundRect);
            Style(amount, "×0", 18f, Color.white, TextAlignmentOptions.Right, true);
            Place(amount.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(-12f, 24f));

            Tag(groundRect, "FirstTag", "FIRST", Gold, Ink, new Vector2(46f, 20f), 12f, new Vector2(0f, 1f));

            TextMeshProUGUI name = NewText("Name", cell);
            Style(name, "", 15f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center, false);
            Place(name.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, CellSize.y - CellSize.x));

            cell.gameObject.SetActive(false);
            return cell;
        }

        // ------------------------------------------------------------- badge

        private void BuildBadge()
        {
            RectTransform badge = Group(results, "RankBadge", 6, out bool made);
            if (made || reset)
            {
                Place(badge, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                      new Vector2(0f, BadgeBottom), new Vector2(BadgeSize, BadgeSize));
            }

            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            UnityEngine.UI.Image ring = Disc(badge, "Ring", 0, knob, Gold, 0f);
            if (ring != null && (reset || screen.rankRing == null)) screen.rankRing = ring;
            Disc(badge, "Inner", 1, knob, Well, 14f);

            TextMeshProUGUI caption = Text(badge, "Rank", null, 2, out bool place);
            if (place && caption != null)
            {
                Style(caption, "RANK", 20f, Caption, TextAlignmentOptions.Center, true);
                caption.characterSpacing = 6f;
                Place(caption.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(0f, 28f));
            }

            TextMeshProUGUI letter = Text(badge, "Rank Value", gm.rankText, 3, out place);
            if (place && letter != null)
            {
                Style(letter, null, 140f, Color.white, TextAlignmentOptions.Center, true);
                Stretch(letter.rectTransform);
                letter.rectTransform.offsetMin = new Vector2(0f, 16f);
                letter.rectTransform.offsetMax = new Vector2(0f, -40f);
            }
            if (letter != null) gm.rankText = letter;
        }

        private UnityEngine.UI.Image Disc(RectTransform badge, string name, int index, Sprite sprite, Color colour, float inset)
        {
            RectTransform rect = Group(badge, name, index, out bool made, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            UnityEngine.UI.Image disc = rect.GetComponent<UnityEngine.UI.Image>();
            if (disc == null) return null;

            if (made || reset)
            {
                Stretch(rect);
                rect.offsetMin = new Vector2(inset, inset);
                rect.offsetMax = new Vector2(-inset, -inset);
                disc.sprite = sprite;
                disc.type = UnityEngine.UI.Image.Type.Simple;
                disc.preserveAspect = true;
                disc.color = colour;
                disc.raycastTarget = false;
            }

            return disc;
        }

        // -------------------------------------------------------------- tags

        private void BuildTags()
        {
            RectTransform tags = Group(results, "Tags", 7, out bool made);

            HorizontalLayoutGroup row = tags.GetComponent<HorizontalLayoutGroup>();
            if (row == null)
            {
                row = tags.gameObject.AddComponent<HorizontalLayoutGroup>();
                made = true;
            }

            if (made || reset)
            {
                Place(tags, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                      new Vector2(0f, TagsBottom), new Vector2(480f, 44f));

                // 只显示开着的 / A switched-off tag takes no room, so whichever is shown sits centred.
                row.spacing = 12f;
                row.childAlignment = TextAnchor.MiddleCenter;
                row.childControlWidth = false;
                row.childControlHeight = false;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
            }

            // FULL COMBO 的字就是 GameManager.fullComboText / The FULL COMBO tag's words are
            // GameManager.fullComboText, which writes them only on a full combo.
            RectTransform full = TagPlate(tags, "FullComboTag", 0, Gold, new Vector2(230f, 44f), out bool fullMade);
            if (full != null)
            {
                TextMeshProUGUI words = Text(full, "Full Combo Text", gm.fullComboText, -1, out bool place);
                if (place && words != null)
                {
                    Style(words, "FULL COMBO", 24f, Ink, TextAlignmentOptions.Center, true);
                    Stretch(words.rectTransform);
                }
                if (words != null && (reset || gm.fullComboText == null)) gm.fullComboText = words;
                if (reset || screen.fullComboTag == null) screen.fullComboTag = full.gameObject;
                if (fullMade) full.gameObject.SetActive(false);
            }

            RectTransform failed = TagPlate(tags, "FailedTag", 1, Red, new Vector2(160f, 44f), out bool failedMade);
            if (failed != null)
            {
                Transform label = failed.Find("Label");
                if (label == null || reset)
                {
                    TextMeshProUGUI words = label != null ? label.GetComponent<TextMeshProUGUI>() : NewText("Label", failed);
                    if (words != null)
                    {
                        Style(words, "FAILED", 24f, Color.white, TextAlignmentOptions.Center, true);
                        Stretch(words.rectTransform);
                    }
                }
                if (reset || screen.failedTag == null) screen.failedTag = failed.gameObject;
                if (failedMade) failed.gameObject.SetActive(false);
            }
        }

        // --------------------------------------------------------------- bar

        private void BuildBar()
        {
            RectTransform bar = Group(results, "BottomBar", 8, out bool made);
            if (made || reset)
            {
                Place(bar, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero,
                      new Vector2(0f, ButtonBottom * 2f + ButtonHeight));
            }

            // 左边 RESTART, 右边 CONTINUE / The existing RETRY and back buttons, relabelled: RETRY keeps
            // RetryFromResults and the sanity cost GameManager writes on it, the back button keeps its
            // persistent ReturnToSongSelect - the one PauseMenu finds for Android Back.
            Transform retry = gm.resultsRetryButton != null ? gm.resultsRetryButton.transform : FindDeep(results, "RetryButton");
            if (retry == null)
            {
                Report.Add("RetryButton: MISSING - run Tools/Rhythm/Add Leaderboard And Retry, which adds it and puts it in the bar");
            }
            else if (Into(retry, bar, -1))
            {
                PlaceButton(retry, new Vector2(0f, 0f), new Vector2(Margin, ButtonBottom), "RESTART", gm.resultsRetryLabel);
            }

            Transform back = FindDeep(results, "BackButton");
            if (back == null)
            {
                Report.Add("BackButton: MISSING - run Tools/Rhythm/Wire Song Flow, which adds it and puts it in the bar");
            }
            else if (Into(back, bar, -1))
            {
                PlaceButton(back, new Vector2(1f, 0f), new Vector2(-Margin, ButtonBottom), "CONTINUE", null);
            }
        }

        private void PlaceButton(Transform node, Vector2 corner, Vector2 position, string caption, TextMeshProUGUI label)
        {
            Place((RectTransform)node, corner, corner, corner, position, new Vector2(ButtonWidth, ButtonHeight));

            if (label == null) label = node.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null) return;

            Style(label, caption, 28f, Color.white, TextAlignmentOptions.Center, true);
            Stretch(label.rectTransform);
        }

        // ------------------------------------------------------------ report

        private void Leftovers()
        {
            var loose = new List<string>();
            string[] ours = { "OperatorArt", "ShadeLeft", "ShadeRight", "ShadeBottom", "LeftBlock", "RightBlock", "RankBadge", "Tags", "BottomBar" };

            for (int i = 0; i < results.childCount; i++)
            {
                string name = results.GetChild(i).name;
                if (System.Array.IndexOf(ours, name) < 0) loose.Add(name);
            }

            if (loose.Count > 0) Report.Add("Left where they were (not part of this layout): " + string.Join(", ", loose));
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// A block pinned to a top corner, laying its children out top-down. Created and placed once;
        /// after that only a reset moves it.
        /// </summary>
        private RectTransform Block(string name, int index, Vector2 corner, Vector2 position, Vector2 size,
                                    TextAnchor alignment, float spacing)
        {
            RectTransform block = Group(results, name, index, out bool made);

            VerticalLayoutGroup layout = block.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
                made = true;
            }

            if (made || reset)
            {
                Place(block, corner, corner, corner, position, size);
                layout.spacing = spacing;
                layout.childAlignment = alignment;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.padding = new RectOffset(0, 0, 0, 0);
            }

            return block;
        }

        /// <summary>A direct child by name, created at <paramref name="index"/> when missing.</summary>
        private RectTransform Group(RectTransform parent, string name, int index, out bool made, params System.Type[] components)
        {
            Transform found = parent.Find(name);
            made = found == null;

            if (made)
            {
                RectTransform rect = NewNode(name, parent, components);
                if (index >= 0) rect.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
                added.Add(parent == results ? name : parent.name + "/" + name);
                return rect;
            }

            if (reset && index >= 0) found.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
            return (RectTransform)found;
        }

        /// <summary>
        /// A text node: the one a field already points at, else the one of that name anywhere under the
        /// panel, else a new one. <paramref name="place"/> says whether to place and style it now - it was
        /// just moved here or created, or this is a reset.
        /// </summary>
        private TextMeshProUGUI Text(RectTransform parent, string name, TextMeshProUGUI known, int index, out bool place)
        {
            Transform node = known != null ? known.transform : FindDeep(results, name);

            if (node == null)
            {
                TextMeshProUGUI made = NewText(name, parent);
                if (index >= 0) made.transform.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
                added.Add(parent.name + "/" + name);
                place = true;
                return made;
            }

            TextMeshProUGUI text = node.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                Report.Add($"{name}: SKIPPED, it is not a TextMeshProUGUI");
                place = false;
                return null;
            }

            place = Into(node, parent, index);
            return text;
        }

        /// <summary>Moves a node under <paramref name="parent"/> if it is not there. True when it should be placed.</summary>
        private bool Into(Transform node, RectTransform parent, int index)
        {
            bool moving = node.parent != parent;

            if (moving)
            {
                node.SetParent(parent, false);
                moved.Add(node.name + " -> " + parent.name);
            }

            if ((moving || reset) && index >= 0) node.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
            return moving || reset;
        }

        /// <summary>A coloured plate with words on it, made once - NEW BEST, FIRST.</summary>
        private RectTransform Tag(RectTransform parent, string name, string words, Color ground, Color ink, Vector2 size,
                                  float fontSize = 20f, Vector2? corner = null)
        {
            Vector2 at = corner ?? new Vector2(0f, 0.5f);
            Transform found = parent.Find(name);
            bool made = found == null;
            if (!made && !reset) return (RectTransform)found;

            RectTransform rect = made ? NewNode(name, parent, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image)) : (RectTransform)found;
            if (made && parent != results && parent.name != "Ground") added.Add(parent.name + "/" + name);

            Place(rect, at, at, at, Vector2.zero, size);

            UnityEngine.UI.Image plate = rect.GetComponent<UnityEngine.UI.Image>();
            if (plate != null) Rounded(plate, ground);

            Transform label = rect.Find("Label");
            TextMeshProUGUI text = label != null ? label.GetComponent<TextMeshProUGUI>() : NewText("Label", rect);
            if (text != null)
            {
                Style(text, words, fontSize, ink, TextAlignmentOptions.Center, true);
                Stretch(text.rectTransform);
            }

            if (made) rect.gameObject.SetActive(false);
            return rect;
        }

        /// <summary>A coloured plate inside the tags row (sized, not anchored - the row lays it out).</summary>
        private RectTransform TagPlate(RectTransform parent, string name, int index, Color ground, Vector2 size, out bool made)
        {
            RectTransform rect = Group(parent, name, index, out made, typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            if (made || reset)
            {
                rect.sizeDelta = size;
                UnityEngine.UI.Image plate = rect.GetComponent<UnityEngine.UI.Image>();
                if (plate != null) Rounded(plate, ground);
            }

            return rect;
        }

        private static void Rounded(UnityEngine.UI.Image plate, Color colour)
        {
            plate.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            plate.type = UnityEngine.UI.Image.Type.Sliced;
            plate.color = colour;
            plate.raycastTarget = false;
        }

        private RectTransform NewNode(string name, Transform parent, params System.Type[] components)
        {
            var types = new List<System.Type> { typeof(RectTransform) };
            types.AddRange(components);

            GameObject go = new GameObject(name, types.ToArray());
            go.layer = results.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(go, UndoName);

            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private TextMeshProUGUI NewText(string name, Transform parent)
        {
            TextMeshProUGUI text = NewNode(name, parent, typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.text = "";
            return text;
        }

        /// <summary>One look for every word on the screen: the HUD's font, one line, no auto-size.</summary>
        private void Style(TextMeshProUGUI text, string words, float size, Color colour, TextAlignmentOptions alignment, bool bold)
        {
            if (text == null) return;

            if (font != null) text.font = font;
            if (words != null) text.text = words;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.color = colour;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.characterSpacing = 0f;
            text.margin = Vector4.zero;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = true;
            text.raycastTarget = false;
        }

        private static string Name(Component component) => component != null ? component.name : "unassigned";
    }

    // ---------------------------------------------------------------- shared

    /// <summary>
    /// 固定高度 / A fixed height in a vertical layout: preferred and min the same, never stretched.
    /// </summary>
    private static void Fixed(RectTransform rect, float height)
    {
        LayoutElement element = rect.GetComponent<LayoutElement>();
        if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                              Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>The first node of that name under <paramref name="root"/>, depth first; the root itself counts.</summary>
    public static Transform FindDeep(Transform root, string name)
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
