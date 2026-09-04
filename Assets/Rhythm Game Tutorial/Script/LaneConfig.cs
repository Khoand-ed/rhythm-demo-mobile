using UnityEngine;

// Geometry for one lane, defined by two scene markers so it can be positioned
// visually in the Scene view instead of by typing coordinates.
[System.Serializable]
public class LaneConfig
{
    public string name = "Left";

    public KeyCode key = KeyCode.LeftArrow;

    [Tooltip("Where notes in this lane appear. Drag this marker in the Scene view to move the spawn point.")]
    public Transform spawnPoint;

    [Tooltip("Where notes in this lane are judged - normally this lane's button.")]
    public Transform hitPoint;

    [Tooltip("A press resolves a note while |songTime - hitTime| is within this; past it the note is missed. " +
             "Reproduces the width of the old Activator trigger for this lane.")]
    public float hitWindow = 0.6f;

    public bool IsValid
    {
        get { return spawnPoint != null && hitPoint != null; }
    }

    public Vector3 SpawnPos
    {
        get { return spawnPoint.position; }
    }

    public Vector3 HitPos
    {
        get { return hitPoint.position; }
    }

    // Unit vector from the spawn marker toward the hit point, so moving either
    // marker re-aims the lane.
    public Vector3 Direction
    {
        get
        {
            Vector3 delta = HitPos - SpawnPos;
            return delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector3.right;
        }
    }

    // How long a note is alive before its hit time. Moving the spawn marker
    // further out makes notes appear earlier; it never changes when they land.
    public float LeadTime(float speed)
    {
        return speed > 0f ? Vector3.Distance(SpawnPos, HitPos) / speed : 0f;
    }
}
