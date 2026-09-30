using Palinode.Core;
using TMPro;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>Small helpers to build the floor from code (sprites sized in cells, depth sorting).</summary>
    public static class FloorSprites
    {
        public const int BackgroundOrder = -30000;
        public const int FloorDecalOrder = -25000;
        public const int DoorOrder = -24000;
        public const int OverlayOrder = 25000;

        /// <summary>Depth by feet position: lower on screen = in front.</summary>
        public static int Order(float worldY) => Mathf.Clamp(Mathf.RoundToInt(-worldY * 100f), -20000, 20000);

        public static GameConfig Config => GameRoot.Instance != null ? GameRoot.Instance.Config : null;

        public static Material Lit => Config != null ? Config.Material("SpriteLit") : null;
        public static Material Unlit => Config != null ? Config.Material("SpriteUnlit") : null;
        public static Material Glow => Config != null ? Config.Material("GlowAdd") : null;
        public static Material Solid => Config != null ? Config.Material("Solid") : null;

        public static Sprite Get(string name) => string.IsNullOrEmpty(name) || Config == null ? null : Config.Sprite(name);

        /// <summary>A sprite renderer child whose sprite is <paramref name="widthCells"/> wide (0 = native size).</summary>
        public static SpriteRenderer Make(Transform parent, string name, Sprite sprite, float widthCells, int order, Material mat = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat != null ? mat : Lit;
            sr.sortingOrder = order;
            if (sprite != null && widthCells > 0f) go.transform.localScale = Vector3.one * ScaleFor(sprite, widthCells);
            return sr;
        }

        public static float ScaleFor(Sprite sprite, float widthCells) =>
            sprite == null ? 1f : widthCells / Mathf.Max(0.01f, sprite.bounds.size.x);

        /// <summary>Scale so the sprite's larger side is <paramref name="size"/> cells (icons of any proportions).</summary>
        public static float FitScale(Sprite sprite, float size) =>
            sprite == null ? 1f : size / Mathf.Max(0.01f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));

        public static float HeightScale(Sprite sprite, float heightCells) =>
            sprite == null ? 1f : heightCells / Mathf.Max(0.01f, sprite.bounds.size.y);

        public static TextMeshPro Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.sortingOrder = order;
            t.rectTransform.sizeDelta = new Vector2(20f, 3f);
            return t;
        }

        /// <summary>Quad of a solid colour (thin lines, telegraph strips, shadows).</summary>
        public static SpriteRenderer Quad(Transform parent, string name, Color color, Vector2 size, int order, Material mat = null)
        {
            var sr = Make(parent, name, UI.ProceduralSprites.White(), 0f, order, mat != null ? mat : Unlit);
            var s = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(size.x / s.x, size.y / s.y, 1f);
            sr.color = color;
            return sr;
        }

        /// <summary>Soft round shadow under an actor.</summary>
        public static SpriteRenderer Shadow(Transform parent, float width, float alpha = 0.45f)
        {
            var sr = Make(parent, "Shadow", UI.ProceduralSprites.SoftDot(64, 0.3f), 0f, FloorDecalOrder + 500, Unlit);
            var s = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(width / s.x, width * 0.4f / s.y, 1f);
            sr.color = new Color(0f, 0f, 0f, alpha);
            return sr;
        }
    }
}
