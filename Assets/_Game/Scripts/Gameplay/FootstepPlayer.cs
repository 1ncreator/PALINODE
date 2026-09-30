using System.Collections.Generic;
using Palinode.Audio;
using UnityEngine;

namespace Palinode.Gameplay
{
    /// <summary>
    /// Plays single footsteps cut out of a longer "footsteps" recording. Step onsets are detected once from the
    /// clip's waveform so every footfall of the walk cycle triggers exactly one real step sound.
    /// </summary>
    public sealed class FootstepPlayer : MonoBehaviour
    {
        private const float SliceLength = 0.42f;

        private AudioSource _a, _b;
        private bool _useA;
        private AudioClip _clip;
        private readonly List<float> _onsets = new List<float>();
        private int _next;
        private float _volume = 0.7f;

        public void Init(AudioClip clip, float volume)
        {
            _clip = clip;
            _volume = volume;
            _a = gameObject.AddComponent<AudioSource>();
            _b = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { _a, _b })
            {
                s.playOnAwake = false;
                s.clip = clip;
                s.spatialBlend = 0f;
            }
            DetectOnsets();
        }

        private void DetectOnsets()
        {
            _onsets.Clear();
            if (_clip == null) return;
            try
            {
                if (_clip.loadState != AudioDataLoadState.Loaded) _clip.LoadAudioData();
                int ch = _clip.channels;
                int total = _clip.samples;
                var data = new float[total * ch];
                if (!_clip.GetData(data, 0)) throw new System.Exception("GetData failed");
                int win = Mathf.Max(1, _clip.frequency / 100); // 10 ms
                int n = total / win;
                var env = new float[n];
                float max = 0f;
                for (int i = 0; i < n; i++)
                {
                    float m = 0f;
                    int start = i * win * ch;
                    for (int k = 0; k < win * ch; k += ch * 2) m = Mathf.Max(m, Mathf.Abs(data[start + k]));
                    env[i] = m;
                    max = Mathf.Max(max, m);
                }
                float hi = max * 0.22f, lo = max * 0.1f;
                bool armed = true;
                float last = -1f;
                for (int i = 0; i < n; i++)
                {
                    if (armed && env[i] > hi)
                    {
                        float t = Mathf.Max(0f, i * 0.01f - 0.02f);
                        if (t - last > 0.28f && t < _clip.length - SliceLength)
                        {
                            _onsets.Add(t);
                            last = t;
                        }
                        armed = false;
                    }
                    else if (!armed && env[i] < lo) armed = true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PALINODE] Footstep onset detection failed ({e.Message}); using fixed slices.");
            }
            if (_onsets.Count < 4)
            {
                _onsets.Clear();
                for (float t = 0.3f; t < _clip.length - SliceLength; t += 0.5f) _onsets.Add(t);
            }
        }

        public int OnsetCount => _onsets.Count;

        public void PlayStep(float masterMul = 1f)
        {
            if (_clip == null || _onsets.Count == 0) return;
            var src = _useA ? _a : _b;
            _useA = !_useA;
            _next = (_next + 1 + Random.Range(0, 3)) % _onsets.Count;
            src.time = _onsets[_next];
            src.volume = _volume * masterMul * Random.Range(0.8f, 1f);
            src.pitch = Random.Range(0.94f, 1.06f);
            src.Play();
            if (src == _a) _endA = Time.unscaledTime + SliceLength; else _endB = Time.unscaledTime + SliceLength;
        }

        private float _endA, _endB;

        private void Update()
        {
            Tail(_a, _endA);
            Tail(_b, _endB);
        }

        private static void Tail(AudioSource s, float end)
        {
            if (s == null || !s.isPlaying) return;
            float left = end - Time.unscaledTime;
            if (left <= 0f) s.Stop();
            else if (left < 0.08f) s.volume *= 0.7f;
        }
    }
}
