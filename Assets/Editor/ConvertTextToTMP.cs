using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ConvertTextToTMP
{
    [MenuItem("Tools/Convert All Legacy Text To TMP")]
    static void ConvertAllText()
    {
        Text[] texts = Object.FindObjectsByType<Text>(
            FindObjectsInactive.Include
        );

        int count = 0;

        foreach (Text oldText in texts)
        {
            GameObject obj = oldText.gameObject;

            string content = oldText.text;
            int fontSize = oldText.fontSize;
            Color color = oldText.color;
            TextAnchor alignment = oldText.alignment;

            Undo.RecordObject(obj, "Convert Text To TMP");

            Object.DestroyImmediate(oldText);

            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();

            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;

            count++;
        }

        Debug.Log($"Converted {count} Text components to TextMeshPro.");
    }
}