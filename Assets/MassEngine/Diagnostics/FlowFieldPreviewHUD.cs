using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Draws the runtime flow field preview textures on screen (Requirement 3).
    /// The preview textures are only WRITTEN by the flow kernels while
    /// RuntimeFlowConfig.runtimeFlowPreviewEnabled is on; with the toggle off this
    /// component shows the last generated content and the GPU pays nothing.
    /// Preview mode (FlowDirection / DensityTarget) is selected in RuntimeFlowConfig.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/Flow Field Preview HUD")]
    public sealed class FlowFieldPreviewHUD : MonoBehaviour
    {
        public MassEngineManager manager;
        [Range(64, 512)] public int previewSize = 192;
        public bool showAttackerFlow = true;
        public bool showDefenderFlow;

        private void Reset()
        {
            manager = GetComponent<MassEngineManager>();
        }

        private void OnGUI()
        {
            if (manager == null || manager.Buffers == null || !manager.Buffers.IsAllocated)
                return;

            float x = Screen.width - previewSize - 8f;
            float y = 8f;

            if (showAttackerFlow && manager.Buffers.runtimeAttackerFlowPreviewTexture != null)
            {
                GUI.DrawTexture(new Rect(x, y, previewSize, previewSize), manager.Buffers.runtimeAttackerFlowPreviewTexture, ScaleMode.ScaleToFit, false);
                GUI.Label(new Rect(x, y + previewSize, previewSize, 20f), "Attacker Flow");
                y += previewSize + 26f;
            }

            if (showDefenderFlow && manager.Buffers.runtimeDefenderFlowPreviewTexture != null)
            {
                GUI.DrawTexture(new Rect(x, y, previewSize, previewSize), manager.Buffers.runtimeDefenderFlowPreviewTexture, ScaleMode.ScaleToFit, false);
                GUI.Label(new Rect(x, y + previewSize, previewSize, 20f), "Defender Flow");
            }
        }
    }
}
