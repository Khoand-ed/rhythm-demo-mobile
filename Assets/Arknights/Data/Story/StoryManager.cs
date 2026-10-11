using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Stories;
using Promuse.Net;
using Tools;
using UnityEngine;

namespace Data.Story {
    /// <summary>
    /// 剧情从哪来 / Where a story comes from and what the player has already seen.
    ///
    /// The script is in the build (<c>Resources/Story/&lt;id&gt;.story</c>); the server only adds the
    /// corrections made on the admin page since, which this asks for before a story plays - one
    /// empty round trip when nothing changed - and keeps on disk for a story played offline.
    ///
    /// 看过没有 / "Seen" is kept on this device, per account: it only decides whether a story starts
    /// by itself, and every story can be replayed from the stage it belongs to anyway.
    /// </summary>
    public class StoryManager : Single<StoryManager> {
        public const string ScriptFolder = "Story";
        public const string BackgroundFolder = "Story/Backgrounds";

        private const string SeenKeyPrefix = "Promuse.Story.Seen.";

        // 别让剧情等网络 / How long a story waits for the corrections before playing with what is held.
        private const int PatchWaitMs = 1500;

        private HashSet<string> seen;
        private string seenFor;

        // ------------------------------------------------------------------ scripts

        public static bool Exists(string storyId) =>
            !string.IsNullOrEmpty(storyId) && Resources.Load<TextAsset>(ScriptFolder + "/" + storyId) != null;

        /// <summary>
        /// The script with the corrections held on this device applied; null when the build has no
        /// such story, or it does not parse - a broken story is skipped rather than shown half.
        /// </summary>
        public StoryScript Load(string storyId) {
            if (string.IsNullOrEmpty(storyId)) return null;

            TextAsset asset = Resources.Load<TextAsset>(ScriptFolder + "/" + storyId);
            if (asset == null) {
                Debug.LogWarning($"[StoryManager] No story '{storyId}' in this build (Resources/{ScriptFolder}/{storyId}.story) - skipped.");
                return null;
            }

            StoryScript script = StoryScript.Parse(storyId, asset.text);
            if (script.Errors.Count > 0) {
                Debug.LogError($"[StoryManager] '{storyId}' does not parse, skipped:\n  " + string.Join("\n  ", script.Errors));
                return null;
            }

            StoryPatchList held = Api?.StoryPatches.Current;
            if (held != null) script.Apply(held.Patches);
            return script;
        }

        /// <summary>
        /// Asks for the corrections made since the ones held, waiting at most a moment: a story
        /// never waits on a slow network, it plays with what the device already has.
        /// </summary>
        public async Task RefreshPatchesAsync() {
            PromuseApi api = Api;
            if (api == null) return;

            try {
                Task fetch = api.GetStoryPatchesAsync();
                await Task.WhenAny(fetch, Task.Delay(PatchWaitMs));
            } catch (Exception e) {
                Debug.LogWarning($"[StoryManager] Could not fetch story corrections, using the ones held: {e.Message}");
            }
        }

        public static Sprite Background(string name) =>
            string.IsNullOrEmpty(name) ? null : Resources.Load<Sprite>(BackgroundFolder + "/" + name);

        // ------------------------------------------------------------------ seen

        public bool HasSeen(string storyId) => !string.IsNullOrEmpty(storyId) && Seen().Contains(storyId);

        public void MarkSeen(string storyId) {
            if (string.IsNullOrEmpty(storyId) || !Seen().Add(storyId)) return;
            PlayerPrefs.SetString(SeenKeyPrefix + seenFor, string.Join(",", seen));
            PlayerPrefs.Save();
        }

        private HashSet<string> Seen() {
            string account = Api != null ? Api.Tokens.PlayerId.ToString("N") : "local";
            if (seen != null && seenFor == account) return seen;

            seenFor = account;
            seen = new HashSet<string>(
                PlayerPrefs.GetString(SeenKeyPrefix + account, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            return seen;
        }

        private static PromuseApi Api {
            get {
                try {
                    return PlayerManager.Inst().Api;
                } catch (Exception) {
                    return null;
                }
            }
        }
    }
}
