using UnityEngine;

// A pooled note. Its position is recomputed from song time every frame rather
// than integrated, so a frame hitch cannot push it out of sync with the audio.
//
// One class serves every note prefab. The Hold prefab additionally wires a
// body and a tail; on Tap and Twin prefabs those stay empty.
public class NoteView : MonoBehaviour
{
    [Tooltip("Hold only. Stretched between head and tail - use Draw Mode Sliced or Tiled.")]
    public SpriteRenderer body;

    [Tooltip("Hold only. Marks where the hold ends.")]
    public Transform tail;

    [Tooltip("Alpha a missed or dropped hold fades to while it scrolls away.")]
    [Range(0f, 1f)]
    public float dimAlpha = 0.35f;

    private NoteData data;
    private float speed;
    private Vector3 hitPos;
    private Vector3 spawnPos;
    private Vector3 direction;
    private Vector3 middlePos;
    private float timeToMiddle;
    private float standUntil;
    private bool judged;
    private bool retired;

    // Song time the head is drawn against. Normally hitTime; while held it
    // follows the clock so the head stays pinned on the button, and on a drop
    // it freezes there so the head moves off from the button instead of
    // jumping to where it would have been.
    private float headTime;
    private bool holding;
    private float ticksCountedUntil;

    private SpriteRenderer[] renderers;
    private Color[] baseColors;

    // The pool this note came from, so it goes back to the right one - Tap,
    // Hold and Twin each have their own prefab and pool.
    public NotePool Pool { get; set; }

    public NoteData Data
    {
        get { return data; }
    }

    public bool Judged
    {
        get { return judged; }
    }

    public bool IsHold
    {
        get { return data.type == NoteType.Hold && data.duration > 0f; }
    }

    public bool Holding
    {
        get { return holding; }
    }

    public float EndTime
    {
        get { return data.hitTime + data.duration; }
    }

    void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;
    }

    public void Bind(NoteData noteData, LaneConfig laneConfig, float noteSpeed,
                     Vector3 middleZonePos, float standAtMiddleDuration)
    {
        data = noteData;
        speed = noteSpeed;
        middlePos = middleZonePos;
        judged = false;
        retired = false;
        holding = false;
        headTime = data.hitTime;
        ticksCountedUntil = data.hitTime;

        SetDimmed(false);

        // Snapshot the lane geometry at spawn time, so moving a marker mid-song
        // re-aims new notes without teleporting the ones already travelling.
        hitPos = laneConfig.HitPos;
        spawnPos = laneConfig.SpawnPos;
        direction = laneConfig.Direction;

        float travelToMiddle = speed > 0f ? Vector3.Distance(hitPos, middleZonePos) / speed : 0f;

        if (IsHold)
        {
            // A hold that is missed or let go keeps scrolling rather than
            // snapping to the middle zone - it is too long to stand there - and
            // is gone once its tail has made the same trip.
            timeToMiddle = float.PositiveInfinity;
            standUntil = EndTime + travelToMiddle;
        }
        else
        {
            // A missed note keeps travelling to the middle zone and pauses there
            // before disappearing - the same beat the old StandAtMiddleThenDisappear
            // coroutine produced, but expressed in song time so pausing and seeking
            // cannot desync it.
            timeToMiddle = data.hitTime + travelToMiddle;
            standUntil = timeToMiddle + standAtMiddleDuration;
        }

        if (body != null) body.gameObject.SetActive(IsHold);
        if (tail != null) tail.gameObject.SetActive(IsHold);
    }

    public void UpdatePosition(float songTime)
    {
        if (retired) return;

        if (judged && songTime >= timeToMiddle)
        {
            // Snap to the marker exactly, y included. The old coroutine did the
            // same, which is why a missed note drops to the marker's height.
            transform.position = middlePos;
            return;
        }

        // Pressed a little early, the head still finishes its approach before
        // it pins, rather than jumping onto the button.
        if (holding) headTime = Mathf.Clamp(songTime, data.hitTime, EndTime);

        // At songTime == hitTime this is exactly hitPos, by construction.
        Vector3 head = hitPos - direction * (speed * (headTime - songTime));
        transform.position = head;

        if (IsHold) UpdateHoldBody(head, songTime);
    }

    private void UpdateHoldBody(Vector3 head, float songTime)
    {
        // A long hold's tail can still be beyond the spawn marker; clamp it
        // there so the body grows out of the marker instead of off-screen.
        float tailDistance = speed * (EndTime - songTime);
        float laneLength = Vector3.Distance(spawnPos, hitPos);
        if (tailDistance > laneLength) tailDistance = laneLength;

        Vector3 tailPos = hitPos - direction * tailDistance;

        if (tail != null) tail.position = tailPos;

        if (body == null) return;

        float length = Vector3.Distance(head, tailPos);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        body.transform.position = (head + tailPos) * 0.5f;
        body.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);

        // size is in the body's local units, so undo whatever scale the
        // prefab carries for the head.
        float scale = Mathf.Abs(body.transform.lossyScale.x);
        body.size = new Vector2(scale > 0.0001f ? length / scale : length, body.size.y);
    }

    // Hit: vanish immediately, exactly as the old SetActive(false) did.
    public void MarkHit()
    {
        judged = true;
        holding = false;
        retired = true;
        gameObject.SetActive(false);
    }

    // Hold head was hit: the note stays up and the body drains into the button.
    public void BeginHold(float songTime)
    {
        judged = true;
        holding = true;
        ticksCountedUntil = Mathf.Max(songTime, data.hitTime);
    }

    // Let go too early: the rest of the hold scrolls away, faded.
    public void MarkDropped(float songTime)
    {
        holding = false;
        headTime = Mathf.Clamp(songTime, data.hitTime, EndTime);
        SetDimmed(true);
    }

    // Missed: stays visible and finishes its run to the middle zone.
    public void MarkMissed()
    {
        judged = true;
        if (IsHold) SetDimmed(true);
    }

    // How many body ticks of `interval` seconds have been held since the last
    // call. Counted on song time, so pausing mid-hold cannot award extra.
    public int ConsumeHoldTicks(float songTime, float interval)
    {
        if (!holding || interval <= 0f) return 0;

        float until = Mathf.Min(songTime, EndTime);
        int ticks = Mathf.FloorToInt((until - ticksCountedUntil) / interval);

        if (ticks > 0) ticksCountedUntil += ticks * interval;
        return Mathf.Max(0, ticks);
    }

    public bool IsFinished(float songTime)
    {
        return retired || (!holding && songTime >= standUntil);
    }

    private void SetDimmed(bool dimmed)
    {
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Color color = baseColors[i];
            if (dimmed) color.a *= dimAlpha;
            renderers[i].color = color;
        }
    }
}
