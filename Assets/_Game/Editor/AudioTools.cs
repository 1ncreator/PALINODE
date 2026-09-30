using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Palinode.Core;
using UnityEditor;
using UnityEngine;

namespace Palinode.Editor
{
    /// <summary>
    /// Audio measurement and offline processing.
    ///  • Measure: peak dBFS, RMS dBFS, integrated loudness (ITU-R BS.1770 K-weighting + gating, LUFS),
    ///    low-frequency energy share and attack (first strong onset) for every clip → Data/audio_levels.json.
    ///    The runtime uses the table to set volumes by target loudness ("lufs" in prologue.json) and to log the mix.
    ///  • Process: Data/audio_processing.json recipes (gain / normalise / saturation / table-rattle layer)
    ///    → Audio/Processed/*.wav.
    /// Idempotent (hash cache).
    /// </summary>
    public static class AudioTools
    {
        public const string AudioRoot = "Assets/_Game/Prologue/Audio";
        public const string ProcessedDir = AudioRoot + "/Processed";
        public const string LevelsPath = "Assets/_Game/Prologue/Data/audio_levels.json";
        public const string RecipesPath = "Assets/_Game/Prologue/Data/audio_processing.json";
        private const string CachePath = ProcessedDir + "/.cache.json";
        private const int Version = 3;

        public sealed class Levels
        {
            public float Peak, Rms, Lufs, Low, Length, Attack;
        }

        // ------------------------------------------------------------------ processing

        public static void Process()
        {
            Directory.CreateDirectory(ProcessedDir);
            if (!File.Exists(RecipesPath)) return;
            var cfg = JNode.Parse(File.ReadAllText(RecipesPath));
            var cache = File.Exists(CachePath) ? JNode.Parse(File.ReadAllText(CachePath)) : new JNode(null);
            var newCache = new Dictionary<string, object>();
            foreach (var m in cfg["music"].Items())
            {
                string src = m.Str("src");
                string outPath = ProcessedDir + "/" + m.Str("out") + ".wav";
                string hash = FileHash(src) + Version + Serialize(m.Raw);
                newCache[m.Str("out")] = hash;
                if (cache[m.Str("out")].AsString() == hash && File.Exists(outPath)) continue;
                if (!ReadWav(src, out var samples, out int channels, out int rate))
                    throw new Exception("Cannot read music WAV: " + src);
                var result = ProcessMusic(samples, channels, rate, m, out string info);
                WriteWav16(outPath, result, channels, rate);
                Debug.Log($"[PALINODE] Music {m.Str("out")}: {info}");
            }
            foreach (var r in cfg["clips"].Items())
            {
                string src = AudioRoot + "/" + r.Str("src");
                string outPath = ProcessedDir + "/" + r.Str("out") + ".wav";
                string hash = FileHash(src) + Version + r.ToString() + Serialize(r.Raw);
                newCache[r.Str("out")] = hash;
                if (cache[r.Str("out")].AsString() == hash && File.Exists(outPath)) continue;
                if (!ReadWav(src, out var samples, out int channels, out int rate))
                    throw new Exception("Processing needs a PCM/float WAV source: " + src);
                var mono = ToMono(samples, channels);
                var result = ApplyRecipe(mono, rate, r);
                WriteWav16(outPath, result, 1, rate);
                Debug.Log($"[PALINODE] Audio processed {r.Str("src")} → {Path.GetFileName(outPath)}");
            }
            File.WriteAllText(CachePath, Serialize(newCache));
        }

