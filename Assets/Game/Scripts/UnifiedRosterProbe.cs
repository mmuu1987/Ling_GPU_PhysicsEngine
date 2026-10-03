using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace MassEngine.Game
{
    public sealed class UnifiedRosterProbe : MonoBehaviour,IModelTrialPreBattleCheck
    {
        [Serializable]class Binding{public string name,profile,mesh,material;public int vertices,count,team;}
        [Serializable]class Evidence
        {
            public bool passed,policyUnchanged,rejectedUnsafeDraftWithoutGpuChange,cancelRestoredCommitted;
            public int sourceScenarioTemplates,selectableTemplates,liveTypes;public string mode;
            public List<string> uiSelected=new List<string>();public List<Binding> bindings=new List<Binding>();
            public string method="Actual uGUI template/add/undo/cancel callbacks plus runtime renderer bindings. Unsafe candidate API must reject without touching GPU/source/committed deployment. Does not inject battle results.";
        }
        public int expectedTemplates,expectedLiveTypes;public bool largeMode;public string mode;public UnitTypeConfig excludedLargeTemplate;
        static UnifiedRosterProbe instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatics()=>instance=null;
        void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--unified-roster-probe")<0){enabled=false;return;}if(instance!=null&&instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);}
        static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException("Unified roster probe: "+why);}
        static Button Button(string key){return FindObjectsByType<Button>(FindObjectsSortMode.None).SingleOrDefault(b=>b.name==key&&b.isActiveAndEnabled);}
        static IEnumerator Click(string key){float until=Time.realtimeSinceStartup+5;while(Button(key)==null&&Time.realtimeSinceStartup<until)yield return null;var b=Button(key);Require(b!=null&&b.IsInteractable(),"Button unavailable: "+key);b.onClick.Invoke();yield return new WaitForSecondsRealtime(.18f);}
        public IEnumerator Run(WarSandboxBattleController controller)
        {
            var report=new Evidence{mode=mode};var deployment=controller.GetComponent<WarSandboxRuntimeDeployment>();var manager=controller.manager;var committed=manager.scenarioConfig;var buffers=manager.Buffers;var policy=deployment.rosterPolicy;Require(policy!=null,"New scene needs explicit roster policy.");string beforePolicy=JsonUtility.ToJson(policy);string beforeScenario=JsonUtility.ToJson(committed);
            report.sourceScenarioTemplates=deployment.SourceScenario.unitTypes.Length;report.selectableTemplates=deployment.Templates.Count;report.liveTypes=manager.UnitTypes.RegisteredTypes.Count;
            Require(report.selectableTemplates==expectedTemplates&&report.liveTypes==expectedLiveTypes,"Wrong selectable/live counts.");
            foreach(var live in manager.UnitTypes.RegisteredTypes)
            {
                var render=live.RenderRuntime;Require(render!=null&&render.nearMesh!=null&&render.nearMaterial!=null&&render.nearBlock!=null&&render.nearMaterial.shader.isSupported,"Missing live model binding.");
                Require(VatProfileReader.TryRead(live.Config.renderConfig.vatProfile,out var profile,out string error),error);Require(render.nearMesh==profile.cleanMesh,"Renderer profile/mesh mismatch.");report.bindings.Add(new Binding{name=live.Config.unitTypeName,profile=live.Config.renderConfig.vatProfile.name,mesh=render.nearMesh.name,material=render.nearMaterial.name,vertices=render.nearMesh.vertexCount,count=live.UnitCount,team=live.Config.teamId});
            }
            yield return Click("edit");Require(deployment.IsEditing,"Editor did not open.");var row=deployment.Draft[0];Vector3 location=row.center;int team=row.teamId;
            for(int i=0;i<deployment.Templates.Count;i++)
            {
                var template=deployment.Templates[i];yield return Click("field-template");yield return Click("template-"+i);var selected=deployment.Draft[0];Require(selected.template==template&&selected.center==location&&selected.teamId==team,"UI selection lost role/team/position.");Require(selected.count==template.spawnConfig.unitCount&&selected.manualSize==template.spawnConfig.spawnSize,"Safe template count/footprint not adopted.");report.uiSelected.Add(template.unitTypeName);
            }
            int countBefore=deployment.Draft.Count;yield return Click("roster-add");Require(deployment.Draft.Count==countBefore+1,"Add composition failed.");var added=deployment.Draft[deployment.Draft.Count-1];Require(added.count==added.template.spawnConfig.unitCount&&added.count!=1000,"Add used legacy1000 instead of preview-safe defaults.");yield return Click("roster-undo");Require(deployment.Draft.Count==countBefore,"Add undo failed.");
            var originalDraft=deployment.Draft.Snapshot();var invalid=(WarSandboxDeploymentEntry[])originalDraft.Clone();
            if(!largeMode){invalid[0].template=excludedLargeTemplate;invalid[0].count=2;invalid[0].manualSize=new Vector3(40,0,40);}
            else{invalid[0].template=policy.templates.First(t=>t.flockingConfig.agentRadius>1);invalid[0].teamId=1;invalid[0].count=2;}
            var candidate=new WarSandboxDeploymentDraft(invalid,deployment.Draft.Rules);int revision=deployment.Draft.Revision;
            Require(!deployment.TryReplaceDraft(candidate,out string rejected)&&!string.IsNullOrEmpty(rejected),"Unsafe role/context accepted.");Require(manager.Buffers==buffers&&buffers.IsAllocated&&manager.scenarioConfig==committed&&deployment.Draft.Revision==revision,"Rejected candidate half-applied.");
            // A direct policy check distinguishes body-density/limit policy from unrelated rectangle validation.
            invalid=(WarSandboxDeploymentEntry[])originalDraft.Clone();invalid[0].count=policy.maximumUnits+1;invalid[0].manualSize=new Vector3(100,0,100);Require(!policy.TryValidate(new WarSandboxDeploymentDraft(invalid,deployment.Draft.Rules),manager.systemConfig.simulationConfig,out _),"Preview population cap was ignored.");
            if(largeMode){invalid=(WarSandboxDeploymentEntry[])originalDraft.Clone();invalid[0].template=policy.templates.First(t=>t.flockingConfig.agentRadius>1);invalid[0].teamId=0;invalid[0].count=12;invalid[0].manualSize=new Vector3(9,0,9);Require(!policy.TryValidate(new WarSandboxDeploymentDraft(invalid,deployment.Draft.Rules),manager.systemConfig.simulationConfig,out _),"Large physical packing was ignored.");}
            report.rejectedUnsafeDraftWithoutGpuChange=true;yield return Click("deployment-cancel");Require(!deployment.IsEditing&&manager.scenarioConfig==committed&&manager.Buffers==buffers&&JsonUtility.ToJson(committed)==beforeScenario,"Cancel changed committed deployment.");report.cancelRestoredCommitted=true;report.policyUnchanged=JsonUtility.ToJson(policy)==beforePolicy;Require(report.policyUnchanged,"Policy/source asset mutated.");report.passed=true;
            string key="--model-trial-output=";string output=Environment.GetCommandLineArgs().First(a=>a.StartsWith(key)).Substring(key.Length);File.WriteAllText(Path.Combine(output,"roster-evidence.json"),JsonUtility.ToJson(report,true));
        }
    }
}
