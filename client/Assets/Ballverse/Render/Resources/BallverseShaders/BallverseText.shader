// Ball labels: glyph quads in world space sampling a dynamic font atlas (alpha), coloured per vertex (sRGB).
Shader "Hidden/Ballverse/Text"
{
    Properties
    {
        _MainTex ("Font atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BallverseText"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BallverseCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = BvWorldToClip(v.positionOS);
                o.color = half4(BvSrgbToLinear(v.color.rgb), v.color.a);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a;
                return half4(i.color.rgb * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
