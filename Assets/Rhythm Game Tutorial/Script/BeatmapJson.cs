using System.Collections.Generic;

// The on-disk beatmap.json shape, mirrored field for field so the documented
// format stays the source of truth. JsonUtility cannot map string values onto
// enums, so `type` and `lane` arrive as plain strings and are converted during
// import.
[System.Serializable]
public class BeatmapJson
{
    public string stageId;
    public string songId;
    public string name;
    public string author;

    public float bpm;
    public int difficulty;

    // Audio sync offset in seconds, authored against this song's own audio.
    public float offset;

    public List<BeatmapNoteJson> notes = new List<BeatmapNoteJson>();
}

// Every key any note type can carry. JsonUtility fills absent numbers with 0
// and cannot report which keys were present, so which fields are meaningful is
// decided by `type` rather than by presence.
[System.Serializable]
public class BeatmapNoteJson
{
    public int id;

    // Tap | Hold | Twin
    public string type;

    // Left | Right. Ignored for Twin, which occupies both lanes.
    public string lane;

    // Tap and Twin.
    public float time;

    // Hold.
    public float startTime;
    public float endTime;
}
