using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Palinode.Editor
{
    /// <summary>Import settings for prologue art (sprites) and audio (streaming loops vs. decompressed one-shots).</summary>
    public static class AssetImport
    {
        public const string AudioRoot = "Assets/_Game/Prologue/Audio";

        /// <summary>Long ambience/music loops → Streaming + Vorbis. Everything else → Decompress On Load.</summary>
        public static readonly HashSet<string> StreamingClips = new HashSet<string>
        {
            "MUS_prologue", "SFX_rain_window", "SFX_rain_street", "SFX_traffic_far", "SFX_archive_ambience", "SFX_clock_tick"
        };

        public static void ConfigureSprites(Dictionary<string, ArtProcessor.PartMeta> meta)
        {
            foreach (var path in Directory.GetFiles(ArtProcessor.ArtRoot, "*.png", SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;
                bool processed = p.StartsWith(ArtProcessor.OutDir);
                string name = Path.GetFileNameWithoutExtension(p);
                bool changed = false;

                void Set<T>(T current, T wanted, System.Action<T> apply)
                {
                    if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                    apply(wanted);
                    changed = true;
                }

                Set(imp.textureType, TextureImporterType.Sprite, v => imp.textureType = v);
                Set(imp.spriteImportMode, SpriteImportMode.Single, v => imp.spriteImportMode = v);
                Set(imp.spritePixelsPerUnit, 100f, v => imp.spritePixelsPerUnit = v);
                Set(imp.maxTextureSize, 4096, v => imp.maxTextureSize = v);
                Set(imp.textureCompression, TextureImporterCompression.CompressedHQ, v => imp.textureCompression = v);
                Set(imp.mipmapEnabled, false, v => imp.mipmapEnabled = v);
                Set(imp.alphaIsTransparency, processed, v => imp.alphaIsTransparency = v);
                Set(imp.wrapMode, TextureWrapMode.Clamp, v => imp.wrapMode = v);
                Set(imp.filterMode, FilterMode.Bilinear, v => imp.filterMode = v);
                Set(imp.npotScale, TextureImporterNPOTScale.None, v => imp.npotScale = v);

                var settings = new TextureImporterSettings();
                imp.ReadTextureSettings(settings);
                bool sChanged = false;
                if (settings.spriteMeshType != SpriteMeshType.FullRect) { settings.spriteMeshType = SpriteMeshType.FullRect; sChanged = true; }
                if (settings.spriteGenerateFallbackPhysicsShape) { settings.spriteGenerateFallbackPhysicsShape = false; sChanged = true; }
                Vector2 pivot = new Vector2(0.5f, 0.5f);
                if (processed && meta.TryGetValue(name, out var m)) pivot = m.Pivot;
                if (settings.spriteAlignment != (int)SpriteAlignment.Custom || (settings.spritePivot - pivot).sqrMagnitude > 1e-8f)
                {
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = pivot;
                    sChanged = true;
                }
                if (sChanged) { imp.SetTextureSettings(settings); changed = true; }
                if (changed) imp.SaveAndReimport();
            }
        }

        public static void ConfigureAudio() => ConfigureAudio(AudioRoot);

        public static void ConfigureAudio(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var path in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (path.EndsWith(".meta")) continue;
                string p = path.Replace('\\', '/');
                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) continue;
                string name = Path.GetFileNameWithoutExtension(p);
                bool stream = StreamingClips.Contains(name) || name.StartsWith("F1_amb_")
                              || (name.StartsWith("MUS_") && !name.StartsWith("MUS_sting") && name != "MUS_title_sting");
                var s = imp.defaultSampleSettings;
                var wanted = s;
                wanted.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                wanted.compressionFormat = AudioCompressionFormat.Vorbis;
                wanted.quality = stream ? 0.7f : 0.85f;
                wanted.preloadAudioData = !stream;
                bool changed = s.loadType != wanted.loadType || s.compressionFormat != wanted.compressionFormat
                               || Mathf.Abs(s.quality - wanted.quality) > 0.001f || s.preloadAudioData != wanted.preloadAudioData;
                if (imp.loadInBackground) { imp.loadInBackground = false; changed = true; }
                if (imp.forceToMono) { imp.forceToMono = false; changed = true; }
                if (!changed) continue;
                imp.defaultSampleSettings = wanted;
                imp.SaveAndReimport();
            }
        }
    }
}
