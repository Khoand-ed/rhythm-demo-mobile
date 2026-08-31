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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        theSR = GetComponent<SpriteRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(keyToPress))
        {
            theSR.sprite = pressedImage;
        }
        if(Input.GetKeyUp(keyToPress))
        {
           theSR.sprite = defaultImage;
        }
    }

    // Called by GameManager when a touch zone (see TouchInputZone) presses
    // this button's key. Touches have no key-up to pair with, so the pressed
    // sprite reverts on its own after touchFlashDuration.
    public void FlashPressed()
    {
        theSR.sprite = pressedImage;
        CancelInvoke(nameof(ResetSprite));
        Invoke(nameof(ResetSprite), touchFlashDuration);
    }

    private void ResetSprite()
    {
        theSR.sprite = defaultImage;
    }
}
