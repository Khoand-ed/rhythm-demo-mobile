using System;
using System.Collections.Generic;

namespace UI {
    /// <summary>
    /// 谁盖住了谁 / Which screens each cover screen has switched off, kept apart from UIManager so it
    /// can be tested without a scene. A cover is a screen opaque from edge to edge: while it is up,
    /// nothing below it can be seen, so the screens below need not be drawn or run.
    ///
    /// 只记账 / It only keeps the books and says what to switch: UIManager does the switching. It
    /// returns from <see cref="Cover"/> the screens to switch off, and from <see cref="Uncover"/> the
    /// ones to switch back on - those still open and held by no other cover.
    /// </summary>
    public class ScreenCovers<T> where T : class {
        private static readonly EqualityComparer<T> Same = EqualityComparer<T>.Default;

        private readonly Dictionary<T, List<T>> held = new Dictionary<T, List<T>>();

        /// <summary>
        /// Records <paramref name="cover"/> as hiding <paramref name="below"/>, and returns those to switch
        /// off. Covering again while already covering does nothing and returns nothing.
        /// </summary>
        public List<T> Cover(T cover, IEnumerable<T> below) {
            List<T> off = new List<T>();
            if (cover == null || held.ContainsKey(cover)) return off;

            List<T> record = new List<T>();
            foreach (T screen in below) {
                if (screen == null || Same.Equals(screen, cover) || record.Contains(screen)) continue;
                record.Add(screen);
                if (!IsCovered(screen)) off.Add(screen);
            }
            held[cover] = record;
            return off;
        }

        /// <summary>
        /// Drops <paramref name="cover"/>'s record, and returns the screens it held that are still open by
        /// <paramref name="stillOpen"/> and held by no other cover - the ones to switch back on.
        /// </summary>
        public List<T> Uncover(T cover, Func<T, bool> stillOpen) {
            List<T> on = new List<T>();
            if (cover == null || !held.TryGetValue(cover, out List<T> record)) return on;

            held.Remove(cover);
            foreach (T screen in record) {
                if (IsCovered(screen)) continue;
                if (stillOpen != null && !stillOpen(screen)) continue;
                on.Add(screen);
            }
            return on;
        }

        /// <summary>
        /// 不再点亮 / Drops <paramref name="cover"/>'s record without returning anything to switch on - for a
        /// cover going away where switching a screen back on is no longer wanted, e.g. while it is destroyed.
        /// </summary>
        public void Forget(T cover) {
            if (cover != null) held.Remove(cover);
        }

        /// <summary>
        /// 拿出来 / Takes <paramref name="screen"/> out of every record: it is being shown on top, and a later
        /// uncover must not switch it on, or think it still hidden.
        /// </summary>
        public void Release(T screen) {
            if (screen == null) return;
            foreach (List<T> record in held.Values) record.Remove(screen);
        }

        /// <summary>Whether any cover holds <paramref name="screen"/> switched off.</summary>
        public bool IsCovered(T screen) {
            if (screen == null) return false;
            foreach (List<T> record in held.Values) {
                if (record.Contains(screen)) return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="cover"/> is covering now.</summary>
        public bool IsCovering(T cover) => cover != null && held.ContainsKey(cover);
    }
}
