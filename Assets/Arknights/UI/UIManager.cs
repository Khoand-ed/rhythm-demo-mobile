using System.Collections.Generic;
using Settings;
using Tools;
using UnityEngine;
using UnityEngine.UI;

namespace UI {
    public class UIManager : Single<UIManager> {
        private Dictionary<string, UIBase> uiDictionary = new Dictionary<string, UIBase>();

        private GameObject camera;
        private GameObject canvas;

        private Vector2 canvasSize;

        public GameObject GetCamera() => camera;
        public GameObject GetCanvas() => canvas;
        public Vector2 GetCanvasSize() => canvasSize;

        protected override void Initialization() {
            //克隆画布摄像机 + 画布
            GameObject cameraPrefab = Asset.Load<GameObject>(GameSettings.UI_PREFAB_PATH, "Camera");
            if (cameraPrefab == null) {
                //没有资源就没有UI, 直接中止避免后面一连串的空引用
                Debug.LogError($"[UIManager] 无法初始化 / Cannot initialise: the '{GameSettings.UI_PREFAB_PATH}' bundle " +
                               "did not provide the 'Camera' prefab. The UI cannot be built - see SETUP.md.");
                return;
            }
            camera = Object.Instantiate(cameraPrefab);
            camera.name = "UIManager";
            canvas = camera.transform.Find("Canvas").gameObject;
            // camera.AddComponent<UIComponent>();
            //画布不能随着场景的切换而销毁
            //该项目是以宽进行适配，所以宽不变, 注意：你用的时候，要确定你的项目画布是以什么方式适配的
            canvasSize = canvas.GetComponent<CanvasScaler>().referenceResolution;
            //高根据屏幕的分辨率的宽高比来计算
            // 分辨率.宽 / 分辨率.高 = 画布.宽 / 画布.高
            // 画布.高 = 画布.宽 / (分辨率.宽 / 分辨率.高) = 画布.宽 * 分辨率.高 / 分辨率.宽
            canvasSize.x = canvasSize.y * Screen.width / Screen.height;
            // canvasSize.y = canvasSize.x * Screen.height / Screen.width;
            Object.DontDestroyOnLoad(camera);

            // 返回键跟着界面走 / Android Back for the front-end lives on the UI camera, which is switched
            // off while a song plays - there the pause menu answers it instead.
            camera.AddComponent<BackKey>();
        }

        // 打开
        public UIBase Show(string type) {
            if (!uiDictionary.TryGetValue(type, out UIBase ui)) ui = Load(type);
            if (ui == null) return null;
            ui.transform.SetAsLastSibling();
            ui.Show();
            ui.UpdateView();
            if (ui.state != UIState.Hide) return ui;
            ui.state = UIState.Show;
            return ui;
        }

        /// <summary>
        /// The screens open right now, bottom to top - what a screen opened over others needs to
        /// put back when it is rebuilt, e.g. after a song (see BootIntent.Beneath).
        /// </summary>
        public List<string> OpenScreens() {
            List<UIBase> open = new List<UIBase>();
            foreach (UIBase ui in uiDictionary.Values) {
                if (ui != null && ui.state == UIState.Show) open.Add(ui);
            }
            open.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            List<string> names = new List<string>();
            foreach (UIBase ui in open) names.Add(ui.Name);
            return names;
        }

        /// <summary>The screen open on top of all the others, or null when none is.</summary>
        public UIBase Top() {
            UIBase top = null;
            foreach (UIBase ui in uiDictionary.Values) {
                if (ui == null || ui.state != UIState.Show) continue;
                if (top == null || ui.transform.GetSiblingIndex() > top.transform.GetSiblingIndex()) top = ui;
            }
            return top;
        }

        public void Update(string type) {
            if (uiDictionary.TryGetValue(type, out UIBase ui)) ui.UpdateView();
        }
        
        // 关闭
        public void Hide(string type, bool destroy = false) {
            if (!uiDictionary.ContainsKey(type)) return;
            UIBase ui = uiDictionary[type];
            if (ui.state == UIState.Show) {
                ui.Hide(destroy);
                ui.state = UIState.Hide;
            } else if (destroy) {
                UnLoad(type);
            }
        }

        // 加载
        private UIBase Load(string type) {
            GameObject prefab = Asset.Load<GameObject>(GameSettings.UI_PREFAB_PATH, type);
            if (prefab == null || canvas == null) {
                Debug.LogError($"[UIManager] 无法加载界面 / Cannot load UI '{type}': " +
                               (prefab == null
                                   ? $"prefab '{type}' is missing from the '{GameSettings.UI_PREFAB_PATH}' bundle."
                                   : "the UI canvas was never created.") + " See SETUP.md.");
                return null;
            }
            GameObject obj = Object.Instantiate(prefab, canvas.transform);
            obj.name = type;
            UIBase ui = obj.GetComponent<UIBase>();
            ui.Name = type;
            ui.Init();
            uiDictionary.Add(type, ui);
            return ui;
        }

        // 卸载
        public void UnLoad(string type) {
            if (!uiDictionary.ContainsKey(type)) return;
            UIBase ui = uiDictionary[type];
            uiDictionary.Remove(type);
            Object.Destroy(ui.gameObject);
        }
    }
}