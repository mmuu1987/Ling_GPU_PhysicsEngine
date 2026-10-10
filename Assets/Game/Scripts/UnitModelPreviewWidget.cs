using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MassEngine.Game
{
    /// <summary>Reusable RawImage control. Deactivation releases resources; reactivation binds lazily.
    /// Only this rectangle captures drag/scroll. No battle camera or draft mutation.</summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class UnitModelPreviewWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
    {
        private UnitTypeConfig config;
        private Texture fallback;
        private float? radiusOverride;
        private UnitModelPreviewRenderer renderer;
        private RawImage image;
        private bool attempted;
        private float clock, nextRender;
        public bool IsLive => renderer != null;
        public UnitTypeConfig Config => config;
        public UnitModelPreviewRenderer Renderer => renderer;
        public string Status => IsLive ? (renderer.HasRadiusRing ? "拖动旋转 · 滚轮缩放" : "圆环不可用 · 可旋转") : fallback != null ? "静态预览 · 无半径圈" : "暂无可用模型预览";
        public void Bind(UnitTypeConfig value, Texture staticFallback, float? previewRadius = null)
        {
            if (config != value) { Release(); config = value; }
            fallback = staticFallback;
            if (radiusOverride != previewRadius) { radiusOverride = previewRadius; renderer?.SetRadiusOverride(radiusOverride); nextRender = 0; }
            if (image == null) image = GetComponent<RawImage>();
            image.raycastTarget = true;
            Ensure();
        }
        private void Ensure()
        {
            if (attempted || !isActiveAndEnabled) return;
            attempted = true;
            try { renderer = new UnitModelPreviewRenderer(config); renderer.SetRadiusOverride(radiusOverride); RenderNow(); }
            catch (Exception e) { renderer?.Dispose(); renderer = null; Debug.LogWarning("Unit preview fallback: " + e.Message); ShowFallback(); }
        }
        private void ShowFallback()
        {
            if (image == null) return;
            image.texture = fallback; image.uvRect = new Rect(0, 0, 1, 1);
            image.color = fallback != null ? Color.white : Color.clear;
        }
        private void LateUpdate()
        {
            Ensure(); if (renderer == null) return;
            clock += Time.unscaledDeltaTime;
            if (Time.unscaledTime < nextRender) return;
            nextRender = Time.unscaledTime + 1f / 30;
            try { RenderNow(); }
            catch (Exception e) { renderer.Dispose(); renderer = null; Debug.LogWarning("Unit preview render fallback: " + e.Message); ShowFallback(); }
        }
        private void RenderNow()
        {
            var rect = ((RectTransform)transform).rect;
            var canvas = GetComponentInParent<Canvas>(); float scale = canvas != null ? canvas.scaleFactor : 1;
            renderer.Render(clock, Mathf.RoundToInt(rect.width * scale), Mathf.RoundToInt(rect.height * scale));
            image.texture = renderer.Texture; image.color = Color.white;
            // No manual GPU Y conversion in the draw path: the RT is sampled as-is.
            image.uvRect = new Rect(0, 0, 1, 1);
        }
        public void ResetView() { renderer?.ResetView(); nextRender = 0; }
        public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) e.Use(); }
        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || renderer == null) return;
            var canvas = GetComponentInParent<Canvas>();
            renderer.Rotate(e.delta / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1)); nextRender = 0; e.Use();
        }
        public void OnScroll(PointerEventData e) { if (renderer == null) return; renderer.Scroll(e.scrollDelta.y); nextRender = 0; e.Use(); }
        private void Release() { renderer?.Dispose(); renderer = null; attempted = false; clock = nextRender = 0; if (image != null) image.texture = null; }
        private void OnDisable() => Release();
        private void OnDestroy() => Release();
    }
}


