#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Promuse.Contracts.Runs;

namespace Promuse.Net
{
    /// <summary>
    /// Results that could not be sent when their run ended, kept on the device until they can.
    ///
    /// 为什么要存 / A song ends wherever the player happens to be - often on a train, often in a
    /// tunnel. Without this a clear played offline was simply gone: the stamina spent, the run
    /// left open, the mission and the leaderboard never told. Now the result waits here and goes
    /// out on the next sign-in or the next visit to the song list.
    ///
    /// 晚到也安全 / Sending late is safe for the same reason sending twice is: the result goes out
    /// under the run's own id as its idempotency key. If the first attempt did reach the server and
    /// only the answer was lost, the server replays that answer instead of counting the clear again.
    ///
    /// 一个文件 / One small JSON file under persistentDataPath rather than PlayerPrefs: a result
    /// carries its input trace, tens of kilobytes, and PlayerPrefs on Android is an XML file that
    /// is read whole into memory at startup.
    /// </summary>
    public sealed class PendingRunResults
    {
        /// <summary>
        /// Oldest dropped beyond this. Twenty songs played without ever once reaching the server is
        /// not a queue any more, and an unbounded file is the worse failure.
        /// </summary>
        public const int Capacity = 20;

        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.DateTimeOffset,
        };

        private readonly string path;

        public PendingRunResults(string directory)
        {
            path = Path.Combine(directory, "pending-run-results.json");
        }

        /// <summary>One run's result, as it would have been sent.</summary>
        public sealed class Entry
        {
            public Guid RunId { get; set; }
            public bool Won { get; set; }
            public RunResult? Result { get; set; }
            public DateTimeOffset QueuedAt { get; set; }
        }

        public int Count => Load().Count;

        /// <summary>
        /// Everything waiting, oldest first. A file that cannot be read is treated as empty
        /// rather than thrown: nothing here may stop the player reaching the game.
        /// </summary>
        public List<Entry> Load()
        {
            try
            {
                if (!File.Exists(path)) return new List<Entry>();

                return JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(path), Json) ?? new List<Entry>();
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning($"[PendingRunResults] Could not read {path}, starting empty: {e.Message}");
                return new List<Entry>();
            }
        }

        /// <summary>Queues a result. A second result for the same run replaces the first.</summary>
        public void Add(Guid runId, bool won, RunResult? result, DateTimeOffset now)
        {
            List<Entry> entries = Load();
            entries.RemoveAll(e => e.RunId == runId);
            entries.Add(new Entry { RunId = runId, Won = won, Result = result, QueuedAt = now });

            while (entries.Count > Capacity) entries.RemoveAt(0);

            Save(entries);
        }

        public void Remove(Guid runId)
        {
            List<Entry> entries = Load();
            if (entries.RemoveAll(e => e.RunId == runId) > 0) Save(entries);
        }

        private void Save(List<Entry> entries)
        {
            try
            {
                if (entries.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                // 先写临时文件再换名 / Written beside and then moved over, so a crash halfway
                // through leaves the old file rather than half of a new one.
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(entries, Json));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning($"[PendingRunResults] Could not write {path}: {e.Message}");
            }
        }
    }
}
