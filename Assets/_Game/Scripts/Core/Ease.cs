using UnityEngine;

namespace Palinode.Core
{
    public static class Ease
    {
        public static float Apply(string name, float t)
        {
            t = Mathf.Clamp01(t);
            switch (name)
            {
                case "linear": return t;
                case "in": case "inQuad": return t * t;
                case "out": case "outQuad": return 1f - (1f - t) * (1f - t);
                case "inOut": case "inOutQuad": return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                case "inCubic": return t * t * t;
                case "outCubic": return 1f - Mathf.Pow(1f - t, 3f);
                case "inOutCubic": return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case "outBack":
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                }
                case "outElastic":
                {
                    if (t <= 0f || t >= 1f) return t;
                    const float c4 = 2f * Mathf.PI / 3f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
                }
                case "inSine": return 1f - Mathf.Cos(t * Mathf.PI / 2f);
                case "outSine": return Mathf.Sin(t * Mathf.PI / 2f);
                default: // "inOutSine" — the default for slow cinematic camera moves
                    return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
            }
        }
    }
}