        /// <summary>
        /// Music recipe (interleaved, channel count preserved):
        ///   start / length (s)                    — explicit cut
        ///   alignPeak { at, window, length }      — cut so that the loudest "window"-second passage lands at "at" s
        ///   endAtLastLoud { length, tail, db }    — cut ending where the track last stays within "db" of its max (final chord)
        ///   stinger { length, fadeOut }           — pick the onset with the strongest low end (sub boom) and cut "length" s
        ///   loop { crossfade }                    — seamless loop: the material after the cut end is cross-faded into the head
        ///   fadeIn / fadeOut (s), normalizeLufs    — edges and integrated loudness of the result
        /// </summary>
        private static float[] ProcessMusic(float[] s, int ch, int rate, JNode m, out string info)
        {
            int frames = s.Length / ch;
            var mono = ToMono(s, ch);
            double start = m.Num("start", 0f), length = m.Num("length", frames / (float)rate);
            var sbInfo = new StringBuilder();

            if (m.Has("alignPeak"))
            {
                var a = m["alignPeak"];
                float win = a.Num("window", 6f);
                var env = ShortTerm(mono, rate, win);
                int best = 0;
                for (int i = 1; i < env.Length; i++) if (env[i] > env[best]) best = i;
                double peakT = best * 0.5 + win * 0.5;
                start = Math.Max(0, peakT - a.Num("at", 20f));
                length = a.Num("length", (float)length);
                sbInfo.Append($"peak at {peakT:0.0}s → start {start:0.0}s; ");
            }
            if (m.Has("endAtLastLoud"))
            {
                var e = m["endAtLastLoud"];
                var env = ShortTerm(mono, rate, 1f);
                float max = float.MinValue;
                foreach (var v in env) max = Math.Max(max, v);
                int last = 0;
                for (int i = 0; i < env.Length; i++) if (env[i] > max - e.Num("db", 6f)) last = i;
                double end = last * 0.5 + 1.0 + e.Num("tail", 3f);
                length = e.Num("length", 16f) + e.Num("tail", 3f);
                start = Math.Max(0, end - length);
                sbInfo.Append($"last loud at {last * 0.5 + 1:0.0}s → {start:0.0}–{end:0.0}s; ");
            }
            if (m.Has("stinger"))
            {
                var st = m["stinger"];
                start = PickStinger(mono, rate, out string list);
                length = st.Num("length", 4.5f);
                sbInfo.Append($"stinger candidates [{list}] → {start:0.00}s; ");
            }

            float xf = m.Has("loop") ? m["loop"].Num("crossfade", 4f) : 0f;
            int a0 = (int)(start * rate);
            int n = (int)(length * rate);
            int x = (int)(xf * rate);
            if (a0 + n + x > frames) { n = frames - a0 - x; }
            if (n <= rate) throw new Exception("Music cut too short");
            var o = new float[n * ch];
            for (int i = 0; i < n; i++)
            for (int c = 0; c < ch; c++)
                o[i * ch + c] = s[(a0 + i) * ch + c];
            if (x > 0)
            {
                // Equal-power cross-fade of the material following the loop end into the loop head.
                for (int i = 0; i < x; i++)
                {
                    float t = (i + 0.5f) / x;
                    float gin = Mathf.Sin(t * Mathf.PI * 0.5f), gout = Mathf.Cos(t * Mathf.PI * 0.5f);
                    for (int c = 0; c < ch; c++)
                        o[i * ch + c] = s[(a0 + i) * ch + c] * gin + s[(a0 + n + i) * ch + c] * gout;
                }
                sbInfo.Append($"loop {length:0.0}s xfade {xf:0.0}s; ");
            }
            ApplyFade(o, ch, rate, m.Num("fadeIn", x > 0 ? 0f : 0.03f), true);
            ApplyFade(o, ch, rate, m.Num("fadeOut", x > 0 ? 0f : 2f), false);
            if (m.Has("normalizeLufs"))
            {
                float l = IntegratedLoudness(o, ch, rate);
                float g = Mathf.Pow(10f, (m.Num("normalizeLufs") - l) / 20f);
                float pk = 0f;
                foreach (var v in o) pk = Math.Max(pk, Math.Abs(v));
                float maxG = pk > 0f ? Mathf.Pow(10f, -1f / 20f) / pk : g; // keep 1 dB headroom
                g = Math.Min(g, maxG);
                for (int i = 0; i < o.Length; i++) o[i] *= g;
                sbInfo.Append($"{l:0.0} → {IntegratedLoudness(o, ch, rate):0.0} LUFS");
            }
            info = $"{start:0.0}s + {length:0.0}s; " + sbInfo;
            return o;
        }

