using UnityEngine;

[RequireComponent(typeof(GPUInstancingManager_Stage6))]
public sealed class Stage6BattleTelemetryHUD_Stage6 : MonoBehaviour
{
    private GPUInstancingManager_Stage6 manager;
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private GUIStyle headerStyle;
    private GUIStyle buttonStyle;
    private bool stylesInitialized;

    private const float PanelWidth = 280f;
    private const float PanelPadding = 12f;
    private const float LineHeight = 22f;

    private void Awake()
    {
        manager = GetComponent<GPUInstancingManager_Stage6>();
    }

    private bool ShouldShow()
    {
        if (manager == null)
            return false;
        if (manager.scenarioConfig != null)
            return manager.scenarioConfig.showBattleTelemetry;
        return true;
    }

    private void InitStyles()
    {
        if (stylesInitialized)
            return;
        stylesInitialized = true;

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = MakeSolidTexture(new Color(0f, 0f, 0f, 0.75f)) },
            padding = new RectOffset(10, 10, 8, 8)
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white },
            richText = true
        };

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.86f, 0.12f, 1f) }
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fixedHeight = 30f
        };
    }

    private void OnGUI()
    {
        if (!ShouldShow())
            return;

        InitStyles();

        var tel = manager.Telemetry;
        string scenarioName = manager.scenarioConfig != null ? manager.scenarioConfig.scenarioName : "No Scenario";

        float lineCount = 10f;
        float panelHeight = PanelPadding * 2f + LineHeight * lineCount + 40f;
        Rect panel = new Rect(PanelPadding, PanelPadding, PanelWidth, panelHeight);

        GUI.Box(panel, GUIContent.none, boxStyle);
        GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, panel.height - 16f));

        GUILayout.Label(scenarioName, headerStyle);
        GUILayout.Space(4f);

        string status = manager.battleStarted ? "<color=#4CFF4C>BATTLE</color>" : "<color=#AAAAAA>IDLE</color>";
        GUILayout.Label($"Status: {status}", labelStyle);

        if (tel.isValid)
        {
            string elapsed = FormatTime(tel.battleElapsedTime);
            GUILayout.Label($"Time: {elapsed}", labelStyle);
            GUILayout.Space(4f);

            GUILayout.Label($"<color=#5599FF>Attackers</color>  Alive: {tel.attackerAlive:N0} / {tel.attackerTotal:N0}  Dead: {tel.AttackerDead:N0}", labelStyle);
            float atkPct = tel.attackerTotal > 0 ? (float)tel.attackerAlive / tel.attackerTotal : 0f;
            DrawBar(atkPct, new Color(0.33f, 0.6f, 1f, 0.8f));

            GUILayout.Space(2f);

            GUILayout.Label($"<color=#FF6655>Defenders</color>  Alive: {tel.defenderAlive:N0} / {tel.defenderTotal:N0}  Dead: {tel.DefenderDead:N0}", labelStyle);
            float defPct = tel.defenderTotal > 0 ? (float)tel.defenderAlive / tel.defenderTotal : 0f;
            DrawBar(defPct, new Color(1f, 0.4f, 0.33f, 0.8f));
        }
        else
        {
            GUILayout.Label("Waiting for telemetry data...", labelStyle);
        }

        GUILayout.Space(6f);

        using (new GUILayout.HorizontalScope())
        {
            if (!manager.battleStarted)
            {
                if (GUILayout.Button("Start Battle", buttonStyle))
                    manager.StartBattle();
            }
            else
            {
                if (GUILayout.Button("Stop Battle", buttonStyle))
                    manager.StopBattle();
            }

            if (GUILayout.Button("Reset", buttonStyle, GUILayout.Width(70f)))
                manager.ResetScenario();
        }

        GUILayout.EndArea();
    }

    private void DrawBar(float ratio, Color color)
    {
        Rect barBg = GUILayoutUtility.GetRect(0f, 8f, GUILayout.ExpandWidth(true));
        GUI.DrawTexture(barBg, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.2f, 0.2f, 0.2f, 0.6f), 0f, 2f);
        if (ratio > 0f)
        {
            Rect barFill = new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(ratio), barBg.height);
            GUI.DrawTexture(barFill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, 2f);
        }
    }

    private static string FormatTime(float seconds)
    {
        if (seconds <= 0f)
            return "0:00";
        int min = Mathf.FloorToInt(seconds / 60f);
        int sec = Mathf.FloorToInt(seconds % 60f);
        return $"{min}:{sec:D2}";
    }

    private static Texture2D MakeSolidTexture(Color color)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }
}
