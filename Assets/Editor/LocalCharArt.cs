using System;
using System.IO;
using Data.Char;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace Arknights.EditorTools {
    /// <summary>
    /// 装本地干员美术 / Installs reference art for an operator on THIS machine only.
    ///
    /// 为什么不进仓库 / The art is another game's sprites, used while this one is built, and the
    /// repository is public - so it must never be committed. Everything lands under
    /// Assets/Arknights/Resources/LocalArt/, which is gitignored, and is reached through one untracked
    /// registry (CharArtOverrides) that CharMeta consults first. A clone without the folder falls back
    /// to the tracked placeholders, with no missing references anywhere. Same shape as the music tools
    /// (ImportMusic): the files stay out of git and a menu item puts them back.
    ///
    /// 装什么 / For AMIYA, from one source folder:
    ///   BattleFront default  -> the rig on the rhythm stage      (CharacterPresenter)
    ///   Building default     -> the rig on the Home screen       (HomeStandby)
    ///   char_002_amiya_1.png -> portrait and card, trimmed       (CharSelect, Gacha, results)
    ///                           avatar, a face cut from it       (CharSelect grid, Gacha tabs)
    ///
    /// 可以重复运行 / Safe to run again: anything already installed is kept, so a file adjusted by hand
    /// is not overwritten. Remove Local Character Art is the explicit reset.
    ///
    /// The Spine importer builds the atlas, material and SkeletonData assets itself, and does it only
    /// if SpineSettings.asset names a default shader that exists.
    /// </summary>
    public static class LocalCharArt {
        private const string Root = "Assets/Arknights/Resources/LocalArt";
        private const string AmiyaRoot = Root + "/AMIYA";
        private const string RegistryPath = Root + "/CharArtOverrides.asset";
        private const string SpineSettingsPath = "Assets/Editor/SpineSettings.asset";

        private const string SourcePref = "Arknights.LocalCharArt.AmiyaSource";
        private const string DefaultSource = @"D:\rhythm-demo-main\_moved-out-of-StreamingAssets\AMIYA\char_002_amiya";

        private const string BattleName = "char_002_amiya";
        private const string HomeName = "build_char_002_amiya";

        // 脸的位置 / Where the face is in char_002_amiya_1.png, measured by eye: x, y, width, height from the
        // TOP LEFT of the 1024x1024 picture. The ears start at y=19 and the chin is near y=310. Written
        // down rather than detected - one picture, one rectangle, and a heuristic would only hide it.
        private static readonly RectInt AvatarCrop = new RectInt(385, 10, 330, 330);
        private const int AvatarSize = 256;
        private const int PortraitMargin = 8;

        [MenuItem("Arknights/Art/Install AMIYA Placeholder Art", false, 0)]
        public static void InstallAmiya() {
            if (Application.isPlaying) {
                Debug.LogError("[LocalCharArt] Exit Play Mode first.");
                return;
            }

            string source = EditorPrefs.GetString(SourcePref, DefaultSource);
            string battleSource = Path.Combine(source, "BattleFront", BattleName, BattleName);
            string homeSource = Path.Combine(source, "Building", HomeName, HomeName);
            string portraitSource = Path.Combine(source, "char_002_amiya_1.png");

            string missing = "";
            foreach (string file in new[] {
                         battleSource + ".skel", battleSource + ".atlas", battleSource + ".png",
                         homeSource + ".skel", homeSource + ".atlas", homeSource + ".png", portraitSource }) {
                if (!File.Exists(file)) missing += "\n  " + file;
            }

            if (missing.Length > 0) {
                Debug.LogError("[LocalCharArt] The source folder is missing files:" + missing +
                               "\nPoint the tool at the right folder with Arknights/Art/Choose AMIYA Source Folder...");
                return;
            }

            if (!SpineShaderExists(out string shaderName)) {
                Debug.LogError($"[LocalCharArt] {SpineSettingsPath} names the default shader '{shaderName}', which " +
                               "does not exist. The Spine importer would fail on every atlas. Set defaultShader to " +
                               "'Spine/Skeleton'.");
                return;
            }

            string battleDir = AmiyaRoot + "/Battle";
            string homeDir = AmiyaRoot + "/Home";
            Directory.CreateDirectory(battleDir);
            Directory.CreateDirectory(homeDir);

            // 先图后图集 / Images first, imported before the atlas text arrives. The Spine importer looks an
            // atlas page's texture up at the moment it meets the atlas; a PNG that has not been imported
            // yet is "missing", and the material it builds then has no texture.
            int copied = 0;
            copied += CopyIfMissing(battleSource + ".png", battleDir + "/" + BattleName + ".png");
            copied += CopyIfMissing(homeSource + ".png", homeDir + "/" + HomeName + ".png");

            string portraitPath = AmiyaRoot + "/portrait.png";
            string avatarPath = AmiyaRoot + "/avatar.png";
            bool madePortrait = !File.Exists(portraitPath);
            bool madeAvatar = !File.Exists(avatarPath);
            if (madePortrait || madeAvatar) CutSprites(portraitSource, portraitPath, avatarPath, madePortrait, madeAvatar);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ConfigureSprite(portraitPath);
            ConfigureSprite(avatarPath);
            ConfigureSpinePage(battleDir + "/" + BattleName + ".png");
            ConfigureSpinePage(homeDir + "/" + HomeName + ".png");

            // 材质先建好 / The materials are made before the atlas text exists. Left to itself, the importer
            // builds a new material in the middle of its own post-process and assigns the page texture to
            // it there - which, under this Unity's asset pipeline, does not stick, and it then logs
            // "Material is missing texture" about its own work. Given a material that already exists, it
            // only has to assign the texture, and that does stick.
            PrepareMaterial(battleDir, BattleName, shaderName);
            PrepareMaterial(homeDir, HomeName, shaderName);

            copied += CopyIfMissing(battleSource + ".skel", battleDir + "/" + BattleName + ".skel.bytes");
            copied += CopyIfMissing(battleSource + ".atlas", battleDir + "/" + BattleName + ".atlas.txt");
            copied += CopyIfMissing(homeSource + ".skel", homeDir + "/" + HomeName + ".skel.bytes");
            copied += CopyIfMissing(homeSource + ".atlas", homeDir + "/" + HomeName + ".atlas.txt");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            SkeletonDataAsset battle = ImportSpine(battleDir, BattleName);
            SkeletonDataAsset home = ImportSpine(homeDir, HomeName);

            if (battle == null || home == null) {
                Debug.LogError("[LocalCharArt] The Spine importer did not produce SkeletonData for " +
                               (battle == null ? BattleName : "") + (battle == null && home == null ? " and " : "") +
                               (home == null ? HomeName : "") + ". Check the Console for the importer's own error.");
                return;
            }

            // 补一次贴图 / The importer assigns a page texture only if it can load it at that instant, and
            // does not say so when it cannot - the material is then built bare. Assign it here, explicitly.
            EnsureTexture(battle, battleDir + "/" + BattleName + ".png");
            EnsureTexture(home, homeDir + "/" + HomeName + ".png");

            // 材质必须带图 / A rig whose material has no texture draws nothing, and says nothing about it.
            if (!HasTexture(battle) || !HasTexture(home)) {
                Debug.LogError("[LocalCharArt] A Spine material was built without its texture. Run " +
                               "Arknights/Art/Remove Local Character Art, then install again.");
                return;
            }

            Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(portraitPath);
            Sprite avatar = AssetDatabase.LoadAssetAtPath<Sprite>(avatarPath);

            bool registryCreated = Register("AMIYA", portrait, avatar, battle, home);

            AssetDatabase.SaveAssets();
            Debug.Log($"[LocalCharArt] AMIYA installed under {AmiyaRoot}\n" +
                      $"  source: {source}\n" +
                      $"  files copied: {copied}, portrait {(madePortrait ? "cut" : "kept")}, avatar {(madeAvatar ? "cut" : "kept")}\n" +
                      $"  battle rig: {AssetDatabase.GetAssetPath(battle)}\n" +
                      $"  home rig:   {AssetDatabase.GetAssetPath(home)}\n" +
                      $"  registry {(registryCreated ? "created" : "updated")}: {RegistryPath}\n" +
                      "  Nothing under LocalArt is tracked by git.");
        }

        [MenuItem("Arknights/Art/Choose AMIYA Source Folder...", false, 1)]
        public static void ChooseSource() {
            string current = EditorPrefs.GetString(SourcePref, DefaultSource);
            string start = Directory.Exists(current) ? current : "";
            string picked = EditorUtility.OpenFolderPanel("Folder that holds BattleFront, Building and char_002_amiya_1.png", start, "");
            if (string.IsNullOrEmpty(picked)) return;

            EditorPrefs.SetString(SourcePref, picked);
            Debug.Log($"[LocalCharArt] AMIYA source folder is now {picked}");
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

        private static void CutSprites(string source, string portraitPath, string avatarPath, bool portrait, bool avatar) {
            Texture2D picture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try {
                if (!picture.LoadImage(File.ReadAllBytes(source), false)) {
                    Debug.LogError($"[LocalCharArt] {source} is not a readable image.");
                    return;
                }

                int w = picture.width;
                int h = picture.height;
                Color32[] pixels = picture.GetPixels32();

                if (portrait) {
                    // 裁到有像素的范围 / Trimmed to what is drawn: the standing figure is half the width of its
                    // canvas, and a portrait that keeps the empty margin shows up half-size.
                    RectInt box = OpaqueBounds(pixels, w, h);
                    int x0 = Mathf.Max(0, box.xMin - PortraitMargin);
                    int y0 = Mathf.Max(0, box.yMin - PortraitMargin);
                    int x1 = Mathf.Min(w, box.xMax + PortraitMargin);
                    int y1 = Mathf.Min(h, box.yMax + PortraitMargin);

                    RectInt crop = new RectInt(x0, y0, x1 - x0, y1 - y0);
                    WritePng(portraitPath, Crop(pixels, w, crop), crop.width, crop.height);
                }

                if (avatar) {
                    // 图片坐标原点在左下 / Texture rows count from the bottom, the measured rectangle from the top.
                    RectInt face = new RectInt(AvatarCrop.x, h - AvatarCrop.y - AvatarCrop.height, AvatarCrop.width, AvatarCrop.height);
                    Color32[] cut = Crop(pixels, w, face);
                    WritePng(avatarPath, Resize(cut, face.width, face.height, AvatarSize, AvatarSize), AvatarSize, AvatarSize);
                }
            } finally {
                UnityEngine.Object.DestroyImmediate(picture);
            }
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
        private static bool Register(string charId, Sprite portrait, Sprite avatar, SkeletonDataAsset battle, SkeletonDataAsset home) {
            CharArtOverrides registry = AssetDatabase.LoadAssetAtPath<CharArtOverrides>(RegistryPath);
            bool created = registry == null;
            if (created) {
                registry = ScriptableObject.CreateInstance<CharArtOverrides>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }

            CharArtOverrides.Entry entry = registry.entries.Find(e => e != null && e.charId == charId);
            if (entry == null) {
                entry = new CharArtOverrides.Entry { charId = charId };
                registry.entries.Add(entry);
            }

            if (entry.portrait == null) entry.portrait = portrait;
            if (entry.card == null) entry.card = portrait;
            if (entry.avatar == null) entry.avatar = avatar;
            if (entry.battleRig == null) entry.battleRig = battle;
            if (entry.homeRig == null) entry.homeRig = home;

            EditorUtility.SetDirty(registry);
            return created;
        }
    }
}
