// Ghostly vehicle: the sprite keeps its own drawing, but washed into a cold pale tint and made translucent;
// the tail end breaks up into drifting noise (animated dissolve along _DissolveDir), with a faint scanline shimmer.
// Additive blend, so even a dark object reads as a faint luminous apparition on a dark street.
Shader "Palinode/SpriteGhost"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Base ("Base brightness", Float) = 0.25
        _Detail ("Detail from luminance", Float) = 2.2
        _Desat ("Keep original colour (0 = tint only)", Range(0, 1)) = 0.25
        _Dissolve ("Tail dissolve amount", Range(0, 1)) = 0.45
        _DissolveDir ("Dissolve direction in UV (towards the head)", Vector) = (1, -0.35, 0, 0)
        _DissolveSoft ("Dissolve edge softness", Range(0.01, 0.5)) = 0.18
        _NoiseScale ("Noise scale", Float) = 38
        _Drift ("Noise drift speed (UV/s, against the motion)", Vector) = (-0.12, 0.03, 0, 0)
        _Scan ("Scanline shimmer", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Base; float _Detail; float _Desat;
                float _Dissolve; float4 _DissolveDir; float _DissolveSoft;
                float _NoiseScale; float4 _Drift; float _Scan;
            CBUFFER_END

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.color = v.color * unity_SpriteColor;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half lum = dot(t.rgb, half3(0.299, 0.587, 0.114));
                half3 tint = i.color.rgb * saturate(_Base + lum * _Detail);
                half3 c = lerp(tint, t.rgb * i.color.rgb * (1 + _Base * 2), _Desat);

                // Tail dissolve: position along the body (0 = tail, 1 = head) against drifting fractal noise.
                float2 dir = normalize(_DissolveDir.xy);
                float along = saturate(dot(i.uv - 0.5, dir) + 0.5);
                float2 np = i.uv * _NoiseScale + _Drift.xy * _Time.y * _NoiseScale;
                float n = Noise(np) * 0.6 + Noise(np * 2.3 + 7.1) * 0.3 + Noise(np * 5.1 - 3.7) * 0.1;
                float edge = along - (_Dissolve * 1.2 - 0.2);           // < 0 → dissolved
                float keep = smoothstep(0, _DissolveSoft, edge + (n - 0.5) * 0.45);
                // Scanline shimmer (moves slowly up the body).
                float scan = 1 - _Scan * (0.5 + 0.5 * sin(i.uv.y * 420 + _Time.y * 9));

                return half4(c * scan, t.a * i.color.a * keep);
            }
            ENDHLSL
        }
    }
}
