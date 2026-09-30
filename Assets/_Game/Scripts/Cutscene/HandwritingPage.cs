using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.UI;
using TMPro;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// A sheet of paper on which lines are handwritten, struck through and corrected.
    /// Layout is expressed in the parent image's pixels: "size", "top", "left", "lineHeight", "fontPx".
    /// </summary>
    public sealed class HandwritingPage : MonoBehaviour, IStageAlpha
    {
        private sealed class LineState
        {
            public HandLine Line;
            public string Text;
            public readonly List<LineRenderer> Marks = new List<LineRenderer>();
            public readonly List<HandLine> Inserts = new List<HandLine>();
            public readonly List<SpriteRenderer> Dots = new List<SpriteRenderer>();
        }

        private StageLayer _layer;
        private GameConfig _config;
        private Vector2 _sizePx;
        private float _topPx, _leftPx, _lineHeightPx, _fontPx;
        private Color _ink;
        private readonly Dictionary<int, LineState> _lines = new Dictionary<int, LineState>();
        private float _alpha = 1f;
        private int _order;

        public float LineHeightPx => _lineHeightPx;

        public void Build(StageLayer layer, JNode spec, GameConfig config)
        {
            _layer = layer;
            _config = config;
            _sizePx = spec.Vec2("size", new Vector2(460f, 560f));
            _topPx = spec.Num("top", 50f);
            _leftPx = spec.Num("left", 24f);
            _lineHeightPx = spec.Num("lineHeight", 62f);
            _fontPx = spec.Num("fontPx", 42f);
            _ink = spec.Color("color", new Color(0.11f, 0.1f, 0.09f, 0.93f));
            _order = layer.Order;
            _fontId = spec.Str("font", "hand_bad");
            _inkSpec = spec["ink"];
            _jitter = spec.Num("jitter", 1f);
            layer.RegisterAlphaTarget(this);
        }

        private string _fontId = "hand_bad";
        private JNode _inkSpec;
        private float _jitter = 1f;
        private static readonly Dictionary<string, Material> InkMaterials = new Dictionary<string, Material>();

        /// <summary>Instance of the font's SDF material with the ink shader (pen or graphite).</summary>
        private Material InkMaterial(TMP_FontAsset font, bool pencil)
        {
            string key = font.name + (pencil ? "#pencil" : "#pen") + "#" + _inkSpec.ToString().GetHashCode();
            if (InkMaterials.TryGetValue(key, out var m) && m != null) return m;
            var ink = _config.Material("InkSDF");
            if (ink == null) return font.material;
            m = new Material(font.material) { name = key, shader = ink.shader };
            var s = _inkSpec;
            m.SetFloat("_InkMode", pencil ? 1f : 0f);
            m.SetFloat("_InkWeight", pencil ? s.Num("pencilWeight", 0.1f) : s.Num("weight", 0.07f));
            m.SetFloat("_PressureScale", s.Num("pressureScale", 3f));
            m.SetFloat("_PressureAmount", pencil ? s.Num("pencilPressure", 0.06f) : s.Num("pressure", 0.14f));
            m.SetFloat("_EdgeRough", pencil ? s.Num("pencilRough", 0.07f) : s.Num("rough", 0.035f));
            m.SetFloat("_Bleed", pencil ? 0f : s.Num("bleed", 0.07f));
            m.SetFloat("_BleedAlpha", pencil ? 0f : s.Num("bleedAlpha", 0.22f));
            m.SetFloat("_Rim", pencil ? 0f : s.Num("rim", 0.35f));
            m.SetFloat("_Grain", pencil ? s.Num("grain", 0.4f) : 0f);
            m.SetFloat("_GrainScale", s.Num("grainScale", 110f));
            InkMaterials[key] = m;
            return m;
        }

        private void ApplyFont(TextMeshPro tmp, bool pencil)
        {
            var font = _config.Font(_fontId) ?? _config.HandwritingFont;
            tmp.font = font;
            tmp.fontSharedMaterial = InkMaterial(font, pencil);
        }

        private LineState GetLine(int index) => _lines.TryGetValue(index, out var s) ? s : null;

        private float LineY(int index) => (_sizePx.y * 0.5f - _topPx - index * _lineHeightPx) * 0.01f;

        public HandLine CreateLine(int index, string text, string align, float fontScale, Color? color)
        {
            if (_lines.TryGetValue(index, out var existing)) RemoveLine(existing);

            var go = new GameObject("Line" + index);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            ApplyFont(tmp, false);
            tmp.wordSpacing = _inkSpec.Num("wordSpacing", 22f);
            tmp.fontSize = _fontPx * fontScale / 10f;
            tmp.color = color ?? _ink;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.sortingOrder = _order + 2;
            tmp.text = text;
            var rt = tmp.rectTransform;
            float w = (_sizePx.x - _leftPx * 2f) * 0.01f;
            rt.sizeDelta = new Vector2(w, _lineHeightPx * 0.01f);
            bool center = align == "center";
            tmp.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            rt.pivot = new Vector2(center ? 0.5f : 0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            go.transform.localPosition = new Vector3(center ? 0f : -_sizePx.x * 0.005f + _leftPx * 0.01f, LineY(index), 0f);

            // Shrink long lines (Russian strings run longer) so they stay on the sheet.
            tmp.ForceMeshUpdate();
            float pref = tmp.preferredWidth;
            if (pref > w) tmp.fontSize *= w / pref;

            var line = go.AddComponent<HandLine>();
            line.Setup(index * 31 + 7, 2.2f * _jitter, tmp.fontSize * 0.011f * _jitter, 0.07f * _jitter, 0.07f * _jitter);
            line.Reveal = 0f;
            line.Alpha = _alpha;
            _lines[index] = new LineState { Line = line, Text = text };
            return line;
        }

        private void RemoveLine(LineState s)
        {
            if (s.Line != null) Destroy(s.Line.gameObject);
            foreach (var m in s.Marks) if (m != null) Destroy(m.gameObject);
            foreach (var i in s.Inserts) if (i != null) Destroy(i.gameObject);
            foreach (var d in s.Dots) if (d != null) Destroy(d.gameObject);
        }

        /// <summary>Write text on a line, glyph by glyph, with the hand (optional) following the pen tip.</summary>
        public IEnumerator Write(int index, string text, float cps, string align, float fontScale, HandFollower hand, Color? color = null)
        {
            var line = CreateLine(index, text, align, fontScale, color);
            int count = line.CharacterCount;
            if (hand != null)
            {
                // Approach lifted, then land on the first glyph.
                hand.SetContact(false);
                hand.Smooth = 0.12f;
                hand.SetTarget(line.PenPointWorld(0, 0f));
                float w = 0f;
                while (w < 0.25f) { w += Time.deltaTime; yield return null; }
                hand.Smooth = 0.012f;
                hand.Writing = true;
            }
            float reveal = 0f;
            string s = line.Text.text;
            while (reveal < count)
            {
                int ci = Mathf.Clamp(Mathf.FloorToInt(reveal), 0, count - 1);
                char ch = ci < s.Length ? s[ci] : 'a';
                bool space = line.IsSpace(ci);
                float speed = cps * (space ? 2.2f : ch == '.' || ch == ',' ? 1.8f : 1f);
                reveal += Time.deltaTime * speed;
                line.Reveal = reveal;
                if (hand != null)
                {
                    hand.SetContact(!space);
                    int cj = Mathf.Clamp(Mathf.FloorToInt(reveal), 0, count - 1);
                    hand.SetTarget(line.PenPointWorld(cj, Mathf.Clamp01(reveal - cj)));
                }
                yield return null;
            }
            line.Reveal = count + 1;
            if (hand != null)
            {
                hand.SetTarget(line.PenPointWorld(count - 1, 1f));
                hand.Writing = false;
                hand.SetContact(false);
                hand.Smooth = 0.06f;
            }
        }

        /// <summary>Animated ink stroke through the whole line.</summary>
        public IEnumerator Strike(int index, float duration, HandFollower hand, Color? color = null, float widthPx = 4.5f)
        {
            var st = GetLine(index);
            if (st == null) yield break;
            st.Line.Text.ForceMeshUpdate();
            if (!st.Line.GlyphBounds(out float l, out float r, out float b, out float t)) yield break;
            float y = Mathf.Lerp(b, t, 0.5f);
            float pad = 0.08f;
            var lr = CreateStroke(st.Line.transform, "Strike", color ?? _ink, widthPx);
            st.Marks.Add(lr);
            const int n = 28;
            var pts = new Vector3[n];
            float seed = index * 1.7f;
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)(n - 1);
                float x = Mathf.Lerp(l - pad, r + pad * 0.6f, k);
                float wob = Mathf.Sin(k * 9f + seed) * 0.012f + Mathf.Sin(k * 23f + seed * 2f) * 0.006f + (k - 0.5f) * 0.03f;
                pts[i] = new Vector3(x, y + wob, 0f);
            }
            if (hand != null)
            {
                // Travel lifted to the start of the line, land, then drag the stroke to the end.
                hand.SetContact(false);
                hand.Smooth = 0.1f;
                hand.SetTarget(st.Line.transform.TransformPoint(pts[0]));
                float w = 0f;
                while (w < 0.35f) { w += Time.deltaTime; yield return null; }
                hand.Smooth = 0.012f;
                hand.SetContact(true);
                hand.Writing = true;
                w = 0f;
                while (w < 0.08f) { w += Time.deltaTime; yield return null; }
            }
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / duration);
                k = k * k * (3f - 2f * k);
                SetPartial(lr, pts, k);
                if (hand != null) hand.SetTarget(st.Line.transform.TransformPoint(Interp(pts, k)));
                yield return null;
            }
            SetPartial(lr, pts, 1f);
            if (hand != null) { hand.Writing = false; hand.SetContact(false); hand.Smooth = 0.06f; }
        }

        /// <summary>Proof-reader's insertion: caret under the gap before the anchor word, then the word above it.</summary>
        public IEnumerator Insert(int index, string word, string anchorWord, Color color, float cps, HandFollower hand)
        {
            var st = GetLine(index);
            if (st == null) yield break;
            var tmp = st.Line.Text;
            tmp.ForceMeshUpdate();
            string text = tmp.text;
            int at = string.IsNullOrEmpty(anchorWord) ? -1 : text.IndexOf(anchorWord, System.StringComparison.Ordinal);
            if (at < 0) at = Mathf.Max(0, text.LastIndexOf(' ') + 1);
            var ti = tmp.textInfo;
            var cAnchor = ti.characterInfo[Mathf.Clamp(at, 0, ti.characterCount - 1)];
            var cPrev = ti.characterInfo[Mathf.Clamp(at - 2, 0, ti.characterCount - 1)];
            float gapX = at >= 2 ? (cPrev.topRight.x + cAnchor.bottomLeft.x) * 0.5f : cAnchor.bottomLeft.x - 0.04f;
            float baseY = cAnchor.baseLine;
            float capTop = Mathf.Min(cAnchor.topRight.y, cAnchor.baseLine + (cAnchor.ascender - cAnchor.baseLine) * 0.8f);

            // Caret "^" drawn in two strokes, apex slightly above the baseline.
            float cw = _fontPx * 0.0028f, ch = _fontPx * 0.0045f;
            var caret = CreateStroke(st.Line.transform, "Caret", color, 3.6f);
            st.Marks.Add(caret);
            var pts = new[]
            {
                new Vector3(gapX - cw, baseY - ch * 0.8f, 0f),
                new Vector3(gapX, baseY + ch * 0.35f, 0f),
                new Vector3(gapX + cw * 1.05f, baseY - ch * 0.75f, 0f)
            };
            if (hand != null)
            {
                hand.SetContact(false);
                hand.Smooth = 0.1f;
                hand.SetTarget(st.Line.transform.TransformPoint(pts[0]));
                float w = 0f;
                while (w < 0.4f) { w += Time.deltaTime; yield return null; }
                hand.Smooth = 0.012f;
                hand.SetContact(true);
                hand.Writing = true;
            }
            float time = 0f, dur = 0.55f;
            while (time < dur)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / dur);
                SetPartial(caret, pts, k);
                if (hand != null) hand.SetTarget(st.Line.transform.TransformPoint(Interp(pts, k)));
                yield return null;
            }
            SetPartial(caret, pts, 1f);
            if (hand != null) hand.SetContact(false);

            // The inserted word above the line, centred on the caret.
            var go = new GameObject("Insert");
            go.layer = gameObject.layer;
            go.transform.SetParent(st.Line.transform, false);
            var wt = go.AddComponent<TextMeshPro>();
            ApplyFont(wt, true); // red pencil: graphite texture, not ink
            wt.fontSize = tmp.fontSize * 0.9f;
            wt.color = color;
            wt.textWrappingMode = TextWrappingModes.NoWrap;
            wt.alignment = TextAlignmentOptions.Center;
            wt.sortingOrder = _order + 3;
            wt.text = word;
            wt.rectTransform.sizeDelta = new Vector2(4f, _lineHeightPx * 0.01f);
            wt.rectTransform.pivot = new Vector2(0.5f, 0f);
            go.transform.localPosition = new Vector3(gapX, capTop + ch * 0.25f - _lineHeightPx * 0.004f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
            var il = go.AddComponent<HandLine>();
            il.Setup(911 + index, 3f * _jitter, wt.fontSize * 0.014f * _jitter, 0.08f * _jitter, 0.08f * _jitter);
            il.Reveal = 0f;
            il.Alpha = _alpha;
            st.Inserts.Add(il);
            int count = il.CharacterCount;
            wt.ForceMeshUpdate();
            if (hand != null)
            {
                hand.Smooth = 0.08f;
                hand.SetTarget(il.PenPointWorld(0, 0f));
                float w = 0f;
                while (w < 0.25f) { w += Time.deltaTime; yield return null; }
                hand.Smooth = 0.012f;
                hand.SetContact(true);
            }
            float reveal = 0f;
            while (reveal < count)
            {
                reveal += Time.deltaTime * cps;
                il.Reveal = reveal;
                if (hand != null)
                {
                    int cj = Mathf.Clamp(Mathf.FloorToInt(reveal), 0, count - 1);
                    hand.SetTarget(il.PenPointWorld(cj, Mathf.Clamp01(reveal - cj)));
                }
                yield return null;
            }
            il.Reveal = count + 1;
            if (hand != null) { hand.Writing = false; hand.SetContact(false); hand.Smooth = 0.06f; }
        }

        /// <summary>Firm dot: at the end of the line ("lineEnd") or after the last inserted word ("insert").</summary>
        public IEnumerator Dot(int index, string at, Color color, float sizePx, HandFollower hand)
        {
            var st = GetLine(index);
            if (st == null) yield break;
            Transform parent;
            Vector3 local;
            if (at == "insert" && st.Inserts.Count > 0)
            {
                var il = st.Inserts[st.Inserts.Count - 1];
                il.Text.ForceMeshUpdate();
                parent = il.transform;
                var c = il.Text.textInfo.characterInfo[il.Text.textInfo.characterCount - 1];
                local = new Vector3(c.topRight.x + 0.06f, c.baseLine + 0.03f, 0f);
            }
            else
            {
                st.Line.Text.ForceMeshUpdate();
                parent = st.Line.transform;
                var ti = st.Line.Text.textInfo;
                var c = ti.characterInfo[ti.characterCount - 1];
                // Over the final full stop if the line ends with one.
                local = c.character == '.'
                    ? new Vector3((c.bottomLeft.x + c.topRight.x) * 0.5f, c.baseLine + 0.035f, 0f)
                    : new Vector3(c.topRight.x + 0.08f, c.baseLine + 0.03f, 0f);
            }
            Vector3 world = parent.TransformPoint(local);
            if (hand != null)
            {
                hand.SetContact(false);
                hand.Smooth = 0.08f;
                hand.SetTarget(world);
                float w = 0f;
                while (w < 0.4f) { w += Time.deltaTime; yield return null; }
                hand.Smooth = 0.01f;
                hand.SetContact(true); // firm press
                w = 0f;
                while (w < 0.1f) { w += Time.deltaTime; yield return null; }
            }
            var go = new GameObject("Dot");
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralSprites.SoftDot(64, 0.75f);
            sr.color = color;
            sr.sortingOrder = _order + 4;
            var mat = _config.Material("SpriteUnlit");
            if (mat != null) sr.sharedMaterial = mat;
            st.Dots.Add(sr);
            float target = sizePx * 0.01f;
            float t = 0f;
            while (t < 0.12f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.12f);
                go.transform.localScale = Vector3.one * target * (0.4f + 0.75f * k - 0.15f * k * k);
                yield return null;
            }
            go.transform.localScale = Vector3.one * target;
            if (hand != null) { hand.SetContact(false); hand.Smooth = 0.3f; }
        }

        public Vector3 LineStartWorld(int index)
        {
            var st = GetLine(index);
            if (st != null) return st.Line.CharTipWorld(0);
            return transform.TransformPoint(new Vector3(-_sizePx.x * 0.005f + _leftPx * 0.01f, LineY(index), 0f));
        }

        public Vector3 LineEndWorld(int index)
        {
            var st = GetLine(index);
            if (st == null) return LineStartWorld(index);
            st.Line.Text.ForceMeshUpdate();
            return st.Line.CharTipWorld(st.Line.Text.textInfo.characterCount - 1);
        }

        public bool HasLine(int index) => _lines.ContainsKey(index);

        /// <summary>Instantly place finished text (used when a shot starts with already-written lines).</summary>
        public void Preset(int index, string text, string align, float fontScale, bool struck)
        {
            var line = CreateLine(index, text, align, fontScale, null);
            line.Reveal = float.MaxValue;
            if (!struck) return;
            line.Text.ForceMeshUpdate();
            if (!line.GlyphBounds(out float l, out float r, out float b, out float t)) return;
            var lr = CreateStroke(line.transform, "Strike", _ink, 4.5f);
            lr.positionCount = 2;
            float y = Mathf.Lerp(b, t, 0.5f);
            lr.SetPositions(new[] { new Vector3(l - 0.08f, y, 0f), new Vector3(r + 0.05f, y + 0.01f, 0f) });
            _lines[index].Marks.Add(lr);
        }

        private LineRenderer CreateStroke(Transform parent, string name, Color color, float widthPx)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.alignment = LineAlignment.TransformZ;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 3;
            lr.numCornerVertices = 2;
            float w = widthPx * 0.01f * transform.lossyScale.x;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, w * 0.55f), new Keyframe(0.12f, w), new Keyframe(0.85f, w * 0.95f), new Keyframe(1f, w * 0.5f));
            var mat = _config.Material("SpriteUnlit");
            if (mat != null) lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = new Color(color.r, color.g, color.b, color.a * _alpha);
            lr.sortingOrder = _order + 3;
            lr.positionCount = 0;
            return lr;
        }

        private static Vector3 Interp(Vector3[] pts, float k)
        {
            float f = k * (pts.Length - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, pts.Length - 2);
            return Vector3.Lerp(pts[i], pts[i + 1], f - i);
        }

        private static void SetPartial(LineRenderer lr, Vector3[] pts, float k)
        {
            float f = k * (pts.Length - 1);
            int full = Mathf.Clamp(Mathf.FloorToInt(f), 0, pts.Length - 1);
            int count = Mathf.Min(pts.Length, full + 2);
            lr.positionCount = count;
            for (int i = 0; i < count; i++) lr.SetPosition(i, pts[Mathf.Min(i, pts.Length - 1)]);
            if (count >= 2 && full < pts.Length - 1) lr.SetPosition(count - 1, Interp(pts, k));
        }

        public void SetStageAlpha(float alpha)
        {
            _alpha = alpha;
            foreach (var s in _lines.Values)
            {
                if (s.Line != null) s.Line.Alpha = alpha;
                foreach (var m in s.Marks)
                {
                    if (m == null) continue;
                    var c = m.startColor;
                    c.a = alpha * 0.95f;
                    m.startColor = m.endColor = c;
                }
                foreach (var i in s.Inserts) if (i != null) i.Alpha = alpha;
                foreach (var d in s.Dots)
                {
                    if (d == null) continue;
                    var c = d.color;
                    c.a = alpha;
                    d.color = c;
                }
            }
        }
    }
}
