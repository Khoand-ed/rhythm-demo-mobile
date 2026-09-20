using System.Collections.Generic;
using Data.Char;
using Data.Player;
using DG.Tweening;
using Manager;
using Tools;
using TMPro;
using UI;
using UI.Sub;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The step between picking a song and playing it: the roster down the left, the
// chosen operator in the middle, their rhythm stats and passive on the right.
//
// Deliberately outside Assets/Arknights, for the same reason SongSelectUI is. It
// needs UIBase, CommonDialogUI and CharData (Arknights assembly), SongChart
// (Rhythm.Core) and SongSession (default assembly) at once, and an asmdef can
// never reference the default assembly - only the other way round.
//
// 显示的是音游数值 / What it shows is the rhythm stat block - max HP, the score and
// fever modifiers, and the one passive. CharMeta also carries an inherited
// tower-defense block (ATK, DEF, block, DP cost) which CharInfoUI and the Lua
// roster still read; none of it means anything in a rhythm run, so none of it is
// drawn here.
//
// 节点名字是绑定的一部分 / The node paths CharCell looks up are load-bearing.
// CharSelectUISetup builds exactly these names, and renaming one there breaks the
// binding here with nothing but a null reference to explain it.
public class CharSelectUI : UIBase
{
    // UIManager keys every screen by its prefab name. UIBase.Name holds the same
    // string but is internal to the Arknights assembly, so this file keeps its own.
    public const string UIName = "CharSelectUI";

    [Header("网格 / Roster grid")]
    public RectTransform cellTemplate;
    public TextMeshProUGUI rosterCount;

    [Header("立绘 / Portrait")]
    public Image portrait;
    public GameObject namePlate;
    public TextMeshProUGUI portraitName;
    public Image portraitStars;
    public Image portraitProfession;
    public Image rarityRule;
    public GameObject emptyState;

    [Header("属性 / Rhythm stats")]
    public TextMeshProUGUI hpValue;
    public TextMeshProUGUI scoreValue;
    public TextMeshProUGUI feverValue;

    [Header("被动 / Passive")]
    public GameObject skillBlock;
    public Image skillIcon;
    public TextMeshProUGUI skillName;
    public TextMeshProUGUI skillDescription;

    [Header("框架 / Frame")]
    public Button backButton;
    public Button playButton;
    public Image playFace;
    public TextMeshProUGUI playLabel;
    public TextMeshProUGUI queueLabel;

    private static readonly Color PlayArmed = new Color(0.17f, 0.56f, 0.90f, 1f);
    private static readonly Color PlayDormant = new Color(0.22f, 0.24f, 0.27f, 0.95f);
    private static readonly Color LabelDim = new Color(1f, 1f, 1f, 0.55f);

    private SongChart chart;
    private CharData selected;
    private readonly List<CharCell> cells = new List<CharCell>();

    // Latched the moment PLAY is accepted, so a second tap during the 0.6s fade
    // cannot start the scene load twice.
    private bool leaving;

    /// <summary>
    /// 从选曲界面进来 / Opened by SongSelectUI once a chart has been picked.
    /// </summary>
    public static CharSelectUI Show(SongChart chart)
    {
        CharSelectUI ui = UIManager.Inst().Show(UIName) as CharSelectUI;

        if (ui == null)
        {
            // UIManager already logged which prefab it could not find; say what fixes it.
            Debug.LogError($"[CharSelectUI] Cannot open '{UIName}'. Run " +
                           "Arknights/Character/Build Char Select UI to generate the prefab.");
            return null;
        }

        ui.chart = chart;
        ui.selected = null;
        ui.leaving = false;

        // UIManager.Show already ran UpdateView once, before chart was set. Draw
        // again now that there is something to draw.
        ui.UpdateView();
        return ui;
    }

    public override void Init()
    {
        backButton.onClick.AddListener(() => HideAndDestroy(UIName));
        playButton.onClick.AddListener(Play);
    }

    /// <summary>
    /// 只画, 不发 / Draws, and hands over nothing. Every claim on this screen is a press.
    ///
    /// 可能在 chart 还没设置时就被调用 / UIManager.Show calls this before the static Show has
    /// assigned the chart, so it has to survive a null one rather than assume it is there.
    /// </summary>
    public override void UpdateView()
    {
        queueLabel.text = chart == null
            ? ""
            : $"{chart.songName}   ·   {chart.DifficultyLabel} LV.{chart.difficulty}";

        BuildGrid();
        Draw();
    }

    // ------------------------------------------------------------------- roster