        private static float[] ShortTerm(float[] mono, int rate, float window)
        {
            int hop = rate / 2, win = (int)(rate * window);
            int count = Math.Max(1, (mono.Length - win) / hop);
            var env = new float[count];
            for (int k = 0; k < count; k++)
            {
                double a = 0;
                for (int i = k * hop; i < k * hop + win && i < mono.Length; i++) a += mono[i] * mono[i];
                env[k] = (float)(10 * Math.Log10(Math.Max(1e-12, a / win)));
            }
            return env;
        }

        /// <summary>Onsets (50 ms level jumps ≥ 12 dB) scored by low-frequency energy of the following second.</summary>
        private static float PickStinger(float[] mono, int rate, out string list)
        {
            int w = rate / 20;
            int nWin = mono.Length / w;
            var e = new float[nWin];
            for (int k = 0; k < nWin; k++)
            {
                double a = 0;
                for (int i = k * w; i < (k + 1) * w; i++) a += mono[i] * mono[i];
                e[k] = (float)(10 * Math.Log10(Math.Max(1e-12, a / w)));
            }
            var sb = new StringBuilder();
            float bestScore = float.MinValue, bestT = 0f;
            int lastOnset = -100;
            for (int k = 6; k < nWin - 20; k++)
            {
                float floor = float.MaxValue;
                for (int j = k - 6; j < k; j++) floor = Math.Min(floor, e[j]);
                if (e[k] - floor < 12f || e[k] < -30f || k - lastOnset < 40) continue;
                lastOnset = k;
                // low-pass energy (< 120 Hz) and total energy over the next second
                double lp = 0, lo = 0, tot = 0;
                int s0 = k * w, s1 = Math.Min(mono.Length, s0 + rate);
                for (int i = s0; i < s1; i++)
                {
                    lp += (2 * Math.PI * 120 / rate) * (mono[i] - lp);
                    lo += lp * lp;
                    tot += mono[i] * mono[i];
                }
                float lowDb = (float)(10 * Math.Log10(Math.Max(1e-12, lo / (s1 - s0))));
                float score = lowDb + (e[k] - floor) * 0.5f;
                float t = k * 0.05f;
                sb.Append($"{t:0.0}s low {lowDb:0} jump {e[k] - floor:0}; ");
                if (score > bestScore) { bestScore = score; bestT = t; }
            }
            list = sb.ToString();
            return Math.Max(0f, bestT - 0.05f);
        }

        private static void ApplyFade(float[] o, int ch, int rate, float seconds, bool fadeIn)
        {
            int n = (int)(seconds * rate), frames = o.Length / ch;
            n = Math.Min(n, frames);
            for (int i = 0; i < n; i++)
            {
                float g = (i + 0.5f) / n;
                g = g * g * (3f - 2f * g);
                int f = fadeIn ? i : frames - 1 - i;
                for (int c = 0; c < ch; c++) o[f * ch + c] *= g;
            }
        }

        private static float[] ApplyRecipe(float[] x, int rate, JNode r)
        {
            var y = (float[])x.Clone();
            // Optional saturation: generates upper harmonics so a low buzz is audible on small speakers.
            float drive = r.Num("drive", 0f);
            if (drive > 0f)
            {
                float pk = Peak(y);
                float g = pk > 0f ? drive / pk : 1f;
                for (int i = 0; i < y.Length; i++) y[i] = (float)Math.Tanh(y[i] * g) / (float)Math.Tanh(drive);
            }
            // Table rattle: short high-passed noise clicks at the motor rate, following the source envelope.
            if (r.Has("rattle"))
            {
                var rt = r["rattle"];
                float rateHz = rt.Num("rate", 170f), level = rt.Num("level", 0.5f), decayMs = rt.Num("decayMs", 1.6f);
                var env = Envelope(x, rate, 0.012f);
                float envMax = Math.Max(1e-9f, Peak(env));
                var rng = new System.Random(7);
                var rattle = new float[y.Length];
                double next = 0;
                float decay = (float)Math.Exp(-1.0 / (decayMs * 0.001 * rate));
                float click = 0f, hp = 0f, prev = 0f;
                for (int i = 0; i < y.Length; i++)
                {
                    if (i >= next)
                    {
                        float e = env[i] / envMax;
                        click = e > 0.12f ? (float)Math.Pow(e, 1.4) * (0.6f + 0.4f * (float)rng.NextDouble()) : 0f;
                        next = i + rate / rateHz * (0.85 + 0.3 * rng.NextDouble());
                    }
                    float n = (float)(rng.NextDouble() * 2 - 1) * click;
                    click *= decay;
                    hp = 0.92f * (hp + n - prev); // one-pole high-pass (≈600 Hz at 48 kHz)
                    prev = n;
                    rattle[i] = hp;
                }
                Normalize(rattle, 1f);
                float yp = Math.Max(1e-9f, Peak(y));
                for (int i = 0; i < y.Length; i++) y[i] = y[i] / yp + rattle[i] * level;
            }
            float gainDb = r.Num("gainDb", 0f);
            if (Math.Abs(gainDb) > 0.001f)
            {
                float g = Mathf.Pow(10f, gainDb / 20f);
                for (int i = 0; i < y.Length; i++) y[i] *= g;
            }
            if (r.Has("normalizePeak")) Normalize(y, Mathf.Pow(10f, r.Num("normalizePeak") / 20f));
            for (int i = 0; i < y.Length; i++) y[i] = Mathf.Clamp(y[i], -1f, 1f);
            return y;
        }

