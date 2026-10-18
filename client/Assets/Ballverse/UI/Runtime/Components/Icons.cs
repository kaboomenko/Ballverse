using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    /// <summary>Icon set of the interface (spec §4.4: own outline icons, 2 pt stroke, round caps).</summary>
    public enum IconKind
    {
        None = 0,
        Back, Close, Gear, Retry, Play, Pause, Dice, Speed, Film, Plus, Minus, Check, ChevronRight, ChevronDown,
        Share, Download, Trash, Copy, Edit, Layout, Video, Simulation, Text, Sound, Undo, Redo, Fullscreen,
        Lock, Modes, Projects, Shop, Swap, Rotate, Mirror, Info, Star, Mail, Shield, Storage, Globe, Palette, Eye,
        Sparkle, Trophy, Hand, Music, Presets, Restore, Clip, More, AlignLeft, AlignCenter, AlignRight,
    }

    /// <summary>
    /// Draws <see cref="IconKind"/> icons with the vector API of UI Toolkit (<c>Painter2D</c>) in the element's text
    /// colour, so icons follow the theme and pressed states. In UXML an icon is a plain element with classes
    /// <c>bv-icon icon--{kind}</c> (kebab-case kind, e.g. <c>icon--chevron-right</c>); <see cref="AttachAll"/> wires them.
    /// </summary>
    public static class Icons
    {
        public const string IconClass = "bv-icon";
        const string KindPrefix = "icon--";
        const float Grid = 24f;

        sealed class KindBox
        {
            public IconKind Kind;
        }

        // Weak keys: icons of destroyed screens are collected with their elements.
        static readonly ConditionalWeakTable<VisualElement, KindBox> s_kinds = new ConditionalWeakTable<VisualElement, KindBox>();
        static Dictionary<string, IconKind> s_byName;

        public static VisualElement Create(IconKind kind, string extraClass = null)
        {
            var e = new VisualElement();
            e.AddToClassList(IconClass);
            if (extraClass != null) e.AddToClassList(extraClass);
            e.pickingMode = PickingMode.Ignore;
            Set(e, kind);
            return e;
        }

        /// <summary>Wires every <c>.bv-icon</c> under <paramref name="root"/> from its <c>icon--*</c> class.</summary>
        public static void AttachAll(VisualElement root)
        {
            root.Query<VisualElement>(className: IconClass).ForEach(e =>
            {
                if (s_kinds.TryGetValue(e, out _)) return;
                foreach (string c in e.GetClasses())
                {
                    if (!c.StartsWith(KindPrefix, StringComparison.Ordinal)) continue;
                    if (TryParse(c.Substring(KindPrefix.Length), out IconKind kind)) Set(e, kind);
                    break;
                }
            });
        }

        public static void Set(VisualElement e, IconKind kind)
        {
            if (!s_kinds.TryGetValue(e, out KindBox box))
            {
                box = new KindBox();
                s_kinds.Add(e, box);
                e.generateVisualContent += Draw;
                e.RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
                e.pickingMode = PickingMode.Ignore;
            }
            box.Kind = kind;
            e.MarkDirtyRepaint();
        }

        static void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            (evt.currentTarget as VisualElement)?.MarkDirtyRepaint();
        }

        /// <summary>Kebab-case name → kind ("chevron-right" → ChevronRight).</summary>
        public static bool TryParse(string name, out IconKind kind)
        {
            if (s_byName == null)
            {
                s_byName = new Dictionary<string, IconKind>(StringComparer.Ordinal);
                foreach (IconKind k in Enum.GetValues(typeof(IconKind))) s_byName[ToKebab(k.ToString())] = k;
            }
            return s_byName.TryGetValue(name, out kind);
        }

        static string ToKebab(string pascal)
        {
            var sb = new System.Text.StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        static void Draw(MeshGenerationContext ctx)
        {
            var e = ctx.visualElement;
            if (!s_kinds.TryGetValue(e, out KindBox box) || box.Kind == IconKind.None) return;
            var kind = box.Kind;
            var rect = e.contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 1f) return;
            var p = new IconPen(ctx, rect, size / Grid, e.resolvedStyle.color);
            IconShapes.Draw(kind, p);
        }
    }

    /// <summary>Coordinates on a 24×24 grid mapped into the element; strokes 2 grid units wide with round caps.</summary>
    internal sealed class IconPen
    {
        readonly Painter2D _p;
        readonly float _s;
        readonly Vector2 _o;
        readonly Color _color;

        public IconPen(MeshGenerationContext ctx, Rect rect, float scale, Color color)
        {
            _p = ctx.painter2D;
            _s = scale;
            float size = 24f * scale;
            _o = new Vector2(rect.x + (rect.width - size) * 0.5f, rect.y + (rect.height - size) * 0.5f);
            _color = color;
        }

        Vector2 P(float x, float y)
        {
            return new Vector2(_o.x + x * _s, _o.y + y * _s);
        }

        void Begin(float width = 2f)
        {
            _p.lineWidth = width * _s;
            _p.strokeColor = _color;
            _p.fillColor = _color;
            _p.lineCap = LineCap.Round;
            _p.lineJoin = LineJoin.Round;
            _p.BeginPath();
        }

        /// <summary>Open polyline.</summary>
        public void Line(params float[] xy)
        {
            Begin();
            _p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) _p.LineTo(P(xy[i], xy[i + 1]));
            _p.Stroke();
        }

        /// <summary>Closed polygon outline.</summary>
        public void Poly(params float[] xy)
        {
            Begin();
            _p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) _p.LineTo(P(xy[i], xy[i + 1]));
            _p.ClosePath();
            _p.Stroke();
        }

        /// <summary>Filled polygon.</summary>
        public void FillPoly(params float[] xy)
        {
            Begin();
            _p.MoveTo(P(xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) _p.LineTo(P(xy[i], xy[i + 1]));
            _p.ClosePath();
            _p.Fill();
        }

        public void Circle(float cx, float cy, float r)
        {
            Begin();
            _p.Arc(P(cx, cy), r * _s, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            _p.ClosePath();
            _p.Stroke();
        }

        public void Dot(float cx, float cy, float r)
        {
            Begin();
            _p.Arc(P(cx, cy), r * _s, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            _p.ClosePath();
            _p.Fill();
        }

        /// <summary>Arc from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (0° = +x, clockwise on screen).</summary>
        public void Arc(float cx, float cy, float r, float fromDeg, float toDeg)
        {
            Begin();
            _p.Arc(P(cx, cy), r * _s, Angle.Degrees(fromDeg), Angle.Degrees(toDeg), ArcDirection.Clockwise);
            _p.Stroke();
        }

        public void RoundRect(float x, float y, float w, float h, float r)
        {
            Begin();
            _p.MoveTo(P(x + r, y));
            _p.LineTo(P(x + w - r, y));
            _p.ArcTo(P(x + w, y), P(x + w, y + r), r * _s);
            _p.LineTo(P(x + w, y + h - r));
            _p.ArcTo(P(x + w, y + h), P(x + w - r, y + h), r * _s);
            _p.LineTo(P(x + r, y + h));
            _p.ArcTo(P(x, y + h), P(x, y + h - r), r * _s);
            _p.LineTo(P(x, y + r));
            _p.ArcTo(P(x, y), P(x + r, y), r * _s);
            _p.ClosePath();
            _p.Stroke();
        }
    }

    internal static class IconShapes
    {
        public static void Draw(IconKind kind, IconPen p)
        {
            switch (kind)
            {
                case IconKind.Back: p.Line(15, 5, 8, 12, 15, 19); break;
                case IconKind.Close: p.Line(6, 6, 18, 18); p.Line(18, 6, 6, 18); break;
                case IconKind.ChevronRight: p.Line(9, 5, 16, 12, 9, 19); break;
                case IconKind.ChevronDown: p.Line(5, 9, 12, 16, 19, 9); break;
                case IconKind.Plus: p.Line(12, 5, 12, 19); p.Line(5, 12, 19, 12); break;
                case IconKind.Minus: p.Line(5, 12, 19, 12); break;
                case IconKind.Check: p.Line(5, 12.5f, 10, 17.5f, 19, 7); break;
                case IconKind.More: p.Dot(6, 12, 1.6f); p.Dot(12, 12, 1.6f); p.Dot(18, 12, 1.6f); break;
                case IconKind.Gear:
                    p.Circle(12, 12, 3.2f);
                    p.Circle(12, 12, 7.5f);
                    p.Line(12, 2.5f, 12, 4.5f); p.Line(12, 19.5f, 12, 21.5f);
                    p.Line(2.5f, 12, 4.5f, 12); p.Line(19.5f, 12, 21.5f, 12);
                    p.Line(5.3f, 5.3f, 6.7f, 6.7f); p.Line(17.3f, 17.3f, 18.7f, 18.7f);
                    p.Line(5.3f, 18.7f, 6.7f, 17.3f); p.Line(17.3f, 6.7f, 18.7f, 5.3f);
                    break;
                case IconKind.Retry: p.Arc(12, 12, 7, -60, 240); p.Line(19, 4, 18.1f, 8.4f, 13.8f, 7.6f); break;
                case IconKind.Restore: p.Arc(12, 12, 7, 120, 420); p.Line(5, 4, 5.9f, 8.4f, 10.2f, 7.6f); break;
                case IconKind.Undo: p.Line(9, 6, 5, 10, 9, 14); p.Line(5, 10, 14, 10); p.Arc(14, 14.5f, 4.5f, -90, 90); p.Line(14, 19, 10, 19); break;
                case IconKind.Redo: p.Line(15, 6, 19, 10, 15, 14); p.Line(19, 10, 10, 10); p.Arc(10, 14.5f, 4.5f, 90, 270); p.Line(10, 19, 14, 19); break;
                case IconKind.Play: p.FillPoly(8, 5, 19, 12, 8, 19); break;
                case IconKind.Pause: p.Line(9, 6, 9, 18); p.Line(15, 6, 15, 18); break;
                case IconKind.Dice:
                    p.RoundRect(4, 4, 16, 16, 4);
                    p.Dot(8.5f, 8.5f, 1.5f); p.Dot(15.5f, 8.5f, 1.5f); p.Dot(12, 12, 1.5f);
                    p.Dot(8.5f, 15.5f, 1.5f); p.Dot(15.5f, 15.5f, 1.5f);
                    break;
                case IconKind.Speed: p.Poly(13, 3, 6, 13.5f, 11.5f, 13.5f, 10.5f, 21, 18, 10.5f, 12.5f, 10.5f); break;
                case IconKind.Film:
                    p.RoundRect(4, 5, 16, 14, 3);
                    p.Line(8, 5, 8, 19); p.Line(16, 5, 16, 19);
                    p.Line(4, 12, 8, 12); p.Line(16, 12, 20, 12);
                    break;
                case IconKind.Clip: p.Circle(7, 7, 2.5f); p.Circle(7, 17, 2.5f); p.Line(9, 8.5f, 19, 17); p.Line(9, 15.5f, 19, 7); break;
                case IconKind.Share: p.Line(12, 15, 12, 4); p.Line(8, 8, 12, 4, 16, 8); p.Line(7, 11, 5, 11, 5, 20, 19, 20, 19, 11, 17, 11); break;
                case IconKind.Download: p.Line(12, 4, 12, 15); p.Line(8, 11, 12, 15, 16, 11); p.Line(5, 19, 19, 19); break;
                case IconKind.Trash: p.Line(4, 7, 20, 7); p.Line(9, 7, 9, 4.5f, 15, 4.5f, 15, 7); p.Poly(6, 7, 7, 20, 17, 20, 18, 7); break;
                case IconKind.Copy: p.RoundRect(8, 8, 12, 12, 2.5f); p.Line(16, 8, 16, 5.5f, 14, 4, 6, 4, 4, 6, 4, 14, 5.5f, 16, 8, 16); break;
                case IconKind.Edit: p.Poly(5, 19, 5.5f, 15, 15, 5.5f, 18.5f, 9, 9, 18.5f); p.Line(13, 7.5f, 16.5f, 11); break;
                case IconKind.Layout: p.RoundRect(6, 3, 12, 18, 2.5f); p.Line(6, 12, 18, 12); break;
                case IconKind.Video: p.RoundRect(3, 6, 13, 12, 2.5f); p.Poly(16, 10, 21, 7, 21, 17, 16, 14); break;
                case IconKind.Simulation: p.Circle(12, 12, 8); p.Dot(12, 12, 3); break;
                case IconKind.Text: p.Line(5, 6, 19, 6); p.Line(12, 6, 12, 19); break;
                case IconKind.Sound:
                    p.Poly(4, 9.5f, 8, 9.5f, 13, 5, 13, 19, 8, 14.5f, 4, 14.5f);
                    p.Arc(13, 12, 4, -45, 45); p.Arc(13, 12, 7.5f, -45, 45);
                    break;
                case IconKind.Music: p.Line(9, 17, 9, 5, 19, 3.5f, 19, 15); p.Circle(6.5f, 17.5f, 2.5f); p.Circle(16.5f, 15.5f, 2.5f); break;
                case IconKind.Fullscreen:
                    p.Line(4, 9, 4, 4, 9, 4); p.Line(15, 4, 20, 4, 20, 9);
                    p.Line(20, 15, 20, 20, 15, 20); p.Line(9, 20, 4, 20, 4, 15);
                    break;
                case IconKind.Lock: p.RoundRect(5, 11, 14, 10, 2.5f); p.Arc(12, 11, 4, 180, 360); break;
                case IconKind.Modes: p.RoundRect(4, 4, 7, 7, 2); p.RoundRect(13, 4, 7, 7, 2); p.RoundRect(4, 13, 7, 7, 2); p.RoundRect(13, 13, 7, 7, 2); break;
                case IconKind.Projects: p.Poly(3, 7, 3, 19, 21, 19, 21, 9, 12, 9, 10, 6, 4, 6); break;
                case IconKind.Shop: p.Poly(5, 8, 19, 8, 18, 20, 6, 20); p.Arc(12, 8, 3.5f, 180, 360); break;
                case IconKind.Swap: p.Line(7, 4, 7, 19); p.Line(4, 7, 7, 4, 10, 7); p.Line(17, 20, 17, 5); p.Line(14, 17, 17, 20, 20, 17); break;
                case IconKind.Rotate: p.Arc(12, 13, 7, 200, 470); p.Line(4, 6, 5.4f, 10.6f, 10, 9.4f); break;
                case IconKind.Mirror: p.Line(12, 3, 12, 21); p.Poly(9, 7, 3, 17, 9, 17); p.Poly(15, 7, 21, 17, 15, 17); break;
                case IconKind.Info: p.Circle(12, 12, 9); p.Line(12, 11, 12, 16.5f); p.Dot(12, 7.5f, 1.2f); break;
                case IconKind.Star: p.Poly(12, 3.5f, 14.6f, 9, 20.5f, 9.6f, 16, 13.6f, 17.3f, 19.5f, 12, 16.4f, 6.7f, 19.5f, 8, 13.6f, 3.5f, 9.6f, 9.4f, 9); break;
                case IconKind.Mail: p.RoundRect(3, 5.5f, 18, 13, 2.5f); p.Line(4, 7, 12, 13, 20, 7); break;
                case IconKind.Shield: p.Poly(12, 3, 19.5f, 6, 19, 13, 12, 21, 5, 13, 4.5f, 6); p.Line(8.5f, 12, 11, 14.5f, 15.5f, 9.5f); break;
                case IconKind.Storage: p.RoundRect(4, 4, 16, 16, 3); p.Line(4, 14, 20, 14); p.Dot(16.5f, 17, 1.2f); break;
                case IconKind.Globe: p.Circle(12, 12, 9); p.Line(3, 12, 21, 12); p.Arc(12, 12, 9, -90, 90); p.Line(12, 3, 12, 21); break;
                case IconKind.Palette:
                    p.Circle(12, 12, 9);
                    p.Dot(8, 10, 1.4f); p.Dot(12, 7, 1.4f); p.Dot(16, 10, 1.4f); p.Dot(15, 15, 1.4f);
                    break;
                case IconKind.Eye: p.Poly(2.5f, 12, 7, 7, 12, 5.5f, 17, 7, 21.5f, 12, 17, 17, 12, 18.5f, 7, 17); p.Circle(12, 12, 3); break;
                case IconKind.Sparkle: p.Poly(12, 3, 13.8f, 10.2f, 21, 12, 13.8f, 13.8f, 12, 21, 10.2f, 13.8f, 3, 12, 10.2f, 10.2f); break;
                case IconKind.Trophy:
                    p.Poly(7, 4, 17, 4, 16.5f, 10, 12, 14, 7.5f, 10);
                    p.Line(7, 6, 4, 6, 4.5f, 9, 7.5f, 10); p.Line(17, 6, 20, 6, 19.5f, 9, 16.5f, 10);
                    p.Line(12, 14, 12, 18); p.Line(8, 20, 16, 20);
                    break;
                case IconKind.Hand: p.Line(9, 13, 9, 5); p.Line(12, 11, 12, 4); p.Line(15, 11.5f, 15, 5.5f); p.Line(18, 12, 18, 8); p.Line(18, 12, 18, 15, 14, 20.5f, 10, 20.5f, 6, 15.5f, 6, 12.5f, 9, 13); break;
                case IconKind.AlignLeft: p.Line(4, 6, 20, 6); p.Line(4, 10, 14, 10); p.Line(4, 14, 20, 14); p.Line(4, 18, 14, 18); break;
                case IconKind.AlignCenter: p.Line(4, 6, 20, 6); p.Line(7, 10, 17, 10); p.Line(4, 14, 20, 14); p.Line(7, 18, 17, 18); break;
                case IconKind.AlignRight: p.Line(4, 6, 20, 6); p.Line(10, 10, 20, 10); p.Line(4, 14, 20, 14); p.Line(10, 18, 20, 18); break;
                case IconKind.Presets: p.Line(4, 7, 20, 7); p.Line(4, 12, 20, 12); p.Line(4, 17, 20, 17); p.Dot(9, 7, 2); p.Dot(15, 12, 2); p.Dot(7, 17, 2); break;
            }
        }
    }
}
