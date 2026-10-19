#ifndef BALLVERSE_COMMON_INCLUDED
#define BALLVERSE_COMMON_INCLUDED

// Shared helpers of the Ballverse simulation renderer (Ballverse.Render). Include after URP's Core.hlsl.
// All scene passes write premultiplied alpha (Blend One OneMinusSrcAlpha) into an HDR buffer; colours above 1 bloom.

// World (drawing-plane metres) -> clip space. Set by BallverseRenderer per frame as
// GL.GetGPUProjectionMatrix(orthographic view, renderIntoTexture: true): the renderer never relies on the camera matrices,
// so the same draw works for live preview and offline export.
float4x4 _BvViewProj;

float4 BvWorldToClip(float3 worldPos)
{
    return mul(_BvViewProj, float4(worldPos, 1.0));
}

// Vertex / instance colours arrive as sRGB-encoded values (theme colours are defined in sRGB).
float3 BvSrgbToLinear(float3 c)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return c;
#else
    float3 lo = c / 12.92;
    float3 hi = pow(max((c + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(c, 0.04045));
#endif
}

float2 BvRotate(float2 p, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2(c * p.x - s * p.y, s * p.x + c * p.y);
}

// Full-screen triangle for the background and post passes (draw 3 vertices with DrawProcedural). Same as the SRP core's
// GetFullScreenTriangleVertexPosition / TexCoord but without the swap-chain pre-rotation (these passes only ever target
// render textures). The uv is in image convention (0 = bottom) on every platform, and RT-to-RT passes keep the memory
// orientation of Unity render textures.
float4 BvFullScreenPosition(uint vertexID)
{
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    return float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
}

float2 BvFullScreenUV(uint vertexID)
{
#if UNITY_UV_STARTS_AT_TOP
    return float2((vertexID << 1) & 2, 1.0 - (vertexID & 2));
#else
    return float2((vertexID << 1) & 2, vertexID & 2);
#endif
}

// Cheap hash in [0, 1) for procedural stars / bokeh (deterministic per cell).
float BvHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

#endif // BALLVERSE_COMMON_INCLUDED
