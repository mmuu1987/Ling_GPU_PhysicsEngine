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
        // OnGUI runs at least twice per frame; rebuilding the text each call was the
        // engine's only steady-state managed allocation. Rebuild at 4 Hz instead.
        private readonly System.Text.StringBuilder textBuilder = new System.Text.StringBuilder(256);
        private readonly GUIContent cachedContent = new GUIContent();
        private float nextTextRefreshTime;
        private bool cachedOverflowAlert;

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

            // Explicit-rect GUI needs no Layout event; skipping it halves the calls.
            if (Event.current.type != EventType.Repaint)
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
            bool overflowAlert = snapshot.gridOverflowPerFrame > 0;

            if (Time.unscaledTime >= nextTextRefreshTime || overflowAlert != cachedOverflowAlert)
            {
                nextTextRefreshTime = Time.unscaledTime + 0.25f;
                cachedOverflowAlert = overflowAlert;

                float frameMs = smoothedDeltaTime * 1000f;
                float fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;

                textBuilder.Length = 0;
                textBuilder.Append("MassEngine  ").Append(frameMs.ToString("F1")).Append(" ms (").Append(fps.ToString("F0")).Append(" fps)\n");
                if (snapshot.valid)
                    textBuilder.Append("Attackers ").Append(snapshot.aliveAttackers).Append("  |  Defenders ").Append(snapshot.aliveDefenders).Append("  /  ").Append(snapshot.totalAgents).Append('\n');
                else
                    textBuilder.Append("Attackers -  |  Defenders -  (sampling...)\n");
                textBuilder.Append("Battle ").Append(snapshot.battleSeconds.ToString("F1")).Append(" s   FlowRebuilds A:").Append(snapshot.attackerFlowRebuilds).Append(" D:").Append(snapshot.defenderFlowRebuilds);
                if (overflowAlert)
                    textBuilder.Append("\n<color=#ff5050>GRID OVERFLOW: ").Append(snapshot.gridOverflowPerFrame).Append("/frame - raise maxAgentsPerCell or cellSize!</color>");
                cachedContent.text = textBuilder.ToString();
            }

            GUI.Box(new Rect(8f, 8f, 360f, overflowAlert ? 80f : 64f), GUIContent.none);
            GUI.Label(new Rect(16f, 12f, 352f, overflowAlert ? 76f : 60f), cachedContent, style);
        }
    }
}
