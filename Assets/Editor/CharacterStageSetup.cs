using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts the operator on the rhythm stage: one CharacterStage object at the middle zone, carrying a
// CharacterPresenter, wired to GameManager.character. Find-or-create throughout, so re-running never
// moves or retunes a stage you have already adjusted - use Reset Character Stage To Default for that.
//
// The stage is empty until an operator with a battle rig is chosen (Arknights/Art/Install AMIYA
// Placeholder Art gives AMIYA one), so a clone without that art plays exactly as before.
public class CharacterStageSetup
{
    private const string StageName = "CharacterStage";

    [MenuItem("Tools/Rhythm/Set Up Character Stage")]
    static void SetUp()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("[CharacterStageSetup] No GameManager in the open scene. Open the gameplay scene first.");
            return;
        }

        // 以 GameManager 所在的场景为准 / The GameManager's scene is the one that counts, which is not
        // necessarily the active one when several are open.
        Scene scene = gameManager.gameObject.scene;
        CharacterPresenter presenter = FindIn(scene);
        bool created = presenter == null;

        if (created)
        {
            GameObject stage = new GameObject(StageName, typeof(CharacterPresenter));
            SceneManager.MoveGameObjectToScene(stage, scene);
            Undo.RegisterCreatedObjectUndo(stage, "Set Up Character Stage");
            stage.transform.position = MarkerPosition(gameManager);
            presenter = stage.GetComponent<CharacterPresenter>();
            presenter.ResetToDefaults();
        }

        bool wired = false;
        if (gameManager.character == null)
        {
            Undo.RecordObject(gameManager, "Set Up Character Stage");
            gameManager.character = presenter;
            EditorUtility.SetDirty(gameManager);
            wired = true;
        }

        EditorUtility.SetDirty(presenter);
        EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);

        Debug.Log($"[CharacterStageSetup] {(created ? "Created" : "Kept")} '{presenter.name}' at {presenter.transform.position}; " +
                  $"GameManager.character {(wired ? "wired" : "was already set")}. " +
                  "Move the stage in the Scene view or change Target Height on the presenter; re-running this tool " +
                  "will not touch either. The stage shows nobody until an operator with a battle rig plays.");
    }

    [MenuItem("Tools/Rhythm/Reset Character Stage To Default")]
    static void Reset()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        CharacterPresenter presenter = gameManager != null ? FindIn(gameManager.gameObject.scene) : Object.FindAnyObjectByType<CharacterPresenter>(FindObjectsInactive.Include);
        if (presenter == null)
        {
            Debug.LogError("[CharacterStageSetup] No CharacterPresenter in the open scene. Run Set Up Character Stage first.");
            return;
        }

        Undo.RecordObject(presenter.transform, "Reset Character Stage");
        Undo.RecordObject(presenter, "Reset Character Stage");
        presenter.transform.position = MarkerPosition(gameManager);
        presenter.ResetToDefaults();

        EditorUtility.SetDirty(presenter);
        EditorSceneManager.MarkSceneDirty(presenter.gameObject.scene);

        Debug.Log($"[CharacterStageSetup] '{presenter.name}' is back at {presenter.transform.position} with default placement and reactions.");
    }

    private static CharacterPresenter FindIn(Scene scene)
    {
        foreach (CharacterPresenter candidate in Object.FindObjectsByType<CharacterPresenter>(FindObjectsInactive.Include))
        {
            if (candidate.gameObject.scene == scene) return candidate;
        }

        return null;
    }

    // "Where the character stands" - GameManager's own words for the middle zone marker.
    private static Vector3 MarkerPosition(GameManager gameManager)
    {
        if (gameManager != null && gameManager.middleZoneMarker != null) return gameManager.middleZoneMarker.position;

        Debug.LogWarning("[CharacterStageSetup] GameManager.middleZoneMarker is not assigned; using the origin.");
        return Vector3.zero;
    }
}
