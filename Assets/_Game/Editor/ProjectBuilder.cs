using System;
using System.Collections.Generic;
using System.IO;
using Palinode.Core;
using Palinode.Localization;
using UnityEditor;
using UnityEngine;

namespace Palinode.Editor
{
    /// <summary>
    /// One-click, idempotent project build: TMP essentials, render pipeline, art processing, import settings,
    /// fonts, runtime config registry, scenes, build & player settings, data validation.
    /// Batch mode: -executeMethod Palinode.Editor.ProjectBuilder.BuildEverything (exit code 0 = success, 1 = failure).
    /// </summary>
    public static class ProjectBuilder
    {
        public const string DataDir = "Assets/_Game/Prologue/Data";
        public const string ProloguePath = DataDir + "/prologue.json";
        public const string LocalizationPath = DataDir + "/localization.csv";
        public const string ConfigPath = "Assets/_Game/Resources/" + GameConfig.ResourcePath + ".asset";

        [MenuItem("Tools/PALINODE/Build Everything")]
        public static void BuildEverythingMenu() => Run(false, false);

        [MenuItem("Tools/PALINODE/Rebuild Processed Art (force)")]
        public static void ForceArt() => Run(true, false);

        public static void BuildEverything() => Run(false, Application.isBatchMode);

        public static void BuildEverythingForce() => Run(true, Application.isBatchMode);

        private static void Run(bool forceArt, bool exit)
        {
            int code = 0;
            try
            {
                Step("Folders", () =>
                {
                    foreach (var d in new[] { "Assets/_Game/Resources", "Assets/_Game/Settings", DataDir, ArtProcessor.OutDir, "Assets/_Game/Tests" })
                        RenderSetup.EnsureFolder(d);
                    Floor1Builder.EnsureFolders();
                });
                Step("Floor I sources", () =>
                {
                    if (Floor1Builder.CopySources() > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                });
                Step("Physics layers", Floor1Builder.NameLayers);
                Step("TMP essentials", () =>
                {
                    if (!FontBuilder.EnsureTmpEssentials()) throw new Exception("TMP Essential Resources were not imported.");
                });
                Dictionary<string, Material> mats = null;
                Step("Render pipeline", () => mats = RenderSetup.Run());
                Dictionary<string, ArtProcessor.PartMeta> meta = null, floorMeta = null;
                Step("Art processing", () =>
                {
                    meta = ArtProcessor.ProcessAll(forceArt);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                });
                Step("Floor I art processing", () =>
                {
                    floorMeta = Floor1Builder.ProcessArt(forceArt);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                });
                Step("Audio processing", () =>
                {
                    AudioTools.Process();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                });
                Step("Import settings", () =>
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        AssetImport.ConfigureSprites(meta);
                        AssetImport.ConfigureAudio();
                        Floor1Builder.ConfigureImports(floorMeta);
                    }
                    finally { AssetDatabase.StopAssetEditing(); }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                });
                Step("Audio measurement", () =>
                {
                    var levels = AudioTools.Measure();
                    AssetDatabase.ImportAsset(AudioTools.LevelsPath, ImportAssetOptions.ForceSynchronousImport);
                    Debug.Log($"[PALINODE] Audio levels measured for {levels.Count} clips");
                });
                (TMPro.TMP_FontAsset hand, TMPro.TMP_FontAsset mono, TMPro.TMP_FontAsset title) fonts = default;
                Step("Fonts", () => fonts = FontBuilder.Run());
                GameConfig config = null;
                Step("Game config", () => config = BuildConfig(mats, fonts.hand, fonts.mono, fonts.title));
                Step("Scenes", () => SceneBuilder.BuildAll(RenderSetup.Profile));
                Step("Player settings", ConfigurePlayer);
                Step("Validation", () =>
                {
                    var data = JNode.Parse(File.ReadAllText(ProloguePath));
                    var table = LocalizationTable.FromCsv(File.ReadAllText(LocalizationPath));
                    // Building scenes unloads unused assets: reload the registry instead of using a destroyed reference.
                    if (config == null) config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
                    var errors = PrologueValidator.Validate(data, config, table);
                    var refs = PrologueValidator.Collect(data);
                    foreach (var s in refs.Scenes)
                        if (!File.Exists(SceneBuilder.PathOf(s))) errors.Add("Scene not built: " + s);
                    errors.AddRange(Palinode.Floor.FloorValidator.Validate(config, table));
                    if (errors.Count > 0) throw new Exception("Data validation failed:\n  " + string.Join("\n  ", errors));
                });
                AssetDatabase.SaveAssets();
                Debug.Log("[PALINODE] BUILD EVERYTHING: SUCCESS");
            }
            catch (Exception e)
            {
                code = 1;
                Debug.LogError("[PALINODE] BUILD EVERYTHING: FAILED\n" + e);
            }
            if (exit) EditorApplication.Exit(code);
        }

