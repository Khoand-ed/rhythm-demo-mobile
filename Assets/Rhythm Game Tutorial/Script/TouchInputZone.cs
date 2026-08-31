using UnityEngine;

// Lets the player tap anywhere on the left/right half of the screen instead
// of tapping the on-screen button directly. Attach to any GameObject in the
// gameplay scene (e.g. an empty "InputZones" object).
public class TouchInputZone : MonoBehaviour
{
    public KeyCode leftZoneKey = KeyCode.LeftArrow;
    public KeyCode rightZoneKey = KeyCode.RightArrow;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            RegisterTouch(Input.mousePosition.x);
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                RegisterTouch(touch.position.x);
            }
        }
    }

    private void RegisterTouch(float screenX)
    {
        KeyCode key = screenX < Screen.width / 2f ? leftZoneKey : rightZoneKey;
        GameManager.instance.SimulateKeyDown(key);
    }
}
