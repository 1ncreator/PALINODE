using System.Collections.Generic;
using UnityEngine;

namespace Palinode.UI
{
    /// <summary>Small runtime-generated sprites (rounded panels, rings, soft dots) so UI needs no extra art.</summary>
    public static class ProceduralSprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite RoundedRect(int radius = 24)
        {
            string key = "rr" + radius;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = key };
            var px = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - c) - 2f);
                float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - c) - 2f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - d + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            float b = radius + 1;
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = key;
            Cache[key] = s;
            return s;
        }

        public static Sprite Ring(int size = 128, float thickness = 0.16f)
        {
            string key = "ring" + size + "_" + thickness;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color32[size * size];
            float c = size / 2f, outer = c - 1f, inner = outer - size * thickness;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s.name = key;
            Cache[key] = s;
            return s;
        }

        /// <summary>Soft round dot with gaussian falloff; used for ink dots, dust motes and glows.</summary>
        public static Sprite SoftDot(int size = 64, float hardness = 0.35f)
        {
            string key = "dot" + size + "_" + hardness;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c;
                float a = d >= 1f ? 0f : Mathf.Clamp01((1f - d) / Mathf.Max(0.01f, 1f - hardness));
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            s.name = key;
            Cache[key] = s;
            return s;
        }

        /// <summary>1x1 white sprite (PPU 1) for solid quads.</summary>
        public static Sprite White()
        {
            const string key = "white";
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = key };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            s.name = key;
            Cache[key] = s;
            return s;
        }

        /// <summary>Vertical gradient (alpha 1 at bottom → 0 at top), used for light shafts and fog banks.</summary>
        public static Sprite VerticalFade(int height = 128)
        {
            string key = "vfade" + height;
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color32[4 * height];
            for (int y = 0; y < height; y++)
            {
                float a = 1f - y / (float)(height - 1);
                a = a * a;
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 4, height), new Vector2(0.5f, 0f), 4f);
            s.name = key;
            Cache[key] = s;
            return s;
        }
    }
}
