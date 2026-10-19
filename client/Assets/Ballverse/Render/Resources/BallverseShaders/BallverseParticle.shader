// Deterministic VFX particles (Ballverse.Core.RenderGeometry.VfxSystem), GPU-instanced quads [-0.5, 0.5]^2 scaled by the
// instance matrix to the particle size.
//
// _PColor : linear rgb, alpha (fade baked in)
// _PParams: x = shape (0 dot, 1 rect, 2 streak, 3 ring, 4 triangle), y = emission, z = ring width fraction, w = unused
Shader "Hidden/Ballverse/Particle"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BallverseParticle"
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

            UNITY_INSTANCING_BUFFER_START(ParticleProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _PParams)
            UNITY_INSTANCING_BUFFER_END(ParticleProps)

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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = BvWorldToClip(TransformObjectToWorld(v.positionOS));
                o.local = v.positionOS.xy * 2.0;   // [-1, 1]
                o.color = UNITY_ACCESS_INSTANCED_PROP(ParticleProps, _PColor);
                o.params = UNITY_ACCESS_INSTANCED_PROP(ParticleProps, _PParams);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.local;
                float r = length(p);
                float aaR = max(fwidth(r), 1e-4);
                float2 aa2 = max(fwidth(p), 1e-4);
                // Triangle distance and every derivative are evaluated before branching on the shape.
                float2 t = float2(abs(p.x), p.y + 0.35);
                float triD = max(t.x * 0.8660254 + t.y * 0.5, -t.y) - 0.5;
                float triAa = max(fwidth(triD), 1e-4);
                int shape = (int)(i.params.x + 0.5);
                float a;
                if (shape == 0)
                {
                    float d = saturate(1.0 - r * r);
                    a = d * sqrt(d);
                }
                else if (shape == 1)
                {
                    float2 e = saturate((1.0 - abs(p)) / aa2 + 0.5);
                    a = e.x * e.y;
                }
                else if (shape == 2)
                {
                    // Soft streak: bright core along x, fading at the tail (x < 0).
                    float across = saturate(1.0 - abs(p.y));
                    float along = saturate(1.0 - abs(p.x));
                    a = across * across * along * lerp(0.35, 1.0, saturate(p.x * 0.5 + 0.5));
                }
                else if (shape == 3)
                {
                    float w = clamp(i.params.z, 0.02, 1.0);
                    float outer = saturate((1.0 - r) / aaR + 0.5);
                    float inner = saturate((r - (1.0 - w)) / aaR + 0.5);
                    a = outer * inner;
                }
                else
                {
                    // Equilateral-ish triangle pointing up.
                    a = saturate(-triD / triAa + 0.5);
                }
                a *= i.color.a;
                float3 rgb = i.color.rgb * i.params.y;
                return half4(rgb * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
