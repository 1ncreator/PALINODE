using System;
using System.Collections.Generic;
using System.IO;
using Palinode.Core;
using UnityEditor;
using UnityEngine;

namespace Palinode.Editor
{
    /// <summary>
    /// Floor I part of the build: copies the art/audio deliveries into the project (only new or changed files),
    /// processes the art (ArtProcessor profile "Floor1"), sets import settings and names the physics layers.
    /// Missing audio listed in Floor1/Data/audio_manifest.json is simply picked up on the next build once the file
    /// appears in the delivery folder.
    /// </summary>
    public static class Floor1Builder
    {
        public const string Root = "Assets/_Game/Floor1";
        public const string ArtDir = Root + "/Art";
        public const string AudioDir = Root + "/Audio";
        public const string DataDir = Root + "/Data";
        public const string FloorJsonPath = DataDir + "/floor1.json";
        public const string ManifestPath = DataDir + "/audio_manifest.json";
        public const string TemplatesPath = DataDir + "/room_templates.json";

        private static readonly string[] AudioExt = { ".wav", ".mp3", ".ogg", ".aif", ".aiff" };

        public static bool IsAudioFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(AudioExt, ext) >= 0;
        }

        /// <summary>Painted room backgrounds are registered as they are (no background removal).</summary>
        public static bool IsBackground(string name) =>
            name.StartsWith("F1_room_", StringComparison.Ordinal) || name == "F1_boss_arena" || name == "F1_corridor";

        public static void EnsureFolders()
        {
            foreach (var d in new[] { Root, ArtDir, ArtProcessor.Floor1.OutDir, AudioDir, DataDir })
                RenderSetup.EnsureFolder(d);
        }

        // ------------------------------------------------------------------ sources

        /// <summary>Copies new/changed deliveries. Returns the number of files copied.</summary>
        public static int CopySources()
        {
            int n = 0;
            var art = JNode.Parse(File.ReadAllText(ArtProcessor.Floor1.ConfigPath));
            n += CopyDir(art.Str("sourceDir"), ArtDir, f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            var manifest = JNode.Parse(File.ReadAllText(ManifestPath));
            n += CopyDir(manifest.Str("sourceDir"), AudioDir, IsAudioFile);
            if (n > 0) Debug.Log($"[PALINODE] Floor I: copied {n} new/changed source files");
            return n;
        }

        private static int CopyDir(string src, string dst, Func<string, bool> filter)
        {
            if (string.IsNullOrEmpty(src) || !Directory.Exists(src))
            {
                Debug.LogWarning($"[PALINODE] Floor I source folder not found: {src} (using the files already in {dst})");
                return 0;
            }
            Directory.CreateDirectory(dst);
            int n = 0;
            foreach (var f in Directory.GetFiles(src))
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("_") || name.StartsWith(".") || !filter(f)) continue;
                string target = Path.Combine(dst, name);
                var si = new FileInfo(f);
                var ti = new FileInfo(target);
                if (ti.Exists && ti.Length == si.Length && ti.LastWriteTimeUtc >= si.LastWriteTimeUtc) continue;
                File.Copy(f, target, true);
                File.SetLastWriteTimeUtc(target, si.LastWriteTimeUtc);
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ art

        public static Dictionary<string, ArtProcessor.PartMeta> ProcessArt(bool force)
        {
            using (ArtProcessor.Use(ArtProcessor.Floor1)) return ArtProcessor.ProcessAll(force);
        }

        public static void ConfigureImports(Dictionary<string, ArtProcessor.PartMeta> meta)
        {
            using (ArtProcessor.Use(ArtProcessor.Floor1)) AssetImport.ConfigureSprites(meta);
            AssetImport.ConfigureAudio(AudioDir);
        }

        public static void CollectSprites(Dictionary<string, Sprite> into)
        {
            if (Directory.Exists(ArtDir))
                foreach (var f in Directory.GetFiles(ArtDir, "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!IsBackground(name)) continue;
                    var s = AssetDatabase.LoadAssetAtPath<Sprite>(f.Replace('\\', '/'));
                    if (s != null) into[name] = s;
                }
            string outDir = ArtProcessor.Floor1.OutDir;
            if (Directory.Exists(outDir))
                foreach (var f in Directory.GetFiles(outDir, "*.png"))
                {
                    var s = AssetDatabase.LoadAssetAtPath<Sprite>(f.Replace('\\', '/'));
                    if (s != null) into[Path.GetFileNameWithoutExtension(f)] = s;
                }
        }

        public static void CollectClips(List<NamedClip> into)
        {
            if (!Directory.Exists(AudioDir)) return;
            foreach (var f in Directory.GetFiles(AudioDir))
            {
                if (!IsAudioFile(f)) continue;
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(f.Replace('\\', '/'));
                if (c != null) into.Add(new NamedClip { name = Path.GetFileNameWithoutExtension(f), clip = c });
            }
        }

        public static List<NamedText> CollectTexts()
        {
            var list = new List<NamedText>();
            void Add(string name, string path)
            {
                var t = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (t != null) list.Add(new NamedText { name = name, text = t });
                else Debug.LogWarning("[PALINODE] Data file not found: " + path);
            }
            Add("floor1", FloorJsonPath);
            Add("floor1_audio", ManifestPath);
            Add("floor1_templates", TemplatesPath);
            return list;
        }

        // ------------------------------------------------------------------ physics layers

        /// <summary>Names the Floor I physics layers in the TagManager (Palinode.Floor.Layers holds the numbers).</summary>
        public static void NameLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null) return;
            bool changed = false;
            for (int i = 0; i < Palinode.Floor.Layers.Names.Length; i++)
            {
                var p = layers.GetArrayElementAtIndex(Palinode.Floor.Layers.First + i);
                if (p.stringValue == Palinode.Floor.Layers.Names[i]) continue;
                p.stringValue = Palinode.Floor.Layers.Names[i];
                changed = true;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
