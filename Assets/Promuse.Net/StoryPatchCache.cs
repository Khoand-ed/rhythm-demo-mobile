#nullable enable

using System;
using System.IO;
using Newtonsoft.Json;
using Promuse.Contracts.Stories;
using UnityEngine;

namespace Promuse.Net
{
    /// <summary>
    /// The last story corrections this device saw, kept on disk - the same idea as
    /// <see cref="RemoteConfigCache"/>: a story played offline still reads with the fixes the
    /// device was last told about.
    ///
    /// 读不出来就当没有 / A file that does not parse reads as no corrections, with a warning: the
    /// story then reads as the build has it, which is never worse than a typo.
    /// </summary>
    public sealed class StoryPatchCache
    {
        private readonly string path;
        private StoryPatchList? current;
        private bool loaded;

        public StoryPatchCache(string directory)
        {
            path = Path.Combine(directory, "story-patches.json");
        }

        public StoryPatchList? Current
        {
            get
            {
                if (!loaded) Load();
                return current;
            }
        }

        public void Store(StoryPatchList list)
        {
            current = list;
            loaded = true;

            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(list));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StoryPatchCache] Could not write {path}: {e.Message}");
            }
        }

        private void Load()
        {
            loaded = true;
            if (!File.Exists(path)) return;

            try
            {
                current = JsonConvert.DeserializeObject<StoryPatchList>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StoryPatchCache] Could not read {path}, ignoring it: {e.Message}");
                current = null;
            }
        }
    }
}
