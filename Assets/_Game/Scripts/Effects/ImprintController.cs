using System.Collections;
using UnityEngine;

namespace Palinode.Effects
{
    /// <summary>
    /// Drives the full-screen "imprint" post effect (pixelation + ink grain) through global shader properties,
    /// so the renderer feature material never needs to be touched at runtime.
    /// </summary>
    public static class ImprintController
    {
        private static readonly int AmountId = Shader.PropertyToID("_PalinodeImprint");
        private static readonly int EdgeId = Shader.PropertyToID("_PalinodeImprintEdge");
        private static readonly int GlitchId = Shader.PropertyToID("_PalinodeGlitch");

        public static float Amount { get; private set; }
        public static float Edge { get; private set; }
        public static float Glitch { get; private set; }

        public static void Set(float amount, float edge)
        {
            Amount = Mathf.Clamp01(amount);
            Edge = Mathf.Clamp01(edge);
            Shader.SetGlobalFloat(AmountId, Amount);
            Shader.SetGlobalFloat(EdgeId, Edge);
        }

        public static void SetGlitch(float g)
        {
            Glitch = Mathf.Clamp01(g);
            Shader.SetGlobalFloat(GlitchId, Glitch);
        }

        public static IEnumerator Animate(float amount, float edge, float duration)
        {
            float a0 = Amount, e0 = Edge, t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                Set(Mathf.Lerp(a0, amount, k), Mathf.Lerp(e0, edge, k));
                yield return null;
            }
            Set(amount, edge);
        }

        public static IEnumerator GlitchBurst(float duration, float strength)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetGlitch(Random.value < 0.7f ? strength * Random.Range(0.4f, 1f) : 0f);
                yield return null;
            }
            SetGlitch(0f);
        }

        public static void Reset()
        {
            Set(0f, 0f);
            SetGlitch(0f);
        }
    }
}
