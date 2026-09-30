using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Palinode.Floor
{
    /// <summary>
    /// Floor I HUD (screen-space overlay): a row of ink drops instead of hearts (a thin red rim that shows more with
    /// every red-pencil edit ever made), blank sheets, the red pencil's charge, the boss bar, the item banner, and
    /// full-screen layers: soaked edges at the last drop, white flashes, the micro-sleep blink and the page turn.
    /// </summary>
    public sealed class FloorHud : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _drops;
        private readonly List<(Image rim, Image drop)> _dropImages = new List<(Image, Image)>();
        private Sprite _full, _half, _empty;
        private TextMeshProUGUI _sheets;
        private RectTransform _pencil;
        private readonly List<Image> _pencilCells = new List<Image>();
        private RectTransform _bossBar;
        private Image _bossFill;
        private TextMeshProUGUI _bossName;
        private CanvasGroup _banner;
        private TextMeshProUGUI _bannerTitle, _bannerText;
        private Coroutine _bannerRoutine;
        private Image _soaked, _flash, _lidTop, _lidBottom, _page;
        private float _soakTarget;
        private int _edits;

        public Canvas Canvas => _canvas;
        public RectTransform Root { get; private set; }

        public void Build(GameConfig config, int edits)
        {
            _edits = edits;
            _canvas = UIFactory.Canvas("FloorHud", transform, null, 20);
            Root = (RectTransform)_canvas.transform;
            _full = config.Sprite("F1_hp_full");
            _half = config.Sprite("F1_hp_half");
            _empty = config.Sprite("F1_hp_empty");

            // Soaked edges (under the rest of the HUD).
            _soaked = UIFactory.Image("Soaked", Root, new Color(1f, 1f, 1f, 0f), SoakedSprite());
            UIFactory.Stretch(_soaked.rectTransform);
            _soaked.raycastTarget = false;

            _drops = UIFactory.Rect("Drops", Root);
            UIFactory.Anchor(_drops, new Vector2(0f, 1f), new Vector2(40f, -34f), new Vector2(700f, 70f));
            _drops.pivot = new Vector2(0f, 1f);

            // Sheets.
            var sheetsRow = UIFactory.Rect("Sheets", Root);
            UIFactory.Anchor(sheetsRow, new Vector2(0f, 1f), new Vector2(40f, -112f), new Vector2(200f, 48f));
            sheetsRow.pivot = new Vector2(0f, 1f);
            var sheetIcon = UIFactory.Image("Icon", sheetsRow, Color.white, config.Sprite("F1_pickup_sheet"));
            sheetIcon.preserveAspect = true;
            UIFactory.Anchor(sheetIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(44f, 44f));
            _sheets = UIFactory.Text("Count", sheetsRow, config.Font("hand_bad"), 36f, new Color(0.93f, 0.88f, 0.78f), TextAlignmentOptions.Left);
            UIFactory.Anchor(_sheets.rectTransform, new Vector2(0f, 0.5f), new Vector2(110f, 0f), new Vector2(120f, 48f));

            // Red pencil + charge.
            _pencil = UIFactory.Rect("Pencil", Root);
            UIFactory.Anchor(_pencil, new Vector2(0f, 1f), new Vector2(40f, -170f), new Vector2(260f, 56f));
            _pencil.pivot = new Vector2(0f, 1f);
            var pencilIcon = UIFactory.Image("Icon", _pencil, Color.white, config.Sprite("PR_redpencil"));
            pencilIcon.preserveAspect = true;
            pencilIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            UIFactory.Anchor(pencilIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(60f, 60f));
            for (int i = 0; i < 4; i++)
            {
                var cell = UIFactory.Image("Charge" + i, _pencil, new Color(0.7f, 0.08f, 0.08f, 0.9f), ProceduralSprites.RoundedRect(6));
                UIFactory.Anchor(cell.rectTransform, new Vector2(0f, 0.5f), new Vector2(76f + i * 34f, 0f), new Vector2(28f, 16f));
                _pencilCells.Add(cell);
            }
            _pencil.gameObject.SetActive(false);

            // Boss bar.
            _bossBar = UIFactory.Rect("BossBar", Root);
            UIFactory.Anchor(_bossBar, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(900f, 300f));
            // The painted frame has an opaque channel: the fill is drawn over it, inside the channel.
            var frameSprite = config.Sprite("F1_bossbar");
            var frame = UIFactory.Image("Frame", _bossBar, Color.white, frameSprite);
            frame.preserveAspect = true;
            UIFactory.Stretch(frame.rectTransform);
            _bossFill = UIFactory.Image("Fill", _bossBar, Color.white, ProceduralSprites.White());
            UIFactory.Anchor(_bossFill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(62f, -2f), new Vector2(604f, 24f));
            _bossFill.type = Image.Type.Filled;
            _bossFill.fillMethod = Image.FillMethod.Horizontal;
            _bossFill.color = new Color(0.46f, 0.08f, 0.07f, 0.92f);
            _bossName = UIFactory.Text("Name", _bossBar, config.TitleFont, 34f, new Color(0.92f, 0.86f, 0.74f));
            UIFactory.Anchor(_bossName.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 48f), new Vector2(600f, 50f));
            _bossName.characterSpacing = 12f;
            _bossBar.gameObject.SetActive(false);

            // Item banner.
            var bannerRt = UIFactory.Rect("Banner", Root);
            UIFactory.Anchor(bannerRt, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1300f, 180f));
            _banner = bannerRt.gameObject.AddComponent<CanvasGroup>();
            _banner.alpha = 0f;
            var bb = UIFactory.Image("Back", bannerRt, new Color(0f, 0f, 0f, 0.6f), ProceduralSprites.RoundedRect(20));
            UIFactory.Stretch(bb.rectTransform);
            // Anchor() puts the pivot on the anchor: the title hangs from the top, the line stands on the bottom.
            _bannerTitle = UIFactory.Text("Title", bannerRt, config.TitleFont, 56f, new Color(0.95f, 0.9f, 0.8f));
            UIFactory.Anchor(_bannerTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1240f, 72f));
            _bannerTitle.characterSpacing = 6f;
            _bannerText = UIFactory.Text("Text", bannerRt, config.Font("hand_bad"), 36f, new Color(0.86f, 0.82f, 0.74f));
            UIFactory.Anchor(_bannerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1240f, 72f));

            // Full-screen layers on top.
            _flash = UIFactory.Image("Flash", Root, new Color(1f, 1f, 1f, 0f), ProceduralSprites.White());
            UIFactory.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
            _lidTop = UIFactory.Image("LidTop", Root, Color.black, ProceduralSprites.White());
            _lidBottom = UIFactory.Image("LidBottom", Root, Color.black, ProceduralSprites.White());
            foreach (var lid in new[] { _lidTop, _lidBottom })
            {
                lid.raycastTarget = false;
                lid.rectTransform.anchorMin = new Vector2(0f, lid == _lidTop ? 1f : 0f);
                lid.rectTransform.anchorMax = new Vector2(1f, lid == _lidTop ? 1f : 0f);
                lid.rectTransform.pivot = new Vector2(0.5f, lid == _lidTop ? 1f : 0f);
                lid.rectTransform.sizeDelta = new Vector2(0f, 0f);
            }
            _page = UIFactory.Image("PageTurn", Root, new Color(0.84f, 0.79f, 0.68f, 0f), config.Sprite("PR_sheet"));
            _page.raycastTarget = false;
            _page.rectTransform.anchorMin = new Vector2(0f, 0f);
            _page.rectTransform.anchorMax = new Vector2(0f, 1f);
            _page.rectTransform.pivot = new Vector2(0f, 0.5f);
            _page.rectTransform.sizeDelta = new Vector2(0f, 60f);
        }

        // ------------------------------------------------------------------ drops

        public void SetHealth(int halves, int maxHalves)
        {
            int containers = Mathf.CeilToInt(maxHalves / 2f);
            while (_dropImages.Count < containers)
            {
                int i = _dropImages.Count;
                var rim = UIFactory.Image("Rim" + i, _drops, new Color(0.75f, 0.06f, 0.07f, 0f), _full);
                rim.preserveAspect = true;
                var drop = UIFactory.Image("Drop" + i, _drops, Color.white, _full);
                drop.preserveAspect = true;
                float x = 30f + (i % 6) * 58f, y = -30f - (i / 6) * 64f;
                UIFactory.Anchor(rim.rectTransform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(56f, 64f));
                UIFactory.Anchor(drop.rectTransform, new Vector2(0f, 1f), new Vector2(x, y), new Vector2(50f, 58f));
                _dropImages.Add((rim, drop));
            }
            float rimA = Mathf.Clamp01(_edits * 0.14f) * 0.95f;
            float rimScale = 1f + Mathf.Min(_edits, 10) * 0.012f;
            for (int i = 0; i < _dropImages.Count; i++)
            {
                var (rim, drop) = _dropImages[i];
                bool on = i < containers;
                rim.gameObject.SetActive(on && _edits > 0);
                drop.gameObject.SetActive(on);
                int h = halves - i * 2;
                drop.sprite = h >= 2 ? _full : h == 1 ? _half : _empty;
                rim.color = new Color(0.75f, 0.06f, 0.07f, rimA);
                rim.rectTransform.localScale = Vector3.one * rimScale;
            }
            _soakTarget = halves <= 2 && halves > 0 ? 1f : 0f;
        }

        public void SetEdits(int edits) => _edits = edits;

        public void SetSheets(int n) => _sheets.text = "× " + n;

        public void SetPencil(bool has, int charge, int full)
        {
            _pencil.gameObject.SetActive(has);
            for (int i = 0; i < _pencilCells.Count; i++)
            {
                _pencilCells[i].gameObject.SetActive(i < full);
                bool lit = i < charge;
                _pencilCells[i].color = lit ? new Color(0.78f, 0.08f, 0.08f, 0.95f) : new Color(0.25f, 0.2f, 0.2f, 0.7f);
            }
        }

        // ------------------------------------------------------------------ boss

        public void BossBar(bool show, float fraction, string name)
        {
            _bossBar.gameObject.SetActive(show);
            _bossFill.fillAmount = Mathf.Clamp01(fraction);
            if (name != null) _bossName.text = name;
        }

        // ------------------------------------------------------------------ banner

        public void Banner(string title, string text, float seconds = 3.2f)
        {
            _bannerTitle.text = title;
            _bannerText.text = text;
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(BannerRoutine(seconds));
        }

        private IEnumerator BannerRoutine(float seconds)
        {
            float t = 0f;
            while (t < 0.25f) { t += Time.unscaledDeltaTime; _banner.alpha = t / 0.25f; yield return null; }
            _banner.alpha = 1f;
            t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (t < 0.6f) { t += Time.unscaledDeltaTime; _banner.alpha = 1f - t / 0.6f; yield return null; }
            _banner.alpha = 0f;
        }

        // ------------------------------------------------------------------ screen layers

        public void Flash(Color c, float duration) => StartCoroutine(FlashRoutine(c, duration));

        private IEnumerator FlashRoutine(Color c, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _flash.color = new Color(c.r, c.g, c.b, c.a * (1f - t / duration));
                yield return null;
            }
            _flash.color = new Color(c.r, c.g, c.b, 0f);
        }

        /// <summary>Eyes close for a moment (energy drink).</summary>
        public void Blink(float duration) => StartCoroutine(BlinkRoutine(duration));

        private IEnumerator BlinkRoutine(float duration)
        {
            float h = Root.rect.height * 0.5f + 4f;
            float close = 0.09f, t = 0f;
            while (t < close) { t += Time.unscaledDeltaTime; SetLids(h * t / close); yield return null; }
            SetLids(h);
            yield return new WaitForSecondsRealtime(duration);
            t = 0f;
            while (t < 0.16f) { t += Time.unscaledDeltaTime; SetLids(h * (1f - t / 0.16f)); yield return null; }
            SetLids(0f);
        }

        private void SetLids(float h)
        {
            _lidTop.rectTransform.sizeDelta = new Vector2(0f, h);
            _lidBottom.rectTransform.sizeDelta = new Vector2(0f, h);
        }

        /// <summary>A page sweeps across the screen (room transition).</summary>
        public IEnumerator PageTurn(float duration, bool leftToRight)
        {
            float w = Root.rect.width;
            float t = 0f;
            var rt = _page.rectTransform;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                // The page grows from one edge and slides off the other.
                float lead = Mathf.SmoothStep(0f, 1f, k) * (w * 1.35f);
                float width = w * 0.45f * Mathf.Sin(k * Mathf.PI);
                float x = leftToRight ? lead - width : w - lead;
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(width, 60f);
                _page.color = new Color(0.84f, 0.79f, 0.68f, 0.85f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            _page.color = new Color(0.84f, 0.79f, 0.68f, 0f);
        }

        private void Update()
        {
            if (_soaked == null) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.4f);
            float a = Mathf.MoveTowards(_soaked.color.a, _soakTarget * pulse, Time.unscaledDeltaTime * 1.5f);
            _soaked.color = new Color(1f, 1f, 1f, a);
        }

        private static Sprite _soakedSprite;

        /// <summary>Ink soaking in from the screen edges: blotchy dark rim, clear centre.</summary>
        private static Sprite SoakedSprite()
        {
            if (_soakedSprite != null) return _soakedSprite;
            const int w = 256, h = 144;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1), v = y / (float)(h - 1);
                float edge = Mathf.Min(Mathf.Min(u, 1f - u) * 1.78f, Mathf.Min(v, 1f - v));
                float n = Mathf.PerlinNoise(u * 7f, v * 4f) * 0.6f + Mathf.PerlinNoise(u * 19f + 3f, v * 11f + 7f) * 0.4f;
                float a = Mathf.Clamp01(1f - (edge - n * 0.13f) / 0.2f);
                a = a * a * (3f - 2f * a);
                px[x + y * w] = new Color32(8, 6, 10, (byte)(a * 235f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            _soakedSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _soakedSprite;
        }
    }
}
