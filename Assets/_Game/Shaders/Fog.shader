// Drifting ground fog: scrolling fbm noise, fading toward the top of the quad.
Shader "Palinode/Fog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Scale ("Noise scale", Float) = 0.8
        _Speed ("Drift speed", Vector) = (0.08, 0.01, 0, 0)
        _Falloff ("Vertical falloff", Float) = 1.6
        _Density ("Density", Float) = 1.0
        _EdgeSoft ("Horizontal edge softness", Float) = 0.15
        _QuadSize ("Quad size (units)", Vector) = (4, 3, 0, 0)
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float _Scale; float4 _Speed; float _Falloff; float _Density; float _EdgeSoft; float4 _QuadSize; float _Seed;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * unity_SpriteColor;
                o.uv = v.uv;
                return o;
            }

            float Hash21(float2 p) { p = frac(p * float2(127.1, 311.7)); p += dot(p, p + 19.19); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * _QuadSize.xy * _Scale + _Seed;
                float t = _Time.y;
                float n = Fbm(p + _Speed.xy * t * 4.0);
                n = n * 0.65 + Fbm(p * 1.7 - _Speed.xy * t * 6.0 + 3.3) * 0.35;
                float v = pow(saturate(1.0 - i.uv.y), _Falloff);
                float edge = smoothstep(0.0, _EdgeSoft, i.uv.x) * smoothstep(0.0, _EdgeSoft, 1.0 - i.uv.x);
                float a = saturate((n - 0.28) * 1.8) * v * edge * _Density;
                return half4(i.color.rgb, a * i.color.a);
            }
            ENDHLSL
        }
    }
}
