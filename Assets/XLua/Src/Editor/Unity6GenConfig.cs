using System;
using System.Reflection;
using XLua;

/// <summary>
/// Unity 6 代码生成兼容性过滤 / Unity 6 compatibility filter for XLua code generation.
///
/// Unity 6 added Span&lt;T&gt; / ReadOnlySpan&lt;T&gt; overloads to a number of core APIs -
/// Transform.TransformDirections, Object.InstantiateAsync, Resources.EntityIdsToValidArray,
/// TextAsset's byte constructors, AnimationCurve.GetKeys/SetKeys, and others.
///
/// Those are 'ref struct' types. The generator emits calls shaped like
///     translator.Assignable&lt;System.Span&lt;Vector3&gt;&gt;(L, 2)
/// and a ref struct may not be used as a generic type argument, so the generated wrappers
/// fail to compile with CS0306.
///
/// XLua already skips pointer members for exactly this reason (see isMethodInBlackList and
/// isMemberInBlackList in Generator.cs); ref structs simply postdate this version of XLua.
/// This filter extends that existing behaviour. Members skipped here are still reachable
/// from Lua through the reflection path - only the static wrapper is omitted.
///
/// PLACEMENT MATTERS: this file lives in the Xlua.Core.Editor assembly on purpose. A config
/// class under Assets/XLua/Editor/ would land in Assembly-CSharp-Editor, which references
/// Assembly-CSharp - the assembly that holds Assets/XLua/Gen/. Once generation emits code
/// that does not compile, Assembly-CSharp-Editor cannot build either, so the filter would be
/// missing from the loaded assemblies at exactly the moment the generator needs it.
/// Xlua.Core.Editor only references Xlua.Core, so it always builds and is always visible to
/// GetGenConfig(XLua.Utils.GetAllTypes()).
/// </summary>
public static class Unity6GenConfig {

    /// <summary>
    /// 一个静态的 Func&lt;MemberInfo, bool&gt; 字段加上 [BlackList] 会被注册为成员过滤器
    /// A static Func&lt;MemberInfo, bool&gt; field marked [BlackList] is registered as a member
    /// filter (Generator.MergeCfg); returning true excludes that member from generation.
    /// </summary>
    [BlackList]
    public static Func<MemberInfo, bool> SkipRefStructMembers = HasRefStructInSignature;

    private static bool HasRefStructInSignature(MemberInfo member) {
        MethodBase method = member as MethodBase;
        if (method != null) {
            foreach (ParameterInfo parameter in method.GetParameters()) {
                if (IsRefStruct(parameter.ParameterType)) return true;
            }
            MethodInfo methodInfo = member as MethodInfo;
            return methodInfo != null && IsRefStruct(methodInfo.ReturnType);
        }

        PropertyInfo property = member as PropertyInfo;
        if (property != null) return IsRefStruct(property.PropertyType);

        FieldInfo field = member as FieldInfo;
        if (field != null) return IsRefStruct(field.FieldType);

        EventInfo eventInfo = member as EventInfo;
        if (eventInfo != null) return IsRefStruct(eventInfo.EventHandlerType);

        return false;
    }

    /// <summary>
    /// ref struct (Span/ReadOnlySpan等) 不能作为泛型参数
    /// A ref struct (Span/ReadOnlySpan/...) cannot be used as a generic type argument.
    /// </summary>
    private static bool IsRefStruct(Type type) {
        if (type == null) return false;
        if (type.IsByRef || type.IsArray || type.IsPointer) type = type.GetElementType();
        if (type == null) return false;
        return type.IsByRefLike;
    }
}
