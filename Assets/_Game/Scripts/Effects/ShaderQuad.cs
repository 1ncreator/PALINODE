using Palinode.Core;
using Palinode.Cutscene;
using Palinode.UI;
using UnityEngine;

namespace Palinode.Effects
{
    /// <summary>
    /// A rectangle rendered with a procedural shader (rain on glass, drifting fog, soft glow).
    /// "size" is in parent pixels; material properties can be overridden with "props": {"_Name": value}.
    /// </summary>
    public sealed class ShaderQuad : MonoBehaviour, IStageAlpha
    {
        private SpriteRenderer _sr;
        private Material _material;
        private Color _color;

        public Material Material => _material;

        public void Build(StageLayer layer, JNode spec, GameConfig config)
        {
            Make(layer, spec, config, spec.Str("shader", "Fog"));
            if (spec.Has("props"))
            {
                foreach (var kv in spec["props"].Pairs())
                {
                    if (kv.Value.IsNumber) _material.SetFloat(kv.Key, kv.Value.AsFloat());
                    else if (kv.Value.IsString && ColorUtility.TryParseHtmlString(kv.Value.AsString(), out var c)) _material.SetColor(kv.Key, c);
                    else if (kv.Value.IsArray) _material.SetVector(kv.Key, new Vector4(kv.Value[0].AsFloat(), kv.Value[1].AsFloat(), kv.Value[2].AsFloat(), kv.Value[3].AsFloat()));
                }
            }
        }

        public void BuildGlow(StageLayer layer, JNode spec, GameConfig config)
        {
            Make(layer, spec, config, "GlowAdd");
            _sr.sprite = ProceduralSprites.SoftDot(128, 0.0f);
        }

        private void Make(StageLayer layer, JNode spec, GameConfig config, string materialName)
        {
            var go = new GameObject("Quad");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _sr = go.AddComponent<SpriteRenderer>();
            _sr.sprite = ProceduralSprites.White();
            _sr.drawMode = SpriteDrawMode.Simple; // keeps 0..1 UVs across the quad for procedural shaders
            Vector2 quad = spec.Vec2("size", new Vector2(400f, 300f)) * 0.01f;
            go.transform.localScale = new Vector3(quad.x, quad.y, 1f);
            _sr.sortingOrder = layer.Order;
            var src = config.Material(materialName);
            _material = src != null ? new Material(src) : null;
            _sr.sharedMaterial = _material;
            _color = spec.Color("color", Color.white);
            _sr.color = _color;
            if (_material != null)
            {
                _material.SetFloat("_Seed", Random.value * 10f);
                _material.SetVector("_QuadSize", new Vector4(quad.x, quad.y, 0f, 0f));
            }
            layer.RegisterAlphaTarget(this);
        }

        public void SetStageAlpha(float alpha)
        {
            if (_sr == null) return;
            var c = _color;
            c.a *= alpha;
            _sr.color = c;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
