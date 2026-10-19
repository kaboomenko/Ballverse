// Studio compositor (spec §7.5 step c, §3.3, §3.5, §3.10): draws the slots, divider, PiP frame and shadow, text plates, glyphs
// and the watermark into the output frame, and converts the frame for the encoder readback. One shader for preview and export.
// Drawn through CommandBuffers outside the camera loop (no pipeline renderer feature needed); written against URP's Core.hlsl
// like the simulation renderer's shaders. Canvas coordinates are pixels with a top-left origin; _StViewProj is
// GL.GetGPUProjectionMatrix(Ortho(0, W, H, 0), true), set by StudioCompositor for every frame.
//
// Pass 0 Image   : textured slot. Quad over the slot rect (_StRect; _StShape.xy = its size). Inside the destination rect (_StDest, slot-normalized
//                  x0 y0 x1 y1) samples _MainTex through the 2x3 UV map (_StUv0 = M00 M01 Tx flipV, _StUv1 = M10 M11 Ty -);
//                  outside it (Fit letterbox) the background: _StBgColor, or _StBgTex through _StBgUv0/_StBgUv1 when
//                  _StParams.w > 0.5 (blurred copy of the same picture). Rounded-rect / circle mask (_StShape: w h radius
//                  border px), frame colour _StBorderColor. _StTint multiplies (alpha = slot opacity). The picture is
//                  opaque unless _StParams.z > 0.5 (keeps the texture alpha: watermark artwork).
// Pass 1 Shape   : solid rounded rect (_StShape, softness _StParams.x px for shadows and glow) in _StColor; glass look when
//                  _StParams.y > 0.5 (vertical sheen and a light rim).
// Pass 2 Glyph   : font-atlas glyph quads (mesh positions in canvas px, offset by _StOffset.xy); alpha from _MainTex.a.
// Pass 3 Readback: full-screen copy of _MainTex flipped vertically (rows top-down, as the encoder wants them) and encoded
//                  to sRGB when _StParams.x > 0.5 (linear colour space: the target is a UNorm texture).
Shader "Hidden/Ballverse/StudioComposite"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _StBgTex ("Background", 2D) = "black" {}
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    float4x4 _StViewProj;
    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    TEXTURE2D(_StBgTex);
    SAMPLER(sampler_StBgTex);
    float4 _StRect;
    float4 _StDest;
    float4 _StUv0;
    float4 _StUv1;
    float4 _StBgUv0;
    float4 _StBgUv1;
    float4 _StShape;
    float4 _StParams;
    float4 _StOffset;
    float4 _StColor;
    float4 _StBgColor;
    float4 _StBorderColor;
    float4 _StTint;

    struct QuadAttributes
    {
        float3 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct QuadVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 local : TEXCOORD0;
    };

    // Unit quad (positions 0..1, y down) placed on _StRect in canvas pixels.
    QuadVaryings QuadVert(QuadAttributes v)
    {
        QuadVaryings o;
        float2 p = _StRect.xy + v.positionOS.xy * _StRect.zw;
        o.positionCS = mul(_StViewProj, float4(p, 0.0, 1.0));
        o.local = v.positionOS.xy;
        return o;
    }

    // Signed distance to a rounded box centred at 0 with half size b and corner radius r (negative inside).
    float SdRoundedBox(float2 p, float2 b, float r)
    {
        r = min(r, min(b.x, b.y));
        float2 q = abs(p) - b + r;
        return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
    }

    // Distance (px) to the rounded box of size _StShape.xy centred in the quad (the quad may be larger: shadows, glow).
    float ShapeDistance(float2 local)
    {
        float2 quad = _StRect.zw;
        return SdRoundedBox(local * quad - quad * 0.5, _StShape.xy * 0.5, _StShape.z);
    }

    float2 MapUv(float2 p, float4 row0, float4 row1)
    {
        float2 uv = float2(dot(row0.xy, p) + row0.z, dot(row1.xy, p) + row1.z);
        return uv;
    }

    float3 LinearToSrgb(float3 c)
    {
        c = max(c, 0.0);
        float3 lo = c * 12.92;
        float3 hi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
        return lerp(hi, lo, step(c, 0.0031308));
    }

    struct FullVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    // Full-screen triangle; uv in image convention (0 = bottom) on every platform (same as the simulation renderer).
    FullVaryings FullVert(uint vertexID : SV_VertexID)
    {
        FullVaryings o;
        float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
        o.positionCS = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
    #if UNITY_UV_STARTS_AT_TOP
        o.uv = float2(uv.x, 1.0 - uv.y);
    #else
        o.uv = uv;
    #endif
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "StudioImage"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex QuadVert
            #pragma fragment frag

            half4 frag(QuadVaryings i) : SV_Target
            {
                float2 s = i.local;
                float d = ShapeDistance(s);
                float coverage = saturate(0.5 - d);
                if (coverage <= 0.0) discard;
                float2 d0 = _StDest.xy;
                float2 d1 = _StDest.zw;
                float4 c;
                if (all(s >= d0) && all(s <= d1))
                {
                    float2 local = (s - d0) / max(d1 - d0, 1e-6);
                    float2 uv = MapUv(local, _StUv0, _StUv1);
                    uv.y = lerp(uv.y, 1.0 - uv.y, step(0.5, _StUv0.w));
                    c = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0);
                    c.a = _StParams.z > 0.5 ? c.a : 1.0;
                }
                else if (_StParams.w > 0.5)
                {
                    float2 uv = MapUv(s, _StBgUv0, _StBgUv1);
                    uv.y = lerp(uv.y, 1.0 - uv.y, step(0.5, _StBgUv0.w));
                    c = float4(SAMPLE_TEXTURE2D_LOD(_StBgTex, sampler_StBgTex, uv, 0).rgb * 0.6, 1.0);
                }
                else
                {
                    c = _StBgColor;
                }
                float border = _StShape.w;
                if (border > 0.0)
                {
                    float inner = saturate(0.5 - (d + border));
                    c = lerp(_StBorderColor, c, inner);
                }
                c *= _StTint;
                c.a *= coverage;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "StudioShape"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex QuadVert
            #pragma fragment frag

            half4 frag(QuadVaryings i) : SV_Target
            {
                float d = ShapeDistance(i.local);
                float soft = max(_StParams.x, 0.0);
                float coverage = soft > 0.0 ? saturate(0.5 - d / max(soft, 1e-3)) : saturate(0.5 - d);
                if (soft > 0.0) coverage = coverage * coverage;
                float4 c = _StColor;
                if (_StParams.y > 0.5)
                {
                    float sheen = lerp(1.25, 0.85, i.local.y);
                    float rim = saturate(1.0 - abs(d + 1.0));
                    c.rgb = saturate(c.rgb * sheen + rim * 0.25);
                    c.a = saturate(c.a + rim * 0.2);
                }
                c.a *= coverage;
                if (c.a <= 0.0) discard;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "StudioGlyph"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            struct GlyphAttributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct GlyphVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            GlyphVaryings vert(GlyphAttributes v)
            {
                GlyphVaryings o;
                o.positionCS = mul(_StViewProj, float4(v.positionOS.xy + _StOffset.xy, 0.0, 1.0));
                o.uv = v.uv;
                return o;
            }

            half4 frag(GlyphVaryings i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * _StColor.a;
                if (a <= 0.0) discard;
                return float4(_StColor.rgb, a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "StudioReadback"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex FullVert
            #pragma fragment frag

            half4 frag(FullVaryings i) : SV_Target
            {
                float3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, 1.0 - i.uv.y)).rgb;
                if (_StParams.x > 0.5) c = LinearToSrgb(c);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
