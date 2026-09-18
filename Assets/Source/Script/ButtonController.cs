using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ButtonController : MonoBehaviour
{
    private SpriteRenderer theSR;
    public Sprite defaultImage;
    public Sprite pressedImage;

    public KeyCode keyToPress;
    public float touchFlashDuration = 0.1f;

    private float flashUntil;
    private PunchScale punch;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        theSR = GetComponent<SpriteRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        // Held state comes from GameManager so a finger resting on a hold note
        // keeps the button down, the same as a held key does.
        bool held = GameManager.instance != null
            ? GameManager.instance.IsLaneHeld(keyToPress)
            : Input.GetKey(keyToPress);

        theSR.sprite = held || Time.unscaledTime < flashUntil ? pressedImage : defaultImage;
    }

    // Called by GameManager when a touch zone (see TouchInputZone) presses
    // this button's key. A tap can start and end inside one frame, so the
    // pressed sprite is kept up for at least touchFlashDuration to be seen.
    public void FlashPressed()
    {
        flashUntil = Time.unscaledTime + touchFlashDuration;
    }

    // Called by GameManager on every press in this lane. The PunchScale is
    // added by Tools/Rhythm/Set Up Hit Feedback; without one this is a no-op.
    public void Punch()
    {
        if (punch == null) punch = GetComponent<PunchScale>();
        if (punch != null) punch.Punch();
    }
}
