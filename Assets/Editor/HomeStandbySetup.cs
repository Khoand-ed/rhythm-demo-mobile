using UI;
using UnityEditor;
using UnityEngine;

// Stands an operator on the Home screen: one StandbyOperator node inside move1, the far plane, so
// the figure drifts with the backdrop's parallax instead of sitting on the glass in front of it.
// Find-or-create, so re-running never moves a node you have already placed - use Reset Standby
// Operator To Default for that.
//
// The node holds no art - only who to show (HomeStandby.operatorId) and the shared SkeletonGraphic
// material - so the tracked prefab stays valid on a clone that has no local art. The figure appears
// when the operator has a Home rig (Arknights/Art/Install Placeholder Operator Art gives all four one).
//
// Edits HomeUI.prefab in place. Do not run Arknights/Placeholders/Generate Bootstrap Assets to
// "fix" Home: it rebuilds the prefab from nothing and drops this node, the three OpenUIButtons and
// every other hand-made change with it. Run this tool again afterwards if that ever happens.
public static class HomeStandbySetup
{
    private const string PrefabPath = "Assets/Arknights/Resources/Prefab/UI/HomeUI.prefab";
    private const string MaterialPath = "Assets/Plugins/Spine/Runtime/spine-unity/Materials/SkeletonGraphicDefault.mat";
    private const string BackdropName = "move1";
    private const string NodeName = "StandbyOperator";

    // Bottom-middle of the node is the operator's feet, in move1's space (its centre is the screen's).
    // Between the player plate on the left and the tiles on the right, clear of both.
    private static readonly Vector2 DefaultPosition = new Vector2(-250f, -470f);

    // The tap area, around a 1.7x chibi.
    private static readonly Vector2 DefaultSize = new Vector2(380f, 760f);

    private const float DefaultScale = 1.7f;

    [MenuItem("Arknights/Home/Place Standby Operator", false, 40)]
    public static void Place()
    {
        Apply(false);
    }

    [MenuItem("Arknights/Home/Reset Standby Operator To Default", false, 41)]
    public static void ResetToDefault()
    {
        Apply(true);
    }

    private static void Apply(bool reset)
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogError($"[HomeStandbySetup] {MaterialPath} is missing - it comes with the Spine runtime.");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform backdrop = contents.transform.Find(BackdropName);
            if (backdrop == null)
            {
                Debug.LogError($"[HomeStandbySetup] HomeUI has no '{BackdropName}' to stand in front of.");
                return;
            }

            Transform existing = backdrop.Find(NodeName);
            bool created = existing == null;

            GameObject node = created ? new GameObject(NodeName, typeof(RectTransform)) : existing.gameObject;
            if (created)
            {
                node.transform.SetParent(backdrop, false);
                node.transform.SetAsLastSibling();
            }

            bool settle = created || reset;

            RectTransform rect = (RectTransform)node.transform;
            if (settle)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = DefaultPosition;
                rect.sizeDelta = DefaultSize;
                rect.localScale = Vector3.one;
            }

            // 透明但吃点击 / Transparent but it takes the tap: an Image at alpha 0 is still a raycast target.
            // That is the point here, and the reason it is only as big as the operator.
            UnityEngine.UI.Image tapArea = node.GetComponent<UnityEngine.UI.Image>();
            if (tapArea == null) tapArea = node.AddComponent<UnityEngine.UI.Image>();
            if (settle)
            {
                tapArea.color = new Color(1f, 1f, 1f, 0f);
                tapArea.raycastTarget = true;
            }

            HomeStandby standby = node.GetComponent<HomeStandby>();
            if (standby == null) standby = node.AddComponent<HomeStandby>();
            if (settle)
            {
                standby.operatorId = "AMIYA";
                standby.scale = DefaultScale;
                standby.idleNames = new[] { "Relax", "Default" };
                standby.tapNames = new[] { "Interact" };
            }

            standby.graphicMaterial = material;

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);

            Debug.Log($"[HomeStandbySetup] {(created ? "Created" : reset ? "Reset" : "Kept")} {NodeName} in {BackdropName} of HomeUI " +
                      $"at {rect.anchoredPosition}, tap area {rect.sizeDelta}, operator '{standby.operatorId}'. " +
                      "Drag it in the prefab to move it; re-running this tool will not. " +
                      "It shows nobody until the operator has a Home rig.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
