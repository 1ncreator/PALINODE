using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Palinode.Core;
using UnityEditor;
using UnityEngine;

namespace Palinode.Editor
{
    /// <summary>
    /// Turns illustrations painted on a flat light-grey backdrop into transparent PNGs and slices sheets with
    /// several objects into separate sprites. Driven by Prologue/Data/art_processing.json; idempotent (hash cache).
    ///
    /// Algorithm per image:
    ///  1. Estimate the backdrop colour from neutral border pixels.
    ///  2. Flood-fill from the border through "backdrop-like" pixels (neutral, within a luminance band that also
    ///     admits darker cast shadows); enclosed backdrop pockets (e.g. clock-hand rings) are detected as holes.
    ///  3. Cast shadows become black with alpha proportional to how much darker they are than the local backdrop.
    ///  4. Object edges get a 2px feather; their colour is taken from 3px inside so no grey halo remains.
    ///  5. Optional: connected components → separate parts (pivot rules, hands rotated upright around the ring).
    /// </summary>
    public static class ArtProcessor
    {
        /// <summary>One art set: sources, processed output and its recipe file (prologue, Floor I, …).</summary>
        public sealed class Profile
        {
            public readonly string ArtRoot, OutDir, ConfigPath;
            public Profile(string artRoot, string outDir, string configPath) { ArtRoot = artRoot; OutDir = outDir; ConfigPath = configPath; }
        }

        public static readonly Profile Prologue = new Profile("Assets/_Game/Prologue/Art", "Assets/_Game/Prologue/Art/Processed",
            "Assets/_Game/Prologue/Data/art_processing.json");
        public static readonly Profile Floor1 = new Profile("Assets/_Game/Floor1/Art", "Assets/_Game/Floor1/Art/Processed",
            "Assets/_Game/Floor1/Data/art_processing.json");

        private static Profile _active = Prologue;

        /// <summary>Profile the static paths below refer to (the prologue unless switched with <see cref="Use"/>).</summary>
        public static Profile Active => _active;
        public static string ArtRoot => _active.ArtRoot;
        public static string OutDir => _active.OutDir;
        public static string ConfigPath => _active.ConfigPath;
        public static string MetaPath => OutDir + "/processed_meta.json";
        private static string CachePath => OutDir + "/.cache.json";
        private const int Version = 11;

        /// <summary>Switch the active profile for a block: <c>using (ArtProcessor.Use(ArtProcessor.Floor1)) { … }</c>.</summary>
        public static IDisposable Use(Profile profile)
        {
            var prev = _active;
            _active = profile;
            return new Restore(() => _active = prev);
        }

        private sealed class Restore : IDisposable
        {
            private Action _a;
            public Restore(Action a) => _a = a;
            public void Dispose() { _a?.Invoke(); _a = null; }
        }

        public sealed class PartMeta
        {
            public string Name;
            public Vector2 Pivot;      // normalized, bottom-left origin
            public RectInt Crop;       // in source pixels (top-left origin)
            public float Rotation;     // degrees applied (hands)
            public Vector2 Tip = new Vector2(float.NaN, float.NaN);   // tool tip, final image px (top-left origin)
            public Vector2 Elbow = new Vector2(float.NaN, float.NaN); // where the limb leaves the original picture
        }

        /// <summary>Centroid of opaque pixels on one edge of the image (where a sleeve leaves the frame).</summary>
        private static Vector2 EdgeCentroid(Color32[] px, int w, int h, string side)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            if (side == "right" || side == "left")
            {
                int x = side == "right" ? w - 2 : 1;
                for (int y = 0; y < h; y++) if (px[x + y * w].a > 128) { sum += new Vector2(x, y); n++; }
            }
            else
            {
                int y = side == "bottom" ? h - 2 : 1;
                for (int x = 0; x < w; x++) if (px[x + y * w].a > 128) { sum += new Vector2(x, y); n++; }
            }
            return n > 0 ? sum / n : new Vector2(float.NaN, float.NaN);
        }

