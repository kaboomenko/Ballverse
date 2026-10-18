// SDF circle for balls (spec §7.7 "квад + SDF-шейдер круга, идеальный край при любом разрешении"), GPU-instanced:
// one draw per up to 128 balls with per-instance colour, skin and glow. The quad is [-0.5, 0.5]^2 scaled by the instance
// matrix to the ball diameter x (1 + _GlowMargin); the disc edge is at |local| = 1.
//
// _BallColor   : linear rgb, alpha
// _BallParams  : x = skin mode (0 solid, 1 gradient, 2 outline, 3 gloss, 4 textured, 5 glass, 6 glass simplified,
//                7 planet), y = emission, z = skin angle relative to the quad (radians), w = unused
// _BallSkinRect: uv rect (x, y, w, h) of the skin image in _SkinTex (textured mode)
Shader "Hidden/Ballverse/SdfBall"
{
    Properties
    {
        _SkinTex ("Skin atlas", 2D) = "white" {}
        _GlowMargin ("Glow margin", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BallverseSdfBall"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options nolightprobe nolodfade maxcount:128

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "BallverseCommon.hlsl"

            TEXTURE2D(_SkinTex);
            SAMPLER(sampler_SkinTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _SkinTex_ST;
                float _GlowMargin;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(BallProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BallColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BallParams)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BallSkinRect)
            UNITY_INSTANCING_BUFFER_END(BallProps)

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                float4 color : TEXCOORD1;
                float4 params : TEXCOORD2;
                float4 skinRect : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 world = TransformObjectToWorld(v.positionOS);
                o.positionCS = BvWorldToClip(world);
                o.local = v.positionOS.xy * 2.0 * (1.0 + _GlowMargin);
                o.color = UNITY_ACCESS_INSTANCED_PROP(BallProps, _BallColor);
                o.params = UNITY_ACCESS_INSTANCED_PROP(BallProps, _BallParams);
                o.skinRect = UNITY_ACCESS_INSTANCED_PROP(BallProps, _BallSkinRect);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.local;
                float r = length(p);
                float aa = max(fwidth(r), 1e-4);
                float cover = saturate((1.0 - r) / aa + 0.5);
                float2 q = BvRotate(p, -i.params.z);
                float z = sqrt(saturate(1.0 - r * r));
                // Sampled outside the branches (gradients must not depend on divergent flow).
                float2 skinUv = i.skinRect.xy + saturate(q * 0.5 + 0.5) * i.skinRect.zw;
                half4 skin = SAMPLE_TEXTURE2D(_SkinTex, sampler_SkinTex, skinUv);

                float3 rgb = i.color.rgb;
                float a = i.color.a;
                int mode = (int)(i.params.x + 0.5);
                float emission = i.params.y;

                if (mode == 1)
                {
                    rgb *= lerp(0.72, 1.28, saturate(p.y * 0.5 + 0.5));
                }
                else if (mode == 2)
                {
                    float ring = saturate((r - 0.76) / aa + 0.5);
                    a *= lerp(0.2, 1.0, ring);
                }
                else if (mode == 3)
                {
                    float3 n = float3(p, z);
                    float diff = saturate(dot(n, normalize(float3(-0.4, 0.6, 0.7))));
                    rgb *= 0.62 + 0.5 * diff;
                    float spec = pow(saturate(1.0 - length(p - float2(-0.35, 0.42)) / 0.42), 3.0);
                    rgb += spec * 0.85;
                }
                else if (mode == 4)
                {
                    float border = saturate((r - 0.9) / aa + 0.5);
                    rgb = lerp(lerp(rgb, skin.rgb, skin.a), rgb, border);
                }
                else if (mode == 5 || mode == 6)
                {
                    float rim = smoothstep(0.7, 1.0, r);
                    if (mode == 5)
                    {
                        float spec = pow(saturate(1.0 - length(p - float2(-0.38, 0.45)) / 0.3), 4.0);
                        a *= lerp(0.26, 0.92, rim);
                        rgb = lerp(rgb, 1.0, rim * 0.3) + spec;
                    }
                    else
                    {
                        a *= lerp(0.45, 0.85, rim);
                    }
                }
                else if (mode == 7)
                {
                    float3 n = float3(q, z);
                    float light = saturate(dot(n, normalize(float3(-0.5, 0.4, 0.75))));
                    float bands = 0.86 + 0.14 * sin(q.y * 10.0 + sin(q.x * 3.0) * 1.5);
                    rgb *= bands * (0.25 + 0.85 * light);
                }

                float3 col = rgb * emission;
                float alpha = a * cover;
                float3 outRgb = col * alpha;

                // Additive halo outside the disc for emissive balls (feeds the bloom; alpha 0 = pure light).
                float glow = saturate(emission - 1.0) * 0.7;
                float edge = saturate((1.0 + _GlowMargin - r) / max(_GlowMargin * 0.35, 1e-3));
                float halo = glow * exp(-max(r - 1.0, 0.0) * 6.0) * (1.0 - cover) * edge * i.color.a;
                outRgb += i.color.rgb * halo;

                return half4(outRgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
