using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class MassGpuRuntime_Stage6
{
    private const float TelemetryUpdateInterval = 0.35f;

    private float nextTelemetryUpdateTime;
    private float battleStartTime = -1f;
    private bool telemetryReadbackPending;
    private bool telemetryHudLookupDone;
    private bool telemetryHudAvailable;

    private void ResetTelemetryState()
    {
        telemetryReadbackPending = false;
        battleStartTime = -1f;
        nextTelemetryUpdateTime = 0f;

        var tel = owner.Telemetry;
        tel.isValid = false;
        tel.attackerAlive = 0;
        tel.defenderAlive = 0;
        tel.attackerTotal = 0;
        tel.defenderTotal = 0;
        tel.battleElapsedTime = 0f;
        tel.battleRunning = false;
        tel.lastUpdateTime = -1f;
    }

    private void UpdateTelemetry()
    {
        var tel = owner.Telemetry;
        tel.battleRunning = battleStarted;

        if (battleStarted && battleStartTime < 0f)
            battleStartTime = Time.time;
        else if (!battleStarted)
            battleStartTime = -1f;

        if (battleStarted && battleStartTime >= 0f)
            tel.battleElapsedTime = Time.time - battleStartTime;

        if (!ShouldSampleTelemetry() || hpBuffer == null || telemetryReadbackPending)
            return;

        if (Time.time < nextTelemetryUpdateTime)
            return;

        nextTelemetryUpdateTime = Time.time + TelemetryUpdateInterval;
        telemetryReadbackPending = true;

        int count = instanceCount;
        int atkCount = Mathf.Clamp(attackerCount, 0, count);

        var capturedHpBuffer = hpBuffer;
        AsyncGPUReadback.Request(capturedHpBuffer, count * sizeof(int), 0, request =>
        {
            telemetryReadbackPending = false;

            if (request.hasError || hpBuffer == null || hpBuffer != capturedHpBuffer)
                return;

            NativeArray<int> hp = request.GetData<int>();
            int attackerAlive = 0;
            int defenderAlive = 0;

            for (int i = 0; i < hp.Length; i++)
            {
                if (hp[i] > 0)
                {
                    if (i < atkCount)
                        attackerAlive++;
                    else
                        defenderAlive++;
                }
            }

            tel.attackerTotal = atkCount;
            tel.defenderTotal = count - atkCount;
            tel.attackerAlive = attackerAlive;
            tel.defenderAlive = defenderAlive;
            tel.lastUpdateTime = Time.time;
            tel.isValid = true;
        });
    }

    private bool ShouldSampleTelemetry()
    {
        if (owner.scenarioConfig != null && !owner.scenarioConfig.showBattleTelemetry)
            return false;

        if (!telemetryHudLookupDone)
        {
            telemetryHudAvailable = owner.GetComponent<Stage6BattleTelemetryHUD_Stage6>() != null;
            telemetryHudLookupDone = true;
        }

        return telemetryHudAvailable;
    }
}
