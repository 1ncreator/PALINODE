using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// One visual element of a cinematic shot. Root layers are positioned by the Stage's virtual camera with parallax
    /// (depth); child layers are attached to a parent layer in the parent's image pixel space.
    /// </summary>
    public sealed class StageLayer : MonoBehaviour
    {
        public string Id { get; private set; }
        public int Group { get; private set; }
        public StageLayer Parent { get; private set; }
        public float Depth { get; set; } = 1f;
        public int Order { get; private set; }

        /// <summary>Root: stage units relative to screen centre. Child: local units in the parent's space.</summary>
        public Vector2 BasePos { get; set; }
        public Vector2 BaseScale { get; set; } = Vector2.one;
        public float BaseRot { get; set; }

        /// <summary>Additive offsets for shakes, bobbing, trembling (same space as BasePos).</summary>
        public Vector2 Offset { get; set; }
        public float RotOffset { get; set; }

        public float Alpha { get; set; } = 1f;
        public float GroupAlpha { get; set; } = 1f;
        public Color Tint { get; set; } = Color.white;

        public SpriteRenderer Renderer { get; private set; }

        /// <summary>Pixel-to-local mapping helpers for children (valid when the layer has a sprite).</summary>
        public Sprite Sprite => Renderer != null ? Renderer.sprite : null;

        public float EffectiveAlpha => Alpha * (Parent != null ? Parent.EffectiveAlpha : GroupAlpha);

        private readonly List<IStageAlpha> _alphaTargets = new List<IStageAlpha>();
        private readonly List<StageLayer> _children = new List<StageLayer>();

        public IReadOnlyList<StageLayer> Children => _children;

        public void Init(string id, int group, StageLayer parent, int order)
        {
            Id = id;
            Group = group;
            Parent = parent;
            Order = order;
            parent?._children.Add(this);
        }

        public SpriteRenderer EnsureRenderer()
        {
            if (Renderer == null) Renderer = gameObject.AddComponent<SpriteRenderer>();
            return Renderer;
        }

        public void RegisterAlphaTarget(IStageAlpha target)
        {
            if (!_alphaTargets.Contains(target)) _alphaTargets.Add(target);
        }

        public void SetSortingOrder(int order)
        {
            Order = order;
            if (Renderer != null) Renderer.sortingOrder = order;
        }

        /// <summary>Convert a pixel coordinate of this layer's sprite (top-left origin) to local units.</summary>
        public Vector2 PixelToLocal(Vector2 px)
        {
            var s = Sprite;
            if (s == null) return px * 0.01f;
            float ppu = s.pixelsPerUnit;
            float h = s.rect.height;
            return new Vector2((px.x - s.pivot.x) / ppu, (h - px.y - s.pivot.y) / ppu);
        }

        /// <summary>Pixel size of this layer's sprite in local units.</summary>
        public float PixelsPerUnit => Sprite != null ? Sprite.pixelsPerUnit : 100f;

        public Vector3 PixelToWorld(Vector2 px) => transform.TransformPoint(PixelToLocal(px));

        public Vector2 WorldToPixel(Vector3 world)
        {
            Vector2 local = transform.InverseTransformPoint(world);
            var s = Sprite;
            if (s == null) return local * 100f;
            float ppu = s.pixelsPerUnit;
            return new Vector2(local.x * ppu + s.pivot.x, s.rect.height - (local.y * ppu + s.pivot.y));
        }

        public void ApplyChildTransform()
        {
            transform.localPosition = new Vector3(BasePos.x + Offset.x, BasePos.y + Offset.y, 0f);
            transform.localRotation = Quaternion.Euler(0f, 0f, BaseRot + RotOffset);
            transform.localScale = new Vector3(BaseScale.x, BaseScale.y, 1f);
        }

        public void ApplyAlpha()
        {
            float a = EffectiveAlpha;
            if (Renderer != null)
            {
                var c = Tint;
                c.a *= a;
                Renderer.color = c;
                Renderer.enabled = c.a > 0.001f;
            }
            for (int i = 0; i < _alphaTargets.Count; i++) _alphaTargets[i].SetStageAlpha(a);
        }

        private void OnDestroy()
        {
            Parent?._children.Remove(this);
        }
    }
}
