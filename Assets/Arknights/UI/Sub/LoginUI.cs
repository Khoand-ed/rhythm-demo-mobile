using System;
using Data.Player;
using Promuse.Contracts.Players;
using Promuse.Net;
using DG.Tweening;
using Manager;
using Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    public class LoginUI : UIBase {
        
        public RectTransform sphere;
        public Camera sphereCamera;
        public CanvasGroup backGround;

        public CanvasGroup homePanel;
        public CanvasGroup loginPanel;
        public CanvasGroup registerPanel;
        public CanvasGroup loadingPanel;
        public CanvasGroup lowerRightPanel;

        public Button home_login;
        public Button home_register;

        // 已登录的标题 / What the title shows instead of the two above while the device holds a session:
        // START, which goes in on the tap, and LOG OUT in the corner. Built by LoginStartSetup; an older
        // prefab without them keeps the two sign-in buttons as the only way in.
        public Button home_start;
        public Button home_logout;

        public InputField login_userName;
        public InputField login_password;
        public Button login_enter;
        public Button login_back;
        
        public InputField register_userName;
        public InputField register_password;
        public InputField register_rePassword;
        public Toggle register_toggle;
        public Button register_enter;
        public Button register_back;

        public RectTransform loading_bar1;
        public RectTransform loading_bar2;

        // 起名 / The name screen, between signing up and Home. Built by NameEntrySetup.
        public CanvasGroup namePanel;
        public InputField name_input;
        public Text name_hint;
        public Button name_confirm;

        // 哪个服务器 / Which server a build that is not production talks to. Built by ServerBadgeSetup.
        public Text serverBadge;

        private Color black = new Color(0, 0, 0, 0);
        private Color red = new Color(0.74f, 0.36f, 0.36f, 0);

        private AudioClip clip;
        private AudioClip loop_clip;
        
        // 开始键正在重开会话 / START is reopening the session this device holds (StartGame).
        private bool resuming;

        private bool HasStart => home_start != null && home_logout != null;

        /// <summary>
        /// Back closes the sign-in or sign-up form when one is open. Anywhere else here - the first screen,
        /// the name screen a new account cannot skip - there is nothing further back, so it asks whether to
        /// leave the game.
        /// </summary>
        public override void OnBack() {
            if (registerPanel.gameObject.activeSelf) { Press(register_back); return; }
            if (loginPanel.gameObject.activeSelf) { Press(login_back); return; }
            BackKey.AskToLeave();
        }

        public override void Init() {
            clip = Asset.Load<AudioClip>("Audio/Music/Login", "m_sys_title_intro");
            loop_clip = Asset.Load<AudioClip>("Audio/Music/Login", "m_sys_title_loop");
            
            homePanel.gameObject.SetActive(true);
            loginPanel.gameObject.SetActive(false);
            registerPanel.gameObject.SetActive(false);
            loadingPanel.gameObject.SetActive(false);
            namePanel.gameObject.SetActive(false);

            // 和服务端同一条规则 / The rule the server checks, from the shared contract: the field
            // holds no more than a name may, and nothing a name may not.
            name_input.characterLimit = PlayerNameRules.MaxLength;
            name_input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            name_hint.text = PlayerNameRules.Description;
            name_confirm.onClick.AddListener(ConfirmName);

            // 不是正式服就写出来 / A build for any server but production says which, with its version,
            // so a tester never takes staging for the real game - or a local build for either.
            ServerTarget target = PlayerManager.Inst().Target;
            bool production = target.environment == ServerEnvironment.Production;
            serverBadge.gameObject.SetActive(!production);
            serverBadge.text = $"{target.environment.ToString().ToUpperInvariant()} · v{Application.version}";

            home_login.onClick.AddListener(() => {
                homePanel.DOFade(0,0.5f).OnComplete(() => {
                    homePanel.gameObject.SetActive(false);
                    loginPanel.gameObject.SetActive(true);
                    loginPanel.DOFade(1,0.5f);
                    backGround.DOFade(1,0.5f);
                    // sphere.DOLocalMove(new Vector3(0,65,-800),1f);
                    sphere.DOAnchorPosY(0,1f);
                    sphereCamera.DOColor(black,1f);
                });
            });
            
            login_back.onClick.AddListener(() => BackToTitle(loginPanel, () => login_password.text = ""));

            home_register.onClick.AddListener(() => {
                homePanel.DOFade(0,0.5f).OnComplete(() => {
                    homePanel.gameObject.SetActive(false);
                    registerPanel.gameObject.SetActive(true);
                    registerPanel.DOFade(1,0.5f);
                    backGround.DOFade(1,0.5f);
                    // sphere.DOLocalMove(new Vector3(0,80,-800),1f);
                    sphere.DOAnchorPosY(140,1f);
                    sphereCamera.DOColor(black,1f);
                });
            });
            
            register_back.onClick.AddListener(() => BackToTitle(registerPanel, () => {
                register_password.text = "";
                register_rePassword.text = "";
            }));

            // 开始 / START and LOG OUT, when the prefab has them. Wired here and never in the prefab: a
            // persistent Hide on LOG OUT would make it this screen's exit button, the one Back presses.
            if (HasStart) {
                home_start.onClick.AddListener(StartGame);
                home_logout.onClick.AddListener(AskToSignOut);
            }

            login_enter.onClick.AddListener(async () => {
                string userName = login_userName.text;
                string password = login_password.text;

                // 防重复提交 / Login is not idempotent - a second press while the
                // first is in flight is a second sign-in, and on a slow
                // connection that is easy to do by accident.
                login_enter.interactable = false;
                ApiResult<PlayerData> result = await PlayerManager.Inst().LoginAsync(userName, password);
                login_enter.interactable = true;

                if (!result.IsSuccess) {
                    // 服务端的措辞 / The server's own wording, including for a
                    // failure that never reached it. One dialog, whatever broke.
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                    return;
                }

                // 注册了却没起名就走了 / Signed up and left before choosing a name: that comes first.
                if (!result.Value.HasName()) {
                    login_password.text = "";
                    ShowNamePanel(loginPanel);
                    return;
                }

                // 登录了也先回标题 / Signed in: back to the title, which now shows START. The account is
                // loaded; the tap on START is what goes in, as for a session kept from a previous run.
                // An older prefab without START goes straight in, as it always did.
                if (HasStart) BackToTitle(loginPanel, () => login_password.text = "");
                else EnterGame(loginPanel);
            });
            
            register_enter.onClick.AddListener(async () => {
                string userName = register_userName.text;
                string password = register_password.text;
                string rePassword = register_rePassword.text;

                // 和服务端一致 / These mirror the server's rules exactly. They
                // are a convenience, not the authority: the server validates
                // again and its 422 is what decides. Where the two disagreed the
                // player got a rejection whose reason the client had just told
                // them was fine - the client used to allow a six-character
                // password the server refuses at eight.
                if (userName.Length < 3 || userName.Length > 24
                    || !System.Text.RegularExpressions.Regex.IsMatch(userName, "^[A-Za-z0-9_]+$")) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                        "Username must be <color=#00B0FF>3-24</color> characters - letters, numbers and underscore only.");
                    return;
                }

                if (password != rePassword) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "Passwords do not match.");
                    return;
                }

                if (password.Length < 8 || password.Length > 128) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "Password must be <color=#00B0FF>8-128</color> characters.");
                    return;
                }

                if (!register_toggle.isOn) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "You must accept the <color=#00B0FF>Registration Agreement</color> and <color=#00B0FF>Privacy Agreement</color>.");
                    return;
                }

                register_enter.interactable = false;
                ApiResult<PlayerData> result = await PlayerManager.Inst().RegisterAsync(userName, password);
                register_enter.interactable = true;

                if (!result.IsSuccess) {
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                    return;
                }

                // 注册即登录 / Signing up signs in, so the name screen is next and the title's START after
                // it. The username only signs in: no one else ever sees it.
                register_password.text = "";
                register_rePassword.text = "";
                CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, "Registration successful.")
                    .AddBackListener(() => ShowNamePanel(registerPanel));
            });

        }

        /// <summary>
        /// 起名界面 / "Do you remember your name?" - the name in the game, chosen once. Comes after
        /// signing up, or after signing in to (or START reopening) an account that never got past it;
        /// Home opens only after. The ball sinks to where the loading screen keeps it.
        /// </summary>
        private void ShowNamePanel(CanvasGroup from) {
            lowerRightPanel.DOFade(0, 0.5f);

            from.DOFade(0, 0.5f).OnComplete(() => {
                from.gameObject.SetActive(false);
                lowerRightPanel.gameObject.SetActive(false);

                name_input.text = "";
                name_confirm.interactable = true;
                namePanel.alpha = 0f;
                namePanel.gameObject.SetActive(true);
                namePanel.DOFade(1, 0.5f);

                sphere.DOScale(0.8f, 1f).SetEase(Ease.Linear);
                sphere.DOAnchorPosY(-540, 1f).SetEase(Ease.Linear);
            });
        }

        private void ConfirmName() {
            string chosen = name_input.text;

            // 服务端还会再查 / A convenience: the server checks the same rule, and its answer decides.
            if (!PlayerNameRules.IsValid(chosen)) {
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, PlayerNameRules.Description);
                return;
            }

            // 起了就不能改, 所以再问一次 / Kept for good, so asked once more. Sent only once the dialog
            // has gone: it is one shared screen, and the answer opens it again - a reply quicker
            // than its fade-out would be hidden along with it.
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                $"Your name will be <color=#FFD54F>{chosen}</color>.\nIt cannot be changed later. Continue?",
                () => {
                    name_confirm.interactable = false;
                    Delay.add(() => SubmitName(chosen), 0.3f);
                });
        }

        private async void SubmitName(string chosen) {
            ApiResult<PlayerData> result = await PlayerManager.Inst().SetNameAsync(chosen);

            if (!result.IsSuccess) {
                name_confirm.interactable = true;
                CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                return;
            }

            // 名字加编号才是正式的 / The name and the tag the server drew for it, as everyone will see it.
            // Then back to the title and its START, as after a password sign-in - or straight in on an
            // older prefab without START.
            PlayerData data = result.Value;
            CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE,
                    $"Welcome, Doctor <color=#00B0FF>{data.GetName()}{PlayerTag.Format(data.GetTag())}</color>.")
                .AddBackListener(() => {
                    if (HasStart) BackToTitle(namePanel);
                    else EnterGame(namePanel);
                });
        }

        /// <summary>
        /// 进游戏 / The Arknights-style hand-over to Home: the panel in front fades, the ball sinks
        /// and the two bars close into one line.
        /// </summary>
        private void EnterGame(CanvasGroup from) {
            // 登陆界面消失
            lowerRightPanel.DOFade(0,0.5f);

            from.DOFade(0,0.5f).OnComplete(() => {
                // LoadingPanel界面显示
                from.gameObject.SetActive(false);
                lowerRightPanel.gameObject.SetActive(false);
                loadingPanel.alpha = 0f;
                loadingPanel.gameObject.SetActive(true);
                loadingPanel.DOFade(1,0.5f);
                Text bar1Text = loading_bar1.transform.GetComponentInChildren<Text>();
                Text bar2Text = loading_bar2.transform.GetComponentInChildren<Text>();
                float value = 0.02f;
                bar1Text.text = $"{value * 200:0.##}%";
                bar2Text.text = $"{value * 200:0.##}%";
                // 球移动

                // sphere.DOLocalMove(new Vector3(0,10,-700),1f).SetEase(Ease.Linear).OnComplete(() => {
                sphere.DOScale(0.8f,1f).SetEase(Ease.Linear);
                sphere.DOAnchorPosY(-540, 1f).SetEase(Ease.Linear).OnComplete(() => {
                    // 进度条移动
                    DOTween.To(() => value,v => value = v,0.5f,3f).SetEase(Ease.InQuint)
                        .OnUpdate(() => {
                            Vector2 vector = loading_bar1.anchorMax;
                            vector.x = value;
                            loading_bar1.anchorMax = vector;
                            vector = loading_bar2.anchorMin;
                            vector.x = 1 - value;
                            loading_bar2.anchorMin = vector;

                            bar1Text.text = $"{value * 200:0.##}%";
                            bar2Text.text = $"{value * 200:0.##}%";
                        })
                        .OnComplete(() => {
                            DOTween.To(() => value,z => value = z,0f,0.5f).OnComplete(() => {
                                Delay.add(() => UIManager.Inst().Show("HomeUI"), 2);
                                UIManager.Inst().Hide(Name, true);
                            });
                        });
                });
            });
        }

        /// <summary>
        /// 开始 / START: reopens the session this device holds and goes where a password sign-in goes -
        /// the name screen for an account that never chose one, Home otherwise - without the password.
        /// Nothing happens until the tap, as on any gacha title screen; <see cref="SessionResume"/>
        /// holds the rules.
        ///
        /// 每次都先问配置 / Each tap asks for the config again first, and a maintenance or update warning
        /// is shown on every tap, not once per config version: the tap asks "can I play?" and is owed
        /// its answer. START stays for when the game opens.
        ///
        /// 按下就锁住 / START and LOG OUT are locked while it runs: a second tap would race the first for
        /// the player's save, and a sign-out would pull the session out from under it. They are given
        /// back whenever it does not move on - the game closed, the server out of reach - so START is
        /// the retry.
        /// </summary>
        private async void StartGame() {
            if (resuming) return;
            resuming = true;
            SetTitleBusy(true);
            bool movedOn = false;

            try {
                bool closed = await RemoteConfigManager.Inst().CheckOnLogin(warnAgain: true);
                if (this == null) return;

                if (!SessionResume.ShouldTry(PlayerManager.Inst().HasStoredSession, closed)) {
                    // 关着就等 / Closed: its warning is up, and START waits for the game to open. No
                    // session after all: the two sign-in buttons.
                    if (!closed) BackToSignIn();
                    return;
                }

                ApiResult<PlayerData> result = await PlayerManager.Inst().ResumeAsync();
                if (this == null) return;

                SessionResume.Next next = SessionResume.After(
                    result.IsSuccess, result.IsSuccess && result.Value.HasName(), PlayerManager.Inst().HasStoredSession);

                switch (next) {
                    case SessionResume.Next.EnterGame:
                        movedOn = true;
                        LeaveTitle(() => EnterGame(homePanel));
                        break;

                    case SessionResume.Next.ChooseName:
                        movedOn = true;
                        LeaveTitle(() => ShowNamePanel(homePanel));
                        break;

                    case SessionResume.Next.SignInAgain:
                        BackToSignIn();
                        break;

                    case SessionResume.Next.TryAgain:
                        // 开始键就是重试 / START is the retry: the reason, one tick, and START still there.
                        CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK,
                            $"{result.Message}\nTap START to try again.");
                        break;
                }
            } catch (Exception e) {
                // 出错也不能把人困在这 / Whatever broke, the title is left as it was, START ready again.
                Debug.LogException(e);
            } finally {
                resuming = false;
                if (!movedOn && this != null) SetTitleBusy(false);
            }
        }

        /// <summary>
        /// 会话没了 / The server no longer honours the session and the device has let it go: the title a
        /// device without a session shows, with a word on why.
        /// </summary>
        private void BackToSignIn() {
            ShowTitleButtons();
            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "Your session has ended. Please sign in again.");
        }

        /// <summary>
        /// 登出 / LOG OUT asks first - the tick signs out, the cross stays. Exit lets go of the session at
        /// once, before the server answers the logout, so the title shows the sign-in buttons straight
        /// away and nothing pressed after could find the old session.
        /// </summary>
        private void AskToSignOut() {
            if (resuming) return;

            CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, "Sign out of this account?", () => {
                if (resuming || this == null) return;
                PlayerManager.Inst().Exit();
                ShowTitleButtons();
            });
        }

        /// <summary>
        /// 标题上放哪几个键 / Which buttons the title shows: START and LOG OUT while the device holds a
        /// session - kept from a previous run, or just made by a sign-in or a sign-up - and ACCOUNT
        /// LOGIN and REGISTER otherwise, and always on an older prefab without START.
        /// </summary>
        private void ShowTitleButtons() {
            bool signedIn = HasStart && PlayerManager.Inst().HasStoredSession;
            home_login.gameObject.SetActive(!signedIn);
            home_register.gameObject.SetActive(!signedIn);
            if (!HasStart) return;

            home_start.gameObject.SetActive(signedIn);
            home_logout.gameObject.SetActive(signedIn);
            SetTitleBusy(false);
        }

        private void SetTitleBusy(bool busy) {
            if (!HasStart) return;
            home_start.interactable = !busy;
            home_logout.interactable = !busy;
        }

        /// <summary>
        /// 回到标题 / Back to the title from a form or the name screen: the panel in front fades, the
        /// ball rises back and its light comes on, and the title shows the buttons that now fit -
        /// START after a sign-in, the two sign-in buttons after backing out of a form.
        /// </summary>
        /// <param name="tidy">Run once the panel has faded - the passwords cleared out of sight.</param>
        private void BackToTitle(CanvasGroup from, Action tidy = null) {
            from.DOFade(0,0.5f).OnComplete(() => {
                tidy?.Invoke();
                from.gameObject.SetActive(false);
                ShowTitleButtons();
                homePanel.gameObject.SetActive(true);
                homePanel.DOFade(1,0.5f);
                backGround.DOFade(0,0.5f).SetDelay(0.5f);

                // 起名页收起了右下角, 球也缩了 / The name screen put away the corner panel and shrank the ball.
                if (!lowerRightPanel.gameObject.activeSelf) {
                    lowerRightPanel.alpha = 0f;
                    lowerRightPanel.gameObject.SetActive(true);
                }
                lowerRightPanel.DOFade(1,0.5f);
                sphere.DOScale(1f,1f).SetEase(Ease.Linear);
                sphere.DOAnchorPosY(263,1f).SetEase(Ease.Linear);
                sphereCamera.DOColor(red,1f);
            });
        }

        /// <summary>
        /// 从标题直接走 / Leaves the title the way START does: the backdrop goes dark and the ball's light
        /// goes out as on the way to a form, so the loading bar and the name screen stand on the black
        /// they always stand on.
        /// </summary>
        private void LeaveTitle(Action next) {
            backGround.DOFade(1, 0.5f);
            sphereCamera.DOColor(black, 1f);
            next();
        }

        public override void Show() {
            base.Show();

            // 登录前先看一眼 / Before anyone signs in: is the game in maintenance, is this build too
            // old? The server refuses either way; this is what lets the screen say why first. Once per
            // config version here - START asks again, and says it again, on every tap.
            _ = RemoteConfigManager.Inst().CheckOnLogin();

            // 记得也不自己进 / A session this device still holds is no longer reopened by itself: the
            // title shows START, and the tap goes in. A sign-out has already let go of its own, so the
            // screen it returns to offers the two sign-in buttons as always.
            ShowTitleButtons();

            SoundManager.Inst().PlayMusic(clip, false, () => {
                SoundManager.Inst().PlayMusic(loop_clip, true);
            });
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1,0.5f);
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0,2f).OnComplete(() => {
                base.Hide(destroy);
            });
        }
    }
}