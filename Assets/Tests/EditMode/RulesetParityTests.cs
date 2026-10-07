using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// server/data/ruleset.json, held to the Unity assets it describes.
//
// 规则有两份, 这里让它们一致 / The device's tuning lives where designers edit it - three assets,
// GameManager's fields in Main.unity, each operator's CharMeta and passive asset. The server
// cannot load any of those, so it reads the same numbers from ruleset.json, and this is what
// stops the two drifting: retune a window in the Inspector without updating the JSON and this
// fails in CI, naming the field, before any player's run is judged against stale rules.
//
// 读场景不靠类型 / GameManager lives in the default assembly, which no asmdef can reference, so it
// is found by type name and read through SerializedObject. CharMeta is read the same way, which
// keeps this assembly referencing nothing but Rhythm.Core.
public class RulesetParityTests
{
    private const string GameplayScene = "Assets/Scenes/Main.unity";
    private const string CharMetaFolder = "Assets/Arknights/Resources/Meta/Char";

    private static Ruleset Json()
    {
        string path = Path.Combine(Application.dataPath, "..", "server", "data", "ruleset.json");
        return JsonUtility.FromJson<Ruleset>(File.ReadAllText(path));
    }

    /// <summary>Opens the gameplay scene beside whatever is open, reads it, and closes it again.</summary>
    private static T WithGameManager<T>(System.Func<SerializedObject, T> read)
    {
        Scene scene = SceneManager.GetSceneByPath(GameplayScene);
        bool opened = !scene.isLoaded;

        if (opened) scene = EditorSceneManager.OpenScene(GameplayScene, OpenSceneMode.Additive);

        try
        {
            MonoBehaviour manager = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(c => c != null && c.GetType().Name == "GameManager");

            Assert.IsNotNull(manager, GameplayScene + " has no GameManager");
            return read(new SerializedObject(manager));
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void ScoreAwardsAndLadder_MatchGameManager()
    {
        ScoreRules json = Json().score;

        WithGameManager(gm =>
        {
            Assert.AreEqual(gm.FindProperty("scorePerPerfectNote").intValue, json.perfect, "score.perfect");
            Assert.AreEqual(gm.FindProperty("scorePerGoodNote").intValue, json.great, "score.great");
            Assert.AreEqual(gm.FindProperty("scorePerNote").intValue, json.hit, "score.hit");
            Assert.AreEqual(gm.FindProperty("scorePerHoldTick").intValue, json.holdTick, "score.holdTick");
            Assert.AreEqual(gm.FindProperty("holdTickInterval").floatValue, json.holdTickInterval, "score.holdTickInterval");

            SerializedProperty ladder = gm.FindProperty("multiplierThresholds");
            int[] scene = new int[ladder.arraySize];
            for (int i = 0; i < scene.Length; i++) scene[i] = ladder.GetArrayElementAtIndex(i).intValue;

            CollectionAssert.AreEqual(scene, json.multiplierThresholds, "score.multiplierThresholds");
            return true;
        });
    }

    [Test]
    public void JudgeHealthAndFever_MatchTheAssetsGameManagerUses()
    {
        Ruleset json = Json();

        WithGameManager(gm =>
        {
            JudgeSettings judge = (JudgeSettings)gm.FindProperty("judge").objectReferenceValue;
            HealthSettings health = (HealthSettings)gm.FindProperty("health").objectReferenceValue;
            FeverSettings fever = (FeverSettings)gm.FindProperty("fever").objectReferenceValue;

            Assert.IsNotNull(judge, "GameManager.judge is unassigned");
            Assert.IsNotNull(health, "GameManager.health is unassigned");
            Assert.IsNotNull(fever, "GameManager.fever is unassigned");

            AssertSame(JudgeRules.From(judge), json.judge, "judge");
            AssertSame(HealthRules.From(health), json.health, "health");
            AssertSame(FeverRules.From(fever), json.fever, "fever");
            return true;
        });
    }

    [Test]
    public void EveryOperator_MatchesItsCharMetaAndPassive()
    {
        Ruleset json = Json();

        string[] assets = AssetDatabase.FindAssets("t:ScriptableObject", new[] { CharMetaFolder })
            .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
            .Where(p => p.EndsWith(".asset"))
            .ToArray();

        CollectionAssert.AreEquivalent(
            assets.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray(),
            json.operators.Select(o => o.id).ToArray(),
            "every operator on the roster needs an entry in ruleset.json, and nothing else may have one");

        foreach (string path in assets)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            OperatorRules expected = json.FindOperator(id);
            SerializedObject meta = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(path));

            // 生效值 / The effective values, with GameManager.CaptureOperator's fallback: a 0
            // means a CharMeta from before the rhythm fields, played at 1.
            float score = meta.FindProperty("scoreModifier").floatValue;
            float feverGain = meta.FindProperty("feverModifier").floatValue;

            Assert.AreEqual(score > 0f ? score : 1f, expected.scoreModifier, id + ".scoreModifier");
            Assert.AreEqual(feverGain > 0f ? feverGain : 1f, expected.feverModifier, id + ".feverModifier");
            Assert.AreEqual(Mathf.Max(0, meta.FindProperty("maxHp").intValue), expected.maxHp, id + ".maxHp");

            PassiveRules passive = PassiveRules.From((PassiveSO)meta.FindProperty("passive").objectReferenceValue);

            Assert.AreEqual(passive.type ?? "", expected.passive.type ?? "", id + ".passive.type");
            CollectionAssert.AreEquivalent(
                passive.parameters.Select(p => p.name + "=" + p.value).ToArray(),
                expected.passive.parameters.Select(p => p.name + "=" + p.value).ToArray(),
                id + ".passive.parameters");
        }
    }

    private static void AssertSame<T>(T unity, T json, string section)
    {
        foreach (System.Reflection.FieldInfo field in typeof(T).GetFields())
        {
            Assert.AreEqual(field.GetValue(unity), field.GetValue(json), section + "." + field.Name);
        }
    }
}
