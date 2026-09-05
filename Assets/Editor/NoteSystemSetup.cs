using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot migration helper: builds the note prefab, the Conductor, the
// NoteSystem object and its spawn point markers, and derives each lane's
// geometry from what the legacy NoteHolder setup is actually doing - including
// the hit window, which comes out of the real Activator/note collider overlap
// rather than a guess.
public class NoteSystemSetup
{
    private const string NotePrefabPath = "Assets/Prefabs/Note.prefab";
    private const string NoteSpritePath = "Assets/Rhythm Game Tutorial/Graphics/NoteCircle.png";

    [MenuItem("Tools/Rhythm/Set Up Note System")]
    static void SetUp()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first - BeatScroller.beatTempo is mutated at runtime.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("No GameManager in the open scene.");
            return;
        }

        BeatScroller[] holders = Object.FindObjectsByType<BeatScroller>(FindObjectsInactive.Include);
        if (holders.Length == 0)
        {
            Debug.LogError("No BeatScroller holders in the open scene - nothing to migrate from.");
            return;
        }

        NoteView notePrefab = BuildNotePrefab();
        if (notePrefab == null) return;

        SetUpConductor(gameManager);

        // The NoteSystem object has to exist before the lanes are derived,
        // because the spawn point markers are parented under it.
        NoteSpawner spawner = EnsureNoteSystem(notePrefab);

        List<LaneConfig> lanes = DeriveLanes(holders, spawner.transform, out float speed, out float standDuration);
        if (lanes.Count == 0)
        {
            Debug.LogError("Could not derive any lanes - check that every note's keyToPress has a matching ButtonController.");
            return;
        }

        Undo.RecordObject(spawner, "Set Up Note System");
        spawner.lanes = lanes.ToArray();
        spawner.standAtMiddleDuration = standDuration;
        EditorUtility.SetDirty(spawner);

        Undo.RecordObject(gameManager, "Set Up Note System");
        gameManager.noteSpawner = spawner;
        CopyEffectPrefabs(gameManager, holders);
        EditorUtility.SetDirty(gameManager);

        EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);

        ReportTravelTimes(spawner, speed);
    }

    // Spells out what the marker positions mean in seconds, since that is the
    // thing you are really tuning when you drag one.
    private static void ReportTravelTimes(NoteSpawner spawner, float speed)
    {
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        report.AppendLine($"Note system set up: {spawner.lanes.Length} lanes at {speed} units/s.");

        foreach (LaneConfig lane in spawner.lanes)
        {
            report.AppendLine($"  {lane.name}: marker x={lane.SpawnPos.x:F2} -> button x={lane.HitPos.x:F2}, " +
                              $"{lane.LeadTime(speed):F2}s of travel.");
        }

        if (spawner.chart != null)
        {
            float leadIn = spawner.GetRequiredLeadIn();
            report.AppendLine($"  Silent run-up before the music: {leadIn:F2}s " +
                              "(the earliest note needs this long to reach its button from the marker). " +
                              "Drag a marker closer to the button to shorten it.");
        }

        report.Append("Spawn points are the SpawnPoint (...) children of NoteSystem - drag them in the Scene view.");
        Debug.Log(report.ToString());
    }

    // The markers are only auto-placed when they do not already exist, so this
    // is the way to pull them back to the default after moving them around.
    [MenuItem("Tools/Rhythm/Reset Spawn Points To Default")]
    static void ResetSpawnPoints()
    {
        NoteSpawner spawner = Object.FindAnyObjectByType<NoteSpawner>(FindObjectsInactive.Include);
        if (spawner == null || spawner.lanes == null)
        {
            Debug.LogError("No NoteSpawner in the open scene - run Tools/Rhythm/Set Up Note System first.");
            return;
        }

        foreach (LaneConfig lane in spawner.lanes)
        {
            if (!lane.IsValid) continue;

            Undo.RecordObject(lane.spawnPoint, "Reset Spawn Points");
            lane.spawnPoint.position = new Vector3(
                DefaultSpawnX(lane.HitPos.x, lane.SpawnPos.x), lane.HitPos.y, 0f);
            EditorUtility.SetDirty(lane.spawnPoint);
        }

        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        ReportTravelTimes(spawner, spawner.chart != null ? spawner.chart.noteSpeed : 0f);
    }

    // Builds Assets/Prefabs/Note.prefab. Sorting and scale come from an existing
    // scene note so notes draw in the same layer they always did, but the sprite
    // and material are replaced: the scene notes point at a sprite asset that no
    // longer exists, and their lit material renders black in a scene with no
    // Light2D. Physics is dropped - the new system judges on song time, not on
    // trigger overlap.
    private static NoteView BuildNotePrefab()
    {
        Sprite circle = EnsureCircleSprite();
        if (circle == null)
        {
            Debug.LogError($"Could not create or load the note sprite at {NoteSpritePath}.");
            return null;
        }

        NoteObject template = Object.FindAnyObjectByType<NoteObject>(FindObjectsInactive.Include);
        SpriteRenderer templateRenderer = template != null ? template.GetComponent<SpriteRenderer>() : null;

        GameObject temp = new GameObject("Note");
        if (template != null) temp.transform.localScale = template.transform.localScale;

        SpriteRenderer renderer = temp.AddComponent<SpriteRenderer>();
        renderer.sprite = circle;
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

        if (templateRenderer != null)
        {
            renderer.color = templateRenderer.color;
            renderer.sortingLayerID = templateRenderer.sortingLayerID;
            renderer.sortingOrder = templateRenderer.sortingOrder;
        }

        temp.AddComponent<NoteView>();

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NotePrefabPath));
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, NotePrefabPath);
        Object.DestroyImmediate(temp);

        // Read the sprite off the persisted prefab, not off `renderer` - that
        // component belongs to `temp`, which was just destroyed above.
        SpriteRenderer savedRenderer = saved.GetComponent<SpriteRenderer>();
        if (savedRenderer == null || savedRenderer.sprite == null)
        {
            Debug.LogWarning($"{NotePrefabPath} still has no sprite - assign one by hand or notes will be invisible.");
        }

        return saved.GetComponent<NoteView>();
    }

    // Generates a plain white circle as a real texture asset, since the project
    // has no circular sprite to point at.
    private static Sprite EnsureCircleSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(NoteSpritePath);
        if (existing != null) return existing;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.5f - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                // One pixel of feathering so the edge is not stair-stepped.
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
            }
        }

        texture.Apply();

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NoteSpritePath));
        System.IO.File.WriteAllBytes(NoteSpritePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(NoteSpritePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(NoteSpritePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;   // a 256px circle is 1 world unit across
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(NoteSpritePath);
    }

    private static void SetUpConductor(GameManager gameManager)
    {
        if (gameManager.theMusic == null)
        {
            Debug.LogWarning("GameManager.theMusic is not assigned - add the Conductor to the music AudioSource by hand.");
            return;
        }

        GameObject musicObject = gameManager.theMusic.gameObject;
        Conductor conductor = musicObject.GetComponent<Conductor>();

        if (conductor == null)
        {
            conductor = Undo.AddComponent<Conductor>(musicObject);
        }

        Undo.RecordObject(conductor, "Set Up Note System");
        conductor.theMusic = gameManager.theMusic;
        EditorUtility.SetDirty(conductor);
    }

    private static NoteSpawner EnsureNoteSystem(NoteView notePrefab)
    {
        NoteSpawner spawner = Object.FindAnyObjectByType<NoteSpawner>(FindObjectsInactive.Include);

        if (spawner == null)
        {
            GameObject noteSystem = new GameObject("NoteSystem");
            Undo.RegisterCreatedObjectUndo(noteSystem, "Set Up Note System");
            spawner = noteSystem.AddComponent<NoteSpawner>();
            noteSystem.AddComponent<NotePool>();
        }

        NotePool pool = spawner.GetComponent<NotePool>();
        if (pool == null) pool = Undo.AddComponent<NotePool>(spawner.gameObject);

        Undo.RecordObject(pool, "Set Up Note System");
        pool.notePrefab = notePrefab;
        EditorUtility.SetDirty(pool);

        Undo.RecordObject(spawner, "Set Up Note System");
        spawner.pool = pool;
        EditorUtility.SetDirty(spawner);

        return spawner;
    }

    // Reads the legacy setup and turns it into lane geometry. Lanes come out
    // ordered left to right so the indices are stable across runs, which the
    // already-extracted chart's lane numbers depend on.
    private static List<LaneConfig> DeriveLanes(BeatScroller[] holders, Transform markerParent,
                                                out float speed, out float standDuration)
    {
        speed = 0f;
        standDuration = 0.3f;

        Dictionary<KeyCode, ButtonController> buttons = new Dictionary<KeyCode, ButtonController>();
        foreach (ButtonController button in Object.FindObjectsByType<ButtonController>(FindObjectsInactive.Include))
        {
            buttons[button.keyToPress] = button;
        }

        List<LaneConfig> lanes = new List<LaneConfig>();

        foreach (BeatScroller holder in holders)
        {
            NoteObject[] notes = holder.GetComponentsInChildren<NoteObject>(true);
            if (notes.Length == 0) continue;

            // beatTempo is still the authored value here: Awake() has not run.
            speed = holder.beatTempo / 60f;
            standDuration = notes[0].standAtMiddleDuration;

            KeyCode key = notes[0].keyToPress;
            if (!buttons.TryGetValue(key, out ButtonController button))
            {
                Debug.LogWarning($"{holder.name}: no ButtonController for {key}, skipping this lane.");
                continue;
            }

            float hitX = button.transform.position.x;

            float farthestNoteX = notes[0].transform.position.x;
            foreach (NoteObject note in notes)
            {
                if (Mathf.Abs(note.transform.position.x - hitX) > Mathf.Abs(farthestNoteX - hitX))
                {
                    farthestNoteX = note.transform.position.x;
                }
            }

            // Place the marker just off-camera at the button's height, so notes
            // travel straight into the button and slide into view rather than
            // covering a lot of off-screen ground first.
            float spawnX = DefaultSpawnX(hitX, farthestNoteX);

            string laneName = holder.name.Contains("Left") ? "Left" : "Right";

            lanes.Add(new LaneConfig
            {
                name = laneName,
                key = key,
                spawnPoint = EnsureSpawnPoint($"SpawnPoint ({laneName})", markerParent,
                                              new Vector3(spawnX, button.transform.position.y, 0f)),
                hitPoint = button.transform,
            });
        }

        lanes.Sort((a, b) => a.HitPos.x.CompareTo(b.HitPos.x));
        return lanes;
    }

    // Just beyond the camera edge on this lane's side. Falls back to the lane's
    // furthest legacy note when there is no orthographic camera to measure.
    private static float DefaultSpawnX(float hitX, float legacyFarthestX)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic) return legacyFarthestX;

        float halfWidth = camera.orthographicSize * camera.aspect;
        float side = Mathf.Sign(legacyFarthestX - hitX);

        return camera.transform.position.x + side * (halfWidth + 1.5f);
    }

    // Find-or-create, so re-running the tool never undoes a marker you dragged.
    private static Transform EnsureSpawnPoint(string name, Transform parent, Vector3 defaultPosition)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing;

        GameObject marker = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(marker, "Set Up Note System");
        marker.transform.SetParent(parent, false);
        marker.transform.position = defaultPosition;

        return marker.transform;
    }

    // Every note referenced the same three effect prefabs, so they move to the
    // one place that now spawns them.
    private static void CopyEffectPrefabs(GameManager gameManager, BeatScroller[] holders)
    {
        foreach (BeatScroller holder in holders)
        {
            NoteObject note = holder.GetComponentInChildren<NoteObject>(true);
            if (note == null) continue;

            gameManager.hitEffect = note.hitEffect;
            gameManager.goodEffect = note.goodEffect;
            gameManager.perfectEffect = note.perfectEffect;
            return;
        }
    }
}
