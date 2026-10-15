using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassEngine.UiPrototype
{
    /// <summary>Procedural height field (sum of Gaussian hills) with cached grids and contour segments.</summary>
    public sealed class TerrainField
    {
        public static readonly float[] Levels = { 0.18f, 0.3f, 0.42f, 0.54f, 0.66f, 0.78f, 0.9f, 1.02f, 1.14f };
        readonly Vector4[] hills;
        readonly Dictionary<long, float[]> grids = new Dictionary<long, float[]>();
        readonly Dictionary<long, List<Vector2>[]> contours = new Dictionary<long, List<Vector2>[]>();

        public TerrainField(int seed)
        {
            var r = new System.Random(seed);
            hills = new Vector4[9];
            for (int i = 0; i < hills.Length; i++)
                hills[i] = new Vector4((float)r.NextDouble() * 1.2f - 0.1f, (float)r.NextDouble() * 1.2f - 0.1f,
                    0.09f + 0.2f * (float)r.NextDouble(), 0.3f + 0.7f * (float)r.NextDouble());
        }

        public float Height(float u, float v)
        {
            float h = 0f;
            for (int i = 0; i < hills.Length; i++)
            {
                var k = hills[i];
                float dx = u - k.x, dy = v - k.y;
                h += k.w * Mathf.Exp(-(dx * dx + dy * dy) / (k.z * k.z));
            }
            return h;
        }

        public float[] Grid(int nx, int ny)
        {
            long key = nx * 100000L + ny;
            if (grids.TryGetValue(key, out var g)) return g;
            g = new float[(nx + 1) * (ny + 1)];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                    g[j * (nx + 1) + i] = Height(i / (float)nx, j / (float)ny);
            grids[key] = g;
            return g;
        }

        /// <summary>Marching-squares segments per level, as point pairs in 0..1 space.</summary>
        public List<Vector2>[] Contours(int nx, int ny)
        {
            long key = nx * 100000L + ny;
            if (contours.TryGetValue(key, out var res)) return res;
            var g = Grid(nx, ny);
            res = new List<Vector2>[Levels.Length];
            var tmp = new Vector2[4];
            for (int l = 0; l < Levels.Length; l++)
            {
                float L = Levels[l];
                var segs = new List<Vector2>();
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float a = g[j * (nx + 1) + i], b = g[j * (nx + 1) + i + 1];
                        float c = g[(j + 1) * (nx + 1) + i + 1], d = g[(j + 1) * (nx + 1) + i];
                        int n = 0;
                        if ((a < L) != (b < L)) tmp[n++] = new Vector2((i + (L - a) / (b - a)) / nx, j / (float)ny);
                        if ((b < L) != (c < L)) tmp[n++] = new Vector2((i + 1) / (float)nx, (j + (L - b) / (c - b)) / ny);
                        if ((c < L) != (d < L)) tmp[n++] = new Vector2((i + 1 - (L - c) / (d - c)) / nx, (j + 1) / (float)ny);
                        if ((d < L) != (a < L)) tmp[n++] = new Vector2(i / (float)nx, (j + 1 - (L - d) / (a - d)) / ny);
                        if (n >= 2) { segs.Add(tmp[0]); segs.Add(tmp[1]); }
                        if (n == 4) { segs.Add(tmp[2]); segs.Add(tmp[3]); }
                    }
                res[l] = segs;
            }
            contours[key] = res;
            return res;
        }

        public static void DrawRelief(TacDraw.Quads q, TerrainField f, Rect r, int nx, int ny, Color low, Color high, float alpha)
        {
            var g = f.Grid(nx, ny);
            float cw = r.width / nx, ch = r.height / ny;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float h = g[j * (nx + 1) + i];
                    float hl = g[j * (nx + 1) + Mathf.Max(0, i - 1)] - g[Mathf.Max(0, j - 1) * (nx + 1) + i];
                    float t = Mathf.Clamp01(h / 1.3f);
                    var c = Color.Lerp(low, high, t);
                    float shade = TacTheme.Toy ? Mathf.Clamp(hl * 3.5f, -0.18f, 0.18f) : Mathf.Clamp(hl * 6f, -0.35f, 0.35f);
                    c = new Color(c.r * (1f - shade), c.g * (1f - shade), c.b * (1f - shade), alpha);
                    q.Add(new Rect(r.x + i * cw, r.y + j * ch, cw + 0.5f, ch + 0.5f), c);
                }
        }

        /// <summary>Smooth (per-vertex coloured) relief used by the Q 版 skin: no visible cells.</summary>
        public static void DrawReliefSmooth(MeshGenerationContext mgc, TerrainField f, Rect r, int nx, int ny, Color low, Color high)
        {
            var g = f.Grid(nx, ny);
            int vx = nx + 1, vy = ny + 1;
            var verts = new Vertex[vx * vy];
            var idx = new ushort[nx * ny * 6];
            for (int j = 0; j < vy; j++)
                for (int i = 0; i < vx; i++)
                {
                    float h = g[j * vx + i];
                    float hl = g[j * vx + Mathf.Max(0, i - 1)] - g[Mathf.Max(0, j - 1) * vx + i];
                    var c = Color.Lerp(low, high, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(h / 1.3f)));
                    float shade = Mathf.Clamp(hl * 3f, -0.1f, 0.1f);
                    c = new Color(c.r * (1f - shade), c.g * (1f - shade), c.b * (1f - shade), 1f);
                    verts[j * vx + i] = new Vertex { position = new Vector3(r.x + r.width * i / nx, r.y + r.height * j / ny, Vertex.nearZ), tint = c };
                }
            int k = 0;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    ushort a = (ushort)(j * vx + i), b = (ushort)(a + 1), c2 = (ushort)(a + vx), d = (ushort)(c2 + 1);
                    idx[k++] = a; idx[k++] = b; idx[k++] = d;
                    idx[k++] = a; idx[k++] = d; idx[k++] = c2;
                }
            var mwd = mgc.Allocate(verts.Length, idx.Length);
            mwd.SetAllVertices(verts);
            mwd.SetAllIndices(idx);
        }

        public static void DrawContours(Painter2D p, TerrainField f, Rect r, int nx, int ny, Color minor, Color major)
        {
            var levels = f.Contours(nx, ny);
            p.lineWidth = 1f;
            for (int l = 0; l < levels.Length; l++)
            {
                var segs = levels[l];
                if (segs.Count == 0) continue;
                p.strokeColor = l % 2 == 1 ? major : minor;
                p.BeginPath();
                for (int s = 0; s + 1 < segs.Count; s += 2)
                {
                    p.MoveTo(new Vector2(r.x + segs[s].x * r.width, r.y + segs[s].y * r.height));
                    p.LineTo(new Vector2(r.x + segs[s + 1].x * r.width, r.y + segs[s + 1].y * r.height));
                }
                p.Stroke();
            }
        }
    }

    public struct MapArmy
    {
        public Rect rect;      // 0..1 map space
        public Color color;
        public float alive;    // 0..1, thins the dot field
        public bool selected;
        public int side;
    }

    /// <summary>Tactical map: "preview" (operation card), "battle" (full-screen stand-in), "minimap".</summary>
    [UxmlElement]
    public partial class TacMapView : VisualElement
    {
        int m_Seed = 7;
        TerrainField field;
        readonly TacDraw.Quads relief = new TacDraw.Quads();
        readonly TacDraw.Quads dots = new TacDraw.Quads();

        [UxmlAttribute] public string mode { get; set; } = "preview";
        [UxmlAttribute] public int seed { get => m_Seed; set { m_Seed = value; field = null; MarkDirtyRepaint(); } }
        public readonly List<MapArmy> armies = new List<MapArmy>();
        public Rect cameraView = new Rect(0.32f, 0.3f, 0.36f, 0.32f);
        public bool showZones;
        public float time;
        public event Action<Vector2> clicked;

        public TacMapView()
        {
            generateVisualContent += OnGenerate;
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (layout.width < 1 || layout.height < 1) return;
                clicked?.Invoke(new Vector2(e.localPosition.x / layout.width, e.localPosition.y / layout.height));
            });
        }

        public void Refresh() => MarkDirtyRepaint();

        void OnGenerate(MeshGenerationContext mgc)
        {
            float w = layout.width, h = layout.height;
            if (w < 4 || h < 4) return;
            field ??= new TerrainField(m_Seed);
            var p = mgc.painter2D;
            var P = TacTheme.Map;
            bool battle = mode == "battle", mini = mode == "minimap";
            var R = new Rect(0, 0, w, h);

            relief.Clear();
            int nx = battle ? 128 : 72, ny = Mathf.Max(8, Mathf.RoundToInt(nx * h / w));
            var low = battle ? P.battleLow : P.reliefLow;
            var high = battle ? P.battleHigh : P.reliefHigh;
            if (TacTheme.Toy) TerrainField.DrawReliefSmooth(mgc, field, R, nx, ny, low, high);
            else { TerrainField.DrawRelief(relief, field, R, nx, ny, low, high, 1f); relief.Flush(mgc); }

            // grid
            p.lineWidth = 1f;
            int div = mini ? 4 : 8;
            p.strokeColor = TacDraw.A(P.grid, P.grid.a * (battle ? 0.8f : 1.1f));
            p.BeginPath();
            for (int i = 1; i < div; i++)
            {
                float x = Mathf.Round(w * i / div) + 0.5f, y = Mathf.Round(h * i / div) + 0.5f;
                p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, h));
                p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(w, y));
            }
            p.Stroke();

            int cx = battle ? 96 : 56, cy = Mathf.Max(8, Mathf.RoundToInt(cx * h / w));
            TerrainField.DrawContours(p, field, R, cx, cy,
                P.contour, P.contourMajor);

            if (showZones)
            {
                ZoneRect(p, new Rect(0.04f, 0.08f, 0.32f, 0.84f), w, h, P.atk);
                ZoneRect(p, new Rect(0.64f, 0.08f, 0.32f, 0.84f), w, h, P.def);
            }

            // armies
            dots.Clear();
            float sp = mini ? 3f : battle ? 5f : 4f;
            foreach (var a in armies)
            {
                var r = new Rect(a.rect.x * w, a.rect.y * h, a.rect.width * w, a.rect.height * h);
                int nxD = Mathf.Max(1, Mathf.FloorToInt(r.width / sp)), nyD = Mathf.Max(1, Mathf.FloorToInt(r.height / sp));
                var c = (Color32)TacDraw.A(a.color, 0.95f);
                float ds = (mini ? 2f : battle ? 2.2f : 2f) * (TacTheme.Toy ? 1.3f : 1f);
                int seedA = (int)(a.rect.x * 1000) + a.side * 7919;
                for (int j = 0; j < nyD; j++)
                    for (int i = 0; i < nxD; i++)
                    {
                        float hsh = TacDraw.Hash(i + seedA, j);
                        if (hsh > a.alive) continue;
                        float jx = battle ? Mathf.Sin(time * 2.1f + hsh * 40f) * 0.8f : 0f;
                        float jy = battle ? Mathf.Cos(time * 1.7f + hsh * 30f) * 0.8f : 0f;
                        dots.Add(new Rect(r.x + i * sp + jx, r.y + j * sp + jy, ds, ds), c);
                    }
                if (dots.Full) break;
            }
            dots.Flush(mgc);

            foreach (var a in armies)
            {
                var r = new Rect(a.rect.x * w, a.rect.y * h, a.rect.width * w, a.rect.height * h);
                p.lineWidth = TacTheme.Toy ? 1.5f : 1f;
                p.strokeColor = TacDraw.A(a.color, a.selected ? 0.9f : 0.35f);
                p.BeginPath();
                p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath();
                p.Stroke();
                if (a.selected)
                {
                    var g = r; g.xMin -= 6; g.yMin -= 6; g.xMax += 6; g.yMax += 6;
                    float L = mini ? 5f : 10f;
                    p.strokeColor = P.selMark;
                    p.lineWidth = (mini ? 1.5f : 2f) * P.lineScale;
                    p.BeginPath();
                    p.MoveTo(new Vector2(g.xMin, g.yMin + L)); p.LineTo(g.min); p.LineTo(new Vector2(g.xMin + L, g.yMin));
                    p.MoveTo(new Vector2(g.xMax - L, g.yMin)); p.LineTo(new Vector2(g.xMax, g.yMin)); p.LineTo(new Vector2(g.xMax, g.yMin + L));
                    p.MoveTo(new Vector2(g.xMax, g.yMax - L)); p.LineTo(g.max); p.LineTo(new Vector2(g.xMax - L, g.yMax));
                    p.MoveTo(new Vector2(g.xMin + L, g.yMax)); p.LineTo(new Vector2(g.xMin, g.yMax)); p.LineTo(new Vector2(g.xMin, g.yMax - L));
                    p.Stroke();
                }
            }

            if (mini)
            {
                var c = cameraView;
                var a0 = new Vector2((c.x + c.width * 0.2f) * w, c.y * h);
                var a1 = new Vector2((c.xMax - c.width * 0.2f) * w, c.y * h);
                var a2 = new Vector2(c.xMax * w, c.yMax * h);
                var a3 = new Vector2(c.x * w, c.yMax * h);
                p.fillColor = P.camFill;
                p.BeginPath(); p.MoveTo(a0); p.LineTo(a1); p.LineTo(a2); p.LineTo(a3); p.ClosePath(); p.Fill();
                p.strokeColor = P.camLine; p.lineWidth = 1.2f * P.lineScale;
                p.BeginPath(); p.MoveTo(a0); p.LineTo(a1); p.LineTo(a2); p.LineTo(a3); p.ClosePath(); p.Stroke();
                if (!P.sweep) return;
                // sweep
                float ang = time * 0.8f;
                var ctr = new Vector2(w * 0.5f, h * 0.5f);
                p.strokeColor = new Color(0.24f, 0.8f, 0.96f, 0.25f); p.lineWidth = 1f;
                p.BeginPath(); p.MoveTo(ctr); p.LineTo(ctr + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Mathf.Max(w, h)); p.Stroke();
            }
        }

        static void ZoneRect(Painter2D p, Rect n, float w, float h, Color c)
        {
            var r = new Rect(n.x * w, n.y * h, n.width * w, n.height * h);
            var P = TacTheme.Map;
            p.fillColor = TacDraw.A(c, P.zoneFill);
            p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); p.Fill();
            p.strokeColor = TacDraw.A(c, P.zoneLine); p.lineWidth = P.lineScale;
            TacDraw.DashedRect(p, r, 6f, 4f);
        }
    }

    public sealed class DeployComp
    {
        public string unit, name;
        public int side;              // 0 attacker, 1 defender
        public Color color;
        public int count;
        public float density;         // units per m²
        public float aspect;          // frontage / depth
        public int facing;            // 0 E, 1 S, 2 W, 3 N
        public Vector2 center, size;  // metres
        public string reason;         // null = valid
        public bool Invalid => reason != null;
        public Rect Rect => new Rect(center - size * 0.5f, size);
        public DeployComp Clone() => (DeployComp)MemberwiseClone();

        public void Resize()
        {
            float area = count / Mathf.Max(0.01f, density);
            float depth = Mathf.Sqrt(area / Mathf.Max(0.05f, aspect));
            float front = depth * aspect;
            size = facing % 2 == 0 ? new Vector2(depth, front) : new Vector2(front, depth);
        }

        public void AdoptSize(Vector2 s)
        {
            size = new Vector2(Mathf.Max(6f, s.x), Mathf.Max(6f, s.y));
            density = count / (size.x * size.y);
            float front = facing % 2 == 0 ? size.y : size.x, depth = facing % 2 == 0 ? size.x : size.y;
            aspect = front / depth;
        }
    }

    /// <summary>Interactive deployment map: drag to move, corner handles to resize, click to rotate (tool 2).</summary>
    [UxmlElement]
    public partial class TacDeployMap : VisualElement
    {
        public const float World = 1024f;
        public struct Blocked { public string name; public bool ellipse; public Rect r; }

        public readonly List<DeployComp> comps = new List<DeployComp>();
        public readonly Rect[] zones = { new Rect(32, 64, 380, 896), new Rect(612, 64, 380, 896) };
        public readonly List<Blocked> blocked = new List<Blocked>
        {
            new Blocked { name = "湖泊", ellipse = true, r = new Rect(170, 640, 230, 170) },
            new Blocked { name = "陡坡", r = new Rect(110, 150, 110, 190) },
            new Blocked { name = "乱石", ellipse = true, r = new Rect(700, 360, 150, 130) },
            new Blocked { name = "峡谷", r = new Rect(470, 0, 84, 300) },
        };
        public int selected = -1;
        public bool snap = true;
        public int tool;
        public event Action changed, committed;
        public event Action<int> selectionChanged;
        public event Action<Vector2> hover;

        readonly TerrainField terrain = new TerrainField(23);
        readonly TacDraw.Quads relief = new TacDraw.Quads();
        readonly TacDraw.Quads dots = new TacDraw.Quads();
        readonly List<Label> compLabels = new List<Label>();
        readonly List<Label> staticLabels = new List<Label>();
        readonly Label dimLabel;
        int drag, dragCorner;
        Vector2 dragOffset;
        bool moved;
        float pulse;

        public TacDeployMap()
        {
            generateVisualContent += OnGenerate;
            dimLabel = new Label { pickingMode = PickingMode.Ignore };
            dimLabel.AddToClassList("map-label"); dimLabel.AddToClassList("map-label--dim");
            Add(dimLabel);
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<GeometryChangedEvent>(_ => Relayout());
            schedule.Execute(() =>
            {
                pulse += 0.12f;
                foreach (var c in comps) if (c.Invalid) { MarkDirtyRepaint(); break; }
            }).Every(50);
        }

        Rect MapRect { get { float w = layout.width, h = layout.height, s = Mathf.Max(10f, Mathf.Min(w, h) - 36f); return new Rect((w - s) * 0.5f, (h - s) * 0.5f + 6f, s, s); } }
        float Scale => MapRect.width / World;
        Vector2 W2S(Vector2 v) { var r = MapRect; return new Vector2(r.x + v.x * Scale, r.y + v.y * Scale); }
        Vector2 S2W(Vector2 v) { var r = MapRect; return new Vector2((v.x - r.x) / Scale, (v.y - r.y) / Scale); }
        Rect W2S(Rect r) => new Rect(W2S(r.min), r.size * Scale);
        static Vector2 Corner(Rect r, int k) => k == 0 ? r.min : k == 1 ? new Vector2(r.xMax, r.yMin) : k == 2 ? r.max : new Vector2(r.xMin, r.yMax);

        public void Select(int i)
        {
            if (selected == i) return;
            selected = i;
            selectionChanged?.Invoke(i);
            Refresh();
        }

        public void Refresh() { Relayout(); MarkDirtyRepaint(); }

        public void Validate()
        {
            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i]; var r = c.Rect;
                c.reason = null;
                var z = zones[c.side];
                if (r.xMin < z.xMin || r.yMin < z.yMin || r.xMax > z.xMax || r.yMax > z.yMax)
                    c.reason = "超出" + (c.side == 0 ? "攻方" : "守方") + "部署区";
                else
                    foreach (var b in blocked)
                        if (Overlaps(b, r)) { c.reason = "完整部署脚印覆盖禁行地形（" + b.name + "）"; break; }
                if (c.reason == null)
                    for (int j = 0; j < comps.Count; j++)
                        if (j != i && comps[j].Rect.Overlaps(r)) { c.reason = "与编成 " + (j + 1) + " 重叠"; break; }
            }
        }

        static bool Overlaps(Blocked b, Rect r)
        {
            if (!b.r.Overlaps(r)) return false;
            if (!b.ellipse) return true;
            var c = b.r.center; var e = b.r.size * 0.5f;
            if (r.Contains(c)) return true;
            for (int i = 0; i <= 10; i++)
                for (int j = 0; j <= 10; j++)
                {
                    var q = new Vector2(Mathf.Lerp(r.xMin, r.xMax, i / 10f), Mathf.Lerp(r.yMin, r.yMax, j / 10f)) - c;
                    if (q.x * q.x / (e.x * e.x) + q.y * q.y / (e.y * e.y) <= 1f) return true;
                }
            return false;
        }

        public void Rotate(int i)
        {
            var c = comps[i];
            c.facing = (c.facing + 1) % 4;
            c.size = new Vector2(c.size.y, c.size.x);
            Validate(); changed?.Invoke(); Refresh();
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            var sp = (Vector2)e.localPosition; var wp = S2W(sp);
            if (selected >= 0 && selected < comps.Count)
            {
                var r = W2S(comps[selected].Rect);
                for (int k = 0; k < 4; k++)
                    if ((sp - Corner(r, k)).sqrMagnitude < 110f) { Begin(2, e.pointerId); dragCorner = k; return; }
            }
            for (int i = comps.Count - 1; i >= 0; i--)
            {
                if (!comps[i].Rect.Contains(wp)) continue;
                Select(i);
                if (tool == 2) { Rotate(i); committed?.Invoke(); return; }
                if (tool == 1)
                {
                    var r = comps[i].Rect; float best = float.MaxValue;
                    for (int k = 0; k < 4; k++) { float d = (Corner(r, k) - wp).sqrMagnitude; if (d < best) { best = d; dragCorner = k; } }
                    Begin(2, e.pointerId);
                }
                else { dragOffset = comps[i].center - wp; Begin(1, e.pointerId); }
                return;
            }
            Select(-1);
        }

        void Begin(int mode, int pointerId) { drag = mode; moved = false; this.CapturePointer(pointerId); }

        void OnMove(PointerMoveEvent e)
        {
            var wp = S2W(e.localPosition);
            hover?.Invoke(wp);
            if (drag == 0 || selected < 0) return;
            var c = comps[selected];
            if (drag == 1)
            {
                var ctr = wp + dragOffset;
                if (snap) ctr = new Vector2(Mathf.Round(ctr.x / 8f) * 8f, Mathf.Round(ctr.y / 8f) * 8f);
                c.center = new Vector2(Mathf.Clamp(ctr.x, 0, World), Mathf.Clamp(ctr.y, 0, World));
            }
            else
            {
                var opp = Corner(c.Rect, (dragCorner + 2) % 4);
                var q = wp;
                if (snap) q = new Vector2(Mathf.Round(q.x / 4f) * 4f, Mathf.Round(q.y / 4f) * 4f);
                var mn = Vector2.Min(opp, q); var mx = Vector2.Max(opp, q);
                c.center = (mn + mx) * 0.5f;
                c.AdoptSize(mx - mn);
                var nr = c.Rect;
                for (int k = 0; k < 4; k++) if ((Corner(nr, k) - opp).sqrMagnitude < 0.01f) dragCorner = (k + 2) % 4;
            }
            moved = true;
            Validate(); changed?.Invoke(); Refresh();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            this.ReleasePointer(e.pointerId);
            if (drag != 0 && moved) committed?.Invoke();
            drag = 0;
        }

        void Relayout()
        {
            if (layout.width < 4) return;
            if (staticLabels.Count == 0)
            {
                AddStatic("攻方部署区", "map-label--zone");
                AddStatic("守方部署区", "map-label--zone");
                foreach (var b in blocked) AddStatic(b.name, "map-label--zone");
            }
            Place(staticLabels[0], W2S(new Vector2(zones[0].center.x, zones[0].yMin - 16)));
            Place(staticLabels[1], W2S(new Vector2(zones[1].center.x, zones[1].yMin - 16)));
            for (int i = 0; i < blocked.Count; i++) Place(staticLabels[2 + i], W2S(blocked[i].r.center));
            while (compLabels.Count < comps.Count)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList("map-label");
                Insert(0, l);
                compLabels.Add(l);
            }
            for (int i = 0; i < compLabels.Count; i++)
            {
                bool on = i < comps.Count;
                compLabels[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                compLabels[i].text = (i + 1).ToString();
                Place(compLabels[i], W2S(comps[i].center));
            }
            if (selected >= 0 && selected < comps.Count)
            {
                var c = comps[selected]; var r = W2S(c.Rect);
                dimLabel.style.display = DisplayStyle.Flex;
                dimLabel.text = $"{c.size.x:0} × {c.size.y:0} m · {c.density:0.00}/m²";
                dimLabel.style.left = r.center.x;
                dimLabel.style.top = r.yMin - 26f;
                dimLabel.BringToFront();
            }
            else dimLabel.style.display = DisplayStyle.None;
        }

        void AddStatic(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("map-label"); l.AddToClassList(cls);
            Insert(0, l);
            staticLabels.Add(l);
        }

        static void Place(VisualElement l, Vector2 p) { l.style.left = p.x; l.style.top = p.y; }

        void OnGenerate(MeshGenerationContext mgc)
        {
            if (layout.width < 20 || layout.height < 20) return;
            var p = mgc.painter2D;
            var R = MapRect; float S = Scale;
            var P = TacTheme.Map;

            relief.Clear();
            if (TacTheme.Toy) TerrainField.DrawReliefSmooth(mgc, terrain, R, 80, 80, P.reliefLow, P.reliefHigh);
            else { TerrainField.DrawRelief(relief, terrain, R, 80, 80, P.reliefLow, P.reliefHigh, 1f); relief.Flush(mgc); }

            p.lineWidth = 1f;
            p.strokeColor = P.grid;
            p.BeginPath();
            for (float m = 64; m < World; m += 64)
            {
                var a = W2S(new Vector2(m, 0)); var b = W2S(new Vector2(m, World));
                p.MoveTo(new Vector2(Mathf.Round(a.x) + 0.5f, a.y)); p.LineTo(new Vector2(Mathf.Round(b.x) + 0.5f, b.y));
                a = W2S(new Vector2(0, m)); b = W2S(new Vector2(World, m));
                p.MoveTo(new Vector2(a.x, Mathf.Round(a.y) + 0.5f)); p.LineTo(new Vector2(b.x, Mathf.Round(b.y) + 0.5f));
            }
            p.Stroke();
            p.strokeColor = P.gridMajor;
            p.BeginPath();
            for (float m = 256; m < World; m += 256)
            {
                var a = W2S(new Vector2(m, 0)); var b = W2S(new Vector2(m, World));
                p.MoveTo(a); p.LineTo(b);
                a = W2S(new Vector2(0, m)); b = W2S(new Vector2(World, m));
                p.MoveTo(a); p.LineTo(b);
            }
            p.Stroke();
            TerrainField.DrawContours(p, terrain, R, 64, 64, P.contour, P.contourMajor);

            // deployment zones
            var zc = new[] { P.atk, P.def };
            for (int z = 0; z < 2; z++)
            {
                var r = W2S(zones[z]);
                p.fillColor = TacDraw.A(zc[z], P.zoneFill);
                FillRect(p, r);
                p.strokeColor = TacDraw.A(zc[z], P.zoneLine); p.lineWidth = P.lineScale;
                TacDraw.DashedRect(p, r, 7f, 5f);
            }

            // blocked terrain
            var dz = P.invalid; var bk = P.blocked;
            foreach (var b in blocked)
            {
                var r = W2S(b.r);
                p.fillColor = TacDraw.A(bk, P.blockedFill);
                if (b.ellipse) { Ellipse(p, r); p.Fill(); } else FillRect(p, r);
                p.strokeColor = TacDraw.A(bk, P.blockedHatch); p.lineWidth = 1f;
                p.BeginPath(); Hatch(p, r, b.ellipse, 9f); p.Stroke();
                p.strokeColor = TacDraw.A(bk, P.blockedLine); p.lineWidth = 1.2f * P.lineScale;
                if (b.ellipse) { Ellipse(p, r); p.Stroke(); } else StrokeRect(p, r);
            }

            // compositions
            dots.Clear();
            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i]; var r = W2S(c.Rect);
                float sp = P.dotSpacing;
                int nx = Mathf.Max(1, Mathf.FloorToInt((r.width - 2) / sp)), ny = Mathf.Max(1, Mathf.FloorToInt((r.height - 2) / sp));
                float fillRatio = Mathf.Clamp01(c.count / (nx * (float)ny));
                var dc = (Color32)TacDraw.A(Color.Lerp(c.color, Color.white, P.dotLighten), 0.9f);
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                        if (TacDraw.Hash(x + i * 131, y) <= Mathf.Max(0.15f, fillRatio))
                            dots.Add(new Rect(r.x + 2 + x * sp, r.y + 2 + y * sp, P.dotSize, P.dotSize), dc);
            }
            dots.Flush(mgc);

            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i]; var r = W2S(c.Rect);
                bool sel = i == selected;
                p.fillColor = TacDraw.A(c.Invalid && TacTheme.Toy ? dz : c.color, sel ? P.compFillSel : P.compFill);
                FillRect(p, r);
                if (c.Invalid)
                {
                    float a = 0.55f + 0.45f * Mathf.Sin(pulse);
                    for (int g = 3; g >= 1; g--)
                    {
                        p.strokeColor = TacDraw.A(dz, 0.12f * a * (4 - g)); p.lineWidth = 1 + g * 3f;
                        StrokeRect(p, r);
                    }
                    p.strokeColor = TacDraw.A(dz, 0.6f + 0.4f * a); p.lineWidth = 2f;
                    StrokeRect(p, r);
                }
                else
                {
                    p.strokeColor = TacDraw.A(TacTheme.Toy ? Color.Lerp(c.color, Color.black, 0.25f) : c.color, sel ? 1f : 0.75f);
                    p.lineWidth = (sel ? 1.6f : 1.2f) * P.lineScale;
                    StrokeRect(p, r);
                }
                // facing arrow
                var dir = c.facing == 0 ? Vector2.right : c.facing == 1 ? Vector2.up : c.facing == 2 ? Vector2.left : Vector2.down;
                var edge = r.center + new Vector2(dir.x * r.width * 0.5f, dir.y * r.height * 0.5f);
                var nrm = new Vector2(-dir.y, dir.x);
                p.fillColor = c.Invalid ? dz : c.color;
                p.BeginPath();
                p.MoveTo(edge + dir * 9f); p.LineTo(edge + nrm * 6f + dir * 1f); p.LineTo(edge - nrm * 6f + dir * 1f); p.ClosePath();
                p.Fill();

                if (sel)
                {
                    var o = r; o.xMin -= 4; o.yMin -= 4; o.xMax += 4; o.yMax += 4;
                    p.strokeColor = P.selDash; p.lineWidth = P.lineScale;
                    TacDraw.DashedRect(p, o, 4f, 3f);
                    for (int k = 0; k < 4; k++)
                    {
                        var q = Corner(r, k);
                        var hr = new Rect(q.x - 4.5f, q.y - 4.5f, 9f, 9f);
                        p.fillColor = P.handleFill; FillRect(p, hr);
                        p.strokeColor = P.handleLine; p.lineWidth = P.lineScale; StrokeRect(p, hr);
                    }
                }
            }

            // map border + edge ticks
            p.strokeColor = P.border; p.lineWidth = P.lineScale;
            StrokeRect(p, R);
            p.strokeColor = P.ticks;
            p.BeginPath();
            for (float m = 0; m <= World; m += 128)
            {
                float L = m % 512 == 0 ? 8f : 4f;
                var a = W2S(new Vector2(m, 0)); p.MoveTo(a); p.LineTo(a + new Vector2(0, -L));
                a = W2S(new Vector2(0, m)); p.MoveTo(a); p.LineTo(a + new Vector2(-L, 0));
            }
            p.Stroke();
        }

        static void FillRect(Painter2D p, Rect r)
        {
            p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); p.Fill();
        }

        static void StrokeRect(Painter2D p, Rect r)
        {
            p.BeginPath(); p.MoveTo(r.min); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(r.max); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); p.Stroke();
        }

        static void Ellipse(Painter2D p, Rect r)
        {
            p.BeginPath();
            for (int i = 0; i <= 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f;
                var v = r.center + new Vector2(Mathf.Cos(a) * r.width * 0.5f, Mathf.Sin(a) * r.height * 0.5f);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
        }

        /// <summary>45° hatch clipped to a rect or ellipse (computed in the shape's unit space).</summary>
        static void Hatch(Painter2D p, Rect r, bool ellipse, float spacing)
        {
            float hx = r.width * 0.5f, hy = r.height * 0.5f;
            var c = r.center;
            float step = spacing / Mathf.Max(4f, Mathf.Min(hx, hy));
            for (float k = -2f; k <= 2f; k += step)
            {
                float x0, x1;
                if (ellipse)
                {
                    float disc = 2f - k * k;
                    if (disc <= 0) continue;
                    float sq = Mathf.Sqrt(disc);
                    x0 = (-k - sq) * 0.5f; x1 = (-k + sq) * 0.5f;
                }
                else
                {
                    x0 = Mathf.Max(-1f, -1f - k); x1 = Mathf.Min(1f, 1f - k);
                    if (x1 <= x0) continue;
                }
                p.MoveTo(c + new Vector2(x0 * hx, -(x0 + k) * hy));
                p.LineTo(c + new Vector2(x1 * hx, -(x1 + k) * hy));
            }
        }
    }
}
