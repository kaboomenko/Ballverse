// Bloom chain and final composite of the simulation renderer (spec §7.7 "Пост-обработка — bloom, цветокоррекция по
// теме, виньетка"). Owned by the renderer instead of a URP Volume so that live preview and offline export (which renders
// many frames per Unity frame through command buffers) apply exactly the same post-processing.
//
// Pass 0 Prefilter : 4-tap box downsample of _BvSource + soft threshold (_BvBloomParams.x threshold, .y knee)
// Pass 1 Downsample: 4-tap box downsample of _BvSource
// Pass 2 Upsample  : 9-tap tent of _BvSource added onto the target (Blend One One)
// Pass 3 Composite : _BvScene + _BvBloom * _BvBloomParams.z, theme colour grade (_BvGrade: x saturation, y contrast
//                    around linear mid-grey 0.18; _BvGradeTint.rgb multiplicative tint), vignette _BvBloomParams.w -> target
Shader "Hidden/Ballverse/Post"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "BallverseCommon.hlsl"

    TEXTURE2D(_BvSource);
    SAMPLER(sampler_BvSource);
    float4 _BvSource_TexelSize;
    TEXTURE2D(_BvScene);
    SAMPLER(sampler_BvScene);
    TEXTURE2D(_BvBloom);
    SAMPLER(sampler_BvBloom);
    float4 _BvBloomParams;
    float4 _BvGrade;
    float4 _BvGradeTint;

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Varyings FullScreenVert(uint vertexID : SV_VertexID)
    {
        Varyings o;
        o.positionCS = BvFullScreenPosition(vertexID);
        o.uv = BvFullScreenUV(vertexID);
        return o;
    }

    float3 SampleSource(float2 uv)
    {
        return SAMPLE_TEXTURE2D(_BvSource, sampler_BvSource, uv).rgb;
    }

    float3 Box4(float2 uv)
    {
        float4 o = _BvSource_TexelSize.xyxy * float4(-1.0, -1.0, 1.0, 1.0);
        return (SampleSource(uv + o.xy) + SampleSource(uv + o.zy) + SampleSource(uv + o.xw) + SampleSource(uv + o.zw)) * 0.25;
    }

    float3 Tent9(float2 uv)
    {
        float4 o = _BvSource_TexelSize.xyxy * float4(1.0, 1.0, -1.0, 0.0);
        float3 s = SampleSource(uv - o.xy);
        s += SampleSource(uv - o.wy) * 2.0;
        s += SampleSource(uv - o.zy);
        s += SampleSource(uv + o.zw) * 2.0;
        s += SampleSource(uv) * 4.0;
        s += SampleSource(uv + o.xw) * 2.0;
        s += SampleSource(uv + o.zy);
        s += SampleSource(uv + o.wy) * 2.0;
        s += SampleSource(uv + o.xy);
        return s * (1.0 / 16.0);
    }

    float3 Threshold(float3 c)
    {
        float threshold = _BvBloomParams.x;
        float knee = max(_BvBloomParams.y, 1e-4);
        float br = max(c.r, max(c.g, c.b));
        float soft = clamp(br - threshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee);
        float contribution = max(soft, br - threshold) / max(br, 1e-4);
        return c * contribution;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "BallverseBloomPrefilter"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullScreenVert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                return half4(Threshold(min(Box4(i.uv), 64.0)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BallverseBloomDownsample"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullScreenVert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                return half4(Box4(i.uv), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BallverseBloomUpsample"
            Blend One One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullScreenVert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                return half4(Tent9(i.uv), 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BallverseComposite"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullScreenVert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float3 col = SAMPLE_TEXTURE2D(_BvScene, sampler_BvScene, i.uv).rgb;
                col += SAMPLE_TEXTURE2D(_BvBloom, sampler_BvBloom, i.uv).rgb * _BvBloomParams.z;
                // Theme colour grade (spec §7.7 «цветокоррекция по теме»); identity for (1, 1) and a white tint.
                float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
                col = max(lerp(luma.xxx, col, _BvGrade.x), 0.0);
                col = max((col - 0.18) * _BvGrade.y + 0.18, 0.0);
                col *= _BvGradeTint.rgb;
                float2 d = (i.uv - 0.5) * 1.41421356;
                col *= 1.0 - _BvBloomParams.w * smoothstep(0.35, 1.0, dot(d, d));
                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
