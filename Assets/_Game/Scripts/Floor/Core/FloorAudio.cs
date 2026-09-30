using System.Collections.Generic;
using Palinode.Audio;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Sound events of Floor I resolved through Floor1/Data/audio_manifest.json. A missing file is replaced by its
    /// fallback (another clip, a procedural sound or silence) with exactly one warning per file in the log.
    /// </summary>
    public sealed class FloorAudio
    {
        private sealed class Resolved
        {
            public AudioClip Clip;
            public float Volume = 1f, Pitch = 1f, Jitter;
        }

        private readonly GameConfig _config;
        private readonly AudioDirector _director;
        private readonly JNode _manifest;
        private readonly Dictionary<string, Resolved> _cache = new Dictionary<string, Resolved>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly Dictionary<string, AudioClip> Synth = new Dictionary<string, AudioClip>();

        public FloorAudio(GameConfig config, AudioDirector director, JNode manifest)
        {
            _config = config;
            _director = director;
            _manifest = manifest;
        }

        public AudioDirector Director => _director;

        /// <summary>
        /// Files the manifest names that are not in the project yet (for the report and tests):
        /// (file, isExpected, fallback description).
        /// </summary>
        public static List<(string file, bool expected, string fallback)> Missing(JNode manifest, GameConfig config)
        {
            var list = new List<(string, bool, string)>();
            void Check(JNode e)
            {
                string f = e.Str("file");
                if (string.IsNullOrEmpty(f) || (config != null && config.HasClip(f))) return;
                var fb = e["fallback"];
                string desc = fb.Has("clip") ? "clip " + fb.Str("clip") : fb.Has("synth") ? "synth " + fb.Str("synth") : fb.Bool("silence") ? "silence" : "none";
                list.Add((f, e.Bool("expected"), desc));
            }
            foreach (var kv in manifest["events"].Pairs()) Check(kv.Value);
            foreach (var kv in manifest["music"].Pairs()) Check(kv.Value);
            return list;
        }

        private Resolved Resolve(string section, string id)
        {
            string key = section + "/" + id;
            if (_cache.TryGetValue(key, out var r)) return r;
            r = new Resolved();
            var e = _manifest[section][id];
            if (e.IsNull)
            {
                Warn(key, $"[PALINODE] Sound event '{id}' is not in audio_manifest.json.");
                _cache[key] = r;
                return r;
            }
            r.Volume = e.Num("volume", 1f);
            r.Pitch = e.Num("pitch", 1f);
            r.Jitter = e.Num("pitchJitter", 0f);
            string file = e.Str("file");
            if (!string.IsNullOrEmpty(file) && _config.HasClip(file))
            {
                r.Clip = _config.Clip(file);
            }
            else
            {
                var fb = e["fallback"];
                string what;
                if (fb.Has("clip") && _config.HasClip(fb.Str("clip")))
                {
                    r.Clip = _config.Clip(fb.Str("clip"));
                    r.Pitch *= fb.Num("pitch", 1f);
                    what = "stand-in clip " + fb.Str("clip");
                }
                else if (fb.Has("synth"))
                {
                    string k = fb.Str("synth");
                    if (!Synth.TryGetValue(k, out var clip) || clip == null) Synth[k] = clip = SfxSynth.Create(k);
                    r.Clip = clip;
                    what = "procedural '" + k + "'";
                }
                else what = "silence";
                Warn(file ?? key, e.Bool("expected")
                    ? $"[PALINODE] Audio '{file}' is not delivered yet — playing {what}. Put {file}.wav into the Floor I audio folder and rebuild."
                    : $"[PALINODE] Audio '{file}' is missing — playing {what}.");
            }
            _cache[key] = r;
            return r;
        }

        private static void Warn(string key, string message)
        {
            if (Warned.Add(key)) Debug.LogWarning(message);
        }

        public AudioSource Play(string id, float volumeMul = 1f, float pan = 0f)
        {
            var r = Resolve("events", id);
            if (r.Clip == null || _director == null) return null;
            float pitch = r.Pitch * (1f + Random.Range(-r.Jitter, r.Jitter));
            return _director.PlayOneShot(r.Clip, r.Volume * volumeMul, pitch, false, pan);
        }

        /// <summary>A looping event (flywheel, engine); stop it with <see cref="Stop"/>.</summary>
        public AudioSource PlayLoop(string id, float volumeMul = 1f)
        {
            var r = Resolve("events", id);
            if (r.Clip == null || _director == null) return null;
            return _director.PlayOneShot(r.Clip, r.Volume * volumeMul, r.Pitch, true);
        }

        public void Stop(AudioSource src, float fade = 0.3f)
        {
            if (src != null && _director != null) _director.StopOneShot(src, fade);
        }

        public void SetVolume(AudioSource src, string id, float volumeMul)
        {
            if (src == null || _director == null) return;
            _director.SetOneShotVolume(src, Resolve("events", id).Volume * volumeMul);
        }

        public float Length(string id)
        {
            var r = Resolve("events", id);
            return r.Clip != null ? r.Clip.length / Mathf.Max(0.1f, r.Pitch) : 0f;
        }

        /// <summary>Music by id ("floor", "boss", "lullaby"); a missing track = silence (music stops).</summary>
        public void Music(string id, float fade = 2f)
        {
            if (_director == null) return;
            var r = Resolve("music", id);
            if (r.Clip == null) { _director.StopMusic(fade); return; }
            _director.PlayMusic(r.Clip, r.Volume, fade, true);
        }

        public void StopMusic(float fade = 1.5f) => _director?.StopMusic(fade);

        /// <summary>Starts an ambience loop, choosing one of its files at random.</summary>
        public void Ambience(string id, SeededRandom rng, float fade = 2f)
        {
            if (_director == null) return;
            var e = _manifest["ambience"][id];
            var files = new List<string>();
            foreach (var f in e["files"].Items())
                if (_config.HasClip(f.AsString())) files.Add(f.AsString());
            if (files.Count == 0) return;
            string pick = files[rng != null ? rng.Range(0, files.Count) : Random.Range(0, files.Count)];
            _director.PlayLoop("f1_" + id, _config.Clip(pick), e.Num("volume", 0.3f), fade);
        }

        public void StopAmbience(string id, float fade = 1f) => _director?.StopLoop("f1_" + id, fade);
    }
}
