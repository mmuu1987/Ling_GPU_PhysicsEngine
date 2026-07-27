using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Minimal on-screen battle telemetry: alive counts per team, battle time, flow
    /// rebuild counters and smoothed frame time. Attach next to
    /// MassEngineManager; costs nothing when disabled.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/Battle Telemetry HUD")]
    public sealed class BattleTelemetryHUD : MonoBehaviour
    {
        public MassEngineManager manager;
        [Range(0.01f, 1f)] public float frameTimeSmoothing = 0.1f;

        private float smoothedDeltaTime;
        private GUIStyle style;

        private void Reset()
        {
            manager = GetComponent<MassEngineManager>();
        }

        private void Update()
        {
            smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime <= 0f ? Time.unscaledDeltaTime : smoothedDeltaTime, Time.unscaledDeltaTime, frameTimeSmoothing);
        }

        private void OnGUI()
        {
            if (manager == null || manager.Telemetry == null)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = true
                };
                style.normal.textColor = Color.white;
            }

            BattleTelemetrySnapshot snapshot = manager.Telemetry.Snapshot;
            float frameMs = smoothedDeltaTime * 1000f;
            float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;

            string text =
                "MassEngine  " + frameMs.ToString("F1") + " ms (" + fps.ToString("F0") + " fps)\n" +
                (snapshot.valid
                    ? "Attackers " + snapshot.aliveAttackers + "  |  Defenders " + snapshot.aliveDefenders + "  /  " + snapshot.totalAgents + "\n"
                    : "Attackers -  |  Defenders -  (sampling...)\n") +
                "Battle " + snapshot.battleSeconds.ToString("F1") + " s   FlowRebuilds A:" + snapshot.attackerFlowRebuilds + " D:" + snapshot.defenderFlowRebuilds +
                (snapshot.gridOverflowPerFrame > 0
                    ? "\n<color=#ff5050>GRID OVERFLOW: " + snapshot.gridOverflowPerFrame + "/frame - raise maxAgentsPerCell or cellSize!</color>"
                    : "");

            bool overflowAlert = snapshot.gridOverflowPerFrame > 0;
            GUI.Box(new Rect(8f, 8f, 360f, overflowAlert ? 80f : 64f), GUIContent.none);
            GUI.Label(new Rect(16f, 12f, 352f, overflowAlert ? 76f : 60f), text, style);
        }
    }
}
