using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Writes the beatmap.json format by hand rather than through JsonUtility, so
// the note list stays one object per line - which is what makes a chart
// reviewable in a diff and hand-editable. The generator and the timeline
// editor both write through here, so their files are indistinguishable.
public static class BeatmapJsonWriter
{
    public static string Write(BeatmapJson header, List<BeatmapNoteJson> notes)
    {
        StringBuilder json = new StringBuilder();

        json.AppendLine("{");
        json.AppendLine($"  \"stageId\": \"{Escape(header.stageId)}\",");
        json.AppendLine($"  \"songId\": \"{Escape(header.songId)}\",");
        json.AppendLine($"  \"name\": \"{Escape(header.name)}\",");
        json.AppendLine($"  \"author\": \"{Escape(header.author)}\",");
        json.AppendLine($"  \"bpm\": {Number(header.bpm)},");
        json.AppendLine($"  \"difficulty\": {Mathf.Clamp(header.difficulty, 1, 10)},");

        // Where bar lines fall, so editors and beat-synced visuals share the
        // generator's grid. Note times are absolute and never depend on it.
        json.AppendLine($"  \"beatPhase\": {Number(header.beatPhase)},");

        // offset is an audio-sync correction, separate from the grid.
        json.AppendLine($"  \"offset\": {Number(header.offset)},");
        json.AppendLine("  \"notes\": [");

        for (int i = 0; i < notes.Count; i++)
        {
            BeatmapNoteJson note = notes[i];
            string comma = i < notes.Count - 1 ? "," : "";
            json.AppendLine($"    {{ \"id\": {note.id}, {Body(note)} }}{comma}");
        }

        json.AppendLine("  ]");
        json.AppendLine("}");

        return json.ToString();
    }

    // Only the keys each type actually uses.
    private static string Body(BeatmapNoteJson note)
    {
        switch (note.type)
        {
            case "Hold":
                return $"\"type\": \"Hold\", \"lane\": \"{note.lane}\", " +
                       $"\"startTime\": {Number(note.startTime)}, \"endTime\": {Number(note.endTime)}";

            case "Twin":
                return $"\"type\": \"Twin\", \"time\": {Number(note.time)}";

            default:
                return $"\"type\": \"Tap\", \"lane\": \"{note.lane}\", \"time\": {Number(note.time)}";
        }
    }

    public static string Number(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
