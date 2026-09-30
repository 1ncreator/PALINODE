using System;
using Palinode.Audio;
using Palinode.Effects;
using Palinode.Localization;
using Palinode.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;

namespace Palinode.Core
{
    /// <summary>
    /// Persistent service root (created on demand by any scene bootstrap): config, settings, localization,
    /// audio, overlay UI, prologue flow, hold-to-skip and debug keys.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public const int UILayer = 5;
        public const float SkipHoldSeconds = 1.5f;

        private static GameRoot _instance;

        public static GameRoot Instance => _instance;

        public GameConfig Config { get; private set; }
        public GameSettings Settings { get; private set; }
        public LocalizationTable Loc { get; private set; }
        public AudioDirector Audio { get; private set; }
        public OverlayUI Overlay { get; private set; }
        public PrologueFlow Flow { get; private set; }
        public JNode Data { get; private set; }
        public Camera MainCamera { get; private set; }

        /// <summary>True while a skippable prologue chapter runs.</summary>
        public bool CanSkip { get; set; }

        /// <summary>Global speed multiplier (tests use ×10). Combined with the F2 debug toggle.</summary>
        public float SpeedMultiplier
        {
            get => _speedMultiplier;
            set { _speedMultiplier = Mathf.Max(0.01f, value); ApplyTimeScale(); }
        }

        public bool DebugFast { get; private set; }
        public static event Action<bool> DebugCollidersChanged;
        public static bool DebugCollidersVisible { get; private set; }

        public event Action LanguageChanged;

        private float _speedMultiplier = 1f;
        private float _skipHold;
        private bool _skipWaitRelease;
        private bool _debugPanel;

        public static GameRoot Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[PALINODE]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameRoot>();
            _instance.Init();
            return _instance;
        }

        private void Init()
        {
            Config = GameConfig.Load();
            if (Config == null)
            {
                Debug.LogError("[PALINODE] GameConfig not found in Resources. Run Tools/PALINODE/Build Everything.");
                Config = ScriptableObject.CreateInstance<GameConfig>();
            }
            Settings = new GameSettings();
            Loc = LocalizationTable.FromCsv(Config.LocalizationCsv != null ? Config.LocalizationCsv.text : string.Empty);
            Data = Config.PrologueJson != null ? JNode.Parse(Config.PrologueJson.text) : new JNode(null);
            Flow = new PrologueFlow(Data);

            var audioGo = new GameObject("Audio");
            audioGo.transform.SetParent(transform, false);
            audioGo.AddComponent<AudioListener>();
            Audio = audioGo.AddComponent<AudioDirector>();
            Audio.MasterVolume = Settings.MasterVolume;
            Audio.ClipLufs = Config.ClipLufs;
            Audio.VoiceFolder = Settings.VoiceFolder;
            var mix = Data["mix"];
            Audio.VoiceDuckLevel = Mathf.Pow(10f, mix.Num("voiceDuckDb", -7f) / 20f);
            Audio.VoiceDuckAttack = mix.Num("voiceDuckAttack", 0.15f);
            Audio.VoiceDuckRelease = mix.Num("voiceDuckRelease", 0.7f);

            // All UI canvases are Screen Space - Overlay: drawn after the camera, never post-processed or pixelated.
            var overlayGo = new GameObject("Overlay");
            overlayGo.transform.SetParent(transform, false);
            Overlay = overlayGo.AddComponent<OverlayUI>();
            Overlay.Build(Config.TitleFont, Config.MonoFont);

            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            var module = es.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();

            Settings.Changed += OnSettingsChanged;
            ImprintController.Reset();
        }

        private void OnSettingsChanged()
        {
            Audio.MasterVolume = Settings.MasterVolume;
            Audio.VoiceFolder = Settings.VoiceFolder;
            LanguageChanged?.Invoke();
        }

        /// <summary>Each scene hands over its main camera (used for world→screen prompts and screenshots).</summary>
        public void RegisterMainCamera(Camera cam)
        {
            MainCamera = cam;
            if (cam == null) return;
            cam.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base;
            cam.cullingMask &= ~(1 << UILayer);
        }

        public string T(string key) => Loc.Get(key, Settings.TextLanguage);

        public AudioClip VoiceClip(string name) => Config.Clip(Settings.VoiceFolder + "/" + name);

        private void ApplyTimeScale()
        {
            Time.timeScale = _speedMultiplier * (DebugFast ? 4f : 1f);
        }

        private void Update()
        {
            UpdateSkip();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UpdateDebug();
#endif
        }

        private void UpdateSkip()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool held = (kb != null && (kb.escapeKey.isPressed || kb.spaceKey.isPressed))
                        || (gp != null && (gp.startButton.isPressed || gp.buttonEast.isPressed));
            float dt = Time.unscaledDeltaTime;
            if (!CanSkip)
            {
                _skipHold = 0f;
                if (!held) _skipWaitRelease = false;
            }
            else if (held && !_skipWaitRelease)
            {
                _skipHold += dt;
                if (_skipHold >= SkipHoldSeconds)
                {
                    _skipHold = 0f;
                    _skipWaitRelease = true;
                    Flow.Skip();
                }
            }
            else
            {
                _skipHold = Mathf.Max(0f, _skipHold - dt * 3f);
                if (!held) _skipWaitRelease = false;
            }
            Overlay.SetSkipProgress(_skipHold / SkipHoldSeconds, T("HINT_SKIP"), _skipHold > 0.05f);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void UpdateDebug()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame)
            {
                _debugPanel = !_debugPanel;
                RefreshDebugPanel();
            }
            if (_debugPanel)
            {
                for (int i = 0; i < 10; i++)
                {
                    var key = i == 0 ? kb.digit0Key : kb[(Key)((int)Key.Digit1 + i - 1)] as UnityEngine.InputSystem.Controls.KeyControl;
                    if (key == null || !key.wasPressedThisFrame) continue;
                    _debugPanel = false;
                    RefreshDebugPanel();
                    if (i == 0) Flow.ReturnToMenu();
                    else if (i - 1 <= Flow.Order.Count) Flow.GoTo(i - 1);   // one past the last chapter = Floor I (skip the prologue)
                    break;
                }
            }
            if (kb.f2Key.wasPressedThisFrame)
            {
                DebugFast = !DebugFast;
                ApplyTimeScale();
                RefreshDebugPanel();
            }
            if (kb.f3Key.wasPressedThisFrame)
            {
                DebugCollidersVisible = !DebugCollidersVisible;
                DebugCollidersChanged?.Invoke(DebugCollidersVisible);
                RefreshDebugPanel();
            }
        }

        private void RefreshDebugPanel()
        {
            var sb = new System.Text.StringBuilder();
            if (_debugPanel)
            {
                sb.AppendLine("PROLOGUE — press a number to jump (0 = menu):");
                for (int i = 0; i < Flow.Order.Count && i < 9; i++)
                    sb.AppendLine($"  {i + 1}. {Flow.Order[i]}{(i == Flow.Index ? "   <" : string.Empty)}");
                if (Flow.Order.Count < 9) sb.AppendLine($"  {Flow.Order.Count + 1}. Floor I (skip the prologue)");
            }
            if (DebugFast) sb.AppendLine("SPEED ×4 (F2)");
            if (DebugCollidersVisible) sb.AppendLine("COLLIDERS (F3)");
            Overlay.SetDebug(sb.ToString());
        }
#endif

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
