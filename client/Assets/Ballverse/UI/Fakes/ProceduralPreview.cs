using System;
using UnityEngine;

namespace Ballverse.UI.Fakes
{
    /// <summary>
    /// Small animated stand-in for a simulation render target (editor preview of the UI without the simulation):
    /// balls bouncing inside a rotating ring, drawn into a Texture2D on the CPU.
    /// </summary>
    public sealed class ProceduralPreview : IDisposable
    {
        readonly Texture2D _texture;
        readonly Color32[] _pixels;
        readonly Color32 _background;
        readonly Color32[] _palette;
        readonly Vector2[] _pos;
        readonly Vector2[] _vel;
        readonly int _w;
        readonly int _h;
        float _angle;

        public Texture2D Texture => _texture;

        public ProceduralPreview(int width, int height, int balls, int seed, Color32 background)
        {
            _w = width;
            _h = height;
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "BallverseFakePreview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _pixels = new Color32[width * height];
            _background = background;
            _palette = new[]
            {
                new Color32(0xFF, 0x3B, 0x5C, 0xFF), new Color32(0x00, 0xD1, 0xFF, 0xFF), new Color32(0xFF, 0xD6, 0x0A, 0xFF),
                new Color32(0x7C, 0xFF, 0x4F, 0xFF), new Color32(0x8B, 0x5C, 0xFF, 0xFF), new Color32(0xFF, 0x8A, 0x00, 0xFF),
            };
            _pos = new Vector2[balls];
            _vel = new Vector2[balls];
            var rnd = new System.Random(seed);
            for (int i = 0; i < balls; i++)
            {
                _pos[i] = new Vector2(width * (0.3f + 0.4f * (float)rnd.NextDouble()), height * (0.35f + 0.3f * (float)rnd.NextDouble()));
                float a = (float)(rnd.NextDouble() * Math.PI * 2);
                _vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (40f + 40f * (float)rnd.NextDouble());
            }
            Step(0f);
        }

        public void Step(float dt)
        {
            _angle += dt * 0.8f;
            var center = new Vector2(_w * 0.5f, _h * 0.5f);
            float ringR = _w * 0.42f;
            const float ballR = 6f;
            for (int i = 0; i < _pos.Length; i++)
            {
                _pos[i] += _vel[i] * dt;
                var d = _pos[i] - center;
                if (d.magnitude > ringR - ballR)
                {
                    var n = d.normalized;
                    _vel[i] = Vector2.Reflect(_vel[i], n);
                    _pos[i] = center + n * (ringR - ballR);
                }
            }
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = _background;
            DrawRing(center, ringR);
            for (int i = 0; i < _pos.Length; i++) DrawDisc(_pos[i], ballR, _palette[i % _palette.Length]);
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        void DrawRing(Vector2 c, float r)
        {
            var color = new Color32(0xE6, 0xF0, 0xFF, 0xFF);
            for (int k = 0; k < 720; k++)
            {
                float a = k * Mathf.PI / 360f;
                if (Mathf.Repeat(a - _angle, Mathf.PI * 2f) < 0.5f) continue; // the gap
                int x = Mathf.RoundToInt(c.x + Mathf.Cos(a) * r);
                int y = Mathf.RoundToInt(c.y + Mathf.Sin(a) * r);
                for (int t = -1; t <= 1; t++) Plot(x + t, y, color);
            }
        }

        void DrawDisc(Vector2 p, float r, Color32 color)
        {
            int x0 = Mathf.FloorToInt(p.x - r);
            int x1 = Mathf.CeilToInt(p.x + r);
            int y0 = Mathf.FloorToInt(p.y - r);
            int y1 = Mathf.CeilToInt(p.y + r);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x - p.x) * (x - p.x) + (y - p.y) * (y - p.y) <= r * r) Plot(x, y, color);
        }

        void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            _pixels[y * _w + x] = c;
        }

        public void Dispose()
        {
            if (_texture != null) Ballverse.UI.UnityObjects.Destroy(_texture);
        }
    }
}
