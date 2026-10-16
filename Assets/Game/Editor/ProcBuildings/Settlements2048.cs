using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-15 procedural settlements on the 2048 maps (user-approved 档1: decoration only — buildings are NOT written
    /// into the navigation / obstacle masks, the height field is not touched, combat is unchanged). Per map half: one
    /// town (plaza, church, inn, market, 2–3 rows of houses along a main and a cross street), one fort (wall ring, corner
    /// towers, gatehouse, keep, tents), two villages (bent main street, farms with fences, windmill) and two ruin fields;
    /// the other half is the 180° mirror (each mirrored piece re-validated). Sites avoid deployment zones, the open core,
    /// keeps, routes (trail mask), forest / rock masks and steep ground; buildings sit on a stone plinth that reaches
    /// 2.5 m below the floor, so gentle slopes need no terrain edits. Streets / plazas / yards are painted as dirt.
    /// </summary>
    public abstract partial class Proc2048MapBase
    {
        public enum SiteKind { Town, Village, Fort, Ruins }

        public sealed class TownSite { public SiteKind kind; public Vector2 c; public float r; public bool mirror; public float scale = 1f; }
        public sealed class TownLot
        {
            public BK kind; public int v; public Vector2 pos; public float yaw; public Vector2 size; public bool prop; public int group = -1; public bool structure;
            /// <summary>2026-10-16 castle A: mesh scale (size already scaled); tall = extra vertical stretch.</summary>
            public float scale = 1f, tall = 1f;
            public SiteKind site;
            public Vector2 Right => new Vector2(Mathf.Cos(yaw * Mathf.Deg2Rad), -Mathf.Sin(yaw * Mathf.Deg2Rad));
            public Vector2 Fwd => new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
        }
        public sealed class TownStreet { public Vector2 a, b; public float half; }
        public sealed class TownTree { public Vector2 p; public string cat; public SiteKind site; }
        public sealed class TownPlan
        {
            public readonly List<TownSite> sites = new List<TownSite>();
            public readonly List<TownLot> lots = new List<TownLot>();
            public readonly List<TownStreet> streets = new List<TownStreet>();
            public readonly List<(Vector2 c, float r)> plazas = new List<(Vector2, float)>();
            public readonly List<TownTree> trees = new List<TownTree>();
            public readonly List<(Vector2 c, Vector2 u, float half)> yards = new List<(Vector2, Vector2, float)>();
            public float[] dirt; public bool[] occupied;
            public string log = "";
        }

        TownPlan towns;
        System.Random townRng;
        public TownPlan Towns { get { Ensure(); if (towns == null) towns = PlanTowns(); return towns; } }
        bool HasTowns => Id != "test-plain-2048";

        // ------------------------------------------------------------------ ground rules
        const int TG = 256; const float TGS = 8f;   // site scoring grid (8 m)
        bool[] siteOk;

        float TSample(float[] g, float x, float z) => Field2048.Sample(g, x, z);
        float TSlope(float x, float z) => field.SlopeDegrees(CellOf(x), CellOf(z));

        /// <summary>Ground where a settlement piece may stand (strict = site scoring).</summary>
        bool TownGround(float x, float z, bool strict)
        {
            if (EdgeDistance2(x, z) > Size.EdgeBlock - (strict ? 40f : 25f)) return false;
            float lim = strict ? .25f : .4f;
            if (TSample(field.Forest, x, z) > lim || TSample(field.Rock, x, z) > lim) return false;
            if (InZone(x, z, strict ? 45f : 28f)) return false;
            if (CoreWeight(x, z) > (strict ? .02f : .06f)) return false;
            var p = new Vector2(x, z);
            foreach (var k in keeps) if ((p - k).sqrMagnitude < (strict ? 50f * 50f : 38f * 38f)) return false;
            if (TSample(field.Trail, x, z) > (strict ? .3f : .5f)) return false;
            return TSlope(x, z) <= (strict ? 12f : 17f);
        }

        bool SiteCell(float x, float z)
        {
            int ix = Mathf.FloorToInt((x + Field2048.Half) / TGS), iz = Mathf.FloorToInt((z + Field2048.Half) / TGS);
            if (ix < 0 || iz < 0 || ix >= TG || iz >= TG) return false;
            return siteOk[iz * TG + ix];
        }

        float SiteFraction(Vector2 c, float r, out float relief)
        {
            int ok = 0, n = 0; float hMin = float.MaxValue, hMax = float.MinValue;
            for (int ring = 0; ring <= 4; ring++)
            {
                float rr = r * ring / 4f; int count = ring == 0 ? 1 : 6 * ring + 2;
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2f / count + ring * .4f;
                    float x = c.x + Mathf.Cos(a) * rr, z = c.y + Mathf.Sin(a) * rr;
                    n++; if (SiteCell(x, z)) ok++;
                    float h = TSample(field.H, x, z); hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
                }
            }
            relief = hMax - hMin;
            return ok / (float)n;
        }

        // ------------------------------------------------------------------ planning
        TownPlan PlanTowns()
        {
            var plan = new TownPlan();
            int N2 = 1025;
            plan.dirt = new float[N2 * N2]; plan.occupied = new bool[Field2048.N * Field2048.N];
            if (!HasTowns) return plan;
            var log = new StringBuilder();
            townRng = new System.Random(Seed * 31 + 9001);
            siteOk = new bool[TG * TG];
            for (int iz = 0; iz < TG; iz++)
                for (int ix = 0; ix < TG; ix++)
                    siteOk[iz * TG + ix] = TownGround(-Field2048.Half + (ix + .5f) * TGS, -Field2048.Half + (iz + .5f) * TGS, true);

            var specs = new (SiteKind kind, float r, float minFrac)[]
            {
                (SiteKind.Town, 100f, .74f), (SiteKind.Fort, 46f * FortScale, .82f), (SiteKind.Village, 72f, .68f), (SiteKind.Village, 66f, .66f),
                (SiteKind.Ruins, 40f, .55f), (SiteKind.Ruins, 34f, .55f),
            };
            bool PlaceSite((SiteKind kind, float r, float minFrac) s, float scale)
            {
                Vector2 best = default; float bestScore = float.MinValue, bestFrac = 0f;
                for (float z = -940f; z <= 940f; z += 24f)
                    for (float x = -940f; x <= (Symmetric ? -24f : 940f); x += 24f)
                    {
                        var c = new Vector2(x, z);
                        if (EdgeDistance2(x, z) > Size.EdgeBlock - s.r - 30f) continue;
                        bool far = false;
                        foreach (var o in plan.sites)
                        {
                            float need = s.r + o.r + 70f;
                            if ((o.c - c).sqrMagnitude < need * need || (Symmetric && (-o.c - c).sqrMagnitude < need * need)) { far = true; break; }
                        }
                        if (far || (Symmetric && c.magnitude < s.r + 40f)) continue;
                        float frac = SiteFraction(c, s.r, out float relief);
                        if (frac < s.minFrac || relief > s.r * .3f) continue;
                        float score = frac - relief / (s.r * 1.5f) + (float)townRng.NextDouble() * .12f;
                        switch (s.kind)
                        {
                            case SiteKind.Town: score += x < -600f ? .25f : 0f; break;
                            case SiteKind.Village: score += Mathf.Abs(z) > 430f ? .15f : 0f; break;
                            case SiteKind.Ruins: score += Mathf.Abs(x) < 320f ? .2f : 0f; break;
                            case SiteKind.Fort:
                            {
                                float ringH = 0f; for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; ringH += TSample(field.H, x + Mathf.Cos(a) * s.r * 2.2f, z + Mathf.Sin(a) * s.r * 2.2f); }
                                score += Mathf.Clamp((TSample(field.H, x, z) - ringH / 8f) / 12f, 0f, .3f) + (Mathf.Abs(z) > 420f && Mathf.Abs(x) > 250f ? .1f : 0f);
                                break;
                            }
                        }
                        if (score > bestScore) { bestScore = score; best = c; bestFrac = frac; }
                    }
                if (bestScore == float.MinValue) { log.AppendLine("site " + s.kind + " r" + s.r + ": no ground"); return false; }
                plan.sites.Add(new TownSite { kind = s.kind, c = best, r = s.r, scale = scale });
                log.AppendLine("site " + s.kind + " r" + s.r + " at " + best + " frac " + bestFrac.ToString("F2"));
                return true;
            }
            // the ×2 fort falls back to the original size when no ground fits it
            foreach (var s in specs)
                if (!PlaceSite(s, s.kind == SiteKind.Fort ? FortScale : 1f) && s.kind == SiteKind.Fort) PlaceSite((SiteKind.Fort, 46f, .82f), 1f);

            int half = plan.sites.Count;
            for (int i = 0; i < half; i++)
            {
                var s = plan.sites[i];
                int before = plan.lots.Count;
                switch (s.kind)
                {
                    case SiteKind.Town: LayoutTown(plan, s, i); break;
                    case SiteKind.Village: LayoutVillage(plan, s, i); break;
                    case SiteKind.Fort: LayoutFort(plan, s, i); break;
                    case SiteKind.Ruins: LayoutRuins(plan, s, i); break;
                }
                log.AppendLine("  " + s.kind + " lots " + (plan.lots.Count - before) + " (buildings " + plan.lots.Skip(before).Count(l => !l.prop) + ")");
                if (fortMisses.Length > 0) { log.AppendLine("    放不下：" + fortMisses); fortMisses.Clear(); }
            }
            if (Symmetric)
            {
                var srcLots = plan.lots.ToList(); var srcStreets = plan.streets.ToList(); var srcPlazas = plan.plazas.ToList(); var srcTrees = plan.trees.ToList(); var srcYards = plan.yards.ToList();
                for (int i = 0; i < half; i++) plan.sites.Add(new TownSite { kind = plan.sites[i].kind, c = -plan.sites[i].c, r = plan.sites[i].r, mirror = true, scale = plan.sites[i].scale });
                foreach (var st in srcStreets) plan.streets.Add(new TownStreet { a = -st.a, b = -st.b, half = st.half });
                foreach (var pz in srcPlazas) plan.plazas.Add((-pz.c, pz.r));
                foreach (var y in srcYards) plan.yards.Add((-y.c, -y.u, y.half));
                int dropped = 0; var droppedWhat = new StringBuilder();
                foreach (var l in srcLots)
                {
                    var m = new TownLot { kind = l.kind, v = l.v, pos = -l.pos, yaw = l.yaw + 180f, size = l.size, prop = l.prop, group = l.group < 0 ? -1 : l.group + 1000, structure = l.structure, site = l.site, scale = l.scale, tall = l.tall };
                    if (LotFits(plan, m, !l.structure && !(l.site == SiteKind.Ruins), l.prop && l.site == SiteKind.Town && l.kind == BK.Stall)) plan.lots.Add(m);
                    else { dropped++; droppedWhat.Append(l.site + "/" + l.kind + (l.prop ? "(prop)" : "") + " "); }
                }
                foreach (var t in srcTrees) if (FreeSpot(plan, -t.p, 2.5f)) plan.trees.Add(new TownTree { p = -t.p, cat = t.cat, site = t.site });
                log.AppendLine("mirror: dropped " + dropped + " of " + srcLots.Count + (dropped > 0 ? " - " + droppedWhat : ""));
            }
            RasterizeTowns(plan);
            log.AppendLine("total lots " + plan.lots.Count + " (buildings " + plan.lots.Count(l => !l.prop) + "), trees " + plan.trees.Count + ", streets " + plan.streets.Count);
            plan.log = log.ToString();
            return plan;
        }

        // ------------------------------------------------------------------ lot validation
        static Vector2[] Corners(TownLot l, float grow)
        {
            Vector2 r = l.Right * (l.size.x * .5f + grow), f = l.Fwd * (l.size.y * .5f + grow);
            return new[] { l.pos - r - f, l.pos + r - f, l.pos + r + f, l.pos - r + f };
        }

        static bool Overlap(TownLot a, TownLot b, float gap)
        {
            float reach = (a.size.magnitude + b.size.magnitude) * .5f + gap;
            if ((a.pos - b.pos).sqrMagnitude > reach * reach) return false;
            var ca = Corners(a, gap * .5f); var cb = Corners(b, gap * .5f);
            foreach (var axis in new[] { a.Right, a.Fwd, b.Right, b.Fwd })
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in ca) { float d = Vector2.Dot(p, axis); amin = Mathf.Min(amin, d); amax = Mathf.Max(amax, d); }
                foreach (var p in cb) { float d = Vector2.Dot(p, axis); bmin = Mathf.Min(bmin, d); bmax = Mathf.Max(bmax, d); }
                if (amax < bmin || bmax < amin) return false;
            }
            return true;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        IEnumerable<Vector2> LotSamples(TownLot l)
        {
            var c = Corners(l, 0f);
            yield return l.pos;
            for (int i = 0; i < 4; i++) { yield return c[i]; yield return (c[i] + c[(i + 1) % 4]) * .5f; }
        }

        bool LotFits(TownPlan plan, TownLot l, bool checkStreets, bool onPlaza = false)
        {
            float hMin = float.MaxValue, hMax = float.MinValue;
            foreach (var p in LotSamples(l))
            {
                if (!TownGround(p.x, p.y, false)) return false;
                float h = TSample(field.H, p.x, p.y); hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
                if (checkStreets)
                {
                    foreach (var st in plan.streets) if (SegDist(p, st.a, st.b) < st.half + .6f) return false;
                    if (!onPlaza) foreach (var pz in plan.plazas) if ((p - pz.c).magnitude < pz.r + .5f) return false;
                }
            }
            float allowed = ProcBuildingMeshes.HasPlinth(l.kind) ? ProcBuildingMeshes.Foundation * l.scale - .45f : .7f;   // scaled plinths reach deeper
            if (hMax - hMin > allowed) return false;
            float gap = l.prop ? .4f : 1.2f;
            foreach (var o in plan.lots)
            {
                if (l.group >= 0 && o.group == l.group && l.structure && o.structure) continue;
                if (Overlap(l, o, Mathf.Min(gap, o.prop ? .4f : 1.2f))) return false;
            }
            foreach (var t in plan.trees) if (SegDistToLot(t.p, l) < 2.2f) return false;
            return true;
        }

        static float SegDistToLot(Vector2 p, TownLot l)
        {
            Vector2 d = p - l.pos;
            float x = Mathf.Abs(Vector2.Dot(d, l.Right)) - l.size.x * .5f, y = Mathf.Abs(Vector2.Dot(d, l.Fwd)) - l.size.y * .5f;
            return new Vector2(Mathf.Max(0f, x), Mathf.Max(0f, y)).magnitude;
        }

        bool FreeSpot(TownPlan plan, Vector2 p, float r)
        {
            if (!TownGround(p.x, p.y, false)) return false;
            foreach (var l in plan.lots) if (SegDistToLot(p, l) < r) return false;
            foreach (var st in plan.streets) if (SegDist(p, st.a, st.b) < st.half + r) return false;
            foreach (var pz in plan.plazas) if ((p - pz.c).magnitude < pz.r + r) return false;
            foreach (var t in plan.trees) if ((t.p - p).sqrMagnitude < 25f) return false;
            return true;
        }

        static float YawFacing(Vector2 frontDir) => Mathf.Atan2(-frontDir.x, -frontDir.y) * Mathf.Rad2Deg;   // local −Z → frontDir

        TownLot Lot(BK k, int v, Vector2 pos, float yaw, SiteKind site, bool prop = false, int group = -1, bool structure = false, float scale = 1f, float tall = 1f) =>
            new TownLot { kind = k, v = v, pos = pos, yaw = yaw, size = ProcBuildingMeshes.Footprint(k, v) * scale, prop = prop, group = group, structure = structure, site = site, scale = scale, tall = tall };

        bool Try(TownPlan plan, TownLot l, bool checkStreets = true, bool onPlaza = false)
        {
            if (!LotFits(plan, l, checkStreets, onPlaza)) return false;
            plan.lots.Add(l); return true;
        }

        int RandV(BK k) => townRng.Next(ProcBuildingMeshes.Variants(k));
        float TR(float a, float b) => a + (float)townRng.NextDouble() * (b - a);
        bool TChance(float p) => townRng.NextDouble() < p;

        BK PickHouse(SiteKind site, float dist, float r)
        {
            float u = (float)townRng.NextDouble(), t = dist / r;
            if (site == SiteKind.Town)
            {
                if (t < .45f) return u < .62f ? BK.TownHouse : u < .82f ? BK.Cottage : BK.Longhouse;
                if (t < .75f) return u < .38f ? BK.TownHouse : u < .72f ? BK.Cottage : u < .9f ? BK.Longhouse : BK.Barn;
                return u < .4f ? BK.Cottage : u < .65f ? BK.Longhouse : u < .85f ? BK.Barn : BK.TownHouse;
            }
            return u < .45f ? BK.Cottage : u < .7f ? BK.Longhouse : u < .85f ? BK.Barn : BK.TownHouse;
        }

        Vector2 ContourDir(Vector2 c, float r)
        {
            Vector2 best = Vector2.right; float bestD = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 16f; var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float d = Mathf.Abs(TSample(field.H, c.x + u.x * r * .8f, c.y + u.y * r * .8f) - TSample(field.H, c.x - u.x * r * .8f, c.y - u.y * r * .8f)) + TR(0f, .6f);
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        /// <summary>Houses along a street, both sides, several rows (row 0 at the street, further rows behind).</summary>
        void StreetLots(TownPlan plan, TownSite s, Vector2 a, Vector2 b, float half, int rows, float density)
        {
            Vector2 dir = (b - a).normalized, n = new Vector2(-dir.y, dir.x); float len = (b - a).magnitude;
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < rows; row++)
                {
                    float t = TR(0f, 3f);
                    while (t < len - 3f)
                    {
                        var k = PickHouse(s.kind, ((a + b) * .5f - s.c).magnitude + row * 10f, s.r);
                        int v = RandV(k); var fp = ProcBuildingMeshes.Footprint(k, v);
                        float off = half + 1.6f + row * 11.5f + fp.y * .5f + (row > 0 ? TR(-1f, 1.5f) : TR(0f, .8f));
                        var pos = a + dir * (t + fp.x * .5f) + n * side * off;
                        var front = -n * side;
                        if ((pos - s.c).magnitude > s.r + 6f) { t += 3f; continue; }
                        if (TChance(density) && Try(plan, Lot(k, v, pos, YawFacing(front) + TR(-2.5f, 2.5f), s.kind)))
                        {
                            Extras(plan, s, plan.lots[plan.lots.Count - 1]);
                            t += fp.x + TR(1.4f, 3.5f);
                        }
                        else t += 2.5f;
                    }
                }
        }

        /// <summary>Woodpiles / crates next to houses, hay + fenced yard behind barns.</summary>
        void Extras(TownPlan plan, TownSite s, TownLot h)
        {
            Vector2 r = h.Right, f = h.Fwd;
            if (h.kind == BK.Barn)
            {
                Vector2 yc = h.pos + f * (h.size.y * .5f + 7.5f);
                FenceRect(plan, s, yc, h.yaw, 16f, 12f);
                Try(plan, Lot(BK.Haystack, RandV(BK.Haystack), yc + r * TR(-4f, 4f), TR(0, 360), s.kind, true));
                Try(plan, Lot(BK.Haystack, RandV(BK.Haystack), yc + r * TR(-4f, 4f) + f * TR(-3f, 3f), TR(0, 360), s.kind, true));
                if (TChance(.5f)) Try(plan, Lot(BK.Cart, 0, h.pos + r * (h.size.x * .5f + 2f), h.yaw + TR(-30f, 30f), s.kind, true));
                return;
            }
            if (TChance(.35f)) Try(plan, Lot(BK.Woodpile, 0, h.pos + r * (h.size.x * .5f + 1.2f) * (TChance(.5f) ? 1 : -1) + f * h.size.y * .2f, h.yaw + 90f, s.kind, true));
            else if (TChance(.3f)) Try(plan, Lot(BK.Crates, RandV(BK.Crates), h.pos + r * (h.size.x * .5f + 1.6f) * (TChance(.5f) ? 1 : -1), h.yaw + TR(-20f, 20f), s.kind, true));
            if (s.kind == SiteKind.Village && TChance(.25f)) FenceRect(plan, s, h.pos + f * (h.size.y * .5f + 6f), h.yaw, 10f, 8f);
        }

        bool FenceRect(TownPlan plan, TownSite s, Vector2 c, float yaw, float w, float d)
        {
            var probe = Lot(BK.Fence, 0, c, yaw, s.kind, true); Vector2 r = probe.Right, f = probe.Fwd;
            BK kind = TChance(.3f) ? BK.StoneFence : BK.Fence;
            int placed = 0, attempted = 0, first = plan.lots.Count;
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side % 2 == 0; float len = alongX ? w : d; int n = Mathf.Max(1, Mathf.RoundToInt(len / 4f));
                Vector2 edgeC = c + (side == 0 ? -f * d * .5f : side == 1 ? r * w * .5f : side == 2 ? f * d * .5f : -r * w * .5f);
                Vector2 along = alongX ? r : f;
                for (int i = 0; i < n; i++)
                {
                    if (side == 0 && i == n / 2) continue;   // gate gap
                    var p = edgeC + along * ((i + .5f) / n * len - len * .5f);
                    attempted++;
                    if (Try(plan, Lot(kind, 0, p, yaw + (alongX ? 0f : 90f), s.kind, true))) placed++;
                }
            }
            if (placed < attempted * .75f) { plan.lots.RemoveRange(first, plan.lots.Count - first); return false; }   // no stray fence pieces
            plan.yards.Add((c, r, Mathf.Max(w, d) * .5f));
            return true;
        }

        void Trees(TownPlan plan, TownSite s, int count, float rIn, float rOut, string[] cats)
        {
            for (int i = 0, tries = 0; i < count && tries < count * 8; tries++)
            {
                float a = TR(0f, Mathf.PI * 2f), rr = TR(rIn, rOut);
                var p = s.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                if (!FreeSpot(plan, p, 3f)) continue;
                plan.trees.Add(new TownTree { p = p, cat = cats[townRng.Next(cats.Length)], site = s.kind }); i++;
            }
        }

        string[] TreeCats => Plants.Trees.Select(t => t.cat).Where(c => c != "Dead").DefaultIfEmpty("Broadleaf").ToArray();

        void Windmill(TownPlan plan, TownSite s)
        {
            Vector2 best = default; float bestH = float.MinValue;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12f, rr = s.r + TR(0f, 22f);
                var p = s.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                var probe = Lot(BK.Windmill, 0, p, TR(0, 360), s.kind);
                if (!LotFits(plan, probe, true)) continue;
                float h = TSample(field.H, p.x, p.y);
                if (h > bestH) { bestH = h; best = p; }
            }
            if (bestH > float.MinValue) Try(plan, Lot(BK.Windmill, RandV(BK.Windmill), best, YawFacing((s.c - best).normalized), s.kind));
        }

        // ------------------------------------------------------------------ town
        void LayoutTown(TownPlan plan, TownSite s, int index)
        {
            Vector2 u = ContourDir(s.c, s.r), v = new Vector2(-u.y, u.x);
            float plaza = 16f;
            plan.plazas.Add((s.c, plaza));
            plan.streets.Add(new TownStreet { a = s.c - u * (s.r - 4f), b = s.c + u * (s.r - 4f), half = 4f });
            plan.streets.Add(new TownStreet { a = s.c - v * (s.r * .7f), b = s.c + v * (s.r * .7f), half = 3.5f });
            // church and inn on two plaza corners, facing the plaza
            var diag = new[] { (u + v).normalized, (u - v).normalized, (-u - v).normalized, (-u + v).normalized };
            int start = townRng.Next(4);
            for (int i = 0; i < 4; i++)
            {
                var d = diag[(start + i) % 4];
                if (Try(plan, Lot(BK.Church, RandV(BK.Church), s.c + d * (plaza + 15f), YawFacing(-d), s.kind))) { start = (start + i + 1) % 4; break; }
            }
            for (int i = 0; i < 3; i++)
            {
                var d = diag[(start + i) % 4];
                if (Try(plan, Lot(BK.Inn, RandV(BK.Inn), s.c + d * (plaza + 10.5f), YawFacing(-d), s.kind))) break;
            }
            // well + market stalls on the plaza
            Try(plan, Lot(BK.Well, 0, s.c + u * 2f + v * 2f, TR(0, 360), s.kind, true), false);
            float a0 = Mathf.Atan2(u.y, u.x);
            for (int i = 0; i < 8; i++)
            {
                float a = a0 + Mathf.PI / 4f + i * Mathf.PI / 4f + TR(-.1f, .1f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (Mathf.Abs(Vector2.Dot(dir, u)) > .93f || Mathf.Abs(Vector2.Dot(dir, v)) > .93f) continue;
                Try(plan, Lot(BK.Stall, RandV(BK.Stall), s.c + dir * 10.5f, YawFacing(-dir), s.kind, true), true, true);
            }
            foreach (var st in plan.streets.Skip(plan.streets.Count - 2).ToArray())
            {
                // split each street at the plaza so the rows do not run through it
                Vector2 dir = (st.b - st.a).normalized; float len = (st.b - st.a).magnitude;
                StreetLots(plan, s, st.a, st.a + dir * (len * .5f - plaza - 1f), st.half, 3, .9f);
                StreetLots(plan, s, st.a + dir * (len * .5f + plaza + 1f), st.b, st.half, 3, .9f);
            }
            Windmill(plan, s);
            Trees(plan, s, 18, s.r * .7f, s.r + 18f, TreeCats);
        }

        // ------------------------------------------------------------------ village
        void LayoutVillage(TownPlan plan, TownSite s, int index)
        {
            Vector2 u = ContourDir(s.c, s.r), v = new Vector2(-u.y, u.x);
            float L = s.r * 2f - 14f;
            Vector2 a = s.c - u * L * .5f + v * TR(-6f, 6f), m = s.c + v * TR(-12f, 12f), b = s.c + u * L * .5f + v * TR(-6f, 6f);
            plan.streets.Add(new TownStreet { a = a, b = m, half = 3f });
            plan.streets.Add(new TownStreet { a = m, b = b, half = 3f });
            bool lane = TChance(.6f);
            Vector2 laneEnd = m + v * (TChance(.5f) ? 1 : -1) * s.r * .6f;
            if (lane) plan.streets.Add(new TownStreet { a = m, b = laneEnd, half = 2.5f });
            Try(plan, Lot(BK.Well, 0, m + v * 5.5f + u * 3f, TR(0, 360), s.kind, true));
            StreetLots(plan, s, a, m, 3f, 2, .8f);
            StreetLots(plan, s, m, b, 3f, 2, .8f);
            if (lane) StreetLots(plan, s, m + (laneEnd - m).normalized * 6f, laneEnd, 2.5f, 1, .8f);
            Windmill(plan, s);
            // fields: a couple of fenced pastures with hay on the outskirts
            for (int i = 0; i < 3; i++)
            {
                float ang = TR(0f, Mathf.PI * 2f); var c = s.c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (s.r * .85f);
                if (FreeSpot(plan, c, 9f) && FenceRect(plan, s, c, YawFacing(u), 18f, 14f)) { Try(plan, Lot(BK.Haystack, RandV(BK.Haystack), c, TR(0, 360), s.kind, true)); }
            }
            Trees(plan, s, 14, s.r * .55f, s.r + 16f, TreeCats);
        }

        // ------------------------------------------------------------------ fort
        /// <summary>2026-10-16 「城堡做大点，壮观点」 option A: the fort (walls, towers, gatehouse, keep) is built at ×2.</summary>
        const float FortScale = 2f;

        void LayoutFort(TownPlan plan, TownSite s, int index)
        {
            float S = s.scale;   // FortScale, or 1 when only the original size fitted
            Vector2 u = (-s.c).normalized;   // face the map centre (the enemy side)
            float ang = Mathf.Round(Mathf.Atan2(u.y, u.x) / (Mathf.PI / 2f)) * (Mathf.PI / 2f) + TR(-.2f, .2f);
            u = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); Vector2 v = new Vector2(-u.y, u.x);
            float H = 28f * S - (S > 1f ? 1f : 0f), seg = 10f * S; int g = index;
            // streets: gate road out and to the keep
            plan.streets.Add(new TownStreet { a = s.c + u * (H + 32f * S), b = s.c + u * (H - 4f * S), half = 3f * Mathf.Sqrt(S) });
            plan.streets.Add(new TownStreet { a = s.c + u * (H - 8f * S), b = s.c - u * 3f * S, half = 2.6f * Mathf.Sqrt(S) });
            var sides = new[] { u, v, -u, -v };
            foreach (var o in sides)
            {
                Vector2 along = new Vector2(-o.y, o.x);
                for (int i = -2; i <= 2; i++)
                {
                    var pos = s.c + o * H + along * (i * seg);
                    if (o == u && i == 0) { Try(plan, Lot(BK.Gatehouse, 0, pos, YawFacing(o), s.kind, false, g, true, S), false); continue; }
                    Try(plan, Lot(BK.WallSeg, 0, pos, YawFacing(o), s.kind, false, g, true, S), false);
                }
                // two towers along every wall (between the segments)
                if (S > 1f)
                    for (int m = -1; m <= 1; m += 2)
                        Try(plan, Lot(BK.Tower, 0, s.c + o * H + along * (m * seg * 1.5f), TR(0, 360), s.kind, false, g, true, S * .8f), false);
            }
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                Try(plan, Lot(BK.Tower, RandV(BK.Tower), s.c + u * (sx * H) + v * (sz * H), TR(0, 360), s.kind, false, g, true, S), false);
            var keepPos = s.c - u * 14.5f * S;
            if (S > 1f) { LayoutFortInterior(plan, s, u, v, H, g); return; }
            if (!Try(plan, Lot(BK.Keep, RandV(BK.Keep), keepPos, YawFacing(u), s.kind))) Try(plan, Lot(BK.Keep, 0, keepPos + v * 6f, YawFacing(u), s.kind));
            Try(plan, Lot(BK.Watchtower, RandV(BK.Watchtower), s.c - u * 21f * S + v * 20f * S, TR(0, 360), s.kind, false, -1, false, Mathf.Max(1f, S * .75f)));
            int tents = Mathf.RoundToInt(10 * S);
            for (int i = 0; i < tents; i++)
            {
                float x = TR(-2f, 20f) * S, y = TR(8f, 22f) * S * (TChance(.5f) ? 1 : -1);
                var k = BK.Tent; int tv = townRng.Next(3);
                Try(plan, Lot(k, tv, s.c + u * x + v * y, YawFacing(-v * Mathf.Sign(y)) + TR(-8f, 8f), s.kind, true));
            }
            for (int i = 0; i < 4 * S; i++) Try(plan, Lot(BK.Crates, RandV(BK.Crates), s.c + u * TR(-18f, 18f) * S + v * TR(-22f, 22f) * S, TR(0, 360), s.kind, true));
            Try(plan, Lot(BK.Cart, 0, s.c + u * (H + 8f * S) + v * 6f * S, YawFacing(v), s.kind, true));
            // palisade-free ground outside: a few tents of a field camp in front of the gate
            for (int i = 0; i < 6 * S; i++)
            {
                var p = s.c + u * TR(H + 10f, H + 26f * S) + v * TR(-24f, 24f) * S;
                Try(plan, Lot(BK.Tent, townRng.Next(3), p, YawFacing(-u) + TR(-20f, 20f), s.kind, true));
            }
        }

        /// <summary>
        /// 2026-10-16 「城堡里面太单调」: interior of the x2 fort after real concentric castles. Inner ward at the back (own
        /// curtain wall, corner towers, inner gatehouse) around the keep + spire and a small orchard; outer bailey with
        /// buildings leaning on the curtain walls - chapel, storehouses (granary), barracks, great hall, stables with hay,
        /// smithy with woodpiles - and an open court along the gate road with a well, market stalls and a fenced training yard.
        /// Existing building kinds only (decoration, same palettes / season swaps).
        /// </summary>
        void LayoutFortInterior(TownPlan plan, TownSite s, Vector2 u, Vector2 v, float H, int g)
        {
            Vector2 P(float a, float b) => s.c + u * a + v * b;   // a: towards the gate (+H), b: sideways
            const float IW = 1.4f;                                // inner curtain scale
            float front = -6f, side = 27f, back = -H + 3f;        // inner ward box (back = outer back wall)
            // street: gate road to the inner gate
            plan.streets.Add(new TownStreet { a = P(H - 10f, 0f), b = P(front + 4f, 0f), half = 4.2f });

            // --- inner ward: curtain wall, corner towers, inner gatehouse
            TL(plan, "内城门", Lot(BK.Gatehouse, 0, P(front, 0f), YawFacing(u), s.kind, false, g, true, IW), false);
            foreach (float b in new[] { -18.5f, 18.5f })
                Try(plan, Lot(BK.WallSeg, 0, P(front, b), YawFacing(u), s.kind, false, g, true, IW), false);
            foreach (int sg in new[] { -1, 1 })
            {
                for (float a = front - 7.5f; a > back + 4f; a -= 14f)
                    Try(plan, Lot(BK.WallSeg, 0, P(a, sg * side), YawFacing(v * sg), s.kind, false, g, true, IW), false);
                Try(plan, Lot(BK.Tower, 0, P(front, sg * side), TR(0, 360), s.kind, false, g, true, 1.4f), false);
            }
            // keep + spire (flat keep with merlons, tall spired tower from its middle)
            var keepPos = P(-31f, 0f);
            if (TL(plan, "主堡", Lot(BK.Keep, 0, keepPos, YawFacing(u), s.kind, false, g, true, 2f), false))
                Try(plan, Lot(BK.Tower, 1, keepPos, TR(0, 360), s.kind, false, g, true, 2.2f, 1.6f), false);
            Try(plan, Lot(BK.Well, 0, P(-12.5f, 15f), 0f, s.kind, true));
            foreach (var t in new[] { P(-47f, -19f), P(-47f, 19f), P(-40f, -20f), P(-40f, 20f) })
                if (FreeSpot(plan, t, 2.5f)) plan.trees.Add(new TownTree { p = t, cat = TreeCats[townRng.Next(TreeCats.Length)], site = s.kind });

            // --- outer bailey, buildings leaning on the curtain walls (front faces the court)
            float lean = H - 15f;                                  // 40 m: clear of the (rotated) mid-wall towers
            // behind, left: chapel; behind, right: granary + storehouse
            // the fort usually sits on a rise: a larger scale gives a deeper plinth (more slope allowed), then nearby spots
            bool Any(string what, Func<float, float, float, TownLot> make, params (float a, float b, float sc)[] spots)
            {
                foreach (var t in spots) if (Try(plan, make(t.a, t.b, t.sc))) return true;
                fortMisses.Append(what + " "); return false;
            }
            if (!Any("小教堂", (a, b, sc) => Lot(BK.Church, 0, P(a, b), YawFacing(u), s.kind, false, -1, false, sc),
                    (-30f, -38.5f, 1.25f), (-30f, -38.5f, 1f), (-27f, -38.5f, 1.15f), (-33f, -38.5f, 1.15f), (-30f, -37f, 1.35f)))
                Any("小教堂(小)", (a, b, sc) => Lot(BK.Cottage, 0, P(a, b), YawFacing(v), s.kind, false, -1, false, sc), (-30f, -38.5f, 1.2f), (-24f, -38.5f, 1.2f));
            Any("粮仓", (a, b, sc) => Lot(BK.Longhouse, 1, P(a, b), YawFacing(-v), s.kind, false, -1, false, sc), (-21f, 38.5f, 1.35f), (-21f, 38.5f, 1.2f), (-18f, 37.5f, 1.3f), (-24f, 37.5f, 1.3f));
            Any("仓库", (a, b, sc) => Lot(BK.Longhouse, 0, P(a, b), YawFacing(-v), s.kind, false, -1, false, sc), (-39f, 38.5f, 1.35f), (-39f, 38.5f, 1.2f), (-42f, 37.5f, 1.3f), (-36f, 37.5f, 1.3f),
                (-39f, 38.5f, 1f));
            for (int i = 0; i < 5; i++) Try(plan, Lot(BK.Crates, RandV(BK.Crates), P(TR(-46f, -14f), TR(31f, 33f)), TR(0, 360), s.kind, true));
            // left wall: great hall + barracks
            // 2026-10-16 「要塞建筑补位」: every wall building tries nearby spots / slightly smaller sizes before it is dropped
            Any("大厅", (a, b, sc) => Lot(BK.Inn, 1, P(a, b), YawFacing(v), s.kind, false, -1, false, sc),
                (6f, -lean + 1f, 1f), (3f, -lean + 1f, 1f), (9f, -lean + 1f, 1f), (6f, -lean + 2.5f, .9f), (3f, -lean + 2.5f, .85f), (9f, -lean + 2.5f, .85f));
            Any("长屋(左中)", (a, b, sc) => Lot(BK.Longhouse, 0, P(a, b), YawFacing(v), s.kind, false, -1, false, sc),
                (24f, -lean, 1.3f), (24f, -lean, 1.15f), (22f, -lean, 1.2f), (26f, -lean, 1.2f), (24f, -lean + 1.5f, 1.05f), (24f, -lean + 2.5f, .95f));
            Any("长屋(左后)", (a, b, sc) => Lot(BK.Longhouse, 1, P(a, b), YawFacing(v), s.kind, false, -1, false, sc),
                (41f, -lean, 1.1f), (41f, -lean, 1f), (39f, -lean, 1f), (42.5f, -lean, .95f), (40f, -lean + 1.5f, .95f), (40f, -lean + 2.5f, .85f));
            // right wall: smithy + workshops, stables with hay
            Any("铁匠/作坊", (a, b, sc) => Lot(BK.Cottage, 1, P(a, b), YawFacing(-v), s.kind, false, -1, false, sc),
                (4f, lean, 1f), (2f, lean, 1f), (4f, lean - 1.5f, .9f));
            Any("铁匠/作坊", (a, b, sc) => Lot(BK.Cottage, 2, P(a, b), YawFacing(-v), s.kind, false, -1, false, sc),
                (13f, lean, 1f), (15f, lean, 1f), (13f, lean - 1.5f, .9f));
            Try(plan, Lot(BK.Woodpile, 0, P(8.5f, lean - 6f), YawFacing(-v), s.kind, true));
            Any("马厩", (a, b, sc) => Lot(BK.Barn, 1, P(a, b), YawFacing(-v), s.kind, false, -1, false, sc),
                (31f, lean - 1f, 1.15f), (31f, lean - 1f, 1f), (29f, lean - 1f, 1.05f), (33f, lean - 1f, 1.05f), (31f, lean - 2.5f, .95f));
            Try(plan, Lot(BK.Haystack, 0, P(41f, lean - 2f), TR(0, 360), s.kind, true));
            Try(plan, Lot(BK.Haystack, 1, P(44f, lean + 2f), TR(0, 360), s.kind, true));
            Try(plan, Lot(BK.Cart, 0, P(24f, lean - 9f), YawFacing(u), s.kind, true));

            // --- open court along the gate road: well, market stalls, fenced training yard
            Try(plan, Lot(BK.Well, 0, P(30f, 9f), 0f, s.kind, true));
            foreach (var (a, b) in new[] { (12f, 8f), (12f, -8f), (32f, -8.5f), (40f, 8.5f) })
                Try(plan, Lot(BK.Stall, RandV(BK.Stall), P(a, b), YawFacing(b > 0 ? -v : v), s.kind, true), true, true);
            Vector2 yard = P(10f, -21f); float yh = 6f;
            for (int k = -1; k <= 1; k += 2)
            {
                Try(plan, Lot(BK.Fence, 0, yard + u * (k * yh) + v * -2f, YawFacing(u), s.kind, true));
                Try(plan, Lot(BK.Fence, 0, yard + u * (k * yh) + v * 2f, YawFacing(u), s.kind, true));
                Try(plan, Lot(BK.Fence, 0, yard + v * (k * yh) + u * -2f, YawFacing(v), s.kind, true));
                Try(plan, Lot(BK.Fence, 0, yard + v * (k * yh) + u * 2f, YawFacing(v), s.kind, true));
            }
            for (int i = 0; i < 3; i++) Try(plan, Lot(BK.Tent, 1 + townRng.Next(2), P(TR(28f, 46f), TR(-22f, -14f)), YawFacing(v) + TR(-15f, 15f), s.kind, true));
            for (int i = 0; i < 4; i++) Try(plan, Lot(BK.Crates, RandV(BK.Crates), P(TR(14f, 46f), TR(-30f, 30f)), TR(0, 360), s.kind, true));

            // --- outside: field camp in front of the gate
            Try(plan, Lot(BK.Cart, 0, P(H + 14f, 9f), YawFacing(v), s.kind, true));
            for (int i = 0; i < 12; i++)
                Try(plan, Lot(BK.Tent, townRng.Next(3), P(TR(H + 12f, H + 48f), TR(-46f, 46f)), YawFacing(-u) + TR(-20f, 20f), s.kind, true));
        }

        bool TL(TownPlan plan, string what, TownLot l, bool checkStreets = true, bool onPlaza = false)
        {
            if (Try(plan, l, checkStreets, onPlaza)) return true;
            fortMisses.Append(what + " ");
            return false;
        }
        readonly StringBuilder fortMisses = new StringBuilder();

        // ------------------------------------------------------------------ ruins
        void LayoutRuins(TownPlan plan, TownSite s, int index)
        {
            float baseYaw = TR(0f, 90f);
            var pieces = new List<(BK k, int n)> { (BK.RuinHouse, 4 + townRng.Next(3)), (BK.RuinTower, 1), (BK.RuinWall, 3), (BK.Rubble, 5) };
            foreach (var (k, n) in pieces)
                for (int i = 0, tries = 0; i < n && tries < n * 10; tries++)
                {
                    float a = TR(0f, Mathf.PI * 2f), rr = Mathf.Sqrt(TR(0f, 1f)) * s.r * .85f;
                    var p = s.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    float yaw = k == BK.Rubble ? TR(0, 360) : baseYaw + 90f * townRng.Next(4) + TR(-12f, 12f);
                    if (Try(plan, Lot(k, RandV(k), p, yaw, s.kind, k == BK.Rubble), false)) i++;
                }
            Trees(plan, s, 5, 0f, s.r, new[] { "Dead" });
            Trees(plan, s, 4, 0f, s.r, new[] { "Stump" });
        }

        // ------------------------------------------------------------------ dirt / occupancy masks
        void RasterizeTowns(TownPlan plan)
        {
            const int N2 = 1025; const float S2 = 2f;
            void Paint(Vector2 min, Vector2 max, Func<Vector2, float> f)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x + 1024f) / S2)), x1 = Mathf.Min(N2 - 1, Mathf.CeilToInt((max.x + 1024f) / S2));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((min.y + 1024f) / S2)), z1 = Mathf.Min(N2 - 1, Mathf.CeilToInt((max.y + 1024f) / S2));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float v = f(new Vector2(-1024f + x * S2, -1024f + z * S2));
                        int i = z * N2 + x; if (v > plan.dirt[i]) plan.dirt[i] = v;
                    }
            }
            foreach (var st in plan.streets)
            {
                float reach = st.half + 3f;
                Paint(Vector2.Min(st.a, st.b) - Vector2.one * reach, Vector2.Max(st.a, st.b) + Vector2.one * reach,
                    p => .95f * (1f - Field2048.Smooth(st.half - 1.2f, st.half + 2.5f, SegDist(p, st.a, st.b))));
            }
            foreach (var pz in plan.plazas)
                Paint(pz.c - Vector2.one * (pz.r + 4f), pz.c + Vector2.one * (pz.r + 4f), p => 1f - Field2048.Smooth(pz.r - 2f, pz.r + 3f, (p - pz.c).magnitude));
            foreach (var y in plan.yards)
                Paint(y.c - Vector2.one * (y.half + 3f), y.c + Vector2.one * (y.half + 3f), p => .45f * (1f - Field2048.Smooth(y.half * .6f, y.half + 1f, (p - y.c).magnitude)));
            foreach (var l in plan.lots)
            {
                if (l.prop && l.kind != BK.Tent) continue;
                float reach = l.size.magnitude * .5f + 3f;
                float k = l.site == SiteKind.Ruins ? .5f : .6f;
                Paint(l.pos - Vector2.one * reach, l.pos + Vector2.one * reach, p => k * (1f - Field2048.Smooth(.5f, 2.6f, SegDistToLot(p, l))));
            }
            foreach (var s in plan.sites.Where(x => x.kind == SiteKind.Fort))
                Paint(s.c - Vector2.one * 36f * s.scale, s.c + Vector2.one * 36f * s.scale, p => .4f * (1f - Field2048.Smooth(24f * s.scale, 32f * s.scale, (p - s.c).magnitude)));
            // occupancy (4 m) for the meadow / pebble scatter: footprints + 3 m, streets, plazas
            for (int z = 0; z < Field2048.N; z++)
                for (int x = 0; x < Field2048.N; x++)
                {
                    float wx = Field2048.X(x), wz = Field2048.X(z);
                    int i2 = Mathf.Clamp(Mathf.RoundToInt((wz + 1024f) / S2), 0, N2 - 1) * N2 + Mathf.Clamp(Mathf.RoundToInt((wx + 1024f) / S2), 0, N2 - 1);
                    plan.occupied[z * Field2048.N + x] = plan.dirt[i2] > .3f;
                }
            foreach (var l in plan.lots)
            {
                float reach = l.size.magnitude * .5f + 3f;
                int x0 = Mathf.Max(0, CellOf(l.pos.x - reach)), x1 = Mathf.Min(Field2048.N - 1, CellOf(l.pos.x + reach) + 1);
                int z0 = Mathf.Max(0, CellOf(l.pos.y - reach)), z1 = Mathf.Min(Field2048.N - 1, CellOf(l.pos.y + reach) + 1);
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    if (SegDistToLot(new Vector2(Field2048.X(x), Field2048.X(z)), l) < 3f) plan.occupied[z * Field2048.N + x] = true;
            }
        }

        /// <summary>Extra dirt for the splat (streets, plazas, yards, building aprons).</summary>
        protected float TownDirt(float x, float z)
        {
            if (!HasTowns) return 0f;
            var d = Towns.dirt; const int N2 = 1025;
            float fx = Mathf.Clamp((x + 1024f) / 2f, 0f, N2 - 1.001f), fz = Mathf.Clamp((z + 1024f) / 2f, 0f, N2 - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            float a = Mathf.Lerp(d[iz * N2 + ix], d[iz * N2 + ix + 1], tx), b = Mathf.Lerp(d[(iz + 1) * N2 + ix], d[(iz + 1) * N2 + ix + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>True on settlement ground (no meadow flowers / pebbles there).</summary>
        protected bool InTown(float x, float z)
        {
            if (!HasTowns) return false;
            int ix = Mathf.Clamp(Mathf.RoundToInt((x + Field2048.Half) / Field2048.Step), 0, Field2048.N - 1), iz = Mathf.Clamp(Mathf.RoundToInt((z + Field2048.Half) / Field2048.Step), 0, Field2048.N - 1);
            return Towns.occupied[iz * Field2048.N + ix];
        }

        // ------------------------------------------------------------------ decoration
        static readonly string[] SiteGroups = { "建筑·城镇", "建筑·村庄", "建筑·要塞", "建筑·废墟" };

        protected void DecorateTowns(Decorator d)
        {
            if (!HasTowns) return;
            if (!ProcBuildingLibrary.Ready) ProcBuildingLibrary.Build();
            var plan = Towns; var pal = ProcBuildingMeshes.PaletteFor(Theme);
            int placed = 0, failed = 0;
            foreach (var l in plan.lots)
            {
                var hs = LotSamples(l).Select(p => d.Height(p.x, p.y)).ToArray();
                float floorY = ProcBuildingMeshes.HasPlinth(l.kind) ? hs.Max() - .05f : hs.Average() - .06f;
                var g = d.Group(SiteGroups[(int)l.site]);
                if (d.PlaceAt(ProcBuildingLibrary.PrefabPath(l.kind, l.v, pal), l.pos.x, l.pos.y, l.yaw, floorY, g, !l.prop, l.scale, l.tall) != null) placed++; else failed++;
            }
            var tg = d.Group("建筑·树木");
            foreach (var t in plan.trees)
            {
                var pool = ProcPropLibrary.Pool(t.cat);
                bool small = t.cat == "Stump";
                d.Place(d.Pick(pool, false), t.p.x, t.p.y, small ? d.Rand(1.2f, 2f) : d.Rand(Plants.TreeMin * .8f, Plants.TreeMax * .9f), tg, false, !small, small ? .1f : .35f);
            }
            File.WriteAllText(Size.LogDir + "/" + Id + "-towns.txt", plan.log + "placed " + placed + ", failed " + failed + "\n");
        }
    }

    // ==================================================================== review screenshots of the settlements
    public static class Settlement2048Shots
    {
        const string OutDir = "outputs/ProcBuildings/maps";

        [MenuItem(Battlefield2048Setup.MenuRoot + "村镇截图（4 张地图）", false, 45)]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Settlements] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[Settlements] 有未保存的场景，已取消。"); return; }
            string restore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(OutDir);
            try
            {
                foreach (var m in Battlefield2048Menus.All.OfType<Proc2048MapBase>())
                {
                    if (!File.Exists(m.ScenePath)) continue;
                    Shoot(m);
                    EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect();
                }
                Debug.Log("[Settlements] 截图完成 → " + OutDir);
            }
            finally
            {
                if (!string.IsNullOrEmpty(restore) && File.Exists(restore)) EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
            }
        }

        static void Shoot(Proc2048MapBase m)
        {
            EditorSceneManager.OpenScene(m.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main; if (cam == null) return;
            var plan = m.Towns;
            var views = new List<(Vector3 eye, Vector3 at, string tag)>();
            foreach (var s in plan.sites.Where(x => !x.mirror))
            {
                Vector3 at = new Vector3(s.c.x, m.Height(s.c.x, s.c.y), s.c.y);
                Vector3 back = new Vector3(-s.c.x, 0, -s.c.y).normalized;   // look from the map centre side
                Vector3 eye = at + back * s.r * 1.25f + Vector3.up * s.r * .75f;
                views.Add((eye, at, s.kind.ToString()));
            }
            var ts = plan.sites.FirstOrDefault(x => x.kind == Proc2048MapBase.SiteKind.Town && !x.mirror);
            if (ts != null)
            {
                Vector3 at = new Vector3(ts.c.x, m.Height(ts.c.x, ts.c.y) + 3f, ts.c.y);
                views.Add((at + new Vector3(30f, 9f, -24f), at + new Vector3(-10f, 0f, 8f), "TownStreet"));
                views.Add((at + new Vector3(260f, 170f, -200f), at, "TownWide"));
            }
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24); var img = new Texture2D(W, H, TextureFormat.RGB24, false);
            var oldTarget = cam.targetTexture; var active = RenderTexture.active; bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                for (int i = 0; i < views.Count; i++)
                {
                    cam.transform.position = views[i].eye; cam.transform.LookAt(views[i].at);
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, W, H), 0, 0); img.Apply();
                    File.WriteAllBytes(OutDir + "/" + m.Folder + "_" + i + "_" + views[i].tag + ".jpg", img.EncodeToJPG(85));
                }
            }
            finally
            {
                cam.targetTexture = oldTarget; RenderTexture.active = active; ShaderUtil.allowAsyncCompilation = async;
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(img);
            }
        }
    }
}
