using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Palinode.Editor
{
    /// <summary>Imports TMP essentials (shaders/settings) and creates SDF font assets with Latin + Cyrillic glyphs.</summary>
    public static class FontBuilder
    {
        public const string FontsDir = "Assets/_Game/Fonts";
        public const string HandPath = FontsDir + "/Caveat/Caveat SDF.asset";
        public const string MonoPath = FontsDir + "/PTMono/PTMono SDF.asset";
        public const string TitlePath = FontsDir + "/CormorantGaramond/CormorantGaramond SDF.asset";

        public static string Charset
        {
            get
            {
                var sb = new StringBuilder();
                for (char c = (char)32; c < 127; c++) sb.Append(c);
                for (char c = 'А'; c <= 'я'; c++) sb.Append(c);
                sb.Append("ЁёÉéÈèÀà«»—–…№’‘“”•‹›·×°");
                return sb.ToString();
            }
        }

        public static bool EnsureTmpEssentials()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) return true;
            string pkg = null;
            foreach (var dir in Directory.GetDirectories("Library/PackageCache", "com.unity.ugui*"))
            {
                string p = Path.Combine(dir, "Package Resources", "TMP Essential Resources.unitypackage");
                if (File.Exists(p)) pkg = p;
            }
            if (pkg == null) throw new Exception("TMP Essential Resources.unitypackage not found in com.unity.ugui.");
            // AssetDatabase.ImportPackage is asynchronous in batch mode; unpack the (tar.gz) package ourselves instead.
            ExtractUnityPackage(pkg);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset");
        }

        /// <summary>Minimal .unitypackage reader: gzip'ed tar of {guid}/asset, {guid}/asset.meta, {guid}/pathname.</summary>
        public static void ExtractUnityPackage(string packagePath)
        {
            var entries = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, byte[]>>();
            using (var fs = File.OpenRead(packagePath))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                var data = ms.ToArray();
                int pos = 0;
                while (pos + 512 <= data.Length)
                {
                    string name = ReadString(data, pos, 100);
                    if (string.IsNullOrEmpty(name)) break;
                    string prefix = ReadString(data, pos + 345, 155);
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                    string sizeField = ReadString(data, pos + 124, 12).Trim();
                    long size = sizeField.Length > 0 ? Convert.ToInt64(sizeField, 8) : 0;
                    char type = (char)data[pos + 156];
                    pos += 512;
                    if (type == '0' || type == '\0')
                    {
                        var bytes = new byte[size];
                        Buffer.BlockCopy(data, pos, bytes, 0, (int)size);
                        name = name.TrimStart('.', '/');
                        int slash = name.IndexOf('/');
                        if (slash > 0)
                        {
                            string guid = name.Substring(0, slash), file = name.Substring(slash + 1);
                            if (!entries.TryGetValue(guid, out var e)) entries[guid] = e = new System.Collections.Generic.Dictionary<string, byte[]>();
                            e[file] = bytes;
                        }
                    }
                    pos += (int)((size + 511) / 512 * 512);
                }
            }
            foreach (var kv in entries)
            {
                if (!kv.Value.TryGetValue("pathname", out var pn)) continue;
                string path = Encoding.UTF8.GetString(pn).Split('\n')[0].Trim();
                if (!path.StartsWith("Assets/")) continue;
                if (kv.Value.TryGetValue("asset", out var asset))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, asset);
                }
                else Directory.CreateDirectory(path);
                if (kv.Value.TryGetValue("asset.meta", out var meta)) File.WriteAllBytes(path + ".meta", meta);
            }
        }

        private static string ReadString(byte[] data, int offset, int len)
        {
            int end = offset;
            while (end < offset + len && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        public const string MarckPath = FontsDir + "/MarckScript/MarckScript SDF.asset";
        public const string BadScriptPath = FontsDir + "/BadScript/BadScript SDF.asset";

        /// <summary>Named handwriting fonts selectable from prologue.json ("font" of a page layer / "handFont").</summary>
        public static System.Collections.Generic.List<Palinode.Core.NamedFont> Handwriting { get; private set; }
            = new System.Collections.Generic.List<Palinode.Core.NamedFont>();

        public static (TMP_FontAsset hand, TMP_FontAsset mono, TMP_FontAsset title) Run()
        {
            var marck = Create(FontsDir + "/MarckScript/MarckScript-Regular.ttf", MarckPath, 90);
            var bad = Create(FontsDir + "/BadScript/BadScript-Regular.ttf", BadScriptPath, 90);
            var hand = Create(FontsDir + "/Caveat/Caveat.ttf", HandPath, 90);
            Handwriting = new System.Collections.Generic.List<Palinode.Core.NamedFont>
            {
                new Palinode.Core.NamedFont { name = "hand_marck", font = marck },
                new Palinode.Core.NamedFont { name = "hand_bad", font = bad },
                new Palinode.Core.NamedFont { name = "hand_caveat", font = hand },
            };
            var mono = Create(FontsDir + "/PTMono/PTMono-Regular.ttf", MonoPath, 72);
            var title = Create(FontsDir + "/CormorantGaramond/CormorantGaramond.ttf", TitlePath, 90);

            // Make our fonts fallbacks of the TMP default font, so any stray text still renders Cyrillic.
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var fb = so.FindProperty("m_fallbackFontAssets");
                if (fb != null)
                {
                    fb.arraySize = 3;
                    fb.GetArrayElementAtIndex(0).objectReferenceValue = title;
                    fb.GetArrayElementAtIndex(1).objectReferenceValue = mono;
                    fb.GetArrayElementAtIndex(2).objectReferenceValue = hand;
                }
                var def = so.FindProperty("m_defaultFontAsset");
                if (def != null) def.objectReferenceValue = title;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            return (hand, mono, title);
        }

        private static TMP_FontAsset Create(string ttfPath, string assetPath, int pointSize)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            // Dynamic font data is cleared by TMP when assets are saved; glyphs are regenerated from the source font on demand.
            if (existing != null && existing.atlasTextures != null && existing.atlasTextures.Length > 0 && existing.atlasTextures[0] != null
                && existing.sourceFontFile != null)
                return existing;
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) throw new Exception("Font not found: " + ttfPath);
            var fa = TMP_FontAsset.CreateFontAsset(font, pointSize, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            if (fa == null) throw new Exception("TMP could not create font asset for " + ttfPath);
            fa.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fa, assetPath);
            var tex = fa.atlasTextures[0];
            tex.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(tex, fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);

            fa.TryAddCharacters(Charset, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning($"[PALINODE] {fa.name}: missing glyphs '{missing}'");
            EditorUtility.SetDirty(fa);
            EditorUtility.SetDirty(tex);
            AssetDatabase.SaveAssets();
            return fa;
        }
    }
}
