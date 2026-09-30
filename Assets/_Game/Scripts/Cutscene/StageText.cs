using System.Collections;
using Palinode.Core;
using TMPro;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// World-space text on a stage layer (printed sheets, signage, engraved plaques).
    /// "style": "print" (letterpress stamp with uneven ink), "paint" (faded sign paint), "carve" (engraved stone).
    /// </summary>
    public sealed class StageText : MonoBehaviour, IStageAlpha
    {
        private TextMeshPro _text;
        private float _alpha = 1f;
        private float _reveal = 1f;
        private string _style;
        private int _seed;
        private Color _color;

        public TextMeshPro Text => _text;

        public void Build(StageLayer layer, JNode spec, GameConfig config) => Build(layer, spec, config, layer.Order + 1);

        public void Build(StageLayer layer, JNode spec, GameConfig config, int order)
        {
            var go = new GameObject("Text");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _text = go.AddComponent<TextMeshPro>();
            _text.font = config.Font(spec.Str("font", "mono"));
            _text.fontSize = spec.Num("fontPx", 40f) / 10f;
            _color = spec.Color("color", new Color(0.1f, 0.09f, 0.08f));
            _text.color = _color;
            _text.alignment = spec.Str("align", "center") == "left" ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.rectTransform.sizeDelta = new Vector2(spec.Num("width", 600f) * 0.01f, spec.Num("height", 200f) * 0.01f);
            _text.characterSpacing = spec.Num("spacing", 0f);
            _text.lineSpacing = spec.Num("lineSpacing", 0f);
            _text.sortingOrder = order;
            _style = spec.Str("style", "print");
            _seed = spec.Str("id", "t").GetHashCode();
            _reveal = spec.Num("reveal", 1f);
            if (spec.Has("text")) _text.text = spec.Str("text");
            if (_style == "carve" || _style == "paint")
            {
                _text.fontStyle = FontStyles.Bold;
            }
            if (_style == "carve")
            {
                // Engraved: light catches the lower lip of each groove (underlay offset down-right), letters stay dark.
                var m = new Material(_text.fontSharedMaterial);
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", spec.Color("engraveLight", new Color(0.95f, 0.9f, 0.8f, 0.45f)));
                m.SetFloat("_UnderlayOffsetX", 0.35f);
                m.SetFloat("_UnderlayOffsetY", -0.45f);
                m.SetFloat("_UnderlayDilate", 0.05f);
                m.SetFloat("_UnderlaySoftness", 0.2f);
                _text.fontSharedMaterial = m;
            }
            if (spec.Has("skew")) _text.transform.localRotation = Quaternion.Euler(0f, 0f, spec.Num("skew"));
            layer?.RegisterAlphaTarget(this);
        }

        public void SetText(string s) => _text.text = s;

        /// <summary>Stamp the text: quick press (scale) and ink coming through unevenly.</summary>
        public IEnumerator Stamp(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                _reveal = k;
                transform.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, Ease.Apply("outCubic", k));
                yield return null;
            }
            _reveal = 1f;
            transform.localScale = Vector3.one;
        }

        /// <summary>Slow emergence (ink soaking through / letters surfacing in stone).</summary>
        public IEnumerator Emerge(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _reveal = Mathf.Clamp01(t / duration);
                yield return null;
            }
            _reveal = 1f;
        }

        public void SetReveal(float r) => _reveal = r;

        public void SetStageAlpha(float alpha) => _alpha = alpha;

        private float Hash(int i, int salt)
        {
            unchecked
            {
                int h = (i * 374761393) ^ (_seed * 668265263) ^ (salt * 1103515245);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        private void LateUpdate()
        {
            if (_text == null) return;
            _text.ForceMeshUpdate();
            var ti = _text.textInfo;
            int n = ti.characterCount;
            for (int i = 0; i < n; i++)
            {
                var c = ti.characterInfo[i];
                if (!c.isVisible) continue;
                var cols = ti.meshInfo[c.materialReferenceIndex].colors32;
                var verts = ti.meshInfo[c.materialReferenceIndex].vertices;
                int vi = c.vertexIndex;
                float ink;
                float order = n > 1 ? i / (float)(n - 1) : 0f;
                switch (_style)
                {
                    case "carve":
                    case "paint":
                        // All letters surface together but unevenly.
                        ink = Mathf.Clamp01((_reveal * 1.4f - Hash(i, 1) * 0.4f)) * (_style == "carve" ? 0.92f + 0.08f * Hash(i, 2) : 0.75f + 0.25f * Hash(i, 2));
                        break;
                    default:
                        ink = Mathf.Clamp01(_reveal * 1.25f - Hash(i, 1) * 0.25f) * (0.72f + 0.28f * Hash(i, 2));
                        break;
                }
                float dy = (Hash(i, 3) - 0.5f) * 0.012f;
                float rot = (Hash(i, 4) - 0.5f) * (_style == "print" ? 1.2f : 0.6f);
                Vector3 mid = (verts[vi] + verts[vi + 2]) * 0.5f;
                var q = Quaternion.Euler(0f, 0f, rot);
                for (int k = 0; k < 4; k++)
                {
                    verts[vi + k] = q * (verts[vi + k] - mid) + mid + new Vector3(0f, dy, 0f);
                    var col = cols[vi + k];
                    col.a = (byte)(255f * _color.a * ink * _alpha * (0.9f + 0.1f * order));
                    cols[vi + k] = col;
                }
            }
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }
    }
}
