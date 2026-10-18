using UnityEngine;

namespace UI {
    /// <summary>
    /// 精英标志 / An elite phase's emblem - a short bar for Elite 0, crossed blades for Elite 1, two
    /// pairs for Elite 2, after the reference - drawn from rectangles by the screen builders, one child
    /// per phase. Showing a phase switches its child on and the others off.
    ///
    /// 不用贴图 / Not the elite sprites: the ones in Resources/Sprite/Char/Elite are placeholder bars,
    /// and a missing glyph or sprite would read as an empty square on the one screen about promotion.
    /// </summary>
    public class EliteEmblem : MonoBehaviour {
        [Tooltip("One drawing per elite phase, Elite 0 first.")]
        public GameObject[] phases = new GameObject[0];

        public void Show(int elite) {
            for (int i = 0; i < phases.Length; i++) {
                if (phases[i] != null) phases[i].SetActive(i == elite);
            }
        }
    }
}
