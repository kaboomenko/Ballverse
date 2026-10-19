using System;
using Ballverse.Core.RenderGeometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render
{
    /// <summary>
    /// The renderer's own render textures: the HDR, multisampled scene buffer and the bloom mip chain. Recreated only when
    /// the size, MSAA, format or bloom mode changes (no per-frame allocation).
    /// </summary>
    sealed class RenderTargets : IDisposable
    {
        public const int MaxBloomLevels = 6;

        readonly RenderTexture[] _bloom = new RenderTexture[MaxBloomLevels];
        int _sceneWidth;
        int _sceneHeight;
        int _msaa;
        bool _point;
        BloomMode _bloomMode = (BloomMode)255;

        public RenderTexture Scene { get; private set; }
        public int BloomLevels { get; private set; }

        public RenderTexture Bloom(int level) => _bloom[level];

        static RenderTextureFormat s_hdrFormat = (RenderTextureFormat)(-1);

        /// <summary>HDR format with fallbacks (R11G11B10 → half float → 8-bit when the GPU lacks float targets).</summary>
        public static RenderTextureFormat HdrFormat
        {
            get
            {
                if ((int)s_hdrFormat >= 0) return s_hdrFormat;
                if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)) s_hdrFormat = RenderTextureFormat.RGB111110Float;
                else if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)) s_hdrFormat = RenderTextureFormat.ARGBHalf;
                else s_hdrFormat = RenderTextureFormat.ARGB32;
                return s_hdrFormat;
            }
        }

        public void Ensure(int width, int height, int msaa, bool pointFilter, BloomMode bloom)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var desc = new RenderTextureDescriptor(width, height, HdrFormat, 0)
            {
                msaaSamples = Math.Max(1, msaa),
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false,
            };
            int supported = SystemInfo.GetRenderTextureSupportedMSAASampleCount(desc);
            desc.msaaSamples = Math.Max(1, Math.Min(desc.msaaSamples, supported));

            if (Scene == null || width != _sceneWidth || height != _sceneHeight || desc.msaaSamples != _msaa)
            {
                Release(Scene);
                Scene = new RenderTexture(desc) { name = "BallverseScene", hideFlags = HideFlags.HideAndDontSave };
                Scene.Create();
                _sceneWidth = width;
                _sceneHeight = height;
                _msaa = desc.msaaSamples;
                _bloomMode = (BloomMode)255;   // chain depends on the scene size
            }
            if (_point != pointFilter || Scene.filterMode != (pointFilter ? FilterMode.Point : FilterMode.Bilinear))
            {
                _point = pointFilter;
                Scene.filterMode = pointFilter ? FilterMode.Point : FilterMode.Bilinear;
            }
            if (bloom != _bloomMode)
            {
                ReleaseBloom();
                _bloomMode = bloom;
                if (bloom != BloomMode.Off)
                {
                    int div = bloom == BloomMode.Full ? 2 : 4;
                    int w = Math.Max(1, width / div), h = Math.Max(1, height / div);
                    int levels = 0;
                    while (levels < MaxBloomLevels && Math.Min(w, h) >= 8)
                    {
                        var d = new RenderTextureDescriptor(w, h, HdrFormat, 0) { msaaSamples = 1, useMipMap = false, sRGB = false };
                        var rt = new RenderTexture(d)
                        {
                            name = "BallverseBloom" + levels,
                            hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp,
                        };
                        rt.Create();
                        _bloom[levels++] = rt;
                        w = Math.Max(1, w / 2);
                        h = Math.Max(1, h / 2);
                    }
                    BloomLevels = levels;
                }
            }
        }

        void ReleaseBloom()
        {
            for (int i = 0; i < _bloom.Length; i++)
            {
                Release(_bloom[i]);
                _bloom[i] = null;
            }
            BloomLevels = 0;
        }

        static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            RenderResources.Destroy(rt);
        }

        public void Dispose()
        {
            ReleaseBloom();
            Release(Scene);
            Scene = null;
        }
    }
}
