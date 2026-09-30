// Handwriting on paper, TMP-compatible SDF shader.
//  _InkMode 0 — fountain-pen ink: stroke weight varies (low-frequency noise ≈ pen pressure), slight bleed halo
//               into the paper fibres, darker ink rim at the stroke edge, uneven density.
//  _InkMode 1 — graphite / coloured pencil: thinner, rough-edged stroke, grainy density that follows paper tooth.
// Noise is evaluated in object space, so it sticks to the letters while the camera moves.
Shader "Palinode/InkSDF"
{
    Properties
    {
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _FaceDilate ("Face Dilate", Range(-1,1)) = 0
        _MainTex ("Font Atlas", 2D) = "white" {}
        _TextureWidth ("Texture Width", float) = 512
        _TextureHeight ("Texture Height", float) = 512
        _GradientScale ("Gradient Scale", float) = 5
        _ScaleX ("Scale X", float) = 1
        _ScaleY ("Scale Y", float) = 1
        _PerspectiveFilter ("Perspective Correction", Range(0, 1)) = 0.875
        _Sharpness ("Sharpness", Range(-1,1)) = 0
        _WeightNormal ("Weight Normal", float) = 0
        _WeightBold ("Weight Bold", float) = .5
        _ScaleRatioA ("Scale RatioA", float) = 1
        _ScaleRatioB ("Scale RatioB", float) = 1
        _ScaleRatioC ("Scale RatioC", float) = 1
        _VertexOffsetX ("Vertex OffsetX", float) = 0
        _VertexOffsetY ("Vertex OffsetY", float) = 0
        _ClipRect ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
        // Properties TMP reads/writes on any SDF material (unused by this shader).
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness ("Outline Softness", Range(0,1)) = 0
        _UnderlayColor ("Border Color", Color) = (0,0,0,.5)
        _UnderlayOffsetX ("Border OffsetX", Range(-1,1)) = 0
        _UnderlayOffsetY ("Border OffsetY", Range(-1,1)) = 0
        _UnderlayDilate ("Border Dilate", Range(-1,1)) = 0
        _UnderlaySoftness ("Border Softness", Range(0,1)) = 0
        _GlowColor ("Glow Color", Color) = (0,1,0,0.5)
        _GlowOffset ("Glow Offset", Range(-1,1)) = 0
        _GlowInner ("Glow Inner", Range(0,1)) = 0.05
        _GlowOuter ("Glow Outer", Range(0,1)) = 0.05
        _GlowPower ("Glow Power", Range(1, 0)) = 0.75
        _ShaderFlags ("Flags", float) = 0
        _MaskSoftnessX ("Mask SoftnessX", float) = 0
        _MaskSoftnessY ("Mask SoftnessY", float) = 0
        _Stencil ("Stencil ID", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _CullMode ("Cull Mode", Float) = 0
        _ColorMask ("Color Mask", Float) = 15

        _InkMode ("Ink mode (0 pen, 1 pencil)", Float) = 0
        _InkWeight ("Stroke weight (SDF units)", Float) = 0.06
        _PressureScale ("Pressure noise scale", Float) = 3.0
        _PressureAmount ("Pressure (weight variation)", Float) = 0.12
        _EdgeRough ("Edge roughness", Float) = 0.04
        _Bleed ("Bleed width (SDF units)", Float) = 0.07
        _BleedAlpha ("Bleed opacity", Float) = 0.22
        _Rim ("Ink rim darkening", Float) = 0.3
        _Grain ("Graphite grain", Float) = 0.0
        _GrainScale ("Grain scale", Float) = 90
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off ZWrite Off Lighting Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FaceColor;
            float _FaceDilate, _GradientScale, _ScaleX, _ScaleY, _Sharpness, _WeightNormal, _WeightBold, _ScaleRatioA;
            float _VertexOffsetX, _VertexOffsetY;
            float _InkWeight, _InkMode, _PressureScale, _PressureAmount, _EdgeRough, _Bleed, _BleedAlpha, _Rim, _Grain, _GrainScale;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float4 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 obj : TEXCOORD1;
                float scale : TEXCOORD2;
                float weight : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 p = v.vertex;
                p.x += _VertexOffsetX; p.y += _VertexOffsetY;
                o.pos = UnityObjectToClipPos(p);
                float2 pixelSize = o.pos.w;
                pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(v.uv.w) * _GradientScale * (_Sharpness + 1);
                float bold = step(v.uv.w, 0);
                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
                o.weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;
                o.scale = scale;
                o.uv = v.uv.xy;
                o.obj = p.xy;
                o.color = v.color;
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;                         // SDF: 0.5 = glyph edge
                float pressure = vnoise(i.obj * _PressureScale) * 2 - 1;   // slow variation along the line
                float rough = vnoise(i.obj * _PressureScale * 14) * 2 - 1; // fibre-scale edge noise
                float thin = _InkMode > 0.5 ? 0.035 : 0.0;
                float threshold = 0.5 - i.weight - _InkWeight - pressure * _PressureAmount * 0.5 - rough * _EdgeRough + thin;
                float face = saturate((a - threshold) * i.scale + 0.5);

                // Bleed: a faint halo spreading into the paper just outside the stroke.
                float bleed = saturate((a - (threshold - _Bleed)) / max(0.001, _Bleed)) * (1 - face);
                bleed *= _BleedAlpha * (0.6 + 0.4 * vnoise(i.obj * _PressureScale * 5));

                // Ink rim: ink pools at the stroke boundary → slightly darker edge, lighter centre.
                float inside = saturate((a - threshold) * 6);
                float rim = lerp(1 - _Rim * 0.5, 1 + _Rim * 0.3, inside);

                // Graphite grain: paper tooth catches the pencil unevenly.
                float grain = 1;
                if (_Grain > 0)
                {
                    float g = hash21(floor(i.obj * _GrainScale)) * 0.6 + vnoise(i.obj * _GrainScale * 0.35) * 0.4;
                    grain = lerp(1, smoothstep(0.15, 0.85, g), _Grain);
                }

                float density = 0.88 + 0.12 * vnoise(i.obj * _PressureScale * 2.3 + 17);
                float alpha = saturate(face * density * grain + bleed) * i.color.a * _FaceColor.a;
                float3 col = i.color.rgb * _FaceColor.rgb * rim;
                return fixed4(col * alpha, alpha);
            }
            ENDCG
        }
    }
}
