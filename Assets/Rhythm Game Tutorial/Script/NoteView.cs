using UnityEngine;

// A pooled note. Its position is recomputed from song time every frame rather
// than integrated, so a frame hitch cannot push it out of sync with the audio.
public class NoteView : MonoBehaviour
{
    private NoteData data;
    private float speed;
    private Vector3 hitPos;
    private Vector3 direction;
    private Vector3 middlePos;
    private float timeToMiddle;
    private float standUntil;
    private bool judged;
    private bool retired;

    public NoteData Data
    {
        get { return data; }
    }

    public bool Judged
    {
        get { return judged; }
    }

    public void Bind(NoteData noteData, LaneConfig laneConfig, float noteSpeed,
                     Vector3 middleZonePos, float standAtMiddleDuration)
    {
        data = noteData;
        speed = noteSpeed;
        middlePos = middleZonePos;
        judged = false;
        retired = false;

        // Snapshot the lane geometry at spawn time, so moving a marker mid-song
        // re-aims new notes without teleporting the ones already travelling.
        hitPos = laneConfig.HitPos;
        direction = laneConfig.Direction;

        // A missed note keeps travelling to the middle zone and pauses there
        // before disappearing - the same beat the old StandAtMiddleThenDisappear
        // coroutine produced, but expressed in song time so pausing and seeking
        // cannot desync it.
        timeToMiddle = speed > 0f
            ? data.hitTime + Vector3.Distance(hitPos, middleZonePos) / speed
            : data.hitTime;

        standUntil = timeToMiddle + standAtMiddleDuration;
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

        // At songTime == hitTime this is exactly hitPos, by construction.
        transform.position = hitPos - direction * (speed * (data.hitTime - songTime));
    }

    // Hit: vanish immediately, exactly as the old SetActive(false) did.
    public void MarkHit()
    {
        judged = true;
        retired = true;
        gameObject.SetActive(false);
    }

    // Missed: stays visible and finishes its run to the middle zone.
    public void MarkMissed()
    {
        judged = true;
    }

    public bool IsFinished(float songTime)
    {
        return retired || songTime >= standUntil;
    }
}