    private void BuildGrid()
    {
        PlayerData player = PlayerManager.Inst().Get();
        List<CharData> roster = player != null ? player.GetCharList() : null;
        int count = roster != null ? roster.Count : 0;

        rosterCount.text = count.ToString();

        // 行是复用的 / Cells are pooled rather than rebuilt, the way MissionUI does it: a
        // redraw is a repaint of the same objects, not a churn of the hierarchy.
        while (cells.Count < count)
        {
            cells.Add(new CharCell(Instantiate(cellTemplate, cellTemplate.parent)));
        }

        for (int i = 0; i < cells.Count; i++)
        {
            bool used = i < count;
            cells[i].SetActive(used);
            if (!used) continue;

            // 捕获副本 / A local copy, because the listener below outlives this iteration.
            CharData data = roster[i];
            cells[i].Draw(data, data == selected, () => Select(data));
        }
    }

    private void Select(CharData data)
    {
        selected = data;

        for (int i = 0; i < cells.Count; i++) cells[i].SetSelected(cells[i].Holds(data));

        Draw();
    }

    // ------------------------------------------------------------------ drawing

    private void Draw()
    {
        CharMeta meta = selected != null ? SafeMeta(selected) : null;
        bool armed = meta != null;

        emptyState.SetActive(!armed);
        portrait.gameObject.SetActive(armed);
        skillBlock.SetActive(false);

        // 整块名牌一起藏 / The whole name plate goes, not just its text. An Image with a null
        // sprite still draws a solid quad, so blanking portraitStars and portraitProfession would
        // leave two coloured boxes sitting under an empty portrait.
        namePlate.SetActive(armed);

        if (armed)
        {
            portrait.sprite = meta.GetImage();
            portraitName.text = meta.GetEnglishName();
            portraitStars.sprite = SafeSprite(() => CharManager.Inst().GetStarImage("info_" + meta.GetRarity()));
            portraitProfession.sprite = SafeSprite(() => CharManager.Inst().GetProfessionSmallImage(meta.GetProfession()));
            rarityRule.color = RarityTint(meta.GetRarity());

            hpValue.text = meta.GetMaxHp().ToString();
            scoreValue.text = $"x{meta.GetScoreModifier():0.00}";
            feverValue.text = $"x{meta.GetFeverModifier():0.00}";

            // 只有一个被动, 没有占位槽 / Exactly one passive and no empty slot: the block is
            // drawn when there is something in it and hidden when there is not.
            CharPassive passive = meta.GetPassive();
            if (passive != null && !passive.IsEmpty())
            {
                skillBlock.SetActive(true);
                skillIcon.sprite = passive.GetIcon();
                skillIcon.color = RarityTint(meta.GetRarity());
                skillName.text = passive.GetName();
                skillDescription.text = passive.GetDescription();
            }
        }
        else
        {
            hpValue.text = "--";
            scoreValue.text = "--";
            feverValue.text = "--";
        }

        playFace.color = armed ? PlayArmed : PlayDormant;
        playLabel.color = armed ? Color.white : LabelDim;
        playLabel.text = armed ? "PLAY" : "SELECT AN OPERATOR";
    }

    private static Color RarityTint(int rarity)
    {
        switch (rarity)
        {
            case 6: return new Color(0.96f, 0.55f, 0.20f);
            case 5: return new Color(0.95f, 0.80f, 0.25f);
            case 4: return new Color(0.63f, 0.50f, 0.82f);
            case 3: return new Color(0.33f, 0.62f, 0.86f);
            case 2: return new Color(0.55f, 0.76f, 0.36f);
            default: return new Color(0.62f, 0.64f, 0.67f);
        }
    }

    // -------------------------------------------------------------------- play

    /// <summary>
    /// 开始游戏 / The only place SongSession is written.
    ///
    /// 按钮永远可按, 是外观装成不可按 / playButton.interactable stays true at all times, and the
    /// dormant look is painted on by Draw(). That is deliberate, and it is the only way to have
    /// both halves of the requirement: Button.OnPointerClick returns before firing onClick when
    /// IsInteractable() is false, so a genuinely disabled button physically cannot explain itself.
    /// Do not "fix" this by setting interactable = false - the popup below is what goes away.
    /// </summary>
    private void Play()
    {
        if (leaving) return;

        if (selected == null)
        {
            CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE,
                                   "Pick an operator before you start.");
            return;
        }

        if (chart == null)
        {
            Debug.LogError("[CharSelectUI] No chart to start; the screen was opened without one.");
            return;
        }

        leaving = true;

        SongSession.Set(chart);
        SongSession.SetCharacter(selected);

