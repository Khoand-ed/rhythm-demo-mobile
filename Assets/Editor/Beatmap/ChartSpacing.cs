using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The scroll-speed rule, in one place. The importer uses it to set a chart's
// noteSpeed and the generator uses it to preview that speed, so the number the
// Beatmap Studio reports is the number the imported chart will actually get.
public static class ChartSpacing
{
    public const string NotePrefabPath = "Assets/Prefabs/Note.prefab";

    // Fraction of clear space to leave between two notes in the same lane.
    public const float SpacingMargin = 1.05f;

    // Used only when the note prefab cannot be measured.
    public const float FallbackNoteWidth = 1.25f;

    // bpm sets the baseline scroll speed. If that would let two notes in the
    // same lane touch, the speed is raised just enough to keep them apart -
    // which never changes when a note lands, only how early it appears.
    public static float ResolveSpeed(List<NoteData> notes, float baseSpeed, float noteWidth, out float minGap)
    {
        minGap = float.MaxValue;

        // Last time each lane is still occupied: a Hold blocks its lane until
        // its tail, so the gap is measured from the end of the hold.
        Dictionary<int, float> laneFreeAt = new Dictionary<int, float>();

        for (int i = 0; i < notes.Count; i++)
        {
            NoteData note = notes[i];

            foreach (int lane in LanesOccupiedBy(note))
            {
                float previousEnd;
                if (laneFreeAt.TryGetValue(lane, out previousEnd))
                {
                    minGap = Mathf.Min(minGap, note.hitTime - previousEnd);
                }

                laneFreeAt[lane] = note.hitTime + note.duration;
            }
        }

        if (minGap == float.MaxValue || minGap <= 0f) return baseSpeed;

        float required = noteWidth * SpacingMargin / minGap;
        return Mathf.Max(baseSpeed, required);
    }

    // The scroll speed a beatmap will get once imported, straight from its
    // json notes - so the generator and the timeline editor can preview it.
    public static float ImpliedSpeed(List<BeatmapNoteJson> notes, float bpm)
    {
        List<NoteData> data = new List<NoteData>(notes.Count);

        for (int i = 0; i < notes.Count; i++)
        {
            BeatmapNoteJson note = notes[i];
            int lane = note.lane == "Right" ? 1 : 0;

            switch (note.type)
            {
                case "Hold":
                    data.Add(new NoteData
                    {
                        type = NoteType.Hold,
                        lane = lane,
                        hitTime = note.startTime,
                        duration = note.endTime - note.startTime,
                    });
                    break;

                case "Twin":
                    data.Add(new NoteData { type = NoteType.Twin, hitTime = note.time });
                    break;

                default:
                    data.Add(new NoteData { type = NoteType.Tap, lane = lane, hitTime = note.time });
                    break;
            }
        }

        data.Sort((a, b) => a.hitTime.CompareTo(b.hitTime));

        float minGap;
        return ResolveSpeed(data, bpm / 60f, MeasureNoteWidth(), out minGap);
    }

    // A Twin occupies both lanes at once, so it counts against each of them.
    public static IEnumerable<int> LanesOccupiedBy(NoteData note)
    {
        if (note.type == NoteType.Twin)
        {
            yield return 0;
            yield return 1;
        }
        else
        {
            yield return note.lane;
        }
    }

    // The note's on-screen width is what two notes have to clear to not overlap.
    public static float MeasureNoteWidth()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NotePrefabPath);
        if (prefab == null) return FallbackNoteWidth;

        SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null || renderer.sprite == null) return FallbackNoteWidth;

        return renderer.sprite.bounds.size.x * Mathf.Abs(prefab.transform.localScale.x);
    }
}
