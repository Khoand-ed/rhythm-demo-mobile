using System.Collections.Generic;
using System.IO;
using Data.Char;
using Data.Item;
using Data.Player;
using Manager;
using Spine.Unity;
using Tools;
using UI;
using UI.Sub;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Arknights.EditorTools {
    /// <summary>
    /// 生成占位资源 / Generates placeholder bootstrap assets.
    ///
    /// The real Arknights art, audio and data are not in this repository, so every Asset.Load
    /// returns null and nothing renders. This builds the minimum set of prefabs and data the
    /// boot path needs, using only Unity's built-in UI sprites, so the project can be run and
    /// exercised end to end.
    ///
    /// Everything lands under Resources/. Asset.Load checks AssetBundles *first*, so once real
    /// bundles are supplied they take over automatically and these placeholders are ignored -
    /// no cleanup required.
    ///
    /// Prefabs are built programmatically rather than authored as .prefab YAML because the
    /// serialised field wiring (Buttons, InputFields, CanvasGroups, Injections) is far easier to
    /// get right - and to re-run - in code.
    /// </summary>
    public static class GeneratePlaceholderAssets {

        private const string ResourcesRoot = "Assets/Arknights/Resources";
        private const string UIPrefabDir = ResourcesRoot + "/Prefab/UI";
        private const string GamePrefabDir = ResourcesRoot + "/Prefab/Game";
        private const string UserDataDir = ResourcesRoot + "/Data/User";
        private const string FontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";

        // 明日方舟风格配色: 近黑底 + 石板灰面板 + 橙色强调
        // Arknights-flavoured palette. There is still no real art here, so the look comes from
        // colour, procedurally drawn chamfered "cut corner" panels, and layout - nothing else.
        // 面板必须完全不透明: 工程是线性色彩空间, 面板底下就是橙色边框, 哪怕3%的透明度
        // 在线性空间里也会把整块面板染成酱红色
        // The panel fills must be fully opaque. The project renders in Linear colour space and
        // these sit directly on the orange frame, where even 3% alpha blends enough of a bright
        // orange through to turn the whole panel maroon.
        private static readonly Color Ink = new Color(0.043f, 0.047f, 0.055f);
        private static readonly Color Panel = new Color(0.106f, 0.118f, 0.141f, 1f);
        private static readonly Color Accent = new Color(1.00f, 0.42f, 0.09f);
        private static readonly Color AccentDim = new Color(0.60f, 0.27f, 0.10f);
        private static readonly Color Field = new Color(0.060f, 0.065f, 0.075f);
        private static readonly Color TextDim = new Color(0.55f, 0.57f, 0.61f);
        private static readonly Color Hostile = new Color(0.82f, 0.20f, 0.16f);
        private static readonly Color Ranged = new Color(0.25f, 0.62f, 1.00f);

        // 登录界面照着参考图走: 上下黑条 + 首页浅灰底 + 登录/注册纯黑底 + 灰色方块按钮, 没有橙色切角
        // The login screen follows the reference shots instead of the chamfered orange styling used
        // elsewhere: black top/bottom bars, a pale grey home backdrop that a near-opaque overlay
        // blacks out for the login and register steps, and flat grey rectangles for every control.
        private static readonly Color BarInk = new Color(0.075f, 0.075f, 0.080f);
        private static readonly Color Daylight = new Color(0.855f, 0.855f, 0.862f);
        private static readonly Color ButtonFace = new Color(0.322f, 0.325f, 0.333f, 0.95f);
        private static readonly Color FieldFace = new Color(0.400f, 0.404f, 0.412f, 0.55f);
        private static readonly Color EdgeLight = new Color(1f, 1f, 1f, 0.30f);
        private static readonly Color LinkBlue = new Color(0.00f, 0.69f, 1.00f);
        private static readonly Color LoadingLine = new Color(0.910f, 0.784f, 0.118f);
        private static readonly Color WireLine = new Color(0.78f, 0.14f, 0.18f);
        private static readonly Color WireNode = new Color(1.00f, 0.42f, 0.40f);

        // 主界面: 半透明白色斜切磁贴 + 深色字, 采购中心那一组是蓝底白字
        // Home screen: translucent white skewed tiles with dark type, except the store cluster,
        // which is the one blue-on-white group in the reference.
        private static readonly Color TileLight = new Color(0.93f, 0.94f, 0.95f, 0.86f);
        private static readonly Color TileBlue = new Color(0.16f, 0.62f, 0.85f, 0.95f);
        private static readonly Color TileInk = new Color(0.08f, 0.09f, 0.10f);
        private static readonly Color HomeInk = new Color(0.16f, 0.17f, 0.19f);

        private const float TileSkew = 14f;

        // 商店: 白底 + 金色源石 + 黑色价格条 / store: white ground, gold originite, black price bar
        private static readonly Color ShopGold = new Color(0.94f, 0.74f, 0.16f);
        private static readonly Color ShopCert = new Color(0.62f, 0.66f, 0.72f);
        private static readonly Color ShopCoin = new Color(0.85f, 0.78f, 0.42f);

        private const float ChamferBigCorner = 28f;
        private const float ChamferSmallCorner = 13f;
        private const float FrameGap = 5f;

        private static Font font;
        private static Sprite chamferBig;
        private static Sprite chamferSmall;
        private static Sprite wireSphere;
        private static Sprite skewTile;
        private static Sprite levelRing;
        private static Sprite hexBadge;

        [MenuItem("Arknights/Placeholders/Generate Bootstrap Assets", false, 0)]
        public static void Generate() {
            font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            EnsureFolder(UIPrefabDir);
            EnsureFolder(GamePrefabDir);
            EnsureFolder(UserDataDir);

            chamferBig = BuildChamferSprite("T_ChamferBig", 160, (int)ChamferBigCorner);
            chamferSmall = BuildChamferSprite("T_ChamferSmall", 80, (int)ChamferSmallCorner);
            wireSphere = BuildWireSphereSprite("T_WireSphere", 512);
            skewTile = BuildSkewSprite("T_SkewTile", 128, (int)TileSkew);
            levelRing = BuildRingSprite("T_LevelRing", 256, 0.80f);
            hexBadge = BuildHexSprite("T_HexBadge", 128);

            BuildCameraPrefab();
            BuildLoginUIPrefab();
            BuildCommonDialogPrefab();
            BuildHomeUIPrefab();
            BuildSettingUIPrefab();
            BuildItemAssets();
            BuildShopCatalogue();
            BuildShopUIPrefab();
            BuildCharSprites();
            // 授予放在 BuildPlayerData 里 / Metas only here. The roster comes from
            // PlayerData.Initialization, which BuildPlayerData runs further down; granting now
            // would be thrown away when those two saves are recreated.
            BuildOperatorMetas();
            BuildCharUIPrefab();
            BuildCharInfoUIPrefab();
            BuildCharPrefab();
            BuildMonsterPrefab();
            BuildCharPlacePrefab();
            BuildAtkRangeDisplayPrefab();
            BuildPlayerData("Saukiya", "123456");
            BuildPlayerData("Test", "123456");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Placeholders] Generated bootstrap assets into " + ResourcesRoot +
                      ". Press Play on Assets/Arknights/Scenes/StartMenu.unity. " +
                      "Log in with Saukiya / 123456.");
        }

        /// <summary>
        /// 只生成角色内容 / Rebuilds the four playable operators and nothing else.
        ///
        /// 为什么不走完整 Generate / This exists because the full Generate Bootstrap Assets rebuilds
        /// HomeUI.prefab from scratch, and that prefab carries three OpenUIButton components added
        /// after the fact by SongSelectUISetup, DepotUISetup and MissionUISetup. A full regenerate
        /// destroys all three silently, and the symptom - "the home tiles stopped working" - only
        /// shows up days later. Use this narrow item for day-to-day character work.
        ///
        /// 真要跑了完整 Generate 就补这三个 / If a full Generate ever does run, the recovery is to
        /// re-run Tools/Rhythm/Wire Song Flow, Arknights/Depot/Build Depot UI and
        /// Arknights/Mission/Build Mission UI.
        /// </summary>
        [MenuItem("Arknights/Placeholders/Generate Rhythm Operators", false, 2)]
        public static void GenerateOperators() {
            BuildCharSprites();
            BuildOperatorMetas();
            GrantOperators();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Placeholders] Built {Operators.Length} operators into {MetaCharDir} " +
                      $"with avatars, portraits and passive icons under {SpriteCharDir}, " +
                      "and granted them to Saukiya and Test.\n" +
                      "  HomeUI.prefab was NOT touched, so its three OpenUIButton hooks are intact.");
        }

        /// <summary>
        /// 四个干员的元数据 / The four CharMeta assets plus the art each one needs.
        ///
        /// 和授予分开 / Kept separate from GrantOperators because the full Generate runs
        /// BuildPlayerData afterwards, which recreates both saves from PlayerData.Initialization -
        /// granting before that would simply be overwritten.
        /// </summary>
        private static void BuildOperatorMetas() {
            for (int i = 0; i < Operators.Length; i++) {
                OperatorSeed seed = Operators[i];
                Sprite avatar = OperatorAvatar(seed, i);
                Sprite portrait = OperatorPortrait(seed, i);
                Sprite icon = PolygonSprite(SpriteCharDir + "/Passive", seed.Id, 96, 3 + i % 5, i * 41f);
                PassiveSO passive = BuildPassive(seed, icon);
                BuildCharMeta(seed, avatar, portrait, passive);
            }
        }

        /// <summary>
        /// 把干员发给两个存档 / Adds every operator to both committed saves, in place.
        ///
        /// 原地打补丁而不是重建 / Patched rather than rebuilt: BuildPlayerData deletes and recreates
        /// the asset, which would throw away whatever the player has been doing in the Editor.
        /// GetCharList returns the live list and CharData(string) is public, so no SerializedObject
        /// gymnastics are needed. The existence check makes re-running this a no-op.
        /// </summary>
        private static void GrantOperators() {
            foreach (string user in new[] { "Saukiya", "Test" }) {
                string path = UserDataDir + "/" + user + ".asset";
                PlayerData data = AssetDatabase.LoadAssetAtPath<PlayerData>(path);

                if (data == null) {
                    Debug.LogWarning($"[Placeholders] No save at {path}; run Generate Bootstrap Assets first.");
                    continue;
                }

                List<CharData> roster = data.GetCharList();
                if (roster == null) {
                    Debug.LogWarning($"[Placeholders] {user} has no charList to add to; regenerate that save.");
                    continue;
                }

                int added = 0;
                foreach (OperatorSeed seed in Operators) {
                    if (data.GetCharData(seed.Id) != null) continue;
                    roster.Add(new CharData(seed.Id));
                    added++;
                }

                // SetDirty 单独不写盘 / SetDirty alone does not write the file; the caller's
                // SaveAssets is what actually persists this.
                if (added > 0) EditorUtility.SetDirty(data);
                Debug.Log($"[Placeholders] {user}: {added} operator(s) added, roster now " +
                          string.Join(", ", roster.ConvertAll(c => c.GetId()).ToArray()));
            }
        }

        [MenuItem("Arknights/Placeholders/Delete Generated Assets", false, 1)]
        public static void Delete() {
            foreach (string dir in new[] { UIPrefabDir, GamePrefabDir, ResourcesRoot + "/Data" }) {
                if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            }
            AssetDatabase.Refresh();
            Debug.Log("[Placeholders] Removed generated placeholder assets.");
        }

        // ------------------------------------------------------------------ Camera + Canvas

        /// <summary>
        /// UIManager.Initialization 需要: 根节点 + 名为"Canvas"的直接子节点 + CanvasScaler
        /// UIManager.Initialization requires a root, a DIRECT child named exactly "Canvas",
        /// and a CanvasScaler on it whose referenceResolution it reads.
        /// </summary>
        private static void BuildCameraPrefab() {
            GameObject root = new GameObject("Camera");
            Camera cam = root.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ink;

            // 必须是透视投影, 否则倾斜不产生任何透视 / Perspective, not orthographic, and this is
            // the whole reason HomeUI's parallax can keystone at all. Under an orthographic
            // projection a RectTransform rotated about X or Y only foreshortens - parallel edges
            // stay parallel, the far edge never narrows - so the tilt is close to invisible.
            //
            // 静止时看不出区别 / At rest nothing changes: a Screen Space - Camera canvas is resized
            // by Unity to fill the frustum at planeDistance either way, and every UI prefab in
            // this project sits at z = 0, so nothing gains or loses size from depth. Only rotated
            // content renders differently.
            //
            // 透视强度看视野角和矩形宽度 / Strength depends on the field of view AND on how far
            // the rotated rect extends from its axis - planeDistance cancels out, but the rect's
            // own size does not. An edge h canvas-units from the axis shifts by h*sin(θ), against
            // a viewing distance of (canvasHeight/2)/tan(fov/2). At 45°, HomeUI's move2 group
            // (1885 x 908) measures a 25.5% near/far size difference under a 9° yaw but only
            // 11.5% under a 9° pitch, because it is twice as wide as it is tall.
            cam.orthographic = false;
            cam.fieldOfView = 45f;

            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root.transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // 必须按高适配, 才能和 UIManager.GetCanvasSize() 对上:
            // 那边算的是 canvasSize.x = canvasSize.y * 宽/高, 也就是把高当成固定的1080。
            // 之前设成按宽适配, 于是 CharInfoUI 按 GetCanvasSize() 撑出来的面板比可视区还宽, 两边被切掉。
            // Height-matched on purpose, to agree with UIManager.GetCanvasSize(), which computes
            // canvasSize.x = canvasSize.y * width / height - i.e. it treats 1080 as the fixed axis.
            // With width-matching the two disagreed, and CharInfoUI sized its panels from
            // GetCanvasSize() to 2160 against a 1920 viewport, so both edges were clipped.
            // At 16:9 the two modes are identical; wider displays now gain side margin instead of
            // losing the top and bottom of every screen.
            scaler.matchWidthOrHeight = 1f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 没有EventSystem按钮和输入框点不动 / Without an EventSystem nothing is clickable.
            GameObject events = new GameObject("EventSystem");
            events.transform.SetParent(root.transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();

            SavePrefab(root, "Camera");
        }

        // ------------------------------------------------------------------ LoginUI

        private static void BuildLoginUIPrefab() {
            GameObject root = NewUIRoot("LoginUI");
            LoginUI ui = root.AddComponent<LoginUI>();

            // 首页是浅灰底, 登录/注册时 backGround 这层黑幕淡入把它盖掉
            // The home step is pale grey; backGround is the blackout LoginUI fades in over it on
            // the way to login/register, and back out on the way home.
            // 必须完全不透明: 线性空间里就算96%也会透出底下的浅灰, 实测变成0.19的灰而不是黑
            // It has to be fully opaque. The project renders in Linear space, where even 4% of the
            // pale backdrop showing through lands at ~0.19 grey instead of black - measured, not
            // guessed. The CanvasGroup is what fades it in, so opacity here costs nothing.
            Rect(root, "BaseBackground", Stretch).AddComponent<Image>().color = Daylight;
            ui.backGround = Group(root, "BackGround", Stretch, new Color(0.043f, 0.043f, 0.047f, 1f), 0f);

            // sphere 被 DOAnchorPosY / DOScale 驱动 / driven by DOAnchorPosY and DOScale
            // 必须排在黑幕之后: 参考图里浅底和黑底两种情况下球都看得见
            // Ordered after the blackout on purpose - the ball stays visible on both the pale and
            // the black backdrop in the reference shots.
            GameObject sphereGo = Rect(root, "Sphere", r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(620, 620);
                r.anchoredPosition = new Vector2(0, 263);
            });
            Image sphereImg = sphereGo.AddComponent<Image>();
            sphereImg.color = Color.white;
            sphereImg.sprite = wireSphere;
            sphereImg.raycastTarget = false;
            ui.sphere = (RectTransform)sphereGo.transform;

            // sphereCamera.DOColor 动画的是背景色 / DOColor animates the background colour
            GameObject sphereCamGo = new GameObject("SphereCamera");
            sphereCamGo.transform.SetParent(root.transform, false);
            Camera sphereCam = sphereCamGo.AddComponent<Camera>();
            sphereCam.clearFlags = CameraClearFlags.SolidColor;
            sphereCam.backgroundColor = new Color(0.74f, 0.36f, 0.36f, 0f);
            sphereCam.cullingMask = 0;
            // 关掉渲染, 否则它的 SolidColor 会把整个屏幕刷掉; DOColor 只改背景色, 不需要它真的渲染
            // Disabled on purpose: a second SolidColor camera would clear the whole screen.
            // LoginUI only tweens its backgroundColor, which works fine on a disabled camera.
            sphereCam.enabled = false;
            ui.sphereCamera = sphereCam;

            // ---- home: 标题 + 两个并排按钮, 深色字压在浅灰底上
            // Home sits on the pale backdrop, so its type is dark - the other two steps run black.
            ui.homePanel = PanelLayer(root, "HomePanel", 1f);
            GameObject home = ui.homePanel.gameObject;

            Text wordmark = Label(home, "Wordmark", "ARKNIGHTS", 148, new Color(0.06f, 0.06f, 0.07f),
                new Vector2(0, 110));
            ((RectTransform)wordmark.transform).sizeDelta = new Vector2(1400, 190);

            GameObject ribbon = Rect(home, "Ribbon", r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(330, 34);
                r.anchoredPosition = Vector2.zero;
            });
            ribbon.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.11f);
            Text ribbonText = Label(ribbon, "RibbonText", "R H O D E S   I S L A N D", 18,
                new Color(0.88f, 0.88f, 0.90f), Vector2.zero);
            ((RectTransform)ribbonText.transform).sizeDelta = new Vector2(330, 34);

            // 宽度是按"REGISTER ACCOUNT"这条最长的标题定的: Style() 把Text设成Overflow,
            // 装不下不会换行而是直接溢出到按钮外面
            // Sized for "REGISTER ACCOUNT", the longest caption: Style() leaves Text on Overflow,
            // so a caption that does not fit spills outside the button rather than wrapping.
            ui.home_login = FlatBtn(home, "home_login", "ACCOUNT LOGIN",
                new Vector2(-152, -160), new Vector2(290, 74), 24, false);
            ui.home_register = FlatBtn(home, "home_register", "REGISTER ACCOUNT",
                new Vector2(152, -160), new Vector2(290, 74), 24, true);

            // ---- login: 返回键在左上, 两个输入框和登录键同宽居中
            // Login: back arrow top-left, then two fields and a button all sharing one width.
            ui.loginPanel = PanelLayer(root, "LoginPanel", 0f);
            GameObject login = ui.loginPanel.gameObject;
            login.SetActive(false);

            ui.login_back = BackBtn(login, "login_back");
            ui.login_userName = FlatInput(login, "login_userName", "Account", new Vector2(0, -60));
            ui.login_password = FlatInput(login, "login_password", "Password", new Vector2(0, -135));
            ui.login_password.contentType = InputField.ContentType.Password;
            ui.login_enter = FlatBtn(login, "login_enter", "LOGIN",
                new Vector2(0, -262), new Vector2(FieldWidth, 64), 26, false);

            // ---- register
            ui.registerPanel = PanelLayer(root, "RegisterPanel", 0f);
            GameObject reg = ui.registerPanel.gameObject;
            reg.SetActive(false);

            ui.register_back = BackBtn(reg, "register_back");
            ui.register_userName = FlatInput(reg, "register_userName", "Username", new Vector2(0, 18));
            ui.register_password = FlatInput(reg, "register_password", "Enter password", new Vector2(0, -57));
            ui.register_password.contentType = InputField.ContentType.Password;
            ui.register_rePassword = FlatInput(reg, "register_rePassword", "Enter password again",
                new Vector2(0, -132));
            ui.register_rePassword.contentType = InputField.ContentType.Password;
            ui.register_toggle = FlatCheck(reg, "register_toggle",
                "I have read and agree to the <color=#00B0FF>Registration Agreement</color> " +
                "and <color=#00B0FF>Privacy Agreement</color>", new Vector2(0, -196));
            ui.register_enter = FlatBtn(reg, "register_enter", "REGISTER",
                new Vector2(0, -282), new Vector2(FieldWidth, 64), 26, false);

            // ---- loading: 两根进度条从左右往中间合成一条黄线 / the two bars close into one yellow line
            ui.loadingPanel = PanelLayer(root, "LoadingPanel", 0f);
            GameObject loading = ui.loadingPanel.gameObject;
            loading.SetActive(false);
            ServerPlate(loading);
            ui.loading_bar1 = Bar(loading, "loading_bar1", true);
            ui.loading_bar2 = Bar(loading, "loading_bar2", false);
            Label(loading, "Status", "Attempting to establish a neural link with the Pigeon Server",
                20, new Color(0.72f, 0.72f, 0.75f), new Vector2(0, -330));

            // ---- 上下黑条压在所有面板之上 / the bars overlay every panel, as in the reference
            GameObject topBar = Rect(root, "TopBar", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 86);
            });
            topBar.AddComponent<Image>().color = BarInk;

            GameObject bottomBar = Rect(root, "BottomBar", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(0.5f, 0);
                r.sizeDelta = new Vector2(0, 96);
            });
            bottomBar.AddComponent<Image>().color = BarInk;

            // 参考图这里是发行商logo, 本仓库没有美术资源, 放一行说明代替
            // The reference carries publisher logos here; with no art in the repo this says what
            // the build actually is instead of faking someone's branding.
            Text mark = Label(bottomBar, "Mark", "PLACEHOLDER BUILD   -   no art assets, see SETUP.md",
                18, new Color(0.62f, 0.62f, 0.65f), Vector2.zero);
            RectTransform markRect = (RectTransform)mark.transform;
            markRect.anchorMin = markRect.anchorMax = new Vector2(0, 0.5f);
            markRect.pivot = new Vector2(0, 0.5f);
            markRect.sizeDelta = new Vector2(700, 40);
            markRect.anchoredPosition = new Vector2(40, 0);
            mark.alignment = TextAnchor.MiddleLeft;

            ui.lowerRightPanel = PanelLayer(root, "LowerRightPanel", 1f);
            GameObject lower = ui.lowerRightPanel.gameObject;
            Plaque(lower, "Announcement", "No Announcements", -488);
            Plaque(lower, "Agreement", "User Agreement", -300);

            Text credit = Label(lower, "Credit", "REMAKE\nby Saukiya", 20,
                new Color(0.92f, 0.92f, 0.94f), Vector2.zero);
            RectTransform creditRect = (RectTransform)credit.transform;
            creditRect.anchorMin = creditRect.anchorMax = new Vector2(1, 0);
            creditRect.pivot = new Vector2(1, 0);
            creditRect.sizeDelta = new Vector2(180, 70);
            creditRect.anchoredPosition = new Vector2(-32, 14);

            SavePrefab(root, "LoginUI");
        }

        private const float FieldWidth = 540f;

        /// <summary>
        /// LoginUI.cs 对进度条调用 GetComponentInChildren&lt;Text&gt;(), 所以必须有子Text
        /// LoginUI calls GetComponentInChildren&lt;Text&gt;() on each bar, so a child Text is required.
        ///
        /// 两根条共用一个Y: 一根从左边长过来, 一根从右边长过来, 合起来就是参考图那条黄线
        /// Both bars share one Y. bar1 grows rightward from the left edge and bar2 leftward from
        /// the right, so when the tween brings both to 0.5 they meet in the middle and read as the
        /// single yellow rule the reference draws across the loading screen. The tween drives the
        /// fill's own anchors, so the track behind it has to span the full width.
        /// </summary>
        private static RectTransform Bar(GameObject parent, string name, bool leftToRight) {
            GameObject track = Rect(parent, name + "_track", r => {
                r.anchorMin = new Vector2(0f, 0.5f);
                r.anchorMax = new Vector2(1f, 0.5f);
                r.sizeDelta = new Vector2(0, 5);
                r.anchoredPosition = new Vector2(0, -30);
            });
            track.AddComponent<Image>().color =
                new Color(LoadingLine.r, LoadingLine.g, LoadingLine.b, 0.07f);

            GameObject fill = new GameObject(name, typeof(RectTransform));
            fill.transform.SetParent(track.transform, false);
            RectTransform fr = (RectTransform)fill.transform;
            fr.anchorMin = leftToRight ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
            fr.anchorMax = leftToRight ? new Vector2(0.02f, 1f) : new Vector2(1f, 1f);
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;
            fill.AddComponent<Image>().color = LoadingLine;

            // 百分比跟着增长的那一头, 抬到线上方, 两根条会合时才不会叠在一起
            // The readout rides the growing tip and sits above the rule, so the two do not overlap
            // when both bars arrive at the centre together.
            GameObject label = Rect(fill, name + "_text", r => {
                r.anchorMin = r.anchorMax = new Vector2(leftToRight ? 1f : 0f, 0.5f);
                r.pivot = new Vector2(leftToRight ? 1f : 0f, 0.5f);
                r.sizeDelta = new Vector2(150, 26);
                r.anchoredPosition = new Vector2(leftToRight ? -10 : 10, 24);
            });
            Text t = label.AddComponent<Text>();
            Style(t, 20, LoadingLine);
            t.alignment = leftToRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            t.text = "0%";

            return fr;
        }

        /// <summary>
        /// 参考图里没有面板底框, 控件直接浮在背景上, 所以一"页"只有RectTransform+CanvasGroup
        /// The reference screens have no panel box - the controls sit straight on the backdrop - so
        /// a step is just a full-screen RectTransform plus the CanvasGroup LoginUI fades.
        /// </summary>
        private static CanvasGroup PanelLayer(GameObject parent, string name, float alpha) {
            CanvasGroup group = Rect(parent, name, Stretch).AddComponent<CanvasGroup>();
            group.alpha = alpha;
            return group;
        }

        /// <summary>
        /// 参考图的按钮就是一个灰方块加一圈浅边 / a flat grey rectangle with a light edge.
        /// marker 画注册键左边那个小三角 / marker draws the triangle the register button carries.
        /// </summary>
        private static Button FlatBtn(GameObject parent, string name, string caption, Vector2 pos,
                                      Vector2 size, int fontSize, bool marker) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = size;
                r.anchoredPosition = pos;
            });
            Image img = go.AddComponent<Image>();
            img.color = ButtonFace;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;
            Border(go, EdgeLight, 2f);

            // 有小三角时标题要往右让一截, 否则长标题会压在三角上
            // With a marker the caption has to give up the left inset, or a long caption runs
            // straight over the triangle.
            Text t = Label(go, "Text", caption, fontSize, Color.white, Vector2.zero);
            RectTransform captionRect = (RectTransform)t.transform;
            captionRect.sizeDelta = marker ? new Vector2(size.x - 30, size.y) : size;
            captionRect.anchoredPosition = marker ? new Vector2(15, 0) : Vector2.zero;

            if (marker) {
                Text triangle = Label(go, "Marker", "▸", fontSize, Color.white, Vector2.zero);
                RectTransform triangleRect = (RectTransform)triangle.transform;
                triangleRect.anchorMin = triangleRect.anchorMax = new Vector2(0, 0.5f);
                triangleRect.pivot = new Vector2(0, 0.5f);
                triangleRect.sizeDelta = new Vector2(30, size.y);
                triangleRect.anchoredPosition = new Vector2(14, 0);
            }
            return button;
        }

        /// <summary>左上角的返回键 / the back chevron pinned to the top-left.</summary>
        private static Button BackBtn(GameObject parent, string name) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(152, 66);
                r.anchoredPosition = new Vector2(48, -128);
            });
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.30f, 0.30f, 0.31f, 0.75f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;

            Text t = Label(go, "Text", "‹", 52, Color.white, new Vector2(0, 4));
            ((RectTransform)t.transform).sizeDelta = new Vector2(152, 66);
            return button;
        }

        private static InputField FlatInput(GameObject parent, string name, string placeholder, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(FieldWidth, 64);
                r.anchoredPosition = pos;
            });
            Image img = go.AddComponent<Image>();
            img.color = FieldFace;

            Text text = FieldText(go, "Text", 24, Color.white);
            Text ph = FieldText(go, "Placeholder", 24, new Color(0.82f, 0.82f, 0.85f, 0.65f));
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;

            InputField field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = ph;
            return field;
        }

        private static Text FieldText(GameObject parent, string name, int size, Color color) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(22, 6);
                r.offsetMax = new Vector2(-22, -6);
            });
            Text t = go.AddComponent<Text>();
            Style(t, size, color);
            t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        private static Toggle FlatCheck(GameObject parent, string name, string caption, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(FieldWidth, 40);
                r.anchoredPosition = pos;
            });
            GameObject boxGo = Rect(go, "Background", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(26, 26);
                r.anchoredPosition = Vector2.zero;
            });
            Image box = boxGo.AddComponent<Image>();
            box.color = new Color(0.62f, 0.63f, 0.65f, 0.85f);

            GameObject markGo = Rect(boxGo, "Checkmark", r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(5, 5);
                r.offsetMax = new Vector2(-5, -5);
            });
            Image mark = markGo.AddComponent<Image>();
            mark.color = new Color(0.10f, 0.10f, 0.12f);

            Text t = Label(go, "Label", caption, 19, new Color(0.88f, 0.88f, 0.90f), Vector2.zero);
            RectTransform textRect = (RectTransform)t.transform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0, 0.5f);
            textRect.pivot = new Vector2(0, 0.5f);
            textRect.sizeDelta = new Vector2(FieldWidth - 40, 40);
            textRect.anchoredPosition = new Vector2(38, 0);
            t.alignment = TextAnchor.MiddleLeft;

            Toggle toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = mark;
            toggle.isOn = false;
            return toggle;
        }

        /// <summary>加载页中间那块服务器信息板 / the server plate shown mid-load.</summary>
        private static void ServerPlate(GameObject parent) {
            GameObject plate = Rect(parent, "ServerPlate", r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(196, 330);
                r.anchoredPosition = new Vector2(0, 60);
            });
            plate.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.20f, 0.55f);
            Border(plate, new Color(1f, 1f, 1f, 0.45f), 2f);

            Label(plate, "ServerCaption", "SERVER", 17, new Color(0.72f, 0.72f, 0.75f), new Vector2(0, 116));
            Label(plate, "ServerName", "TERRA", 34, Color.white, new Vector2(0, 80));
            Label(plate, "ServerIndex", "#0", 86, Color.white, new Vector2(0, -6));
            Label(plate, "ServerLoad", "100%", 20, LoadingLine, new Vector2(0, -122));
        }

        /// <summary>
        /// 底栏右下角那两块牌子 / the two plates in the bottom-right of every reference shot.
        /// LoginUI 没有对应字段, 所以只做外观不做交互 - 按下去没反应的按钮比不做按钮更糟
        /// LoginUI has no fields for these, so they are styled labels rather than Buttons: a button
        /// that swallows the click and does nothing is worse than something that never looked
        /// clickable. Wire them up here if the screens behind them ever exist.
        /// </summary>
        private static void Plaque(GameObject parent, string name, string caption, float x) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(1, 0);
                r.sizeDelta = new Vector2(174, 52);
                r.anchoredPosition = new Vector2(x, 22);
            });
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.24f, 0.24f, 0.25f, 0.95f);
            img.raycastTarget = false;
            Border(go, new Color(1f, 1f, 1f, 0.22f), 1.5f);

            Text t = Label(go, "Text", caption, 18, new Color(0.90f, 0.90f, 0.92f), Vector2.zero);
            ((RectTransform)t.transform).sizeDelta = new Vector2(174, 52);
        }

        /// <summary>
        /// uGUI 没有描边属性, 只能用四条细Image拼一圈
        /// uGUI has no border property, so an outline is four thin stretched Images. They are added
        /// before the caption so the text always draws over them, and none of them take raycasts.
        /// </summary>
        private static void Border(GameObject parent, Color color, float thickness) {
            Edge(parent, "EdgeTop", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, thickness), new Vector2(0, -thickness * 0.5f), color);
            Edge(parent, "EdgeBottom", new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, thickness), new Vector2(0, thickness * 0.5f), color);
            Edge(parent, "EdgeLeft", new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(thickness, 0), new Vector2(thickness * 0.5f, 0), color);
            Edge(parent, "EdgeRight", new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(thickness, 0), new Vector2(-thickness * 0.5f, 0), color);
        }

        private static void Edge(GameObject parent, string name, Vector2 min, Vector2 max,
                                 Vector2 size, Vector2 offset, Color color) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = min;
                r.anchorMax = max;
                r.sizeDelta = size;
                r.anchoredPosition = offset;
            });
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        // ------------------------------------------------------------------ CommonDialogUI

        private static void BuildCommonDialogPrefab() {
            GameObject root = NewUIRoot("CommonDialogUI");
            CommonDialogUI ui = root.AddComponent<CommonDialogUI>();

            // Show() 会对 blur.material 设置 _Size, 给它一份独立材质避免污染共享材质
            // Show() sets _Size on blur.material; give it its own material so the shared
            // default UI material is never mutated. The property does not exist on UI/Default,
            // which Unity treats as a harmless no-op.
            GameObject blurGo = Rect(root, "blur", Stretch);
            Image blur = blurGo.AddComponent<Image>();
            blur.color = new Color(0, 0, 0, 0.55f);
            blur.material = PlaceholderMaterial();
            ui.blur = blur;

            // 参考图的弹窗是一条横贯屏幕的色带, 不是居中的方框
            // The reference dialog is a band running the full width of the screen rather than a
            // centred box, so Box stretches horizontally and only its height is fixed.
            GameObject box = Rect(root, "Box", r => {
                r.anchorMin = new Vector2(0, 0.5f);
                r.anchorMax = new Vector2(1, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(0, 280);
                r.anchoredPosition = new Vector2(0, 40);
            });

            ui.blackGround = Rect(box, "blackGround", Stretch);
            ui.blackGround.AddComponent<Image>().color = new Color(0.16f, 0.16f, 0.17f, 0.94f);
            ui.whiteGround = Rect(box, "whiteGround", Stretch);
            ui.whiteGround.AddComponent<Image>().color = new Color(0.90f, 0.90f, 0.92f, 0.96f);
            ui.blackGround.SetActive(false);
            ui.whiteGround.SetActive(false);

            // 字色由 CommonDialogUI.GetText() 在运行时按黑底/白底切换, 这里只定排版
            // Colour is chosen at runtime by CommonDialogUI.GetText() from the ground type, so this
            // only fixes the typography.
            GameObject msg = Rect(box, "message", r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(80, 0);
                r.offsetMax = new Vector2(-80, 0);
            });
            Text msgText = msg.AddComponent<Text>();
            Style(msgText, 30, Color.white);
            msgText.supportRichText = true;
            ui.message = msgText;

            GameObject one = Rect(root, "mode_One", Stretch);
            ui.mode_One = one;
            ui.mode_One_back = RoundBtn(one, "mode_One_back", "✓", new Vector2(0, -200));

            GameObject two = Rect(root, "mode_Two", Stretch);
            ui.mode_Two = two;
            ui.mode_Two_enter = RoundBtn(two, "mode_Two_enter", "✓", new Vector2(-90, -200));
            ui.mode_Two_back = RoundBtn(two, "mode_Two_back", "✕", new Vector2(90, -200));
            two.SetActive(false);

            SavePrefab(root, "CommonDialogUI");
        }

        /// <summary>参考图里确认键是个白色圆钮 / the reference confirm control is a white disc.</summary>
        private static Button RoundBtn(GameObject parent, string name, string glyph, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(74, 74);
                r.anchoredPosition = pos;
            });
            Image img = go.AddComponent<Image>();
            img.sprite = Builtin("Knob");
            img.color = Color.white;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;

            Text t = Label(go, "Glyph", glyph, 34, new Color(0.10f, 0.10f, 0.12f), Vector2.zero);
            ((RectTransform)t.transform).sizeDelta = new Vector2(74, 74);
            return button;
        }

        // ------------------------------------------------------------------ HomeUI (Lua driven)

        /// <summary>
        /// HomeUI 由 Lua 驱动, injections 的名字必须和 HomeUI.lua.txt 里的全局变量对应
        /// HomeUI is Lua driven: the injection names must match the globals HomeUI.lua.txt reads.
        /// </summary>
        private static void BuildHomeUIPrefab() {
            GameObject root = NewUIRoot("HomeUI");
            RuaUI ui = root.AddComponent<RuaUI>();

            Rect(root, "BackGround", Stretch).AddComponent<Image>().color = HomeInk;

            // move1 / move2 由 Lua 跟着鼠标做视差, 所以它们就是背景层
            // Lua parallaxes these two against the mouse, so they ARE the backdrop. The centre of
            // the screen is deliberately left clear - that is where the operator art would go.
            GameObject move1 = Rect(root, "move1", Centered(2400, 1400));
            move1.AddComponent<Image>().color = new Color(0.22f, 0.24f, 0.27f, 0.55f);
            Beam(move1, "Beam1", new Vector2(-160, 250), new Vector2(2100, 90), 0.16f);
            Beam(move1, "Beam2", new Vector2(120, -80), new Vector2(2100, 140), 0.12f);
            Beam(move1, "Beam3", new Vector2(-40, -360), new Vector2(2100, 70), 0.10f);

            GameObject move2 = Rect(root, "move2", Centered(1500, 900));
            move2.AddComponent<Image>().color = new Color(0.30f, 0.33f, 0.37f, 0.30f);
            Beam(move2, "Strut1", new Vector2(-420, 0), new Vector2(26, 900), 0.22f);
            Beam(move2, "Strut2", new Vector2(330, -60), new Vector2(26, 760), 0.18f);

            // ---- 左侧: 等级环 + 名字 + 罗德岛横幅 / level ring, name, Rhodes Island banner
            GameObject plate = Rect(root, "PlayerPlate", At(new Vector2(-742, 10), new Vector2(420, 300)));
            plate.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.08f, 0.55f);
            Text banner = Label(plate, "Banner", "RHODES ISLAND", 22, new Color(1, 1, 1, 0.28f),
                new Vector2(10, -108));
            ((RectTransform)banner.transform).sizeDelta = new Vector2(420, 40);

            GameObject ringTrack = Rect(root, "expTrack", At(new Vector2(-792, 75), new Vector2(190, 190)));
            Image ringTrackImage = ringTrack.AddComponent<Image>();
            ringTrackImage.sprite = levelRing;
            ringTrackImage.color = new Color(1, 1, 1, 0.12f);

            // 经验条做成环形: Lua 只设 fillAmount, 填充方式是在这里定的
            // The exp readout is a ring here. Lua only ever assigns fillAmount, so the fill method
            // is ours to choose - Radial360 from the top is what the reference draws.
            GameObject expGo = Rect(root, "expPercentage", At(new Vector2(-792, 75), new Vector2(190, 190)));
            Image exp = expGo.AddComponent<Image>();
            exp.sprite = levelRing;
            exp.color = new Color(0.98f, 0.78f, 0.16f);
            exp.type = Image.Type.Filled;           // fillAmount 只有 Filled 才有视觉效果
            exp.fillMethod = Image.FillMethod.Radial360;
            exp.fillOrigin = (int)Image.Origin360.Top;
            exp.fillClockwise = true;

            Text pLevel = Label(root, "playerLevel", "1", 58, Color.white, new Vector2(-792, 86));
            Label(root, "levelCaption", "LV", 20, new Color(1, 1, 1, 0.65f), new Vector2(-792, 44));
            Text pName = Label(root, "playerName", "Doctor", 34, Color.white, new Vector2(-770, -44));
            ((RectTransform)pName.transform).sizeDelta = new Vector2(360, 50);

            // ---- 左上角四个图标 / the four icons across the top-left
            IconButton(root, "SettingsIcon", IconGlyph.Gear, new Vector2(-866, 434), ui, "SettingUI");
            IconButton(root, "AlertIcon", IconGlyph.Alert, new Vector2(-770, 434), null, null);
            IconButton(root, "MailIcon", IconGlyph.Mail, new Vector2(-674, 434), null, null);
            IconButton(root, "CalendarIcon", IconGlyph.Calendar, new Vector2(-578, 434), null, null);

            // ---- 右上角: 时间 + 三种货币 / clock and the three currencies
            Text clock = Label(root, "time", "----/--/-- --:--", 22, new Color(1, 1, 1, 0.72f),
                new Vector2(330, 474));
            ((RectTransform)clock.transform).sizeDelta = new Vector2(420, 34);

            Text coin = Currency(root, "dragonCoin", "LMD", new Vector2(120, 422),
                new Color(0.36f, 0.62f, 0.86f), false);
            Text jade = Currency(root, "syntheticJade", "ORUNDUM", new Vector2(450, 422),
                new Color(0.82f, 0.26f, 0.42f), true);
            Text stone = Currency(root, "sourceStone", "ORIGINITE", new Vector2(780, 422),
                new Color(0.85f, 0.72f, 0.28f), true);

            // ---- 右侧磁贴 / the tile grid, positions traced off the reference at 1920x1080
            GameObject operation = MenuTile(root, "OperationTile", "OPERATION", null,
                new Vector2(508, 253), new Vector2(789, 246), 64, TileLight, TileInk, ui, "SelectDungeonUI");
            AccentBar(operation, false);

            // 标题要让开左边的理智块, 默认是整块磁贴居中, 会压在一起
            // MenuTile centres its caption across the whole tile, which would put OPERATION straight
            // on top of the sanity block; give it just the clear area to the right of it.
            RectTransform operationCaption = (RectTransform)operation.transform.Find("Caption");
            operationCaption.sizeDelta = new Vector2(460, 246);
            operationCaption.anchoredPosition = new Vector2(160, 0);

            // 作战磁贴里那块理智: reason/maxReason 正好是参考图上的 80 和 /123
            // The sanity block inside the operation tile - reason and maxReason are exactly the
            // 80 and /123 the reference prints there.
            GameObject sanity = Rect(operation, "SanityBlock",
                At(new Vector2(-232, 16), new Vector2(286, 190)));
            sanity.AddComponent<Image>().color = new Color(0.72f, 0.73f, 0.75f, 0.55f);
            Label(sanity, "plus", "+", 44, TileInk, new Vector2(-104, 26));
            Text reason = Label(sanity, "reason", "0", 82, TileInk, new Vector2(24, 26));
            ((RectTransform)reason.transform).sizeDelta = new Vector2(210, 110);

            GameObject sanityStrip = Rect(sanity, "SanityStrip",
                At(new Vector2(0, -62), new Vector2(286, 52)));
            sanityStrip.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.12f, 0.92f);
            Label(sanityStrip, "sanityCaption", "SANITY", 22, new Color(1, 1, 1, 0.75f), new Vector2(-62, 0));
            Label(sanityStrip, "sanitySlash", "/", 24, new Color(1, 1, 1, 0.55f), new Vector2(14, 0));
            Text maxReason = Label(sanityStrip, "maxReason", "0", 26, Color.white, new Vector2(74, 0));
            ((RectTransform)maxReason.transform).sizeDelta = new Vector2(120, 40);

            // 故意不接 / Deliberately unwired. SquadUI was a tower-defense screen for filling a
            // four-slot squad, and it never had a prefab, so the tile only ever logged a load
            // error. Picking an operator now happens inside the song flow, on CharSelectUI.
            MenuTile(root, "SquadTile", "SQUAD", null,
                new Vector2(245, 51), new Vector2(337, 130), 40, TileLight, TileInk, null, null);
            MenuTile(root, "OperatorsTile", "OPERATORS", "Roster Management",
                new Vector2(643, 51), new Vector2(417, 130), 40, TileLight, TileInk, ui, "CharUI");

            MenuTile(root, "StoreTile", "STORE", null,
                new Vector2(293, -164), new Vector2(270, 150), 34, TileBlue, Color.white, ui, "ShopUI");
            MenuTile(root, "RecruitTile", "RECRUIT", null,
                new Vector2(574, -112), new Vector2(263, 61), 26, TileLight, TileInk, null, null);
            MenuTile(root, "PublicRecruitTile", "PUBLIC RECRUIT", null,
                new Vector2(552, -198), new Vector2(205, 68), 21, TileBlue, Color.white, null, null);
            MenuTile(root, "HeadhuntTile", "HEADHUNTING", null,
                new Vector2(801, -198), new Vector2(234, 68), 21, TileBlue, Color.white, null, null);

            GameObject missions = MenuTile(root, "MissionsTile", "MISSIONS", null,
                new Vector2(194, -325), new Vector2(219, 130), 36, TileLight, TileInk, null, null);
            AccentBar(missions, true);
            // 制造站不是仓库 / Deliberately unwired. This tile used to open the depot, back when
            // DepotTile did nothing and something had to reach the item screen; DepotTile opens it
            // properly now, and a manufacturing station is not a warehouse.
            MenuTile(root, "ManufactureTile", "MANUFACTURE", null,
                new Vector2(541, -325), new Vector2(255, 130), 28, TileLight, TileInk, null, null);
            MenuTile(root, "DepotTile", "DEPOT", null,
                new Vector2(848, -349), new Vector2(154, 82), 26, TileLight, TileInk, null, null);

            NewsPanel(root);

            ui.injections = new[] {
                Inject("time", clock),
                Inject("move1", move1.transform),
                Inject("move2", move2.transform),
                Inject("playerName", pName),
                Inject("playerLevel", pLevel),
                Inject("expPercentage", exp),
                Inject("reason", reason),
                Inject("maxReason", maxReason),
                Inject("sourceStone", stone),
                Inject("syntheticJade", jade),
                Inject("dragonCoin", coin),
            };

            SavePrefab(root, "HomeUI");
        }

        private static Injection Inject(string name, Object value) {
            return new Injection { name = name, value = value };
        }

        // ------------------------------------------------------------------ CharUI (Lua driven)

        private const string MetaCharDir = ResourcesRoot + "/Meta/Char";
        private const string PassiveCharDir = ResourcesRoot + "/Meta/Passive";
        private const string SpriteCharDir = ResourcesRoot + "/Sprite/Char";

        private static readonly Color[] RarityColors = {
            new Color(0.62f, 0.64f, 0.67f), // 1
            new Color(0.55f, 0.76f, 0.36f), // 2
            new Color(0.33f, 0.62f, 0.86f), // 3
            new Color(0.63f, 0.50f, 0.82f), // 4
            new Color(0.95f, 0.80f, 0.25f), // 5
            new Color(0.96f, 0.55f, 0.20f)  // 6
        };

        /// <summary>
        /// 一个可玩干员的全部占位数据 / Everything one playable operator is seeded from.
        ///
        /// 音游数值来自 GDD / The rhythm numbers are the GDD's MVP table (1x5*, 2x4*, 1x3*), and the
        /// passives are its Character-Driven Gameplay examples. The names are invented - the GDD
        /// only ever says "Nhân vật A/B/C/D" - so change them here and nowhere else.
        /// </summary>
        /// <summary>
        /// 只给生成器用的映射 / Which PassiveSO subclass an operator gets. A switch on this is
        /// fine because it lives in the generator: adding a passive to the game means adding a
        /// subclass and an asset, and only seeding a NEW placeholder operator touches this.
        /// The runtime never branches on a passive's kind - that is the whole point of the
        /// subclasses.
        /// </summary>
        private enum PassiveKind { ComboShield, JudgeUpgrade, FeverExtend, Regen }

        private readonly struct OperatorSeed {
            public readonly string Id;
            public readonly string English;
            public readonly string Chinese;
            public readonly int Rarity;
            public readonly CharProfession Profession;
            public readonly int MaxHp;
            public readonly float Score;
            public readonly float Fever;
            public readonly string PassiveName;
            public readonly string PassiveText;
            public readonly PassiveKind Kind;

            public OperatorSeed(string id, string english, string chinese, int rarity,
                                CharProfession profession, int maxHp, float score, float fever,
                                string passiveName, string passiveText, PassiveKind kind) {
                Id = id; English = english; Chinese = chinese; Rarity = rarity;
                Profession = profession; MaxHp = maxHp; Score = score; Fever = fever;
                PassiveName = passiveName; PassiveText = passiveText; Kind = kind;
            }
        }

        // AMIYA 保持第一个 / AMIYA stays, as the 5-star. Removing it would break
        // PlayerData.Initialization, both committed saves, and the Lua roster; repurposing it
        // costs nothing and lands exactly on the GDD's 1/2/1 rarity split.
        private static readonly OperatorSeed[] Operators = {
            new OperatorSeed("AMIYA", "Amiya", "阿米娅", 5, CharProfession.SHU_SHI,
                300, 1.2f, 1.2f, "Field Medic",
                "The first Miss of a run does not break the combo.",
                PassiveKind.ComboShield),
            new OperatorSeed("NOVA", "Nova", "新星", 4, CharProfession.JU_JI,
                275, 1.1f, 1.1f, "Overdrive",
                "A Great has a chance to be counted as a Perfect.",
                PassiveKind.JudgeUpgrade),
            new OperatorSeed("ECHO", "Echo", "回声", 4, CharProfession.YI_LIAO,
                275, 1.1f, 1.1f, "Resonance",
                "A high combo extends how long Fever lasts.",
                PassiveKind.FeverExtend),
            // PULSE 原本没有效果 / PULSE used to read "no special effect". A baseline operator
            // that changes nothing cannot demonstrate the Character-Driven Gameplay pillar,
            // so it takes the GDD's regen instead.
            new OperatorSeed("PULSE", "Pulse", "脉冲", 3, CharProfession.XIAN_FENG,
                250, 1.0f, 1.0f, "Steady Beat",
                "Recovers a little HP at a steady interval.",
                PassiveKind.Regen)
        };

        /// <summary>
        /// CharManager 用名字当字典键, 所以每张图的文件名都是硬约定
        /// CharManager keys its dictionaries by sprite name, so these filenames are the contract:
        ///   CardGround/{rarity}_1..4 and {rarity}_star   (the _1..4 set is filtered by ^\d_.+)
        ///   ProfessionSmall/{CharProfession}             (keyed by the enum's ToString)
        ///   Elite/card_1, card_2
        /// 找不到就是 KeyNotFoundException, 因为取的时候没做存在判断
        /// A miss is a KeyNotFoundException rather than a null, because the getters index straight
        /// into the dictionary without checking.
        /// </summary>
        private static void BuildCharSprites() {
            for (int rarity = 1; rarity <= RarityColors.Length; rarity++) {
                Color tint = RarityColors[rarity - 1];
                CardGround(rarity + "_1", 64, 96, tint, 1f);
                // _2.._4 是叠在底图上的装饰层, 占位阶段留空, 但名字必须在
                // Layers 2-4 are decorative overlays on the real card art. They are blank here, but
                // the names have to exist or setData throws on the first lookup.
                CardGround(rarity + "_2", 8, 8, tint, 0f);
                CardGround(rarity + "_3", 8, 8, tint, 0f);
                CardGround(rarity + "_4", 8, 8, tint, 0f);
                BuildStarRowSprite(rarity + "_star", rarity);
            }

            // 八个职业各给一个能分辨的多边形 / a distinguishable polygon per profession
            ProfessionGlyph(CharProfession.XIAN_FENG, 3, 90f);
            ProfessionGlyph(CharProfession.JIN_WEI, 4, 45f);
            ProfessionGlyph(CharProfession.JU_JI, 5, 90f);
            ProfessionGlyph(CharProfession.ZHONG_ZHUANG, 6, 0f);
            ProfessionGlyph(CharProfession.YI_LIAO, 8, 22.5f);
            ProfessionGlyph(CharProfession.FU_ZHU, 4, 0f);
            ProfessionGlyph(CharProfession.SHU_SHI, 3, -90f);
            ProfessionGlyph(CharProfession.TE_ZHONG, 5, -90f);

            EliteBadge("card_1", 1);
            EliteBadge("card_2", 2);

            // 详情页要的另外四套 / the four sets CharInfoUI indexes
            //   Star/info_N        GetStarImage("info_" + rarity)
            //   Profession/{enum}  GetProfessionImage - 大图, 和卡片上的小图分开
            //   Elite/big_N        GetCharEliteImage("big_" + elite) - 精英0也要, 干员初始就是0
            //   Camp/{enum}        GetCampImage(camp.ToString())
            // big_0 matters: a fresh operator is elite 0, so that key is hit immediately.
            for (int rarity = 1; rarity <= RarityColors.Length; rarity++) {
                StarRow(SpriteCharDir + "/Star", "info_" + rarity, rarity);
            }
            for (int elite = 0; elite <= 2; elite++) {
                EliteBadgeAt(SpriteCharDir + "/Elite", "big_" + elite, elite);
            }
            foreach (CharProfession profession in System.Enum.GetValues(typeof(CharProfession))) {
                PolygonSprite(SpriteCharDir + "/Profession", profession.ToString(), 96,
                    3 + ((int)profession % 6), (int)profession * 17f);
            }
            int campIndex = 0;
            foreach (CharCamp camp in System.Enum.GetValues(typeof(CharCamp))) {
                PolygonSprite(SpriteCharDir + "/Camp", camp.ToString(), 96,
                    3 + (campIndex % 6), campIndex * 23f);
                campIndex++;
            }
        }

        private static void CardGround(string name, int width, int height, Color tint, float alpha) {
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++) {
                // 竖向渐变, 底部更暗, 和参考图的卡面一样 / vertical fade, darker at the foot
                float t = y / (float)(height - 1);
                Color row = Color.Lerp(tint * 0.25f, tint, t);
                for (int x = 0; x < width; x++) {
                    pixels[y * width + x] = new Color(row.r, row.g, row.b, alpha);
                }
            }
            ImportSpriteAt(SpriteCharDir + "/CardGround", name, pixels, width, height, Vector4.zero);
        }

        /// <summary>一排五角星, 星数就是稀有度 / a row of five-pointed stars, one per rarity point.</summary>
        private static void BuildStarRowSprite(string name, int count) {
            StarRow(SpriteCharDir + "/CardGround", name, count);
        }

        private static void StarRow(string dir, string name, int count) {
            const int cell = 32;
            int width = cell * count;
            Color[] pixels = new Color[width * cell];

            for (int star = 0; star < count; star++) {
                Vector2 centre = new Vector2(star * cell + cell * 0.5f, cell * 0.5f);
                for (int y = 0; y < cell; y++) {
                    for (int x = 0; x < cell; x++) {
                        Vector2 point = new Vector2(star * cell + x + 0.5f, y + 0.5f);
                        if (!InsideStar(point - centre, cell * 0.46f, cell * 0.20f)) continue;
                        pixels[y * width + (star * cell + x)] = Color.white;
                    }
                }
            }
            ImportSpriteAt(dir, name, pixels, width, cell, Vector4.zero);
        }

        /// <summary>
        /// 十个顶点的多边形判定 / point-in-polygon against the star's ten alternating vertices.
        /// 用极角直接算比逐边求交简单 / comparing against the interpolated radius at this angle is
        /// simpler than a full edge-crossing test and exact enough at this size.
        /// </summary>
        private static bool InsideStar(Vector2 offset, float outer, float inner) {
            float angle = Mathf.Atan2(offset.y, offset.x);
            float spike = Mathf.PI * 2f / 5f;
            // 把角度折到一个尖角内 / fold the angle into a single spike, then into half of it
            float local = Mathf.Repeat(angle + Mathf.PI * 0.5f, spike);
            float half = spike * 0.5f;
            float t = Mathf.Abs(local - half) / half;          // 1 at the tip, 0 at the valley
            return offset.magnitude <= Mathf.Lerp(inner, outer, t);
        }

        private static void ProfessionGlyph(CharProfession profession, int sides, float rotation) {
            PolygonSprite(SpriteCharDir + "/ProfessionSmall", profession.ToString(), 48, sides, rotation);
        }

        private static Sprite PolygonSprite(string dir, string name, int size, int sides, float rotation) {
            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            StampPolygon(pixels, size, size, new Vector2(half, half), half * 0.88f,
                sides, rotation, Color.white);
            return ImportSpriteAt(dir, name, pixels, size, size, Vector4.zero);
        }

        /// <summary>
        /// 把一个正多边形盖到像素数组上 / Stamps a regular n-gon into an existing pixel buffer.
        ///
        /// 从 PolygonSprite 里抽出来 / Lifted out of PolygonSprite so the operator avatar and
        /// portrait can draw the same silhouette over a gradient instead of onto an empty square.
        /// </summary>
        private static void StampPolygon(Color[] pixels, int width, int height, Vector2 centre,
                                         float radius, int sides, float rotation, Color colour) {
            float offsetAngle = rotation * Mathf.Deg2Rad;
            // 正n边形: 极角折进一个扇区后, 边到中心的距离是 cos 关系
            // Regular n-gon: fold the angle into one sector, where the edge sits at
            // radius * cos(sector/2) / cos(foldedAngle).
            float sector = Mathf.PI * 2f / sides;

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    Vector2 offset = new Vector2(x + 0.5f - centre.x, y + 0.5f - centre.y);
                    float angle = Mathf.Atan2(offset.y, offset.x) - offsetAngle;
                    float folded = Mathf.Repeat(angle, sector) - sector * 0.5f;
                    float edge = radius * Mathf.Cos(sector * 0.5f) / Mathf.Cos(folded);
                    if (offset.magnitude <= edge) pixels[y * width + x] = colour;
                }
            }
        }

        /// <summary>
        /// 干员头像 / The grid cell icon, written to Sprite/Char/Avatar/{id}.
        ///
        /// 不能放进 CardGround / Deliberately its own folder. CharManager's constructor scans
        /// Sprite/Char/{Camp,CardGround,Elite,Profession,ProfessionSmall,Star} and every Add is
        /// unguarded, so a file landing in CardGround whose name matches ^\d_.+ would throw on a
        /// duplicate key before the game even starts. Avatar/Portrait/Passive are not scanned.
        /// </summary>
        private static Sprite OperatorAvatar(OperatorSeed seed, int index) {
            const int size = 192;
            Color tint = RarityColors[seed.Rarity - 1];
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++) {
                float t = y / (float)(size - 1);
                Color row = Color.Lerp(tint * 0.25f, tint, t);
                for (int x = 0; x < size; x++) pixels[y * size + x] = new Color(row.r, row.g, row.b, 1f);
            }

            // 从五边形起步 / Five sides upward, never three: a triangle silhouette next to the
            // screen's own PLAY button reads as a second play button.
            StampPolygon(pixels, size, size, new Vector2(size * 0.5f, size * 0.52f), size * 0.30f,
                5 + index % 4, index * 29f, new Color(1f, 1f, 1f, 0.92f));

            return ImportSpriteAt(SpriteCharDir + "/Avatar", seed.Id, pixels, size, size, Vector4.zero);
        }

        /// <summary>
        /// 干员立绘 / The centre-panel portrait, written to Sprite/Char/Portrait/{id}.
        ///
        /// 底部压暗一条 / The darker band across the bottom third is what stops it reading as a
        /// blown-up icon: it gives the silhouette something to stand on.
        /// </summary>
        private static Sprite OperatorPortrait(OperatorSeed seed, int index) {
            const int width = 512;
            const int height = 768;
            Color tint = RarityColors[seed.Rarity - 1];
            Color[] pixels = new Color[width * height];

            for (int y = 0; y < height; y++) {
                float t = y / (float)(height - 1);
                Color row = Color.Lerp(tint * 0.18f, tint * 0.85f, t);
                if (t < 0.28f) row *= 0.55f;
                for (int x = 0; x < width; x++) pixels[y * width + x] = new Color(row.r, row.g, row.b, 1f);
            }

            StampPolygon(pixels, width, height, new Vector2(width * 0.5f, height * 0.58f), width * 0.34f,
                5 + index % 4, index * 29f, new Color(1f, 1f, 1f, 0.90f));

            return ImportSpriteAt(SpriteCharDir + "/Portrait", seed.Id, pixels, width, height, Vector4.zero);
        }

        private static void EliteBadge(string name, int level) {
            EliteBadgeAt(SpriteCharDir + "/Elite", name, level);
        }

        private static void EliteBadgeAt(string dir, string name, int level) {
            const int size = 48;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    // level 道横杠 / one chevron bar per elite level
                    int band = y * (level * 2 + 1) / size;
                    if (band % 2 == 1) pixels[y * size + x] = Color.white;
                }
            }
            ImportSpriteAt(dir, name, pixels, size, size, Vector4.zero);
        }

        /// <summary>
        /// 造一个干员元数据 / authors one CharMeta asset.
        /// 字段都是 private [SerializeField], 所以只能走 SerializedObject 写
        /// Every field is private and serialized, so SerializedObject is the only way in.
        /// atkRange 必须非空: OnEnable 会遍历它, 而 CreateInstance 就会触发一次 OnEnable
        /// atkRange must end up non-null - OnEnable walks it, and CreateInstance fires OnEnable.
        /// </summary>
        /// <summary>
        /// 一个干员的元数据 / One operator's CharMeta, both stat blocks at once.
        ///
        /// 塔防那块必须继续写 / The elite/level attribute block below is not dead weight: it feeds
        /// CharData.GetAttribute(), which CharInfoUI and the Lua roster both read. Skipping it for
        /// the rhythm operators would have GetAttribute() index an empty array. The rhythm block is
        /// what CharSelectUI shows; the tower-defense block is what the inherited screens show.
        /// </summary>
        /// <summary>
        /// 造出这个干员的被动资产 / Creates the PassiveSO asset for one operator.
        ///
        /// 幂等 / Idempotent through DeleteAsset + CreateAsset at a fixed path, matching how
        /// BuildCharMeta already handles re-runs: the GUID changes, but every reference to it
        /// is rewritten in the same pass.
        /// </summary>
        private static PassiveSO BuildPassive(OperatorSeed seed, Sprite icon) {
            EnsureFolder(PassiveCharDir);

            PassiveSO asset;
            switch (seed.Kind) {
                case PassiveKind.ComboShield:
                    asset = ScriptableObject.CreateInstance<ComboShieldPassive>();
                    ((ComboShieldPassive)asset).charges = 1;
                    break;
                case PassiveKind.JudgeUpgrade:
                    asset = ScriptableObject.CreateInstance<JudgeUpgradePassive>();
                    ((JudgeUpgradePassive)asset).chance = 0.25f;
                    break;
                case PassiveKind.FeverExtend:
                    asset = ScriptableObject.CreateInstance<FeverExtendPassive>();
                    ((FeverExtendPassive)asset).comboThreshold = 50;
                    ((FeverExtendPassive)asset).extraSeconds = 3f;
                    break;
                default:
                    asset = ScriptableObject.CreateInstance<RegenPassive>();
                    ((RegenPassive)asset).interval = 10f;
                    ((RegenPassive)asset).amount = 5;
                    break;
            }

            asset.id = seed.Id;
            asset.passiveName = seed.PassiveName;
            asset.description = seed.PassiveText;
            asset.icon = icon;

            string path = $"{PassiveCharDir}/{seed.Id}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);

            return asset;
        }

        private static void BuildCharMeta(OperatorSeed seed, Sprite avatar, Sprite portrait,
                                          PassiveSO passive) {
            EnsureFolder(MetaCharDir);
            CharMeta meta = ScriptableObject.CreateInstance<CharMeta>();

            SerializedObject so = new SerializedObject(meta);
            so.FindProperty("chineseName").stringValue = seed.Chinese;
            so.FindProperty("englishName").stringValue = seed.English;
            so.FindProperty("rarity").intValue = seed.Rarity;
            so.FindProperty("profession").enumValueIndex = (int)seed.Profession;
            so.FindProperty("charPosition").enumValueIndex = (int)CharPosition.YUAN_CHENG;
            so.FindProperty("camp").enumValueIndex = (int)CharCamp.LDD;
            so.FindProperty("feature").stringValue = "Placeholder operator - see SETUP.md.";

            SerializedProperty range = so.FindProperty("atkRange");
            range.arraySize = 1;
            range.GetArrayElementAtIndex(0).stringValue = "ooo";

            // 精英/等级属性各三档(0/1/2), GetAttribute 会按精英阶级索引
            // Three entries each, because GetAttribute indexes these by elite rank.
            SerializedProperty elite = so.FindProperty("eliteAttributes");
            SerializedProperty perLevel = so.FindProperty("levelAttributes");
            elite.arraySize = 3;
            perLevel.arraySize = 3;

            // GetAttribute 是 levelAttributes[elite] * level + eliteAttributes[elite], 干员起始等级是0,
            // 所以基础值必须放在 eliteAttributes[0], 否则详情页全是0
            // GetAttribute computes levelAttributes[elite] * level + eliteAttributes[elite], and a
            // new operator starts at level 0 - so the base numbers have to live in eliteAttributes[0]
            // or every stat on the detail screen reads zero.
            for (int i = 0; i < 3; i++) {
                float scale = 1f + i * 0.55f;
                SetAttribute(elite, i, 1060f * scale, 264f * scale, 83f * scale, 0f, 1.3f, 70, 18, 1);
                SetAttribute(perLevel, i, 24f, 5.2f, 1.6f, 0f, 0f, 0, 0, 0);
            }

            so.FindProperty("tags").arraySize = 0;
            SerializedProperty talent = so.FindProperty("talent");
            talent.arraySize = 1;
            talent.GetArrayElementAtIndex(0).stringValue = seed.PassiveName;

            // 音游数值 / The rhythm block - the one CharSelectUI reads.
            so.FindProperty("maxHp").intValue = seed.MaxHp;
            so.FindProperty("scoreModifier").floatValue = seed.Score;
            so.FindProperty("feverModifier").floatValue = seed.Fever;

            // 现在是资产引用 / An asset reference now, not three nested strings. The passive
            // carries its own name, text and icon, so there is one copy of them rather than a
            // display copy on the meta and a behaviour copy somewhere else to drift apart.
            so.FindProperty("passive").objectReferenceValue = passive;

            // image2 也给图是顺手的好处 / Assigning image2 as well is a free win: the Lua roster in
            // CharUI draws flat grey today because GetCharImage() returns null for every operator.
            so.FindProperty("image1").objectReferenceValue = portrait;   // 立绘, centre panel
            so.FindProperty("image2").objectReferenceValue = portrait;   // 卡贴, CharUI's cards
            so.FindProperty("image3").objectReferenceValue = avatar;     // 头像, grid cell

            so.ApplyModifiedPropertiesWithoutUndo();

            string path = MetaCharDir + "/" + seed.Id + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meta, path);
        }

        private static void SetAttribute(SerializedProperty array, int index, float health, float atk,
                                         float def, float resistance, float atkSpeed,
                                         int respawn, int cost, int block) {
            SerializedProperty entry = array.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("maxHealth").floatValue = health;
            entry.FindPropertyRelative("atk").floatValue = atk;
            entry.FindPropertyRelative("def").floatValue = def;
            entry.FindPropertyRelative("magicResistance").floatValue = resistance;
            entry.FindPropertyRelative("atkSpeed").floatValue = atkSpeed;
            entry.FindPropertyRelative("respawnTime").intValue = respawn;
            entry.FindPropertyRelative("cost").intValue = cost;
            entry.FindPropertyRelative("block").intValue = block;
        }

        /// <summary>
        /// 干员列表 / the operator roster. Lua驱动, injections 的名字对应 CharUI.lua.txt 里的全局变量
        /// Lua driven: the injection names are the globals CharUI.lua.txt reads, and the card's child
        /// names are what getCharCard resolves with transform:Find.
        ///
        /// 卡片的位置是 GridLayoutGroup 排的 - Lua 只调 SetSiblingIndex, 不设坐标
        /// The grid does the positioning. Lua only ever calls SetSiblingIndex to reorder cards; it
        /// never assigns a position, so without a layout group every card would sit on top of the
        /// first one.
        /// </summary>
        private static void BuildCharUIPrefab() {
            GameObject root = NewUIRoot("CharUI");
            RuaUI ui = root.AddComponent<RuaUI>();

            Rect(root, "BackGround", Stretch).AddComponent<Image>().color = HomeInk;

            GameObject topBar = Rect(root, "TopBar", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 110);
            });
            topBar.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.12f, 0.96f);

            Button back = FlatBtn(topBar, "back", "‹", new Vector2(-830, 0), new Vector2(170, 68), 46, false);
            back.GetComponent<Image>().color = new Color(0.30f, 0.31f, 0.33f, 0.9f);
            WireHide(back, ui, "CharUI");

            // 两个排序开关是互斥的, 所以放同一个 ToggleGroup / mutually exclusive, hence one group
            ToggleGroup sortGroup = topBar.AddComponent<ToggleGroup>();
            Toggle togLevel = SortToggle(topBar, "tog_level", "LEVEL", new Vector2(560, 0), sortGroup, true);
            Toggle togRarity = SortToggle(topBar, "tog_rarity", "RARITY", new Vector2(740, 0), sortGroup, false);
            FlatBtn(topBar, "SortOrder", "≡", new Vector2(880, 0), new Vector2(90, 68), 32, false);

            // 横向滚动: ScrollView/Viewport(Mask)/Content, 卡片模板必须在 Content 里
            // Lua 是 Instantiate(cardPrefab, cardPrefab.parent), 所以模板的父级就是滚动内容
            // Horizontal scroll. The template has to live inside Content because the Lua spawns with
            // Instantiate(cardPrefab, cardPrefab.parent) - the template's parent IS the container.
            GameObject scrollView = Rect(root, "ScrollView", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 1);
                r.offsetMin = new Vector2(40, 60);
                r.offsetMax = new Vector2(-40, -130);
            });
            ScrollRect scroll = scrollView.AddComponent<ScrollRect>();

            GameObject viewport = Rect(scrollView, "Viewport", Stretch);
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.AddComponent<Mask>().showMaskGraphic = true;

            GameObject content = Rect(viewport, "Content", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(0, 856);
            });

            // 固定两行往右长 / two fixed rows, growing rightward
            GridLayoutGroup layout = content.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(200, 420);
            layout.spacing = new Vector2(16, 16);
            layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            layout.constraintCount = 2;
            layout.startAxis = GridLayoutGroup.Axis.Vertical;
            layout.childAlignment = TextAnchor.UpperLeft;

            // 只让宽度跟着内容走, 高度是两行固定的
            // Only the width tracks the content; the height is the two fixed rows.
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = (RectTransform)content.transform;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            GameObject card = BuildCharCard(content);
            card.SetActive(false);

            ui.injections = new[] {
                Inject("cardPrefab", (RectTransform)card.transform),
                Inject("tog_level", togLevel),
                Inject("tog_rarity", togRarity),
            };

            SavePrefab(root, "CharUI");
        }

        private static Toggle SortToggle(GameObject parent, string name, string caption, Vector2 pos,
                                         ToggleGroup group, bool on) {
            GameObject go = Rect(parent, name, At(pos, new Vector2(170, 68)));
            Toggle toggle = go.AddComponent<Toggle>();

            GameObject background = Rect(go, "Background", Stretch);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(1f, 1f, 1f, 0.06f);

            GameObject selected = Rect(go, "Selected", Stretch);
            Image selectedImage = selected.AddComponent<Image>();
            selectedImage.color = new Color(0.25f, 0.55f, 0.85f, 0.35f);

            RowLabel(go, "Label", caption, 24, Color.white, Vector2.zero, 170, TextAnchor.MiddleCenter);

            toggle.targetGraphic = backgroundImage;
            toggle.graphic = selectedImage;
            toggle.group = group;
            toggle.isOn = on;
            return toggle;
        }

        /// <summary>
        /// 卡片模板 / the card template. 子物体名字全部由 getCharCard 硬编码
        /// Every child name here is hard-coded in getCharCard, and the root needs a Button because
        /// the Lua attaches its click handler with transform:GetComponent("Button").
        ///
        /// y缩放从0开始, 让 ScaleY 补间把它长出来 / authored at y-scale 0 so the tween grows it in
        /// </summary>
        private static GameObject BuildCharCard(GameObject grid) {
            GameObject card = Rect(grid, "cardPrefab", r => { });
            card.transform.localScale = new Vector3(1, 0, 1);
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = new Color(0.13f, 0.14f, 0.16f);
            card.AddComponent<Button>().targetGraphic = cardImage;

            // 四层卡面底图 / the four card-ground layers, filled by CharManager at runtime
            CardLayer(card, "img_rarity_1");
            CardLayer(card, "img_rarity_2");
            CardLayer(card, "img_rarity_3");
            CardLayer(card, "img_rarity_4");

            // 立绘: 没有美术资源时 sprite 是 null, Image 就渲染成一块纯色
            // The portrait. With no art the sprite resolves to null and the Image simply draws a
            // flat colour, which is the placeholder.
            GameObject portrait = Rect(card, "img_char_card", r => {
                r.anchorMin = new Vector2(0, 0.18f);
                r.anchorMax = new Vector2(1, 1);
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
            });
            Image portraitImage = portrait.AddComponent<Image>();
            portraitImage.color = new Color(0.42f, 0.44f, 0.48f);
            portraitImage.raycastTarget = false;

            GameObject profession = Rect(card, "img_profession", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(40, 40);
                r.anchoredPosition = new Vector2(8, -8);
            });
            Image professionImage = profession.AddComponent<Image>();
            professionImage.color = Color.white;
            professionImage.raycastTarget = false;

            GameObject stars = Rect(card, "img_rarity_star", r => {
                r.anchorMin = r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(1, 1);
                r.sizeDelta = new Vector2(120, 24);
                r.anchoredPosition = new Vector2(-8, -12);
            });
            Image starsImage = stars.AddComponent<Image>();
            starsImage.color = new Color(0.98f, 0.82f, 0.25f);
            starsImage.raycastTarget = false;

            GameObject elite = Rect(card, "img_elite", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(44, 44);
                r.anchoredPosition = new Vector2(10, 10);
            });
            Image eliteImage = elite.AddComponent<Image>();
            eliteImage.color = Color.white;
            eliteImage.raycastTarget = false;
            elite.SetActive(false);   // Lua 按精英阶级开关 / Lua toggles this by elite rank

            // 等级环 / the level ring, matching the reference's circular badge
            GameObject expGround = Rect(card, "img_exp_ground", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0);
                r.pivot = new Vector2(0, 0);
                r.sizeDelta = new Vector2(74, 74);
                r.anchoredPosition = new Vector2(8, 18);
            });
            Image expGroundImage = expGround.AddComponent<Image>();
            expGroundImage.sprite = levelRing;
            expGroundImage.color = new Color(1f, 1f, 1f, 0.18f);
            expGroundImage.raycastTarget = false;

            GameObject expFill = Rect(expGround, "img_exp_percentage", Stretch);
            Image expImage = expFill.AddComponent<Image>();
            expImage.sprite = levelRing;
            expImage.color = new Color(0.98f, 0.82f, 0.25f);
            expImage.type = Image.Type.Filled;
            expImage.fillMethod = Image.FillMethod.Radial360;
            expImage.fillOrigin = (int)Image.Origin360.Top;
            expImage.raycastTarget = false;

            // txt_level 必须是卡片的直接子物体: Lua 用 transform:Find("txt_level"), 不带路径的
            // Find 只看直接子级, 塞进 img_exp_ground 里就返回 nil
            // txt_level has to be a direct child. The Lua calls transform:Find("txt_level"), and a
            // bare name only searches direct children - nesting it under the ring returns nil, and
            // the Lua then indexes that nil straight away.
            Text level = RowLabel(card, "txt_level", "1", 30, Color.white,
                new Vector2(45, 49), 74, TextAnchor.MiddleCenter);
            RectTransform levelRect = (RectTransform)level.transform;
            levelRect.anchorMin = levelRect.anchorMax = Vector2.zero;
            levelRect.pivot = new Vector2(0.5f, 0.5f);
            levelRect.anchoredPosition = new Vector2(45, 49);

            Text lvCaption = RowLabel(card, "LvCaption", "LV", 13, new Color(1f, 1f, 1f, 0.7f),
                new Vector2(45, 72), 74, TextAnchor.MiddleCenter);
            RectTransform lvRect = (RectTransform)lvCaption.transform;
            lvRect.anchorMin = lvRect.anchorMax = Vector2.zero;
            lvRect.pivot = new Vector2(0.5f, 0.5f);
            lvRect.anchoredPosition = new Vector2(45, 72);

            GameObject nameGround = Rect(card, "NameGround", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(0.5f, 0);
                r.sizeDelta = new Vector2(0, 56);
            });
            nameGround.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.07f, 0.92f);

            // 卡片宽200, 名字要贴右边又不能出界, 所以框跟卡片同宽再留点内边距
            // The card is 200 wide; right-aligning inside a box that is itself offset pushed the
            // name past the edge, so the box spans the card and only the padding moves it in.
            RowLabel(card, "txt_name", "Operator", 22, Color.white, new Vector2(-10, -172), 180,
                TextAnchor.MiddleRight);
            return card;
        }

        /// <summary>
        /// 干员详情 / the operator detail screen.
        ///
        /// CharInfoUI 会把 prefab 克隆三份左右滑动, 所以 prefab 的父级就是 ScrollRect 的 content,
        /// 三块面板并排, horizontalNormalizedPosition 的 0/0.5/1 正好对应左中右
        /// Init clones `prefab` three times into prefab.parent and swipes between them, so that
        /// parent is the ScrollRect's content: three canvas-wide panels side by side, which is what
        /// makes horizontalNormalizedPosition 0 / 0.5 / 1 land on left, centre and right.
        ///
        /// 面板里每一个名字都是 CharPrefab 用路径字符串找的, 错一个就是空引用
        /// Every name inside the panel is resolved by path string in CharPrefab's constructor and
        /// init(), so a single wrong name is a null reference before the screen draws.
        /// </summary>
        private static void BuildCharInfoUIPrefab() {
            GameObject root = NewUIRoot("CharInfoUI");
            CharInfoUI ui = root.AddComponent<CharInfoUI>();
            ScrollRect scroll = root.AddComponent<ScrollRect>();

            Rect(root, "BackGround", Stretch).AddComponent<Image>().color = new Color(0.09f, 0.09f, 0.10f);

            GameObject viewport = Rect(root, "Viewport", Stretch);
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = true;

            GameObject content = Rect(viewport, "Content", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(0, 0);
            });
            HorizontalLayoutGroup row = content.AddComponent<HorizontalLayoutGroup>();
            // 面板宽度是 Init 里按画布尺寸设的, 所以这里绝不能让布局去控制子物体尺寸
            // Init assigns each panel's size from the canvas, so the layout group must not control
            // child size or it would immediately overwrite that.
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = (RectTransform)content.transform;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Unrestricted;

            ui.prefab = (RectTransform)BuildCharPanel(content).transform;

            // 返回键挂在根节点上, 不进滚动内容:
            // Init 会把 prefab 克隆三份, 放面板里就会变成三个跟着左右滑的按钮; 而且它在 ScrollRect
            // 里面, 从按钮上起手的拖拽会被当成滚动而不是点击
            // The back button sits on the root rather than inside the scroll content. Init clones
            // the panel three times, so putting it in the card would give three buttons sliding with
            // the carousel - and inside a ScrollRect a drag begun on the button scrolls instead of
            // clicking. Created after Viewport so it draws over the panels, and before
            // professionHelp so that overlay still covers it.
            Button back = BackBtn(root, "back");
            ((RectTransform)back.transform).anchoredPosition = new Vector2(48, -48);
            WireHide(back, ui, "CharInfoUI");

            // 职业说明浮层 / the profession help overlay
            GameObject help = Rect(root, "professionHelp", Stretch);
            Image helpImage = help.AddComponent<Image>();
            helpImage.color = new Color(0.04f, 0.04f, 0.05f, 0.88f);
            help.AddComponent<CanvasGroup>();
            help.AddComponent<Button>().targetGraphic = helpImage;
            RowLabel(help, "HelpText", "Profession details go here.\nTap anywhere to close.", 30,
                Color.white, Vector2.zero, 900, TextAnchor.MiddleCenter);
            help.SetActive(false);
            ui.professionHelp = help.transform;

            SavePrefab(root, "CharInfoUI");
        }

        private static GameObject BuildCharPanel(GameObject content) {
            GameObject panel = Rect(content, "prefab", At(Vector2.zero, new Vector2(1920, 1080)));
            panel.AddComponent<CanvasGroup>();

            // 立绘和阵营标, 都在面板根下 / portrait and faction mark, both at the panel root
            GameObject art = Rect(panel, "img_char", At(new Vector2(60, 0), new Vector2(820, 1000)));
            Image artImage = art.AddComponent<Image>();
            artImage.color = new Color(0.30f, 0.31f, 0.34f);
            artImage.raycastTarget = false;

            // 阵营标放在返回键右边的同一条顶栏上:
            // 压在返回键上不行, 挪到它下面又会盖住属性栏第一行, 所以往右让开
            // The faction mark sits beside the back button on the same top strip. Under the button
            // it covered the first stat row, and on top of it the two collided outright - so it
            // moves sideways instead, which is the only direction that is actually free.
            GameObject camp = Rect(panel, "img_camp", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(150, 150);
                r.anchoredPosition = new Vector2(232, -26);
            });
            Image campImage = camp.AddComponent<Image>();
            campImage.color = Color.white;
            campImage.raycastTarget = false;

            GameObject info = Rect(panel, "CharInfoPanel", Stretch);

            // 三根柱子都贴着面板边缘锚定, 不用中心偏移:
            // 面板宽度是 Init 按 GetCanvasSize() 给的, 而那个宽度跟着画面宽高比变(这里是2160不是1920),
            // 所以任何按中心算的固定偏移在别的分辨率下都会跑到屏幕外
            // The three columns anchor to the panel's own edges rather than sitting at fixed offsets
            // from its centre. Init sizes each panel from GetCanvasSize(), which scales with the
            // display aspect - it came out 2160 wide here, not 1920 - so centre-relative offsets
            // drift off-screen as soon as the aspect changes.

            // ---- 左中: 属性 / MiddleLeftPanel: the stat block
            GameObject stats = Rect(info, "MiddleLeftPanel", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(520, 420);
                r.anchoredPosition = new Vector2(150, 120);
            });
            BarStat(stats, "Health", "HP", 150);
            BarStat(stats, "Atk", "ATK", 96);
            BarStat(stats, "Def", "DEF", 42);
            BarStat(stats, "MagicResistance", "RES", -12);
            PlainStat(stats, "RespawnTime", "REDEPLOY", 270, -66);
            PlainStat(stats, "Cost", "DP COST", 270, -112);
            PlainStat(stats, "Block", "BLOCK", 270, -158);
            PlainStat(stats, "AtkSpeed", "ATK SPD", 270, -204);

            GameObject trust = Rect(stats, "Trust", At(new Vector2(0, -260), new Vector2(520, 48)));
            RowLabel(trust, "Caption", "TRUST", 20, new Color(1f, 1f, 1f, 0.55f),
                new Vector2(-190, 0), 140, TextAnchor.MiddleLeft);
            GameObject trustGround = Rect(trust, "img_ground", At(new Vector2(60, 0), new Vector2(300, 8)));
            trustGround.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
            GameObject trustFill = Rect(trust, "img_trust_percentage", At(new Vector2(60, 0), new Vector2(300, 8)));
            Image trustImage = trustFill.AddComponent<Image>();
            trustImage.color = new Color(0.33f, 0.72f, 0.95f);
            trustImage.type = Image.Type.Filled;
            trustImage.fillMethod = Image.FillMethod.Horizontal;
            RowLabel(trust, "txt_trust_percentage", "0%", 22, Color.white,
                new Vector2(230, 0), 100, TextAnchor.MiddleRight);

            // ---- 左下: 名字/职业/位置/标签 / LowerLeftPanel
            GameObject lower = Rect(info, "LowerLeftPanel", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0);
                r.pivot = new Vector2(0, 0);
                r.sizeDelta = new Vector2(520, 330);
                r.anchoredPosition = new Vector2(150, 60);
            });
            GameObject rarity = Rect(lower, "img_rarity", At(new Vector2(-150, 130), new Vector2(220, 30)));
            Image rarityImage = rarity.AddComponent<Image>();
            rarityImage.color = new Color(0.98f, 0.82f, 0.25f);
            rarityImage.raycastTarget = false;

            RowLabel(lower, "txt_english_name", "Operator", 34, new Color(1f, 1f, 1f, 0.75f),
                new Vector2(-110, 76), 300, TextAnchor.MiddleLeft);
            RowLabel(lower, "txt_chinese_name", "干员", 62, Color.white,
                new Vector2(-80, 6), 360, TextAnchor.MiddleLeft);

            GameObject professionButton = Rect(lower, "img_profession",
                At(new Vector2(-200, -100), new Vector2(110, 110)));
            Image professionImage = professionButton.AddComponent<Image>();
            professionImage.color = Color.white;
            professionButton.AddComponent<Button>().targetGraphic = professionImage;

            GameObject positionGround = Rect(lower, "img_position_ground",
                At(new Vector2(20, -72), new Vector2(280, 46)));
            positionGround.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);
            RowLabel(positionGround, "txt_position", "Ranged", 24, Color.white,
                Vector2.zero, 280, TextAnchor.MiddleCenter);

            GameObject tagGround = Rect(lower, "img_tag_ground",
                At(new Vector2(20, -128), new Vector2(280, 46)));
            tagGround.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);
            RowLabel(tagGround, "txt_tag", "-", 22, Color.white,
                Vector2.zero, 280, TextAnchor.MiddleCenter);

            // ---- 右侧: 等级/精英/特性/天赋 / RightPanel
            GameObject right = Rect(info, "RightPanel", r => {
                r.anchorMin = r.anchorMax = new Vector2(1, 0.5f);
                r.pivot = new Vector2(1, 0.5f);
                r.sizeDelta = new Vector2(600, 940);
                r.anchoredPosition = new Vector2(-60, 0);
            });

            GameObject levelBlock = Rect(right, "Level", At(new Vector2(0, 340), new Vector2(560, 180)));
            Image levelImage = levelBlock.AddComponent<Image>();
            levelImage.color = new Color(1f, 1f, 1f, 0.08f);
            levelBlock.AddComponent<Button>().targetGraphic = levelImage;

            RowLabel(levelBlock, "Caption", "LV", 22, new Color(1f, 1f, 1f, 0.6f),
                new Vector2(-210, 48), 100, TextAnchor.MiddleLeft);
            RowLabel(levelBlock, "txt_level", "1", 66, Color.white,
                new Vector2(-150, -4), 180, TextAnchor.MiddleLeft);
            RowLabel(levelBlock, "txt_max_level", "50", 26, new Color(1f, 1f, 1f, 0.55f),
                new Vector2(-40, -52), 140, TextAnchor.MiddleLeft);

            GameObject expBox = Rect(levelBlock, "Image", At(new Vector2(140, 34), new Vector2(260, 52)));
            expBox.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            RowLabel(expBox, "txt_exp", "0/0", 24, Color.white, Vector2.zero, 250, TextAnchor.MiddleCenter);

            GameObject expGround = Rect(levelBlock, "img_exp_ground",
                At(new Vector2(140, -16), new Vector2(260, 10)));
            expGround.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
            GameObject expFill = Rect(expGround, "img_exp_percentage", Stretch);
            Image expImage = expFill.AddComponent<Image>();
            expImage.color = new Color(0.93f, 0.80f, 0.02f);
            expImage.type = Image.Type.Filled;
            expImage.fillMethod = Image.FillMethod.Horizontal;

            GameObject eliteBlock = Rect(right, "Elite", At(new Vector2(-140, 180), new Vector2(270, 130)));
            Image eliteBg = eliteBlock.AddComponent<Image>();
            eliteBg.color = new Color(1f, 1f, 1f, 0.08f);
            eliteBlock.AddComponent<Button>().targetGraphic = eliteBg;
            RowLabel(eliteBlock, "Caption", "PROMOTION", 20, new Color(1f, 1f, 1f, 0.6f),
                new Vector2(-60, 40), 200, TextAnchor.MiddleLeft);
            GameObject eliteIcon = Rect(eliteBlock, "img_elite", At(new Vector2(40, -10), new Vector2(90, 90)));
            Image eliteIconImage = eliteIcon.AddComponent<Image>();
            eliteIconImage.color = Color.white;
            eliteIconImage.raycastTarget = false;

            // 小标题要抬到正文上面: RowLabel 是垂直居中的, 标题和正文都按中心排会压在一起
            // The caption has to clear the body text. RowLabel centres vertically, so placing both
            // near the block's middle overlaps them.
            GameObject feature = Rect(right, "Feature", At(new Vector2(0, -30), new Vector2(560, 200)));
            RowLabel(feature, "Caption", "TRAIT", 22, new Color(1f, 1f, 1f, 0.5f),
                new Vector2(-200, 86), 200, TextAnchor.MiddleLeft);
            Text featureText = RowLabel(feature, "txt_feature", "-", 26, Color.white,
                new Vector2(0, -16), 540, TextAnchor.UpperLeft);
            ((RectTransform)featureText.transform).sizeDelta = new Vector2(540, 120);

            GameObject talent = Rect(right, "Talent", At(new Vector2(0, -290), new Vector2(560, 260)));
            RowLabel(talent, "Caption", "TALENT", 22, new Color(1f, 1f, 1f, 0.5f),
                new Vector2(-200, 112), 200, TextAnchor.MiddleLeft);

            GameObject list = Rect(talent, "List", At(new Vector2(0, -20), new Vector2(560, 200)));
            VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
            listLayout.childControlWidth = false;
            listLayout.childControlHeight = false;
            listLayout.childForceExpandWidth = false;
            listLayout.childForceExpandHeight = false;
            listLayout.spacing = 10f;
            listLayout.childAlignment = TextAnchor.UpperCenter;

            // 天赋条目模板: init() 会 Instantiate 它到同一个父级, 所以自己保持失活
            // The talent row template. init() clones it into the same parent, so it stays inactive.
            GameObject talentRow = Rect(list, "img_ground", At(Vector2.zero, new Vector2(520, 56)));
            talentRow.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            RowLabel(talentRow, "txt_talent", "-", 24, Color.white, Vector2.zero, 500, TextAnchor.MiddleLeft);
            talentRow.SetActive(false);

            return panel;
        }

        /// <summary>带进度条的属性行 / a stat row with a fill bar behind it.</summary>
        private static void BarStat(GameObject parent, string name, string caption, float y) {
            GameObject row = Rect(parent, name, At(new Vector2(-130, y), new Vector2(260, 44)));
            RowLabel(row, "Caption", caption, 20, new Color(1f, 1f, 1f, 0.55f),
                new Vector2(-80, 0), 120, TextAnchor.MiddleLeft);
            RowLabel(row, "txt_value", "0", 28, Color.white, new Vector2(60, 0), 120, TextAnchor.MiddleRight);

            GameObject ground = Rect(row, "img_ground", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(0.5f, 0);
                r.sizeDelta = new Vector2(0, 6);
            });
            ground.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

            GameObject fill = Rect(ground, "img_value_percentage", Stretch);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.95f, 0.95f, 0.96f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
        }

        /// <summary>只有数值的属性行 / a stat row that is just a caption and a value.</summary>
        private static void PlainStat(GameObject parent, string name, string caption, float x, float y) {
            GameObject row = Rect(parent, name, At(new Vector2(x - 130, y), new Vector2(260, 40)));
            RowLabel(row, "Caption", caption, 20, new Color(1f, 1f, 1f, 0.55f),
                new Vector2(-70, 0), 140, TextAnchor.MiddleLeft);
            RowLabel(row, "txt_value", "0", 26, Color.white, new Vector2(70, 0), 120, TextAnchor.MiddleRight);
        }

        private static void CardLayer(GameObject card, string name) {
            GameObject layer = Rect(card, name, Stretch);
            Image image = layer.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
        }

        // ------------------------------------------------------------------ Item + shop catalogue

        private const string MetaItemDir = ResourcesRoot + "/Meta/Item";
        private const string SpriteItemDir = ResourcesRoot + "/Sprite/Item";
        private const string ShopDataDir = ResourcesRoot + "/Data/Shop";

        /// <summary>
        /// 凭证交易所的货架 / the certificate exchange catalogue.
        /// 价格一律是6号(资质凭证), 对应侧边栏第一个分区; ShowPriceItem 就是按这个id筛的
        /// Everything is priced in item 6, the base certificate, which is what the sidebar's first
        /// zone selects - ShowPriceItem filters the list by exactly that price id.
        ///
        /// 卖的东西沿用已有的物品id: 1=合成玉 2=龙门币 (PlayerData.Initialization 就是这么发的),
        /// 其余是新编的
        /// The goods reuse the ids PlayerData already hands out - 1 is Orundum and 2 is LMD - with
        /// new ids only for things that did not exist yet.
        /// </summary>
        private static readonly int[,] ShopCatalogue = {
            // sellId, sellAmount, priceAmount
            { 10, 1,    240 },  // Headhunting Permit
            { 2,  4000, 10  },  // LMD
            { 11, 1,    10  },  // Basic Battle Record
            { 1,  100,  40  },  // Orundum
            { 12, 1,    8   },  // Recruitment Permit
            { 13, 1,    12  },  // Skill Summary Vol.1
        };

        private static void BuildItemAssets() {
            // ItemManager 构造时按 item_ground_{稀有度} 建字典, 名字不对就进不去
            // ItemManager keys these by the digits after "item_ground_", so the names are fixed.
            for (int rarity = 1; rarity <= 6; rarity++) {
                Color tint = RarityColors[rarity - 1];
                Color[] pixels = new Color[64 * 64];
                for (int y = 0; y < 64; y++) {
                    for (int x = 0; x < 64; x++) {
                        float t = y / 63f;
                        Color row = Color.Lerp(tint * 0.35f, tint, t);
                        pixels[y * 64 + x] = new Color(row.r, row.g, row.b, 1f);
                    }
                }
                ImportSpriteAt(SpriteItemDir + "/ItemGround", "item_ground_" + rarity, pixels, 64, 64, Vector4.zero);
            }

            BuildItemMeta(1, "Orundum", 4, "Synthetic material. Commonly spent on recruiting operators.");
            BuildItemMeta(2, "LMD", 4, "The standard currency of Lungmen.");
            BuildItemMeta(10, "Headhunting Permit", 5, "Permits a single headhunting attempt.");
            BuildItemMeta(11, "Basic Battle Record", 3, "Grants a small amount of operator experience.");
            BuildItemMeta(12, "Recruitment Permit", 4, "Permits one recruitment listing.");
            BuildItemMeta(13, "Skill Summary Vol.1", 3, "Used to raise an operator's skill level.");
        }

        private static void BuildItemMeta(int id, string name, int rarity, string useInfo) {
            EnsureFolder(MetaItemDir);

            // 每个物品给一个能分辨的多边形当图标 / a distinguishable polygon per item as its icon
            Sprite icon = PolygonSprite(SpriteItemDir + "/Icon", "item_" + id, 96, 3 + id % 5, id * 13f);

            ItemMeta meta = ScriptableObject.CreateInstance<ItemMeta>();
            SerializedObject so = new SerializedObject(meta);
            so.FindProperty("id").intValue = id;
            so.FindProperty("name").stringValue = name;
            so.FindProperty("rarity").intValue = rarity;
            so.FindProperty("useInfo").stringValue = useInfo;
            so.FindProperty("description").stringValue = useInfo;
            so.FindProperty("waysObtain").stringValue = "Certificate Exchange";
            so.FindProperty("icon").objectReferenceValue = icon;
            so.ApplyModifiedPropertiesWithoutUndo();

            // ItemManager 用 id.ToString() 当资源名 / ItemManager loads these by id.ToString()
            string path = MetaItemDir + "/" + id + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meta, path);
        }

        /// <summary>
        /// ShopManager 的字段初始化直接对 Asset.Load 的结果取 .list (ShopManager.cs:9), 没有这个资源
        /// 就是空引用 - 所以哪怕货架是空的, 这个列表也必须存在
        /// ShopManager's field initialiser dereferences Asset.Load(...).list with no null check, so
        /// this asset has to exist before anything touches that singleton.
        /// </summary>
        private static void BuildShopCatalogue() {
            EnsureFolder(ShopDataDir);

            ShopItemDataList list = ScriptableObject.CreateInstance<ShopItemDataList>();
            SerializedObject so = new SerializedObject(list);
            SerializedProperty entries = so.FindProperty("list");
            entries.arraySize = ShopCatalogue.GetLength(0);

            for (int i = 0; i < ShopCatalogue.GetLength(0); i++) {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                SerializedProperty sell = entry.FindPropertyRelative("sellItem");
                SerializedProperty price = entry.FindPropertyRelative("priceItem");
                sell.FindPropertyRelative("id").intValue = ShopCatalogue[i, 0];
                sell.FindPropertyRelative("amount").intValue = ShopCatalogue[i, 1];
                price.FindPropertyRelative("id").intValue = 6;
                price.FindPropertyRelative("amount").intValue = ShopCatalogue[i, 2];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = ShopDataDir + "/ShopItemDataList.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(list, path);
        }

        // ------------------------------------------------------------------ ShopUI

        /// <summary>
        /// 商店(外壳) / The store, as a shell.
        ///
        /// ShopUI.Init() 和两个内部类全部是按路径字符串找子物体的 (Expand.GetComponent -> Find().
        /// GetComponent), Find 找不到就直接空引用。所以下面每一个名字都是硬约定, 不能改:
        ///   CurrencyPanel/{ZZPZ,GJPZ,CGPZ,LMB,YS}/ValueGround/txt_value
        ///   ShopItemPanel/{ItemNameGround/Text, ItemIconGround/Image, ItemUseInfo,
        ///                  ItemAmountGround/ItemAmount, PriceIcon, PriceAmount,
        ///                  BuyAmountGround/Text, AllPriceIcon, AllPriceAmount,
        ///                  CloseButton, AddButton, TakeButton, MinButton, MaxButton, BuyButton}
        ///
        /// Every one of those names is load-bearing: Init and the two nested classes resolve their
        /// children by path string, and Transform.Find returning null is an immediate NRE. The buy
        /// panel is built in full even though it is hidden, because its constructor runs from Init.
        ///
        /// 货架是空的: ShopManager 的字段初始化直接对 Asset.Load 的结果取 .list (ShopManager.cs:9),
        /// 而 Data/Shop 这个包不存在, 所以只要调 ShowPriceItem 就会炸。标签页因此只做样子不接事件。
        ///
        /// The shelf is intentionally empty. ShopManager's field initialiser dereferences
        /// Asset.Load(...).list with no null check (ShopManager.cs:9) and the Data/Shop bundle does
        /// not exist, so calling ShowPriceItem throws the moment the singleton is constructed. The
        /// tabs are therefore visual only, and the five cards on screen are static placeholders.
        /// </summary>
        private static void BuildShopUIPrefab() {
            GameObject root = NewUIRoot("ShopUI");
            ShopUI ui = root.AddComponent<ShopUI>();

            Rect(root, "BackGround", Stretch).AddComponent<Image>().color =
                new Color(0.90f, 0.90f, 0.92f);

            ui.zzpz_icon = hexBadge;
            ui.gjpz_icon = hexBadge;
            ui.cgpz_icon = hexBadge;

            // ---- 顶栏 / top bar
            GameObject topBar = Rect(root, "TopBar", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 120);
            });
            topBar.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.19f, 0.96f);

            Button back = FlatBtn(topBar, "back", "‹", new Vector2(-830, 0), new Vector2(170, 68), 46, false);
            back.GetComponent<Image>().color = new Color(0.30f, 0.31f, 0.33f, 0.95f);
            WireHide(back, ui, "ShopUI");

            // 五种货币都要建, 少一个 Init 就空引用 - 参考图只画了源石一种
            // All five slots are mandatory; the reference only shows originite, but Init resolves
            // every one of them and would throw on the first missing path.
            //
            // 必须挂在根节点下: Init 是从 UIBase.transform 开始找 "CurrencyPanel/...", 塞进 TopBar
            // 里 Find 就返回 null
            // It has to hang off the root. Init resolves "CurrencyPanel/..." from UIBase.transform,
            // so nesting this inside TopBar - which is what reads naturally - makes Find return null
            // and Init throws before the screen ever draws.
            GameObject currency = Rect(root, "CurrencyPanel", r => {
                r.anchorMin = r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(1, 1);
                r.sizeDelta = new Vector2(1180, 100);
                r.anchoredPosition = new Vector2(-30, -10);
            });
            CurrencySlot(currency, "ZZPZ", "BASE CERT", ShopCert, -940);
            CurrencySlot(currency, "GJPZ", "SENIOR CERT", ShopCert, -710);
            CurrencySlot(currency, "CGPZ", "PURCHASE CERT", ShopCert, -470);
            CurrencySlot(currency, "LMB", "LMD", ShopCoin, -230);
            CurrencySlot(currency, "YS", "ORIGINITE", ShopGold, -20);

            // ---- 三页内容, 标签切换 / three pages, switched by the tabs
            GameObject originitePage = Rect(root, "OriginitePage", Stretch);
            GameObject certificatePage = Rect(root, "CertificatePage", Stretch);
            GameObject lmdPage = Rect(root, "LmdPage", Stretch);

            // ---- 三个交易所标签 / the three exchange tabs
            GameObject tabBar = Rect(root, "TabBar", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 84);
                r.anchoredPosition = new Vector2(0, -120);
            });
            tabBar.AddComponent<Image>().color = new Color(0.27f, 0.28f, 0.30f, 0.96f);
            ToggleGroup tabs = tabBar.AddComponent<ToggleGroup>();
            ExchangeTab(tabBar, "OriginiteTab", "ORIGINITE EXCHANGE", ShopGold, -640, true,
                tabs, originitePage, ui, -1);
            // 凭证页切过去时顺手拉一次货架: 用Int持久监听, 它带的是固定参数, 会忽略Toggle传的bool
            // Selecting the certificate tab also fills the shelf. An Int persistent listener carries
            // a fixed argument and ignores the bool the Toggle passes, which is what lets a
            // UnityEvent<bool> drive ShowPriceItem(int) with no glue code.
            ExchangeTab(tabBar, "CertificateTab", "CERTIFICATE EXCHANGE", ShopCert, 0, false,
                tabs, certificatePage, ui, 6);
            ExchangeTab(tabBar, "LmdTab", "LMD EXCHANGE", ShopCoin, 640, false,
                tabs, lmdPage, ui, -1);

            // ---- 源石页: 还是那五张美元卡, 静态的 / originite page keeps the static USD cards
            string[] amounts = { "6", "20", "40", "66", "130" };
            string[] names = {
                "Originite Cluster", "Originite Pile", "Originite Bag",
                "Originite Case", "Originite Crate"
            };
            string[] prices = { "$0.99", "$4.99", "$9.99", "$19.99", "$29.99" };
            float[] columns = { -673, -336, 0, 336, 673 };
            GameObject usdGrid = Rect(originitePage, "Grid", At(new Vector2(0, -70), new Vector2(1820, 760)));
            for (int i = 0; i < columns.Length; i++) {
                ShopCard(usdGrid, "Card" + i, columns[i], amounts[i], amounts[i], names[i], prices[i]);
            }

            // ---- 凭证页: 左侧分区 + 可横向滚动的货架 / sidebar plus a horizontally scrolling shelf
            CertificateZone(certificatePage, "BaseZone", "BASE CERTIFICATE", ShopCert, 250, ui, 6);
            CertificateZone(certificatePage, "SeniorZone", "SENIOR CERTIFICATE", ShopGold, 100, ui, 7);
            CertificateZone(certificatePage, "PurchaseZone", "PURCHASE CERTIFICATE", Hostile, -50, ui, 8);

            GameObject scrollView = Rect(certificatePage, "ShelfScroll", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 1);
                r.offsetMin = new Vector2(420, 60);
                r.offsetMax = new Vector2(-40, -220);
            });
            ScrollRect scroll = scrollView.AddComponent<ScrollRect>();

            GameObject viewport = Rect(scrollView, "Viewport", Stretch);
            viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            viewport.AddComponent<Mask>().showMaskGraphic = true;

            GameObject content = Rect(viewport, "Content", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(0, 700);
            });
            GridLayoutGroup shelf = content.AddComponent<GridLayoutGroup>();
            shelf.cellSize = new Vector2(300, 330);
            shelf.spacing = new Vector2(20, 20);
            shelf.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            shelf.constraintCount = 2;
            shelf.startAxis = GridLayoutGroup.Axis.Vertical;
            shelf.childAlignment = TextAnchor.UpperLeft;

            ContentSizeFitter shelfFitter = content.AddComponent<ContentSizeFitter>();
            shelfFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            shelfFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = (RectTransform)content.transform;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;

            // 模板必须在滚动内容里: ShowPriceItem 是 Instantiate(prefab, prefab.parent) 克隆的
            // The template lives inside Content because ShowPriceItem clones with
            // Instantiate(shopItemPrefab, shopItemPrefab.parent) - that parent IS the container.
            GameObject template = CatalogueCard(content, "ShopItemTemplate");
            template.SetActive(false);
            ui.shopItemPrefab = template.transform;

            // ---- 龙门页: 参考图第四张就是这个状态 / the fourth reference is exactly this state
            RowLabel(lmdPage, "NotOpen", "NOT YET AVAILABLE", 72, new Color(0.42f, 0.43f, 0.45f),
                new Vector2(0, -60), 1400, TextAnchor.MiddleCenter);

            certificatePage.SetActive(false);
            lmdPage.SetActive(false);

            BuildShopItemPanel(root);
            SavePrefab(root, "ShopUI");
        }

        /// <summary>
        /// 顶栏一格货币 / one currency readout. 路径 ValueGround/txt_value 是 Init 写死的
        /// The ValueGround/txt_value path is fixed by ShopUI.Init.
        /// </summary>
        private static void CurrencySlot(GameObject parent, string name, string caption, Color tint, float x) {
            GameObject slot = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(1, 0.5f);
                r.pivot = new Vector2(1, 0.5f);
                r.sizeDelta = new Vector2(210, 80);
                r.anchoredPosition = new Vector2(x, 0);
            });

            GameObject badge = Rect(slot, "Icon", At(new Vector2(-78, 6), new Vector2(40, 40)));
            Image badgeImage = badge.AddComponent<Image>();
            badgeImage.sprite = hexBadge;
            badgeImage.color = tint;
            badgeImage.raycastTarget = false;

            GameObject ground = Rect(slot, "ValueGround", At(new Vector2(28, 6), new Vector2(150, 44)));
            ground.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);
            RowLabel(ground, "txt_value", "0", 28, Color.white, new Vector2(-8, 0), 130, TextAnchor.MiddleRight);

            RowLabel(slot, "Caption", caption, 14, new Color(1f, 1f, 1f, 0.45f),
                new Vector2(0, -28), 210, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// 交易所标签 / an exchange tab.
        /// priceId 大于0时, 选中这一页会顺带调 ShowPriceItem 把货架填上
        /// When priceId is positive, selecting the tab also calls ShowPriceItem to fill the shelf.
        /// </summary>
        private static void ExchangeTab(GameObject parent, string name, string caption, Color tint,
                                        float x, bool selected, ToggleGroup group, GameObject page,
                                        ShopUI ui, int priceId) {
            GameObject tab = Rect(parent, name, At(new Vector2(x, 0), new Vector2(600, 84)));
            Toggle toggle = tab.AddComponent<Toggle>();
            Image background = tab.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0f);

            // 选中态用中灰而不是参考图的白底:
            // 白底就得配深色字, 但没选中时背景是深色条, 深色字又看不见; 而Toggle只淡入淡出graphic
            // 自己, 塞进它里面的文字不会跟着隐藏(和滑动开关踩的是同一个坑)。中灰两种状态下白字都清楚。
            // Mid grey rather than the reference's white: white would need dark type, which then
            // vanishes against the dark bar when unselected - and a caption nested inside `graphic`
            // would not hide with it, since Toggle cross-fades only that one Graphic. This is the
            // same trap the sliding switch hit. Mid grey keeps white type legible in both states.
            GameObject selectedGo = Rect(tab, "Selected", Stretch);
            Image selectedImage = selectedGo.AddComponent<Image>();
            selectedImage.color = new Color(0.40f, 0.41f, 0.44f, 0.98f);

            GameObject accent = Rect(selectedGo, "Accent", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 6);
            });
            Image accentImage = accent.AddComponent<Image>();
            accentImage.color = tint;
            accentImage.raycastTarget = false;

            toggle.targetGraphic = background;
            toggle.graphic = selectedImage;
            toggle.group = group;
            toggle.isOn = selected;
            UnityEventTools.AddPersistentListener(toggle.onValueChanged, page.SetActive);
            if (priceId > 0) {
                UnityEventTools.AddIntPersistentListener(toggle.onValueChanged, ui.ShowPriceItem, priceId);
            }

            GameObject badge = Rect(tab, "Icon", At(new Vector2(-195, 0), new Vector2(34, 34)));
            Image badgeImage = badge.AddComponent<Image>();
            badgeImage.sprite = hexBadge;
            badgeImage.color = selected ? tint : new Color(tint.r, tint.g, tint.b, 0.5f);
            badgeImage.raycastTarget = false;

            // RowLabel 左对齐是从框的左边缘起排的, 所以要把框推到图标右边
            // RowLabel starts its text at the box's left edge, so the box has to clear the icon.
            RowLabel(tab, "Caption", caption, 25, Color.white, new Vector2(60, 0), 340, TextAnchor.MiddleLeft);
        }

        /// <summary>
        /// 左侧的凭证分区 / one certificate zone in the left sidebar.
        /// 点一下就把货架换成这个凭证能买的东西 / clicking swaps the shelf to that certificate's goods
        /// </summary>
        private static void CertificateZone(GameObject parent, string name, string caption, Color tint,
                                            float y, ShopUI ui, int priceId) {
            GameObject zone = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(340, 140);
                r.anchoredPosition = new Vector2(30, y);
            });
            Image background = zone.AddComponent<Image>();
            background.color = new Color(0.22f, 0.23f, 0.25f, 0.96f);
            Button button = zone.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject stripe = Rect(zone, "Stripe", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(10, 0);
            });
            Image stripeImage = stripe.AddComponent<Image>();
            stripeImage.color = tint;
            stripeImage.raycastTarget = false;

            GameObject badge = Rect(zone, "Icon", At(new Vector2(-110, 0), new Vector2(52, 52)));
            Image badgeImage = badge.AddComponent<Image>();
            badgeImage.sprite = hexBadge;
            badgeImage.color = tint;
            badgeImage.raycastTarget = false;

            RowLabel(zone, "Caption", caption, 21, Color.white, new Vector2(40, 0), 230, TextAnchor.MiddleLeft);

            // 固定参数的Int持久监听, 存得进预制体 / an Int persistent listener, which serialises
            UnityEventTools.AddIntPersistentListener(button.onClick, ui.ShowPriceItem, priceId);
        }

        /// <summary>
        /// 凭证货架上的一张卡 / one card on the certificate shelf.
        /// 名字/价格图标/价格三条路径是 ShopUI.ShopItem 写死的, ItemIconComponent 还要求同物体上有Button
        /// Name_Ground/Text and Price_Ground/Layout/{Price_Icon,Price} are the paths ShopUI.ShopItem
        /// resolves, and ItemIconComponent calls GetComponent&lt;Button&gt;() on its own object.
        /// </summary>
        private static GameObject CatalogueCard(GameObject parent, string name) {
            GameObject card = Rect(parent, name, r => { });
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = new Color(0.96f, 0.96f, 0.97f, 0.98f);
            card.AddComponent<Button>().targetGraphic = cardImage;

            // 顶上的黑色标题条 / the dark title bar across the top
            GameObject nameGround = Rect(card, "Name_Ground", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 56);
            });
            nameGround.AddComponent<Image>().color = new Color(0.13f, 0.14f, 0.16f);
            RowLabel(nameGround, "Text", "Item", 24, Color.white, Vector2.zero, 290, TextAnchor.MiddleCenter);

            // 稀有度底图 + 图标 + 数量, 三个都是 ItemIconComponent 的字段
            // Rarity ground, icon and amount are the three fields ItemIconComponent drives.
            GameObject ground = Rect(card, "Ground", At(new Vector2(0, 14), new Vector2(170, 170)));
            Image groundImage = ground.AddComponent<Image>();
            groundImage.color = Color.white;
            groundImage.raycastTarget = false;

            GameObject icon = Rect(ground, "Icon", At(Vector2.zero, new Vector2(120, 120)));
            Image iconImage = icon.AddComponent<Image>();
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;

            // 数量压在稀有度底图右下角, 底下垫一条暗色: 四位数直接盖在彩色底图上读不出来
            // The count sits bottom-right over the rarity ground with a dark strip behind it - a
            // four-digit amount straight on the coloured ground is unreadable.
            GameObject amountHolder = Rect(card, "AmountGround", At(new Vector2(43, -54), new Vector2(160, 38)));
            amountHolder.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.10f, 0.72f);
            Text amount = RowLabel(amountHolder, "Amount", "1", 24, Color.white,
                new Vector2(-10, 0), 150, TextAnchor.MiddleRight);

            // 底部灰色价格条 / the grey price bar along the bottom
            GameObject priceGround = Rect(card, "Price_Ground", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(0.5f, 0);
                r.sizeDelta = new Vector2(-24, 52);
                r.anchoredPosition = new Vector2(0, 14);
            });
            priceGround.AddComponent<Image>().color = new Color(0.62f, 0.63f, 0.65f, 0.95f);

            GameObject layout = Rect(priceGround, "Layout", Stretch);
            GameObject priceIcon = Rect(layout, "Price_Icon", At(new Vector2(-72, 0), new Vector2(34, 34)));
            Image priceIconImage = priceIcon.AddComponent<Image>();
            priceIconImage.sprite = hexBadge;
            priceIconImage.color = ShopCert;
            priceIconImage.raycastTarget = false;
            RowLabel(layout, "Price", "0", 26, TileInk, new Vector2(20, 0), 160, TextAnchor.MiddleLeft);

            ItemIconComponent iic = card.AddComponent<ItemIconComponent>();
            iic.ground = groundImage;
            iic.icon = iconImage;
            iic.amount = amount;
            return card;
        }

        /// <summary>
        /// 一张商品卡 / one product card.
        /// 里面的 Name_Ground/Text 和 Price_Ground/Layout/{Price_Icon,Price} 是 ShopItem 写死的路径,
        /// ItemIconComponent 还要求同一个物体上有 Button
        /// Name_Ground/Text and Price_Ground/Layout/{Price_Icon,Price} are the paths ShopUI.ShopItem
        /// resolves, and ItemIconComponent calls GetComponent&lt;Button&gt;() on its own object.
        /// </summary>
        private static GameObject ShopCard(GameObject grid, string name, float x, string amount,
                                           string bonus, string title, string price) {
            GameObject card = Rect(grid, name, At(new Vector2(x, 0), new Vector2(300, 727)));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = new Color(0.16f, 0.17f, 0.19f);
            card.AddComponent<Button>().targetGraphic = cardImage;

            // 上半截是美术位 / the upper two thirds is where the product art would sit
            GameObject art = Rect(card, "Ground", r => {
                r.anchorMin = new Vector2(0, 0.38f);
                r.anchorMax = new Vector2(1, 1);
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
            });
            Image artImage = art.AddComponent<Image>();
            artImage.color = new Color(0.30f, 0.31f, 0.34f);
            artImage.raycastTarget = false;

            GameObject icon = Rect(art, "Icon", At(new Vector2(0, 40), new Vector2(150, 150)));
            Image iconImage = icon.AddComponent<Image>();
            iconImage.sprite = hexBadge;
            iconImage.color = ShopGold;
            iconImage.raycastTarget = false;

            // 数量行压在美术区下沿, 和参考图一样 / the quantity sits over the art, as in the reference
            GameObject countBadge = Rect(card, "CountIcon", At(new Vector2(-70, -34), new Vector2(46, 46)));
            Image countImage = countBadge.AddComponent<Image>();
            countImage.sprite = hexBadge;
            countImage.color = ShopGold;
            countImage.raycastTarget = false;

            Text amountText = RowLabel(card, "Amount", amount, 56, Color.white,
                new Vector2(50, -34), 170, TextAnchor.MiddleLeft);

            RowLabel(card, "Plus", "+", 26, new Color(1f, 1f, 1f, 0.8f), new Vector2(0, -108), 60, TextAnchor.MiddleCenter);

            GameObject bonusStrip = Rect(card, "BonusStrip", At(new Vector2(0, -152), new Vector2(252, 44)));
            bonusStrip.AddComponent<Image>().color = ShopGold;
            RowLabel(bonusStrip, "BonusLabel", "BONUS", 18, TileInk, new Vector2(-62, 0), 120, TextAnchor.MiddleLeft);
            RowLabel(bonusStrip, "BonusAmount", "+" + bonus, 22, TileInk, new Vector2(66, 0), 100, TextAnchor.MiddleRight);

            GameObject nameGround = Rect(card, "Name_Ground", At(new Vector2(0, -215), new Vector2(300, 52)));
            RowLabel(nameGround, "Text", title, 22, Color.white, new Vector2(14, 0), 270, TextAnchor.MiddleLeft);

            GameObject priceGround = Rect(card, "Price_Ground", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(0.5f, 0);
                r.sizeDelta = new Vector2(0, 78);
            });
            priceGround.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.07f);

            GameObject layout = Rect(priceGround, "Layout", Stretch);
            GameObject priceIcon = Rect(layout, "Price_Icon", At(new Vector2(-104, 0), new Vector2(36, 36)));
            Image priceIconImage = priceIcon.AddComponent<Image>();
            priceIconImage.sprite = hexBadge;
            priceIconImage.color = ShopGold;
            priceIconImage.raycastTarget = false;
            // 外壳阶段卡片是静态的, 价格用美元直接写死; 真接上数据时 ShopItem 会覆盖这两个
            // Static while the shelf is a shell: ShopItem overwrites this icon and text once real
            // catalogue data drives the cards.
            priceIcon.SetActive(false);
            RowLabel(layout, "Price", price, 34, Color.white, new Vector2(0, 0), 280, TextAnchor.MiddleCenter);

            ItemIconComponent iic = card.AddComponent<ItemIconComponent>();
            iic.ground = artImage;
            iic.icon = iconImage;
            iic.amount = amountText;
            return card;
        }

        /// <summary>
        /// 购买弹窗 / the buy dialog. 参考图里没有, 但 Init 会构造它, 所有子物体都必须在
        /// Not in the reference, but ShopUI.Init constructs it, so every child it resolves must exist.
        /// </summary>
        private static void BuildShopItemPanel(GameObject root) {
            GameObject panel = Rect(root, "ShopItemPanel", Stretch);
            Image blur = panel.AddComponent<Image>();
            blur.color = new Color(0.05f, 0.05f, 0.06f, 0.65f);
            blur.material = PlaceholderMaterial();   // ShopItemPanel 读它的 _Size / its _Size is tweened
            panel.AddComponent<CanvasGroup>();

            // 参考图是左右两栏: 左边白底放图和描述, 右边深色放价格和数量
            // Two columns, as the reference draws it: a pale panel on the left carrying the art and
            // description, a dark one on the right carrying price, quantity and the buy button.
            // 两块底板必须完全不透明: 线性空间里98%还是能透出底下亮色的货架卡
            // Both plates are fully opaque. In Linear colour space even 2% of the bright shelf
            // underneath reads clearly through a dark panel - the same trap the login blackout hit.
            GameObject left = Rect(panel, "Box", At(new Vector2(-280, 0), new Vector2(580, 620)));
            left.AddComponent<Image>().color = new Color(0.95f, 0.95f, 0.96f, 1f);

            GameObject right = Rect(panel, "BuyBox", At(new Vector2(290, 0), new Vector2(560, 620)));
            right.AddComponent<Image>().color = new Color(0.20f, 0.21f, 0.23f, 1f);

            GameObject nameGround = Rect(panel, "ItemNameGround", At(new Vector2(-440, 250), new Vector2(240, 56)));
            nameGround.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.19f);
            RowLabel(nameGround, "Text", "Item", 26, Color.white, Vector2.zero, 230, TextAnchor.MiddleCenter);

            GameObject iconGround = Rect(panel, "ItemIconGround", At(new Vector2(-280, 60), new Vector2(500, 300)));
            iconGround.AddComponent<Image>().color = new Color(0.86f, 0.87f, 0.89f);
            GameObject iconGo = Rect(iconGround, "Image", At(Vector2.zero, new Vector2(200, 200)));
            Image icon = iconGo.AddComponent<Image>();
            icon.sprite = hexBadge;
            icon.color = ShopGold;

            Text useInfo = RowLabel(panel, "ItemUseInfo", "Placeholder item description.", 22,
                new Color(0.30f, 0.31f, 0.33f), new Vector2(-280, -140), 500, TextAnchor.UpperLeft);
            ((RectTransform)useInfo.transform).sizeDelta = new Vector2(500, 90);

            // 售价 / unit price
            // 小标题往右让开两块底板的接缝: 左板到+10为止, 贴着缝排第一个字会被压掉
            // The captions clear the seam between the two plates. The left one ends at +10, and a
            // left-aligned label starting exactly there loses its first glyph against the edge.
            RowLabel(panel, "PriceCaption", "PRICE", 20, new Color(1f, 1f, 1f, 0.5f),
                new Vector2(140, 240), 160, TextAnchor.MiddleLeft);
            Badge(panel, "PriceIcon", new Vector2(300, 240), ShopCert);
            RowLabel(panel, "PriceAmount", "0", 30, Color.white, new Vector2(400, 240), 180, TextAnchor.MiddleLeft);

            // 商品数量 / how much the entry grants
            GameObject amountGround = Rect(panel, "ItemAmountGround", At(new Vector2(140, 140), new Vector2(200, 44)));
            amountGround.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);
            RowLabel(amountGround, "ItemAmount", "x 1", 22, Color.white, Vector2.zero, 200, TextAnchor.MiddleCenter);

            RowLabel(panel, "BuyCaption", "QUANTITY", 20, new Color(1f, 1f, 1f, 0.5f),
                new Vector2(160, 50), 200, TextAnchor.MiddleLeft);

            FlatBtn(panel, "MinButton", "MIN", new Vector2(80, -20), new Vector2(90, 60), 18, false);
            FlatBtn(panel, "TakeButton", "-", new Vector2(178, -20), new Vector2(60, 60), 30, false);
            GameObject buyAmountGround = Rect(panel, "BuyAmountGround", At(new Vector2(290, -20), new Vector2(140, 60)));
            buyAmountGround.AddComponent<Image>().color = new Color(0.88f, 0.89f, 0.91f);
            RowLabel(buyAmountGround, "Text", "0", 30, TileInk, Vector2.zero, 140, TextAnchor.MiddleCenter);
            FlatBtn(panel, "AddButton", "+", new Vector2(402, -20), new Vector2(60, 60), 30, false);
            FlatBtn(panel, "MaxButton", "MAX", new Vector2(500, -20), new Vector2(90, 60), 18, false);

            // 总计支付 / total
            RowLabel(panel, "TotalCaption", "TOTAL", 20, new Color(1f, 1f, 1f, 0.5f),
                new Vector2(140, -110), 160, TextAnchor.MiddleLeft);
            Badge(panel, "AllPriceIcon", new Vector2(300, -110), ShopCert);
            RowLabel(panel, "AllPriceAmount", "0", 30, Color.white, new Vector2(400, -110), 180, TextAnchor.MiddleLeft);

            Button buy = FlatBtn(panel, "BuyButton", "PURCHASE", new Vector2(290, -230), new Vector2(420, 76), 28, false);
            buy.GetComponent<Image>().color = new Color(0.72f, 0.82f, 0.16f);
            buy.transform.Find("Text").GetComponent<Text>().color = TileInk;

            Button close = FlatBtn(panel, "CloseButton", "✕", new Vector2(540, 280), new Vector2(60, 60), 26, false);
            close.GetComponent<Image>().color = new Color(0.30f, 0.31f, 0.33f, 0.95f);

            panel.SetActive(false);
        }

        private static void Badge(GameObject parent, string name, Vector2 pos, Color tint) {
            GameObject go = Rect(parent, name, At(pos, new Vector2(40, 40)));
            Image image = go.AddComponent<Image>();
            image.sprite = hexBadge;
            image.color = tint;
            image.raycastTarget = false;
        }

        // ------------------------------------------------------------------ SettingUI

        /// <summary>
        /// 设置界面(占位) / The settings screen, as a placeholder.
        ///
        /// SettingUI.Init() 会无条件访问它的每一个字段, 所以哪个都不能少 - 少一个就是空引用。
        /// 异形屏那一行按要求藏起来了: slider还在, 只是整行 SetActive(false), Init() 照样读得到它。
        ///
        /// Init() dereferences every one of its fields the first time the screen is shown, so all of
        /// them have to exist or it throws before anything renders. The notch row is hidden rather
        /// than omitted for exactly that reason - the Slider is still there and still wired, its row
        /// is just inactive, which Init() does not mind.
        ///
        /// 声音那一页是真的能用的: Toggle和Slider直接接在 GameSettings 和 SoundManager.UpdateData 上。
        /// The sound page genuinely works - its toggles and sliders are the ones Init() binds to
        /// GameSettings, and each change calls SoundManager.UpdateData(). The numeric readouts beside
        /// the sliders are static text: showing a live value would need a field or a script, and this
        /// screen is meant to be replaced.
        /// </summary>
        private static void BuildSettingUIPrefab() {
            GameObject root = NewUIRoot("SettingUI");
            SettingUI ui = root.AddComponent<SettingUI>();

            GameObject blurGo = Rect(root, "blur", Stretch);
            Image blur = blurGo.AddComponent<Image>();
            blur.color = new Color(0.05f, 0.05f, 0.06f, 0.55f);
            blur.material = PlaceholderMaterial();
            ui.blur = blur;

            // ---- 顶栏 / top bar
            GameObject topBar = Rect(root, "TopBar", r => {
                r.anchorMin = new Vector2(0, 1);
                r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, 110);
            });
            topBar.AddComponent<Image>().color = new Color(0.96f, 0.96f, 0.97f, 0.98f);

            Button back = FlatBtn(topBar, "back", "‹", new Vector2(-810, 0), new Vector2(170, 66), 46, false);
            back.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.95f);
            back.transform.Find("Text").GetComponent<Text>().color = TileInk;
            WireHide(back, ui, "SettingUI");

            // 图标和标题必须错开: RowLabel是左对齐的, 文字从框的左边缘起, 会直接压在图标上
            // The icon and the title have to be spaced apart deliberately - RowLabel is left-aligned,
            // so its text starts at the box's left edge, not at its centre.
            Disc(topBar, "GearRim", new Vector2(-620, 0), 40, TileInk);
            Disc(topBar, "GearBore", new Vector2(-620, 0), 15, new Color(0.96f, 0.96f, 0.97f));
            RowLabel(topBar, "Title", "SETTINGS", 34, TileInk, new Vector2(-400, 0), 320, TextAnchor.MiddleLeft);

            // ---- 主体 / body
            GameObject body = Rect(root, "Body", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 1);
                r.offsetMin = new Vector2(0, 0);
                r.offsetMax = new Vector2(0, -110);
            });
            body.AddComponent<Image>().color = new Color(0.93f, 0.94f, 0.95f, 0.95f);

            GameObject rail = Rect(body, "TabRail", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(330, 0);
            });
            rail.AddComponent<Image>().color = new Color(0.85f, 0.86f, 0.88f, 0.96f);

            GameObject gamePage = Rect(body, "GamePage", Stretch);
            GameObject soundPage = Rect(body, "SoundPage", Stretch);

            ToggleGroup tabs = body.AddComponent<ToggleGroup>();
            TabToggle(rail, "GameTab", "GAME", new Vector2(0, 300), tabs, gamePage, true);
            TabToggle(rail, "SoundTab", "SOUND", new Vector2(0, 190), tabs, soundPage, false);

            // ---- 游戏页 / game page
            ui.game_keep_speed = SettingRow(gamePage, "game_keep_speed", "2x SPEED HOLD",
                "Every action stays at double speed once enabled", 300);
            ui.game_performance = SettingRow(gamePage, "game_performance", "PERFORMANCE",
                "Turning this off raises the frame rate, but costs battery and heat", 190);

            // 异形屏这一行按要求隐藏, 但字段必须存在 / hidden by request; the field must still exist
            GameObject notchRow = Rect(gamePage, "NotchRow", At(new Vector2(165, 80), new Vector2(1420, 90)));
            RowLabel(notchRow, "NotchLabel", "NOTCH INSET", 28, TileInk,
                new Vector2(-560, 0), 420, TextAnchor.MiddleLeft);
            ui.game_profileScreen = MakeSlider(notchRow, "game_profileScreen", new Vector2(120, 0), 480);
            notchRow.SetActive(false);

            GameObject exitRow = Rect(gamePage, "ExitRow", At(new Vector2(165, -30), new Vector2(1420, 90)));
            RowLabel(exitRow, "ExitLabel", "LOG OUT", 28, TileInk,
                new Vector2(-560, 0), 420, TextAnchor.MiddleLeft);
            ui.game_exit = FlatBtn(exitRow, "game_exit", "EXIT THIS ACCOUNT",
                new Vector2(0, 0), new Vector2(300, 62), 22, false);
            ui.game_exit.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.19f, 0.95f);

            // ---- 声音页 / sound page
            ui.sound_soundEffect = VolumeRow(soundPage, "sound_soundEffect", "SOUND EFFECTS", 300,
                out ui.sound_soundEffect_value);
            ui.sound_music = VolumeRow(soundPage, "sound_music", "MUSIC", 190, out ui.sound_music_value);
            ui.sound_voice = VolumeRow(soundPage, "sound_voice", "VOICE", 80, out ui.sound_voice_value);

            soundPage.SetActive(false);

            SavePrefab(root, "SettingUI");
        }

        /// <summary>游戏页的一行: 标题 + 开关 + 说明 / a game-page row: label, switch, footnote.</summary>
        private static Toggle SettingRow(GameObject page, string name, string caption, string note, float y) {
            GameObject row = Rect(page, name + "Row", At(new Vector2(165, y), new Vector2(1420, 90)));
            RowLabel(row, "Label", caption, 28, TileInk, new Vector2(-560, 0), 420, TextAnchor.MiddleLeft);
            Toggle toggle = Pill(row, name, new Vector2(-160, 0));
            RowLabel(row, "Note", "*" + note, 19, new Color(0.42f, 0.43f, 0.45f),
                new Vector2(330, 0), 720, TextAnchor.MiddleLeft);
            return toggle;
        }

        /// <summary>声音页的一行: 标题 + 开关 + 滑杆 + 数值 / label, switch, slider, readout.</summary>
        private static Toggle VolumeRow(GameObject page, string name, string caption, float y, out Slider slider) {
            GameObject row = Rect(page, name + "Row", At(new Vector2(165, y), new Vector2(1420, 90)));
            RowLabel(row, "Label", caption, 28, TileInk, new Vector2(-560, 0), 420, TextAnchor.MiddleLeft);
            Toggle toggle = Pill(row, name, new Vector2(-160, 0));
            slider = MakeSlider(row, name + "_value", new Vector2(280, 0), 480);
            RowLabel(row, "Readout", "100", 26, TileInk, new Vector2(600, 0), 120, TextAnchor.MiddleRight);
            return toggle;
        }

        /// <summary>
        /// 参考图里的滑动开关 / the sliding switch from the reference.
        ///
        /// 这里不给 Toggle 设 graphic, 显隐全交给 PillSwitch:
        /// Toggle 只会对 graphic 本身淡入淡出, 挂在它下面的子物体不受影响, 关掉时还是看得见。
        /// 滑块和两个标签因此都做成平级的兄弟节点。
        ///
        /// toggle.graphic is deliberately left null and PillSwitch owns the visuals. Toggle only
        /// cross-fades that one Graphic, never its children, so a nested colour block and caption
        /// stay visible in the off state. Keeping the knob and both labels as flat siblings avoids
        /// that entirely - and gives the knob something to slide along.
        /// </summary>
        private static Toggle Pill(GameObject parent, string name, Vector2 pos) {
            const float travel = 45f;
            Vector2 size = new Vector2(180, 56);

            GameObject go = Rect(parent, name, At(pos, size));
            Toggle toggle = go.AddComponent<Toggle>();

            GameObject track = Rect(go, "Track", Stretch);
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = new Color(0.86f, 0.87f, 0.89f, 0.98f);

            GameObject knob = Rect(go, "Knob", At(new Vector2(-travel, 0), new Vector2(90, 56)));
            Image knobImage = knob.AddComponent<Image>();
            knobImage.color = TileBlue;
            knobImage.raycastTarget = false;

            // 标签压在滑块上, 谁在下面谁就是亮的 / the captions ride over the knob; the lit one is
            // whichever the knob is currently sitting under
            Text onText = RowLabel(go, "OnText", "ON", 22, Color.white,
                new Vector2(-travel, 0), 90, TextAnchor.MiddleCenter);
            Text offText = RowLabel(go, "OffText", "OFF", 22, Color.white,
                new Vector2(travel, 0), 90, TextAnchor.MiddleCenter);

            toggle.targetGraphic = trackImage;
            toggle.graphic = null;
            toggle.isOn = true;

            PillSwitch pill = go.AddComponent<PillSwitch>();
            pill.knob = (RectTransform)knob.transform;
            pill.knobImage = knobImage;
            pill.onLabel = onText;
            pill.offLabel = offText;
            pill.onColor = TileBlue;
            pill.offColor = new Color(0.44f, 0.45f, 0.47f);
            pill.travel = travel;
            return toggle;
        }

        private static void TabToggle(GameObject rail, string name, string caption, Vector2 pos,
                                      ToggleGroup group, GameObject page, bool on) {
            Vector2 size = new Vector2(250, 96);
            GameObject go = Rect(rail, name, At(pos, size));
            Toggle toggle = go.AddComponent<Toggle>();

            GameObject background = Rect(go, "Background", Stretch);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(0.78f, 0.79f, 0.81f, 0.9f);

            GameObject selected = Rect(go, "Selected", Stretch);
            Image selectedImage = selected.AddComponent<Image>();
            selectedImage.color = new Color(1f, 1f, 1f, 0.98f);

            RowLabel(go, "Label", caption, 28, TileInk, new Vector2(34, 0), 170, TextAnchor.MiddleLeft);
            Disc(go, "Dot", new Vector2(-82, 0), 26, TileInk);

            toggle.targetGraphic = backgroundImage;
            toggle.graphic = selectedImage;
            toggle.group = group;
            toggle.isOn = on;

            // 动态监听: 页面的显隐直接跟着开关的值走, 不需要写脚本
            // A dynamic persistent listener, so the page's active state follows the toggle with no
            // script involved. AddBoolPersistentListener would nail the argument to a constant.
            UnityEventTools.AddPersistentListener(toggle.onValueChanged, page.SetActive);
        }

        /// <summary>
        /// 音量滑杆 / a volume slider. GameSettings 存的是int 0-100, 所以整数步进
        /// GameSettings stores these as an int and divides by 100f, so the slider is whole-numbered
        /// over 0-100 rather than the default normalised range.
        /// </summary>
        private static Slider MakeSlider(GameObject parent, string name, Vector2 pos, float width) {
            GameObject go = Rect(parent, name, At(pos, new Vector2(width, 34)));
            Slider slider = go.AddComponent<Slider>();

            GameObject track = Rect(go, "Background", r => {
                r.anchorMin = new Vector2(0, 0.5f);
                r.anchorMax = new Vector2(1, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(0, 4);
            });
            track.AddComponent<Image>().color = new Color(0.24f, 0.25f, 0.27f, 0.85f);

            GameObject fillArea = Rect(go, "Fill Area", r => {
                r.anchorMin = new Vector2(0, 0.5f);
                r.anchorMax = new Vector2(1, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(-18, 6);
            });
            GameObject fill = Rect(fillArea, "Fill", r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = new Vector2(0, 1);
                r.sizeDelta = new Vector2(18, 0);
            });
            fill.AddComponent<Image>().color = TileBlue;

            GameObject handleArea = Rect(go, "Handle Slide Area", r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.sizeDelta = new Vector2(-18, 0);
            });
            GameObject handle = Rect(handleArea, "Handle", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(0, 1);
                r.sizeDelta = new Vector2(18, 0);
            });
            Image handleImage = handle.AddComponent<Image>();
            handleImage.sprite = Builtin("Knob");
            handleImage.color = new Color(0.16f, 0.17f, 0.19f);

            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 100;
            slider.wholeNumbers = true;
            slider.value = 100;
            return slider;
        }

        private static Text RowLabel(GameObject parent, string name, string text, int size, Color color,
                                     Vector2 pos, float width, TextAnchor align) {
            Text t = Label(parent, name, text, size, color, pos);
            RectTransform r = (RectTransform)t.transform;
            r.sizeDelta = new Vector2(width, 46);
            t.alignment = align;
            return t;
        }

        // ------------------------------------------------------------------ HomeUI parts

        private static System.Action<RectTransform> At(Vector2 centre, Vector2 size) {
            return r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = size;
                r.anchoredPosition = centre;
            };
        }

        private static void Beam(GameObject parent, string name, Vector2 pos, Vector2 size, float alpha) {
            GameObject go = Rect(parent, name, At(pos, size));
            Image img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, alpha);
            img.raycastTarget = false;
        }

        private static void Disc(GameObject parent, string name, Vector2 pos, float diameter, Color color) {
            GameObject go = Rect(parent, name, At(pos, new Vector2(diameter, diameter)));
            Image img = go.AddComponent<Image>();
            img.sprite = Builtin("Knob");
            img.color = color;
            img.raycastTarget = false;
        }

        private static void Tilted(GameObject parent, string name, Vector2 pos, Vector2 size,
                                   float degrees, Color color) {
            GameObject go = Rect(parent, name, At(pos, size));
            go.transform.localRotation = Quaternion.Euler(0, 0, degrees);
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        private enum IconGlyph { Gear, Alert, Mail, Calendar }

        /// <summary>
        /// 左上角那四个图标: LiberationSans里没有齿轮和信封, 所以用方块和圆拼出来
        /// LiberationSans carries no gear or envelope glyph, so each icon is assembled out of a
        /// couple of primitives. Abstract, but it never renders as a missing-glyph box - which is
        /// what putting U+2699 in a Text here would actually do.
        /// </summary>
        private static void IconButton(GameObject parent, string name, IconGlyph glyph, Vector2 pos,
                                       RuaUI ui, string screen) {
            GameObject go = Rect(parent, name, At(pos, new Vector2(58, 58)));
            Image img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.10f);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;

            switch (glyph) {
                case IconGlyph.Gear:
                    Disc(go, "Rim", Vector2.zero, 40, new Color(1, 1, 1, 0.85f));
                    Disc(go, "Bore", Vector2.zero, 15, HomeInk);
                    break;
                case IconGlyph.Alert:
                    Tilted(go, "Diamond", Vector2.zero, new Vector2(34, 34), 45f, new Color(1, 1, 1, 0.85f));
                    Label(go, "Bang", "!", 24, HomeInk, Vector2.zero);
                    break;
                case IconGlyph.Mail:
                    Beam(go, "Body", Vector2.zero, new Vector2(44, 30), 0.85f);
                    Tilted(go, "FlapLeft", new Vector2(-6, 4), new Vector2(26, 3), -34f, HomeInk);
                    Tilted(go, "FlapRight", new Vector2(6, 4), new Vector2(26, 3), 34f, HomeInk);
                    break;
                case IconGlyph.Calendar:
                    // 网格要画得不对称, 居中的十字看着就是个加号
                    // The grid has to be off-centre: a centred cross just reads as a plus sign.
                    Beam(go, "Body", Vector2.zero, new Vector2(42, 36), 0.85f);
                    Tilted(go, "Head", new Vector2(0, 14), new Vector2(42, 8), 0f, new Color(1, 1, 1, 0.45f));
                    Tilted(go, "RowLine", new Vector2(0, -2), new Vector2(32, 2), 0f, HomeInk);
                    Tilted(go, "Col1", new Vector2(-10, -8), new Vector2(2, 14), 0f, HomeInk);
                    Tilted(go, "Col2", new Vector2(10, -8), new Vector2(2, 14), 0f, HomeInk);
                    break;
            }

            if (ui != null && screen != null) Wire(button, ui, screen);
        }

        /// <summary>
        /// 右上角一格货币: 图标 + 数值(注入) + 加号 / one currency slot: icon, injected value, plus.
        /// </summary>
        private static Text Currency(GameObject parent, string name, string caption, Vector2 pos,
                                     Color iconColor, bool plus) {
            GameObject slot = Rect(parent, name + "Slot", At(pos, new Vector2(280, 56)));

            Tilted(slot, "Icon", new Vector2(-118, 0), new Vector2(30, 30), 45f, iconColor);
            Text captionText = Label(slot, name + "Caption", caption, 13,
                new Color(1, 1, 1, 0.45f), new Vector2(-118, -30));
            ((RectTransform)captionText.transform).sizeDelta = new Vector2(140, 20);

            // 数值右对齐: Lua随时会把它从"5"改成"10000", 左对齐的话加号和数字之间的空隙会忽大忽小
            // The value is right-aligned against a fixed edge. Lua rewrites it from "5" to "10000"
            // at will, and left-aligning would leave the plus button stranded a different distance
            // away for every length; this way it always sits tight against the last digit.
            Text value = Label(slot, name, "0", 30, Color.white, new Vector2(-40, 0));
            RectTransform valueRect = (RectTransform)value.transform;
            valueRect.sizeDelta = new Vector2(160, 44);
            value.alignment = TextAnchor.MiddleRight;

            if (plus) {
                Disc(slot, "Plus", new Vector2(68, 0), 32, new Color(1, 1, 1, 0.22f));
                Label(slot, "PlusGlyph", "+", 25, Color.white, new Vector2(68, 0));
            }
            return value;
        }

        /// <summary>
        /// 斜切磁贴 / A leaning tile.
        /// 斜边放在九宫格的左右边框里, 所以磁贴拉宽时斜度不变; 文字是独立的正的子节点
        /// The slant lives in the sprite's left and right 9-slice borders, so widening a tile keeps
        /// the lean at a constant width rather than shearing the whole shape further. The caption is
        /// a separate upright child - sheared text is unreadable, and the reference keeps it upright
        /// too.
        /// </summary>
        private static GameObject MenuTile(GameObject parent, string name, string caption, string subtitle,
                                           Vector2 centre, Vector2 size, int fontSize, Color fill,
                                           Color textColor, RuaUI ui, string screen) {
            GameObject go = Rect(parent, name, At(centre, size));
            Image img = go.AddComponent<Image>();
            img.sprite = skewTile;
            img.type = Image.Type.Sliced;
            img.color = fill;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = img;

            Text label = Label(go, "Caption", caption, fontSize, textColor,
                new Vector2(0, subtitle == null ? 0 : 13));
            ((RectTransform)label.transform).sizeDelta = new Vector2(size.x - TileSkew * 2f, size.y);

            if (subtitle != null) {
                Text sub = Label(go, "Subtitle", subtitle, 19,
                    new Color(textColor.r, textColor.g, textColor.b, 0.62f), new Vector2(0, -26));
                ((RectTransform)sub.transform).sizeDelta = new Vector2(size.x - TileSkew * 2f, 30);
            }

            if (ui != null && screen != null) Wire(button, ui, screen);
            return go;
        }

        /// <summary>
        /// 把按钮接到 UIBase.Show(string) 上 / points a button at UIBase.Show(string).
        ///
        /// onClick.AddListener 加的是运行时监听, 存不进预制体; 要写进序列化数据必须用 UnityEventTools。
        /// AddListener registers a RUNTIME listener, which is not serialised into the prefab and
        /// would silently vanish - persistent listeners are the only kind that survive, and they
        /// need a Component target, which is why this goes through UIBase.Show(string) on the
        /// screen's own RuaUI rather than UIManager (a plain C# singleton, not a Component).
        ///
        /// 目标界面的预制体还不存在, 点了只会打一条缺失资源的错误, 不会崩
        /// The target prefabs do not exist yet, so a click logs one missing-prefab error from
        /// UIManager.Load and returns null. Nothing throws.
        /// </summary>
        private static void Wire(Button button, UIBase ui, string screen) {
            UnityEventTools.AddStringPersistentListener(button.onClick, ui.Show, screen);
        }

        /// <summary>返回键接 UIBase.Hide(string) / a back button points at UIBase.Hide(string).</summary>
        private static void WireHide(Button button, UIBase ui, string screen) {
            UnityEventTools.AddStringPersistentListener(button.onClick, ui.Hide, screen);
        }

        /// <summary>磁贴上的橙色细条 / the orange rule the reference draws on some tiles.</summary>
        private static void AccentBar(GameObject tile, bool top) {
            GameObject go = Rect(tile, top ? "AccentTop" : "AccentBottom", r => {
                r.anchorMin = new Vector2(0, top ? 1 : 0);
                r.anchorMax = new Vector2(1, top ? 1 : 0);
                r.pivot = new Vector2(0.5f, top ? 1 : 0);
                r.sizeDelta = new Vector2(-TileSkew * 2f, 5f);
                r.anchoredPosition = new Vector2(0, top ? -2f : 2f);
            });
            Image img = go.AddComponent<Image>();
            img.color = Accent;
            img.raycastTarget = false;
        }

        /// <summary>左下角的新闻小屏 / the breaking-news screen in the bottom-left corner.</summary>
        private static void NewsPanel(GameObject parent) {
            GameObject panel = Rect(parent, "NewsPanel", At(new Vector2(-832, -335), new Vector2(256, 164)));
            panel.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.11f, 0.92f);

            GameObject strip = Rect(panel, "BreakingLabel", At(new Vector2(-38, 62), new Vector2(180, 26)));
            strip.AddComponent<Image>().color = new Color(0.80f, 0.10f, 0.14f);
            Text breaking = Label(strip, "BreakingText", "BREAKING NEWS", 16, Color.white, Vector2.zero);
            ((RectTransform)breaking.transform).sizeDelta = new Vector2(180, 26);

            // 彩条测试图 / the colour-bar test pattern on the little screen
            Color[] bars = {
                new Color(0.75f, 0.75f, 0.75f), new Color(0.75f, 0.75f, 0.20f),
                new Color(0.20f, 0.75f, 0.75f), new Color(0.20f, 0.70f, 0.25f),
                new Color(0.75f, 0.25f, 0.70f), new Color(0.75f, 0.20f, 0.22f),
                new Color(0.22f, 0.25f, 0.72f)
            };
            for (int i = 0; i < bars.Length; i++) {
                GameObject bar = Rect(panel, "Bar" + i, At(new Vector2(-105 + i * 30, 6), new Vector2(28, 74)));
                Image barImage = bar.AddComponent<Image>();
                barImage.color = bars[i];
                barImage.raycastTarget = false;
            }

            Text stand = Label(panel, "StandBy", "PLEASE STAND BY", 14,
                new Color(1, 1, 1, 0.65f), new Vector2(0, -58));
            ((RectTransform)stand.transform).sizeDelta = new Vector2(256, 24);
        }

        // ------------------------------------------------------------------ PlayerData

        /// <summary>
        /// PlayerData.Initialization 已经把等级/理智/物品(0,1,2)都填好了, 正好是HomeUI.lua需要的
        /// PlayerData.Initialization already seeds level, reason and items 0/1/2 - exactly what
        /// HomeUI.lua reads - so there is no need to poke private fields via SerializedObject.
        /// </summary>
        private static void BuildPlayerData(string userName, string password) {
            string path = UserDataDir + "/" + userName + ".asset";
            PlayerData data = ScriptableObject.CreateInstance<PlayerData>();
            data.Initialization(userName, password);

            // Initialization 只发 0/1/2 三种, 不给凭证, 于是凭证交易所里什么都买不起:
            // HasItem 返回false, 购买数量就一直卡在0
            // Initialization hands out items 0, 1 and 2 only. Without certificates every purchase in
            // the certificate exchange is refused - HasItem fails and the buy amount stays pinned at
            // zero - so the screen looks functional but nothing can actually be bought.
            data.AddItem(6, 400);   // base certificate
            data.AddItem(7, 200);   // senior certificate
            data.AddItem(8, 20);    // purchase certificate

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
        }

        // ------------------------------------------------------------------ UI helpers

        private static GameObject NewUIRoot(string name) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform r = (RectTransform)go.transform;
            Stretch(r);
            go.AddComponent<CanvasGroup>(); // UIBase.Awake 读取 / UIBase.Awake reads this
            return go;
        }

        private static void Stretch(RectTransform r) {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static System.Action<RectTransform> Centered(float w, float h) {
            return r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(w, h);
                r.anchoredPosition = Vector2.zero;
            };
        }

        private static GameObject Rect(GameObject parent, string name, System.Action<RectTransform> layout) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            layout((RectTransform)go.transform);
            return go;
        }

        private static GameObject GroupGo(GameObject parent, string name,
                                          System.Action<RectTransform> layout, Color color, float alpha) {
            GameObject go = Rect(parent, name, layout);
            go.AddComponent<Image>().color = color;
            go.AddComponent<CanvasGroup>().alpha = alpha;
            return go;
        }

        private static CanvasGroup Group(GameObject parent, string name,
                                         System.Action<RectTransform> layout, Color color, float alpha) {
            return GroupGo(parent, name, layout, color, alpha).GetComponent<CanvasGroup>();
        }

        private static System.Action<RectTransform> Inset(float margin) {
            return r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(margin, margin);
                r.offsetMax = new Vector2(-margin, -margin);
            };
        }

        private static GameObject ChamferFill(GameObject parent, string name,
                                              System.Action<RectTransform> layout, Color color,
                                              Sprite sprite = null) {
            GameObject go = Rect(parent, name, layout);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : chamferBig;
            img.type = Image.Type.Sliced;
            img.color = color;
            return go;
        }

        private static CanvasGroup ChamferGroup(GameObject parent, string name,
                                                System.Action<RectTransform> layout, Color color,
                                                float alpha, Sprite sprite = null) {
            CanvasGroup group = ChamferFill(parent, name, layout, color, sprite).AddComponent<CanvasGroup>();
            group.alpha = alpha;
            return group;
        }

        /// <summary>
        /// 控制台面板: 橙色切角外框 + 内缩的切角填充层, 边缘露出一条细橙线, 四角加HUD托架
        /// A console panel: an accent chamfered frame with an inset chamfered fill, so a thin
        /// accent line shows around the edge, plus optional HUD corner brackets. Returns the
        /// fill - children attach there, exactly like the GroupGo callers expect - while the
        /// CanvasGroup comes back on the frame, which is what Show/Hide toggles.
        /// </summary>
        private static GameObject FramePanel(GameObject parent, string name,
                                             System.Action<RectTransform> layout, Color fillColor,
                                             float alpha, bool brackets, out CanvasGroup group) {
            GameObject frame = Rect(parent, name, layout);
            Image frameImg = frame.AddComponent<Image>();
            frameImg.sprite = chamferBig;
            frameImg.type = Image.Type.Sliced;
            frameImg.color = Accent;
            group = frame.AddComponent<CanvasGroup>();
            group.alpha = alpha;

            GameObject fill = Rect(frame, name + "Fill", Inset(FrameGap));
            Image fillImg = fill.AddComponent<Image>();
            fillImg.sprite = chamferBig;
            fillImg.type = Image.Type.Sliced;
            fillImg.color = fillColor;

            // 托架要在填充层之后创建, 否则会被盖住 / after the fill, or the fill covers them
            if (brackets) CornerBrackets(frame, ChamferBigCorner + 10f);
            return fill;
        }

        private static void CornerBrackets(GameObject panel, float inset) {
            Vector2[] corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            foreach (Vector2 anchor in corners) CornerBracket(panel, anchor, inset);
        }

        /// <summary>
        /// 轴心跟着锚点走, 四个角就自动镜像, 不用分别算方向
        /// Pinning the pivot to the same corner as the anchor mirrors the L automatically, so
        /// all four corners fall out of one set of numbers.
        /// </summary>
        private static void CornerBracket(GameObject parent, Vector2 anchor, float inset) {
            const float armLength = 24f;
            const float thickness = 3f;
            Vector2 pivot = new Vector2(anchor.x < 0.5f ? 0f : 1f, anchor.y < 0.5f ? 0f : 1f);
            Vector2 pos = new Vector2(anchor.x < 0.5f ? inset : -inset, anchor.y < 0.5f ? inset : -inset);

            foreach (Vector2 size in new[] { new Vector2(armLength, thickness), new Vector2(thickness, armLength) }) {
                GameObject arm = Rect(parent, "Bracket", r => {
                    r.anchorMin = r.anchorMax = anchor;
                    r.pivot = pivot;
                    r.sizeDelta = size;
                    r.anchoredPosition = pos;
                });
                arm.AddComponent<Image>().color = Accent;
            }
        }

        private static Text TitleLabel(GameObject parent, string name, string text, int size,
                                       Vector2 pos, float ruleWidth) {
            Text t = Label(parent, name, text, size, Accent, pos);
            GameObject rule = Rect(parent, name + "Rule", r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(ruleWidth, 3f);
                r.anchoredPosition = pos + new Vector2(0, -size * 0.85f);
            });
            rule.AddComponent<Image>().color = Accent;
            return t;
        }

        private static Text Label(GameObject parent, string name, string text, int size, Color color, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(700, 60);
                r.anchoredPosition = pos;
            });
            Text t = go.AddComponent<Text>();
            Style(t, size, color);
            t.text = text;
            return t;
        }

        private static void Style(Text t, int size, Color color) {
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
        }

        private static Button Btn(GameObject parent, string name, string caption, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(240, 60);
                r.anchoredPosition = pos;
            });
            Image img = go.AddComponent<Image>();
            img.sprite = chamferSmall;
            img.type = Image.Type.Sliced;
            img.color = Accent;
            Button b = go.AddComponent<Button>();
            b.targetGraphic = img;

            // Selectable 运行时会用 colors.normalColor 覆盖 targetGraphic 的颜色, 只设 img.color
            // 的话按钮一进Play就变白, 所以配色必须同时写进 ColorBlock
            // Selectable overwrites targetGraphic's colour with colors.normalColor at runtime,
            // so setting img.color alone turns the button white the moment Play starts - the
            // tint has to go on the ColorBlock too.
            ColorBlock colors = b.colors;
            colors.normalColor = Accent;
            colors.highlightedColor = Color.Lerp(Accent, Color.white, 0.25f);
            colors.pressedColor = AccentDim;
            colors.selectedColor = Accent;
            colors.disabledColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            colors.fadeDuration = 0.1f;
            b.colors = colors;

            Text t = Label(go, "Text", caption, 24, Ink, Vector2.zero);
            ((RectTransform)t.transform).sizeDelta = new Vector2(240, 60);
            return b;
        }

        /// <summary>
        /// 旧版InputField必须有textComponent, 否则输入时会空引用
        /// A legacy InputField without a textComponent throws as soon as it is used.
        /// </summary>
        private static InputField Input(GameObject parent, string name, string placeholder, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(460, 56);
                r.anchoredPosition = pos;
            });
            Image img = go.AddComponent<Image>();
            img.sprite = chamferSmall;
            img.type = Image.Type.Sliced;
            img.color = Field;

            GameObject underline = Rect(go, "Underline", r => {
                r.anchorMin = new Vector2(0, 0);
                r.anchorMax = new Vector2(1, 0);
                r.pivot = new Vector2(0.5f, 0f);
                r.sizeDelta = new Vector2(0, 2f);
                r.anchoredPosition = Vector2.zero;
            });
            underline.AddComponent<Image>().color = Accent;

            GameObject textGo = Rect(go, "Text", r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(16, 8);
                r.offsetMax = new Vector2(-16, -8);
            });
            Text text = textGo.AddComponent<Text>();
            Style(text, 24, Color.white);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            GameObject phGo = Rect(go, "Placeholder", r => {
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(16, 8);
                r.offsetMax = new Vector2(-16, -8);
            });
            Text ph = phGo.AddComponent<Text>();
            Style(ph, 24, new Color(1, 1, 1, 0.35f));
            ph.alignment = TextAnchor.MiddleLeft;
            ph.text = placeholder;

            InputField field = go.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = ph;

            // 和按钮同理: 不写 ColorBlock 输入框运行时会变白 / same tint trap as the buttons
            ColorBlock colors = field.colors;
            colors.normalColor = Field;
            colors.highlightedColor = Color.Lerp(Field, Color.white, 0.12f);
            colors.pressedColor = Field;
            colors.selectedColor = Color.Lerp(Field, Accent, 0.15f);
            colors.disabledColor = new Color(Field.r, Field.g, Field.b, 0.5f);
            colors.fadeDuration = 0.1f;
            field.colors = colors;
            return field;
        }

        private static Toggle Check(GameObject parent, string name, string caption, Vector2 pos) {
            GameObject go = Rect(parent, name, r => {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(460, 40);
                r.anchoredPosition = pos;
            });
            GameObject boxGo = Rect(go, "Background", r => {
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f);
                r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(30, 30);
                r.anchoredPosition = Vector2.zero;
            });
            // 30像素的小方框不切角 - 13像素的切角会把它削成八边形
            // The 30px box stays square: a 13px chamfer would eat it into an octagon.
            Image box = boxGo.AddComponent<Image>();
            box.color = Field;

            GameObject markGo = Rect(boxGo, "Checkmark", Stretch);
            Image mark = markGo.AddComponent<Image>();
            mark.sprite = Builtin("Checkmark");
            mark.color = Accent;

            Text t = Label(go, "Label", caption, 20, Color.white, new Vector2(30, 0));
            ((RectTransform)t.transform).sizeDelta = new Vector2(400, 40);
            t.alignment = TextAnchor.MiddleLeft;

            Toggle toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = mark;
            toggle.isOn = false;

            ColorBlock colors = toggle.colors;
            colors.normalColor = Field;
            colors.highlightedColor = Color.Lerp(Field, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(Field, Color.black, 0.2f);
            colors.selectedColor = Field;
            colors.disabledColor = new Color(Field.r, Field.g, Field.b, 0.5f);
            colors.fadeDuration = 0.1f;
            toggle.colors = colors;
            return toggle;
        }

        // ------------------------------------------------------------------ asset plumbing

        // ------------------------------------------------------------------ Game prefabs

        /// <summary>
        /// GameManager.Initialization 加载的四个战斗预制体 / The four battle prefabs GameManager loads.
        ///
        /// Entity's constructor does transform.Find("Model"), then Model.Find("Skeleton")
        /// .GetComponent&lt;SkeletonAnimation&gt;() (via the Expand.GetComponent extension), and Char
        /// additionally reads a "Dir" child. The placeholders reproduce that hierarchy exactly, so
        /// real Spine assets drop in without the prefabs being rebuilt.
        ///
        /// 这些只是占位的方块, 骨骼数据来自缺失的 Meta/Char - 真资源到位前跑不了战斗
        /// The quads are stand-ins only: skeletons come from the absent Meta/Char and Meta/Monster
        /// bundles, so battle still cannot run - these exist to stop GameManager erroring on boot
        /// and to give the scene something visible once it can.
        ///
        /// SkeletonAnimation is [ExecuteInEditMode], so it initialises the moment it is added here.
        /// That is silent: SkeletonRenderer.Initialize returns early on a null skeletonDataAsset
        /// (SkeletonRenderer.cs:336) rather than logging, so generation stays quiet.
        /// </summary>
        private static void BuildCharPrefab() {
            GameObject root = new GameObject("CharPrefab");
            EntityBody(root, Accent, 1.6f);

            // Char.dir: 决定朝向和攻击范围的原点 / drives facing and is the attack-range origin
            GameObject dir = new GameObject("Dir");
            dir.transform.SetParent(root.transform, false);

            SavePrefab(root, GamePrefabDir, "CharPrefab");
        }

        private static void BuildMonsterPrefab() {
            GameObject root = new GameObject("MonsterPrefab");
            EntityBody(root, Hostile, 1.4f);
            SavePrefab(root, GamePrefabDir, "MonsterPrefab");
        }

        private static void EntityBody(GameObject root, Color color, float height) {
            // Entity.Update 对 Model 的 z 做缩放来翻转朝向, 占位方块跟着翻正好
            // Entity.Update flips Model's z scale to face the target; the stand-in rides along.
            GameObject model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            GameObject skeleton = new GameObject("Skeleton");
            skeleton.transform.SetParent(model.transform, false);
            skeleton.AddComponent<SkeletonAnimation>();

            GameObject body = Primitive(PrimitiveType.Quad, model.transform, "Placeholder");
            body.transform.localScale = new Vector3(height * 0.5f, height, 1f);
            body.transform.localPosition = new Vector3(0, height * 0.5f, 0);
            Paint(body, "M_Placeholder" + root.name, color, false);
        }

        /// <summary>
        /// GameUI:354 放置干员时生成的落点标记 / the deploy marker GameUI spawns at GameUI.cs:354.
        /// </summary>
        private static void BuildCharPlacePrefab() {
            GameObject root = new GameObject("CharPlacePrefab");
            GroundTile(root, "M_PlaceholderCharPlace", Accent, 0.92f, 0.45f);
            SavePrefab(root, GamePrefabDir, "CharPlacePrefab");
        }

        /// <summary>
        /// GameUI:278 每格攻击范围生成一个 / GameUI spawns one of these per attack-range cell.
        /// </summary>
        private static void BuildAtkRangeDisplayPrefab() {
            GameObject root = new GameObject("AtkRangeDisplay");
            GroundTile(root, "M_PlaceholderAtkRange", Ranged, 0.90f, 0.35f);
            SavePrefab(root, GamePrefabDir, "AtkRangeDisplay");
        }

        /// <summary>
        /// 平铺在地面的半透明格子 / A translucent cell lying flat on the ground.
        /// 调用方会自带 rotation, 所以根节点保持不旋转, 由子物体负责躺平
        /// Both callers pass their own rotation to Instantiate, so the root is left unrotated and
        /// the child does the lying-flat - otherwise the caller's rotation would stand it upright.
        /// </summary>
        private static void GroundTile(GameObject root, string materialName, Color color, float size, float alpha) {
            GameObject tile = Primitive(PrimitiveType.Quad, root.transform, "Tile");
            tile.transform.localRotation = Quaternion.Euler(90, 0, 0);
            tile.transform.localScale = new Vector3(size, size, 1f);
            Paint(tile, materialName, new Color(color.r, color.g, color.b, alpha), true);
        }

        /// <summary>
        /// CreatePrimitive 会附带碰撞体, 必须去掉: GameUI 用射线点地面选格子, 多一个碰撞体就会挡住
        /// CreatePrimitive attaches a collider. It has to go - GameUI raycasts the ground to pick
        /// cells, and a stray collider on a marker would intercept those hits and change gameplay.
        /// </summary>
        private static GameObject Primitive(PrimitiveType type, Transform parent, string name) {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            return go;
        }

        /// <summary>
        /// URP project. Sprites/Default is pipeline-agnostic and stays; the opaque case must use
        /// URP's Unlit, because the built-in Unlit/Color it used to ask for is not a URP shader.
        /// URP/Unlit keys its tint off _BaseColor, which is what Material.color maps to here.
        /// </summary>
        private static void Paint(GameObject go, string materialName, Color color, bool transparent) {
            string path = GamePrefabDir + "/" + materialName + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                Shader shader = Shader.Find(transparent
                    ? "Sprites/Default"
                    : "Universal Render Pipeline/Unlit");
                if (shader == null) {
                    Debug.LogError("Paint: shader not found, is URP installed? " + materialName);
                    return;
                }
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ asset plumbing

        private static Sprite Builtin(string name) {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/" + name + ".psd");
        }

        /// <summary>
        /// 画一张四角45度切角的九宫格贴图 - 明日方舟面板的招牌造型, 没有美术资源就用代码画
        ///
        /// Draws the 45-degree "cut corner" panel shape Arknights UI is built on, as a 9-sliced
        /// sprite. Unity ships no chamfered built-in, and this repo has no art, so it is
        /// rasterised here. PPU must match the canvas referencePixelsPerUnit (100): uGUI scales
        /// the 9-slice border by referencePixelsPerUnit/spritePPU, so a "helpful" PPU of 1 blows
        /// the 28px border up to 2800px, Unity clamps it to the rect, and every panel renders as
        /// a diamond. At 100 one texture pixel is one canvas unit and the bevel stays a constant
        /// on-screen size however far the panel is stretched.
        /// </summary>
        private static Sprite BuildChamferSprite(string name, int size, int corner) {
            const int ss = 2; // 2倍超采样让斜边不锯齿 / 2x supersample to smooth the diagonals
            int hiSize = size * ss;
            int hiCorner = corner * ss;

            bool[] hi = new bool[hiSize * hiSize];
            for (int y = 0; y < hiSize; y++) {
                for (int x = 0; x < hiSize; x++) {
                    hi[y * hiSize + x] = InsideChamfer(x, y, hiSize, hiCorner);
                }
            }

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    int hits = 0;
                    for (int dy = 0; dy < ss; dy++) {
                        for (int dx = 0; dx < ss; dx++) {
                            if (hi[(y * ss + dy) * hiSize + (x * ss + dx)]) hits++;
                        }
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, (float)hits / (ss * ss));
                }
            }

            return ImportSprite(name, pixels, size, corner);
        }

        /// <summary>
        /// 画那个挂在顶上的红色线框球 / Draws the red wireframe ball every reference screen hangs
        /// from the top. 没有美术资源, 所以点用黄金角铺在球面上, 各自连最近的几个邻居, 再正交投影
        /// With no art to load, the vertices are spread over a sphere by the golden angle - an even
        /// scatter without having to subdivide a real icosahedron - then each is linked to its
        /// nearest neighbours and projected straight down the z axis. Edges are drawn as chords
        /// rather than arcs, which is what a faceted polyhedron actually looks like, and depth
        /// fades the far half so the result reads as a ball instead of a flat doily.
        /// </summary>
        private static Sprite BuildWireSphereSprite(string name, int size) {
            const int pointCount = 54;
            const int linksPerPoint = 4;

            Vector3[] points = new Vector3[pointCount];
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < pointCount; i++) {
                float y = 1f - i / (float)(pointCount - 1) * 2f;
                float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float theta = goldenAngle * i;
                points[i] = new Vector3(Mathf.Cos(theta) * ring, y, Mathf.Sin(theta) * ring);
            }

            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            float radius = half * 0.88f;

            int[] order = new int[pointCount];
            for (int i = 0; i < pointCount; i++) {
                int self = i;
                for (int j = 0; j < pointCount; j++) order[j] = j;
                System.Array.Sort(order, (a, b) => (points[self] - points[a]).sqrMagnitude
                    .CompareTo((points[self] - points[b]).sqrMagnitude));
                // order[0] 是自己 / order[0] is the point itself, at distance zero
                for (int k = 1; k <= linksPerPoint && k < pointCount; k++) {
                    WireEdge(pixels, size, points[i], points[order[k]], half, radius);
                }
            }

            for (int i = 0; i < pointCount; i++) WireNodeDot(pixels, size, points[i], half, radius);

            return ImportSprite(name, pixels, size, 0);
        }

        private static void WireEdge(Color[] pixels, int size, Vector3 a, Vector3 b,
                                     float half, float radius) {
            int steps = Mathf.Max(1, Mathf.CeilToInt((a - b).magnitude * radius));
            for (int s = 0; s <= steps; s++) {
                Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                float alpha = Mathf.Lerp(0.16f, 0.95f, Mathf.InverseLerp(-1f, 1f, p.z));
                Plot(pixels, size, half + p.x * radius, half + p.y * radius, WireLine, alpha);
            }
        }

        private static void WireNodeDot(Color[] pixels, int size, Vector3 p, float half, float radius) {
            float depth = Mathf.InverseLerp(-1f, 1f, p.z);
            float peak = Mathf.Lerp(0.25f, 1f, depth);
            float dotRadius = Mathf.Lerp(2.0f, 4.5f, depth);
            int cx = Mathf.RoundToInt(half + p.x * radius);
            int cy = Mathf.RoundToInt(half + p.y * radius);
            int span = Mathf.CeilToInt(dotRadius);

            for (int dy = -span; dy <= span; dy++) {
                for (int dx = -span; dx <= span; dx++) {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > dotRadius) continue;
                    Splat(pixels, size, cx + dx, cy + dy, WireNode, peak * (1f - d / dotRadius));
                }
            }
        }

        /// <summary>双线性铺开一个点, 不然斜线全是锯齿 / spreads a sample over the four pixels it straddles.</summary>
        private static void Plot(Color[] pixels, int size, float x, float y, Color color, float alpha) {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            Splat(pixels, size, x0, y0, color, alpha * (1f - fx) * (1f - fy));
            Splat(pixels, size, x0 + 1, y0, color, alpha * fx * (1f - fy));
            Splat(pixels, size, x0, y0 + 1, color, alpha * (1f - fx) * fy);
            Splat(pixels, size, x0 + 1, y0 + 1, color, alpha * fx * fy);
        }

        /// <summary>
        /// 取最亮的一次写入 / keeps the brightest contributor.
        /// 每条边会被两端各画一次, 直接叠加会让交叉点糊成一团, 取最大值就没这问题
        /// Every edge gets drawn twice, once from each end, and nodes sit on top of edges. Adding
        /// those up blows the crossings out into blobs; taking the max keeps the line weight even.
        /// </summary>
        private static void Splat(Color[] pixels, int size, int x, int y, Color color, float alpha) {
            if (x < 0 || y < 0 || x >= size || y >= size || alpha <= 0f) return;
            int index = y * size + x;
            if (alpha <= pixels[index].a) return;
            pixels[index] = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>
        /// 写PNG进工程再按Sprite导入 / writes the PNG into the project and imports it as a Sprite.
        /// PPU 必须是100, 和画布的 referencePixelsPerUnit 对上, 否则九宫格边框会被放大到整块塌掉
        /// PPU has to stay at the canvas's referencePixelsPerUnit of 100: uGUI scales the 9-slice
        /// border by referencePixelsPerUnit/spritePPU, so any other value distorts the corners.
        /// </summary>
        /// <summary>
        /// 画一个向右倾的平行四边形九宫格 / the leaning parallelogram behind every home-screen tile.
        /// 斜度只放进左右边框, 上下边框留0: 上下要是也切片, 每一行的形状就被固定住了, 平行四边形会塌掉
        /// The slant goes into the left and right borders only, with top and bottom left at zero.
        /// Slicing vertically as well would pin the top and bottom rows to a fixed height, and since
        /// this shape changes with every row, that collapses the parallelogram into a stepped mess.
        /// </summary>
        private static Sprite BuildSkewSprite(string name, int size, int slant) {
            const int ss = 2; // 2倍超采样 / 2x supersample, same as the chamfer
            int hiSize = size * ss;
            int hiSlant = slant * ss;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    int hits = 0;
                    for (int dy = 0; dy < ss; dy++) {
                        for (int dx = 0; dx < ss; dx++) {
                            int hx = x * ss + dx;
                            int hy = y * ss + dy;
                            float offset = hiSlant * (hy / (float)(hiSize - 1));
                            if (hx >= offset && hx < hiSize - hiSlant + offset) hits++;
                        }
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, (float)hits / (ss * ss));
                }
            }
            return ImportSprite(name, pixels, size, new Vector4(slant, 0, slant, 0));
        }

        /// <summary>
        /// 画一个六边形 / a flat-top hexagon - the originite and certificate badge shape.
        /// 用 Image.color 上色, 所以贴图本身是白的 / left white so Image.color tints it per use.
        /// </summary>
        private static Sprite BuildHexSprite(string name, int size) {
            const int ss = 2;
            int hiSize = size * ss;
            float half = hiSize * 0.5f;
            float radius = half * 0.98f;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    int hits = 0;
                    for (int dy = 0; dy < ss; dy++) {
                        for (int dx = 0; dx < ss; dx++) {
                            float px = Mathf.Abs(x * ss + dx + 0.5f - half);
                            float py = Mathf.Abs(y * ss + dy + 0.5f - half);
                            // 正六边形: |y| <= r*sqrt(3)/2 且 斜边在 r 之内
                            // Regular hexagon: capped vertically, with the two slanted edges cutting
                            // the corners at 60 degrees.
                            if (py <= radius * 0.866f && px * 0.5f + py * 0.866f <= radius * 0.866f) hits++;
                        }
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, (float)hits / (ss * ss));
                }
            }
            return ImportSprite(name, pixels, size, Vector4.zero);
        }

        /// <summary>
        /// 画一个圆环 / an annulus - the level ring and the exp arc drawn over it.
        /// </summary>
        private static Sprite BuildRingSprite(string name, int size, float innerRatio) {
            Color[] pixels = new Color[size * size];
            float half = size * 0.5f;
            float outer = half - 1f;
            float inner = outer * innerRatio;

            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 内外两条边各留1像素做抗锯齿 / one pixel of feather on each edge
                    float alpha = Mathf.Clamp01(Mathf.Min(outer - d, d - inner));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            return ImportSprite(name, pixels, size, Vector4.zero);
        }

        private static Sprite ImportSprite(string name, Color[] pixels, int size, int border) {
            return ImportSprite(name, pixels, size, new Vector4(border, border, border, border));
        }

        private static Sprite ImportSprite(string name, Color[] pixels, int size, Vector4 border) {
            return ImportSpriteAt(UIPrefabDir, name, pixels, size, size, border);
        }

        private static Sprite ImportSpriteAt(string dir, string name, Color[] pixels, int width, int height, Vector4 border) {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            EnsureFolder(dir);
            string assetPath = dir + "/" + name + ".png";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath),
                tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>
        /// 四个角对称, 所以贴图正反都一样, 不用管纹理坐标的原点在哪
        /// All four corners are cut identically, so the texture reads the same either way up -
        /// no need to care which end of the pixel array is the bottom row.
        /// </summary>
        private static bool InsideChamfer(int x, int y, int size, int corner) {
            int left = x;
            int right = size - 1 - x;
            int bottom = y;
            int top = size - 1 - y;
            if (left + bottom < corner) return false;
            if (right + bottom < corner) return false;
            if (left + top < corner) return false;
            if (right + top < corner) return false;
            return true;
        }

        private static Material PlaceholderMaterial() {
            const string path = UIPrefabDir + "/PlaceholderBlur.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("UI/Default"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void SavePrefab(GameObject root, string name) {
            SavePrefab(root, UIPrefabDir, name);
        }

        private static void SavePrefab(GameObject root, string dir, string name) {
            string path = dir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string path) {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
