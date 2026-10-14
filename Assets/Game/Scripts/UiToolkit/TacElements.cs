using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassEngine.Game.Ui
{
    /// <summary>Painter2D helpers shared by the custom-drawn elements.</summary>
    public static class TacDraw
    {
        public static Color A(Color c, float a) { c.a = a; return c; }

        public static void ChamferPath(Painter2D p, float w, float h, float c, float inset)
        {
            p.BeginPath();
            if (c < 0.5f)
            {
                p.MoveTo(new Vector2(inset, inset));
                p.LineTo(new Vector2(w - inset, inset));
                p.LineTo(new Vector2(w - inset, h - inset));
                p.LineTo(new Vector2(inset, h - inset));
                p.ClosePath();
                return;
            }
            p.MoveTo(new Vector2(c, inset));
            p.LineTo(new Vector2(w - inset, inset));
            p.LineTo(new Vector2(w - inset, h - c));
            p.LineTo(new Vector2(w - c, h - inset));
            p.LineTo(new Vector2(inset, h - inset));
            p.LineTo(new Vector2(inset, c));
            p.ClosePath();
        }

        public static void RoundPath(Painter2D p, Rect r, float rad)
        {
            rad = Mathf.Max(0f, Mathf.Min(rad, Mathf.Min(r.width, r.height) * 0.5f));
            p.BeginPath();
            if (rad < 0.5f)
            {
                p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath();
                return;
            }
            p.MoveTo(new Vector2(r.xMin + rad, r.yMin));
            p.LineTo(new Vector2(r.xMax - rad, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + rad), rad);
            p.LineTo(new Vector2(r.xMax, r.yMax - rad));
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - rad, r.yMax), rad);
            p.LineTo(new Vector2(r.xMin + rad, r.yMax));
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - rad), rad);
            p.LineTo(new Vector2(r.xMin, r.yMin + rad));
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + rad, r.yMin), rad);
            p.ClosePath();
        }

        /// <summary>Q 版面板：卡通实心投影 + 圆角实底 + 粗描边；glow>0 时外圈加一道强调色描边。</summary>
        public static void SoftFrame(Painter2D p, float w, float h, Color fill, Color line, Color accent,
            float radius, float border, float shadow, float glow)
        {
            if (w < 4f || h < 4f) return;
            float sh = shadow > 0.001f ? 5f : 0f;
            var body = new Rect(0f, 0f, w, h - sh);
            p.lineJoin = LineJoin.Round;
            if (sh > 0f)
            {
                p.fillColor = new Color(0.16f, 0.2f, 0.28f, shadow);
                RoundPath(p, new Rect(0f, sh, w, h - sh), radius); p.Fill();
            }
            if (fill.a > 0.001f) { p.fillColor = fill; RoundPath(p, body, radius); p.Fill(); }
            if (border > 0.01f)
            {
                p.strokeColor = line; p.lineWidth = border;
                float b = border * 0.5f;
                RoundPath(p, new Rect(b, b, w - border, h - sh - border), Mathf.Max(0f, radius - b)); p.Stroke();
            }
            if (glow > 0.01f)
            {
                p.strokeColor = A(accent, Mathf.Clamp01(glow));
                p.lineWidth = 3f;
                float b = border + 1.5f;
                RoundPath(p, new Rect(b, b, w - b * 2f, h - sh - b * 2f), Mathf.Max(0f, radius - b)); p.Stroke();
            }
        }

        public static void Frame(Painter2D p, float w, float h, Color fill, Color line, Color accent,
            float chamfer, float glow, bool brackets, bool topbar)
        {
            if (w < 4f || h < 4f) return;
            float c = Mathf.Min(chamfer, Mathf.Min(w, h) * 0.3f);
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;
            if (fill.a > 0.001f) { p.fillColor = fill; ChamferPath(p, w, h, c, 0f); p.Fill(); }
            if (glow > 0.01f)
            {
                for (int i = 3; i >= 1; i--)
                {
                    p.strokeColor = A(accent, glow * 0.07f * (4 - i));
                    p.lineWidth = 1f + i * 3f;
                    ChamferPath(p, w, h, c, 0.5f);
                    p.Stroke();
                }
            }
            p.lineWidth = 1f;
            p.strokeColor = line;
            ChamferPath(p, w, h, c, 0.5f);
            p.Stroke();

            p.strokeColor = accent;
            p.lineWidth = 2f;
            if (c >= 0.5f)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(1f, c + 8f));
                p.LineTo(new Vector2(1f, c));
                p.LineTo(new Vector2(c, 1f));
                p.LineTo(new Vector2(c + (topbar ? Mathf.Min(84f, w * 0.25f) : 8f), 1f));
                p.Stroke();
                // bottom-right chamfer tick
                p.strokeColor = A(accent, 0.55f);
                p.BeginPath();
                p.MoveTo(new Vector2(w - c - 10f, h - 1f));
                p.LineTo(new Vector2(w - c, h - 1f));
                p.LineTo(new Vector2(w - 1f, h - c));
                p.LineTo(new Vector2(w - 1f, h - c - 10f));
                p.Stroke();
            }
            if (brackets)
            {
                float L = Mathf.Min(14f, Mathf.Min(w, h) * 0.2f);
                p.strokeColor = A(accent, 0.8f);
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.MoveTo(new Vector2(w - L, 1f)); p.LineTo(new Vector2(w - 1f, 1f)); p.LineTo(new Vector2(w - 1f, L));
                p.MoveTo(new Vector2(1f, h - L)); p.LineTo(new Vector2(1f, h - 1f)); p.LineTo(new Vector2(L, h - 1f));
                p.Stroke();
            }
        }

        public static void Circle(Painter2D p, Vector2 c, float r, int seg = 40)
        {
            p.BeginPath();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var v = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
        }

        /// <summary>Arc polyline; angles in degrees, 0 = +x, 90 = up (screen y is flipped).</summary>
        public static void ArcTo(Painter2D p, Vector2 c, float r, float a0, float a1, bool move, int seg = 24)
        {
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg) * Mathf.Deg2Rad;
                var v = c + new Vector2(Mathf.Cos(a) * r, -Mathf.Sin(a) * r);
                if (i == 0 && move) p.MoveTo(v); else p.LineTo(v);
            }
        }

        public static void DashedRect(Painter2D p, Rect r, float dash, float gap)
        {
            p.BeginPath();
            DashLine(p, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), dash, gap);
            DashLine(p, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), dash, gap);
            DashLine(p, new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), dash, gap);
            DashLine(p, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), dash, gap);
            p.Stroke();
        }

        public static void DashLine(Painter2D p, Vector2 a, Vector2 b, float dash, float gap)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.01f) return;
            var d = (b - a) / len;
            for (float t = 0; t < len; t += dash + gap)
            {
                p.MoveTo(a + d * t);
                p.LineTo(a + d * Mathf.Min(len, t + dash));
            }
        }

        /// <summary>Batched axis-aligned quads (dots / blocks) written straight into the mesh.</summary>
        public sealed class Quads
        {
            readonly List<Vertex> verts = new List<Vertex>(4096);
            readonly List<ushort> idx = new List<ushort>(6144);
            public void Clear() { verts.Clear(); idx.Clear(); }
            public bool Full => verts.Count > 60000;
            public void Add(Rect r, Color32 c)
            {
                if (Full) return;
                ushort b = (ushort)verts.Count;
                verts.Add(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = c });
                verts.Add(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = c });
                verts.Add(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = c });
                verts.Add(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = c });
                idx.Add(b); idx.Add((ushort)(b + 1)); idx.Add((ushort)(b + 2));
                idx.Add(b); idx.Add((ushort)(b + 2)); idx.Add((ushort)(b + 3));
            }
            public void Flush(MeshGenerationContext mgc)
            {
                if (verts.Count == 0) return;
                var mwd = mgc.Allocate(verts.Count, idx.Count);
                mwd.SetAllVertices(verts.ToArray());
                mwd.SetAllIndices(idx.ToArray());
            }
        }

        public static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }

    /// <summary>Chamfered sci-fi panel. Appearance comes from USS custom properties (--frame-*).</summary>
    [UxmlElement]
    public partial class TacFrame : VisualElement
    {
        static readonly CustomStyleProperty<Color> sFill = new CustomStyleProperty<Color>("--frame-fill");
        static readonly CustomStyleProperty<Color> sLine = new CustomStyleProperty<Color>("--frame-line");
        static readonly CustomStyleProperty<Color> sAccent = new CustomStyleProperty<Color>("--frame-accent");
        static readonly CustomStyleProperty<float> sChamfer = new CustomStyleProperty<float>("--frame-chamfer");
        static readonly CustomStyleProperty<float> sGlow = new CustomStyleProperty<float>("--frame-glow");
        static readonly CustomStyleProperty<float> sBrackets = new CustomStyleProperty<float>("--frame-brackets");
        static readonly CustomStyleProperty<float> sTopbar = new CustomStyleProperty<float>("--frame-topbar");
        static readonly CustomStyleProperty<float> sRadius = new CustomStyleProperty<float>("--frame-radius");
        static readonly CustomStyleProperty<float> sBorder = new CustomStyleProperty<float>("--frame-border");
        static readonly CustomStyleProperty<float> sShadow = new CustomStyleProperty<float>("--frame-shadow");

        Color fill = new Color(0.03f, 0.07f, 0.1f, 0.88f), line = new Color(0.11f, 0.23f, 0.31f), accent = new Color(0.24f, 0.8f, 0.96f);
        float chamfer = 14f, glow, brackets = 1f, topbar = 1f, radius = -1f, border = 2f, shadow;

        public TacFrame()
        {
            AddToClassList("tac-frame");
            generateVisualContent += OnGenerate;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyle);
        }

        void OnStyle(CustomStyleResolvedEvent e)
        {
            var s = e.customStyle;
            if (s.TryGetValue(sFill, out var c)) fill = c;
            if (s.TryGetValue(sLine, out c)) line = c;
            if (s.TryGetValue(sAccent, out c)) accent = c;
            if (s.TryGetValue(sChamfer, out var f)) chamfer = f;
            if (s.TryGetValue(sGlow, out f)) glow = f;
            if (s.TryGetValue(sBrackets, out f)) brackets = f;
            if (s.TryGetValue(sTopbar, out f)) topbar = f;
            if (s.TryGetValue(sRadius, out f)) radius = f;
            if (s.TryGetValue(sBorder, out f)) border = f;
            if (s.TryGetValue(sShadow, out f)) shadow = f;
            MarkDirtyRepaint();
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            if (radius >= 0f)
                TacDraw.SoftFrame(mgc.painter2D, layout.width, layout.height, fill, line, accent, radius, border, shadow, glow);
            else
                TacDraw.Frame(mgc.painter2D, layout.width, layout.height, fill, line, accent, chamfer, glow, brackets > 0.5f, topbar > 0.5f);
        }
    }

    /// <summary>Full-screen backdrop: fine grid, major crosses, slow sweep line.</summary>
    [UxmlElement]
    public partial class TacBackdrop : VisualElement
    {
        float sweep;
        public TacBackdrop()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
            schedule.Execute(() => { if (TacTheme.Toy) return; sweep = (sweep + 0.0025f) % 1.2f; MarkDirtyRepaint(); }).Every(33);
        }

        void OnGenerate(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            float w = layout.width, h = layout.height;
            if (w < 2 || h < 2) return;
            if (TacTheme.Toy) { DrawToy(p, w, h); return; }
            p.lineWidth = 1f;
            p.strokeColor = new Color(0.24f, 0.8f, 0.96f, 0.035f);
            p.BeginPath();
            for (float x = 0; x < w; x += 32) { p.MoveTo(new Vector2(x + 0.5f, 0)); p.LineTo(new Vector2(x + 0.5f, h)); }
            for (float y = 0; y < h; y += 32) { p.MoveTo(new Vector2(0, y + 0.5f)); p.LineTo(new Vector2(w, y + 0.5f)); }
            p.Stroke();
            p.strokeColor = new Color(0.24f, 0.8f, 0.96f, 0.16f);
            p.BeginPath();
            for (float x = 160; x < w; x += 160)
                for (float y = 160; y < h; y += 160)
                {
                    p.MoveTo(new Vector2(x - 5, y + 0.5f)); p.LineTo(new Vector2(x + 6, y + 0.5f));
                    p.MoveTo(new Vector2(x + 0.5f, y - 5)); p.LineTo(new Vector2(x + 0.5f, y + 6));
                }
            p.Stroke();
            float sy = (sweep - 0.1f) * h;
            for (int i = 0; i < 6; i++)
            {
                p.strokeColor = new Color(0.24f, 0.8f, 0.96f, 0.012f * (6 - i));
                p.lineWidth = 2f + i * 6f;
                p.BeginPath(); p.MoveTo(new Vector2(0, sy)); p.LineTo(new Vector2(w, sy)); p.Stroke();
            }
        }
    }

    public partial class TacBackdrop
    {
        /// <summary>Q 版背景：斜向柔和条纹 + 几朵白云 + 底部草地丘陵。</summary>
        static void DrawToy(Painter2D p, float w, float h)
        {
            p.lineWidth = 26f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.09f);
            p.BeginPath();
            for (float x = -h; x < w; x += 72f) { p.MoveTo(new Vector2(x, h)); p.LineTo(new Vector2(x + h, 0)); }
            p.Stroke();
            p.fillColor = new Color(1f, 1f, 1f, 0.55f);
            for (int i = 0; i < 7; i++)
            {
                float cx = w * (0.06f + 0.15f * i) + TacDraw.Hash(i, 3) * 60f;
                float cy = h * (0.12f + 0.22f * TacDraw.Hash(i, 9));
                float s = 0.7f + TacDraw.Hash(i, 5) * 0.6f;
                Cloud(p, new Vector2(cx, cy), s);
            }
            // rolling hills along the bottom
            HillBand(p, w, h, h * 0.80f, 46f, 0.0031f, new Color(0.62f, 0.84f, 0.45f, 1f), 1);
            HillBand(p, w, h, h * 0.87f, 34f, 0.0047f, new Color(0.49f, 0.76f, 0.36f, 1f), 2);
        }

        static void Cloud(Painter2D p, Vector2 c, float s)
        {
            TacDraw.Circle(p, c, 34f * s); p.Fill();
            TacDraw.Circle(p, c + new Vector2(38f, 8f) * s, 26f * s); p.Fill();
            TacDraw.Circle(p, c + new Vector2(-38f, 10f) * s, 22f * s); p.Fill();
            TacDraw.RoundPath(p, new Rect(c.x - 60f * s, c.y + 4f * s, 124f * s, 30f * s), 15f * s); p.Fill();
        }

        static void HillBand(Painter2D p, float w, float h, float y0, float amp, float freq, Color col, int seed)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(new Vector2(0, h));
            for (float x = 0; x <= w + 8f; x += 8f)
                p.LineTo(new Vector2(x, y0 - amp * (0.55f * Mathf.Sin(x * freq + seed) + 0.45f * Mathf.Sin(x * freq * 2.3f + seed * 1.7f))));
            p.LineTo(new Vector2(w, h));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>Stroke icons on a 24-unit grid; colour follows the element's text colour.</summary>
    [UxmlElement]
    public partial class TacIcon : VisualElement
    {
        string m_Icon = "";
        [UxmlAttribute] public string icon { get => m_Icon; set { m_Icon = value ?? ""; MarkDirtyRepaint(); } }

        Painter2D p; Vector2 o; float s;

        public TacIcon()
        {
            AddToClassList("tac-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);
        void M(float x, float y) => p.MoveTo(P(x, y));
        void L(float x, float y) => p.LineTo(P(x, y));
        void Poly(params float[] xy) { M(xy[0], xy[1]); for (int i = 2; i < xy.Length; i += 2) L(xy[i], xy[i + 1]); }
        void RectS(float x, float y, float w, float h) { Poly(x, y, x + w, y, x + w, y + h, x, y + h); p.ClosePath(); }
        void Dot(float x, float y, float r) { TacDraw.Circle(p, P(x, y), r * s, 16); p.Fill(); }
        void Ring(float x, float y, float r) { TacDraw.Circle(p, P(x, y), r * s, 40); p.Stroke(); }
        void Arc(float x, float y, float r, float a0, float a1, bool move = true) => TacDraw.ArcTo(p, P(x, y), r * s, a0, a1, move);

        void OnGenerate(MeshGenerationContext mgc)
        {
            float w = layout.width, h = layout.height;
            if (w < 2 || h < 2) return;
            p = mgc.painter2D;
            s = Mathf.Min(w, h) / 24f;
            o = new Vector2((w - 24 * s) * 0.5f, (h - 24 * s) * 0.5f);
            var col = resolvedStyle.color;
            p.strokeColor = col; p.fillColor = col;
            p.lineWidth = Mathf.Max(1.3f, 1.75f * s);
            p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;

            switch (m_Icon)
            {
                case "attack":
                    p.BeginPath(); Poly(5, 19, 18, 6); Poly(15.5f, 4.5f, 19.5f, 4.5f, 19.5f, 8.5f); Poly(4, 15.5f, 8.5f, 20); p.Stroke();
                    p.BeginPath(); Poly(19, 19, 6, 6); Poly(8.5f, 4.5f, 4.5f, 4.5f, 4.5f, 8.5f); Poly(20, 15.5f, 15.5f, 20); p.Stroke();
                    break;
                case "move":
                    p.BeginPath(); Poly(3, 12, 19, 12); Poly(13, 6, 19.5f, 12, 13, 18); p.Stroke();
                    p.BeginPath(); Poly(3, 8, 6, 12, 3, 16); p.Stroke();
                    break;
                case "hold":
                    p.BeginPath(); Poly(12, 2.5f, 19.5f, 5.5f, 19.5f, 11, 16.5f, 17, 12, 21.5f, 7.5f, 17, 4.5f, 11, 4.5f, 5.5f); p.ClosePath(); p.Stroke();
                    p.BeginPath(); Poly(12, 7, 12, 17); Poly(8, 11, 16, 11); p.Stroke();
                    break;
                case "retreat":
                    p.BeginPath(); M(19, 18); L(19, 11); Arc(14, 11, 5, 0, 90, false); L(5, 6); p.Stroke();
                    p.BeginPath(); Poly(9, 2, 5, 6, 9, 10); p.Stroke();
                    break;
                case "follow":
                case "target":
                    Ring(12, 12, 7);
                    p.BeginPath(); Poly(12, 1.5f, 12, 6); Poly(12, 18, 12, 22.5f); Poly(1.5f, 12, 6, 12); Poly(18, 12, 22.5f, 12); p.Stroke();
                    Dot(12, 12, 1.8f);
                    break;
                case "overview":
                    p.BeginPath(); Poly(3, 8, 3, 3, 8, 3); Poly(16, 3, 21, 3, 21, 8); Poly(21, 16, 21, 21, 16, 21); Poly(8, 21, 3, 21, 3, 16); p.Stroke();
                    p.BeginPath(); RectS(8.5f, 8.5f, 7, 7); p.Stroke();
                    break;
                case "pause":
                    p.BeginPath(); RectS(6.5f, 5, 3.5f, 14); p.Fill();
                    p.BeginPath(); RectS(14, 5, 3.5f, 14); p.Fill();
                    break;
                case "play":
                    p.BeginPath(); Poly(7, 4.5f, 19.5f, 12, 7, 19.5f); p.ClosePath(); p.Fill();
                    break;
                case "settings":
                    Ring(12, 12, 3.2f); Ring(12, 12, 7.2f);
                    p.BeginPath();
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        M(12 + d.x * 7.2f, 12 + d.y * 7.2f); L(12 + d.x * 10.2f, 12 + d.y * 10.2f);
                    }
                    p.Stroke();
                    break;
                case "exit":
                    p.BeginPath(); Poly(13, 4, 4.5f, 4, 4.5f, 20, 13, 20); p.Stroke();
                    p.BeginPath(); Poly(10, 12, 21, 12); Poly(17, 8, 21, 12, 17, 16); p.Stroke();
                    break;
                case "library":
                    p.BeginPath(); RectS(3.5f, 4, 4.5f, 16); p.Stroke();
                    p.BeginPath(); RectS(9.5f, 4, 4.5f, 16); p.Stroke();
                    p.BeginPath(); Poly(15.5f, 5.5f, 19.5f, 4.5f, 21.5f, 19, 17.5f, 20); p.ClosePath(); p.Stroke();
                    break;
                case "help":
                    Ring(12, 12, 9.5f);
                    p.BeginPath(); M(9, 9.5f); p.BezierCurveTo(P(9, 6), P(15, 6), P(15, 9.5f)); p.BezierCurveTo(P(15, 12), P(12, 12), P(12, 14.5f)); p.Stroke();
                    Dot(12, 17.5f, 1.3f);
                    break;
                case "info":
                    Ring(12, 12, 9.5f);
                    p.BeginPath(); Poly(12, 11, 12, 17); p.Stroke();
                    Dot(12, 7.5f, 1.3f);
                    break;
                case "warn":
                    p.BeginPath(); Poly(12, 3, 22, 20.5f, 2, 20.5f); p.ClosePath(); p.Stroke();
                    p.BeginPath(); Poly(12, 9.5f, 12, 14.5f); p.Stroke();
                    Dot(12, 17.5f, 1.3f);
                    break;
                case "undo":
                    p.BeginPath(); Arc(12, 14, 7, 160, 0); L(19, 19); p.Stroke();
                    p.BeginPath(); Poly(2.5f, 8, 5.4f, 11.6f, 9.5f, 9.5f); p.Stroke();
                    break;
                case "redo":
                    p.BeginPath(); Arc(12, 14, 7, 20, 180); L(5, 19); p.Stroke();
                    p.BeginPath(); Poly(21.5f, 8, 18.6f, 11.6f, 14.5f, 9.5f); p.Stroke();
                    break;
                case "add":
                    p.BeginPath(); Poly(12, 5, 12, 19); Poly(5, 12, 19, 12); p.Stroke();
                    break;
                case "copy":
                    p.BeginPath(); RectS(8.5f, 8.5f, 11, 12); p.Stroke();
                    p.BeginPath(); Poly(5, 16, 4.5f, 16, 4.5f, 4, 15, 4, 15, 5); p.Stroke();
                    break;
                case "delete":
                    p.BeginPath(); Poly(3.5f, 6.5f, 20.5f, 6.5f); Poly(9, 6.5f, 9, 3.5f, 15, 3.5f, 15, 6.5f); p.Stroke();
                    p.BeginPath(); Poly(6, 6.5f, 7, 20.5f, 17, 20.5f, 18, 6.5f); p.Stroke();
                    p.BeginPath(); Poly(10, 10, 10, 17); Poly(14, 10, 14, 17); p.Stroke();
                    break;
                case "rotate":
                    p.BeginPath(); Arc(12, 12, 7.5f, 60, 330); p.Stroke();
                    p.BeginPath(); Poly(18, 2.5f, 15.8f, 5.6f, 19.6f, 7.4f); p.Stroke();
                    break;
                case "check":
                    p.BeginPath(); Poly(4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 6.5f); p.Stroke();
                    break;
                case "close":
                    p.BeginPath(); Poly(6, 6, 18, 18); Poly(18, 6, 6, 18); p.Stroke();
                    break;
                case "chev-r":
                    p.BeginPath(); Poly(9, 5, 16, 12, 9, 19); p.Stroke();
                    break;
                case "chev-l":
                    p.BeginPath(); Poly(15, 5, 8, 12, 15, 19); p.Stroke();
                    break;
                case "save":
                    p.BeginPath(); Poly(4, 4, 17, 4, 20, 7, 20, 20, 4, 20); p.ClosePath(); p.Stroke();
                    p.BeginPath(); RectS(8, 4, 7, 5); p.Stroke();
                    p.BeginPath(); RectS(7.5f, 13, 9, 7); p.Stroke();
                    break;
                case "swap":
                    p.BeginPath(); Poly(4, 8, 19, 8); Poly(15, 4, 19, 8, 15, 12); p.Stroke();
                    p.BeginPath(); Poly(20, 16, 5, 16); Poly(9, 12, 5, 16, 9, 20); p.Stroke();
                    break;
                case "grid":
                    p.BeginPath(); RectS(3.5f, 3.5f, 17, 17); p.Stroke();
                    p.BeginPath(); Poly(9.2f, 3.5f, 9.2f, 20.5f); Poly(14.8f, 3.5f, 14.8f, 20.5f); Poly(3.5f, 9.2f, 20.5f, 9.2f); Poly(3.5f, 14.8f, 20.5f, 14.8f); p.Stroke();
                    break;
                case "audio":
                    p.BeginPath(); Poly(4, 9, 8, 9, 13, 4.5f, 13, 19.5f, 8, 15, 4, 15); p.ClosePath(); p.Stroke();
                    p.BeginPath(); Arc(13, 12, 4.5f, -45, 45); p.Stroke();
                    p.BeginPath(); Arc(13, 12, 8, -45, 45); p.Stroke();
                    break;
                case "flag":
                    p.BeginPath(); Poly(5.5f, 2.5f, 5.5f, 21.5f); p.Stroke();
                    p.BeginPath(); Poly(5.5f, 4, 19, 4, 16, 8.5f, 19, 13, 5.5f, 13); p.Stroke();
                    break;
                case "map":
                    p.BeginPath(); Poly(3, 6, 9, 3.5f, 15, 6, 21, 3.5f, 21, 18, 15, 20.5f, 9, 18, 3, 20.5f); p.ClosePath(); p.Stroke();
                    p.BeginPath(); Poly(9, 3.5f, 9, 18); Poly(15, 6, 15, 20.5f); p.Stroke();
                    break;
                case "logo":
                    p.BeginPath();
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (i * 60f - 90f) * Mathf.Deg2Rad;
                        var v = new Vector2(12 + Mathf.Cos(a) * 10f, 12 + Mathf.Sin(a) * 10f);
                        if (i == 0) M(v.x, v.y); else L(v.x, v.y);
                    }
                    p.ClosePath(); p.Stroke();
                    p.BeginPath(); Poly(7, 14.5f, 12, 8.5f, 17, 14.5f); p.Stroke();
                    Dot(12, 16.5f, 1.4f);
                    break;
                case "infantry":
                    // helmet + shoulders + spear
                    p.BeginPath(); Arc(11, 9, 4.2f, 0, 180); L(6.8f, 11); L(15.2f, 11); p.ClosePath(); p.Fill();
                    p.BeginPath(); Poly(8.5f, 12.5f, 13.5f, 12.5f); p.Stroke();
                    p.BeginPath(); M(3.5f, 22); p.BezierCurveTo(P(3.5f, 16), P(7, 14.5f), P(11, 14.5f)); p.BezierCurveTo(P(15, 14.5f), P(18.5f, 16), P(18.5f, 22)); p.Stroke();
                    p.BeginPath(); Poly(20.5f, 21.5f, 20.5f, 3.5f); Poly(19, 6, 20.5f, 2, 22, 6); p.Stroke();
                    break;
                case "archer":
                    p.BeginPath(); Arc(6, 12, 9, -60, 60); p.Stroke();
                    p.BeginPath(); Poly(10.5f, 4.2f, 10.5f, 19.8f); p.Stroke();
                    p.BeginPath(); Poly(4, 12, 21, 12); Poly(17.5f, 9, 21, 12, 17.5f, 15); p.Stroke();
                    break;
                case "beast":
                    p.BeginPath(); Poly(3, 17, 6, 10, 11, 8, 15, 4, 17, 8, 21, 9, 19, 12, 16, 12, 15, 17); p.Stroke();
                    p.BeginPath(); Poly(7, 17, 7, 21); Poly(13, 17, 13, 21); p.Stroke();
                    Dot(17.5f, 7.5f, 0.9f);
                    break;
                case "cavalry":
                    p.BeginPath(); Poly(3, 15, 6, 10, 14, 10, 17, 6, 21, 7, 19, 10, 18, 15); p.Stroke();
                    p.BeginPath(); Poly(6, 15, 5, 21); Poly(16, 15, 17, 21); Poly(10, 10, 10, 4, 12, 2.5f); p.Stroke();
                    break;
                default:
                    p.BeginPath(); RectS(5, 5, 14, 14); p.Stroke();
                    break;
            }
        }
    }
}
