using System;
using System.Collections.Generic;
using DG.Tweening;
using XLua;

namespace Manager {
    /// <summary>
    /// 让Lua能调用DOTween的扩展方法 / Lets Lua call DOTween's extension methods.
    ///
    /// Lua calls like `self.canvasGroup:DOFade(1, 0.5)` fail with "attempt to call a nil value"
    /// even though CanvasGroup itself resolves fine - DOFade is a C# EXTENSION method (declared on
    /// DOTweenModuleUI/ShortcutExtensions, not on CanvasGroup/Transform themselves), and extension
    /// methods aren't visible to plain reflection off the instance's own type.
    ///
    /// CURRENTLY INERT - this list is never consulted. Utils.GetExtensionMethodsOf only performs
    /// the [ReflectionUse] scan while InternalGlobals.extensionMethodMap is null (Utils.cs:333).
    /// InternalGlobals.Init() assigns that map from the generated register whenever the generated
    /// types exist, and this project's XLuaGenAutoRegister.cs declares it EMPTY:
    ///     extensionMethodMap = new Dictionary&lt;Type, IEnumerable&lt;MethodInfo&gt;&gt;() { };
    /// Empty is still non-null, so the scan below is permanently skipped and every extension
    /// method resolves to nil in Lua.
    ///
    /// The fades that needed this are served by RuaUI.FadeIn/FadeOut instead - plain C# methods,
    /// which Lua reaches without any of the above. Kept because it documents the trap and becomes
    /// live again if the generated register is ever cleared or regenerated.
    ///
    /// To give Lua real DOTween access, add these types to a [LuaCallCSharp] list and re-run
    /// XLua/Generate Code so they land in the generated map - note that CharUI.lua.txt:64 still
    /// calls transform:DOScaleY, which will stay nil until that happens.
    /// </summary>
    public static class LuaReflectionConfig {
        [ReflectionUse]
        public static List<Type> ReflectionUse = new List<Type>() {
            typeof(ShortcutExtensions), // DOTween.dll core: Transform.DOScale/DOScaleY, Camera/Material/Light.DOColor, ...
            typeof(DOTweenModuleUI),    // DOTween.Modules.dll: CanvasGroup/Image/Text/RectTransform/Slider/ScrollRect tweens
        };
    }
}
