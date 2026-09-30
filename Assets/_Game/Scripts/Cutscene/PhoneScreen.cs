using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.UI;
using TMPro;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// Content drawn onto a phone screen inside an illustration: incoming call card or a chat thread.
    /// Size/positions are in the parent image's pixels; text uses PT Mono.
    /// </summary>
    public sealed class PhoneScreen : MonoBehaviour, IStageAlpha
    {
        private sealed class Item
        {
            public Transform Root;
            public SpriteRenderer Bubble;
            public TextMeshPro Text;
            public float Height;
            public float Appear;
            public Color BubbleColor;
            public Color TextColor;
        }

        private StageLayer _layer;
        private GameConfig _config;
        private Vector2 _size;
        private float _fontPx;
        private float _alpha = 1f;
        private int _order;
        private TextMeshPro _header;
        private TextMeshPro _callName;
        private TextMeshPro _callSub;
        private SpriteRenderer _pulse;
        private readonly List<Item> _items = new List<Item>();
        private Transform _content;
        private float _scroll;
        private bool _call;
        private Color _textColor;
        private Color _bubbleColor;

        public void Build(StageLayer layer, JNode spec, GameConfig config)
        {
            _layer = layer;
            _config = config;
            _size = spec.Vec2("size", new Vector2(150f, 390f)) * 0.01f;
            _fontPx = spec.Num("fontPx", 13f);
            _order = layer.Order;
            _textColor = spec.Color("textColor", new Color(0.12f, 0.15f, 0.2f));
            _bubbleColor = spec.Color("bubbleColor", new Color(0.86f, 0.9f, 0.95f, 0.96f));
            _content = new GameObject("Content").transform;
            _content.gameObject.layer = gameObject.layer;
            _content.SetParent(transform, false);
            layer.RegisterAlphaTarget(this);
        }

        private TextMeshPro MakeText(Transform parent, string text, float fontPx, Color color, TextAlignmentOptions align, float width)
        {
            var go = new GameObject("Text");
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.font = _config.MonoFont;
            t.fontSize = fontPx / 10f;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.sortingOrder = _order + 3;
            t.rectTransform.sizeDelta = new Vector2(width, 1f);
            t.text = text;
            return t;
        }

        public void SetHeader(string name)
        {
            if (_header == null)
            {
                _header = MakeText(transform, name, _fontPx * 1.15f, new Color(0.1f, 0.12f, 0.16f), TextAlignmentOptions.Center, _size.x);
                _header.fontStyle = FontStyles.Bold;
                _header.transform.localPosition = new Vector3(0f, _size.y * 0.5f - _fontPx * 0.028f, 0f);
                var line = new GameObject("HeaderRule");
                line.layer = gameObject.layer;
                line.transform.SetParent(transform, false);
                var sr = line.AddComponent<SpriteRenderer>();
                sr.sprite = ProceduralSprites.White();
                sr.color = new Color(0.2f, 0.25f, 0.32f, 0.35f);
                sr.sortingOrder = _order + 2;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = new Vector2(_size.x * 0.86f, 0.008f);
                line.transform.localPosition = new Vector3(0f, _size.y * 0.5f - _fontPx * 0.052f, 0f);
                ApplyAlpha(sr, 0.35f);
            }
            _header.text = name;
        }

        public IEnumerator ShowCall(string name, string sub)
        {
            _call = true;
            _callName = MakeText(transform, name, _fontPx * 2.4f, new Color(0.08f, 0.1f, 0.14f), TextAlignmentOptions.Center, _size.x * 1.2f);
            _callName.transform.localPosition = new Vector3(0f, _size.y * 0.16f, 0f);
            _callName.characterSpacing = 8f;
            _callSub = MakeText(transform, sub, _fontPx * 0.9f, new Color(0.25f, 0.3f, 0.38f), TextAlignmentOptions.Center, _size.x * 1.2f);
            _callSub.transform.localPosition = new Vector3(0f, _size.y * 0.06f, 0f);
            var p = new GameObject("Pulse");
            p.layer = gameObject.layer;
            p.transform.SetParent(transform, false);
            p.transform.localPosition = new Vector3(0f, -_size.y * 0.28f, 0f);
            _pulse = p.AddComponent<SpriteRenderer>();
            _pulse.sprite = ProceduralSprites.Ring(128, 0.1f);
            _pulse.sortingOrder = _order + 2;
            _pulse.color = new Color(0.3f, 0.7f, 0.45f, 0.8f);
            var mat = _config.Material("SpriteUnlit");
            if (mat != null) _pulse.sharedMaterial = mat;
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.35f);
                _callName.alpha = k * _alpha;
                _callSub.alpha = k * _alpha;
                yield return null;
            }
        }

        public IEnumerator AddMessage(string text, bool accent)
        {
            if (_header == null) SetHeader(string.Empty);
            var root = new GameObject("Msg" + _items.Count).transform;
            root.gameObject.layer = gameObject.layer;
            root.SetParent(_content, false);
            float maxW = _size.x * 0.78f;
            float pad = _fontPx * 0.006f;
            var txt = MakeText(root, text, _fontPx, _textColor, TextAlignmentOptions.TopLeft, maxW - pad * 2f);
            txt.ForceMeshUpdate();
            Vector2 pref = txt.GetPreferredValues(text, maxW - pad * 2f, 0f);
            float w = Mathf.Min(maxW, pref.x + pad * 2f + 0.01f);
            float h = pref.y + pad * 2f;
            txt.rectTransform.sizeDelta = new Vector2(w - pad * 2f, pref.y);
            txt.rectTransform.pivot = new Vector2(0f, 1f);
            txt.transform.localPosition = new Vector3(pad, -pad, 0f);

            var b = new GameObject("Bubble");
            b.layer = gameObject.layer;
            b.transform.SetParent(root, false);
            var sr = b.AddComponent<SpriteRenderer>();
            sr.sprite = ProceduralSprites.RoundedRect(6);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(w, h);
            sr.sortingOrder = _order + 2;
            var mat = _config.Material("SpriteUnlit");
            if (mat != null) sr.sharedMaterial = mat;
            b.transform.localPosition = new Vector3(w * 0.5f, -h * 0.5f, 0f);

            var item = new Item
            {
                Root = root,
                Bubble = sr,
                Text = txt,
                Height = h,
                BubbleColor = accent ? new Color(0.75f, 0.83f, 0.93f, 0.97f) : _bubbleColor,
                TextColor = _textColor
            };
            _items.Add(item);
            Layout();
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                item.Appear = Mathf.Clamp01(t / 0.3f);
                Layout();
                yield return null;
            }
            item.Appear = 1f;
            Layout();
        }

        private void Layout()
        {
            float top = _size.y * 0.5f - _fontPx * 0.075f;
            float gap = _fontPx * 0.006f;
            float y = top;
            float left = -_size.x * 0.5f + _size.x * 0.07f;
            float total = 0f;
            foreach (var it in _items) total += it.Height + gap;
            float bottomLimit = -_size.y * 0.5f + _fontPx * 0.03f;
            float overflow = Mathf.Max(0f, total - (top - bottomLimit));
            _scroll = Mathf.Lerp(_scroll, overflow, 0.25f);
            foreach (var it in _items)
            {
                float k = it.Appear;
                float e = 1f - (1f - k) * (1f - k);
                it.Root.localPosition = new Vector3(left, y + _scroll - (1f - e) * 0.03f, 0f);
                it.Root.localScale = Vector3.one * (0.92f + 0.08f * e);
                // Hide bubbles that scrolled above the header.
                bool clipped = y + _scroll > top + 0.001f;
                float a = e * _alpha * (clipped ? 0f : 1f);
                var bc = it.BubbleColor; bc.a *= a; it.Bubble.color = bc;
                it.Text.alpha = a;
                y -= it.Height + gap;
            }
        }

        private void ApplyAlpha(SpriteRenderer sr, float baseA)
        {
            var c = sr.color;
            c.a = baseA * _alpha;
            sr.color = c;
        }

        private void Update()
        {
            if (_pulse != null)
            {
                float k = Mathf.Repeat(Time.time * 0.8f, 1f);
                _pulse.transform.localScale = Vector3.one * (0.12f + k * 0.2f) * _size.x;
                var c = _pulse.color;
                c.a = (1f - k) * 0.8f * _alpha;
                _pulse.color = c;
            }
            if (_items.Count > 0) Layout();
        }

        public void SetStageAlpha(float alpha)
        {
            _alpha = alpha;
            if (_header != null) _header.alpha = alpha;
            if (_callName != null) _callName.alpha = alpha;
            if (_callSub != null) _callSub.alpha = alpha;
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            {
                if (sr.name != "HeaderRule") continue;
                ApplyAlpha(sr, 0.35f);
            }
        }
    }
}
