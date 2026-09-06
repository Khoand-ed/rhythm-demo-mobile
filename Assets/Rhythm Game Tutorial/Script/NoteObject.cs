using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteObject : MonoBehaviour
{

    public bool canBePressed;

    public KeyCode keyToPress;

    private bool Obtained = false;

    public GameObject hitEffect, goodEffect, perfectEffect;

    public float perfectWindow = 0.05f;
    public float goodWindow = 0.1f;

    public float standAtMiddleDuration = 0.3f;

    // The AudioSource.time this note should ideally be hit at. Derived once
    // at Start() from this note's starting distance to its own hit zone and
    // its lane's speed, so existing hand-placed notes need no re-authoring.
    private float targetTime;

    // The AudioSource.time this note reaches the middle zone if never hit.
    private float timeToReachMiddle;

    void Start()
    {
        BeatScroller lane = GetComponentInParent<BeatScroller>();
        float speed = lane != null ? Mathf.Abs(lane.beatTempo) : 0f;
        float startX = transform.position.x;

        float hitZoneX = GameManager.instance.GetHitZoneX(keyToPress);
        float distance = Mathf.Abs(startX - hitZoneX);
        targetTime = speed > 0f ? distance / speed : 0f;

        float middleX = GameManager.instance.GetMiddleZonePosition().x;
        float distanceToMiddle = Mathf.Abs(startX - middleX);
        timeToReachMiddle = speed > 0f ? distanceToMiddle / speed : 0f;
    }

    // Called by GameManager when this note is next in line for its key.
    public void TryHit()
    {
        Obtained = true;
        gameObject.SetActive(false);

        float delta = Mathf.Abs(GameManager.instance.theMusic.time - targetTime);

        if (delta <= perfectWindow)
        {
            Debug.Log("Perfect");
            GameManager.instance.PerfectHit();
            Instantiate(perfectEffect, transform.position, perfectEffect.transform.rotation);
        }
        else if (delta <= goodWindow)
        {
            Debug.Log("Good");
            GameManager.instance.GoodHit();
            Instantiate(goodEffect, transform.position, goodEffect.transform.rotation);
        }
        else
        {
            Debug.Log("Hit");
            GameManager.instance.NormalHit();
            Instantiate(hitEffect, transform.position, hitEffect.transform.rotation);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if(other.CompareTag("Activator"))
        {
            canBePressed = true;
            GameManager.instance.RegisterNote(keyToPress, this);
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Activator"))
        {
            canBePressed = false;

            if (!Obtained)
            {
                GameManager.instance.NoteMissed(NoteType.Tap);
                StartCoroutine(StandAtMiddleThenDisappear());
            }

            GameManager.instance.UnregisterNote(keyToPress, this);
        }
    }

    private IEnumerator StandAtMiddleThenDisappear()
    {
        float delay = timeToReachMiddle - GameManager.instance.theMusic.time;
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        // Detach so the note stops inheriting the lane's scroll movement.
        transform.SetParent(null, true);
        transform.position = GameManager.instance.GetMiddleZonePosition();

        yield return new WaitForSeconds(standAtMiddleDuration);

        // Future hook: trigger the character-attack reaction here.
        gameObject.SetActive(false);
    }
}
