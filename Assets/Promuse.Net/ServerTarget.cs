#nullable enable

using UnityEngine;

namespace Promuse.Net
{
    /// <summary>The servers a build can be made for.</summary>
    public enum ServerEnvironment
    {
        /// <summary>The development API on this machine (or the PC, through adb reverse).</summary>
        Local = 0,

        /// <summary>The VM's staging environment: every merge to main, before production.</summary>
        Staging = 1,

        /// <summary>The VM's production environment: tagged releases.</summary>
        Production = 2,
    }

    /// <summary>
    /// 连哪个服务器 / Which server this build talks to - one asset, Resources/ServerTarget.asset, read by
    /// PlayerManager when the game starts. Chosen in the Editor with Arknights/Build/Server: Local |
    /// Staging | Production; a build is refused when the choice cannot work (see <see cref="Problem"/>).
    ///
    /// 各存各的 / Each environment keeps its own session and caches on the device: a staging account's
    /// refresh token is no use to production, and a run result queued offline belongs to the server
    /// that opened the run. Local keeps the names it always had, so switching to it - or never
    /// switching at all - loses nothing already on a device.
    /// </summary>
    public sealed class ServerTarget : ScriptableObject
    {
        public const string ResourceName = "ServerTarget";
        public const string DefaultLocalUrl = "http://127.0.0.1:5199";

        [Tooltip("Which server this build talks to. Set it with Arknights/Build/Server: ...")]
        public ServerEnvironment environment = ServerEnvironment.Local;

        [Tooltip("The development API. A phone reaches the PC's through `adb reverse tcp:5199 tcp:5199`.")]
        public string localUrl = DefaultLocalUrl;

        [Tooltip("https://<name>-staging.duckdns.org - the VM's staging (deploy/README.md).")]
        public string stagingUrl = "";

        [Tooltip("https://<name>.duckdns.org - the VM's production (deploy/README.md).")]
        public string productionUrl = "";

        /// <summary>The selected server's root, without a trailing slash.</summary>
        public string BaseUrl
        {
            get
            {
                string url = environment switch
                {
                    ServerEnvironment.Staging => stagingUrl,
                    ServerEnvironment.Production => productionUrl,
                    _ => localUrl,
                };

                return (url ?? "").Trim().TrimEnd('/');
            }
        }

        /// <summary>
        /// What sets this environment's saved data apart on a device: empty for Local - the keys and
        /// files it always used - and the environment's name otherwise.
        /// </summary>
        public string StorageScope =>
            environment == ServerEnvironment.Local ? "" : environment.ToString().ToLowerInvariant();

        /// <summary>The transport's configuration for this server.</summary>
        public PromuseConfig CreateConfig() => new PromuseConfig { baseUrl = BaseUrl, storageScope = StorageScope };

        /// <summary>
        /// Why a build for this target would not work, or null when it would.
        /// </summary>
        /// <param name="developmentBuild">
        /// A release build may not use plain HTTP at all (Player Settings: development builds only),
        /// so it can never reach Local.
        /// </param>
        public string? Problem(bool developmentBuild)
        {
            string url = BaseUrl;

            if (url.Length == 0)
            {
                return $"The {environment} URL is empty. Fill it in Assets/Promuse.Net/Resources/ServerTarget.asset " +
                       "(the DuckDNS name from deploy/README.md), or choose another server under Arknights/Build.";
            }

            if (environment == ServerEnvironment.Local)
            {
                if (!developmentBuild)
                {
                    return "A release build cannot reach Local: release builds may not use plain HTTP, and " +
                           $"{url} is this machine. Choose Staging or Production under Arknights/Build, or make a " +
                           "Development build.";
                }

                return null;
            }

            // Android refuses plain HTTP to anything but the phone itself (PromuseNetwork.androidlib).
            if (!url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            {
                return $"{environment} must be reached over HTTPS, and {url} is not. The VM serves every " +
                       "environment over HTTPS (Caddy).";
            }

            return null;
        }

        /// <summary>
        /// The asset in Resources, or - when a project has none - Local with its defaults, the server
        /// the game has always talked to.
        /// </summary>
        public static ServerTarget Load()
        {
            ServerTarget? target = Resources.Load<ServerTarget>(ResourceName);
            return target != null ? target : CreateInstance<ServerTarget>();
        }
    }
}
