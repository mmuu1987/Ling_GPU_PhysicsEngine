using System;
using System.Linq;
using UnityEngine;
namespace MassEngine.Game
{
    [DefaultExecutionOrder(-250)]
    public sealed class UnifiedRosterCaseRouter : MonoBehaviour
    {
        [Serializable]public sealed class Entry{public string key,battlefieldId,templateId;public UnitTypeConfig unit;public ScenarioConfig scenario;public int initial,saved,expectedTemplates;}
        public WarSandboxModelTrialSmoke smoke;public UnifiedRosterProbe probe;public Entry[] entries;
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--model-trial-smoke")<0)return;string key=args.FirstOrDefault(a=>a.StartsWith("--roster-case="))?.Substring("--roster-case=".Length)??"regular";var e=entries.SingleOrDefault(x=>x.key==key);if(e==null)throw new InvalidOperationException("Unknown roster case "+key);
            smoke.expectedSourceScenario=e.scenario;smoke.expectedTemplate=e.unit;smoke.battlefieldId=e.battlefieldId;smoke.templateId=e.templateId;smoke.templateRevision=1;smoke.planSlot="Unified-"+key;smoke.initialTrialCount=e.initial;smoke.savedTrialCount=e.saved;smoke.preBattleCheck=key=="warrior"?null:probe;
            probe.mode=key;probe.expectedTemplates=e.expectedTemplates;probe.expectedLiveTypes=e.scenario.unitTypes.Length;probe.largeMode=key=="large";
        }
    }
}
