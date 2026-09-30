using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Audio
{
    public enum AudioCategory { Ambience, Music, Sfx, Voice }

    /// <summary>
    /// Owns every AudioSource of the game. Volume of a source = base * fade * category duck * silence.
    /// Master volume is applied through AudioListener.volume. All fades run on scaled time so debug speed-up works.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public AudioCategory Category;
            public float BaseVolume = 1f;
            public float Fade = 1f;
            public float FadeFrom = 1f, FadeTo = 1f, FadeTime, FadeDuration;
            public bool StopAfterFade;
            public bool ThroughSilence;   // one-shot that keeps ringing when Silence() cuts everything else
            public string Id;
        }

        private readonly Dictionary<string, Voice> _loops = new Dictionary<string, Voice>();
        private readonly List<Voice> _oneShots = new List<Voice>();
        private readonly Stack<AudioSource> _pool = new Stack<AudioSource>();
        private Voice _music;
        private Voice _voice;

        private float _duck = 1f, _duckFrom = 1f, _duckTo = 1f, _duckTime, _duckDuration;
        private float _silence = 1f, _silenceFrom = 1f, _silenceTo = 1f, _silenceTime, _silenceDuration;

        // Side-chain: music and ambience dip automatically while a voice line plays.
        private float _voiceDuck = 1f;
        public float VoiceDuckLevel { get; set; } = 0.45f;   // linear (≈ −7 dB)
        public float VoiceDuckAttack { get; set; } = 0.15f;
        public float VoiceDuckRelease { get; set; } = 0.7f;

        /// <summary>Clip name → integrated loudness (LUFS). Used for loudness-based volumes and mix logging.</summary>
        public System.Func<string, float> ClipLufs { get; set; }

        /// <summary>Linear volume that plays <paramref name="clip"/> at <paramref name="targetLufs"/> (clamped to 1).</summary>
        public float VolumeForLufs(string clipName, float targetLufs, float fallback)
        {
            float l = ClipLufs != null ? ClipLufs(clipName) : float.NaN;
            if (float.IsNaN(l)) return fallback;
            float v = Mathf.Pow(10f, (targetLufs - l) / 20f);
            if (v > 1.001f) Debug.LogWarning($"[PALINODE] {clipName} is {l:0.0} LUFS; target {targetLufs:0.0} needs +{20f * Mathf.Log10(v):0.0} dB (capped at 0 dB)");
            return Mathf.Clamp01(v);
        }

        public float MasterVolume
        {
            get => AudioListener.volume;
            set => AudioListener.volume = Mathf.Clamp01(value);
        }

        public bool IsSilenced => _silenceTo < 0.001f;

        public IEnumerable<string> LoopIds => _loops.Keys;

        public string MusicClipName => _music != null && _music.Source.clip != null && !_music.StopAfterFade ? _music.Source.clip.name : null;

        private AudioSource CreateSource(string name)
        {
            if (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                pooled.gameObject.SetActive(true);
                pooled.name = name;
                return pooled;
            }
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

        private void Release(Voice v)
        {
            v.Source.Stop();
            v.Source.clip = null;
            v.Source.gameObject.SetActive(false);
            _pool.Push(v.Source);
        }

        // ---------------- Loops (ambience) ----------------

        public bool IsLoopPlaying(string id) => _loops.TryGetValue(id, out var v) && !v.StopAfterFade;

        public void PlayLoop(string id, AudioClip clip, float volume, float fadeIn)
        {
            if (clip == null) return;
            if (_loops.TryGetValue(id, out var existing))
            {
                existing.StopAfterFade = false;
                existing.BaseVolume = volume;
                if (existing.Source.clip != clip)
                {
                    existing.Source.clip = clip;
                    existing.Source.Play();
                }
                StartFade(existing, 1f, fadeIn);
                return;
            }
            var v = new Voice { Source = CreateSource("Loop_" + id), Category = AudioCategory.Ambience, BaseVolume = volume, Id = id };
            v.Source.clip = clip;
            v.Source.loop = true;
            v.Source.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f)) * (clip.length > 4f ? 1f : 0f);
            v.Fade = fadeIn > 0f ? 0f : 1f;
            StartFade(v, 1f, fadeIn);
            v.Source.Play();
            _loops[id] = v;
            Apply(v);
        }

        public void SetLoopVolume(string id, float volume, float duration)
        {
            if (!_loops.TryGetValue(id, out var v)) return;
            // Express as a fade relative to the new base volume so the change is smooth.
            float current = v.BaseVolume * v.Fade;
            v.BaseVolume = Mathf.Max(0.0001f, volume);
            v.Fade = current / v.BaseVolume;
            StartFade(v, 1f, duration);
        }

        public void StopLoop(string id, float fadeOut)
        {
            if (!_loops.TryGetValue(id, out var v)) return;
            if (fadeOut <= 0f)
            {
                _loops.Remove(id);
                Release(v);
                return;
            }
            v.StopAfterFade = true;
            StartFade(v, 0f, fadeOut);
        }

        public void StopAllLoops(float fadeOut)
        {
            foreach (var id in new List<string>(_loops.Keys)) StopLoop(id, fadeOut);
        }

        // ---------------- Music ----------------

        /// <param name="loop">false for through-composed cues (they end on their own baked fade-out)</param>
        public void PlayMusic(AudioClip clip, float volume, float fadeIn, bool loop = true)
        {
            if (clip == null) return;
            if (_music != null && _music.Source.clip == clip && !_music.StopAfterFade)
            {
                _music.BaseVolume = volume;
                StartFade(_music, 1f, fadeIn);
                return;
            }
            StopMusic(fadeIn);
            var v = new Voice { Source = CreateSource("Music_" + clip.name), Category = AudioCategory.Music, BaseVolume = volume };
            v.Source.clip = clip;
            v.Source.loop = loop;
            v.Fade = fadeIn > 0f ? 0f : 1f;
            StartFade(v, 1f, fadeIn);
            v.Source.Play();
            _music = v;
            Apply(v);
        }

        /// <summary>Automate the music level (smooth, relative to the current level).</summary>
        public void SetMusicVolume(float volume, float duration)
        {
            if (_music == null) return;
            float current = _music.BaseVolume * _music.Fade;
            _music.BaseVolume = Mathf.Max(0.0001f, volume);
            _music.Fade = current / _music.BaseVolume;
            StartFade(_music, 1f, duration);
        }

        public float MusicVolume => _music != null ? _music.BaseVolume : 0f;

        public void StopMusic(float fadeOut)
        {
            if (_music == null) return;
            var m = _music;
            _music = null;
            if (fadeOut <= 0f)
            {
                Release(m);
                return;
            }
            m.StopAfterFade = true;
            StartFade(m, 0f, fadeOut);
            _oneShots.Add(m); // let the one-shot list own the fading tail
        }

        // ---------------- One-shots & voice ----------------

        /// <param name="throughSilence">the sound is not cut by Silence() and is not muted while silenced</param>
        public AudioSource PlayOneShot(AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = false, float pan = 0f,
            bool throughSilence = false)
        {
            if (clip == null) return null;
            var v = new Voice { Source = CreateSource("Sfx_" + clip.name), Category = AudioCategory.Sfx, BaseVolume = volume,
                ThroughSilence = throughSilence };
            v.Source.clip = clip;
            v.Source.loop = loop;
            v.Source.pitch = pitch;
            v.Source.panStereo = Mathf.Clamp(pan, -1f, 1f);
            v.Source.Play();
            _oneShots.Add(v);
            Apply(v);
            return v.Source;
        }

        public void StopOneShot(AudioSource src, float fadeOut)
        {
            foreach (var v in _oneShots)
            {
                if (v.Source != src) continue;
                v.StopAfterFade = true;
                StartFade(v, 0f, Mathf.Max(0.01f, fadeOut));
            }
        }

        public float PlayVoice(AudioClip clip, float volume = 1f)
        {
            StopVoice();
            if (clip == null) return 0f;
            _voice = new Voice { Source = CreateSource("Voice_" + clip.name), Category = AudioCategory.Voice, BaseVolume = volume };
            _voice.Source.clip = clip;
            _voice.Source.loop = false;
            _voice.Source.Play();
            Apply(_voice);
            return clip.length;
        }

        public void StopVoice()
        {
            if (_voice == null) return;
            Release(_voice);
            _voice = null;
        }

        // ---------------- Global mix ----------------

        /// <summary>Lower ambience/music/sfx (not voice) to <paramref name="level"/>.</summary>
        public void Duck(float level, float duration)
        {
            _duckFrom = _duck;
            _duckTo = Mathf.Clamp01(level);
            _duckTime = 0f;
            _duckDuration = Mathf.Max(0f, duration);
            if (_duckDuration <= 0f) _duck = _duckTo;
        }

        /// <summary>Instant (or faded) total silence. Music is stopped for good; loops keep running muted.</summary>
        public void Silence(float duration = 0f)
        {
            StopMusic(duration);
            foreach (var v in _oneShots)
            {
                if (v.ThroughSilence) continue;
                v.StopAfterFade = true;
                if (duration <= 0f) v.Source.Stop();
            }
            _silenceFrom = _silence;
            _silenceTo = 0f;
            _silenceTime = 0f;
            _silenceDuration = duration;
            if (duration <= 0f) _silence = 0f;
            StopVoice();
            ApplyAll();
        }

        public void Unsilence(float duration)
        {
            _silenceFrom = _silence;
            _silenceTo = 1f;
            _silenceTime = 0f;
            _silenceDuration = duration;
            if (duration <= 0f) _silence = 1f;
        }

        /// <summary>Hard reset used when leaving the prologue (menu, floor).</summary>
        public void StopEverything(float fade)
        {
            StopAllLoops(fade);
            StopMusic(fade);
            StopVoice();
            foreach (var v in _oneShots)
            {
                v.StopAfterFade = true;
                StartFade(v, 0f, Mathf.Max(0.01f, fade));
            }
            _duck = _duckTo = 1f;
            _silence = _silenceTo = 1f;
        }

        public bool IsVoicePlaying => _voice != null && _voice.Source.isPlaying;

        private static void StartFade(Voice v, float to, float duration)
        {
            v.FadeFrom = v.Fade;
            v.FadeTo = to;
            v.FadeTime = 0f;
            v.FadeDuration = Mathf.Max(0f, duration);
            if (v.FadeDuration <= 0f) v.Fade = to;
        }

        private float CategoryMul(AudioCategory c)
        {
            switch (c)
            {
                case AudioCategory.Voice: return _silence;
                case AudioCategory.Music:
                case AudioCategory.Ambience: return _duck * _silence * _voiceDuck;
                default: return _duck * _silence;
            }
        }

        public void SetPan(AudioSource src, float pan)
        {
            if (src != null) src.panStereo = Mathf.Clamp(pan, -1f, 1f);
        }

        public void SetOneShotVolume(AudioSource src, float volume)
        {
            foreach (var v in _oneShots)
                if (v.Source == src) { v.BaseVolume = volume; Apply(v); }
        }

        /// <summary>
        /// Estimated loudness per bus (LUFS, power sum of clip loudness + 20·log10(effective volume · master)).
        /// </summary>
        public string MixReport()
        {
            var bus = new Dictionary<AudioCategory, double>();
            var lines = new List<string>();
            void Add(Voice v)
            {
                if (v == null || v.Source == null || !v.Source.isPlaying || v.Source.clip == null) return;
                string name = v.Category == AudioCategory.Voice && v.Source.clip != null ? VoiceKey(v.Source.clip.name) : v.Source.clip.name;
                float l = ClipLufs != null ? ClipLufs(name) : float.NaN;
                if (float.IsNaN(l) && v.Category == AudioCategory.Voice && ClipLufs != null) l = ClipLufs("en/" + v.Source.clip.name);
                float vol = v.Source.volume * MasterVolume;
                if (float.IsNaN(l) || vol <= 0.0001f) return;
                float eff = l + 20f * Mathf.Log10(vol);
                bus.TryGetValue(v.Category, out var p);
                bus[v.Category] = p + Mathf.Pow(10f, eff / 10f);
                lines.Add($"{v.Source.clip.name}:{eff:0.0}");
            }
            foreach (var v in _loops.Values) Add(v);
            foreach (var v in _oneShots) Add(v);
            Add(_music);
            Add(_voice);
            string B(AudioCategory c) => bus.TryGetValue(c, out var p) && p > 0 ? (10 * System.Math.Log10(p)).ToString("0.0") : "—";
            double tot = 0;
            foreach (var p in bus.Values) tot += p;
            string total = tot > 0 ? (10 * System.Math.Log10(tot)).ToString("0.0") : "—";
            return $"music={B(AudioCategory.Music)} amb={B(AudioCategory.Ambience)} sfx={B(AudioCategory.Sfx)} voice={B(AudioCategory.Voice)} total={total} LUFS; out rms={_outRmsDb:0.0} peak={_outPeakDb:0.0} dBFS [{string.Join(", ", lines)}]";
        }

        /// <summary>Voice clips are registered as "en/VO_x"; the playing clip only knows "VO_x".</summary>
        public string VoiceFolder { get; set; } = "en";
        private string VoiceKey(string clip) => VoiceFolder + "/" + clip;

        // Measured output (listener) — valid only when an audio device is present.
        private readonly float[] _outBuf = new float[1024];
        private float _outRmsDb = -120f, _outPeakDb = -120f;

        private void MeasureOutput()
        {
            AudioListener.GetOutputData(_outBuf, 0);
            double s = 0;
            float pk = 0f;
            foreach (var x in _outBuf) { s += x * x; pk = Mathf.Max(pk, Mathf.Abs(x)); }
            float rms = (float)(10 * System.Math.Log10(System.Math.Max(1e-12, s / _outBuf.Length)));
            _outRmsDb = Mathf.Lerp(_outRmsDb, rms, 0.2f);
            _outPeakDb = Mathf.Max(_outPeakDb - Time.unscaledDeltaTime * 20f, 20f * Mathf.Log10(Mathf.Max(1e-6f, pk)));
        }

        private void Apply(Voice v)
        {
            float mul = v.ThroughSilence ? _duck : CategoryMul(v.Category);
            v.Source.volume = Mathf.Clamp01(v.BaseVolume * v.Fade * mul);
        }

        private void ApplyAll()
        {
            foreach (var v in _loops.Values) Apply(v);
            foreach (var v in _oneShots) Apply(v);
            if (_music != null) Apply(_music);
            if (_voice != null) Apply(_voice);
        }

        private static bool TickFade(Voice v, float dt)
        {
            if (v.FadeDuration <= 0f) { v.Fade = v.FadeTo; return v.StopAfterFade && v.Fade <= 0.0001f; }
            v.FadeTime += dt;
            float t = Mathf.Clamp01(v.FadeTime / v.FadeDuration);
            v.Fade = Mathf.Lerp(v.FadeFrom, v.FadeTo, t);
            return v.StopAfterFade && t >= 1f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            MeasureOutput();
            bool speaking = _voice != null && _voice.Source.isPlaying;
            float target = speaking ? VoiceDuckLevel : 1f;
            float rate = speaking ? VoiceDuckAttack : VoiceDuckRelease;
            _voiceDuck = Mathf.MoveTowards(_voiceDuck, target, dt / Mathf.Max(0.01f, rate) * (1f - VoiceDuckLevel));

            if (_duckDuration > 0f)
            {
                _duckTime += dt;
                _duck = Mathf.Lerp(_duckFrom, _duckTo, Mathf.Clamp01(_duckTime / _duckDuration));
            }
            if (_silenceDuration > 0f)
            {
                _silenceTime += dt;
                _silence = Mathf.Lerp(_silenceFrom, _silenceTo, Mathf.Clamp01(_silenceTime / _silenceDuration));
            }

            List<string> deadLoops = null;
            foreach (var kv in _loops)
            {
                if (TickFade(kv.Value, dt)) (deadLoops ??= new List<string>()).Add(kv.Key);
                Apply(kv.Value);
            }
            if (deadLoops != null)
            {
                foreach (var id in deadLoops)
                {
                    Release(_loops[id]);
                    _loops.Remove(id);
                }
            }

            if (_music != null)
            {
                TickFade(_music, dt);
                Apply(_music);
            }
            if (_voice != null)
            {
                Apply(_voice);
                if (!_voice.Source.isPlaying && _voice.Source.time <= 0f) { Release(_voice); _voice = null; }
            }

            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                var v = _oneShots[i];
                bool faded = TickFade(v, dt);
                Apply(v);
                bool finished = !v.Source.isPlaying && !v.Source.loop;
                if (faded || finished || (v.StopAfterFade && IsSilenced))
                {
                    _oneShots.RemoveAt(i);
                    Release(v);
                }
            }
        }
    }
}
