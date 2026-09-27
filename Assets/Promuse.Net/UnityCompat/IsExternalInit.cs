// C# 9 records use init-only setters, and the compiler looks for this exact type
// to emit them. .NET 5 and later ship it; Unity's .NET Standard 2.1 profile does
// not, so declaring it here is what makes `record` and `init` compile at all.
//
// 只有 Unity 编译这个 / Deliberately OUTSIDE the Contracts folder, which is the
// only folder Promuse.Contracts.csproj links. net10.0 already has this type, and
// linking a second copy would be a duplicate definition rather than a shim.
//
// Nothing references it by name - the compiler finds it. Deleting it produces
// "predefined type is not defined" errors on every record in this assembly.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
