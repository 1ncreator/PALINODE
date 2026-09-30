using System;
using System.Collections;
using System.Collections.Generic;
using Palinode.Audio;
using Palinode.Core;
using Palinode.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Cutscene
{
    /// <summary>Everything a step needs while a chapter runs. Owns the chapter's background coroutines.</summary>
    public sealed class CutsceneContext
    {
        private readonly List<Coroutine> _async = new List<Coroutine>();
        private readonly Dictionary<string, AudioSource> _namedSfx = new Dictionary<string, AudioSource>();

        public GameRoot Root { get; }
        public Stage Stage { get; }
        public MonoBehaviour Runner { get; }
        public ILevel Level { get; }
        public Light2D GlobalLight { get; }
        public string ChapterId { get; }
        public float GlobalLightBase { get; set; } = 1f;

        public AudioDirector Audio => Root.Audio;
        public OverlayUI Overlay => Root.Overlay;
        public GameConfig Config => Root.Config;

        public static event Action<string> MarkerReached;

        public CutsceneContext(GameRoot root, Stage stage, MonoBehaviour runner, ILevel level, Light2D globalLight, string chapterId)
        {
            Root = root;
            Stage = stage;
            Runner = runner;
            Level = level;
            GlobalLight = globalLight;
            ChapterId = chapterId;
        }

        public string T(string key) => Root.T(key);

        public void StartAsync(IEnumerator routine)
        {
            if (routine == null) return;
            _async.Add(Runner.StartCoroutine(routine));
        }

        public void StopAll()
        {
            foreach (var c in _async) if (c != null) Runner.StopCoroutine(c);
            _async.Clear();
            foreach (var s in _namedSfx.Values) if (s != null) Audio.StopOneShot(s, 0.2f);
            _namedSfx.Clear();
        }

        public void RememberSfx(string id, AudioSource src)
        {
            if (!string.IsNullOrEmpty(id) && src != null) _namedSfx[id] = src;
        }

        public AudioSource NamedSfx(string id) => id != null && _namedSfx.TryGetValue(id, out var s) ? s : null;

        public void RaiseMarker(string id) => Raise(id);

        /// <summary>Named moment of the prologue (used by tests for screenshots and by tools for timing).</summary>
        public static void Raise(string id)
        {
            Debug.Log($"[PALINODE] marker {id}");
            var root = GameRoot.Instance;
            if (root != null) Debug.Log($"[PALINODE] mix @{id} {root.Audio.MixReport()}");
            MarkerReached?.Invoke(id);
        }

        public static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
