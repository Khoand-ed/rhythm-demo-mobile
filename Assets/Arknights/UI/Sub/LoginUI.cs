using System;
using Data.Player;
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

                // 真等待才盖加载画面 / The loading screen covers the real wait - signing in and
                // fetching the player's state - instead of the old fixed five-second bar, which
                // only started once that wait was already over. loadingPanel and the two
                // loading_bar nodes stay in the prefab, unused, so going back is one revert.
                LoadingScreen.Begin();
                ApiResult<PlayerData> result = await PlayerManager.Inst().LoginAsync(userName, password);
                login_enter.interactable = true;

                if (!result.IsSuccess) {
                    // 失败马上撤 / Lift at once on a failure: the player is about to be told
                    // why, and a minimum display time would only hold that back.
                    LoadingScreen.Abort();

                    // 服务端的措辞 / The server's own wording, including for a
                    // failure that never reached it. One dialog, whatever broke.
                    CommonDialogUI.Message(CommonDialogUI.GroundType.BLACK, result.Message);
                    return;
                }

                // 在画面底下换界面 / Swap screens underneath it. HomeUI opens on top of this
                // one and its backdrop is opaque, so the login screen's own slow fade-out is
                // never seen.
                // finally: HomeUI is Lua-driven and can throw, and a throw must not leave the
                // player under a loading screen that never lifts.
                try {
                    UIManager.Inst().Show("HomeUI");
                    UIManager.Inst().Hide(Name, true);
                } finally {
                    LoadingScreen.End();
                }
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

                
                CommonDialogUI.Message(CommonDialogUI.GroundType.WHITE, "Registration successful.")
                    .AddBackListener(() => {
                    registerPanel.DOFade(0,0.5f).OnComplete(() => {
                        registerPanel.gameObject.SetActive(false);
                        homePanel.gameObject.SetActive(true);
                        homePanel.DOFade(1,0.5f);
                        backGround.DOFade(0,0.5f).SetDelay(0.5f);
                        sphere.DOAnchorPosY(263,1f).SetEase(Ease.Linear);
                    });
                });
            });
            
        }

        public override void Show() {
            base.Show();
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