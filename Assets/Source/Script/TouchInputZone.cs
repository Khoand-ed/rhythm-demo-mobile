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

    // Hold notes need to know a finger is still down, not just that one
    // landed. A finger that slides across the middle counts for the half it
    // is on now, which is what a player sliding off a hold would expect.
    public bool IsHeld(KeyCode key)
    {
        if (Input.GetMouseButton(0) && KeyFor(Input.mousePosition.x) == key) return true;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) continue;
            if (KeyFor(touch.position.x) == key) return true;
        }

        return false;
    }

    private KeyCode KeyFor(float screenX)
    {
        return screenX < Screen.width / 2f ? leftZoneKey : rightZoneKey;
    }

    private void RegisterTouch(float screenX)
    {
        GameManager.instance.SimulateKeyDown(KeyFor(screenX));
    }
}
