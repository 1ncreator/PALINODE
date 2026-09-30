// Solid silhouette of a sprite: RGB = SpriteRenderer.color, alpha = texture alpha × color alpha. Unlit.
// Used for hit flashes, outlines (elite enemies) and ink copies.
Shader "Palinode/SpriteSolid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
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

            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

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
                // Soft cast shadows (low alpha) are not part of the silhouette.
                half a = smoothstep(0.55, 0.8, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a);
                return half4(i.color.rgb, a * i.color.a);
            }
            ENDHLSL
        }
    }
}
