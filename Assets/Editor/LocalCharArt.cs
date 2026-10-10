using System;
using System.Collections.Generic;
using System.IO;
using Data.Char;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace Arknights.EditorTools {
    /// <summary>
    /// 装本地干员美术 / Installs reference art for the operators on THIS machine only.
    ///
    /// 为什么不进仓库 / The art is another game's sprites, used while this one is built, and the
    /// repository is public - so it must never be committed. Everything lands under
    /// Assets/Arknights/Resources/LocalArt/, which is gitignored, and is reached through one untracked
    /// registry (CharArtOverrides) that CharMeta consults first. A clone without the folder falls back
    /// to the tracked placeholders, with no missing references anywhere. Same shape as the music tools
    /// (ImportMusic): the files stay out of git and a menu item puts them back.
    ///
    /// 装什么 / For each operator in Stands below, from one source root:
    ///   BattleFront set -> the rig on the rhythm stage, with its motion table (CharacterPresenter)
    ///   Building set    -> the rig on the Home screen                       (HomeStandby)
    ///   one picture     -> portrait and card, cut or trimmed                (CharSelect, Gacha, results)
    ///                      avatar, a face cut from it                       (CharSelect grid, Gacha tabs)
    ///
    /// 可以重复运行 / Safe to run again: anything already installed is kept, so a file or a motion table
    /// adjusted by hand is not overwritten. Remove Local Character Art is the explicit reset. An operator
    /// whose source files are missing is skipped and named; the others install anyway.
    ///
    /// The Spine importer builds the atlas, material and SkeletonData assets itself, and does it only
    /// if SpineSettings.asset names a default shader that exists.
    /// </summary>
    public static class LocalCharArt {
        private const string Root = "Assets/Arknights/Resources/LocalArt";
        private const string RegistryPath = Root + "/CharArtOverrides.asset";
        private const string SpineSettingsPath = "Assets/Editor/SpineSettings.asset";

        private const string SourcePref = "Arknights.LocalCharArt.SourceRoot";
        private const string DefaultSource = @"D:\rhythm-demo-main\_moved-out-of-StreamingAssets";

        private const int AvatarSize = 256;
        private const int PortraitMargin = 8;

        /// <summary>
        /// 一个干员的美术从哪来 / Where one operator's stand-in art comes from, and how its rig acts.
        ///
        /// 矩形都是目测的 / Every rectangle is x, y, width, height from the TOP LEFT of the picture,
        /// measured by eye. Written down rather than detected - one picture, one rectangle, and a heuristic
        /// would only hide it.
        /// </summary>
        private sealed class Stand {
            public string id;          // the CharMeta it stands in for
            public string folder;      // relative to the source root
            public string battle;      // BattleFront set name, as exported
            public string home;        // Building set name, as exported
            public string picture;     // the illustration, in the folder itself
            public RectInt portrait;   // the tall frame cut from the picture; empty = trim to what is drawn
            public RectInt face;       // the square the avatar is cut from
            public Action<OperatorMotion, Spine.SkeletonData> motion; // what differs from the defaults
        }

        // 谁替谁 / Who stands in for whom, matched by rarity in their own game: Conviction is the 4-star
        // there, as NOVA is here; Flametail and Lappland are both 6-stars there. Only the art changes -
        // names, stats and passives stay this game's, and the server checks those.
        private static readonly Stand[] Stands = {
            new Stand {
                id = "AMIYA", folder = "AMIYA/char_002_amiya",
                battle = "char_002_amiya", home = "build_char_002_amiya", picture = "char_002_amiya_1.png",
                // A clean standing figure on an empty canvas: trimmed, not cut.
                face = new RectInt(385, 10, 330, 330),
                motion = (m, rig) => {
                    m.twin = Range("Skill_2", 0f, 0.4f, 1.3f);
                    m.holdLoop = Range("Skill_2", 0.15f, 0.4f, 1f);
                    m.holdEnd = Range("Attack_End", 0f, 0f, 1.6f);
                }
            },
            new Stand {
                id = "NOVA", folder = "ART/159/char_159_peacok",
                battle = "char_159_peacok_game#1", home = "build_char_159_peacok_game#1", picture = "char_159_peacok_game#1.png",
                // Skin splash art with its whole scene: a tall frame round the figure keeps it large.
                portrait = new RectInt(682, 410, 816, 1390),
                face = new RectInt(889, 512, 320, 320),
                motion = (m, rig) => { m.holdLoop = Around(rig, "Attack", 1f); }
            },
            new Stand {
                id = "ECHO", folder = "ART/420/char_420_flamtl",
                battle = "char_420_flamtl_game#2", home = "build_char_420_flamtl_game#2", picture = "char_420_flamtl_game#2.png",
                portrait = new RectInt(796, 415, 708, 1215),
                face = new RectInt(961, 484, 320, 320),
                motion = (m, rig) => {
                    m.holdLoop = Range("Skill_2_Loop", 0f, 0f, 1f);
                    m.holdEnd = Range("Skill_2_End", 0f, 0f, 1f);
                }
            },
            new Stand {
                id = "PULSE", folder = "ART/140/char_140_whitew",
                battle = "char_140_whitew_boc#1", home = "build_char_140_whitew_boc#1", picture = "char_140_whitew_boc#1.png",
                portrait = new RectInt(572, 340, 876, 1460),
                face = new RectInt(863, 361, 320, 320),
                motion = (m, rig) => { m.holdLoop = Range("Combat", 0f, 0f, 1f); }
            },
        };

        // ------------------------------------------------------------------- menu

        [MenuItem("Arknights/Art/Install Placeholder Operator Art", false, 0)]
        public static void Install() {
            if (Application.isPlaying) {
                Debug.LogError("[LocalCharArt] Exit Play Mode first.");
                return;
            }

            if (!SpineShaderExists(out string shaderName)) {
                Debug.LogError($"[LocalCharArt] {SpineSettingsPath} names the default shader '{shaderName}', which " +
                               "does not exist. The Spine importer would fail on every atlas. Set defaultShader to " +
                               "'Spine/Skeleton'.");
                return;
            }

            string source = EditorPrefs.GetString(SourcePref, DefaultSource);
            var ready = new List<Stand>();
            var report = new List<string>();

            foreach (Stand stand in Stands) {
                string missing = MissingFiles(source, stand);
                if (missing.Length > 0) {
                    report.Add($"  {stand.id}: SKIPPED, missing{missing}");
                    continue;
                }

                ready.Add(stand);
            }

            // 先图后图集 / Images first, imported before any atlas text arrives. The Spine importer looks an
            // atlas page's texture up at the moment it meets the atlas; a PNG that has not been imported
            // yet is "missing", and the material it builds then has no texture.
            var copied = new Dictionary<Stand, int>();
            foreach (Stand stand in ready) {
                int n = 0;
                n += CopyIfMissing(SourceSet(source, stand, true) + ".png", SetPath(stand, true) + ".png");
                n += CopyIfMissing(SourceSet(source, stand, false) + ".png", SetPath(stand, false) + ".png");
                copied[stand] = n + CutSprites(Path.Combine(source, stand.folder, stand.picture), stand);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // 材质先建好 / The materials are made before the atlas text exists. Left to itself, the importer
            // builds a new material in the middle of its own post-process and assigns the page texture to
            // it there - which, under this Unity's asset pipeline, does not stick, and it then logs
            // "Material is missing texture" about its own work. Given a material that already exists, it
            // only has to assign the texture, and that does stick.
            foreach (Stand stand in ready) {
                ConfigureSprite(Dir(stand) + "/portrait.png");
                ConfigureSprite(Dir(stand) + "/avatar.png");
                foreach (bool battle in new[] { true, false }) {
                    ConfigureSpinePage(SetPath(stand, battle) + ".png");
                    PrepareMaterial(SetDir(stand, battle), LocalName(stand, battle), shaderName);
                }
            }

            foreach (Stand stand in ready) {
                foreach (bool battle in new[] { true, false }) {
                    string from = SourceSet(source, stand, battle);
                    string to = SetPath(stand, battle);
                    copied[stand] += CopyIfMissing(from + ".skel", to + ".skel.bytes");
                    copied[stand] += CopyAtlasIfMissing(from + ".atlas", to + ".atlas.txt", Exported(stand, battle), LocalName(stand, battle));
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // 登记表只开一次 / The registry is opened once for the whole run. Created and then looked up
            // again within one refresh, a new asset can come back null under this asset pipeline - each
            // operator would then make a fresh registry over the last, and only the final one would stay.
            CharArtOverrides registry = AssetDatabase.LoadAssetAtPath<CharArtOverrides>(RegistryPath);
            bool registryCreated = registry == null;
            if (registryCreated) registry = ScriptableObject.CreateInstance<CharArtOverrides>();

            foreach (Stand stand in ready) {
                SkeletonDataAsset battleRig = ImportSpine(SetDir(stand, true), LocalName(stand, true));
                SkeletonDataAsset homeRig = ImportSpine(SetDir(stand, false), LocalName(stand, false));

                // 补一次贴图 / The importer assigns a page texture only if it can load it at that instant, and
                // does not say so when it cannot - the material is then built bare. Assign it here, explicitly.
                EnsureTexture(battleRig, SetPath(stand, true) + ".png");
                EnsureTexture(homeRig, SetPath(stand, false) + ".png");

                // 材质必须带图 / A rig whose material has no texture draws nothing, and says nothing about it.
                if (!HasTexture(battleRig) || !HasTexture(homeRig)) {
                    report.Add($"  {stand.id}: FAILED, a Spine set did not import with its texture. Remove Local " +
                               "Character Art, then install again.");
                    continue;
                }

                OperatorMotion motion = MotionFor(stand, battleRig, out bool motionMade);
                Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(Dir(stand) + "/portrait.png");
                Sprite avatar = AssetDatabase.LoadAssetAtPath<Sprite>(Dir(stand) + "/avatar.png");
                Register(registry, stand.id, portrait, avatar, battleRig, motion, homeRig);

                string size = portrait != null ? $"{portrait.rect.width}x{portrait.rect.height}" : "MISSING";
                report.Add($"  {stand.id}: from {stand.folder} - {copied[stand]} file(s) written, motion table " +
                           $"{(motionMade ? "made" : "kept")}, portrait {size}");
            }

            if (registryCreated) AssetDatabase.CreateAsset(registry, RegistryPath);
            else EditorUtility.SetDirty(registry);

            AssetDatabase.SaveAssets();
            Debug.Log($"[LocalCharArt] Placeholder operator art under {Root}\n" +
                      $"  source root: {source}\n" + string.Join("\n", report) + "\n" +
                      $"  registry {(registryCreated ? "created" : "updated")}: {RegistryPath}\n" +
                      "  Nothing under LocalArt is tracked by git.");
        }

        [MenuItem("Arknights/Art/Choose Placeholder Art Source Folder...", false, 1)]
        public static void ChooseSource() {
            string current = EditorPrefs.GetString(SourcePref, DefaultSource);
            string start = Directory.Exists(current) ? current : "";
            string picked = EditorUtility.OpenFolderPanel("Folder that holds AMIYA/ and ART/", start, "");
            if (string.IsNullOrEmpty(picked)) return;

            EditorPrefs.SetString(SourcePref, picked);
            Debug.Log($"[LocalCharArt] Placeholder art source root is now {picked}");
        }

        // ------------------------------------------------------------------ paths

        private static string Dir(Stand stand) => Root + "/" + stand.id;

        private static string SetDir(Stand stand, bool battle) => Dir(stand) + (battle ? "/Battle" : "/Home");

        private static string Exported(Stand stand, bool battle) => battle ? stand.battle : stand.home;

        // '#' 不进资源名 / The exported names carry a '#', which the asset name does without.
        private static string LocalName(Stand stand, bool battle) => Exported(stand, battle).Replace('#', '_');

        private static string SetPath(Stand stand, bool battle) {
            string dir = SetDir(stand, battle);
            Directory.CreateDirectory(dir);
            return dir + "/" + LocalName(stand, battle);
        }

        private static string SourceSet(string source, Stand stand, bool battle) {
            string name = Exported(stand, battle);
            return Path.Combine(source, stand.folder, battle ? "BattleFront" : "Building", name, name);
        }

        private static string MissingFiles(string source, Stand stand) {
            string missing = "";
            foreach (bool battle in new[] { true, false }) {
                string set = SourceSet(source, stand, battle);
                foreach (string ext in new[] { ".skel", ".atlas", ".png" }) {
                    if (!File.Exists(set + ext)) missing += "\n    " + set + ext;
                }
            }

            string picture = Path.Combine(source, stand.folder, stand.picture);
            if (!File.Exists(picture)) missing += "\n    " + picture;
            return missing;
        }

        // ----------------------------------------------------------------- motion

        // 有就留着 / A table that exists is kept, hand edits and all; only a missing one is made.
        private static OperatorMotion MotionFor(Stand stand, SkeletonDataAsset rigAsset, out bool made) {
            string path = Dir(stand) + "/" + stand.id + "_Motion.asset";
            OperatorMotion motion = AssetDatabase.LoadAssetAtPath<OperatorMotion>(path);
            made = motion == null;
            if (!made) return motion;

            Spine.SkeletonData rig = rigAsset.GetSkeletonData(false);
            motion = ScriptableObject.CreateInstance<OperatorMotion>();
            Defaults(motion, rig);
            if (stand.motion != null) stand.motion(motion, rig);

            AssetDatabase.CreateAsset(motion, path);
            return motion;
        }

        // 方舟骨骼通用的表 / What fits any Arknights rig: the standard names, and the strike timed off the
        // rig's own OnAttack event, so it lands on the press whatever the animation's wind-up.
        private static void Defaults(OperatorMotion m, Spine.SkeletonData rig) {
            m.idle = Range("Idle", 0f, 0f, 1f);
            m.enter = Range("Start", 0f, 0f, 1f);
            m.tap = Around(rig, "Attack", 2.2f);

            string skill = rig.FindAnimation("Skill") != null ? "Skill" : "Skill_1";
            m.twin = Around(rig, skill, 1.6f);
            m.fever = Range(skill, 0f, 0f, 1.4f);

            // 没有 Stun 就留空 / No Stun, no miss clip: the stage shakes and reddens the rig instead.
            if (rig.FindAnimation("Stun") != null) m.miss = Range("Stun", 0f, 0.5f, 2f);

            m.fail = Range("Die", 0f, 0f, 1f);
            m.win = Range("Start", 0f, 0f, 1f);
        }

        private static OperatorMotion.Clip Range(string name, float from, float to, float speed) {
            return new OperatorMotion.Clip(from, to, speed, name);
        }

        // 击中前 0.18 秒起, 击中后 0.32 秒止 / From 0.18s before the animation's OnAttack to 0.32s after:
        // the arm is out about 80ms after the press at 2.2x, and the follow-through still reads. An
        // animation with no OnAttack plays whole.
        private static OperatorMotion.Clip Around(Spine.SkeletonData rig, string name, float speed) {
            Spine.Animation animation = rig.FindAnimation(name);
            if (animation == null) return Range(name, 0f, 0f, speed);

            foreach (Spine.Timeline timeline in animation.Timelines) {
                if (!(timeline is Spine.EventTimeline events)) continue;

                for (int i = 0; i < events.Frames.Length; i++) {
                    if (events.Events[i].Data.Name != "OnAttack") continue;

                    float hit = events.Frames[i];
                    return Range(name, Mathf.Max(0f, hit - 0.18f), Mathf.Min(animation.Duration, hit + 0.32f), speed);
                }
            }

            return Range(name, 0f, 0f, speed);
        }

        /// <summary>
        /// 回到占位美术 / Back to the tracked placeholders: deletes the whole LocalArt folder. The install
        /// above puts it back from the source folder.
        /// </summary>
        [MenuItem("Arknights/Art/Remove Local Character Art", false, 20)]
        public static void Remove() {
            if (Application.isPlaying) {
                Debug.LogError("[LocalCharArt] Exit Play Mode first.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Root)) {
                Debug.Log("[LocalCharArt] There is no local art to remove.");
                return;
            }

            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh();
            Debug.Log($"[LocalCharArt] Removed {Root}. Every operator is back on the tracked placeholder art.");
        }

        // ------------------------------------------------------------------ files

        private static int CopyIfMissing(string source, string assetPath) {
            if (File.Exists(assetPath)) return 0;

            File.Copy(source, assetPath);
            return 1;
        }

        // 图集里写着贴图页的文件名 / An atlas names its page file. When the asset name drops the export's
        // '#', the page line has to follow, or the importer looks for a texture that is not there.
        private static int CopyAtlasIfMissing(string source, string assetPath, string exportedName, string localName) {
            if (File.Exists(assetPath)) return 0;

            if (exportedName == localName) {
                File.Copy(source, assetPath);
            } else {
                string text = File.ReadAllText(source);
                File.WriteAllText(assetPath, text.Replace(exportedName + ".png", localName + ".png"));
            }

            return 1;
        }

        private static bool SpineShaderExists(out string shaderName) {
            shaderName = "";
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(SpineSettingsPath);
            if (settings == null) return true; // no settings asset: the importer uses its own default

            SerializedProperty property = new SerializedObject(settings).FindProperty("defaultShader");
            if (property == null) return true;

            shaderName = property.stringValue;
            return Shader.Find(shaderName) != null;
        }

        // 让 Spine 导入器生成材质和数据 / The Spine importer makes <name>_Atlas, _Material and _SkeletonData
        // itself once the three files sit together. The images go in first so the atlas finds its page.
        private static SkeletonDataAsset ImportSpine(string directory, string name) {
            string skeletonData = directory + "/" + name + "_SkeletonData.asset";

            SkeletonDataAsset existing = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonData);
            if (existing != null) return existing;

            AssetDatabase.ImportAsset(directory + "/" + name + ".png", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(directory + "/" + name + ".atlas.txt", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(directory + "/" + name + ".skel.bytes", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            return AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonData);
        }

        // 图集页要按 Spine 的要求导入 / An atlas page is a premultiplied-alpha texture, not a sprite. This
        // project's default import for a new texture is Sprite with Alpha Is Transparency on, which bleeds
        // colour into the fully transparent pixels - harmless for a sprite, but on a premultiplied page
        // those pixels would ADD light round every edge. Spine's own importer sets the right values, but
        // only for a texture it sees arrive together with its atlas; here the page is imported first.
        private static void ConfigureSpinePage(string assetPath) {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            bool already = importer.textureType == TextureImporterType.Default &&
                           !importer.alphaIsTransparency && !importer.mipmapEnabled &&
                           importer.wrapMode == TextureWrapMode.Clamp;
            if (already) return;

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        // 一页的图集, 材质叫 <名字>_Material / One page, so the importer calls the material <name>_Material
        // (it only adds the page name when there are several). Premultiplied alpha is the shader's default,
        // so nothing else needs setting.
        private static void PrepareMaterial(string directory, string name, string shaderName) {
            string path = directory + "/" + name + "_Material.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

            Shader shader = Shader.Find(shaderName);
            Texture2D page = AssetDatabase.LoadAssetAtPath<Texture2D>(directory + "/" + name + ".png");
            if (shader == null || page == null) return;

            Material material = new Material(shader);
            material.mainTexture = page;
            AssetDatabase.CreateAsset(material, path);
        }

        private static void EnsureTexture(SkeletonDataAsset rig, string pagePath) {
            if (rig == null || HasTexture(rig)) return;
            if (rig.atlasAssets == null || rig.atlasAssets.Length == 0 || rig.atlasAssets[0] == null) return;

            Texture2D page = AssetDatabase.LoadAssetAtPath<Texture2D>(pagePath);
            Material material = rig.atlasAssets[0].PrimaryMaterial;
            if (page == null || material == null) return;

            material.mainTexture = page;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static bool HasTexture(SkeletonDataAsset rig) {
            if (rig == null || rig.atlasAssets == null || rig.atlasAssets.Length == 0 || rig.atlasAssets[0] == null) return false;

            Material material = rig.atlasAssets[0].PrimaryMaterial;
            return material != null && material.mainTexture != null;
        }

        // ----------------------------------------------------------------- sprites

        // Writes whichever of portrait.png and avatar.png the operator does not have yet. Returns how many.
        private static int CutSprites(string source, Stand stand) {
            Directory.CreateDirectory(Dir(stand));
            string portraitPath = Dir(stand) + "/portrait.png";
            string avatarPath = Dir(stand) + "/avatar.png";
            bool portrait = !File.Exists(portraitPath);
            bool avatar = !File.Exists(avatarPath);
            if (!portrait && !avatar) return 0;

            Texture2D picture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try {
                if (!picture.LoadImage(File.ReadAllBytes(source), false)) {
                    Debug.LogError($"[LocalCharArt] {source} is not a readable image.");
                    return 0;
                }

                int w = picture.width;
                int h = picture.height;
                Color32[] pixels = picture.GetPixels32();

                if (portrait) {
                    RectInt crop;
                    if (stand.portrait.width > 0 && stand.portrait.height > 0) {
                        // 立绘带场景就裁一个竖框 / A splash with its scene gets a tall frame round the figure.
                        crop = FromTop(stand.portrait, w, h);
                    } else {
                        // 空白画布就裁到有像素的范围 / A figure on an empty canvas is trimmed to what is drawn:
                        // it is half the width of the canvas, and keeping the margin shows it half-size.
                        RectInt box = OpaqueBounds(pixels, w, h);
                        int x0 = Mathf.Max(0, box.xMin - PortraitMargin);
                        int y0 = Mathf.Max(0, box.yMin - PortraitMargin);
                        int x1 = Mathf.Min(w, box.xMax + PortraitMargin);
                        int y1 = Mathf.Min(h, box.yMax + PortraitMargin);
                        crop = new RectInt(x0, y0, x1 - x0, y1 - y0);
                    }

                    WritePng(portraitPath, Crop(pixels, w, crop), crop.width, crop.height);
                }

                if (avatar) {
                    RectInt face = FromTop(stand.face, w, h);
                    Color32[] cut = Crop(pixels, w, face);
                    WritePng(avatarPath, Resize(cut, face.width, face.height, AvatarSize, AvatarSize), AvatarSize, AvatarSize);
                }

                return (portrait ? 1 : 0) + (avatar ? 1 : 0);
            } finally {
                UnityEngine.Object.DestroyImmediate(picture);
            }
        }

        // 图片坐标原点在左下 / Texture rows count from the bottom, the measured rectangles from the top.
        // Clamped, so a rectangle drawn a little past the edge still cuts.
        private static RectInt FromTop(RectInt rect, int w, int h) {
            int x = Mathf.Clamp(rect.x, 0, w - 1);
            int y = Mathf.Clamp(h - rect.y - rect.height, 0, h - 1);
            int width = Mathf.Min(rect.width, w - x);
            int height = Mathf.Min(rect.height, h - y);
            return new RectInt(x, y, width, height);
        }

        private static RectInt OpaqueBounds(Color32[] pixels, int w, int h) {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++) {
                int row = y * w;
                for (int x = 0; x < w; x++) {
                    if (pixels[row + x].a == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return maxX < 0 ? new RectInt(0, 0, w, h) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static Color32[] Crop(Color32[] source, int sourceWidth, RectInt rect) {
            Color32[] result = new Color32[rect.width * rect.height];
            for (int y = 0; y < rect.height; y++) {
                Array.Copy(source, (rect.y + y) * sourceWidth + rect.x, result, y * rect.width, rect.width);
            }

            return result;
        }

        /// <summary>
        /// 先乘透明度再混合 / Bilinear resize on premultiplied colour. The picture's transparent pixels are
        /// black, so blending straight colour would pull a dark halo into every edge.
        /// </summary>
        private static Color32[] Resize(Color32[] source, int sw, int sh, int dw, int dh) {
            Color32[] result = new Color32[dw * dh];

            for (int y = 0; y < dh; y++) {
                float sy = (y + 0.5f) * sh / dh - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(sy), 0, sh - 1);
                int y1 = Mathf.Min(y0 + 1, sh - 1);
                float fy = Mathf.Clamp01(sy - y0);

                for (int x = 0; x < dw; x++) {
                    float sx = (x + 0.5f) * sw / dw - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), 0, sw - 1);
                    int x1 = Mathf.Min(x0 + 1, sw - 1);
                    float fx = Mathf.Clamp01(sx - x0);

                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    Tap(source[y0 * sw + x0], (1f - fx) * (1f - fy), ref r, ref g, ref b, ref a);
                    Tap(source[y0 * sw + x1], fx * (1f - fy), ref r, ref g, ref b, ref a);
                    Tap(source[y1 * sw + x0], (1f - fx) * fy, ref r, ref g, ref b, ref a);
                    Tap(source[y1 * sw + x1], fx * fy, ref r, ref g, ref b, ref a);

                    if (a > 0f) {
                        r /= a;
                        g /= a;
                        b /= a;
                    }

                    result[y * dw + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
                }
            }

            return result;
        }

        private static void Tap(Color32 c, float weight, ref float r, ref float g, ref float b, ref float a) {
            float alpha = c.a / 255f;
            r += c.r / 255f * alpha * weight;
            g += c.g / 255f * alpha * weight;
            b += c.b / 255f * alpha * weight;
            a += alpha * weight;
        }

        private static void WritePng(string assetPath, Color32[] pixels, int w, int h) {
            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            } finally {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // 和占位头像同样的导入设置 / The same import settings the placeholder portraits use: a single
        // sprite, colour bled under the transparent edge so filtering cannot draw a dark line, no mipmaps.
        //
        // 必须检查 Single / Single is checked on its own: this project's default import for a new texture is
        // already a Sprite, but Multiple with automatic slicing, which trims the sprite to its opaque pixels.
        // For the avatar that turns a 256x256 square into a 212x250 cut, and the name gets a "_0".
        private static void ConfigureSprite(string assetPath) {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            bool already = importer.textureType == TextureImporterType.Sprite &&
                           importer.spriteImportMode == SpriteImportMode.Single &&
                           importer.alphaIsTransparency && !importer.mipmapEnabled;
            if (already) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spritePixelsPerUnit = 100f;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- registry

        // 只补空的 / Fills what is empty and leaves anything set alone.
        private static void Register(CharArtOverrides registry, string charId, Sprite portrait, Sprite avatar,
                                     SkeletonDataAsset battle, OperatorMotion motion, SkeletonDataAsset home) {
            CharArtOverrides.Entry entry = registry.entries.Find(e => e != null && e.charId == charId);
            if (entry == null) {
                entry = new CharArtOverrides.Entry { charId = charId };
                registry.entries.Add(entry);
            }

            if (entry.portrait == null) entry.portrait = portrait;
            if (entry.card == null) entry.card = portrait;
            if (entry.avatar == null) entry.avatar = avatar;
            if (entry.battleRig == null) entry.battleRig = battle;
            if (entry.battleMotion == null) entry.battleMotion = motion;
            if (entry.homeRig == null) entry.homeRig = home;
        }
    }
}
