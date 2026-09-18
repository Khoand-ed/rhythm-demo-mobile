using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the hit feedback as ordinary assets and wires it into the open
// gameplay scene: synthesised hit sounds, judgement popups made from the
// existing effect prefabs, FAST/SLOW tags, a burst ring, and punch components
// on the lane buttons and the combo counter.
//
// Find-or-create throughout: anything that already exists is left alone, so
// restyled prefabs and swapped-in sounds survive a rerun. Delete an asset to
// have it rebuilt.
public class HitFeedbackSetup
{
    private const string AudioFolder = "Assets/Source/Audio";
    private const string PrefabFolder = "Assets/Prefabs/Feedback";
    private const string RingSpritePath = "Assets/Source/Graphics/HitRing.png";

    private const string TapClipPath = AudioFolder + "/hit_tap.wav";
    private const string TwinClipPath = AudioFolder + "/hit_twin.wav";
    private const string HoldEndClipPath = AudioFolder + "/hit_hold_end.wav";

    private const int SampleRate = 44100;

    // Effects draw above notes (0-1) but below nothing else in the scene.
    private const int EffectSortingOrder = 10;

    [MenuItem("Tools/Rhythm/Set Up Hit Feedback")]
    static void SetUp()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("Exit Play Mode first.");
            return;
        }

        GameManager gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager == null)
        {
            Debug.LogError("No GameManager in the open scene - open Main.unity first.");
            return;
        }

        EnsureFolder(AudioFolder);
        EnsureFolder(PrefabFolder);

        AudioClip tap = EnsureClip(TapClipPath, SynthTap);
        AudioClip twin = EnsureClip(TwinClipPath, SynthTwin);
        AudioClip holdEnd = EnsureClip(HoldEndClipPath, SynthHoldEnd);

        FeedbackPop perfect = EnsureJudgementPopup("JudgePerfect", gameManager.perfectEffect);
        FeedbackPop great = EnsureJudgementPopup("JudgeGreat", gameManager.goodEffect);
        FeedbackPop hit = EnsureJudgementPopup("JudgeHit", gameManager.hitEffect);
        FeedbackPop miss = EnsureJudgementPopup("JudgeMiss",
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MissEffect.prefab"));

        FeedbackPop fast = EnsureTimingTag("TimingFast", "FAST", new Color(0.35f, 0.75f, 1f));
        FeedbackPop slow = EnsureTimingTag("TimingSlow", "SLOW", new Color(1f, 0.55f, 0.25f));
        FeedbackPop burst = EnsureBurst();

        HitFeedback feedback = EnsureFeedbackObject(gameManager);

        Undo.RecordObject(feedback, "Set Up Hit Feedback");
        if (feedback.tapClip == null) feedback.tapClip = tap;
        if (feedback.twinClip == null) feedback.twinClip = twin;
        if (feedback.holdEndClip == null) feedback.holdEndClip = holdEnd;
        if (feedback.perfectPopup == null) feedback.perfectPopup = perfect;
        if (feedback.greatPopup == null) feedback.greatPopup = great;
        if (feedback.hitPopup == null) feedback.hitPopup = hit;
        if (feedback.missPopup == null) feedback.missPopup = miss;
        if (feedback.fastTag == null) feedback.fastTag = fast;
        if (feedback.slowTag == null) feedback.slowTag = slow;
        if (feedback.burst == null) feedback.burst = burst;

        if (feedback.comboPunch == null && gameManager.multiText != null)
        {
            feedback.comboPunch = EnsurePunch(gameManager.multiText.gameObject, 0.35f, 0.15f);
        }

        EditorUtility.SetDirty(feedback);

        System.Collections.Generic.List<PunchScale> buttonPunches = new System.Collections.Generic.List<PunchScale>();
        foreach (ButtonController button in Object.FindObjectsByType<ButtonController>(FindObjectsInactive.Include))
        {
            buttonPunches.Add(EnsurePunch(button.gameObject, 0.15f, 0.12f));
        }

        // The buttons also breathe with the beat, so the tempo is felt
        // before the first note arrives.
        BeatPulse pulse = feedback.GetComponent<BeatPulse>();
        if (pulse == null)
        {
            pulse = Undo.AddComponent<BeatPulse>(feedback.gameObject);
            pulse.targets = buttonPunches.ToArray();
            EditorUtility.SetDirty(pulse);
        }

        Undo.RecordObject(gameManager, "Set Up Hit Feedback");
        gameManager.feedback = feedback;
        EditorUtility.SetDirty(gameManager);

        LowerAudioLatency();

        EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);
        Selection.activeObject = feedback.gameObject;

        Debug.Log("Hit feedback set up on the HitFeedback object. Popups, tags and the burst ring are in " +
                  $"{PrefabFolder}; hit sounds are in {AudioFolder}. Swap any of them in the HitFeedback " +
                  "Inspector - rerunning this tool will not overwrite your choices.", feedback);
    }

    private static HitFeedback EnsureFeedbackObject(GameManager gameManager)
    {
        HitFeedback feedback = gameManager.feedback != null
            ? gameManager.feedback
            : Object.FindAnyObjectByType<HitFeedback>(FindObjectsInactive.Include);

        if (feedback == null)
        {
            GameObject created = new GameObject("HitFeedback");
            Undo.RegisterCreatedObjectUndo(created, "Set Up Hit Feedback");
            SceneManagerMoveToScene(created, gameManager.gameObject.scene);
            feedback = created.AddComponent<HitFeedback>();
        }

        if (feedback.hitSource == null)
        {
            AudioSource source = feedback.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(feedback.gameObject);

            source.playOnAwake = false;
            source.spatialBlend = 0f;

            // Highest priority: a hit sound cut for lack of voices is worse
            // than any other sound going missing.
            source.priority = 0;

            feedback.hitSource = source;
        }

        return feedback;
    }

    private static void SceneManagerMoveToScene(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }

    private static PunchScale EnsurePunch(GameObject target, float amount, float duration)
    {
        PunchScale punch = target.GetComponent<PunchScale>();
        if (punch != null) return punch;

        punch = Undo.AddComponent<PunchScale>(target);
        punch.amount = amount;
        punch.duration = duration;
        EditorUtility.SetDirty(punch);
        return punch;
    }

    // Judgement popups reuse the existing Perfect/Good/Hit/Missed effect art
    // and its particles, but as a pooled FeedbackPop instead of an
    // instantiate-and-destroy EffectObject. The originals stay untouched for
    // the legacy note path.
    private static FeedbackPop EnsureJudgementPopup(string name, GameObject source)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        FeedbackPop existing = AssetDatabase.LoadAssetAtPath<FeedbackPop>(path);
        if (existing != null) return existing;

        if (source == null)
        {
            Debug.LogWarning($"No source effect for {name}; skipping it. Assign one on HitFeedback by hand.");
            return null;
        }

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = name;
        root.transform.position = Vector3.zero;

        foreach (EffectObject old in root.GetComponentsInChildren<EffectObject>(true))
        {
            Object.DestroyImmediate(old);
        }

        foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, EffectSortingOrder);
        }

        FeedbackPop pop = root.AddComponent<FeedbackPop>();
        pop.lifetime = 0.5f;
        pop.startScale = 1.45f;
        pop.endScale = 1f;
        pop.scaleTime = 0.09f;
        pop.rise = 0.25f;
        pop.fadeStart = 0.55f;

        return SavePrefab(root, path);
    }

    private static FeedbackPop EnsureTimingTag(string name, string label, Color color)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        FeedbackPop existing = AssetDatabase.LoadAssetAtPath<FeedbackPop>(path);
        if (existing != null) return existing;

        GameObject root = new GameObject(name);
        TMPro.TextMeshPro text = root.AddComponent<TMPro.TextMeshPro>();
        text.text = label;
        text.fontSize = 3.2f;
        text.fontStyle = TMPro.FontStyles.Bold;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = color;
        text.rectTransform.sizeDelta = new Vector2(3f, 0.8f);

        MeshRenderer renderer = root.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sortingOrder = EffectSortingOrder + 1;

        FeedbackPop pop = root.AddComponent<FeedbackPop>();
        pop.lifetime = 0.4f;
        pop.startScale = 1.2f;
        pop.endScale = 1f;
        pop.scaleTime = 0.07f;
        pop.rise = 0.15f;
        pop.fadeStart = 0.5f;

        return SavePrefab(root, path);
    }

    // An expanding, fading ring out of the button - the "impact" of a hit.
    // HitFeedback tints it by judgement, so one prefab serves every result.
    private static FeedbackPop EnsureBurst()
    {
        string path = $"{PrefabFolder}/HitBurst.prefab";
        FeedbackPop existing = AssetDatabase.LoadAssetAtPath<FeedbackPop>(path);
        if (existing != null) return existing;

        Sprite ring = EnsureRingSprite();
        if (ring == null) return null;

        GameObject root = new GameObject("HitBurst");
        root.transform.localScale = Vector3.one * 1.6f;

        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = ring;
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        renderer.sortingOrder = EffectSortingOrder - 1;

        FeedbackPop pop = root.AddComponent<FeedbackPop>();
        pop.lifetime = 0.32f;
        pop.startScale = 0.55f;
        pop.endScale = 1.35f;
        pop.scaleTime = 0.32f;
        pop.rise = 0f;
        pop.fadeStart = 0.15f;

        return SavePrefab(root, path);
    }

    private static FeedbackPop SavePrefab(GameObject root, string path)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved != null ? saved.GetComponent<FeedbackPop>() : null;
    }

    // A soft white ring, 1 world unit across, thick enough to read at speed.
    private static Sprite EnsureRingSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
        if (existing != null) return existing;

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float outer = size * 0.5f - 2f;
        float thickness = size * 0.08f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float edge = Mathf.Min(outer - distance, distance - (outer - thickness));

                // A faint fill inside the ring gives the burst some body.
                float alpha = edge >= 0f ? Mathf.Clamp01(edge) : (distance < outer - thickness ? 0.12f * (distance / outer) : 0f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        System.IO.File.WriteAllBytes(RingSpritePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(RingSpritePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(RingSpritePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
    }

    // Hit sounds are synthesised rather than shipped, so the project has no
    // third-party audio to license. They are plain .wav files: replace them
    // with recorded ones at any time.
    private static AudioClip EnsureClip(string path, System.Func<float[]> synth)
    {
        AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (existing != null) return existing;

        WriteWav(path, Normalise(synth(), 0.9f));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer != null)
        {
            // Decompressed and preloaded: a hit sound that waits on a decoder
            // lands late, which is the one thing it must not do.
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.forceToMono = true;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    // A crisp tick with a little body: a bright click for timing, a short low
    // thump so it has weight under the music.
    private static float[] SynthTap()
    {
        float[] samples = new float[Seconds(0.09f)];
        System.Random noise = new System.Random(7);

        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float click = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t / 0.010f);
            float snap = ((float)noise.NextDouble() * 2f - 1f) * Mathf.Exp(-t / 0.004f);
            float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(420f, 260f, t / 0.09f) * t) * Mathf.Exp(-t / 0.028f);
            samples[i] = 0.45f * click + 0.3f * snap + 0.6f * body;
        }

        return samples;
    }

    // A clap: three quick noise slaps and a low hit, heavier than a tap so
    // pressing both sides at once sounds like one big accent.
    private static float[] SynthTwin()
    {
        float[] samples = new float[Seconds(0.18f)];
        System.Random noise = new System.Random(11);
        float previous = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float white = (float)noise.NextDouble() * 2f - 1f;

            // First difference tilts the noise bright, like a hand clap.
            float bright = white - previous;
            previous = white;

            float slaps = 0f;
            foreach (float at in new[] { 0f, 0.009f, 0.018f })
            {
                if (t >= at) slaps += Mathf.Exp(-(t - at) / 0.012f);
            }

            float low = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(200f, 120f, t / 0.18f) * t) * Mathf.Exp(-t / 0.05f);
            samples[i] = 0.5f * bright * slaps + 0.7f * low;
        }

        return samples;
    }

    // A short bright chime to close a hold - rewarding, and different enough
    // from a tap that the player hears the hold was completed.
    private static float[] SynthHoldEnd()
    {
        float[] samples = new float[Seconds(0.25f)];
        float[] tap = SynthTap();

        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)SampleRate;
            float chime = (Mathf.Sin(2f * Mathf.PI * 1318.5f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 1975.5f * t))
                          * Mathf.Exp(-t / 0.07f);
            samples[i] = 0.35f * chime + (i < tap.Length ? 0.6f * tap[i] : 0f);
        }

        return samples;
    }

    private static int Seconds(float seconds)
    {
        return Mathf.CeilToInt(seconds * SampleRate);
    }

    private static float[] Normalise(float[] samples, float peak)
    {
        float max = 0f;
        for (int i = 0; i < samples.Length; i++) max = Mathf.Max(max, Mathf.Abs(samples[i]));
        if (max <= 0f) return samples;

        float gain = peak / max;
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;

        // A few samples of fade-out so the clip never ends on a click.
        int fade = Mathf.Min(64, samples.Length);
        for (int i = 0; i < fade; i++) samples[samples.Length - 1 - i] *= i / (float)fade;

        return samples;
    }

    // 16-bit mono PCM.
    private static void WriteWav(string path, float[] samples)
    {
        using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(System.IO.File.Create(path)))
        {
            int dataBytes = samples.Length * 2;

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
            }
        }
    }

    // Unity's "Best performance" DSP buffer (1024 samples) adds ~20 ms before
    // any sound is heard - enough to make hit sounds feel detached from the
    // press. "Good latency" (512) halves it and is safe on phones; "Best
    // latency" (256) can crackle on low-end Android, so it is not forced.
    private static void LowerAudioLatency()
    {
        Object[] managers = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
        if (managers.Length == 0) return;

        SerializedObject audio = new SerializedObject(managers[0]);
        SerializedProperty buffer = audio.FindProperty("m_DSPBufferSize");
        if (buffer == null || (buffer.intValue != 0 && buffer.intValue <= 512)) return;

        int previous = buffer.intValue;
        buffer.intValue = 512;

        // The Project Settings page reads the requested size; both must move
        // together or the setting snaps back on the next save.
        SerializedProperty requested = audio.FindProperty("m_RequestedDSPBufferSize");
        if (requested != null) requested.intValue = 512;

        audio.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        Debug.Log($"Audio DSP buffer {previous} -> 512 (Project Settings > Audio > DSP Buffer Size: Good latency), " +
                  "so hit sounds play closer to the press.");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
