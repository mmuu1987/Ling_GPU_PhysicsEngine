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
        /// <summary>
        /// Teams whose preview to stack down the right edge, drawn in this order. Replaced the
        /// attacker/defender pair of toggles now that every team owns a slice of the flow field;
        /// the default is team 0 only, which is what those toggles defaulted to.
        /// </summary>
        public int[] previewTeamIds = { 0 };

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

            if (previewTeamIds == null)
                return;

            for (int i = 0; i < previewTeamIds.Length; i++)
            {
                int teamId = previewTeamIds[i];
                // Null means the team has no slice (id out of range for this scenario). Skipping
                // is deliberate: falling back to team 0's texture would label another team's field.
                RenderTexture preview = manager.Buffers.GetFlowPreviewTexture(teamId);
                if (preview == null)
                    continue;

                GUI.DrawTexture(new Rect(x, y, previewSize, previewSize), preview, ScaleMode.ScaleToFit, false);
                GUI.Label(new Rect(x, y + previewSize, previewSize, 20f), "Team " + teamId + " Flow");
                y += previewSize + 26f;
            }
        }
    }
}
