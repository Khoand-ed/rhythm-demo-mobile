using Promuse.Net;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 选服务器 / Chooses the server the game talks to - in Play Mode and in every build made after - by
/// setting Assets/Promuse.Net/Resources/ServerTarget.asset:
///   Arknights/Build/Server: Local         the development API (default)
///   Arknights/Build/Server: Staging       the VM's staging, https://&lt;name&gt;-staging.duckdns.org
///   Arknights/Build/Server: Production    the VM's production, https://&lt;name&gt;.duckdns.org
/// The current one is ticked. The two VM addresses are typed into the asset's Inspector once the
/// DuckDNS names exist (deploy/README.md); a build for a server that has none is refused
/// (ServerTargetBuildCheck).
///
/// 找到就不动 / Find-or-create: the asset is made with the defaults only when missing, and choosing a
/// server changes the choice and nothing else. Reset Server Target To Default puts back Local and its
/// default address, and keeps the two VM addresses - they describe the servers, not a choice.
/// </summary>
public static class ServerTargetMenu
{
    public const string AssetPath = "Assets/Promuse.Net/Resources/" + ServerTarget.ResourceName + ".asset";

    private const string Root = "Arknights/Build/";
    private const string LocalItem = Root + "Server: Local";
    private const string StagingItem = Root + "Server: Staging";
    private const string ProductionItem = Root + "Server: Production";

    // ------------------------------------------------------------------ menu items

    [MenuItem(LocalItem, false, 20)]
    private static void ChooseLocal() => Choose(ServerEnvironment.Local);

    [MenuItem(StagingItem, false, 21)]
    private static void ChooseStaging() => Choose(ServerEnvironment.Staging);

    [MenuItem(ProductionItem, false, 22)]
    private static void ChooseProduction() => Choose(ServerEnvironment.Production);

    // The tick beside the current server. Validation runs every time the menu opens.
    [MenuItem(LocalItem, true)]
    private static bool TickLocal() => Tick(LocalItem, ServerEnvironment.Local);

    [MenuItem(StagingItem, true)]
    private static bool TickStaging() => Tick(StagingItem, ServerEnvironment.Staging);

    [MenuItem(ProductionItem, true)]
    private static bool TickProduction() => Tick(ProductionItem, ServerEnvironment.Production);

    [MenuItem(Root + "Reset Server Target To Default", false, 30)]
    public static void ResetToDefault()
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        ServerTarget target = FindOrCreate(out _);
        Undo.RecordObject(target, "Reset Server Target");
        target.environment = ServerEnvironment.Local;
        target.localUrl = ServerTarget.DefaultLocalUrl;
        Save(target);

        Debug.Log($"[ServerTargetMenu] Reset: Local, {target.localUrl}. Kept staging '{target.stagingUrl}' and " +
                  $"production '{target.productionUrl}'.");
    }

    // ------------------------------------------------------------------ the asset

    /// <summary>The asset, or null when the project has none yet. Never creates one.</summary>
    public static ServerTarget Find() => AssetDatabase.LoadAssetAtPath<ServerTarget>(AssetPath);

    /// <summary>The asset, made with the defaults (Local) when missing.</summary>
    public static ServerTarget FindOrCreate(out bool created)
    {
        ServerTarget target = Find();
        created = target == null;
        if (!created) return target;

        string folder = System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Promuse.Net", "Resources");

        target = ScriptableObject.CreateInstance<ServerTarget>();
        AssetDatabase.CreateAsset(target, AssetPath);
        AssetDatabase.SaveAssets();
        return target;
    }

    private static void Choose(ServerEnvironment environment)
    {
        if (Application.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }

        ServerTarget target = FindOrCreate(out bool created);
        Undo.RecordObject(target, "Choose Server");
        target.environment = environment;
        Save(target);

        string scope = target.StorageScope.Length == 0 ? "the device's existing session and caches" : $"its own '{target.StorageScope}' session and caches";
        Debug.Log($"[ServerTargetMenu] Server: {environment} - {(target.BaseUrl.Length == 0 ? "(no address yet)" : target.BaseUrl)}, " +
                  $"with {scope}. Play Mode and the next build talk to it." + (created ? $" Created {AssetPath}." : ""));

        // Told now rather than at build time; a Development build is the kind made from this menu.
        string problem = target.Problem(developmentBuild: true);
        if (problem != null) Debug.LogWarning("[ServerTargetMenu] " + problem);
    }

    private static bool Tick(string item, ServerEnvironment environment)
    {
        ServerTarget target = Find();
        ServerEnvironment current = target != null ? target.environment : ServerEnvironment.Local;
        Menu.SetChecked(item, current == environment);
        return !Application.isPlaying;
    }

    private static void Save(ServerTarget target)
    {
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
    }
}
