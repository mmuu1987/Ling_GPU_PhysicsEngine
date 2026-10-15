using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MassEngine.Game.Ui;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>Brush deployment, phase 1 (2026-10-15): paint / erase the selected composition's footprint on the
    /// deployment map. One stroke = one Undo step. Members are placed front first on a lattice inside the painted cells
    /// (CPU, WarSandboxBrushMask); the GPU simulation only receives spawn positions.</summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private bool brushMode, brushErase;
        private int brushSize = 1;
        private static readonly float[] BrushRadii = { 5f, 12.5f, 25f };
        private static readonly string[] BrushNames = { "小", "中", "大" };

        // Stroke state: a world-aligned cell grid for the whole battlefield, built on pointer down.
        private bool brushStroke, brushStrokeErase, brushHasLast;
        private WarSandboxDeploymentDraft brushDraft; private int brushRevision, brushIndex; private WarSandboxDeploymentEntry brushOriginal;
        private bool[] brushGrid, brushBlocked; private int brushGW, brushGH; private Vector2 brushOrigin; private float brushRadius;
        private int brushMinI, brushMinJ, brushMaxI, brushMaxJ; private Vector2 brushLast;
        private Texture2D brushGhostTex; private VisualElement dpBrushGhost, dpBrushRing;
        private readonly Dictionary<WarSandboxBrushMask, KeyValuePair<string, Texture2D>> brushTextures = new Dictionary<WarSandboxBrushMask, KeyValuePair<string, Texture2D>>();

        private Button dpBrushPaint, dpBrushErase, dpBrushClear; private readonly Button[] dpBrushSizes = new Button[3]; private Label dpBrushInfo;

        private const float Cell = WarSandboxBrushMask.DefaultCell;

        private void BuildBrushControls(VisualElement r)
        {
            var host = r.Q("dp-place-scroll"); if (host == null) return;
            var box = new VisualElement();
            var title = new Label("画笔布阵"); title.AddToClassList("dp-sub"); box.Add(title);
            var row = Tk.E("row", "dp-btnrow"); box.Add(row);
            dpBrushPaint = BrushButton(row, "画笔", () => { brushMode = !(brushMode && !brushErase); brushErase = false; spatialSelection = true; placing = false; });
            dpBrushErase = BrushButton(row, "橡皮", () => { brushMode = !(brushMode && brushErase); brushErase = true; spatialSelection = true; placing = false; });
            dpBrushClear = BrushButton(row, "恢复矩形", ClearBrushShape);
            var sizes = Tk.E("row", "dp-btnrow"); box.Add(sizes);
            for (int k = 0; k < 3; k++) { int s = k; dpBrushSizes[k] = BrushButton(sizes, BrushNames[k], () => brushSize = s); }
            dpBrushInfo = new Label(); dpBrushInfo.AddToClassList("hint"); dpBrushInfo.AddToClassList("hint--wrap"); dpBrushInfo.AddToClassList("dp-gap");
            dpBrushInfo.style.height = 66; dpBrushInfo.style.overflow = Overflow.Hidden; // fixed size: content changes never move the panel
            box.Add(dpBrushInfo);
            host.Insert(0, box);

            dpBrushGhost = Layer(dpMap); dpBrushGhost.style.right = StyleKeyword.Auto; dpBrushGhost.style.bottom = StyleKeyword.Auto; Tk.Show(dpBrushGhost, false);
            dpBrushRing = Layer(dpMap);
            dpBrushRing.style.right = StyleKeyword.Auto; dpBrushRing.style.bottom = StyleKeyword.Auto;
            dpBrushRing.style.borderTopWidth = dpBrushRing.style.borderBottomWidth = dpBrushRing.style.borderLeftWidth = dpBrushRing.style.borderRightWidth = 1.5f;
            dpBrushRing.style.borderTopColor = dpBrushRing.style.borderBottomColor = dpBrushRing.style.borderLeftColor = dpBrushRing.style.borderRightColor = Color.white;
            Tk.Show(dpBrushRing, false);
            dpMap.RegisterCallback<PointerMoveEvent>(e => BrushHover(e.localPosition, true));
            dpMap.RegisterCallback<PointerLeaveEvent>(_ => BrushHover(Vector2.zero, false));
        }

        private Button BrushButton(VisualElement row, string text, System.Action action)
        {
            var b = new Button { text = text, focusable = false };
            b.AddToClassList("btn"); b.AddToClassList("btn--sm"); b.AddToClassList("bh-grow"); b.AddToClassList("dp-third");
            b.clicked += () => { if (deployment == null) return; action(); nextUiRefresh = 0; };
            row.Add(b); return b;
        }

        private float BodyRadius(WarSandboxDeploymentEntry e) =>
            e.template != null && e.template.flockingConfig != null ? Mathf.Max(.05f, e.template.flockingConfig.agentRadius) : .45f;
        private float BrushSpacing(WarSandboxDeploymentEntry e) => WarSandboxBrushMask.Spacing(e.density, BodyRadius(e));
        /// <summary>Large bodies: the brush is never narrower than one body spacing, or a stroke could hold nobody.</summary>
        private float BrushWorldRadius(WarSandboxDeploymentEntry e) => Mathf.Max(BrushRadii[Mathf.Clamp(brushSize, 0, 2)], BrushSpacing(e) * .75f);
        private float BrushYaw(int index) => WarSandboxDeploymentFacing.ResolveQuarter(deployment.Draft, index) * 90f;

        private bool BrushUsable => brushMode && deployment != null && deployment.Draft != null && deployment.Draft.Count > 0 && spatialSelection;

        private void HandleBrushKeys()
        {
            if (!brushMode || WarSandboxUGUI.IsTyping) return;
            if (deployment == null || deployment.Draft == null || deployment.Draft.Count == 0) { brushMode = false; return; }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) { brushSize = Mathf.Max(0, brushSize - 1); nextUiRefresh = 0; }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { brushSize = Mathf.Min(2, brushSize + 1); nextUiRefresh = 0; }
        }

        private void UpdateBrushControls(WarSandboxDeploymentEntry e)
        {
            if (dpBrushPaint == null) return;
            bool locked = deployment.Draft.IsLocked(selected);
            if (locked && brushMode) { brushMode = false; CancelBrushStroke(); }
            dpBrushPaint.SetEnabled(!locked); dpBrushErase.SetEnabled(!locked); dpBrushClear.SetEnabled(!locked && e.brush != null);
            DpOnState(dpBrushPaint, brushMode && !brushErase); DpOnState(dpBrushErase, brushMode && brushErase);
            for (int k = 0; k < 3; k++)
            {
                DpOnState(dpBrushSizes[k], brushSize == k);
                dpBrushSizes[k].text = BrushNames[k] + " " + (Mathf.Max(BrushRadii[k], BrushSpacing(e) * .75f) * 2).ToString("0") + "m";
            }
            string text;
            if (e.brush == null)
                text = brushMode ? "在地图上左键涂出这个编成的形状（第一笔替换矩形）\nShift 或「橡皮」擦除 · [ ] 笔刷大小 · Esc 退出" :
                    "点「画笔」后在地图上涂出任意形状\n士兵按密度从前排开始排进涂色区";
            else
            {
                int capacity = e.brush.Capacity(BrushSpacing(e));
                text = capacity >= e.count
                    ? "形状 " + e.brush.Area.ToString("N0") + " m² · 可放 " + capacity.ToString("N0") + " 人 · 已用 " + Mathf.RoundToInt(100f * e.count / Mathf.Max(1, capacity)) + "%"
                    : "容量不足：可放 " + capacity.ToString("N0") + " 人，还差 " + (e.count - capacity).ToString("N0") + " 人\n请多涂一些、提高密度或减少人数";
                text += "\n密度决定间距 · " + (brushMode ? "Alt+拖动平移整块" : "拖动色块平移整块") + " · R 旋转";
            }
            Tk.SetText(dpBrushInfo, text);
            dpBrushInfo.style.color = e.brush != null && e.brush.Capacity(BrushSpacing(e)) < e.count ? new StyleColor(new Color(.85f, .2f, .15f)) : new StyleColor(StyleKeyword.Null);
        }

        private void ClearBrushShape()
        {
            if (!CommitFields()) return;
            var draft = deployment.Draft; if (draft == null || draft.Count == 0) return;
            var original = draft[selected]; if (original.brush == null) return;
            var next = original; next.brush = null; next.manualSize = Vector3.zero;
            if (!deployment.TryEditFormation(draft, draft.Revision, selected, original, next, true, out var error)) { inputError = "无法恢复矩形：" + error; return; }
            inputError = null; RefreshAfterUiChange();
        }

        // ---------------------------------------------------------------------------------------------------------
        // Stroke
        // ---------------------------------------------------------------------------------------------------------
        private int GridIndex(float x, float z, out int gi, out int gj)
        {
            gi = Mathf.FloorToInt((x - brushOrigin.x) / Cell); gj = Mathf.FloorToInt((z - brushOrigin.y) / Cell);
            return gi >= 0 && gj >= 0 && gi < brushGW && gj < brushGH ? gj * brushGW + gi : -1;
        }

        /// <summary>conservative: every cell the rectangle touches (blocking); otherwise cells whose centre lies inside (filling).</summary>
        private void BlockRect(Rect r, bool[] grid, bool conservative)
        {
            int i0, i1, j0, j1; float ax = (r.xMin - brushOrigin.x) / Cell, bx = (r.xMax - brushOrigin.x) / Cell, az = (r.yMin - brushOrigin.y) / Cell, bz = (r.yMax - brushOrigin.y) / Cell;
            if (conservative) { i0 = Mathf.FloorToInt(ax + 1e-3f); i1 = Mathf.CeilToInt(bx - 1e-3f) - 1; j0 = Mathf.FloorToInt(az + 1e-3f); j1 = Mathf.CeilToInt(bz - 1e-3f) - 1; }
            else { i0 = Mathf.CeilToInt(ax - .5f); i1 = Mathf.FloorToInt(bx - .5f); j0 = Mathf.CeilToInt(az - .5f); j1 = Mathf.FloorToInt(bz - .5f); }
            i0 = Mathf.Max(0, i0); j0 = Mathf.Max(0, j0); i1 = Mathf.Min(brushGW - 1, i1); j1 = Mathf.Min(brushGH - 1, j1);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) grid[j * brushGW + i] = true;
        }

        /// <summary>block: every grid cell another composition touches (own army cell by cell, hostile as its rectangle).
        /// !block: the selected composition's own cells, by cell centre.</summary>
        private void StampEntry(WarSandboxDeploymentEntry e, bool[] grid, bool ownTeam, bool block)
        {
            if (e.brush == null || !ownTeam) { BlockRect(e.Bounds, grid, block); return; }
            var m = e.brush;
            for (int j = 0; j < m.height; j++)
                for (int i = 0; i < m.width; i++)
                {
                    if (!m[i, j]) continue;
                    if (block) { BlockRect(WarSandboxBrushFootprint.CellRect(e, i, j), grid, true); continue; }
                    var c = m.CellCenter(i, j);
                    int k = GridIndex(e.center.x + c.x, e.center.z + c.y, out _, out _);
                    if (k >= 0) grid[k] = true;
                }
        }

        /// <summary>Pointer down on the map. False = not a brush gesture (translation handles it).</summary>
        private bool BrushBegin(Vector2 local, bool shift, bool alt)
        {
            if (!BrushUsable || alt) return false;
            if (!MapGestureAllowed || !MapLayoutAvailable) return true;
            if (HasPendingInputs()) { inputError = "输入尚未更新：请先更新数值或按 Esc 放弃输入，再使用画笔。"; nextUiRefresh = 0; return true; }
            var draft = deployment.Draft; int index = Mathf.Clamp(selected, 0, draft.Count - 1);
            if (draft.IsLocked(index)) { inputError = WarSandboxDeploymentDraft.LockedMessage; nextUiRefresh = 0; return true; }
            Vector2 world = deployment.WorldSize;
            brushGW = Mathf.CeilToInt(world.x / Cell); brushGH = Mathf.CeilToInt(world.y / Cell); brushOrigin = -world * .5f;
            int n = brushGW * brushGH;
            if (brushGrid == null || brushGrid.Length != n) { brushGrid = new bool[n]; brushBlocked = new bool[n]; }
            else { System.Array.Clear(brushGrid, 0, n); System.Array.Clear(brushBlocked, 0, n); }

            // Blocked: boundary padding, obstacles (+clearance), other compositions (own army cell by cell, hostile as rectangles).
            var sim = deployment.controller != null && deployment.controller.manager != null && deployment.controller.manager.systemConfig != null ? deployment.controller.manager.systemConfig.simulationConfig : null;
            float pad = sim != null ? Mathf.Max(0f, sim.boundaryPadding) : 0f;
            var inner = new Rect(-world.x * .5f + pad, -world.y * .5f + pad, world.x - 2 * pad, world.y - 2 * pad);
            for (int j = 0; j < brushGH; j++)
                for (int i = 0; i < brushGW; i++)
                {
                    float x0 = brushOrigin.x + i * Cell, z0 = brushOrigin.y + j * Cell;
                    if (x0 < inner.xMin || z0 < inner.yMin || x0 + Cell > inner.xMax || z0 + Cell > inner.yMax) brushBlocked[j * brushGW + i] = true;
                }
            var rules = draft.Rules;
            if (rules.staticObstaclesEnabled && rules.staticObstacles != null)
                foreach (var o in rules.staticObstacles)
                {
                    var wall = o.Bounds; float c = rules.staticObstacleClearance;
                    BlockRect(new Rect(wall.xMin - c, wall.yMin - c, wall.width + 2 * c, wall.height + 2 * c), brushBlocked, true);
                }
            var e = draft[index];
            for (int k = 0; k < draft.Count; k++) if (k != index) StampEntry(draft[k], brushBlocked, draft[k].teamId == e.teamId, true);

            brushStrokeErase = brushErase || shift;
            brushMinI = brushGW; brushMinJ = brushGH; brushMaxI = -1; brushMaxJ = -1;
            if (e.brush != null || brushStrokeErase)
            {
                if (e.brush != null) StampEntry(e, brushGrid, true, false); else BlockRect(e.Bounds, brushGrid, false);
                for (int j = 0; j < brushGH; j++) for (int i = 0; i < brushGW; i++) if (brushGrid[j * brushGW + i]) Grow(i, j);
            }
            brushDraft = draft; brushRevision = draft.Revision; brushIndex = index; brushOriginal = e;
            brushRadius = BrushWorldRadius(e);
            brushStroke = true; brushHasLast = false;
            if (index < dpBlockPool.Count) Tk.Show(dpBlockPool[index].root, false);
            BrushMove(local);
            return true;
        }

        private void Grow(int i, int j)
        {
            if (i < brushMinI) brushMinI = i; if (j < brushMinJ) brushMinJ = j;
            if (i > brushMaxI) brushMaxI = i; if (j > brushMaxJ) brushMaxJ = j;
        }

        private void Stamp(Vector2 p)
        {
            int r = Mathf.CeilToInt(brushRadius / Cell) + 1; float rr = brushRadius * brushRadius;
            GridIndex(p.x, p.y, out int ci, out int cj);
            for (int j = Mathf.Max(0, cj - r); j <= Mathf.Min(brushGH - 1, cj + r); j++)
                for (int i = Mathf.Max(0, ci - r); i <= Mathf.Min(brushGW - 1, ci + r); i++)
                {
                    float dx = brushOrigin.x + (i + .5f) * Cell - p.x, dz = brushOrigin.y + (j + .5f) * Cell - p.y;
                    if (dx * dx + dz * dz > rr) continue;
                    int k = j * brushGW + i;
                    if (brushStrokeErase) brushGrid[k] = false;
                    else if (!brushBlocked[k] && !brushGrid[k]) { brushGrid[k] = true; Grow(i, j); }
                }
        }

        private void BrushMove(Vector2 local)
        {
            if (!brushStroke) return;
            if (!ReferenceEquals(deployment.Draft, brushDraft) || deployment.Draft.Revision != brushRevision) { CancelBrushStroke("画笔已取消：草稿已变化。"); return; }
            var w = WarSandboxMinimapProjection.MapToWorld(local, translationWorld, translationMap); var p = new Vector2(w.x, w.z);
            if (brushHasLast)
            {
                float d = Vector2.Distance(brushLast, p); int steps = Mathf.Clamp(Mathf.CeilToInt(d / Mathf.Max(.5f, brushRadius * .5f)), 1, 512);
                for (int s = 1; s <= steps; s++) Stamp(Vector2.Lerp(brushLast, p, s / (float)steps));
            }
            else Stamp(p);
            brushLast = p; brushHasLast = true;
            DrawBrushGhost();
            BrushHover(local, true);
        }

        private void DrawBrushGhost()
        {
            if (brushMaxI < brushMinI) { Tk.Show(dpBrushGhost, false); return; }
            int w = brushMaxI - brushMinI + 1, h = brushMaxJ - brushMinJ + 1;
            if (brushGhostTex == null || brushGhostTex.width != w || brushGhostTex.height != h)
            {
                if (brushGhostTex != null) Destroy(brushGhostTex);
                brushGhostTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            }
            Color team = WarSandboxTeamPalette.Resolve(brushOriginal.teamId);
            var on = (Color32)new Color(team.r, team.g, team.b, .75f); var off = new Color32(0, 0, 0, 0);
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) px[j * w + i] = brushGrid[(brushMinJ + j) * brushGW + brushMinI + i] ? on : off;
            brushGhostTex.SetPixels32(px); brushGhostTex.Apply(false);
            var rect = MapBounds(new Rect(brushOrigin.x + brushMinI * Cell, brushOrigin.y + brushMinJ * Cell, w * Cell, h * Cell), translationWorld, translationMap);
            Place(dpBrushGhost, rect); dpBrushGhost.style.backgroundImage = new StyleBackground(brushGhostTex); Tk.Show(dpBrushGhost, true);
        }

        private void BrushEnd()
        {
            if (!brushStroke) return;
            brushStroke = false; Tk.Show(dpBrushGhost, false);
            if (!ReferenceEquals(deployment.Draft, brushDraft) || deployment.Draft.Revision != brushRevision) { inputError = "画笔已取消：草稿已变化。"; nextUiRefresh = 0; return; }
            int minI = brushGW, minJ = brushGH, maxI = -1, maxJ = -1;
            for (int j = Mathf.Max(0, brushMinJ); j <= brushMaxJ; j++)
                for (int i = Mathf.Max(0, brushMinI); i <= brushMaxI; i++)
                    if (brushGrid[j * brushGW + i]) { if (i < minI) minI = i; if (i > maxI) maxI = i; if (j < minJ) minJ = j; if (j > maxJ) maxJ = j; }
            if (maxI < 0) { inputError = "形状已全部擦除，未提交：如要回到矩形，请点「恢复矩形」。"; nextUiRefresh = 0; return; }
            int w = maxI - minI + 1, h = maxJ - minJ + 1;
            if (w > WarSandboxBrushMask.MaxSide || h > WarSandboxBrushMask.MaxSide)
            { inputError = "形状太大（单边最多 " + (WarSandboxBrushMask.MaxSide * Cell).ToString("0") + " 米），未提交。"; nextUiRefresh = 0; return; }
            var bits = new bool[w * h];
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) bits[j * w + i] = brushGrid[(minJ + j) * brushGW + minI + i];
            var mask = new WarSandboxBrushMask(Cell, w, h, bits);
            var original = brushOriginal; var next = original;
            if (original.brush == null)
            {
                // The rectangle's spacing carries over: density = members per square metre of the old footprint.
                var size = original.Size; float d = original.count / Mathf.Max(.01f, size.x * size.z);
                next.density = Mathf.Clamp(float.IsNaN(d) ? original.density : d, .05f, SpawnConfig.PackingLimitPerSquareMeter);
            }
            next.brush = mask; next.manualSize = Vector3.zero;
            next.center = new Vector3(brushOrigin.x + (minI + maxI + 1) * .5f * Cell, original.center.y, brushOrigin.y + (minJ + maxJ + 1) * .5f * Cell);
            if (!deployment.TryEditFormation(brushDraft, brushRevision, brushIndex, original, next, true, out var error))
            { inputError = "画笔未提交：" + error; nextUiRefresh = 0; return; }
            inputError = null; spatialSelection = true; RefreshAfterUiChange(); nextUiRefresh = 0;
        }

        private void CancelBrushStroke(string reason = null)
        {
            if (!brushStroke) return;
            brushStroke = false; if (dpBrushGhost != null) Tk.Show(dpBrushGhost, false);
            if (reason != null) inputError = reason; nextUiRefresh = 0;
        }

        private void BrushHover(Vector2 local, bool inside)
        {
            if (dpBrushRing == null) return;
            bool show = inside && BrushUsable && deployment.Draft != null && translationMap.width > 0 && translationWorld.x > 0;
            Tk.Show(dpBrushRing, show);
            if (!show) return;
            var e = deployment.Draft[Mathf.Clamp(selected, 0, deployment.Draft.Count - 1)];
            float r = BrushWorldRadius(e) / translationWorld.x * translationMap.width;
            Place(dpBrushRing, new Rect(local.x - r, local.y - r, 2 * r, 2 * r));
            dpBrushRing.style.borderTopLeftRadius = dpBrushRing.style.borderTopRightRadius = dpBrushRing.style.borderBottomLeftRadius = dpBrushRing.style.borderBottomRightRadius = r;
            var c = brushErase ? new Color(1f, .45f, .4f) : Color.white;
            dpBrushRing.style.borderTopColor = dpBrushRing.style.borderBottomColor = dpBrushRing.style.borderLeftColor = dpBrushRing.style.borderRightColor = c;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Map look of committed shapes: painted cells faint, cells holding members strong, red when over capacity.
        // ---------------------------------------------------------------------------------------------------------
        private bool ApplyBrushLook(VisualElement block, WarSandboxDeploymentEntry e, int index)
        {
            if (e.brush == null) { block.style.backgroundImage = new StyleBackground(StyleKeyword.None); return false; }
            float spacing = BrushSpacing(e), yaw = BrushYaw(index);
            Color team = WarSandboxTeamPalette.Resolve(e.teamId);
            string key = spacing.ToString("R") + "|" + yaw + "|" + e.count + "|" + ColorUtility.ToHtmlStringRGBA(team);
            if (!brushTextures.TryGetValue(e.brush, out var cached) || cached.Key != key)
            {
                var m = e.brush;
                var tex = cached.Value != null && cached.Value.width == m.width && cached.Value.height == m.height ? cached.Value :
                    new Texture2D(m.width, m.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                if (cached.Value != null && cached.Value != tex) Destroy(cached.Value);
                var points = m.FrontFirst(spacing, yaw); bool short_ = points.Length < e.count;
                var used = new bool[m.width * m.height];
                for (int k = 0; k < Mathf.Min(e.count, points.Length); k++)
                {
                    int i = Mathf.FloorToInt(points[k].x / m.cell + m.width * .5f), j = Mathf.FloorToInt(points[k].y / m.cell + m.height * .5f);
                    if (i >= 0 && j >= 0 && i < m.width && j < m.height) used[j * m.width + i] = true;
                }
                Color baseColor = short_ ? new Color(.95f, .25f, .2f) : team;
                var faint = (Color32)new Color(baseColor.r, baseColor.g, baseColor.b, .3f);
                var strong = (Color32)new Color(baseColor.r, baseColor.g, baseColor.b, .85f);
                var px = new Color32[m.width * m.height];
                for (int j = 0; j < m.height; j++)
                    for (int i = 0; i < m.width; i++)
                        px[j * m.width + i] = !m[i, j] ? new Color32(0, 0, 0, 0) : used[j * m.width + i] ? strong : faint;
                tex.SetPixels32(px); tex.Apply(false);
                brushTextures[e.brush] = cached = new KeyValuePair<string, Texture2D>(key, tex);
            }
            block.style.backgroundColor = Color.clear;
            block.style.backgroundImage = new StyleBackground(cached.Value);
            return true;
        }

        private readonly List<WarSandboxBrushMask> brushPrune = new List<WarSandboxBrushMask>();
        private void PruneBrushTextures()
        {
            if (brushTextures.Count == 0 || deployment == null || deployment.Draft == null) return;
            brushPrune.Clear();
            foreach (var mask in brushTextures.Keys)
            {
                bool live = false;
                for (int i = 0; i < deployment.Draft.Count && !live; i++) live = ReferenceEquals(deployment.Draft[i].brush, mask);
                if (!live) brushPrune.Add(mask);
            }
            foreach (var mask in brushPrune) { var t = brushTextures[mask].Value; if (t != null) Destroy(t); brushTextures.Remove(mask); }
        }

        private void DisposeBrush()
        {
            foreach (var pair in brushTextures.Values) if (pair.Value != null) Destroy(pair.Value);
            brushTextures.Clear();
            if (brushGhostTex != null) { Destroy(brushGhostTex); brushGhostTex = null; }
            brushStroke = false; brushGrid = brushBlocked = null;
        }
    }
}
