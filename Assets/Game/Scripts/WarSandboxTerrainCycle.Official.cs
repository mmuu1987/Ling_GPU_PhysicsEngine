#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MassEngine.Game
{
    /// <summary>
    /// Opt-in "official-catalog" functional smoke for the official game build (OfficialRosterBuilder): the normal first
    /// launch must auto-enter launch-open, then every catalog card is entered through its uGUI button in order, checked at
    /// full strength with its briefing and preview, started, captured, reset and left with GPU/scene resources released.
    /// 30 FPS cap, isolated plans and settings; never performance or human-acceptance evidence.
    /// </summary>
    public sealed partial class WarSandboxTerrainCycle
    {
        private bool IsOfficialRun => report.phase == "official-catalog";

        private IEnumerator RunOfficialCatalog()
        {
            var audio = WarSandboxAudio.Ensure();
            Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Wrong isolated settings.");
            audio.SetMuted(true); // In-memory only; leave player preferences untouched.
            report.actions.Add("Functional smoke: 30 FPS cap; every official battlefield entered via its catalog card; no performance claim.");
            var entries = session.catalog.entries;
            Require(entries.Length > 5 && session.catalog.defaultEntryId == "launch-open" && entries[0].id == "launch-open", "Wrong official catalog.");
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == "launch-open", "First launch did not enter the quick battle.");
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                Stage("official-" + entry.id);
                if (i > 0)
                {
                    yield return Click("card-" + i); // B1 catalog: select the row, then deploy
                    yield return Click("card-" + i + "-enter");
                    yield return WaitForLoad(WarSandboxEntryState.Battle);
                }
                Require(session.CurrentEntryId == entry.id, "Menu entered the wrong battlefield: " + entry.id);
                Require(entry.preview != null, "Catalog preview is absent: " + entry.id);
                BindBattle(entry.terrainSurface != null);
                VerifyFullStrength();
                yield return new WaitForSecondsRealtime(.25f);
                Require(FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.transform.parent.name == "preset-briefing" && t.text == entry.briefing),
                    "Battlefield briefing is absent from Setup: " + entry.id);
                yield return CaptureWorld(entry.id + "-setup.png");
                int population = manager.UnitTypes.TotalAgentCount;
                yield return Click("start");
                yield return new WaitForSecondsRealtime(population <= 5000 ? 6f : 1f);
                Require(manager.IsBattleRunning || controller.BattleResult.valid, "Battlefield did not start: " + entry.id);
                if (population <= 5000) yield return CaptureWorld(entry.id + "-battle.png");
                yield return Click("reset"); VerifyFullStrength();
                yield return Leave(false);
                VerifySources();
                if (i == 0)
                {
                    yield return new WaitForSecondsRealtime(.2f);
                    renderCamera = FindFirstObjectByType<Camera>(); Require(renderCamera != null, "Menu camera absent.");
                    CreateCaptureTarget();
                    yield return CapturePresetUI("catalog-ui.png"); ReleaseCamera();
                }
            }
            Require(!File.Exists(settingsFile), "Smoke test wrote settings.");
            report.actions.Add("Official catalog entered: " + entries.Length + " battlefields.");
            Stage("complete");
        }
    }
}
#endif
