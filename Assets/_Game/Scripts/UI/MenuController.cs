using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Palinode.UI
{
    /// <summary>Title screen: PALINODE, New Game, Settings (text/voice language, subtitles, master volume), Quit.</summary>
    public sealed class MenuController : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private string backgroundSprite = "P1-02_bg";

        private GameRoot _root;
        private RectTransform _mainPanel;
        private RectTransform _settingsPanel;
        private readonly List<System.Action> _refreshers = new List<System.Action>();
        private Button _firstMain;
        private Button _firstSettings;
        private Transform _bg;
        private bool _starting;

        public void Configure(Camera cam) => mainCamera = cam;

        private void Awake()
        {
            _root = GameRoot.Ensure();
            _root.RegisterMainCamera(mainCamera);
            _root.CanSkip = false;
            Time.timeScale = _root.SpeedMultiplier * (_root.DebugFast ? 4f : 1f);
            Effects.ImprintController.Reset();
        }

        private void Start()
        {
            var audio = _root.Audio;
            audio.StopEverything(1f);
            audio.PlayLoop("menu_rain", _root.Config.Clip("SFX_rain_window"), 0.45f, 2f);
            audio.PlayLoop("menu_tick", _root.Config.Clip("SFX_clock_tick"), 0.12f, 3f);
            // Menu music (prologue.json "menu.music"): a seamless loop.
            var m = _root.Data["menu"]["music"];
            if (m.Has("clip"))
                audio.PlayMusic(_root.Config.Clip(m.Str("clip")), audio.VolumeForLufs(m.Str("clip"), m.Num("lufs", -30f), 0.3f), m.Num("fade", 3f));

            BuildBackground();
            BuildUI();
            _root.LanguageChanged += Refresh;
            Refresh();
            ShowMain();
            _root.Overlay.ResetAll();
            _root.Overlay.SetFade(Color.black, 1f);
            StartCoroutine(_root.Overlay.FadeTo(Color.black, 0f, 2f));
        }

        private void OnDestroy()
        {
            if (_root != null) _root.LanguageChanged -= Refresh;
        }

        private void BuildBackground()
        {
            var go = new GameObject("MenuBackground");
            _bg = go.transform;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _root.Config.Sprite(backgroundSprite);
            sr.sharedMaterial = _root.Config.Material("SpriteUnlit");
            sr.color = new Color(0.42f, 0.42f, 0.48f);
            if (sr.sprite != null)
            {
                Vector2 n = sr.sprite.bounds.size;
                float f = Mathf.Max(19.2f / n.x, 10.8f / n.y) * 1.08f;
                go.transform.localScale = Vector3.one * f;
            }
            var rain = new GameObject("MenuRain");
            rain.transform.SetParent(go.transform, false);
            var layer = rain.AddComponent<Cutscene.StageLayer>();
            layer.Init("menuRain", 0, null, 10);
            var fx = rain.AddComponent<Effects.ParticleFX>();
            var spec = JNode.Parse("{\"size\":[1600,900],\"rate\":260,\"speed\":10,\"angle\":10,\"color\":\"#b8c4d850\"}");
            fx.BuildRain(layer, spec, _root.Config);
        }

        private void Update()
        {
            if (_bg != null)
            {
                float t = Time.unscaledTime;
                _bg.position = new Vector3(Mathf.Sin(t * 0.05f) * 0.25f, Mathf.Sin(t * 0.037f) * 0.12f, 0f);
            }
        }

        private void BuildUI()
        {
            var canvas = UIFactory.Canvas("MenuCanvas", transform, null, 10);
            var titleFont = _root.Config.TitleFont;

            var vignette = UIFactory.Image("Shade", canvas.transform, new Color(0f, 0f, 0f, 0.35f), ProceduralSprites.White());
            UIFactory.Stretch(vignette.rectTransform);

            var title = UIFactory.Text("Title", canvas.transform, titleFont, 170f, new Color(0.93f, 0.89f, 0.8f));
            UIFactory.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1600f, 220f));
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            title.text = "PALINODE";
            title.characterSpacing = 22f;

            // Main panel
            _mainPanel = UIFactory.Rect("Main", canvas.transform);
            UIFactory.Anchor(_mainPanel, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(600f, 360f));
            var newGame = MakeButton(_mainPanel, 0, "MENU_NEW", OnNewGame);
            var settings = MakeButton(_mainPanel, 1, "MENU_SETTINGS", ShowSettings);
            var quit = MakeButton(_mainPanel, 2, "MENU_QUIT", OnQuit);
            UIFactory.Navigation(new Selectable[] { newGame, settings, quit });
            _firstMain = newGame;

            // Settings panel
            _settingsPanel = UIFactory.Rect("Settings", canvas.transform);
            UIFactory.Anchor(_settingsPanel, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(1100f, 520f));
            var back = UIFactory.Image("Back", _settingsPanel, new Color(0f, 0f, 0f, 0.55f), ProceduralSprites.RoundedRect(22));
            UIFactory.Stretch(back.rectTransform);
            var head = UIFactory.Text("Heading", _settingsPanel, titleFont, 54f, new Color(0.9f, 0.85f, 0.75f));
            UIFactory.Anchor(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(900f, 70f));
            _refreshers.Add(() => head.text = _root.T("SET_TITLE"));

            var s = _root.Settings;
            var textLang = MakeRow(0, "SET_TEXT_LANG", () => _root.T(s.TextLanguage == Language.EN ? "LANG_EN" : "LANG_RU"),
                () => s.SetTextLanguage(s.TextLanguage == Language.EN ? Language.RU : Language.EN));
            var voiceLang = MakeRow(1, "SET_VOICE_LANG", () => _root.T(s.VoiceLanguage == Language.EN ? "LANG_EN" : "LANG_RU"),
                () => s.SetVoiceLanguage(s.VoiceLanguage == Language.EN ? Language.RU : Language.EN));
            var subs = MakeRow(2, "SET_SUBS", () => _root.T(s.Subtitles ? "VAL_ON" : "VAL_OFF"), () => s.SetSubtitles(!s.Subtitles));
            var volume = MakeVolumeRow(3);
            var backBtn = UIFactory.Button("BackButton", _settingsPanel, titleFont, 40f, new Vector2(300f, 64f), ShowMain, out var backLabel);
            UIFactory.Anchor((RectTransform)backBtn.transform, new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(300f, 64f));
            _refreshers.Add(() => backLabel.text = _root.T("SET_BACK"));
            UIFactory.Navigation(new Selectable[] { textLang, voiceLang, subs, volume, backBtn });
            _firstSettings = textLang;
        }

        private Button MakeButton(RectTransform parent, int index, string key, UnityEngine.Events.UnityAction action)
        {
            var b = UIFactory.Button(key, parent, _root.Config.TitleFont, 52f, new Vector2(520f, 86f), action, out var label);
            UIFactory.Anchor((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(0f, -index * 104f), new Vector2(520f, 86f));
            label.characterSpacing = 6f;
            _refreshers.Add(() => label.text = _root.T(key));
            return b;
        }

        private Button MakeRow(int index, string labelKey, System.Func<string> value, System.Action toggle)
        {
            float y = -140f - index * 78f;
            var label = UIFactory.Text(labelKey, _settingsPanel, _root.Config.TitleFont, 40f, new Color(0.85f, 0.8f, 0.72f), TextAlignmentOptions.Left);
            UIFactory.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(90f, y), new Vector2(520f, 64f));
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            var btn = UIFactory.Button(labelKey + "_Value", _settingsPanel, _root.Config.TitleFont, 40f, new Vector2(380f, 64f), null, out var valueLabel);
            UIFactory.Anchor((RectTransform)btn.transform, new Vector2(1f, 1f), new Vector2(-90f, y), new Vector2(380f, 64f));
            ((RectTransform)btn.transform).pivot = new Vector2(1f, 0.5f);
            btn.onClick.AddListener(() => { toggle(); Refresh(); });
            _refreshers.Add(() =>
            {
                label.text = _root.T(labelKey);
                valueLabel.text = "‹  " + value() + "  ›";
            });
            return btn;
        }

        private Slider MakeVolumeRow(int index)
        {
            float y = -140f - index * 78f;
            var label = UIFactory.Text("Volume", _settingsPanel, _root.Config.TitleFont, 40f, new Color(0.85f, 0.8f, 0.72f), TextAlignmentOptions.Left);
            UIFactory.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(90f, y), new Vector2(520f, 64f));
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            _refreshers.Add(() => label.text = _root.T("SET_VOLUME"));

            var root = UIFactory.Rect("VolumeSlider", _settingsPanel);
            UIFactory.Anchor(root, new Vector2(1f, 1f), new Vector2(-110f, y), new Vector2(340f, 28f));
            root.pivot = new Vector2(1f, 0.5f);
            var bgImg = UIFactory.Image("Track", root, new Color(1f, 1f, 1f, 0.15f), ProceduralSprites.RoundedRect(8));
            UIFactory.Stretch(bgImg.rectTransform);
            var fillArea = UIFactory.Rect("FillArea", root);
            UIFactory.Stretch(fillArea);
            var fill = UIFactory.Image("Fill", fillArea, new Color(0.88f, 0.8f, 0.62f, 0.85f), ProceduralSprites.RoundedRect(8));
            UIFactory.Stretch(fill.rectTransform);
            var handleArea = UIFactory.Rect("HandleArea", root);
            UIFactory.Stretch(handleArea);
            var handle = UIFactory.Image("Handle", handleArea, new Color(0.95f, 0.92f, 0.85f), ProceduralSprites.SoftDot(64, 0.85f));
            handle.rectTransform.sizeDelta = new Vector2(36f, 36f);
            handle.raycastTarget = true;
            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = _root.Settings.MasterVolume;
            slider.onValueChanged.AddListener(v => _root.Settings.SetMasterVolume(v));
            var colors = slider.colors;
            colors.highlightedColor = colors.selectedColor = HoverFeedback.LabelHighlight;
            slider.colors = colors;
            var hover = root.gameObject.AddComponent<HoverFeedback>();
            hover.Label = label;
            hover.Scale = 1f;
            return slider;
        }

        private void Refresh()
        {
            foreach (var r in _refreshers) r();
        }

        private void ShowMain()
        {
            _mainPanel.gameObject.SetActive(true);
            _settingsPanel.gameObject.SetActive(false);
            UIFactory.Select(_firstMain);
        }

        private void ShowSettings()
        {
            _mainPanel.gameObject.SetActive(false);
            _settingsPanel.gameObject.SetActive(true);
            UIFactory.Select(_firstSettings);
        }

        private void OnNewGame()
        {
            if (_starting) return;
            _starting = true;
            StartCoroutine(StartGame());
        }

        private IEnumerator StartGame()
        {
            _root.Audio.StopAllLoops(1.5f);
            _root.Audio.StopMusic(1.5f);
            yield return _root.Overlay.FadeTo(Color.black, 1f, 1.5f);
            _root.Flow.StartNewGame();
        }

        /// <summary>Used by tests and attract mode.</summary>
        public void StartNewGameImmediate() => OnNewGame();

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