        private static float[] Envelope(float[] x, int rate, float windowSec)
        {
            int w = Math.Max(1, (int)(rate * windowSec));
            var env = new float[x.Length];
            double acc = 0;
            for (int i = 0; i < x.Length; i++)
            {
                acc += x[i] * x[i];
                if (i >= w) acc -= x[i - w] * x[i - w];
                env[i] = (float)Math.Sqrt(Math.Max(0, acc / w));
            }
            return env;
        }

        private static float Peak(float[] x)
        {
            float p = 0f;
            foreach (var v in x) p = Math.Max(p, Math.Abs(v));
            return p;
        }

        private static void Normalize(float[] x, float target)
        {
            float p = Peak(x);
            if (p <= 1e-9f) return;
            float g = target / p;
            for (int i = 0; i < x.Length; i++) x[i] *= g;
        }

        private static float[] ToMono(float[] s, int ch)
        {
            if (ch == 1) return s;
            var m = new float[s.Length / ch];
            for (int i = 0; i < m.Length; i++)
            {
                float a = 0f;
                for (int c = 0; c < ch; c++) a += s[i * ch + c];
                m[i] = a / ch;
            }
            return m;
        }

        /// <summary>Batch: prints a 2-second loudness envelope and silent gaps of every WAV in ArtInbox/music.</summary>
        public static void AnalyzeInboxBatch()
        {
            int code = 0;
            try
            {
                foreach (var f in Directory.GetFiles("ArtInbox/music", "*.wav"))
                {
                    if (!ReadWav(f, out var s, out int ch, out int rate)) { Debug.LogWarning("cannot read " + f); continue; }
                    var mono = ToMono(s, ch);
                    var l = Analyze(s, ch, rate);
                    var sb = new StringBuilder();
                    sb.Append($"[PALINODE] {Path.GetFileName(f)} len={l.Length:0.0}s LUFS={l.Lufs:0.0} peak={l.Peak:0.0}\n  env(2s,dB):");
                    int win = rate * 2;
                    for (int i = 0; i + win <= mono.Length; i += win)
                    {
                        double a = 0;
                        for (int k = i; k < i + win; k++) a += mono[k] * mono[k];
                        sb.Append($" {10 * Math.Log10(Math.Max(1e-12, a / win)):0}");
                    }
                    // silent gaps (100 ms windows below -50 dB) longer than 0.4 s
                    sb.Append("\n  gaps:");
                    int w10 = rate / 10, run = 0;
                    for (int i = 0; i + w10 <= mono.Length; i += w10)
                    {
                        double a = 0;
                        for (int k = i; k < i + w10; k++) a += mono[k] * mono[k];
                        bool quiet = 10 * Math.Log10(Math.Max(1e-12, a / w10)) < -50;
                        if (quiet) run++;
                        else
                        {
                            if (run >= 4) sb.Append($" {(i / (float)rate - run * 0.1f):0.0}-{i / (float)rate:0.0}");
                            run = 0;
                        }
                    }
                    Debug.Log(sb.ToString());
                }
            }
            catch (Exception e) { Debug.LogError(e); code = 1; }
            EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------ measurement

        public static Dictionary<string, Levels> Measure()
        {
            var old = File.Exists(LevelsPath) ? JNode.Parse(File.ReadAllText(LevelsPath)) : new JNode(null);
            var result = new Dictionary<string, Levels>();
            var hashes = new Dictionary<string, string>();
            var files = new List<(string key, string path)>();
            foreach (var f in Directory.GetFiles(AudioRoot + "/SFX")) if (!f.EndsWith(".meta")) files.Add((Path.GetFileNameWithoutExtension(f), f));
            foreach (var lang in new[] { "en", "ru" })
                foreach (var f in Directory.GetFiles(AudioRoot + "/Voice/" + lang)) if (!f.EndsWith(".meta")) files.Add((lang + "/" + Path.GetFileNameWithoutExtension(f), f));
            if (Directory.Exists(ProcessedDir))
                foreach (var f in Directory.GetFiles(ProcessedDir, "*.wav")) files.Add((Path.GetFileNameWithoutExtension(f), f));
            // Floor I clips (copied from the delivery folder by Floor1Builder).
            if (Directory.Exists(Floor1Builder.AudioDir))
                foreach (var f in Directory.GetFiles(Floor1Builder.AudioDir))
                    if (Floor1Builder.IsAudioFile(f)) files.Add((Path.GetFileNameWithoutExtension(f), f));

            foreach (var (key, pathRaw) in files)
            {
                string path = pathRaw.Replace('\\', '/');
                string hash = FileHash(path) + Version;
                hashes[key] = hash;
                var prev = old["clips"][key];
                if (!prev.IsNull && prev.Str("hash") == hash)
                {
                    result[key] = new Levels
                    {
                        Peak = prev.Num("peak"), Rms = prev.Num("rms"), Lufs = prev.Num("lufs"), Low = prev.Num("low"),
                        Length = prev.Num("length"), Attack = prev.Num("attack")
                    };
                    continue;
                }
                if (!Decode(path, out var samples, out int ch, out int rate))
                {
                    Debug.LogWarning("[PALINODE] Cannot decode for measurement: " + path);
                    continue;
                }
                result[key] = Analyze(samples, ch, rate);
            }

            var sb = new StringBuilder();
            sb.Append("{\n  \"note\": \"Generated by Palinode.Editor.AudioTools. peak/rms in dBFS, lufs = integrated loudness (BS.1770), low = energy share below 150 Hz, attack = first strong onset, s.\",\n  \"clips\": {\n");
            var keys = new List<string>(result.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                var l = result[keys[i]];
                sb.Append($"    \"{keys[i]}\": {{\"peak\": {F(l.Peak)}, \"rms\": {F(l.Rms)}, \"lufs\": {F(l.Lufs)}, \"low\": {F(l.Low)}, \"length\": {F(l.Length)}, \"attack\": {F(l.Attack)}, \"hash\": \"{hashes[keys[i]]}\"}}");
                sb.Append(i < keys.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  }\n}\n");
            File.WriteAllText(LevelsPath, sb.ToString());
            return result;
        }

        public static Levels Analyze(float[] s, int ch, int rate)
        {
            int frames = s.Length / ch;
            double sum = 0, lowSum = 0;
            float peak = 0f;
            var lp = new double[ch];
            for (int i = 0; i < frames; i++)
            for (int c = 0; c < ch; c++)
            {
                float v = s[i * ch + c];
                peak = Math.Max(peak, Math.Abs(v));
                sum += v * v;
                lp[c] += (2 * Math.PI * 150 / rate) * (v - lp[c]);
                lowSum += lp[c] * lp[c];
            }
            double ms = sum / Math.Max(1, frames * ch);
            var l = new Levels
            {
                Peak = Db(peak),
                Rms = (float)(10 * Math.Log10(Math.Max(1e-12, ms))),
                Low = (float)(lowSum / Math.Max(1e-12, sum)),
                Length = frames / (float)rate,
                Lufs = IntegratedLoudness(s, ch, rate),
                Attack = FindAttack(s, ch, rate)
            };
            return l;
        }

        /// <summary>Time of the first 10 ms window whose level rises 12 dB over the running floor and within 10 dB of the clip max.</summary>
        public static float FindAttack(float[] s, int ch, int rate)
        {
            int w = Math.Max(1, rate / 100);
            int frames = s.Length / ch;
            int nWin = frames / w;
            if (nWin == 0) return 0f;
            var e = new float[nWin];
            float max = 0f;
            for (int k = 0; k < nWin; k++)
            {
                double a = 0;
                for (int i = k * w; i < (k + 1) * w; i++)
                for (int c = 0; c < ch; c++) a += s[i * ch + c] * s[i * ch + c];
                e[k] = (float)(10 * Math.Log10(Math.Max(1e-12, a / (w * ch))));
                max = Math.Max(max == 0f ? e[k] : max, e[k]);
            }
            float floor = e[0];
            for (int k = 1; k < nWin; k++)
            {
                if (e[k] > floor + 12f && e[k] > max - 10f) return k * w / (float)rate;
                floor = Math.Min(floor * 0.9f + e[k] * 0.1f, Math.Max(floor, e[k]));
            }
            return 0f;
        }

        public static float IntegratedLoudness(float[] s, int ch, int rate)
        {
            // K-weighting: high-shelf (+4 dB @ 1.5 kHz) then RLB high-pass (38 Hz), RBJ biquads.
            var shelf = HighShelf(rate, 1681.97, 3.99984, 0.7071752);
            var hp = HighPass(rate, 38.13547, 0.5003270);
            int frames = s.Length / ch;
            var z = new double[frames];
            for (int c = 0; c < Math.Min(ch, 2); c++)
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, v1 = 0, v2 = 0;
                for (int i = 0; i < frames; i++)
                {
                    double x = s[i * ch + c];
                    double y = shelf[0] * x + shelf[1] * x1 + shelf[2] * x2 - shelf[3] * y1 - shelf[4] * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    double v = hp[0] * y + hp[1] * u1 + hp[2] * u2 - hp[3] * v1 - hp[4] * v2;
                    u2 = u1; u1 = y; v2 = v1; v1 = v;
                    z[i] += v * v;
                }
            }
            int block = (int)(0.4 * rate), step = (int)(0.1 * rate);
            var blocks = new List<double>();
            if (frames < block)
            {
                double a = 0;
                for (int i = 0; i < frames; i++) a += z[i];
                blocks.Add(a / Math.Max(1, frames));
            }
            else
            {
                double acc = 0;
                for (int i = 0; i < block; i++) acc += z[i];
                for (int start = 0; start + block <= frames; start += step)
                {
                    if (start > 0)
                    {
                        for (int i = start - step; i < start; i++) acc -= z[i];
                        for (int i = start + block - step; i < start + block; i++) acc += z[i];
                    }
                    blocks.Add(acc / block);
                }
            }
            double L(double m) => -0.691 + 10 * Math.Log10(Math.Max(1e-12, m));
            var gated = blocks.FindAll(m => L(m) > -70);
            if (gated.Count == 0) return -70f;
            double mean = 0;
            foreach (var m in gated) mean += m;
            mean /= gated.Count;
            double rel = L(mean) - 10;
            var gated2 = gated.FindAll(m => L(m) > rel);
            double mean2 = 0;
            foreach (var m in gated2) mean2 += m;
            mean2 /= Math.Max(1, gated2.Count);
            return (float)L(mean2);
        }

