using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Palinode.Editor
{
    /// <summary>
    /// Control sheets for background removal: every processed sprite on black, white and magenta, plus ×3 zooms of
    /// its silhouette edge (top, left, right) on magenta. Output: Screenshots/ArtCheck/&lt;sprite&gt;.png.
    /// Batch: -executeMethod Palinode.Editor.ArtCheckSheets.GenerateBatch
    /// </summary>
    public static class ArtCheckSheets
    {
        public const string OutDir = "Screenshots/ArtCheck";
        private const int Cell = 520;
        private const int ZoomCell = 520;

        private static readonly Color32[] Backs =
        {
            new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255), new Color32(255, 0, 255, 255)
        };

        [MenuItem("Tools/PALINODE/Art Check Sheets")]
        public static void Generate()
        {
            Directory.CreateDirectory(OutDir);
            var skip = new HashSet<string> { "G_printhouse_albedo", "P2-05_print", "P2-05_print_portrait" };
            int n = 0;
            foreach (var f in Directory.GetFiles(ArtProcessor.OutDir, "*.png"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (skip.Contains(name)) continue;
                MakeSheet(f, Path.Combine(OutDir, name + ".png"));
                n++;
            }
            Debug.Log($"[PALINODE] Art check sheets: {n} → {OutDir}");
        }

        public static void GenerateBatch()
        {
            int code = 0;
            try { Generate(); }
            catch (Exception e) { Debug.LogError(e); code = 1; }
            EditorApplication.Exit(code);
        }

        private static Color32[] LoadTopDown(string path, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            w = tex.width; h = tex.height;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            var td = new Color32[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, (h - 1 - y) * w, td, y * w, w);
            return td;
        }

        private static void MakeSheet(string src, string dst)
        {
            var px = LoadTopDown(src, out int w, out int h);
            int sw = Cell * 3, sh = Cell + ZoomCell + 30;
            var sheet = new Color32[sw * sh];
            for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(40, 40, 40, 255);

            // Row 1: whole sprite on three backgrounds.
            float s = Mathf.Min((Cell - 10f) / w, (Cell - 10f) / h);
            for (int b = 0; b < 3; b++) Blit(px, w, h, 0, 0, w, h, sheet, sw, b * Cell + 5, 5, s, Backs[b], Cell - 10, Cell - 10);

            // Row 2: zooms around the silhouette (topmost, leftmost, rightmost opaque edge).
            FindEdges(px, w, h, out var top, out var left, out var right);
            var pts = new[] { top, left, right };
            int zw = ZoomCell / 3; // source window size for ×3
            for (int k = 0; k < 3; k++)
            {
                int cx = Mathf.Clamp(pts[k].x - zw / 2, 0, Mathf.Max(0, w - zw));
                int cy = Mathf.Clamp(pts[k].y - zw / 2, 0, Mathf.Max(0, h - zw));
                Blit(px, w, h, cx, cy, Mathf.Min(zw, w), Mathf.Min(zw, h), sheet, sw, k * Cell + 5, Cell + 25, 3f, Backs[k == 1 ? 0 : 2], ZoomCell - 10, ZoomCell - 10);
            }

            var outTex = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
            var bottomUp = new Color32[sheet.Length];
            for (int y = 0; y < sh; y++) Array.Copy(sheet, y * sw, bottomUp, (sh - 1 - y) * sw, sw);
            outTex.SetPixels32(bottomUp);
            outTex.Apply();
            File.WriteAllBytes(dst, outTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(outTex);
        }

        private static void FindEdges(Color32[] px, int w, int h, out Vector2Int top, out Vector2Int left, out Vector2Int right)
        {
            top = new Vector2Int(w / 2, h / 2); left = top; right = top;
            int minY = h, minX = w, maxX = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (px[x + y * w].a < 200) continue;
                if (y < minY) { minY = y; top = new Vector2Int(x, y); }
                if (x < minX) { minX = x; left = new Vector2Int(x, y); }
                if (x > maxX) { maxX = x; right = new Vector2Int(x, y); }
            }
        }

        private static void Blit(Color32[] src, int w, int h, int sx, int sy, int swid, int shei, Color32[] dst, int dw,
            int dx, int dy, float scale, Color32 back, int maxW, int maxH)
        {
            int ow = Mathf.Min(maxW, Mathf.RoundToInt(swid * scale)), oh = Mathf.Min(maxH, Mathf.RoundToInt(shei * scale));
            for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int ix = Mathf.Clamp(sx + (int)(x / scale), 0, w - 1), iy = Mathf.Clamp(sy + (int)(y / scale), 0, h - 1);
                var c = src[ix + iy * w];
                float a = c.a / 255f;
                dst[(dx + x) + (dy + y) * dw] = new Color32(
                    (byte)(c.r * a + back.r * (1 - a)), (byte)(c.g * a + back.g * (1 - a)), (byte)(c.b * a + back.b * (1 - a)), 255);
            }
        }
    }
}
