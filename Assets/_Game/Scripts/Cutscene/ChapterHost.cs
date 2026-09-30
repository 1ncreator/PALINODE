using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.Effects;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Cutscene
{
    /// <summary>
    /// Scene-side runner of prologue chapters. Present in every prologue scene; for interactive scenes it is linked
    /// to the level (ILevel) and the stage is attached to the gameplay camera so inserts overlay the game view.
    /// </summary>
    public sealed class ChapterHost : MonoBehaviour, PrologueFlow.IHost
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Stage stage;
        [SerializeField] private Light2D globalLight;
        [SerializeField] private MonoBehaviour level;

        private GameRoot _root;
        private CutsceneContext _ctx;
        private Coroutine _run;
        private ILevel _level;

        public string SceneName => gameObject.scene.name;
        public string CurrentChapter { get; private set; }
        public Stage Stage => stage;
        public Camera MainCamera => mainCamera;

        public void Configure(Camera cam, Stage st, Light2D global, MonoBehaviour lvl)
        {
            mainCamera = cam;
            stage = st;
            globalLight = global;
            level = lvl;
        }

        private void Awake()
        {
            _root = GameRoot.Ensure();
            _root.RegisterMainCamera(mainCamera);
            _level = level as ILevel;
            stage.Init(_root.Config, _level != null ? 20000 : 0, _level == null);
            if (_level != null)
            {
                stage.transform.SetParent(_level.StageAnchor, false);
                stage.transform.localPosition = new Vector3(0f, 0f, 10f);
            }
            _root.Flow.RegisterHost(this);
        }

        private void Start()
        {
            string id = _root.Flow.ClaimPending(this);
            if (id != null) _root.Flow.StartPendingOn(this);
        }

        private void LateUpdate()
        {
            if (_level != null && mainCamera != null)
            {
                float s = mainCamera.orthographicSize / 5.4f;
                stage.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        public void RunChapter(string chapterId)
        {
            StopChapter();
            _run = StartCoroutine(RunRoutine(chapterId));
        }

        private IEnumerator RunRoutine(string id)
        {
            var chapter = _root.Flow.Chapter(id);
            CurrentChapter = id;
            Prepare(chapter);
            _ctx = new CutsceneContext(_root, stage, this, _level, globalLight, id)
            {
                GlobalLightBase = globalLight != null ? globalLight.intensity : 1f
            };
            _level?.OnChapterStart(chapter);
            _root.CanSkip = chapter.Bool("skippable", true);
            float started = Time.time;
            yield return Steps.RunList(chapter["steps"], _ctx);
            // Game-time length of the chapter (for fitting through-composed music cues).
            Debug.Log($"[PALINODE] chapter {id}: {Time.time - started:0.0} s; music {(_root.Audio.MusicClipName ?? "none")}");
            _root.CanSkip = false;
            _ctx.StopAll();
            _run = null;
            CurrentChapter = null;
            _root.Flow.CompleteCurrent();
        }

        private void Prepare(JNode chapter)
        {
            var audio = _root.Audio;
            audio.Unsilence(0f);
            audio.Duck(1f, 0f);
            audio.StopVoice();

            var wanted = new HashSet<string>();
            foreach (var a in chapter["ambience"].Items())
            {
                string clip = a.Str("clip");
                string id = a.Str("id", clip);
                wanted.Add(id);
                float vol = a.Has("lufs") ? audio.VolumeForLufs(clip, a.Num("lufs"), 1f) : a.Num("volume", 1f);
                if (audio.IsLoopPlaying(id)) audio.SetLoopVolume(id, vol, a.Num("fade", 1f));
                else audio.PlayLoop(id, _root.Config.Clip(clip), vol, a.Num("fade", 1.5f));
            }
            foreach (var id in new List<string>(audio.LoopIds))
                if (!wanted.Contains(id)) audio.StopLoop(id, chapter.Num("ambienceFadeOut", 1.2f));

            if (chapter.Has("music"))
            {
                var m = chapter["music"];
                float mv = m.Has("lufs") ? audio.VolumeForLufs(m.Str("clip"), m.Num("lufs"), 0.3f) : m.Num("volume", 0.3f);
                audio.PlayMusic(_root.Config.Clip(m.Str("clip")), mv, m.Num("fade", 2f), m.Bool("loop", true));
            }
            else audio.StopMusic(chapter.Num("musicFadeOut", 1.5f));

            ImprintController.Set(chapter.Num("imprint", 0f), chapter.Num("imprintEdge", 0f));
            ImprintController.SetGlitch(0f);

            _root.Overlay.ResetAll();
            if (chapter.Bool("startBlack", true)) _root.Overlay.SetFade(chapter.Color("startColor", Color.black), 1f);

            stage.ClearAll();
            if (globalLight != null && chapter.Has("globalLight"))
            {
                globalLight.intensity = chapter["globalLight"].Num("intensity", 1f);
                globalLight.color = chapter["globalLight"].Color("color", Color.white);
            }
        }

        public void StopChapter()
        {
            if (_run != null) StopCoroutine(_run);
            StopAllCoroutines();
            _run = null;
            _ctx?.StopAll();
            _ctx = null;
            if (_root != null)
            {
                _root.CanSkip = false;
                _root.Audio.StopVoice();
                _root.Overlay.HideSubtitle();
                _root.Overlay.HidePrompt();
                _root.Overlay.HideHint();
            }
            ImprintController.SetGlitch(0f);
        }

        private void OnDestroy()
        {
            if (_root == null) return;
            _root.Flow.UnregisterHost(this);
            if (_run != null) _root.CanSkip = false;
        }
    }
}