        private static double[] HighShelf(int fs, double f0, double gainDb, double q)
        {
            double A = Math.Pow(10, gainDb / 40), w = 2 * Math.PI * f0 / fs, cw = Math.Cos(w), sw = Math.Sin(w), alpha = sw / (2 * q);
            double b0 = A * ((A + 1) + (A - 1) * cw + 2 * Math.Sqrt(A) * alpha);
            double b1 = -2 * A * ((A - 1) + (A + 1) * cw);
            double b2 = A * ((A + 1) + (A - 1) * cw - 2 * Math.Sqrt(A) * alpha);
            double a0 = (A + 1) - (A - 1) * cw + 2 * Math.Sqrt(A) * alpha;
            double a1 = 2 * ((A - 1) - (A + 1) * cw);
            double a2 = (A + 1) - (A - 1) * cw - 2 * Math.Sqrt(A) * alpha;
            return new[] { b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0 };
        }

        private static double[] HighPass(int fs, double f0, double q)
        {
            double w = 2 * Math.PI * f0 / fs, cw = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
            double b0 = (1 + cw) / 2, b1 = -(1 + cw), b2 = (1 + cw) / 2, a0 = 1 + alpha, a1 = -2 * cw, a2 = 1 - alpha;
            return new[] { b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0 };
        }

