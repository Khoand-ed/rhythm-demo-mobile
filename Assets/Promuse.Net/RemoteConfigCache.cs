#nullable enable

using System;
using System.IO;
using Newtonsoft.Json;
using Promuse.Contracts.Config;
using UnityEngine;

namespace Promuse.Net
{
    /// <summary>
    /// The last remote config this device saw, kept on disk.
    ///
    /// 断网也知道 / A phone that opens the game offline still knows the last maintenance window
    /// and minimum version it was told about, rather than knowing nothing. It is never the
    /// authority - the server enforces every switch whatever the device believes - only what the
    /// screens draw until the next successful read.
    ///
    /// 读不出来就当没有 / A file that does not parse reads as no config, with a warning: nothing
    /// in here may stop the game from starting.
    /// </summary>
    public sealed class RemoteConfigCache
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.DateTimeOffset,
        };

        private readonly string path;
        private RemoteConfig? current;
        private bool loaded;

        public RemoteConfigCache(string directory)
        {
            path = Path.Combine(directory, "remote-config.json");
        }

        public RemoteConfig? Current
        {
            get
            {
                if (!loaded) Load();
                return current;
            }
        }

        public void Store(RemoteConfig config)
        {
            current = config;
            loaded = true;

            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(config, Json));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfigCache] Could not write {path}: {e.Message}");
            }
        }

        private void Load()
        {
            loaded = true;
            if (!File.Exists(path)) return;

            try
            {
                current = JsonConvert.DeserializeObject<RemoteConfig>(File.ReadAllText(path), Json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteConfigCache] Could not read {path}, ignoring it: {e.Message}");
                current = null;
            }
        }
    }
}
