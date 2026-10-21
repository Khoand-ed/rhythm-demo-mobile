using Promuse.Net;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 打包前看一眼服务器 / Before every player build: says which server it will talk to, and refuses one
/// that cannot work (ServerTarget.Problem) -
///   - a server chosen with no address yet (the VM's are typed in once DuckDNS has them),
///   - Staging or Production over plain HTTP, which Android refuses,
///   - a release build pointed at Local, which release builds cannot reach: they may not use plain
///     HTTP at all, and 127.0.0.1 on a phone is the phone.
/// A refusal names what to change; the build never starts.
/// </summary>
public sealed class ServerTargetBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        // Never created here: a project without the asset builds for Local, as it always did.
        ServerTarget target = ServerTargetMenu.Find();
        if (target == null) target = ScriptableObject.CreateInstance<ServerTarget>();

        bool development = (report.summary.options & BuildOptions.Development) != 0;
        string problem = target.Problem(development);

        if (problem != null) throw new BuildFailedException("[ServerTargetBuildCheck] " + problem);

        Debug.Log($"[ServerTargetBuildCheck] This {(development ? "Development" : "release")} build talks to " +
                  $"{target.environment}: {target.BaseUrl}");
    }
}
