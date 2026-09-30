// Full-screen "imprint" effect: the world starts to look like a letterpress proof.
// Pixelation (block size grows with the amount, optionally stronger toward the frame edges),
// tone posterisation, ink grain and a faint block grid. Driven by global floats set from C#:
//   _PalinodeImprint (0..1), _PalinodeImprintEdge (0..1), _PalinodeGlitch (0..1).
Shader "Palinode/Imprint"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "Imprint"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PalinodeImprint;
            float _PalinodeImprintEdge;
            float _PalinodeGlitch;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half3 SampleSrc(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 res = _ScreenParams.xy;

                // --- Glitch: horizontal band tearing + RGB split ---
                float g = _PalinodeGlitch;
                float2 guv = uv;
                float split = 0;
                if (g > 0.001)
                {
                    float t = floor(_Time.y * 24.0);
                    float band = floor(uv.y * 28.0);
                    float r = Hash21(float2(band, t));
                    guv.x += (r > 0.55 ? (r - 0.55) * 0.35 : 0.0) * g * (Hash21(float2(t, band * 3.1)) > 0.5 ? 1 : -1);
                    split = 0.006 * g;
                }

                float edgeDist = length((uv - 0.5) * float2(1.0, 0.8)) * 1.55;
                float local = saturate(_PalinodeImprint + _PalinodeImprintEdge * pow(saturate(edgeDist), 2.2));

                if (local < 0.002)
                {
                    if (g <= 0.001) return half4(SampleSrc(uv), 1);
                    half3 c0;
                    c0.r = SampleSrc(guv + float2(split, 0)).r;
                    c0.g = SampleSrc(guv).g;
                    c0.b = SampleSrc(guv - float2(split, 0)).b;
                    return half4(c0, 1);
                }

                // --- Pixelation ---
                float px = floor(lerp(1.0, 12.0, pow(local, 1.7)) + 0.5);
                float2 cell = floor(guv * res / px);
                float2 uvq = (cell + 0.5) * px / res;

                half3 c;
                c.r = SampleSrc(uvq + float2(split, 0)).r;
                c.g = SampleSrc(uvq).g;
                c.b = SampleSrc(uvq - float2(split, 0)).b;

                // --- Ink: posterise tones, grain, slight warm paper cast ---
                // Posterise in a perceptual (gamma) space so dark scenes keep their gradations.
                float levels = lerp(64.0, 12.0, local);
                c = pow(floor(pow(max(c, 0), 1.0 / 2.2) * levels + 0.5) / levels, 2.2);

                // One grain value per printed block: reads as uneven ink, not as noise.
                float n = Hash21(cell + 17.0);
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                float inkMask = saturate(1.0 - lum * 1.6);
                c *= lerp(1.0, 0.86 + 0.2 * n, local * (0.3 + 0.7 * inkMask));
                c = lerp(c, c * float3(1.04, 0.98, 0.88), local * 0.6);

                // Faint printed block grid
                float2 fb = frac(guv * res / px);
                float gridEdge = smoothstep(0.82, 1.0, max(fb.x, fb.y)) * step(3.0, px);
                c *= 1.0 - gridEdge * 0.22 * local;

                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