        // ------------------------------------------------------------------ decoding

        /// <summary>WAV/AIFF are parsed directly; compressed formats are decoded by Unity (temporarily DecompressOnLoad).</summary>
        private static bool Decode(string path, out float[] samples, out int ch, out int rate)
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && ReadWav(path, out samples, out ch, out rate)) return true;
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            samples = null; ch = 0; rate = 0;
            if (imp == null) return false;
            var orig = imp.defaultSampleSettings;
            bool changed = orig.loadType != AudioClipLoadType.DecompressOnLoad;
            if (changed)
            {
                var s = orig;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
            try
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) return false;
                clip.LoadAudioData();
                ch = clip.channels;
                rate = clip.frequency;
                samples = new float[clip.samples * ch];
                return clip.GetData(samples, 0);
            }
            finally
            {
                if (changed)
                {
                    imp.defaultSampleSettings = orig;
                    imp.SaveAndReimport();
                }
            }
        }

        public static bool ReadWav(string path, out float[] samples, out int channels, out int rate)
        {
            samples = null; channels = 0; rate = 0;
            var b = File.ReadAllBytes(path);
            if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF") return false;
            int p = 12, fmt = 0, bits = 0, data = -1, size = 0;
            while (p + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, p, 4);
                int csz = BitConverter.ToInt32(b, p + 4);
                if (id == "fmt ")
                {
                    fmt = BitConverter.ToInt16(b, p + 8);
                    channels = BitConverter.ToInt16(b, p + 10);
                    rate = BitConverter.ToInt32(b, p + 12);
                    bits = BitConverter.ToInt16(b, p + 22);
                    if (fmt == -2 && csz >= 26) fmt = BitConverter.ToInt16(b, p + 32); // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data") { data = p + 8; size = Math.Min(csz, b.Length - data); break; }
                p += 8 + csz + (csz & 1);
            }
            if (data < 0 || channels <= 0) return false;
            int bps = bits / 8;
            int n = size / bps;
            samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = data + i * bps;
                switch (bits)
                {
                    case 16: samples[i] = BitConverter.ToInt16(b, o) / 32768f; break;
                    case 24: samples[i] = ((b[o] << 8 | b[o + 1] << 16 | b[o + 2] << 24) >> 8) / 8388608f; break;
                    case 32: samples[i] = fmt == 3 ? BitConverter.ToSingle(b, o) : BitConverter.ToInt32(b, o) / 2147483648f; break;
                    case 8: samples[i] = (b[o] - 128) / 128f; break;
                    default: return false;
                }
            }
            return true;
        }

        public static void WriteWav16(string path, float[] s, int ch, int rate)
        {
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = s.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate);
                w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
                foreach (var v in s) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }

        // ------------------------------------------------------------------ utils

        private static float Db(float a) => (float)(20 * Math.Log10(Math.Max(1e-9, a)));
        private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        private static string FileHash(string path)
        {
            using (var md5 = MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").Substring(0, 16);
        }

        private static string Serialize(object o)
        {
            switch (o)
            {
                case null: return "null";
                case string s: return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                case bool bo: return bo ? "true" : "false";
                case double d: return d.ToString(CultureInfo.InvariantCulture);
                case Dictionary<string, object> dict:
                {
                    var sb = new StringBuilder("{");
                    bool first = true;
                    foreach (var kv in dict) { if (!first) sb.Append(','); first = false; sb.Append(Serialize(kv.Key)).Append(':').Append(Serialize(kv.Value)); }
                    return sb.Append('}').ToString();
                }
                case List<object> list:
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Serialize(list[i])); }
                    return sb.Append(']').ToString();
                }
                default: return "\"" + o + "\"";
            }
        }
    }
}
