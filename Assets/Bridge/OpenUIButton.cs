using UI;
using UnityEngine;
using UnityEngine.UI;

// Turns any node in a prefab into an entry point for a UIManager screen, so a
// tile on a Lua-driven screen (HomeUI) can open a C# one without editing its
// Lua script or adding an injection to it.
[RequireComponent(typeof(Button))]
public class OpenUIButton : MonoBehaviour
{
    [Tooltip("The UIManager key to show, i.e. the prefab name under Resources/Prefab/UI.")]
    public string uiName;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(Open);
    }

    private void Open()
    {
        if (string.IsNullOrEmpty(uiName))
        {
            Debug.LogWarning($"{name} has an OpenUIButton with no uiName set.", this);
            return;
        }

        UIManager.Inst().Show(uiName);
    }
}
