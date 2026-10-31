using System;
using System.Collections.Generic;
using Data.Char;
using Settings;
using UnityEngine;

namespace Promuse.Manufacture {
    /// <summary>
    /// 制造站的房间 / The Manufacture room's floor: the operators chosen for it, in the order chosen from
    /// left to right, in the slots nearest the middle - so two or three stand together in the middle.
    ///
    /// 没有骨骼的就跳过 / An operator with no battle rig - every one, on a clone without the local art -
    /// is left out with a warning, once, and the others close up, so the room never shows a gap or
    /// fails.
    ///
    /// 一次一句 / One line at a time: when an operator speaks, any other line in the room is taken down,
    /// so bubbles never stack over each other.
    ///
    /// Sits on the room's own canvas: the rigs redraw every frame, and only that canvas rebuilds for them.
    /// Built by Arknights/Manufacture/Build Manufacture UI; the field bindings are the contract.
    /// </summary>
    public class ManufactureRoom : MonoBehaviour {
        [Tooltip("Where the operators stand, left to right. Moved by hand in the prefab; the tool keeps them.")]
        public ManufactureChibi[] slots = Array.Empty<ManufactureChibi>();

        [Tooltip("The shared SkeletonGraphic material from the Spine runtime. Nothing local-only.")]
        public Material graphicMaterial;

        [Tooltip("How tall an operator stands, in canvas pixels: feet to the top of the visible figure.")]
        public float chibiHeight = 230f;

        private readonly List<string> requested = new List<string>();
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        private ManufactureLines lines;
        private bool linesLoaded;
        private bool wired;

        /// <summary>The operators standing now, left to right.</summary>
        public List<string> Standing() {
            List<string> ids = new List<string>();
            foreach (ManufactureChibi slot in slots) {
                if (slot != null && slot.IsStanding) ids.Add(slot.OperatorId);
            }
            return ids;
        }

        /// <summary>
        /// Stands <paramref name="ids"/> in the slots, left to right, as many as there are slots. Does nothing
        /// when the same operators were asked for last time, so redrawing the screen does not rebuild them.
        /// </summary>
        public void Show(IReadOnlyList<string> ids) {
            Wire();
            if (Same(ids)) return;

            Clear();
            requested.AddRange(ids);

            if (!linesLoaded) {
                lines = ManufactureLines.Load();
                linesLoaded = true;
            }

            // 先挑能站的 / Those who can stand first: an operator with no battle rig is left out here.
            List<string> standing = new List<string>();
            List<CharMeta> metas = new List<CharMeta>();
            foreach (string id in ids) {
                CharMeta meta = SafeMeta(id);
                if (meta == null || meta.GetBattleRig() == null) {
                    if (warned.Add(id)) Debug.LogWarning($"[ManufactureRoom] {id} has no battle rig on this machine, so it is left out of the room.");
                    continue;
                }

                standing.Add(id);
                metas.Add(meta);
            }

            List<ManufactureChibi> used = Middle(standing.Count);
            for (int i = 0; i < used.Count; i++) {
                ManufactureChibi slot = used[i];

                // 朝向房间中间 / The left half faces right, into the room; the right half faces left.
                bool faceLeft = X(slot) > 0.5f;
                IReadOnlyList<string> said = lines != null ? lines.For(standing[i]) : null;
                slot.Stand(standing[i], metas[i], graphicMaterial, chibiHeight, faceLeft, said);
            }
        }

        /// <summary>
        /// 站在中间 / The <paramref name="count"/> slots nearest the middle of the room, left to right, so a
        /// room of two or three stands together in the middle rather than bunched on the left. The order
        /// chosen in the picker is kept left to right across them.
        /// </summary>
        public List<ManufactureChibi> Middle(int count) {
            List<ManufactureChibi> byMiddle = new List<ManufactureChibi>();
            foreach (ManufactureChibi slot in slots) {
                if (slot != null) byMiddle.Add(slot);
            }

            // 稳定排序 / Nearest the middle first; of two as near, the left one.
            List<ManufactureChibi> sorted = new List<ManufactureChibi>(byMiddle);
            sorted.Sort((a, b) => {
                int near = Mathf.Abs(X(a)).CompareTo(Mathf.Abs(X(b)));
                return near != 0 ? near : X(a).CompareTo(X(b));
            });

            List<ManufactureChibi> used = sorted.GetRange(0, Mathf.Min(count, sorted.Count));
            used.Sort((a, b) => X(a).CompareTo(X(b)));
            return used;
        }

        private static float X(ManufactureChibi slot) => ((RectTransform)slot.transform).anchoredPosition.x;

        /// <summary>Empties every slot.</summary>
        public void Clear() {
            requested.Clear();
            foreach (ManufactureChibi slot in slots) {
                if (slot != null) slot.Leave();
            }
        }

        private bool Same(IReadOnlyList<string> ids) {
            if (requested.Count == 0 || ids.Count != requested.Count) return false;
            for (int i = 0; i < ids.Count; i++) {
                if (ids[i] != requested[i]) return false;
            }
            return true;
        }

        private void Wire() {
            if (wired) return;
            wired = true;
            foreach (ManufactureChibi slot in slots) {
                if (slot != null) slot.Spoke += HushOthers;
            }
        }

        private void HushOthers(ManufactureChibi speaker) {
            foreach (ManufactureChibi slot in slots) {
                if (slot != null && slot != speaker) slot.Hush();
            }
        }

        // 取不到就当没有 / A missing operator costs the room one figure, not the screen. Asked of Resources
        // first, as ItemVisual.Avatar does, so an id that is no operator is not reported as a missing asset.
        private static CharMeta SafeMeta(string id) {
            if (string.IsNullOrEmpty(id) || Resources.Load<CharMeta>(GameSettings.CHAR_META_PATH + "/" + id) == null) return null;

            try {
                return CharManager.Inst().GetMeta(id);
            } catch (Exception) {
                return null;
            }
        }
    }
}
