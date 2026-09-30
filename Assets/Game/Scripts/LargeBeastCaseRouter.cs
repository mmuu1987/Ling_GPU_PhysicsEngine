using System;
using System.Linq;
using UnityEngine;
namespace MassEngine.Game
{
    [DefaultExecutionOrder(-250)]
    public sealed class LargeBeastCaseRouter : MonoBehaviour
    {
        [Serializable] public sealed class Entry {public string key,battlefieldId,templateId;public UnitTypeConfig unit;public ScenarioConfig scenario;public bool large;}
        public WarSandboxModelTrialSmoke smoke;public LargeBeastProbe probe;public Entry[] entries;
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--model-trial-smoke")<0)return;
            string key=args.FirstOrDefault(a=>a.StartsWith("--enemy-model="))?.Substring("--enemy-model=".Length)??"wolf";var e=entries.SingleOrDefault(x=>x.key==key);if(e==null)throw new InvalidOperationException("Unknown beast test "+key);
            smoke.expectedTemplate=e.unit;smoke.expectedSourceScenario=e.scenario;smoke.battlefieldId=e.battlefieldId;smoke.templateId=e.templateId;smoke.templateRevision=1;smoke.planSlot="LargeBeast-"+key;smoke.initialTrialCount=e.large?8:64;smoke.savedTrialCount=e.large?12:96;smoke.preBattleCheck=e.large?probe:null;
        }
    }
}
