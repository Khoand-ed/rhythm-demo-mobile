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

        private Color black = new Color(0, 0, 0, 0);
        private Color red = new Color(0.74f, 0.36f, 0.36f, 0);

        private AudioClip clip;
        private AudioClip loop_clip;
        
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
            
            login_back.onClick.AddListener(() => {
                loginPanel.DOFade(0,0.5f).OnComplete(() => {
                    login_password.text = "";
                    loginPanel.gameObject.SetActive(false);
                    homePanel.gameObject.SetActive(true);
                    homePanel.DOFade(1,0.5f);
                    backGround.DOFade(0,0.5f).SetDelay(0.5f);
                    // sphere.DOLocalMove(new Vector3(0,263,-830),1f).SetEase(Ease.Linear);
                    sphere.DOAnchorPosY(263,1f).SetEase(Ease.Linear);
                    sphereCamera.DOColor(red,1f);
                    // sphere.DOScale(1.5f,1f).SetEase(Ease.Linear);
                });
            });
            
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
            
            register_back.onClick.AddListener(() => {
                registerPanel.DOFade(0,0.5f).OnComplete(() => {
                    register_password.text = "";
                    register_rePassword.text = "";
                    registerPanel.gameObject.SetActive(false);
                    homePanel.gameObject.SetActive(true);
                    homePanel.DOFade(1,0.5f);
                    backGround.DOFade(0,0.5f).SetDelay(0.5f);
                    // sphere.DOLocalMove(new Vector3(0,100,-830),1f).SetEase(Ease.Linear);
                    sphere.DOAnchorPosY(263,1f).SetEase(Ease.Linear);
                    sphereCamera.DOColor(red,1f);
                    // sphere.DOScale(1.5f,1f).SetEase(Ease.Linear);
                });
            });
            
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

                EnterGame(loginPanel);
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

                // 注册即登录 / Signing up signs in, so the name screen is next and Home after it. The
                // username only signs in: no one else ever sees it.
                register_password.text = "";
                register_rePassword.text = "";
                CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, "Registration successful.")
                    .AddBackListener(() => ShowNamePanel(registerPanel));
            });

        }

        /// <summary>
        /// 起名界面 / "Do you remember your name?" - the name in the game, chosen once. Comes after
        /// signing up, or after signing in to an account that never got past it; Home opens only
        /// after. The ball sinks to where the loading screen keeps it, so going on from here is
        /// one continuous move.
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
            PlayerData data = result.Value;
            CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE,
                    $"Welcome, Doctor <color=#00B0FF>{data.GetName()}{PlayerTag.Format(data.GetTag())}</color>.")
                .AddBackListener(() => EnterGame(namePanel));
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

        public override void Show() {
            base.Show();

            // 登录前先看一眼 / Before anyone signs in: is the game in maintenance, is this build too
            // old? The server refuses either way; this is what lets the screen say why first.
            RemoteConfigManager.Inst().CheckOnLogin();

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