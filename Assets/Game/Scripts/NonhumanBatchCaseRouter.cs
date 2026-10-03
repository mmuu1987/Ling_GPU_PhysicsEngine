using System;
using System.Linq;
using UnityEngine;
namespace MassEngine.Game
{
    [DefaultExecutionOrder(-250)]
    public sealed class NonhumanBatchCaseRouter : MonoBehaviour
    {
        [Serializable]public sealed class Entry{public string key,battlefieldId,templateId;public UnitTypeConfig unit;public ScenarioConfig scenario;public int initial,saved,templates;public bool large;}
        public WarSandboxModelTrialSmoke smoke;public UnifiedRosterProbe probe;public Entry[] entries;
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--model-trial-smoke")<0)return;string k=args.FirstOrDefault(a=>a.StartsWith("--roster-case="))?.Substring("--roster-case=".Length)??"triceratops";var e=entries.SingleOrDefault(x=>x.key==k);if(e==null)throw new InvalidOperationException("Unknown nonhuman roster case: "+k);
            smoke.expectedSourceScenario=e.scenario;smoke.expectedTemplate=e.unit;smoke.battlefieldId=e.battlefieldId;smoke.templateId=e.templateId;smoke.templateRevision=1;smoke.planSlot="Nonhuman2-"+k;smoke.initialTrialCount=e.initial;smoke.savedTrialCount=e.saved;smoke.preBattleCheck=k=="warrior"?null:probe;probe.mode=k;probe.expectedTemplates=e.templates;probe.expectedLiveTypes=e.scenario.unitTypes.Length;probe.largeMode=e.large;
        }
    }
}
