using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render
{
    /// <summary>
    /// Records GPU-instanced draws of balls (SDF shader) and particles into a command buffer (spec §7.7 "один
    /// инстансированный draw call на материал"). Chunks of <see cref="ChunkSize"/> instances (the shaders' maxcount); per-
    /// instance data goes through reused arrays and property blocks (a command buffer copies a block's values when the
    /// draw is recorded), so recording allocates nothing. Falls back to one draw per instance on GPUs without instancing.
    /// </summary>
    sealed class InstanceDrawer
    {
        public const int ChunkSize = 128;

        // Shader skin modes (BallverseSdfBall.shader).
        const float ModeSolid = 0f, ModeGradient = 1f, ModeOutline = 2f, ModeGloss = 3f, ModeTextured = 4f, ModeGlass = 5f,
            ModeGlassSimple = 6f, ModePlanet = 7f;

        readonly Matrix4x4[] _matrices = new Matrix4x4[ChunkSize];
        readonly Vector4[] _colors = new Vector4[ChunkSize];
        readonly Vector4[] _params = new Vector4[ChunkSize];
        readonly Vector4[] _rects = new Vector4[ChunkSize];
        readonly MaterialPropertyBlock _ballBlock = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock _particleBlock = new MaterialPropertyBlock();
        readonly MaterialPropertyBlock _singleBlock = new MaterialPropertyBlock();
        readonly Dictionary<int, Vector4> _skinRects = new Dictionary<int, Vector4>(64);
        BallverseSkinAtlas _skinAtlas;

        /// <summary>Forgets cached skin lookups (new stream or atlas).</summary>
        public void ResetSkins()
        {
            _skinRects.Clear();
        }

        public void DrawBalls(CommandBuffer cmd, RenderResources res, ReadOnlySpan<BallInstance> balls, RenderContext ctx, bool simplifiedGlass)
        {
            Material mat = res.BallMaterial;
            if (mat == null || balls.Length == 0) return;
            if (!ReferenceEquals(_skinAtlas, ctx.Skins))
            {
                _skinAtlas = ctx.Skins;
                _skinRects.Clear();
            }
            Texture skinTex = _skinAtlas != null && _skinAtlas.Atlas != null ? (Texture)_skinAtlas.Atlas : Texture2D.whiteTexture;
            _ballBlock.SetTexture(ShaderIds.SkinTex, skinTex);
            _singleBlock.SetTexture(ShaderIds.SkinTex, skinTex);
            float quadScale = 2f * (1f + RenderResources.BallGlowMargin);
            int n = 0;
            for (int i = 0; i < balls.Length; i++)
            {
                BallInstance b = balls[i];
                Quaternion rot = Quaternion.Euler(0f, 0f, b.SquashAngle * Mathf.Rad2Deg);
                _matrices[n] = Matrix4x4.TRS(new Vector3(b.X, b.Y, 0f), rot, new Vector3(b.RadiusX * quadScale, b.RadiusY * quadScale, 1f));
                _colors[n] = ColorConvert.ToWorking(b.Color);
                Vector4 rect = default;
                float mode = SkinMode(b, simplifiedGlass);
                if (mode == ModeTextured && !TryGetSkinRect(b.SkinId, ctx, out rect)) mode = ModeSolid;
                _params[n] = new Vector4(mode, b.Emission, b.SkinAngle - b.SquashAngle, 0f);
                _rects[n] = rect;
                n++;
                if (n == ChunkSize)
                {
                    FlushBalls(cmd, res, mat, n);
                    n = 0;
                }
            }
            if (n > 0) FlushBalls(cmd, res, mat, n);
        }

        static float SkinMode(in BallInstance b, bool simplifiedGlass)
        {
            switch (b.Skin)
            {
                case BallSkin.Gradient: return ModeGradient;
                case BallSkin.Outline: return ModeOutline;
                case BallSkin.Gloss: return ModeGloss;
                case BallSkin.Emoji:
                case BallSkin.Flag: return ModeTextured;
            }
            switch (b.Finish)
            {
                case BallFinish.Gloss: return ModeGloss;
                case BallFinish.Glass: return simplifiedGlass ? ModeGlassSimple : ModeGlass;
                case BallFinish.Planet: return ModePlanet;
                default: return ModeSolid;
            }
        }

        bool TryGetSkinRect(int skinId, RenderContext ctx, out Vector4 rect)
        {
            if (_skinRects.TryGetValue(skinId, out rect)) return rect.z > 0f;
            rect = default;
            if (_skinAtlas != null && ctx.Labels != null)
            {
                string key = ctx.Labels(skinId);
                if (_skinAtlas.TryGetRect(key, out Rect uv)) rect = new Vector4(uv.x, uv.y, uv.width, uv.height);
            }
            _skinRects[skinId] = rect;   // cached either way (one dictionary insert per skin per stream)
            return rect.z > 0f;
        }

        void FlushBalls(CommandBuffer cmd, RenderResources res, Material mat, int count)
        {
            if (res.InstancingSupported)
            {
                _ballBlock.SetVectorArray(ShaderIds.BallColor, _colors);
                _ballBlock.SetVectorArray(ShaderIds.BallParams, _params);
                _ballBlock.SetVectorArray(ShaderIds.BallSkinRect, _rects);
                cmd.DrawMeshInstanced(res.Quad, 0, mat, 0, _matrices, count, _ballBlock);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                _singleBlock.SetVector(ShaderIds.BallColor, _colors[i]);
                _singleBlock.SetVector(ShaderIds.BallParams, _params[i]);
                _singleBlock.SetVector(ShaderIds.BallSkinRect, _rects[i]);
                cmd.DrawMesh(res.Quad, _matrices[i], mat, 0, 0, _singleBlock);
            }
        }

        public void DrawParticles(CommandBuffer cmd, RenderResources res, ReadOnlySpan<ParticleInstance> particles)
        {
            Material mat = res.ParticleMaterial;
            if (mat == null || particles.Length == 0) return;
            int n = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleInstance p = particles[i];
                float sy = p.Shape == ParticleShape.Ring ? p.SizeX : p.SizeY;
                Quaternion rot = Quaternion.Euler(0f, 0f, p.Rotation * Mathf.Rad2Deg);
                _matrices[n] = Matrix4x4.TRS(new Vector3(p.X, p.Y, 0f), rot, new Vector3(Mathf.Max(p.SizeX, 1e-4f), Mathf.Max(sy, 1e-4f), 1f));
                _colors[n] = ColorConvert.ToWorking(p.Color);
                _params[n] = new Vector4((float)p.Shape, p.Emission, p.Shape == ParticleShape.Ring ? p.SizeY : 0f, 0f);
                n++;
                if (n == ChunkSize)
                {
                    FlushParticles(cmd, res, mat, n);
                    n = 0;
                }
            }
            if (n > 0) FlushParticles(cmd, res, mat, n);
        }

        void FlushParticles(CommandBuffer cmd, RenderResources res, Material mat, int count)
        {
            if (res.InstancingSupported)
            {
                _particleBlock.SetVectorArray(ShaderIds.ParticleColor, _colors);
                _particleBlock.SetVectorArray(ShaderIds.ParticleParams, _params);
                cmd.DrawMeshInstanced(res.Quad, 0, mat, 0, _matrices, count, _particleBlock);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                _singleBlock.SetVector(ShaderIds.ParticleColor, _colors[i]);
                _singleBlock.SetVector(ShaderIds.ParticleParams, _params[i]);
                cmd.DrawMesh(res.Quad, _matrices[i], mat, 0, 0, _singleBlock);
            }
        }
    }
}
