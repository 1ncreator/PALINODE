using System;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Procedural stand-ins for sounds that are not delivered yet (adapted from RogueDungeon's SfxSynth, but dark and
    /// low-passed to sit with the painted, noir sound of the game). Mono, 44.1 kHz.
    /// </summary>
    public static class SfxSynth
    {
        public const int Rate = 44100;

        public static AudioClip Create(string kind)
        {
            float[] s;
            switch (kind)
            {
                case "rumble": s = Rumble(1.4f); break;
                case "whir": s = Whir(2.0f); break;
                case "whoosh": s = Concat(Whoosh(0.9f), Thud(0.5f)); break;
                case "jam": s = Jam(2.2f); break;
                case "clank": s = Clank(0.6f); break;
                case "rev": s = Rev(0.9f); break;
                case "thud": s = Thud(0.6f); break;
                default: s = new float[Rate / 10]; break;
            }
            var clip = AudioClip.Create("synth_" + kind, s.Length, 1, Rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        private static uint _noise = 0x9E3779B9u;

        private static float Noise()
        {
            _noise ^= _noise << 13;
            _noise ^= _noise >> 17;
            _noise ^= _noise << 5;
            return (_noise & 0xFFFF) / 32767.5f - 1f;
        }

        /// <summary>Heavy roller: low-passed noise with a slow periodic knock (the roller's seams).</summary>
        private static float[] Rumble(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.02f;
                lp2 += (lp - lp2) * 0.05f;
                float knock = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 5f)), 12f) * Mathf.Sin(t * 2f * Mathf.PI * 70f);
                float env = Mathf.Min(1f, t / 0.12f) * Mathf.Min(1f, (dur - t) / 0.3f);
                o[i] = Mathf.Clamp((lp2 * 6f + knock * 0.35f + Mathf.Sin(t * 2f * Mathf.PI * 42f) * 0.25f) * env * 0.8f, -1f, 1f);
            }
            return o;
        }

        /// <summary>Flywheel: a seamless whirring loop (period-aligned tones + faint ticking).</summary>
        private static float[] Whir(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.08f;
                float tone = Mathf.Sin(t * 2f * Mathf.PI * 55f) * 0.3f + Mathf.Sin(t * 2f * Mathf.PI * 110f) * 0.12f;
                float tick = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 6f)), 30f) * lp * 3f;
                float wob = 0.75f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI * 1.5f);
                o[i] = Mathf.Clamp((tone * wob + lp * 0.35f + tick) * 0.6f, -1f, 1f);
            }
            return o;
        }

        private static float[] Whoosh(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float cut = Mathf.Lerp(0.01f, 0.25f, t * t);
                lp += (Noise() - lp) * cut;
                o[i] = lp * Mathf.Sin(t * Mathf.PI) * 1.4f;
            }
            return o;
        }

        private static float[] Thud(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.06f;
                float f = Mathf.Lerp(90f, 38f, t / dur);
                float env = Mathf.Exp(-t * 7f);
                o[i] = Mathf.Clamp((Mathf.Sin(t * 2f * Mathf.PI * f) * 0.9f + lp * 0.8f) * env, -1f, 1f);
            }
            return o;
        }

        /// <summary>Metallic hit: inharmonic partials with fast decay.</summary>
        private static float[] Clank(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            float[] fr = { 180f, 427f, 689f, 1130f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int k = 0; k < fr.Length; k++) v += Mathf.Sin(t * 2f * Mathf.PI * fr[k]) * Mathf.Exp(-t * (6f + k * 5f)) / (k + 1);
                o[i] = Mathf.Clamp(v * 0.8f, -1f, 1f);
            }
            return o;
        }

        /// <summary>The machine chokes: grinding bursts, clanks, a dying drone.</summary>
        private static float[] Jam(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            var clank = Clank(0.6f);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += (Noise() - lp) * 0.12f;
                float grind = lp * (0.5f + 0.5f * Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(18f, 3f, t / dur))));
                float drone = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(70f, 25f, t / dur)) * 0.4f;
                float env = Mathf.Clamp01((dur - t) / 0.8f);
                o[i] = (grind * 0.7f + drone) * env;
            }
            foreach (float at in new[] { 0.05f, 0.5f, 1.1f })
            {
                int off = (int)(at * Rate);
                for (int i = 0; i < clank.Length && off + i < n; i++) o[off + i] += clank[i] * 0.8f;
            }
            for (int i = 0; i < n; i++) o[i] = Mathf.Clamp(o[i], -1f, 1f);
            return o;
        }

        /// <summary>Engine revving up (sawtooth through a low-pass, rising).</summary>
        private static float[] Rev(float dur)
        {
            int n = (int)(dur * Rate);
            var o = new float[n];
            double phase = 0;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(38f, 120f, t * t);
                phase += f / Rate;
                float saw = (float)(phase % 1.0) * 2f - 1f;
                lp += (saw + Noise() * 0.3f - lp) * 0.08f;
                float env = Mathf.Min(1f, t / 0.1f) * Mathf.Min(1f, (1f - t) / 0.1f);
                o[i] = lp * env * 0.9f;
            }
            return o;
        }

        private static float[] Concat(float[] a, float[] b)
        {
            var o = new float[a.Length + b.Length];
            Array.Copy(a, o, a.Length);
            Array.Copy(b, 0, o, a.Length, b.Length);
            return o;
        }
    }
}
