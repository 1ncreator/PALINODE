using TMPro;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// One handwritten line. Glyphs are revealed left-to-right (the glyph being written is wiped in, not faded),
    /// and every glyph gets its own small baseline drift, rotation, slant and size, so a font reads as a hand.
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public sealed class HandLine : MonoBehaviour
    {
        private TextMeshPro _text;
        private float _reveal = float.MaxValue;
        private float _alpha = 1f;
        private int _seed;
        private float _jitterDeg = 2.2f;
        private float _jitterY = 0.018f;
        private float _sizeJitter = 0.06f;
        private float _slantJitter = 0.08f;

        public TextMeshPro Text => _text ??= GetComponent<TextMeshPro>();

        /// <summary>Characters revealed; the fractional part wipes in the current glyph from the left.</summary>
        public float Reveal
        {
            get => _reveal;
            set => _reveal = value;
        }

        public float Alpha
        {
            get => _alpha;
            set => _alpha = value;
        }

        public int CharacterCount
        {
            get
            {
                Text.ForceMeshUpdate();
                return Text.textInfo.characterCount;
            }
        }

        public void Setup(int seed, float jitterDeg, float jitterYUnits, float sizeJitter = 0.06f, float slantJitter = 0.08f)
        {
            _seed = seed;
            _jitterDeg = jitterDeg;
            _jitterY = jitterYUnits;
            _sizeJitter = sizeJitter;
            _slantJitter = slantJitter;
        }

        private float Hash(int i, int salt)
        {
            unchecked
            {
                int h = (i * 73856093) ^ (_seed * 19349663) ^ (salt * 83492791);
                h = (h << 13) ^ h;
                return 1f - ((h * (h * h * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f;
            }
        }

        private float BaselineDrift(int i) => Hash(i, 2) * _jitterY + Mathf.Sin(i * 0.9f + _seed) * _jitterY * 0.5f;

        /// <summary>Local-space bottom-right of character i near the baseline (end of that glyph).</summary>
        public Vector3 CharTipLocal(int i)
        {
            var ti = Text.textInfo;
            if (ti.characterCount == 0) return Vector3.zero;
            i = Mathf.Clamp(i, 0, ti.characterCount - 1);
            var c = ti.characterInfo[i];
            return new Vector3(c.bottomRight.x, c.baseLine + (c.ascender - c.baseLine) * 0.15f + BaselineDrift(i), 0f);
        }

        public Vector3 CharTipWorld(int i) => transform.TransformPoint(CharTipLocal(i));

        /// <summary>
        /// Where the nib is while glyph i is being written with progress k (0..1): it sweeps the glyph left→right
        /// and moves up and down through the letter body, like tracing strokes.
        /// </summary>
        public Vector3 PenPointLocal(int i, float k)
        {
            var ti = Text.textInfo;
            if (ti.characterCount == 0) return Vector3.zero;
            i = Mathf.Clamp(i, 0, ti.characterCount - 1);
            var c = ti.characterInfo[i];
            float left = c.bottomLeft.x, right = Mathf.Max(left + 0.001f, c.topRight.x);
            float body = c.isVisible ? Mathf.Max(0.01f, c.topRight.y - c.baseLine) : (c.ascender - c.baseLine) * 0.5f;
            float x = Mathf.Lerp(left, right, k);
            float strokes = 2f + Mathf.Abs(Hash(i, 7)) * 2f;
            float y = c.baseLine + BaselineDrift(i) + body * (0.5f + 0.45f * Mathf.Sin(k * Mathf.PI * strokes + Hash(i, 8) * 3f));
            if (!c.isVisible) y = c.baseLine + body * 1.1f; // spaces: pen travels above the paper line
            return new Vector3(x, y, 0f);
        }

        public Vector3 PenPointWorld(int i, float k) => transform.TransformPoint(PenPointLocal(i, k));

        public bool IsSpace(int i)
        {
            var ti = Text.textInfo;
            return i < 0 || i >= ti.characterCount || !ti.characterInfo[i].isVisible;
        }

        /// <summary>Bounds of visible glyphs in local space (x: left→right, y: cap-height band).</summary>
        public bool GlyphBounds(out float left, out float right, out float bottom, out float top)
        {
            // Band from the baseline to the average glyph top (not the font ascender, which in script fonts
            // includes room for flourishes) — the middle of this band is where a strike-through belongs.
            var ti = Text.textInfo;
            left = float.MaxValue; right = float.MinValue; bottom = 0f; top = 0f;
            int n = 0;
            for (int i = 0; i < ti.characterCount; i++)
            {
                var c = ti.characterInfo[i];
                if (!c.isVisible) continue;
                n++;
                left = Mathf.Min(left, c.bottomLeft.x);
                right = Mathf.Max(right, c.topRight.x);
                bottom += c.baseLine + BaselineDrift(i);
                top += Mathf.Min(c.topRight.y, c.baseLine + (c.ascender - c.baseLine) * 0.8f) + BaselineDrift(i);
            }
            if (n == 0) return false;
            bottom /= n;
            top /= n;
            return true;
        }

        private void LateUpdate()
        {
            var t = Text;
            bool all = _reveal >= float.MaxValue * 0.5f;
            t.maxVisibleCharacters = all ? 99999 : Mathf.CeilToInt(_reveal);
            t.ForceMeshUpdate();
            var ti = t.textInfo;
            for (int i = 0; i < ti.characterCount; i++)
            {
                var c = ti.characterInfo[i];
                if (!c.isVisible) continue;
                int mi = c.materialReferenceIndex;
                int vi = c.vertexIndex;
                var verts = ti.meshInfo[mi].vertices;
                var uvs = ti.meshInfo[mi].uvs0;
                var cols = ti.meshInfo[mi].colors32;

                // Left-to-right wipe of the glyph being written (vertices: 0 BL, 1 TL, 2 TR, 3 BR).
                float k = all ? 1f : Mathf.Clamp01(_reveal - i);
                if (k < 1f)
                {
                    float x0 = verts[vi].x, x1 = verts[vi + 3].x;
                    float u0 = uvs[vi].x, u1 = uvs[vi + 3].x;
                    float xk = Mathf.Lerp(x0, x1, k), uk = Mathf.Lerp(u0, u1, k);
                    verts[vi + 2].x = xk; verts[vi + 3].x = xk;
                    var a = uvs[vi + 2]; a.x = uk; uvs[vi + 2] = a;
                    var b = uvs[vi + 3]; b.x = uk; uvs[vi + 3] = b;
                }

                Vector3 pivot = new Vector3((verts[vi].x + verts[vi + 3].x) * 0.5f, verts[vi].y, 0f);
                float rot = Hash(i, 1) * _jitterDeg;
                float size = 1f + Hash(i, 4) * _sizeJitter;
                float slant = Hash(i, 5) * _slantJitter;
                float dy = BaselineDrift(i);
                Quaternion q = Quaternion.Euler(0f, 0f, rot);
                for (int n = 0; n < 4; n++)
                {
                    Vector3 p = (verts[vi + n] - pivot) * size;
                    p.x += p.y * slant;
                    verts[vi + n] = q * p + pivot + new Vector3(0f, dy, 0f);
                }

                float charAlpha = (k > 0f ? 1f : 0f) * _alpha * (0.9f + 0.1f * (Hash(i, 3) * 0.5f + 0.5f));
                for (int n = 0; n < 4; n++)
                {
                    var col = cols[vi + n];
                    col.a = (byte)(t.color.a * 255f * charAlpha);
                    cols[vi + n] = col;
                }
            }
            t.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32 | TMP_VertexDataUpdateFlags.Uv0);
        }
    }
}