        /// <summary>Centroid of opaque pixels on the right column and bottom row (where a sleeve leaves the picture).</summary>
        private static Vector2 ExitCentroid(Color32[] px, int w, int h)
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int y = 0; y < h; y++) if (px[(w - 2) + y * w].a > 128) { sum += new Vector2(w - 2, y); n++; }
            for (int x = 0; x < w; x++) if (px[x + (h - 2) * w].a > 128) { sum += new Vector2(x, h - 2); n++; }
            return n > 0 ? sum / n : new Vector2(float.NaN, float.NaN);
        }

        /// <summary>
        /// Grow the canvas right/bottom and continue whatever crosses those edges along direction
        /// <paramref name="d"/> (both components ≥ 0), mirror-tiling the texture with period <paramref name="period"/>.
        /// </summary>
        private static Color32[] ExtendAlong(Color32[] px, ref int w, ref int h, int addX, int addY, Vector2 d, int period)
        {
            int nw = w + Math.Max(0, addX), nh = h + Math.Max(0, addY);
            var o = new Color32[nw * nh];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, o, y * nw, w);
            d = new Vector2(Mathf.Max(1e-4f, d.x), Mathf.Max(1e-4f, d.y));
            for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                if (x < w && y < h) continue;
                float tx = x >= w ? (x - (w - 1)) / d.x : 0f;
                float ty = y >= h ? (y - (h - 1)) / d.y : 0f;
                float t = Mathf.Max(tx, ty);
                Vector2 q = new Vector2(x, y) - d * t;
                int qx = Mathf.RoundToInt(q.x), qy = Mathf.RoundToInt(q.y);
                if (qx < 0 || qy < 0 || qx >= w || qy >= h) continue;
                if (px[qx + qy * w].a < 8) continue;
                float k = t % (2f * period);
                float m = k < period ? k : 2f * period - k;
                Vector2 s = q - d * m;
                int sx = Mathf.Clamp(Mathf.RoundToInt(s.x), 0, w - 1), sy = Mathf.Clamp(Mathf.RoundToInt(s.y), 0, h - 1);
                var c = px[sx + sy * w];
                if (c.a < 128) c = px[qx + qy * w];
                o[x + y * nw] = c;
            }
            w = nw;
            h = nh;
            return o;
        }

        /// <summary>
        /// Grow the canvas on one side and continue whatever touches that edge by mirrored tiling of the last
        /// <paramref name="period"/> pixels (keeps knit/cloth texture continuous). Only right/bottom are supported.
        /// </summary>
        private static Color32[] Extend(Color32[] px, ref int w, ref int h, string side, int add, int period)
        {
            if (side == "right")
            {
                int nw = w + add;
                var o = new Color32[nw * h];
                for (int y = 0; y < h; y++)
                {
                    Array.Copy(px, y * w, o, y * nw, w);
                    if (px[(w - 2) + y * w].a < 8) continue;
                    for (int x = 0; x < add; x++)
                    {
                        int k = x % (2 * period);
                        int sx = w - 1 - (k < period ? k : 2 * period - 1 - k);
                        o[(w + x) + y * nw] = px[sx + y * w];
                    }
                }
                w = nw;
                return o;
            }
            if (side == "bottom")
            {
                int nh = h + add;
                var o = new Color32[w * nh];
                Array.Copy(px, o, px.Length);
                for (int x = 0; x < w; x++)
                {
                    if (px[x + (h - 2) * w].a < 8) continue;
                    for (int y = 0; y < add; y++)
                    {
                        int k = y % (2 * period);
                        int sy = h - 1 - (k < period ? k : 2 * period - 1 - k);
                        o[x + (h + y) * w] = px[x + sy * w];
                    }
                }
                h = nh;
                return o;
            }
            return px;
        }

        private sealed class Params
        {
            public float Tolerance = 30f;     // luminance band around backdrop (lighter side uses BrightTol)
            public float BrightTol = 20f;
            public float ChromaTol = 16f;
            public float ShadowDepth = 80f;   // how much darker than backdrop a cast shadow may be
            public float ShadowStrength = 0.82f;
            public float ShadowNoise = 12f;
            public int ShadowReach = 70;      // px from the object where shadows are kept
            public float StrictTol = 15f;     // for enclosed holes
            public int MinHole = 400;
            public int MinComponent = 80;
            public int MergeDistance = 90;
            public bool KeepShadows = true;
            public int Margin = 40;
            public int Band = 3;               // matting band width, px
            public float Erode = 0.55f;        // alpha multiplier of the outermost edge pixel
            public float PocketTol = 22f;      // |lum − backdrop| for pockets
            public float PocketChroma = 12f;
            public float PocketStd = 13f;      // max luminance std-dev inside the pocket (flatness)
            public int PocketMin = 20;         // min pocket area, px

            public static Params From(JNode defaults, JNode img)
            {
                var p = new Params();
                foreach (var src in new[] { defaults, img })
                {
                    if (src.IsNull) continue;
                    p.Tolerance = src.Num("tolerance", p.Tolerance);
                    p.BrightTol = src.Num("brightTol", p.BrightTol);
                    p.ChromaTol = src.Num("chromaTol", p.ChromaTol);
                    p.ShadowDepth = src.Num("shadowDepth", p.ShadowDepth);
                    p.ShadowStrength = src.Num("shadowStrength", p.ShadowStrength);
                    p.ShadowNoise = src.Num("shadowNoise", p.ShadowNoise);
                    p.ShadowReach = src.Int("shadowReach", p.ShadowReach);
                    p.StrictTol = src.Num("strictTol", p.StrictTol);
                    p.MinHole = src.Int("minHole", p.MinHole);
                    p.MinComponent = src.Int("minComponent", p.MinComponent);
                    p.MergeDistance = src.Int("mergeDistance", p.MergeDistance);
                    p.KeepShadows = src.Bool("keepShadows", p.KeepShadows);
                    p.Margin = src.Int("margin", p.Margin);
                    p.Band = src.Int("band", p.Band);
                    p.Erode = src.Num("erode", p.Erode);
                    p.PocketTol = src.Num("pocketTol", p.PocketTol);
                    p.PocketChroma = src.Num("pocketChroma", p.PocketChroma);
                    p.PocketStd = src.Num("pocketStd", p.PocketStd);
                    p.PocketMin = src.Int("pocketMin", p.PocketMin);
                }
                return p;
            }
        }

        // ------------------------------------------------------------------ entry point

        /// <summary>Process everything listed in art_processing.json. Returns metadata for all outputs.</summary>
        public static Dictionary<string, PartMeta> ProcessAll(bool force)
        {
            Directory.CreateDirectory(OutDir);
            var cfg = JNode.Parse(File.ReadAllText(ConfigPath));
            var cache = File.Exists(CachePath) ? JNode.Parse(File.ReadAllText(CachePath)) : new JNode(null);
            var newCache = new Dictionary<string, object>();
            var meta = LoadMeta();
            var defaults = cfg["defaults"];
            // Any change of this processor's source invalidates the cache as well.
            string codeSalt = Version + ":" + CodeHash();

            foreach (var img in cfg["images"].Items())
            {
                string src = ResolveSrc(img.Str("src"));
                string key = img.Str("src");
                string hash = Hash(src, Serialize(defaults) + codeSalt + Serialize(img));
                newCache[key] = hash;
                var outputs = OutputNames(img);
                bool upToDate = !force && cache[key].AsString() == hash && outputs.TrueForAll(n => File.Exists(OutPath(n)) && meta.ContainsKey(n));
                if (upToDate) continue;
                try
                {
                    foreach (var m in ProcessImage(src, img, Params.From(defaults, img))) meta[m.Name] = m;
                    Debug.Log($"[PALINODE] Processed {key} → {string.Join(", ", outputs)}");
                }
                catch (Exception e)
                {
                    throw new Exception($"Art processing failed for {key}: {e.Message}", e);
                }
            }

            foreach (var w in cfg["warps"].Items())
            {
                string src = ResolveSrc(w.Str("src"));
                string key = "warp:" + w.Str("out");
                string hash = Hash(src, Serialize(w) + codeSalt);
                newCache[key] = hash;
                bool upToDate = !force && cache[key].AsString() == hash && File.Exists(OutPath(w.Str("out")));
                if (!upToDate) Warp(src, w);
                foreach (var c in w["crops"].Items())
                {
                    if (upToDate && File.Exists(OutPath(c.Str("out")))) continue;
                    CropFrom(OutPath(w.Str("out")), c);
                }
                var wm = new PartMeta { Name = w.Str("out"), Pivot = new Vector2(0.5f, 0.5f) };
                if (w.Str("pivot") == "bottom")
                {
                    var wpx = Load(OutPath(w.Str("out")), out int ww, out int wh);
                    var mask = new bool[wpx.Length];
                    for (int i = 0; i < wpx.Length; i++) mask[i] = wpx[i].a < 128;
                    wm.Pivot = BottomPivot(mask, ww, wh, new RectInt(0, 0, ww, wh));
                }
                meta[w.Str("out")] = wm;
                foreach (var c in w["crops"].Items()) meta[c.Str("out")] = new PartMeta { Name = c.Str("out"), Pivot = new Vector2(0.5f, 0.5f) };
            }

            // Tone adjustments: painted-dark maps need a brighter "albedo" so 2D lights (which multiply) can reveal them.
            foreach (var a in cfg["levels"].Items())
            {
                string src = ResolveSrc(a.Str("src"));
                string key = "levels:" + a.Str("out");
                string hash = Hash(src, Serialize(a) + codeSalt);
                newCache[key] = hash;
                if (force || cache[key].AsString() != hash || !File.Exists(OutPath(a.Str("out")))) Levels(src, a);
                meta[a.Str("out")] = new PartMeta { Name = a.Str("out"), Pivot = new Vector2(0.5f, 0.5f) };
            }

            // Occluder cut-outs (after "levels", so they can be cut from tone-adjusted maps).
            foreach (var set in cfg["cutouts"].Items())
            {
                string src = ResolveSrc(set.Str("src"));
                string key = "cutouts:" + set.Str("src");
                string hash = Hash(src, Serialize(set) + codeSalt);
                newCache[key] = hash;
                bool upToDate = !force && cache[key].AsString() == hash;
                if (upToDate)
                    foreach (var item in set["items"].Items())
                        if (!File.Exists(OutPath(item.Str("out"))) || !meta.ContainsKey(item.Str("out"))) upToDate = false;
                if (!upToDate) Cutouts(set, meta);
            }

            // Animation frame sequences: background removal + consistent scale and a common anchor (feet under torso).
            foreach (var seq in cfg["sequences"].Items())
            {
                var frames = new List<string>();
                foreach (var fr in seq["frames"].Items()) frames.Add(fr.AsString());
                string prefix = seq.Str("out");
                var names = new List<string>();
                for (int i = 0; i < frames.Count; i++) names.Add(prefix + (i + 1).ToString("00"));
                string key = "seq:" + prefix;
                var sb = new StringBuilder(Serialize(defaults) + codeSalt + Serialize(seq));
                foreach (var fr in frames) sb.Append(FileHashShort(ResolveSrc(fr)));
                string hash = sb.ToString().GetHashCode().ToString() + sb.Length;
                newCache[key] = hash;
                bool upToDate = !force && cache[key].AsString() == hash && names.TrueForAll(n => File.Exists(OutPath(n)) && meta.ContainsKey(n));
                if (upToDate) continue;
                var p = Params.From(defaults, seq);
                for (int i = 0; i < frames.Count; i++)
                {
                    var spec = new JNode(new Dictionary<string, object> { { "out", names[i] } });
                    ProcessImage(ResolveSrc(frames[i]), spec, p);
                }
                NormalizeSequence(names, seq, meta);
                Debug.Log($"[PALINODE] Sequence {prefix}: {frames.Count} frames");
            }

            // Overlays: a region (mask polygon) of a redrawn picture warped onto an original background.
            foreach (var ov in cfg["overlays"].Items())
            {
                string src = ResolveSrc(ov.Str("src"));
                string key = "overlay:" + ov.Str("out");
                string hash = Hash(src, Serialize(ov) + codeSalt);
                newCache[key] = hash;
                if (force || cache[key].AsString() != hash || !File.Exists(OutPath(ov.Str("out")))) Overlay(src, ov);
                meta[ov.Str("out")] = new PartMeta { Name = ov.Str("out"), Pivot = new Vector2(0.5f, 0.5f) };
            }

            SaveMeta(meta);
            File.WriteAllText(CachePath, MiniWrite(newCache));
            return meta;
        }

        /// <summary>"ArtInbox/…" sources are read from the project root (raw deliveries, not imported by Unity).</summary>
        public static string ResolveSrc(string s)
        {
            if (s.StartsWith("ArtInbox/", StringComparison.Ordinal)) return s;
            return Path.Combine(ArtRoot, s).Replace('\\', '/');
        }

        private static string FileHashShort(string path)
        {
            using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").Substring(0, 10);
        }

        /// <summary>
        /// Bring a walk cycle to one scale and anchor. Frames whose silhouette height deviates from the median by more
        /// than "scaleTolerance" are rescaled to the median (redrawn frames of different size); the others keep their
        /// natural height (head bob). Everything is then scaled so the median height equals "height". Pivot: the
        /// lowest opaque row (feet), x = centre of the torso band (20–50 % from the top) — stable while legs swing.
        /// </summary>
        private static void NormalizeSequence(List<string> names, JNode seq, Dictionary<string, PartMeta> meta)
        {
            int target = seq.Int("height", 760);
            float tol = seq.Num("scaleTolerance", 0.04f);
            int margin = seq.Int("margin", 24);
            var data = new List<(Color32[] px, int w, int h, RectInt box, float torsoX)>();
            foreach (var n in names)
            {
                var px = Load(OutPath(n), out int w, out int h);
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[x + y * w].a > 128) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                var box = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
                int t0 = box.yMin + (int)(box.height * seq.Num("torsoTop", 0.2f)), t1 = box.yMin + (int)(box.height * seq.Num("torsoBottom", 0.5f));
                double sx = 0; long cnt = 0;
                for (int y = t0; y < t1; y++)
                for (int x = box.xMin; x < box.xMax; x++)
                    if (px[x + y * w].a > 128) { sx += x; cnt++; }
                data.Add((px, w, h, box, cnt > 0 ? (float)(sx / cnt) : box.center.x));
            }
            var heights = new List<int>();
            foreach (var d in data) heights.Add(d.box.height);
            heights.Sort();
            float median = heights[heights.Count / 2];
            for (int i = 0; i < data.Count; i++)
            {
                var d = data[i];
                float hRel = d.box.height / median;
                float s = target / median * (Mathf.Abs(hRel - 1f) > tol ? 1f / hRel : 1f);
                int ow = Mathf.CeilToInt(d.box.width * s) + margin * 2, oh = Mathf.CeilToInt(d.box.height * s) + margin * 2;
                var o = new Color32[ow * oh];
                for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    float sxp = d.box.xMin + (x - margin + 0.5f) / s - 0.5f, syp = d.box.yMin + (y - margin + 0.5f) / s - 0.5f;
                    o[x + y * ow] = Sample(d.px, d.w, d.h, sxp, syp);
                }
                Save(OutPath(names[i]), o, ow, oh);
                float pivX = (d.torsoX - d.box.xMin) * s + margin;
                float pivY = d.box.height * s + margin; // feet row, from top
                meta[names[i]] = new PartMeta { Name = names[i], Crop = new RectInt(0, 0, ow, oh), Pivot = new Vector2(pivX / ow, 1f - pivY / oh) };
            }
        }

        /// <summary>
        /// Warp the masked region of <c>src</c> so that its "srcQuad" lands on "dstQuad" of a canvas "size"
        /// (e.g. the doorway of a redrawn frame onto the doorway of the original background). Mask edges are feathered.
        /// </summary>
        private static void Overlay(string srcPath, JNode ov)
        {
            var src = Load(srcPath, out int sw, out int sh);
            Vector2 size = ov.Vec2("size", new Vector2(sw, sh));
            int ow = (int)size.x, oh = (int)size.y;
            Vector2[] Q(JNode n) { var q = new Vector2[4]; for (int i = 0; i < 4; i++) q[i] = new Vector2(n[i][0].AsFloat(), n[i][1].AsFloat()); return q; }
            var sq = Q(ov["srcQuad"]);
            var dq = Q(ov["dstQuad"]);
            var H = Homography(dq, sq);   // dst → src
            var mask = new List<Vector2>();
            foreach (var p in ov["mask"].Items()) mask.Add(new Vector2(p[0].AsFloat(), p[1].AsFloat()));
            float feather = ov.Num("feather", 10f);
            var o = new Color32[ow * oh];
            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                float u = x + 0.5f, v = y + 0.5f;
                double den = H[6] * u + H[7] * v + 1.0;
                float sx = (float)((H[0] * u + H[1] * v + H[2]) / den), sy = (float)((H[3] * u + H[4] * v + H[5]) / den);
                var sp = new Vector2(sx, sy);
                if (!InPolygon(mask, sp)) continue;
                float dmin = float.MaxValue;
                for (int i = 0, j = mask.Count - 1; i < mask.Count; j = i++) dmin = Mathf.Min(dmin, SegDist(sp, mask[j], mask[i]));
                float a = feather > 0f ? Mathf.Clamp01(dmin / feather) : 1f;
                var c = Sample(src, sw, sh, sx - 0.5f, sy - 0.5f);
                c.a = (byte)(255 * a * a * (3f - 2f * a));
                o[x + y * ow] = c;
            }
            Save(OutPath(ov.Str("out")), o, ow, oh);
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static string CodeHash()
        {
            var guids = AssetDatabase.FindAssets("ArtProcessor t:MonoScript");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith("/ArtProcessor.cs")) continue;
                using (var md5 = MD5.Create())
                    return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").Substring(0, 12);
            }
            return "nocode";
        }

        private static string Serialize(JNode n) => n.Raw == null ? "null" : MiniWrite(n.Raw);

        private static List<string> OutputNames(JNode img)
        {
            var list = new List<string>();
            if (img["parts"].IsArray) foreach (var p in img["parts"].Items()) list.Add(p.Str("name"));
            else list.Add(img.Str("out"));
            return list;
        }

        public static string OutPath(string name) => OutDir + "/" + name + ".png";

        private static string Hash(string file, string salt)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = File.ReadAllBytes(file);
                var s = Encoding.UTF8.GetBytes(salt);
                var all = new byte[bytes.Length + s.Length];
                Buffer.BlockCopy(bytes, 0, all, 0, bytes.Length);
                Buffer.BlockCopy(s, 0, all, bytes.Length, s.Length);
                return BitConverter.ToString(md5.ComputeHash(all)).Replace("-", "");
            }
        }

        // ------------------------------------------------------------------ image core

        private static Color32[] Load(string path, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new Exception("Cannot decode " + path);
            w = tex.width;
            h = tex.height;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            // Unity textures are bottom-up; flip to top-down for intuitive image coordinates.
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, (h - 1 - y) * w, flipped, y * w, w);
            return flipped;
        }

        private static void Save(string path, Color32[] topDown, int w, int h)
        {
            var px = new Color32[topDown.Length];
            for (int y = 0; y < h; y++) Array.Copy(topDown, y * w, px, (h - 1 - y) * w, w);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static float Lum(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        private static float Chroma(Color32 c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));

        private static List<PartMeta> ProcessImage(string srcPath, JNode img, Params p)
        {
            var src = Load(srcPath, out int w, out int h);
            int n = w * h;

            // 1. Backdrop estimate from neutral border pixels.
            var border = new List<float>();
            for (int x = 0; x < w; x += 3) { AddBorder(src[x + 2 * w]); AddBorder(src[x + (h - 3) * w]); }
            for (int y = 0; y < h; y += 3) { AddBorder(src[2 + y * w]); AddBorder(src[w - 3 + y * w]); }
            void AddBorder(Color32 c) { if (Chroma(c) <= p.ChromaTol) border.Add(Lum(c)); }
            if (border.Count < 20) throw new Exception("No neutral backdrop found on the border.");
            border.Sort();
            float bgL = border[border.Count / 2];

            // 2. Flood fill from border.
            var lum = new float[n];
            var neutral = new bool[n];
            for (int i = 0; i < n; i++) { lum[i] = Lum(src[i]); neutral[i] = Chroma(src[i]) <= p.ChromaTol; }
            bool Candidate(int i) => neutral[i] && lum[i] <= bgL + p.BrightTol && lum[i] >= bgL - p.ShadowDepth;
            var bg = new bool[n];
            var queue = new Queue<int>(n / 4);
            void Seed(int i) { if (!bg[i] && Candidate(i) && Mathf.Abs(lum[i] - bgL) <= p.Tolerance) { bg[i] = true; queue.Enqueue(i); } }
            for (int x = 0; x < w; x++) { Seed(x); Seed(x + (h - 1) * w); }
            for (int y = 0; y < h; y++) { Seed(y * w); Seed(w - 1 + y * w); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            void Visit(int j) { if (!bg[j] && Candidate(j)) { bg[j] = true; queue.Enqueue(j); } }

            // Enclosed backdrop pockets (holes): strict match, big enough.
            var holeCenters = new List<(Vector2 c, int area, RectInt box)>();
            var seen = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (bg[i] || seen[i] || !neutral[i] || Mathf.Abs(lum[i] - bgL) > p.StrictTol) continue;
                var comp = new List<int>();
                queue.Enqueue(i);
                seen[i] = true;
                while (queue.Count > 0)
                {
                    int k = queue.Dequeue();
                    comp.Add(k);
                    int x = k % w, y = k / w;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = nx + ny * w;
                        if (seen[j] || bg[j] || !neutral[j] || Mathf.Abs(lum[j] - bgL) > p.StrictTol) continue;
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
                if (comp.Count < p.MinHole) continue;
                Vector2 c = Vector2.zero;
                int minX = w, minY = h, maxX = 0, maxY = 0;
                foreach (int k in comp)
                {
                    bg[k] = true;
                    int x = k % w, y = k / w;
                    c += new Vector2(x, y);
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
                holeCenters.Add((c / comp.Count, comp.Count, new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1)));
            }

            // Small enclosed backdrop pockets (between fingers, in hair, between arm and body): flat, neutral,
            // backdrop-bright regions. Texture (local std-dev) separates them from grey cloth or grey hair.
            int pockets = RemovePockets(src, lum, bg, w, h, bgL, p);

            // Remove tiny object specks (noise) → backdrop.
            var label = new int[n];
            var comps = LabelComponents(bg, w, h, label, out var areas, out var boxes);
            for (int i = 0; i < n; i++)
                if (!bg[i] && areas[label[i]] < p.MinComponent) bg[i] = true;

            // Local backdrop luminance field (coarse grid of pure backdrop pixels).
            float[] field = BackdropField(lum, bg, w, h, bgL, p.Tolerance);

            // 3. Distance from object (for shadow reach) and from backdrop (for matting band).
            var distObj = DistanceField(bg, w, h, true, p.ShadowReach + 2);
            var distBg = DistanceField(bg, w, h, false, p.Band + 2);

            // Mean backdrop colour (keeps a slight tint of the grey, used for un-mixing edge pixels).
            Vector3 bgRGB = BackdropColor(src, bg, neutral, lum, bgL, p.Tolerance);

            // 4. Compose RGBA. Edge band: alpha from colour un-mixing C = a·F + (1−a)·B, grey spill removed,
            //    rim eroded by ~1 px; shadows become soft dark alpha.
            var outPx = new Color32[n];
            var inner = PropagateInnerColors(src, distBg, w, h, p.Band + 1);
            for (int i = 0; i < n; i++)
            {
                if (bg[i])
                {
                    if (!p.KeepShadows || distObj[i] > p.ShadowReach) { outPx[i] = new Color32(0, 0, 0, 0); continue; }
                    float local = field[i];
                    float dark = local - lum[i] - p.ShadowNoise;
                    float a = Mathf.Clamp01(dark / Mathf.Max(20f, local * 0.62f)) * p.ShadowStrength;
                    float reachFade = 1f - Mathf.Clamp01((distObj[i] - p.ShadowReach * 0.6f) / (p.ShadowReach * 0.4f));
                    a *= reachFade;
                    outPx[i] = new Color32(14, 11, 10, (byte)(a * 255f));
                    continue;
                }
                int d = distBg[i];
                if (d > p.Band) { outPx[i] = new Color32(src[i].r, src[i].g, src[i].b, 255); continue; }

                Vector3 C = new Vector3(src[i].r, src[i].g, src[i].b);
                Vector3 F = new Vector3(inner[i].r, inner[i].g, inner[i].b);
                Vector3 B = bgRGB * (field[i] / Mathf.Max(1f, bgL));
                Vector3 fb = F - B;
                float alpha;
                if (fb.sqrMagnitude < 28f * 28f)
                    alpha = d <= 1 ? 0.4f : d == 2 ? 0.8f : 1f; // object colour ≈ backdrop: fall back to distance
                else
                    alpha = Mathf.Clamp01(Vector3.Dot(C - B, fb) / fb.sqrMagnitude);
                if (d <= 1) alpha *= p.Erode;                   // trim the outermost pixel
                Vector3 col = F;
                if (alpha > 0.15f)
                {
                    Vector3 un = B + (C - B) / alpha;           // colour with the grey un-mixed
                    un = new Vector3(Mathf.Clamp(un.x, 0, 255), Mathf.Clamp(un.y, 0, 255), Mathf.Clamp(un.z, 0, 255));
                    // Keep un-mixed detail only when it does not drift toward the backdrop grey.
                    float toward = Vector3.Distance(un, B) >= Vector3.Distance(F, B) * 0.6f ? 0.5f : 0f;
                    col = Vector3.Lerp(F, un, toward);
                }
                outPx[i] = new Color32((byte)col.x, (byte)col.y, (byte)col.z, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
            SmoothEdgeAlpha(outPx, bg, distBg, w, h, p.Band);
            SmoothShadowAlpha(outPx, bg, w, h);
            if (pockets > 0) Debug.Log($"[PALINODE] {Path.GetFileName(srcPath)}: removed {pockets} backdrop pockets");

            // 5. Parts.
            var result = new List<PartMeta>();
            if (img.Has("sprayCut")) SprayCut(outPx, w, h, img["sprayCut"]);
            if (img.Has("groundCut")) GroundCut(outPx, w, h, img["groundCut"]);

            if (!img["parts"].IsArray)
            {
                ApplyErase(outPx, w, h, img["erase"]);
                string name = img.Str("out");
                var pm = new PartMeta { Name = name, Pivot = new Vector2(0.5f, 0.5f), Crop = new RectInt(0, 0, w, h) };

                // Tool tip: the opaque pixel furthest along "tipDir" (image coords, y down).
                if (img.Has("tipDir"))
                {
                    Vector2 dir = img.Vec2("tipDir", new Vector2(-1f, 1f)).normalized;
                    float best = float.MinValue;
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (outPx[x + y * w].a < 200) continue;
                        float d = x * dir.x + y * dir.y;
                        if (d > best) { best = d; pm.Tip = new Vector2(x + 0.5f, y + 0.5f); }
                    }
                }

                // Extend a limb that leaves the picture (sleeve) so it always runs off-screen.
                int ow = w, oh = h;
                var outFinal = outPx;
                if (img.Has("extend"))
                {
                    var ext = img["extend"];
                    Vector2 dir = ext.Vec2("dir", new Vector2(1f, 0f)).normalized;
                    Vector2 add = ext.Vec2("px", new Vector2(700f, 0f));
                    pm.Elbow = ExitCentroid(outFinal, ow, oh);
                    outFinal = ExtendAlong(outFinal, ref ow, ref oh, (int)add.x, (int)add.y, dir, ext.Int("period", 96));
                }
                if (img.Has("elbowPush") && !float.IsNaN(pm.Elbow.x))
                    pm.Elbow += img.Vec2("elbowPush", Vector2.zero);

                Save(OutPath(name), outFinal, ow, oh);
                pm.Crop = new RectInt(0, 0, ow, oh);
                if (img.Str("pivot") == "bottom") pm.Pivot = BottomPivot(bg, w, h, new RectInt(0, 0, w, h));
                if (img.Str("pivot") == "tip" && !float.IsNaN(pm.Tip.x)) pm.Pivot = new Vector2(pm.Tip.x / ow, 1f - pm.Tip.y / oh);
                result.Add(pm);
                return result;
            }

            // Recompute components after cleanup and pick the N largest as seeds.
            comps = LabelComponents(bg, w, h, label, out areas, out boxes);
            var partSpecs = new List<JNode>(img["parts"].Items());
            var order = new List<int>();
            for (int k = 1; k <= comps; k++) order.Add(k);
            order.Sort((a, b) => areas[b].CompareTo(areas[a]));
            if (order.Count < partSpecs.Count) throw new Exception($"Expected {partSpecs.Count} parts, found {order.Count} components.");
            var seeds = order.GetRange(0, partSpecs.Count);
            string sort = img.Str("sort", "x");
            if (sort == "x") seeds.Sort((a, b) => boxes[a].center.x.CompareTo(boxes[b].center.x));
            else if (sort == "y") seeds.Sort((a, b) => boxes[a].center.y.CompareTo(boxes[b].center.y));
            else if (sort == "rows")
            {
                // Reading order: rows top→bottom (a new row starts where the vertical gap exceeds half a part's
                // height), each row left→right.
                seeds.Sort((a, b) => boxes[a].center.y.CompareTo(boxes[b].center.y));
                var rows = new List<List<int>>();
                foreach (int s in seeds)
                {
                    var last = rows.Count > 0 ? rows[rows.Count - 1] : null;
                    if (last != null)
                    {
                        float rowY = 0f, rowH = 0f;
                        foreach (int r in last) { rowY += boxes[r].center.y; rowH = Mathf.Max(rowH, boxes[r].height); }
                        rowY /= last.Count;
                        if (Mathf.Abs(boxes[s].center.y - rowY) < Mathf.Max(rowH, boxes[s].height) * 0.5f) { last.Add(s); continue; }
                    }
                    rows.Add(new List<int> { s });
                }
                seeds.Clear();
                foreach (var row in rows)
                {
                    row.Sort((a, b) => boxes[a].center.x.CompareTo(boxes[b].center.x));
                    seeds.AddRange(row);
                }
            }

            // Map every component to a part (nearest seed by box distance) or drop it.
            var compPart = new int[comps + 1];
            for (int k = 1; k <= comps; k++)
            {
                compPart[k] = -1;
                int si = seeds.IndexOf(k);
                if (si >= 0) { compPart[k] = si; continue; }
                float best = float.MaxValue;
                for (int s = 0; s < seeds.Count; s++)
                {
                    float dd = BoxDistance(boxes[k], boxes[seeds[s]]);
                    if (dd < best) { best = dd; compPart[k] = s; }
                }
                if (best > p.MergeDistance) compPart[k] = -1;
            }

            // Voronoi assignment of every pixel (objects + shadows) to the nearest part.
            var owner = new int[n];
            for (int i = 0; i < n; i++) owner[i] = -1;
            var q = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (bg[i]) continue;
                int part = compPart[label[i]];
                if (part < 0) { outPx[i] = new Color32(0, 0, 0, 0); continue; }
                owner[i] = part;
                q.Enqueue(i);
            }
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                int x = i % w, y = i / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (owner[j] >= 0 || outPx[j].a == 0) continue;
                    owner[j] = owner[i];
                    q.Enqueue(j);
                }
            }

            for (int s = 0; s < partSpecs.Count; s++)
            {
                var spec = partSpecs[s];
                // Bounding box of opaque object pixels of this part.
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int i = 0; i < n; i++)
                {
                    if (owner[i] != s || bg[i]) continue;
                    int x = i % w, y = i / w;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
                var objBox = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
                int m = p.Margin;
                var crop = new RectInt(Math.Max(0, minX - m), Math.Max(0, minY - m), 0, 0);
                crop.width = Math.Min(w, maxX + m + 1) - crop.x;
                crop.height = Math.Min(h, maxY + m + 1) - crop.y;
                var partPx = new Color32[crop.width * crop.height];
                for (int y = 0; y < crop.height; y++)
                for (int x = 0; x < crop.width; x++)
                {
                    int i = (crop.x + x) + (crop.y + y) * w;
                    partPx[x + y * crop.width] = owner[i] == s ? outPx[i] : new Color32(0, 0, 0, 0);
                }

                var pm = new PartMeta { Name = spec.Str("name"), Crop = crop, Pivot = new Vector2(0.5f, 0.5f) };
                if (spec.Bool("hand"))
                {
                    // Pivot = centre of the ring's hole; rotate so the tip points straight up.
                    Vector2 pivotSrc = new Vector2(objBox.center.x, objBox.yMax - objBox.width * 0.5f);
                    int bestArea = 0;
                    foreach (var hc in holeCenters)
                    {
                        if (!objBox.Contains(new Vector2Int((int)hc.c.x, (int)hc.c.y))) continue;
                        if (hc.area > bestArea) { bestArea = hc.area; pivotSrc = hc.c; }
                    }
                    if (spec.Has("pivotSrc")) pivotSrc = spec.Vec2("pivotSrc", pivotSrc);
                    Vector2 tip = pivotSrc;
                    float far = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        if (owner[i] != s || bg[i]) continue;
                        var v = new Vector2(i % w, i / w);
                        float dd = (v - pivotSrc).sqrMagnitude;
                        if (dd > far) { far = dd; tip = v; }
                    }
                    Vector2 dir = tip - pivotSrc; // image space (y down)
                    float angle = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg; // 0 when pointing up
                    var rotated = RotateAround(partPx, crop.width, crop.height, pivotSrc - new Vector2(crop.x, crop.y), -angle,
                        out int rw, out int rh, out Vector2 newPivot);
                    Save(OutPath(pm.Name), rotated, rw, rh);
                    pm.Pivot = new Vector2(newPivot.x / rw, 1f - newPivot.y / rh);
                    pm.Rotation = -angle;
                    result.Add(pm);
                    continue;
                }

                ApplyErase(partPx, crop.width, crop.height, spec["erase"]);
                Save(OutPath(pm.Name), partPx, crop.width, crop.height);
                if (spec.Str("pivot") == "bottom")
                {
                    var local = new bool[crop.width * crop.height];
                    for (int y = 0; y < crop.height; y++)
                    for (int x = 0; x < crop.width; x++)
                    {
                        int i = (crop.x + x) + (crop.y + y) * w;
                        local[x + y * crop.width] = !(owner[i] == s && !bg[i]);
                    }
                    pm.Pivot = BottomPivot(local, crop.width, crop.height, new RectInt(0, 0, crop.width, crop.height));
                }
                else if (spec.Has("pivotSrc"))
                {
                    Vector2 ps = spec.Vec2("pivotSrc", Vector2.zero);
                    pm.Pivot = new Vector2((ps.x - crop.x) / crop.width, 1f - (ps.y - crop.y) / crop.height);
                }
                result.Add(pm);
            }
            return result;
        }

        /// <summary>
        /// Removes painted ground effects (wet spray, splashes) under a vehicle: flood from the transparent area into
        /// opaque pixels that are not ink (lum ≥ inkLum) and not strongly coloured, only in the lower part of the object
        /// (below "minY" of its height). The black ink outline of the body stops the flood. Leftover crumbs are removed.
        /// </summary>
        private static void SprayCut(Color32[] px, int w, int h, JNode s)
        {
            float inkLum = s.Num("inkLum", 58f), maxChroma = s.Num("chroma", 42f), minYRel = s.Num("minY", 0.45f);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[x + y * w].a > 128) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxY < 0) return;
            int yCut = minY + (int)((maxY - minY) * minYRel);
            bool Spray(int i)
            {
                var c = px[i];
                if (c.a < 8) return false;
                float l = Lum(c);
                return l >= inkLum && Chroma(c) <= maxChroma;
            }
            var kill = new bool[w * h];
            var q = new Queue<int>();
            for (int y = yCut; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if (px[i].a >= 8) continue;
                // transparent pixel next to an opaque spray-like pixel → seed
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < yCut || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (!kill[j] && Spray(j)) { kill[j] = true; q.Enqueue(j); }
                }
            }
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                int x = i % w, y = i / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < yCut || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (kill[j] || !Spray(j)) continue;
                    kill[j] = true;
                    q.Enqueue(j);
                }
            }
            for (int i = 0; i < kill.Length; i++) if (kill[i]) px[i] = new Color32(0, 0, 0, 0);

            // Morphological opening in the spray zone: thin dark streaks (spray) vanish, the solid body survives.
            int r = s.Int("open", 0);
            if (r > 0)
            {
                var solid = new bool[w * h];
                for (int i = 0; i < solid.Length; i++) solid[i] = px[i].a >= 128;
                var eroded = Morph(solid, w, h, r, false);
                var opened = Morph(eroded, w, h, r + 1, true);
                for (int y = yCut; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    if (px[i].a > 0 && !opened[i]) px[i] = new Color32(0, 0, 0, 0);
                }
            }
            // Crumbs: small opaque islands left in the spray zone.
            var bgMask = new bool[w * h];
            for (int i = 0; i < bgMask.Length; i++) bgMask[i] = px[i].a < 64;
            var label = new int[w * h];
            LabelComponents(bgMask, w, h, label, out var areas, out var boxes);
            int keepMin = s.Int("minIsland", 2500);
            for (int i = 0; i < px.Length; i++)
                if (!bgMask[i] && areas[label[i]] < keepMin) px[i] = new Color32(0, 0, 0, 0);
        }

        /// <summary>
        /// Everything below the ground-contact polyline "line" (x ascending, image px) is painted ground (spray, puddles)
        /// and is removed; the first "shadowDepth" px under the line become a soft black contact shadow.
        /// </summary>
        private static void GroundCut(Color32[] px, int w, int h, JNode g)
        {
            var line = new List<Vector2>();
            foreach (var p in g["line"].Items()) line.Add(new Vector2(p[0].AsFloat(), p[1].AsFloat()));
            if (line.Count < 2) return;
            float margin = g.Num("margin", 3f), depth = g.Num("shadowDepth", 16f), strength = g.Num("shadow", 0.5f);
            for (int x = 0; x < w; x++)
            {
                float gy = line[line.Count - 1].y;
                if (x <= line[0].x) gy = line[0].y;
                else
                    for (int k = 0; k < line.Count - 1; k++)
                        if (x >= line[k].x && x <= line[k + 1].x)
                        {
                            float t = (x - line[k].x) / Mathf.Max(1e-3f, line[k + 1].x - line[k].x);
                            gy = Mathf.Lerp(line[k].y, line[k + 1].y, t);
                            break;
                        }
                for (int y = Mathf.Max(0, Mathf.CeilToInt(gy + margin)); y < h; y++)
                {
                    int i = x + y * w;
                    if (px[i].a == 0) continue;
                    float d = y - gy - margin;
                    float a = d < depth ? strength * (1f - d / depth) * (px[i].a / 255f) : 0f;
                    px[i] = new Color32(6, 6, 8, (byte)(a * 255f));
                }
            }
        }

        /// <summary>Binary erosion (dilate=false) / dilation with a square of radius r, separable.</summary>
        private static bool[] Morph(bool[] m, int w, int h, int r, bool dilate)
        {
            var tmp = new bool[m.Length];
            var o = new bool[m.Length];
            for (int y = 0; y < h; y++)
            {
                int run = 0; // count of set pixels in window
                for (int x = -r; x < w; x++)
                {
                    int add = x + r, rem = x - r - 1;
                    if (add < w && m[add + y * w]) run++;
                    if (rem >= 0 && m[rem + y * w]) run--;
                    if (x < 0) continue;
                    int win = Math.Min(w - 1, x + r) - Math.Max(0, x - r) + 1;
                    tmp[x + y * w] = dilate ? run > 0 : run == win;
                }
            }
            for (int x = 0; x < w; x++)
            {
                int run = 0;
                for (int y = -r; y < h; y++)
                {
                    int add = y + r, rem = y - r - 1;
                    if (add < h && tmp[x + add * w]) run++;
                    if (rem >= 0 && tmp[x + rem * w]) run--;
                    if (y < 0) continue;
                    int win = Math.Min(h - 1, y + r) - Math.Max(0, y - r) + 1;
                    o[x + y * w] = dilate ? run > 0 : run == win;
                }
            }
            return o;
        }

        private static void ApplyErase(Color32[] px, int w, int h, JNode erase)
        {
            if (!erase.IsArray) return;
            foreach (var poly in erase.Items())
            {
                var pts = new List<Vector2>();
                foreach (var pt in poly.Items()) pts.Add(new Vector2(pt[0].AsFloat(), pt[1].AsFloat()));
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (InPolygon(pts, new Vector2(x + 0.5f, y + 0.5f))) px[x + y * w] = new Color32(0, 0, 0, 0);
            }
        }

        private static bool InPolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        private static float BoxDistance(RectInt a, RectInt b)
        {
            float dx = Mathf.Max(0, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
            float dy = Mathf.Max(0, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Pivot at the feet: bottom-most object row, x = centre of the lowest 6% of the object.</summary>
        private static Vector2 BottomPivot(bool[] bgMask, int w, int h, RectInt area)
        {
            int bottom = -1, top = h;
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
                if (!bgMask[x + y * w]) { bottom = Math.Max(bottom, y); top = Math.Min(top, y); }
            if (bottom < 0) return new Vector2(0.5f, 0f);
            // x: centre of the silhouette's bounding box (stable for walking poses where one foot is ahead).
            int left = w, right = -1;
            for (int y = top; y <= bottom; y++)
            for (int x = area.xMin; x < area.xMax; x++)
                if (!bgMask[x + y * w]) { left = Math.Min(left, x); right = Math.Max(right, x); }
            float cx = right >= left ? (left + right + 1) * 0.5f : w * 0.5f;
            return new Vector2(cx / w, 1f - (bottom + 1f) / h);
        }

        private static int LabelComponents(bool[] bg, int w, int h, int[] label, out List<int> areas, out List<RectInt> boxes)
        {
            Array.Clear(label, 0, label.Length);
            areas = new List<int> { 0 };
            boxes = new List<RectInt> { default };
            int next = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < bg.Length; s++)
            {
                if (bg[s] || label[s] != 0) continue;
                next++;
                int area = 0, minX = w, minY = h, maxX = 0, maxY = 0;
                label[s] = next;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    area++;
                    int x = i % w, y = i / w;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = nx + ny * w;
                        if (bg[j] || label[j] != 0) continue;
                        label[j] = next;
                        stack.Push(j);
                    }
                }
                areas.Add(area);
                boxes.Add(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
            }
            return next;
        }

        /// <summary>Chessboard distance (BFS) to the nearest pixel of the other class, capped.</summary>
        private static int[] DistanceField(bool[] bg, int w, int h, bool fromObject, int cap)
        {
            int n = w * h;
            var dist = new int[n];
            var q = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                bool source = fromObject ? !bg[i] : bg[i];
                dist[i] = source ? 0 : int.MaxValue;
                if (source) q.Enqueue(i);
            }
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                if (dist[i] >= cap) continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (dist[j] <= dist[i] + 1) continue;
                    dist[j] = dist[i] + 1;
                    q.Enqueue(j);
                }
            }
            return dist;
        }

        private static int RemovePockets(Color32[] src, float[] lum, bool[] bg, int w, int h, float bgL, Params p)
        {
            int n = w * h;
            // Candidates by colour only; flatness and thickness are judged per connected region below
            // (a 5×5 window never fits inside a small pocket between curls).
            var cand = new bool[n];
            for (int i = 0; i < n; i++)
                cand[i] = !bg[i] && Chroma(src[i]) <= p.PocketChroma && Mathf.Abs(lum[i] - bgL) <= p.PocketTol;
            var seen = new bool[n];
            var q = new Queue<int>();
            var comp = new List<int>();
            int removed = 0;
            for (int s = 0; s < n; s++)
            {
                if (!cand[s] || seen[s]) continue;
                comp.Clear();
                seen[s] = true;
                q.Enqueue(s);
                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    comp.Add(k);
                    int x = k % w, y = k / w;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = nx + ny * w;
                        if (seen[j] || !cand[j]) continue;
                        seen[j] = true;
                        q.Enqueue(j);
                    }
                }
                if (comp.Count < p.PocketMin) continue;
                // Flat (low luminance spread) and not a thin strand (enough pixels fully surrounded by the region).
                double m1 = 0, m2 = 0;
                int interior = 0;
                foreach (int k in comp)
                {
                    m1 += lum[k]; m2 += lum[k] * lum[k];
                    int x = k % w, y = k / w;
                    if (x > 0 && y > 0 && x < w - 1 && y < h - 1 && cand[k - 1] && cand[k + 1] && cand[k - w] && cand[k + w]
                        && cand[k - w - 1] && cand[k - w + 1] && cand[k + w - 1] && cand[k + w + 1]) interior++;
                }
                double mean = m1 / comp.Count, std = Math.Sqrt(Math.Max(0, m2 / comp.Count - mean * mean));
                if (std > p.PocketStd || interior < Math.Max(3, comp.Count * 0.12)) continue;
                foreach (int k in comp) bg[k] = true;
                removed++;
            }
            return removed;
        }

        private static Vector3 BackdropColor(Color32[] src, bool[] bg, bool[] neutral, float[] lum, float bgL, float tol)
        {
            Vector3 sum = Vector3.zero;
            int cnt = 0;
            for (int i = 0; i < src.Length; i += 7)
            {
                if (!bg[i] || !neutral[i] || Mathf.Abs(lum[i] - bgL) > tol * 0.5f) continue;
                sum += new Vector3(src[i].r, src[i].g, src[i].b);
                cnt++;
            }
            return cnt > 0 ? sum / cnt : new Vector3(bgL, bgL, bgL);
        }

        /// <summary>Light 3×3 smoothing of alpha inside the matting band (removes stair-stepping).</summary>
        private static void SmoothEdgeAlpha(Color32[] px, bool[] bg, int[] distBg, int w, int h, int band)
        {
            var a = new float[px.Length];
            for (int i = 0; i < px.Length; i++) a[i] = bg[i] ? 0f : px[i].a;
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = x + y * w;
                if (bg[i] || distBg[i] > band) continue;
                float s = a[i] * 4f + (a[i - 1] + a[i + 1] + a[i - w] + a[i + w]) * 2f
                          + a[i - w - 1] + a[i - w + 1] + a[i + w - 1] + a[i + w + 1];
                px[i].a = (byte)Mathf.Clamp(Mathf.Min(a[i], s / 16f + 8f), 0f, 255f);
            }
        }

        /// <summary>For edge pixels (inside the band) take the colour of the nearest pixel beyond the band.</summary>
        private static Color32[] PropagateInnerColors(Color32[] src, int[] distBg, int w, int h, int minDist = 3)
        {
            int n = w * h;
            var col = new Color32[n];
            var done = new bool[n];
            var q = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (distBg[i] >= minDist && distBg[i] != int.MaxValue) { col[i] = src[i]; done[i] = true; q.Enqueue(i); }
                else if (distBg[i] == int.MaxValue) { col[i] = src[i]; done[i] = true; }
            }
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                int x = i % w, y = i / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = nx + ny * w;
                    if (done[j] || distBg[j] == 0) continue;
                    col[j] = col[i];
                    done[j] = true;
                    q.Enqueue(j);
                }
            }
            for (int i = 0; i < n; i++) if (!done[i]) col[i] = src[i];
            return col;
        }

        private static float[] BackdropField(float[] lum, bool[] bg, int w, int h, float bgL, float tol)
        {
            const int cell = 48;
            int gw = (w + cell - 1) / cell, gh = (h + cell - 1) / cell;
            var sum = new float[gw * gh];
            var cnt = new int[gw * gh];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if (!bg[i] || Mathf.Abs(lum[i] - bgL) > tol * 0.6f) continue;
                int g = x / cell + (y / cell) * gw;
                sum[g] += lum[i];
                cnt[g]++;
            }
            var grid = new float[gw * gh];
            var have = new bool[gw * gh];
            for (int g = 0; g < grid.Length; g++)
            {
                if (cnt[g] > 20) { grid[g] = sum[g] / cnt[g]; have[g] = true; }
            }
            // Fill empty cells from neighbours (iterative dilation).
            for (int iter = 0; iter < gw + gh; iter++)
            {
                bool any = false;
                var next = (float[])grid.Clone();
                var nextHave = (bool[])have.Clone();
                for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    int g = gx + gy * gw;
                    if (have[g]) continue;
                    float s = 0; int c = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = gx + dx, ny = gy + dy;
                        if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                        int k = nx + ny * gw;
                        if (have[k]) { s += grid[k]; c++; }
                    }
                    if (c > 0) { next[g] = s / c; nextHave[g] = true; any = true; }
                }
                grid = next;
                have = nextHave;
                if (!any) break;
            }
            var field = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                float fy = Mathf.Clamp((y + 0.5f) / cell - 0.5f, 0, gh - 1);
                int y0 = (int)fy, y1 = Math.Min(gh - 1, y0 + 1);
                float ty = fy - y0;
                for (int x = 0; x < w; x++)
                {
                    float fx = Mathf.Clamp((x + 0.5f) / cell - 0.5f, 0, gw - 1);
                    int x0 = (int)fx, x1 = Math.Min(gw - 1, x0 + 1);
                    float tx = fx - x0;
                    float a = have[x0 + y0 * gw] ? grid[x0 + y0 * gw] : bgL;
                    float b = have[x1 + y0 * gw] ? grid[x1 + y0 * gw] : bgL;
                    float c = have[x0 + y1 * gw] ? grid[x0 + y1 * gw] : bgL;
                    float d = have[x1 + y1 * gw] ? grid[x1 + y1 * gw] : bgL;
                    field[x + y * w] = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                }
            }
            return field;
        }

        /// <summary>Box-blur the alpha of shadow pixels (removes hatching speckle).</summary>
        private static void SmoothShadowAlpha(Color32[] px, bool[] bg, int w, int h)
        {
            var a = new float[px.Length];
            for (int i = 0; i < px.Length; i++) a[i] = bg[i] ? px[i].a : -1f;
            const int r = 2;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w;
                if (!bg[i]) continue;
                float s = 0; int c = 0;
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    float v = a[nx + ny * w];
                    if (v < 0f) continue;
                    s += v; c++;
                }
                if (c > 0) px[i].a = (byte)Mathf.Clamp(s / c, 0f, 255f);
            }
        }

        private static Color32 Sample(Color32[] px, int w, int h, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            Color32 P(int xx, int yy) => xx < 0 || yy < 0 || xx >= w || yy >= h ? new Color32(0, 0, 0, 0) : px[xx + yy * w];
            Color a = P(x0, y0), b = P(x0 + 1, y0), c = P(x0, y0 + 1), d = P(x0 + 1, y0 + 1);
            // Premultiplied interpolation avoids dark fringes.
            Vector4 pa = new Vector4(a.r * a.a, a.g * a.a, a.b * a.a, a.a);
            Vector4 pb = new Vector4(b.r * b.a, b.g * b.a, b.b * b.a, b.a);
            Vector4 pc = new Vector4(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
            Vector4 pd = new Vector4(d.r * d.a, d.g * d.a, d.b * d.a, d.a);
            Vector4 r = Vector4.Lerp(Vector4.Lerp(pa, pb, tx), Vector4.Lerp(pc, pd, tx), ty);
            if (r.w <= 0.0001f) return new Color32(0, 0, 0, 0);
            return new Color(r.x / r.w, r.y / r.w, r.z / r.w, r.w);
        }

        private static Color32[] RotateAround(Color32[] px, int w, int h, Vector2 pivot, float degrees,
            out int ow, out int oh, out Vector2 newPivot)
        {
            // Image space has y down; a positive "degrees" rotates clockwise on screen.
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 Rot(Vector2 v) => new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
            var corners = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(0, h), new Vector2(w, h) };
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in corners)
            {
                var r = Rot(c - pivot);
                min = Vector2.Min(min, r);
                max = Vector2.Max(max, r);
            }
            ow = Mathf.CeilToInt(max.x - min.x) + 2;
            oh = Mathf.CeilToInt(max.y - min.y) + 2;
            newPivot = -min + Vector2.one;
            var outPx = new Color32[ow * oh];
            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                Vector2 r = new Vector2(x + 0.5f, y + 0.5f) - newPivot;
                // inverse rotation
                Vector2 s = new Vector2(r.x * cos + r.y * sin, -r.x * sin + r.y * cos) + pivot;
                outPx[x + y * ow] = Sample(px, w, h, s.x - 0.5f, s.y - 0.5f);
            }
            return outPx;
        }

        // ------------------------------------------------------------------ warp & crop

        private static void Warp(string srcPath, JNode w)
        {
            var src = Load(srcPath, out int sw, out int sh);
            Vector2 size = w.Vec2("size", new Vector2(1380, 930));
            int ow = (int)size.x, oh = (int)size.y;
            var quad = new Vector2[4];
            for (int i = 0; i < 4; i++) quad[i] = new Vector2(w["quad"][i][0].AsFloat(), w["quad"][i][1].AsFloat());
            var H = Homography(new[] { new Vector2(0, 0), new Vector2(ow, 0), new Vector2(ow, oh), new Vector2(0, oh) }, quad);
            var outPx = new Color32[ow * oh];
            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                float u = x + 0.5f, v = y + 0.5f;
                float den = (float)(H[6] * u + H[7] * v + 1.0);
                float sx = (float)((H[0] * u + H[1] * v + H[2]) / den);
                float sy = (float)((H[3] * u + H[4] * v + H[5]) / den);
                var c = Sample(src, sw, sh, sx - 0.5f, sy - 0.5f);
                if (!w.Bool("keepAlpha")) c.a = 255;
                outPx[x + y * ow] = c;
            }
            Save(OutPath(w.Str("out")), outPx, ow, oh);
        }

        /// <summary>
        /// Cut objects out of a painted map by polygon (occluders the player can walk behind). The sprite pivot is the
        /// object's base point, so its sorting order follows the same "Y of the base" rule as characters.
        /// Meta: "base" (map px) — the level places the sprite exactly over its painted original.
        /// </summary>
        private static void Cutouts(JNode set, Dictionary<string, PartMeta> meta)
        {
            string srcPath = ResolveSrc(set.Str("src"));
            var src = Load(srcPath, out int sw, out int sh);
            foreach (var item in set["items"].Items())
            {
                var poly = new List<Vector2>();
                foreach (var p in item["poly"].Items()) poly.Add(new Vector2(p[0].AsFloat(), p[1].AsFloat()));
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in poly) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX) - 2), y0 = Mathf.Max(0, Mathf.FloorToInt(minY) - 2);
                int x1 = Mathf.Min(sw, Mathf.CeilToInt(maxX) + 2), y1 = Mathf.Min(sh, Mathf.CeilToInt(maxY) + 2);
                int w = x1 - x0, h = y1 - y0;
                var outPx = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 4×4 supersampled coverage → soft 1 px edge.
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (InPolygon(poly, new Vector2(x0 + x + (sx + 0.5f) / 4f, y0 + y + (sy + 0.5f) / 4f))) inside++;
                    if (inside == 0) continue;
                    var c = src[(x0 + x) + (y0 + y) * sw];
                    c.a = (byte)(255 * inside / 16);
                    outPx[x + y * w] = c;
                }
                string name = item.Str("out");
                Save(OutPath(name), outPx, w, h);
                Vector2 b = item.Vec2("base", new Vector2((minX + maxX) * 0.5f, maxY));
                meta[name] = new PartMeta
                {
                    Name = name,
                    Crop = new RectInt(x0, y0, w, h),
                    Pivot = new Vector2((b.x - x0) / w, 1f - (b.y - y0) / h),
                    Tip = b // stored as "tip"; exposed to the runtime also as "base"
                };
            }
        }

        /// <summary>out = pow(clamp(in * gain), gamma), per channel in sRGB space.</summary>
        private static void Levels(string srcPath, JNode a)
        {
            var px = Load(srcPath, out int w, out int h);
            float gain = a.Num("gain", 2f), gamma = a.Num("gamma", 1f);
            var lut = new byte[256];
            for (int i = 0; i < 256; i++) lut[i] = (byte)Mathf.Clamp(Mathf.Pow(Mathf.Clamp01(i / 255f * gain), gamma) * 255f, 0f, 255f);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(lut[c.r], lut[c.g], lut[c.b], 255);
            }
            Save(OutPath(a.Str("out")), px, w, h);
        }

        private static void CropFrom(string path, JNode c)
        {
            var px = Load(path, out int w, out int h);
            Vector4 r = c.Vec4("rect", new Vector4(0, 0, w, h));
            int cx = (int)r.x, cy = (int)r.y, cw = (int)r.z, ch = (int)r.w;
            var outPx = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int sx = Mathf.Clamp(cx + x, 0, w - 1), sy = Mathf.Clamp(cy + y, 0, h - 1);
                outPx[x + y * cw] = px[sx + sy * w];
            }
            Save(OutPath(c.Str("out")), outPx, cw, ch);
        }

        /// <summary>Solve the 8-parameter homography mapping points a[i] → b[i].</summary>
        private static double[] Homography(Vector2[] a, Vector2[] b)
        {
            var m = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double x = a[i].x, y = a[i].y, u = b[i].x, v = b[i].y;
                int r = i * 2;
                m[r, 0] = x; m[r, 1] = y; m[r, 2] = 1; m[r, 3] = 0; m[r, 4] = 0; m[r, 5] = 0; m[r, 6] = -u * x; m[r, 7] = -u * y; m[r, 8] = u;
                m[r + 1, 0] = 0; m[r + 1, 1] = 0; m[r + 1, 2] = 0; m[r + 1, 3] = x; m[r + 1, 4] = y; m[r + 1, 5] = 1; m[r + 1, 6] = -v * x; m[r + 1, 7] = -v * y; m[r + 1, 8] = v;
            }
            for (int col = 0; col < 8; col++)
            {
                int piv = col;
                for (int r = col + 1; r < 8; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
                for (int k = 0; k < 9; k++) { var t = m[col, k]; m[col, k] = m[piv, k]; m[piv, k] = t; }
                for (int r = 0; r < 8; r++)
                {
                    if (r == col) continue;
                    double f = m[r, col] / m[col, col];
                    for (int k = col; k < 9; k++) m[r, k] -= f * m[col, k];
                }
            }
            var h = new double[8];
            for (int i = 0; i < 8; i++) h[i] = m[i, 8] / m[i, i];
            return h;
        }

        // ------------------------------------------------------------------ metadata

        public static Dictionary<string, PartMeta> LoadMeta()
        {
            var dict = new Dictionary<string, PartMeta>();
            if (!File.Exists(MetaPath)) return dict;
            var j = JNode.Parse(File.ReadAllText(MetaPath));
            foreach (var kv in j.Pairs())
            {
                var v = kv.Value;
                dict[kv.Key] = new PartMeta
                {
                    Name = kv.Key,
                    Pivot = v.Vec2("pivot", new Vector2(0.5f, 0.5f)),
                    Crop = new RectInt(v["crop"][0].AsInt(), v["crop"][1].AsInt(), v["crop"][2].AsInt(), v["crop"][3].AsInt()),
                    Rotation = v.Num("rotation", 0f),
                    Tip = v.Vec2("tip", new Vector2(float.NaN, float.NaN)),
                    Elbow = v.Vec2("elbow", new Vector2(float.NaN, float.NaN))
                };
            }
            return dict;
        }

        private static void SaveMeta(Dictionary<string, PartMeta> meta)
        {
            var sb = new StringBuilder("{\n");
            var keys = new List<string>(meta.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                var m = meta[keys[i]];
                string extra = (float.IsNaN(m.Tip.x) ? "" : $", \"tip\": [{F(m.Tip.x)}, {F(m.Tip.y)}], \"base\": [{F(m.Tip.x)}, {F(m.Tip.y)}]") + (float.IsNaN(m.Elbow.x) ? "" : $", \"elbow\": [{F(m.Elbow.x)}, {F(m.Elbow.y)}]");
                sb.Append($"  \"{m.Name}\": {{\"pivot\": [{F(m.Pivot.x)}, {F(m.Pivot.y)}], \"crop\": [{m.Crop.x}, {m.Crop.y}, {m.Crop.width}, {m.Crop.height}], \"rotation\": {F(m.Rotation)}{extra}}}");
                sb.Append(i < keys.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("}\n");
            File.WriteAllText(MetaPath, sb.ToString());
        }

        private static string F(float f) => f.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

        private static string MiniWrite(object o)
        {
            switch (o)
            {
                case null: return "null";
                case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case bool b: return b ? "true" : "false";
                case double d: return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case Dictionary<string, object> dict:
                {
                    var sb = new StringBuilder("{");
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(MiniWrite(kv.Key)).Append(':').Append(MiniWrite(kv.Value));
                    }
                    return sb.Append('}').ToString();
                }
                case List<object> list:
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(MiniWrite(list[i]));
                    }
                    return sb.Append(']').ToString();
                }
                default: return "\"" + o + "\"";
            }
        }
    }
}
