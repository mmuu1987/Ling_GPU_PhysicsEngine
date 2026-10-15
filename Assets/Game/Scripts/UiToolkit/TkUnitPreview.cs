using System;
using UnityEngine;
using UnityEngine.UIElements;
using Tk = MassEngine.Game.Ui.WarSandboxUiToolkit;

namespace MassEngine.Game
{
    /// <summary>
    /// UI Toolkit twin of UnitModelPreviewWidget: the same UnitModelPreviewRenderer drawn into a RenderTexture shown as the
    /// background of a view element; left drag rotates, the wheel zooms, 复位 resets. Resources are released whenever the
    /// owner deactivates it (page hidden) and rebuilt lazily, like the widget's OnDisable.
    /// </summary>
    internal sealed class TkUnitPreview : VisualElement
    {
        private readonly VisualElement view;
        private readonly Label status;
        private readonly Button reset;
        private UnitTypeConfig config;
        private Texture fallback;
        private float? radius;
        private UnitModelPreviewRenderer renderer;
        private RenderTexture shown;
        private bool attempted, active;
        private float clock, nextRender, lastTick = -1;

        public TkUnitPreview()
        {
            AddToClassList("upv");
            view = Tk.E("upv__view");
            var bar = Tk.E("upv__bar");
            status = Tk.L("", "upv__status");
            reset = new Button { text = "复位", focusable = false };
            reset.AddToClassList("btn"); reset.AddToClassList("btn--sm"); reset.AddToClassList("upv__reset");
            reset.clicked += () => { renderer?.ResetView(); nextRender = 0; };
            bar.Add(status); bar.Add(reset);
            Add(view); Add(bar);
            view.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && renderer != null) { view.CapturePointer(e.pointerId); e.StopPropagation(); } });
            view.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (renderer == null || !view.HasPointerCapture(e.pointerId)) return;
                renderer.Rotate(new Vector2(e.deltaPosition.x, -e.deltaPosition.y)); nextRender = 0;
            });
            view.RegisterCallback<PointerUpEvent>(e => { if (view.HasPointerCapture(e.pointerId)) view.ReleasePointer(e.pointerId); });
            view.RegisterCallback<WheelEvent>(e =>
            {
                if (renderer == null || Mathf.Approximately(e.delta.y, 0)) return;
                renderer.Scroll(-Mathf.Sign(e.delta.y)); nextRender = 0; e.StopPropagation();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => Deactivate());
            schedule.Execute(Tick).Every(15);
        }

        public void Bind(UnitTypeConfig value, Texture staticFallback, float? previewRadius)
        {
            active = true;
            if (config != value) { Release(); config = value; }
            if (fallback != staticFallback) { fallback = staticFallback; if (attempted && renderer == null) ShowFallback(); }
            if (radius != previewRadius) { radius = previewRadius; renderer?.SetRadiusOverride(radius); nextRender = 0; }
        }

        public void Deactivate() { if (!active && renderer == null && !attempted) return; active = false; Release(); }

        private void Release()
        {
            renderer?.Dispose(); renderer = null; attempted = false; clock = nextRender = 0; lastTick = -1; shown = null;
            view.style.backgroundImage = new StyleBackground(StyleKeyword.None);
        }

        private void ShowFallback()
        {
            if (fallback is Texture2D t2) view.style.backgroundImage = new StyleBackground(t2);
            else if (fallback is RenderTexture rt) view.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            else view.style.backgroundImage = new StyleBackground(StyleKeyword.None);
        }

        private void Tick()
        {
            if (!active || config == null || panel == null) return;
            if (!attempted)
            {
                attempted = true;
                try { renderer = new UnitModelPreviewRenderer(config); renderer.SetRadiusOverride(radius); }
                catch (Exception e) { renderer?.Dispose(); renderer = null; Debug.LogWarning("Unit preview fallback: " + e.Message); ShowFallback(); }
            }
            Tk.SetText(status, renderer != null ? (renderer.HasRadiusRing ? "拖动旋转 · 滚轮缩放" : "圆环不可用 · 可旋转") : fallback != null ? "静态预览 · 无半径圈" : "暂无可用模型预览");
            reset.SetEnabled(renderer != null);
            if (renderer == null) return;
            float now = Time.unscaledTime;
            if (lastTick >= 0) clock += now - lastTick;
            lastTick = now;
            if (now < nextRender) return;
            var r = view.contentRect; float s = Tk.Scale;
            int w = Mathf.RoundToInt(r.width * s), h = Mathf.RoundToInt(r.height * s);
            if (w < 16 || h < 16) return;
            nextRender = now + 1f / 30;
            try { renderer.Render(clock, w, h); }
            catch (Exception e) { renderer.Dispose(); renderer = null; Debug.LogWarning("Unit preview render fallback: " + e.Message); ShowFallback(); return; }
            if (shown != renderer.Texture) { shown = renderer.Texture; view.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(shown)); }
            view.MarkDirtyRepaint();
        }
    }
}
