using System.IO;
using Data.Story;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// 剧情脚本导入 / Imports a <c>.story</c> file as a TextAsset - what <c>Resources.Load</c> hands the
/// game - and reads it on the way in, so a mistake shows in the Console the moment the file is
/// saved, pointing at the line, instead of when the story is played.
///
/// The format is described on <see cref="StoryScript"/>.
/// </summary>
[ScriptedImporter(1, "story")]
public class StoryScriptImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        string source = File.ReadAllText(ctx.assetPath);
        string id = Path.GetFileNameWithoutExtension(ctx.assetPath);

        TextAsset asset = new TextAsset(source);
        ctx.AddObjectToAsset("main", asset);
        ctx.SetMainObject(asset);

        StoryScript script = StoryScript.Parse(id, source);
        foreach (string error in script.Errors) ctx.LogImportError($"[StoryScriptImporter] {ctx.assetPath}:{error}", asset);
        foreach (string warning in script.Warnings) ctx.LogImportWarning($"[StoryScriptImporter] {ctx.assetPath}:{warning}", asset);
    }
}
