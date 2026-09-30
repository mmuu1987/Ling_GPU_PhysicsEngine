using System;
using System.Linq;
using UnityEngine;
namespace MassEngine.Game
{
    // Only the isolated enemy collection menu uses this. Reuses the established smoke, no combat logic.
    [DefaultExecutionOrder(-250)]
    public sealed class EnemyBatchRuntime : MonoBehaviour
    {
        [Serializable] public sealed class Entry { public string key,battlefieldId,templateId;public UnitTypeConfig unit;public ScenarioConfig scenario; }
        public WarSandboxModelTrialSmoke smoke;
        public Entry[] entries;
        void Awake()
        {
            var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--model-trial-smoke")<0)return;
            string key=args.FirstOrDefault(a=>a.StartsWith("--enemy-model="))?.Substring("--enemy-model=".Length)??"warrior";
            var entry=entries.SingleOrDefault(e=>e.key==key);if(entry==null)throw new InvalidOperationException("Unknown enemy model test key: "+key);
            smoke.expectedSourceScenario=entry.scenario;smoke.expectedTemplate=entry.unit;smoke.battlefieldId=entry.battlefieldId;smoke.templateId=entry.templateId;smoke.templateRevision=1;smoke.planSlot="EnemyCollection-"+key;
        }
    }
}
