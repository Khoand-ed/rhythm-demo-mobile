using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Notices;
using Promuse.Net;
using Tools;
using UnityEngine;
using UnityEngine.Networking;

namespace Data.HomeContent {
    /// <summary>
    /// 公告 / The notices showing now, which of them this player has read, and their banners.
    ///
    /// 横幅缓存在本机 / A banner's name is its content hash, so a copy on disk can never be stale:
    /// each one is downloaded once per device and read from disk after that.
    /// </summary>
    public class NoticeManager : Single<NoticeManager> {
        private readonly List<Notice> notices = new List<Notice>();
        private readonly Dictionary<string, Sprite> banners = new Dictionary<string, Sprite>();

        public IReadOnlyList<Notice> Notices => notices;

        /// <summary>Unread notices - the dot on Home's alert icon.</summary>
        public int Unread {
            get {
                int count = 0;
                foreach (Notice n in notices) if (!n.IsRead) count++;
                return count;
            }
        }

        public event Action Changed;

        public async Task<ApiResult<NoticeList>> LoadAsync() {
            ApiResult<NoticeList> result = await PlayerManager.Inst().Api.GetNoticesAsync();
            if (!result.IsSuccess) return result;

            notices.Clear();
            notices.AddRange(result.Value.Notices);
            Changed?.Invoke();
            return result;
        }

        /// <summary>
        /// Clears the dot at once and tells the server; a failure only means the dot comes back on the
        /// next load, which is not worth bothering the player about.
        /// </summary>
        public async void MarkRead(Notice notice) {
            if (notice == null || notice.IsRead) return;

            int at = notices.IndexOf(notice);
            if (at >= 0) notices[at] = notice with { IsRead = true };
            Changed?.Invoke();

            await PlayerManager.Inst().Api.ReadNoticeAsync(notice.NoticeId);
        }

        /// <summary>The banner as a sprite: from memory, from disk, or downloaded. Null if it cannot be had.</summary>
        public async Task<Sprite> BannerAsync(string url) {
            if (string.IsNullOrEmpty(url)) return null;
            if (banners.TryGetValue(url, out Sprite cached)) return cached;

            string name = Path.GetFileName(url);
            string folder = Path.Combine(Application.persistentDataPath, "banners");
            string path = Path.Combine(folder, name);

            Texture2D texture = null;
            try {
                if (File.Exists(path)) {
                    texture = new Texture2D(2, 2);
                    if (!texture.LoadImage(File.ReadAllBytes(path))) texture = null;
                }
            } catch (Exception e) {
                Debug.LogWarning($"[NoticeManager] Could not read the cached banner {path}: {e.Message}");
                texture = null;
            }

            if (texture == null) {
                using UnityWebRequest request = UnityWebRequest.Get(PlayerManager.Inst().Api.BaseUrl + url);
                var done = new TaskCompletionSource<bool>();
                request.SendWebRequest().completed += _ => done.TrySetResult(true);
                await done.Task;

                if (request.result != UnityWebRequest.Result.Success) {
                    Debug.LogWarning($"[NoticeManager] Banner {url} could not be downloaded: {request.error}");
                    return null;
                }

                byte[] bytes = request.downloadHandler.data;
                texture = new Texture2D(2, 2);
                if (!texture.LoadImage(bytes)) return null;

                try {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(path, bytes);
                } catch (Exception e) {
                    Debug.LogWarning($"[NoticeManager] Could not cache the banner at {path}: {e.Message}");
                }
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            banners[url] = sprite;
            return sprite;
        }
    }
}
