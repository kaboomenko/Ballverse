// BallverseGlass (spec §4.5): one shared backdrop per frame for all glass surfaces.
// Pass 0 Prefilter: maps screen UV onto the source slot ("cover" fit) and downsamples; outside the slot → backdrop colour.
// Pass 1 Down / Pass 2 Up: dual-filter (Kawase) blur (Bjørge, "Bandwidth-efficient rendering", SIGGRAPH 2015).
// Pass 3 Compose: for up to 8 rounded rectangles (glass surfaces, panel units, y down) adds edge refraction
//   (SDF normal offset), an edge highlight and the adaptive tint that keeps text at WCAG AA (4.5:1) per pixel.
// UI Toolkit samples the composed texture as each surface's background image (see GlassSurfaces.cs).
Shader "Hidden/Ballverse/Glass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _SlotRect;        // xy = slot min (screen UV, origin bottom-left), zw = slot size
        float4 _SrcScaleOffset;  // slot UV -> source UV: xy = scale, zw = offset ("cover")
        float4 _Backdrop;        // colour outside the slot
        float _Spread;           // prefilter tap distance in source texels
        float _Offset;           // dual-filter sample offset multiplier
        float4 _Rects[8];        // xMin, yMin, xMax, yMax in panel units (y down)
        float4 _Radii[8];        // x = corner radius in panel units
        float _RectCount;
        float4 _PanelSize;       // xy = panel size in panel units
        float _Refraction;       // max refraction offset, panel units (0 = off)
        float _Band;             // refraction band depth from the edge, panel units
        float4 _Tint;            // rgb = tint colour (linear), a = minimum tint opacity
        float _TextTarget;       // backdrop luminance bound that keeps 4.5:1 for the theme's text colour
        float _TintLum;          // luminance of the tint colour (linear)
        float _LightText;        // 1 = light text (tint darkens), 0 = dark text (tint lightens)
        float _Highlight;        // edge highlight strength (0 = off)
        float4 _LightDir;        // xy = direction towards the light, panel space (y down), normalized

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        half4 fragPrefilter(v2f i) : SV_Target
        {
            float2 slotUv = (i.uv - _SlotRect.xy) / max(_SlotRect.zw, float2(1e-5, 1e-5));
            if (any(slotUv < 0.0) || any(slotUv > 1.0))
                return half4(_Backdrop.rgb, 1.0);
            float2 srcUv = slotUv * _SrcScaleOffset.xy + _SrcScaleOffset.zw;
            float2 t = _MainTex_TexelSize.xy * _Spread;
            half3 c = tex2D(_MainTex, srcUv + float2(-t.x, -t.y)).rgb;
            c += tex2D(_MainTex, srcUv + float2(t.x, -t.y)).rgb;
            c += tex2D(_MainTex, srcUv + float2(-t.x, t.y)).rgb;
            c += tex2D(_MainTex, srcUv + float2(t.x, t.y)).rgb;
            return half4(c * 0.25, 1.0);
        }

        half4 fragDown(v2f i) : SV_Target
        {
            float2 hp = _MainTex_TexelSize.xy * 0.5 * _Offset;
            half4 s = tex2D(_MainTex, i.uv) * 4.0;
            s += tex2D(_MainTex, i.uv - hp);
            s += tex2D(_MainTex, i.uv + hp);
            s += tex2D(_MainTex, i.uv + float2(hp.x, -hp.y));
            s += tex2D(_MainTex, i.uv - float2(hp.x, -hp.y));
            return s * 0.125;
        }

        half4 fragUp(v2f i) : SV_Target
        {
            float2 hp = _MainTex_TexelSize.xy * 0.5 * _Offset;
            half4 s = tex2D(_MainTex, i.uv + float2(-hp.x * 2.0, 0.0));
            s += tex2D(_MainTex, i.uv + float2(-hp.x, hp.y)) * 2.0;
            s += tex2D(_MainTex, i.uv + float2(0.0, hp.y * 2.0));
            s += tex2D(_MainTex, i.uv + float2(hp.x, hp.y)) * 2.0;
            s += tex2D(_MainTex, i.uv + float2(hp.x * 2.0, 0.0));
            s += tex2D(_MainTex, i.uv + float2(hp.x, -hp.y)) * 2.0;
            s += tex2D(_MainTex, i.uv + float2(0.0, -hp.y * 2.0));
            s += tex2D(_MainTex, i.uv + float2(-hp.x, -hp.y)) * 2.0;
            return s * (1.0 / 12.0);
        }

        float SdRoundBox(float2 p, float2 halfSize, float r)
        {
            float2 q = abs(p) - halfSize + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }

        float RectSdf(int k, float2 p)
        {
            float4 rc = _Rects[k];
            float2 c = (rc.xy + rc.zw) * 0.5;
            float2 h = max((rc.zw - rc.xy) * 0.5, float2(0.0, 0.0));
            float r = min(_Radii[k].x, min(h.x, h.y));
            return SdRoundBox(p - c, h, r);
        }

        half4 fragCompose(v2f i) : SV_Target
        {
            float2 p = float2(i.uv.x * _PanelSize.x, (1.0 - i.uv.y) * _PanelSize.y);
            int count = (int)_RectCount;
            int hit = -1;
            float d = 1e5;
            [unroll]
            for (int k = 0; k < 8; k++)
            {
                if (k < count && hit < 0)
                {
                    float dk = RectSdf(k, p);
                    if (dk < 0.0)
                    {
                        hit = k;
                        d = dk;
                    }
                }
            }

            float2 uv = i.uv;
            float2 n = float2(0.0, 0.0);
            if (hit >= 0)
            {
                float dx = RectSdf(hit, p + float2(1.0, 0.0)) - RectSdf(hit, p - float2(1.0, 0.0));
                float dy = RectSdf(hit, p + float2(0.0, 1.0)) - RectSdf(hit, p - float2(0.0, 1.0));
                float len = length(float2(dx, dy));
                n = len > 1e-5 ? float2(dx, dy) / len : float2(0.0, 0.0);
                float t = saturate(1.0 + d / max(_Band, 1e-3));
                float2 shift = -n * (_Refraction * t * t);
                uv += float2(shift.x / _PanelSize.x, -shift.y / _PanelSize.y);
            }

            half3 col = tex2D(_MainTex, uv).rgb;
            if (hit < 0)
                return half4(col, 1.0);

            float3 lin = col;
            #ifdef UNITY_COLORSPACE_GAMMA
            lin = GammaToLinearSpace(col);
            #endif
            float lum = dot(lin, float3(0.2126, 0.7152, 0.0722));
            float a = 0.0;
            if (_LightText > 0.5)
            {
                if (lum > _TextTarget) a = saturate((lum - _TextTarget) / max(lum - _TintLum, 1e-4));
            }
            else
            {
                if (lum < _TextTarget) a = saturate((_TextTarget - lum) / max(_TintLum - lum, 1e-4));
            }
            a = max(a, _Tint.a);
            lin = lerp(lin, _Tint.rgb, a);

            float edge = saturate(1.0 - abs(d + 0.75) / 1.25);
            lin += edge * saturate(dot(n, _LightDir.xy)) * _Highlight;

            #ifdef UNITY_COLORSPACE_GAMMA
            col = LinearToGammaSpace(lin);
            #else
            col = lin;
            #endif
            return half4(col, 1.0);
        }
        ENDCG

        Pass
        {
            Name "Prefilter"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG
        }
        Pass
        {
            Name "Down"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragDown
            ENDCG
        }
        Pass
        {
            Name "Up"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragUp
            ENDCG
        }
        Pass
        {
            Name "Compose"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragCompose
            ENDCG
        }
    }
    Fallback Off
}
