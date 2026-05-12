using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class DefaultFlowFieldVisualizer : IFlowFieldVisualizer
    {
        public bool IsEnabled { get; set; }
        public FlowFieldPreviewMode PreviewMode { get; set; }

        private Vector2[] flowData;
        private Texture2D previewTexture;
        private Color[] pixels;

        public void Render(ComputeBuffer flowFieldBuffer, RenderTexture target, int resolutionX, int resolutionZ)
        {
            if (!IsEnabled || target == null || flowFieldBuffer == null)
                return;

            int width = Mathf.Max(1, resolutionX);
            int height = Mathf.Max(1, resolutionZ);
            int count = width * height;
            EnsureCpuResources(width, height, count);

            int copiedCount = Mathf.Min(count, flowFieldBuffer.count);
            flowFieldBuffer.GetData(flowData, 0, 0, copiedCount);
            for (int i = copiedCount; i < count; i++)
                flowData[i] = Vector2.zero;

            for (int i = 0; i < count; i++)
            {
                Vector2 flow = flowData[i];
                pixels[i] = PreviewMode == FlowFieldPreviewMode.FlowDirection
                    ? FlowDirectionToColor(flow)
                    : DensityToColor(flow.magnitude);
            }

            previewTexture.SetPixels(pixels);
            previewTexture.Apply(false, false);
            Graphics.Blit(previewTexture, target);
        }

        public void Release()
        {
            if (previewTexture != null)
                Object.Destroy(previewTexture);

            previewTexture = null;
            flowData = null;
            pixels = null;
        }

        private void EnsureCpuResources(int width, int height, int count)
        {
            if (flowData == null || flowData.Length != count)
                flowData = new Vector2[count];
            if (pixels == null || pixels.Length != count)
                pixels = new Color[count];
            if (previewTexture == null || previewTexture.width != width || previewTexture.height != height)
            {
                if (previewTexture != null)
                    Object.Destroy(previewTexture);

                previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                previewTexture.wrapMode = TextureWrapMode.Clamp;
                previewTexture.filterMode = FilterMode.Point;
            }
        }

        private static Color FlowDirectionToColor(Vector2 flow)
        {
            float magnitude = Mathf.Clamp01(flow.magnitude);
            if (magnitude <= 0.0001f)
                return new Color(0.03f, 0.03f, 0.03f, 1f);

            Vector2 direction = flow.normalized;
            return new Color(
                direction.x * 0.5f + 0.5f,
                direction.y * 0.5f + 0.5f,
                magnitude,
                1f);
        }

        private static Color DensityToColor(float density)
        {
            float value = Mathf.Clamp01(density);
            return Color.Lerp(new Color(0.03f, 0.03f, 0.03f, 1f), new Color(1f, 0.45f, 0.1f, 1f), value);
        }
    }
}
