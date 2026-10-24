using Manager;
using UnityEngine;
using UnityEngine.UI;
using XLua;

namespace UI {
    [CSharpCallLua]
    public abstract class UIBase : MonoBehaviour {
        public new GameObject gameObject { get; private set; }
        public new RectTransform transform { get; private set; }
        public CanvasGroup canvasGroup { get; private set; }
        internal string Name;

        internal UIState state = UIState.Hide;

        public bool debug;
        
        private void Awake() {
            gameObject = base.gameObject;
            transform = base.transform as RectTransform;
            canvasGroup = GetComponent<CanvasGroup>();
            if (debug) {
                gameObject.SetActive(true);
                Init();
                Show();
            }
        }

        public virtual void Init() { }
        
        public virtual void UpdateView() { }

        public virtual void Show() {
            gameObject.SetActive(true);
        }

        public virtual void Hide(bool destroy = false) {
            gameObject.SetActive(false);
            if (destroy) UIManager.Inst().UnLoad(Name);
        }

        public void Show(string value) => UIManager.Inst().Show(value);

        public void Hide(string value) => UIManager.Inst().Hide(value);

        public void HideAndDestroy(string value) => UIManager.Inst().Hide(value,true);

        public void PlayEffect(AudioClip clip) => SoundManager.Inst().PlayEffect(clip);

        // ------------------------------------------------------------------ Android Back

        private Button exitButton;
        private bool exitSearched;

        /// <summary>
        /// 安卓返回键 / What Android's Back does while this is the top screen (<see cref="BackKey"/>):
        /// presses this screen's own way out, so Back does exactly what that button does - including
        /// where it returns to and whether the screen is destroyed. A screen with something open over
        /// its own content (a detail panel, a log) overrides this to close that first.
        /// </summary>
        public virtual void OnBack() {
            Button exit = ExitButton();
            if (exit == null) {
                UIManager.Inst().Hide(Name);
                return;
            }

            // 按钮被藏起或禁用时什么都不做 / A way out that is hidden or switched off - mid-request,
            // mid-transition - is not taken behind the player's back either.
            if (exit.isActiveAndEnabled && exit.IsInteractable()) exit.onClick.Invoke();
        }

        /// <summary>
        /// The button that leaves this screen. A screen that holds it in a field returns that field;
        /// otherwise it is found once in the prefab (<see cref="FindExitButton"/>).
        /// </summary>
        public virtual Button ExitButton() {
            if (!exitSearched) {
                exitButton = FindExitButton(this);
                exitSearched = true;
            }
            return exitButton;
        }

        /// <summary>
        /// 在预制体里找出口 / A screen's way out when no field names it, in the two ways the prefabs
        /// wire one:
        ///   1. a button whose click, set in the prefab, calls Hide or HideAndDestroy on this screen
        ///      (Depot, Mission, Shop, Settings, the Lua CharUI...);
        ///   2. a button at TopBar/BackButton, which the screen hooks up in code (the song list).
        /// Null when neither exists - then Back simply hides the screen.
        /// </summary>
        public static Button FindExitButton(UIBase screen) {
            Component root = screen;
            foreach (Button button in root.GetComponentsInChildren<Button>(true)) {
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++) {
                    if (button.onClick.GetPersistentTarget(i) != screen) continue;
                    string method = button.onClick.GetPersistentMethodName(i);
                    if (method == nameof(Hide) || method == nameof(HideAndDestroy)) return button;
                }
            }

            Transform topBarBack = root.transform.Find("TopBar/BackButton");
            return topBarBack != null ? topBarBack.GetComponent<Button>() : null;
        }

        /// <summary>For an override: presses <paramref name="button"/> as a tap would, when a tap could.</summary>
        protected static bool Press(Button button) {
            if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return false;
            button.onClick.Invoke();
            return true;
        }
    }

    // [Serializable]
    // public class Injection {
    //     public string name;
    //     public Object value;
    // }

    public enum UIState {
        Show,
        Hide
    }
}