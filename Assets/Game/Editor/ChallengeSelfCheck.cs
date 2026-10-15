using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using MassEngine.Game;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>挑战模式 第1期 self-check (2026-10-14): draft lock, rules validation, plan round trip, controller gate.
    /// Pure edit-mode logic, no scene or Play needed. Writes Logs/Chal/selfcheck.txt.</summary>
    public static class ChallengeSelfCheck
    {
        [MenuItem("Tools/挑战模式/逻辑自检（不进 Play）")]
        public static void Run()
        {
            var log = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string name, bool ok) { log.AppendLine((ok ? "PASS " : "FAIL ") + name); if (ok) pass++; else fail++; }
            var a = ScriptableObject.CreateInstance<UnitTypeConfig>();
            var b = ScriptableObject.CreateInstance<UnitTypeConfig>();
            var go = new GameObject("ChallengeSelfCheck");
            try
            {
                var rules = WarSandboxBattlefieldRules.Default;
                Check("default off", !rules.challengeEnabled && !rules.Locks(0) && !rules.Locks(1));
                var e0 = new WarSandboxDeploymentEntry { template = a, teamId = 0, count = 100, center = new Vector3(-50, 0, 0), density = 1, aspect = 1 };
                var e1 = new WarSandboxDeploymentEntry { template = b, teamId = 1, count = 100, center = new Vector3(50, 0, 0), density = 1, aspect = 1 };
                var draft = new WarSandboxDeploymentDraft(new[] { e0, e1 }, rules);
                Check("SetChallenge on", draft.SetChallenge(true, 0));
                Check("locked enemy / own free", draft.IsLocked(1) && !draft.IsLocked(0) && draft.IsTeamLocked(1) && !draft.IsTeamLocked(0));
                var m1 = e1; m1.count = 200; var m0 = e0; m0.count = 200;
                Check("Set enemy rejected", !draft.Set(1, m1));
                Check("Set own accepted", draft.Set(0, m0));
                var mv = draft[0]; mv.teamId = 1;
                Check("move own formation into enemy rejected", !draft.Set(0, mv));
                Check("Remove enemy rejected", !draft.Remove(1));
                Check("RemoveArmy enemy rejected", !draft.RemoveArmy(1));
                Check("rules copy carries challenge", draft.Rules.challengeEnabled && draft.Rules.challengeTeam == 0);
                Check("cycle to team 1", draft.SetChallenge(true, 1) && draft.IsLocked(0) && !draft.IsLocked(1));
                Check("off", draft.SetChallenge(false, 0) && !draft.IsLocked(0) && !draft.IsLocked(1));
                Check("undo restores team 1", draft.Undo() && draft.Rules.challengeEnabled && draft.Rules.challengeTeam == 1);
                Check("enemy edits allowed after off", draft.Redo() && draft.Set(1, m1));

                var missing = WarSandboxBattlefieldRules.Default; missing.challengeEnabled = true; missing.challengeTeam = 5;
                var d2 = new WarSandboxDeploymentDraft(new[] { e0, e1 }, missing);
                bool v = d2.TryValidate(new Vector2(1000, 1000), d2.Rules, out string err, false);
                Check("validate: commanded army missing -> " + err, !v && err != null && err.Contains("挑战模式"));
                var bad = WarSandboxBattlefieldRules.Default; bad.challengeEnabled = true; bad.challengeTeam = -1;
                Check("rules validate rejects team -1", !bad.TryValidate(out _));

                var on = WarSandboxBattlefieldRules.Default; on.challengeEnabled = true; on.challengeTeam = 1;
                var pr = WarSandboxPlanRules.From(on);
                Check("plan round trip", pr.TryToRuntime(out var back, out string perr) && back.challengeEnabled && back.challengeTeam == 1);
                string json;
                using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(WarSandboxPlanRules)).WriteObject(ms, pr); json = Encoding.UTF8.GetString(ms.ToArray()); }
                Check("json has fields", json.Contains("\"challengeEnabled\":true") && json.Contains("\"challengeTeam\":1"));
                string old = json.Replace(",\"challengeEnabled\":true", "").Replace("\"challengeEnabled\":true,", "")
                                 .Replace(",\"challengeTeam\":1", "").Replace("\"challengeTeam\":1,", "");
                Check("old json has no challenge fields", !old.Contains("challenge"));
                WarSandboxPlanRules oldRules;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(old))) oldRules = (WarSandboxPlanRules)new DataContractJsonSerializer(typeof(WarSandboxPlanRules)).ReadObject(ms);
                Check("old plan loads as free", oldRules.TryToRuntime(out var oldBack, out _) && !oldBack.challengeEnabled);

                var c = go.AddComponent<WarSandboxBattleController>();
                Check("controller free by default", c.CanCommandTeam(0) && c.CanCommandTeam(1) && !c.ChallengeActive);
                c.challengeEnabled = true; c.challengeTeam = 1;
                Check("controller gate", !c.CanCommandTeam(0) && c.CanCommandTeam(1) && !c.CanCommandTeam(0, out string gerr) && gerr.Contains("守方"));
                var cap = c.CaptureBattlefieldRules();
                Check("controller capture carries challenge", cap.challengeEnabled && cap.challengeTeam == 1);
            }
            catch (Exception ex) { log.AppendLine("EXCEPTION " + ex); fail++; }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
            }
            log.Insert(0, "ChallengeSelfCheck " + DateTime.Now.ToString("s") + " pass=" + pass + " fail=" + fail + "\n");
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText("Logs/Chal/selfcheck.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log("[ChallengeSelfCheck] pass=" + pass + " fail=" + fail);
        }
    }
}