        private static void Step(string name, Action a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[PALINODE] >> {name}");
            a();
            Debug.Log($"[PALINODE] << {name} ({sw.ElapsedMilliseconds} ms)");
        }

        private static GameConfig BuildConfig(Dictionary<string, Material> mats, TMPro.TMP_FontAsset hand, TMPro.TMP_FontAsset mono, TMPro.TMP_FontAsset title)
        {
            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var dir in new[] { "Scenes", "Items", "Game" })
            {
                string d = ArtProcessor.ArtRoot + "/" + dir;
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*.png"))
                {
                    var s = AssetDatabase.LoadAssetAtPath<Sprite>(f.Replace('\\', '/'));
                    if (s != null) sprites[Path.GetFileNameWithoutExtension(f)] = s;
                }
            }
            // Processed art overrides originals with the same name.
            foreach (var f in Directory.GetFiles(ArtProcessor.OutDir, "*.png"))
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(f.Replace('\\', '/'));
                if (s != null) sprites[Path.GetFileNameWithoutExtension(f)] = s;
            }
            Floor1Builder.CollectSprites(sprites);
            var spriteList = new List<NamedSprite>();
            var names = new List<string>(sprites.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var n in names) spriteList.Add(new NamedSprite { name = n, sprite = sprites[n] });

            var clips = new List<NamedClip>();
            foreach (var f in Directory.GetFiles(AssetImport.AudioRoot + "/SFX"))
            {
                if (f.EndsWith(".meta")) continue;
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(f.Replace('\\', '/'));
                if (c != null) clips.Add(new NamedClip { name = Path.GetFileNameWithoutExtension(f), clip = c });
            }
            if (Directory.Exists(AudioTools.ProcessedDir))
                foreach (var f in Directory.GetFiles(AudioTools.ProcessedDir, "*.wav"))
                {
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>(f.Replace('\\', '/'));
                    if (c != null) clips.Add(new NamedClip { name = Path.GetFileNameWithoutExtension(f), clip = c });
                }
            foreach (var lang in new[] { "en", "ru" })
            {
                string d = AssetImport.AudioRoot + "/Voice/" + lang;
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d))
                {
                    if (f.EndsWith(".meta")) continue;
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>(f.Replace('\\', '/'));
                    if (c != null) clips.Add(new NamedClip { name = lang + "/" + Path.GetFileNameWithoutExtension(f), clip = c });
                }
            }

            Floor1Builder.CollectClips(clips);

            var matList = new List<NamedMaterial>();
            foreach (var kv in mats) matList.Add(new NamedMaterial { name = kv.Key, material = kv.Value });

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.EditorAssign(AssetDatabase.LoadAssetAtPath<TextAsset>(ProloguePath), AssetDatabase.LoadAssetAtPath<TextAsset>(LocalizationPath),
                spriteList, clips, matList, hand, mono, title);
            config.EditorAssignExtra(AssetDatabase.LoadAssetAtPath<TextAsset>(AudioTools.LevelsPath), FontBuilder.Handwriting,
                AssetDatabase.LoadAssetAtPath<TextAsset>(ArtProcessor.MetaPath));
            config.EditorAssignTexts(Floor1Builder.CollectTexts());
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.productName = "PALINODE";
            PlayerSettings.companyName = "Palinode";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.visibleInBackground = true;
        }
    }
}
