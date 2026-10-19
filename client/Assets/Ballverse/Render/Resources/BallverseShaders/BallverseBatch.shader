// World-space triangle batches (shapes, rings, platforms, trails, overlays) from Ballverse.Core.RenderGeometry.GeometryBatch.
// Vertex layout: position float3 (metres), colour UNorm8x4 (sRGB), texcoord0.x = emission multiplier.
Shader "Hidden/Ballverse/Batch"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BallverseBatch"
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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = BvWorldToClip(v.positionOS);
                float3 rgb = BvSrgbToLinear(v.color.rgb) * v.uv.x;
                o.color = half4(rgb * v.color.a, v.color.a);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