        // SoundManager is DontDestroyOnLoad, so the front-end's music would keep
        // playing underneath the song. HomeUI starts it again on the way back,
        // because its Lua show() is what plays it.
        SoundManager.Inst().StopMusic();

        // The song map is hidden rather than destroyed - it is worth keeping, and
        // HomeUI has to still be underneath for Back to land on. This screen is
        // destroyed, so the next entry rebuilds it with no stale selection.
        UIManager.Inst().Hide(SongSelectUI.UIName);
        HideAndDestroy(UIName);

        // The UI camera and canvas are DontDestroyOnLoad, so they survive into the
        // gameplay scene and would draw the front-end over the song. Switching the
        // camera off takes the whole front-end out of the way at once; GameStart
        // switches it back on when the player returns.
        //
        // 这个闭包不碰实例成员 / The lambda touches no instance member, so destroying
        // this screen while it is pending is safe.
        SongChart starting = chart;
        string operatorId = selected.GetId();

        Delay.add(() =>
        {
            GameObject uiCamera = UIManager.Inst().GetCamera();
            if (uiCamera != null) uiCamera.SetActive(false);

            SceneManager.LoadScene(SongSelectUI.GameplayScene);
        }, 0.6f);

        Debug.Log($"Starting {starting.stageId} ({starting.name}) with {operatorId}.");
    }

    // ------------------------------------------------------------------- frame

    public override void Show()
    {
        base.Show();
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1, 0.3f);
    }

    public override void Hide(bool destroy = false)
    {
        canvasGroup.DOFade(0, 0.2f).OnComplete(() => base.Hide(destroy));
    }

    // ------------------------------------------------------------------ safety

    /// <summary>
    /// 取元数据不能炸 / Resolves an operator's metadata without letting a failure reach the caller.
    ///
    /// 不只是返回 null / A missing meta does not merely come back null: CharData.GetCharMeta goes
    /// through Asset.Load, and ABManager throws outright when the bundle behind it is missing or
    /// already held open. Missing bundles are a normal state in this project.
    /// </summary>
    private static CharMeta SafeMeta(CharData data)
    {
        try
        {
            return data.GetCharMeta();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CharSelectUI] No metadata for operator {data.GetId()}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 取装饰图不能炸 / CharManager's sprite getters index their dictionaries with no ContainsKey
    /// check, so a missing star or profession glyph is a KeyNotFoundException that would take the
    /// whole screen down rather than leaving one icon blank.
    /// </summary>
    private static Sprite SafeSprite(System.Func<Sprite> get)
    {
        try
        {
            return get();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CharSelectUI] Missing chrome sprite, drawing without it: {e.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------- cells

    /// <summary>一个干员格子 / One roster cell: avatar, name, rarity and profession.</summary>
    private class CharCell
    {
        private readonly GameObject root;
        private readonly Button button;
        private readonly Image ground;
        private readonly Image avatar;
        private readonly Image profession;
        private readonly Image stars;
        private readonly TextMeshProUGUI label;
        private readonly GameObject selectedMark;

        private CharData data;

        internal CharCell(RectTransform cell)
        {
            root = cell.gameObject;
            button = cell.GetComponent<Button>();
            ground = cell.GetComponent<Image>("Ground");
            avatar = cell.GetComponent<Image>("Avatar");
            profession = cell.GetComponent<Image>("Profession");
            stars = cell.GetComponent<Image>("Stars");
            label = cell.GetComponent<TextMeshProUGUI>("NameGround/Name");
            selectedMark = cell.Find("Selected").gameObject;
            root.SetActive(true);
        }

        internal void SetActive(bool value) => root.SetActive(value);

        internal bool Holds(CharData value) => data == value;

        internal void SetSelected(bool value) => selectedMark.SetActive(value);

        internal void Draw(CharData value, bool isSelected, UnityAction onClick)
        {
            data = value;
            CharMeta meta = SafeMeta(value);

            if (meta != null)
            {
                int rarity = meta.GetRarity();
                ground.sprite = SafeSprite(() => CharManager.Inst().GetCardGroundImage(rarity + "_1"));
                stars.sprite = SafeSprite(() => CharManager.Inst().GetCardGroundImage(rarity + "_star"));
                profession.sprite = SafeSprite(() => CharManager.Inst().GetProfessionSmallImage(meta.GetProfession()));
                avatar.sprite = meta.GetAvatar();
                label.text = meta.GetEnglishName();
            }
            else
            {
                // 没有元数据也要画出来 / Still draw the cell: an id the player can see beats a
                // silent gap in the grid.
                label.text = value.GetId();
            }

            selectedMark.SetActive(isSelected);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
        }
    }
}
