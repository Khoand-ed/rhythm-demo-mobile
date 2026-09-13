using System;
using DG.Tweening;
using Manager;
using Tools;
using XLua;

namespace UI {

    [CSharpCallLua]
    public class RuaUI : UIBase { // LUA脚本启动器
        public Injection[] injections;

        private static LuaEnv luaEnv;
        private static LuaEnv LuaEnv => luaEnv ?? (luaEnv = LuaEnvManager.Inst().LuaEnv());
        
        private LuaTable scriptEnv;
        private Action luaUpdate;
        private Action luaUpdateView;
        private Action luaShow;
        private Action<bool> luaHide;


        public override void Init() { // 初始化
            scriptEnv = LuaEnv.NewTable();
            LuaTable meta = LuaEnv.NewTable();
            meta.Set("__index", LuaEnv.Global); //设置元表
            scriptEnv.SetMetaTable(meta);
            meta.Dispose();
            
            scriptEnv.Set("self", this);
            foreach (Injection injection in injections) {//带入脚本变量
                scriptEnv.Set(injection.name, injection.value);
            }
            LuaEnv.DoString(LuaEnvManager.LoadLuaText("UI." + Name), "chunk", scriptEnv);
            
            scriptEnv.Get("update", out luaUpdate);
            scriptEnv.Get("updateView", out luaUpdateView);
            scriptEnv.Get("show", out luaShow);
            scriptEnv.Get("hide", out luaHide);
            scriptEnv.Get<Action>("init")?.Invoke();
        }


        private void Update() { //更新
            luaUpdate?.Invoke();
        }


        public override void UpdateView() { //更新
            luaUpdateView?.Invoke();
        }

        public override void Show() {
            luaShow?.Invoke();
        }

        public override void Hide(bool destroy = false) {
            if (luaHide != null)
                luaHide(destroy);
            else {
                base.Hide(destroy);
            }
        }


        public LuaTable GetScriptEnv() => scriptEnv;
        public void BaseShow() => base.Show(); // lua层无法调用被覆盖的父类方法
        public void BaseHide(bool destroy = false) => base.Hide(destroy); // lua层无法调用被覆盖的父类方法

        /// <summary>
        /// DOFade/OnComplete 是扩展方法, Lua取不到, 所以在C#这边包一层
        /// DOFade and OnComplete are extension methods, so Lua cannot reach them - it resolves
        /// members off the instance's own type. xLua *can* serve extension methods through
        /// Utils.GetExtensionMethodsOf, but only while InternalGlobals.extensionMethodMap is null;
        /// the generated register (XLuaGenAutoRegister.cs) assigns an empty map, which is non-null
        /// and so permanently suppresses that lookup. See LuaReflectionConfig for the full write-up.
        /// </summary>
        public void FadeIn(float duration) => canvasGroup.DOFade(1, duration);

        public void FadeOut(float duration, bool destroy = false) =>
            canvasGroup.DOFade(0, duration).OnComplete(() => BaseHide(destroy));

        /// <summary>
        /// DOScaleY 也是扩展方法, 同样要包一层 / DOScaleY is an extension method too, so it needs the
        /// same wrapper. 目标由Lua传进来, 因为它缩放的是卡片而不是界面本身
        /// The target is passed in because the caller scales a spawned card, not the screen itself.
        /// </summary>
        // 全限定名: 这个文件没有 using UnityEngine, 而加上去会让 Object 在 System 和 UnityEngine 之间歧义
        // Fully qualified on purpose - this file has no using UnityEngine, and adding one would make
        // Object ambiguous against System.
        public void ScaleY(UnityEngine.Transform target, float value, float duration) =>
            target.DOScaleY(value, duration);
    }
}