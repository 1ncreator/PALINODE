// Procedural rain on a window pane: static droplets with bright rims and drops sliding down.
// Rendered as a transparent overlay quad (SpriteRenderer, 0..1 UVs).
Shader "Palinode/RainGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Density ("Drops per unit", Float) = 7
        _Speed ("Slide speed", Float) = 0.35
        _Highlight ("Highlight color", Color) = (0.85, 0.9, 1, 0.55)
        _Shadow ("Shadow color", Color) = (0.02, 0.03, 0.05, 0.35)
        _Haze ("Haze", Float) = 0.06
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
                float _Density; float _Speed; float4 _Highlight; float4 _Shadow; float _Haze; float4 _QuadSize; float _Seed;
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

            float Hash21(float2 p) { p = frac(p * float2(233.34, 851.73)); p += dot(p, p + 23.45); return frac(p.x * p.y); }
            float2 Hash22(float2 p) { float n = Hash21(p); return float2(n, Hash21(p + n)); }

            // Returns x: rim highlight, y: droplet body
            float2 StaticDrops(float2 uv, float scale, float seed)
            {
                float2 p = uv * scale;
                float2 id = floor(p);
                float2 gv = frac(p) - 0.5;
                float n = Hash21(id + seed);
                if (n < 0.45) return 0;
                float2 o = (Hash22(id + seed * 1.7) - 0.5) * 0.6;
                float r = lerp(0.07, 0.2, Hash21(id + 4.1 + seed));
                float2 d = (gv - o) * float2(1.0, 0.85);
                float dist = length(d);
                float body = smoothstep(r, r * 0.55, dist);
                float rim = smoothstep(r * 0.55, r * 0.25, length(d - float2(-r * 0.3, r * 0.35))) * body;
                return float2(rim, body);
            }

            float2 SlidingDrops(float2 uv, float scale, float t, float seed)
            {
                float2 p = uv * float2(scale, 1.0);
                float col = floor(p.x);
                float colN = Hash21(float2(col, seed));
                if (colN < 0.55) return 0;
                float x = frac(p.x) - 0.5 + (colN - 0.75) * 0.4;
                float y = frac(uv.y * 0.5 + t * (0.4 + colN) + colN * 7.0);
                float head = 0.12;
                float2 d = float2(x * 1.0, (y - head) * scale * 0.5);
                float dist = length(d);
                float r = 0.14;
                float body = smoothstep(r, r * 0.5, dist);
                float trail = smoothstep(0.05, 0.0, abs(x)) * smoothstep(head, 1.0, y) * (1.0 - y) * 0.7;
                float rim = smoothstep(r * 0.5, r * 0.15, length(d - float2(-0.04, 0.04))) * body;
                return float2(rim, max(body, trail * 0.6));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _QuadSize.xy;
                float t = _Time.y * _Speed;
                float2 a = StaticDrops(uv, _Density, _Seed);
                float2 b = StaticDrops(uv + 0.37, _Density * 1.9, _Seed + 3.0) * 0.8;
                float2 s = SlidingDrops(uv, _Density * 0.7, t, _Seed + 11.0);
                float rim = saturate(a.x + b.x + s.x);
                float body = saturate(a.y + b.y + s.y);
                float3 col = lerp(_Shadow.rgb, _Highlight.rgb, rim);
                float alpha = saturate(body * _Shadow.a + rim * _Highlight.a) + _Haze;
                return half4(col, alpha * i.color.a) * half4(i.color.rgb, 1);
            }
            ENDHLSL
        }
    }
}
