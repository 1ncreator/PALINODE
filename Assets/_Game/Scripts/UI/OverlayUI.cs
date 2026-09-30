using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Palinode.UI
{
    /// <summary>
    /// Persistent screen-space overlay rendered by the UI overlay camera (never affected by the imprint effect):
    /// fade curtain, subtitles, title cards, hold-to-skip ring and the interaction prompt.
    /// </summary>
    public sealed class OverlayUI : MonoBehaviour
    {
        private Canvas _canvas;
        private Image _fade;
        private RectTransform _subtitlePanel;
        private TextMeshProUGUI _subtitle;
        private CanvasGroup _subtitleGroup;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _titleSub;
        private CanvasGroup _titleGroup;
        private Image _skipRing;
        private Image _skipRingBack;
        private TextMeshProUGUI _skipLabel;
        private CanvasGroup _skipGroup;
        private RectTransform _prompt;
        private TextMeshProUGUI _promptText;
        private CanvasGroup _promptGroup;
        private TextMeshProUGUI _debug;
        private RectTransform _hint;
        private TextMeshProUGUI _hintText;
        private CanvasGroup _hintGroup;

        private Coroutine _fadeRoutine;
        private Coroutine _subtitleRoutine;
        private float _promptTargetAlpha;
        private float _hintTargetAlpha;

        public Canvas Canvas => _canvas;
        public float FadeAlpha => _fade != null ? _fade.color.a : 0f;
        public Color FadeColor => _fade != null ? _fade.color : Color.clear;

        public void Build(TMP_FontAsset titleFont, TMP_FontAsset monoFont)
        {
            _canvas = UIFactory.Canvas("OverlayCanvas", transform, null, 100);

            // Subtitles
            _subtitlePanel = UIFactory.Rect("Subtitles", _canvas.transform);
            UIFactory.Anchor(_subtitlePanel, new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(1400f, 90f));
            _subtitleGroup = _subtitlePanel.gameObject.AddComponent<CanvasGroup>();
            _subtitleGroup.alpha = 0f;
            var back = UIFactory.Image("Backing", _subtitlePanel, new Color(0f, 0f, 0f, 0.62f), ProceduralSprites.RoundedRect(20));
            UIFactory.Stretch(back.rectTransform);
            _subtitle = UIFactory.Text("Text", _subtitlePanel, titleFont, 42f, new Color(0.94f, 0.91f, 0.84f));
            UIFactory.Stretch(_subtitle.rectTransform);
            _subtitle.margin = new Vector4(36f, 10f, 36f, 12f);
            _subtitle.textWrappingMode = TextWrappingModes.Normal;

            // Title card
            var titleRoot = UIFactory.Rect("Title", _canvas.transform);
            UIFactory.Stretch(titleRoot);
            _titleGroup = titleRoot.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.alpha = 0f;
            _title = UIFactory.Text("Main", titleRoot, titleFont, 150f, new Color(0.92f, 0.88f, 0.8f));
            UIFactory.Anchor(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1800f, 220f));
            _title.characterSpacing = 18f;
            _titleSub = UIFactory.Text("Sub", titleRoot, titleFont, 46f, new Color(0.75f, 0.7f, 0.62f));
            UIFactory.Anchor(_titleSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1800f, 80f));
            _titleSub.characterSpacing = 30f;

            // Prompt
            _prompt = UIFactory.Rect("Prompt", _canvas.transform);
            UIFactory.Anchor(_prompt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 64f));
            _prompt.pivot = new Vector2(0.5f, 0f);
            _promptGroup = _prompt.gameObject.AddComponent<CanvasGroup>();
            _promptGroup.alpha = 0f;
            var pb = UIFactory.Image("Backing", _prompt, new Color(0f, 0f, 0f, 0.55f), ProceduralSprites.RoundedRect(14));
            UIFactory.Stretch(pb.rectTransform);
            _promptText = UIFactory.Text("Text", _prompt, titleFont, 34f, new Color(0.95f, 0.9f, 0.78f));
            UIFactory.Stretch(_promptText.rectTransform);

            // Gameplay hint (controls / objective), top centre
            _hint = UIFactory.Rect("Hint", _canvas.transform);
            UIFactory.Anchor(_hint, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(760f, 110f));
            _hint.pivot = new Vector2(0.5f, 1f);
            _hintGroup = _hint.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.alpha = 0f;
            var hb = UIFactory.Image("Backing", _hint, new Color(0f, 0f, 0f, 0.55f), ProceduralSprites.RoundedRect(16));
            UIFactory.Stretch(hb.rectTransform);
            _hintText = UIFactory.Text("Text", _hint, titleFont, 32f, new Color(0.95f, 0.9f, 0.78f));
            UIFactory.Stretch(_hintText.rectTransform);
            _hintText.margin = new Vector4(28f, 10f, 28f, 12f);
            _hintText.lineSpacing = 12f;

            // Skip ring
            var skip = UIFactory.Rect("Skip", _canvas.transform);
            UIFactory.Anchor(skip, new Vector2(1f, 0f), new Vector2(-48f, 44f), new Vector2(64f, 64f));
            _skipGroup = skip.gameObject.AddComponent<CanvasGroup>();
            _skipGroup.alpha = 0f;
            _skipRingBack = UIFactory.Image("RingBack", skip, new Color(1f, 1f, 1f, 0.15f), ProceduralSprites.Ring());
            UIFactory.Stretch(_skipRingBack.rectTransform);
            _skipRing = UIFactory.Image("Ring", skip, new Color(0.95f, 0.88f, 0.7f, 0.95f), ProceduralSprites.Ring());
            UIFactory.Stretch(_skipRing.rectTransform);
            _skipRing.type = Image.Type.Filled;
            _skipRing.fillMethod = Image.FillMethod.Radial360;
            _skipRing.fillOrigin = (int)Image.Origin360.Top;
            _skipRing.fillClockwise = true;
            _skipRing.fillAmount = 0f;
            _skipLabel = UIFactory.Text("Label", skip, titleFont, 26f, new Color(0.9f, 0.86f, 0.78f, 0.9f), TextAlignmentOptions.Right);
            UIFactory.Anchor(_skipLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(-16f, 0f), new Vector2(420f, 40f));
            _skipLabel.rectTransform.pivot = new Vector2(1f, 0.5f);

            // Debug line
            _debug = UIFactory.Text("Debug", _canvas.transform, monoFont, 22f, new Color(0.6f, 1f, 0.6f, 0.95f), TextAlignmentOptions.TopLeft);
            UIFactory.Anchor(_debug.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(1200f, 600f));
            _debug.text = string.Empty;

            // Fade curtain: above the picture, subtitles and prompts; title cards, skip ring and debug text stay on top of it.
            _fade = UIFactory.Image("Fade", _canvas.transform, new Color(0f, 0f, 0f, 0f), ProceduralSprites.White());
            UIFactory.Stretch(_fade.rectTransform);
            _fade.raycastTarget = false;
            _fade.transform.SetSiblingIndex(_prompt.GetSiblingIndex() + 1);
            _hint.SetSiblingIndex(_fade.transform.GetSiblingIndex());   // hint sits under the curtain too
            titleRoot.SetAsLastSibling();
            skip.SetAsLastSibling();
            _debug.transform.SetAsLastSibling();
        }

        // ---------------- Fade ----------------

        public void SetFade(Color color, float alpha)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
            color.a = alpha;
            _fade.color = color;
        }

        public IEnumerator FadeTo(Color color, float alpha, float duration)
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(color, alpha, duration));
            yield return _fadeRoutine;
        }

        private IEnumerator FadeRoutine(Color color, float alpha, float duration)
        {
            Color from = _fade.color;
            // Fading from transparent: switch to the target color immediately so we don't blend through black.
            if (from.a <= 0.001f) from = new Color(color.r, color.g, color.b, 0f);
            Color to = new Color(color.r, color.g, color.b, alpha);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                _fade.color = Color.Lerp(from, to, k);
                yield return null;
            }
            _fade.color = to;
            _fadeRoutine = null;
        }

        // ---------------- Subtitles ----------------

        public void ShowSubtitle(string text, bool visible)
        {
            if (_subtitleRoutine != null) StopCoroutine(_subtitleRoutine);
            if (!visible || string.IsNullOrEmpty(text))
            {
                _subtitleRoutine = StartCoroutine(FadeGroup(_subtitleGroup, 0f, 0.2f));
                return;
            }
            _subtitle.text = text;
            _subtitle.ForceMeshUpdate();
            float w = Mathf.Min(1500f, _subtitle.preferredWidth + 80f);
            _subtitlePanel.sizeDelta = new Vector2(Mathf.Max(420f, w), 90f);
            _subtitle.ForceMeshUpdate();
            float h = Mathf.Max(84f, _subtitle.GetPreferredValues(text, w - 72f, 0f).y + 26f);
            _subtitlePanel.sizeDelta = new Vector2(Mathf.Max(420f, w), h);
            _subtitleRoutine = StartCoroutine(FadeGroup(_subtitleGroup, 1f, 0.18f));
        }

        public void HideSubtitle() => ShowSubtitle(null, false);

        public string CurrentSubtitle => _subtitleGroup != null && _subtitleGroup.alpha > 0.01f ? _subtitle.text : null;

        // ---------------- Title ----------------

        public IEnumerator TitleCard(string main, string sub, float fadeIn, float hold, float fadeOut, float mainSize = 150f)
        {
            _title.text = main ?? string.Empty;
            _title.fontSize = mainSize;
            _titleSub.text = sub ?? string.Empty;
            _titleSub.alpha = 1f;
            yield return FadeGroup(_titleGroup, 1f, fadeIn);
            float t = 0f;
            while (t < hold) { t += Time.deltaTime; yield return null; }
            if (fadeOut >= 0f) yield return FadeGroup(_titleGroup, 0f, fadeOut);
        }

        public IEnumerator SetTitleSub(string sub, float fade)
        {
            _titleSub.text = sub ?? string.Empty;
            float t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                _titleSub.alpha = Mathf.Clamp01(t / fade);
                yield return null;
            }
            _titleSub.alpha = 1f;
        }

        public IEnumerator FadeTitle(float alpha, float duration) => FadeGroup(_titleGroup, alpha, duration);

        public void ClearTitle()
        {
            _titleGroup.alpha = 0f;
            _title.text = string.Empty;
            _titleSub.text = string.Empty;
        }

        // ---------------- Skip ----------------

        public void SetSkipProgress(float progress, string label, bool visible)
        {
            _skipRing.fillAmount = Mathf.Clamp01(progress);
            _skipLabel.text = label;
            float target = visible ? 1f : 0f;
            _skipGroup.alpha = Mathf.MoveTowards(_skipGroup.alpha, target, Time.unscaledDeltaTime * 4f);
        }

        // ---------------- Prompt ----------------

        public void ShowPrompt(string text, Vector3 worldPos, Camera worldCamera)
        {
            _promptText.text = text;
            _promptTargetAlpha = 1f;
            if (worldCamera != null)
            {
                Vector3 sp = worldCamera.WorldToViewportPoint(worldPos);
                var canvasRt = (RectTransform)_canvas.transform;
                Vector2 size = canvasRt.rect.size;
                _prompt.anchorMin = _prompt.anchorMax = Vector2.zero;
                _prompt.anchoredPosition = new Vector2(sp.x * size.x, sp.y * size.y);
            }
        }

        public void HidePrompt() => _promptTargetAlpha = 0f;

        public bool PromptVisible => _promptGroup != null && _promptGroup.alpha > 0.5f;

        // ---------------- Hint ----------------

        /// <summary>Controls / objective hint at the top of the screen (multi-line text, sized to fit).</summary>
        public void ShowHint(string text)
        {
            if (string.IsNullOrEmpty(text)) { HideHint(); return; }
            _hintText.text = text;
            _hintText.ForceMeshUpdate();
            Vector2 pref = _hintText.GetPreferredValues(text, 1400f, 0f);
            _hint.sizeDelta = new Vector2(Mathf.Clamp(pref.x + 64f, 360f, 1460f), pref.y + 30f);
            _hintTargetAlpha = 1f;
        }

        public void HideHint() => _hintTargetAlpha = 0f;

        public void SetDebug(string text) { if (_debug != null) _debug.text = text; }

        private void Update()
        {
            if (_promptGroup != null)
                _promptGroup.alpha = Mathf.MoveTowards(_promptGroup.alpha, _promptTargetAlpha, Time.unscaledDeltaTime * 5f);
            if (_hintGroup != null)
                _hintGroup.alpha = Mathf.MoveTowards(_hintGroup.alpha, _hintTargetAlpha, Time.unscaledDeltaTime * 2.5f);
        }

        private static IEnumerator FadeGroup(CanvasGroup g, float to, float duration)
        {
            float from = g.alpha, t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            g.alpha = to;
        }

        public void ResetAll()
        {
            if (_subtitleRoutine != null) StopCoroutine(_subtitleRoutine);
            _subtitleGroup.alpha = 0f;
            ClearTitle();
            _promptGroup.alpha = 0f;
            _promptTargetAlpha = 0f;
            _hintGroup.alpha = 0f;
            _hintTargetAlpha = 0f;
        }
    }
}
