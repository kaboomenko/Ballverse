// Procedural background of the simulation slot (spec §2.2 background, §2.10 themes): one full-screen triangle.
//
// _BgTop, _BgBottom : linear colours of the vertical gradient
// _BgGrid           : linear rgb + alpha of the 1 m world grid
// _BgStar           : linear star colour
// _BgParams         : x = style (0 solid, 1 gradient, 2 grid, 3 stars, 4 blur), y = time (s), z = grid spacing (m), w = unused
// _BgView           : visible world rect (minX, minY, width, height) in metres
// _BgPixels         : target size in pixels (w, h, 1/w, 1/h)
Shader "Hidden/Ballverse/Background"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BallverseBackground"
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BallverseCommon.hlsl"

            float4 _BgTop;
            float4 _BgBottom;
            float4 _BgGrid;
            float4 _BgStar;
            float4 _BgParams;
            float4 _BgView;
            float4 _BgPixels;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = BvFullScreenPosition(vertexID);
                o.uv = BvFullScreenUV(vertexID);   // 0 = bottom of the image on every platform
                return o;
            }

            float GridLines(float2 world, float spacing)
            {
                float2 g = world / spacing;
                float2 w = max(fwidth(g), 1e-5);
                float2 d = abs(frac(g - 0.5) - 0.5) / w;
                return 1.0 - saturate(min(d.x, d.y) - 0.5);
            }

            float Stars(float2 uv, float time)
            {
                float2 px = uv * _BgPixels.xy;
                float cell = max(_BgPixels.y / 48.0, 8.0);
                float2 id = floor(px / cell);
                float h = BvHash21(id);
                if (h > 0.22) return 0.0;
                float2 center = (id + float2(BvHash21(id + 17.0), BvHash21(id + 31.0))) * cell;
                float size = lerp(0.6, 1.8, BvHash21(id + 5.0)) * (_BgPixels.y / 1920.0) * 1.6 + 0.5;
                float d = length(px - center) / size;
                float twinkle = 0.65 + 0.35 * sin(time * lerp(1.0, 3.0, h * 4.0) + h * 40.0);
                return saturate(1.0 - d) * twinkle;
            }

            float3 Bokeh(float2 uv, float time)
            {
                float3 acc = 0.0;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float fk = (float)k;
                    float2 c = float2(BvHash21(float2(fk, 1.0)), BvHash21(float2(fk, 7.0)));
                    c += 0.05 * float2(sin(time * 0.13 + fk), cos(time * 0.11 + fk * 1.7));
                    float rad = lerp(0.12, 0.3, BvHash21(float2(fk, 3.0)));
                    float2 d = (uv - c) * float2(_BgPixels.x * _BgPixels.w, 1.0);
                    float blob = saturate(1.0 - length(d) / rad);
                    acc += blob * blob * lerp(_BgTop.rgb, 1.0, 0.25) * 0.18;
                }
                return acc;
            }

            half4 frag(Varyings i) : SV_Target
            {
                int style = (int)(_BgParams.x + 0.5);
                float2 world = _BgView.xy + i.uv * _BgView.zw;
                float grid = GridLines(world, max(_BgParams.z, 0.05));
                float stars = Stars(i.uv, _BgParams.y);
                float3 bokeh = Bokeh(i.uv, _BgParams.y);

                float3 col = style == 0 ? _BgBottom.rgb : lerp(_BgBottom.rgb, _BgTop.rgb, saturate(i.uv.y));
                if (style == 2) col = lerp(col, _BgGrid.rgb, grid * _BgGrid.a);
                else if (style == 3) col += _BgStar.rgb * stars;
                else if (style == 4) col += bokeh;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
