using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using MassEngine.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>One explicit human-approved admission; no rebake, no old content edits, no automatic art approval.</summary>
    public static class RobotExpressiveAdmissionBuilder
    {
        public const string Root="Assets/Game/Content/Characters/OfficialRoster/Version08";
        public const string Previous="Assets/Game/Content/Characters/OfficialRoster/Version07";
        public const string CatalogPath=Root+"/Catalog.asset";
        public const string MenuScene=Root+"/LaunchMenu.unity";
        public const string BattleScene=Root+"/RobotBattlefield.unity";
        public const string UnitPath=Root+"/Robot/Robot.asset";
        public const string ScenarioPath=Root+"/Robot/Scenario.asset";
        public const string PolicyPath=Root+"/Robot/RosterPolicy.asset";
        public const string TemplateId="roster-robot-expressive";
        public const string BattlefieldId="robot-expressive";
        public const string OpponentId="enemy-collection-knight-control";
        public const string PortraitPath=Root+"/UnitPreviews/roster-robot-expressive.png";
        public const string OverviewPath=Root+"/Previews/robot-expressive.png";
        public const string Output="Builds/OfficialRoster-20261002-04";
        public const string UserApproval="挺好的，就这样吧，我觉得可以转入到正式角色中";
        [Serializable] public sealed class AdmissionReport
        {
            public string date="2026-10-02",userMessage=UserApproval,approvalScope="User-approved robot model presentation and formal-character admission only; not manual gameplay, balance, performance or V1 acceptance.";
            public bool userVisualApproval=true,formalAdmissionAuthorized=true,manualPlaytestPerformed=false,balanceAccepted=false,performanceAccepted=false,rebaked=false;
            public bool technicalPrepared,overviewAttached;
            public string formalCatalog=CatalogPath,unit=UnitPath,templateId=TemplateId,battlefieldId=BattlefieldId,sourceSha256=RobotExpressiveBuilder.SourceSha;
            public string nativeEvidence="Logs/AgentRobotExpressive20261002/import-04/native-report.json",preparationEvidence="Logs/AgentRobotExpressive20261002/prepare-04/preparation-report.json",captureEvidence="Logs/AgentRobotExpressive20261002/capture-03/capture-report.json";
            public int identityBattlefields,selectableBattlefields,identityTemplates,selectableTemplates;
            public string faceScope="Neutral-face four-state VAT derivative; all14 native clips and all3 authored face targets remain preserved in the original/native source.";
            public string materialScope="Source solid-colour palette in the existing GPU shader, not full originating PBR recreation.";
        }
        static string Evidence()
        {var p=Environment.GetEnvironmentVariable("ROBOT_FORMAL_OUTPUT");Require(!string.IsNullOrWhiteSpace(p),"Fresh protected runner required");Directory.CreateDirectory(p);return p;}
        static void CheckEvidence()
        {
            Require(RobotExpressiveBuilder.ValidatePinnedSource(),"Pinned source invalid");
            foreach(var path in new[]{"Logs/AgentRobotExpressive20261002/import-04/native-report.json","Logs/AgentRobotExpressive20261002/prepare-04/preparation-report.json","Logs/AgentRobotExpressive20261002/capture-03/capture-report.json","Logs/AgentRobotExpressive20261002/tests-03/receipt.json","Logs/AgentRobotExpressive20261002/player-02/receipt.json"})
                Require(File.Exists(path)&&File.ReadAllText(path).Contains("\"passed\": true")||File.Exists(path)&&File.ReadAllText(path).Contains("\"passed\":true"),"Missing successful robot evidence: "+path);
            var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(RobotExpressiveBuilder.Output+"/PipelineReport.json"));
            Require(r.automatedPassed&&r.maxPositionError<=r.allowedPositionError&&r.fullVertices==7214&&r.totalFrames==146,"Robot VAT gate not satisfied");
        }
        public static bool ValidateFormalBinding()
        {
            var unit=Load<UnitTypeConfig>(UnitPath);var v=UnitTypeBinder.ValidateBinding(unit);Require(v.IsValid,string.Join("\n",v.Errors));
            var source=Load<UnitTypeConfig>(RobotExpressiveBuilder.Output+"/Unit.asset");
            Require(unit.renderConfig.vatProfile==source.renderConfig.vatProfile&&unit.renderConfig.nearMesh==source.renderConfig.nearMesh&&unit.renderConfig.nearMaterial==source.renderConfig.nearMaterial,"Do not substitute a different render binding after visual approval");return true;
        }
        [MenuItem("MassEngine/Official Roster/Prepare Robot Version08 (fresh only)")]
        public static void Prepare08()
        {
            CheckEvidence();string evidence=Evidence();Require(!Directory.Exists(Root)&&!File.Exists(Root+".meta"),"Never overwrite formal/history outputs");
            var previous=Load<WarSandboxBattlefieldCatalog>(Previous+"/Catalog.asset");
            Require(previous.entries.Length==30&&previous.SelectableEntryCount==28&&previous.templates.Length==67&&previous.templates.Count(t=>!t.hiddenFromSelection)==65,"Version07 baseline changed");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                CharacterPipeline.EnsureFolder(Root);CharacterPipeline.EnsureFolder(Root+"/Robot");CharacterPipeline.EnsureFolder(Root+"/UnitPreviews");CharacterPipeline.EnsureFolder(Root+"/Previews");
                var source=Load<UnitTypeConfig>(RobotExpressiveBuilder.Output+"/Unit.asset");
                var robot=CharacterPipeline.CloneUnit(source,Root+"/Robot","Robot");robot.unitTypeName="机器人 · 近战";CharacterPipeline.Save(robot);
                Require(robot.teamId==0&&robot.spawnConfig.unitCount==64,"Keep the approved robot defaults");
                WritePortrait(File.ReadAllBytes(RobotExpressiveBuilder.Output+"/Trial/RobotPreview02.png"));
                var catalog=ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();catalog.defaultEntryId=previous.defaultEntryId;
                catalog.entries=previous.entries.Select(e=>e.CopyIdentity()).ToArray();
                catalog.templates=previous.templates.Select(t=>new WarSandboxUnitTemplateEntry{templateId=t.templateId,revision=t.revision,config=t.config,hiddenFromSelection=t.hiddenFromSelection,selectionNote=t.selectionNote,unitPreview=t.unitPreview}).Concat(new[]{new WarSandboxUnitTemplateEntry{templateId=TemplateId,revision=1,config=robot,unitPreview=Load<Texture2D>(PortraitPath)}}).ToArray();
                var opponent=catalog.FindTemplate(OpponentId).config;Require(opponent.teamId==1&&opponent.spawnConfig.unitCount==64,"Wrong existing knight control");
                var rules=Load<WarSandboxBattlefieldCatalog>(RobotExpressiveBuilder.Output+"/Trial/Catalog.asset").entries[0].rules;
                catalog.entries=catalog.entries.Concat(new[]{new WarSandboxBattlefieldEntry{id=BattlefieldId,displayName="机器人军团",scenePath=BattleScene,contentVersion=1,terrainId="flat-ground",terrainVersion=1,rules=rules,
                    description="2 支军团 · 128 人 · 歼灭战\n机器人以原生步行接敌、出拳近战；可在配兵布阵中与常规兵种自由组合，不新增特殊技能。",
                    briefing="目标：消灭另一支军团。\nEnter 开战，Space 暂停；配兵布阵可调整机器人和常规兵种，保存自己的方案。",featuredTemplateIds=new[]{TemplateId,OpponentId}}}).ToArray();
                AssetDatabase.CreateAsset(catalog,CatalogPath);CharacterPipeline.Save(catalog);
                var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{robot,opponent};AssetDatabase.CreateAsset(scenario,ScenarioPath);
                var regular=catalog.entries.Single(e=>e.id=="unified-regular").featuredTemplateIds.Select(id=>catalog.FindTemplate(id).config);
                var policy=ScriptableObject.CreateInstance<WarSandboxRosterPolicy>();policy.templates=regular.Concat(new[]{robot,opponent}).Distinct().ToArray();policy.maximumUnits=256;policy.maximumRadius=.8f;policy.useTemplateSpawnDefaults=true;
                policy.explanation="机器人及既有常规角色的近战编成场；不添加特殊技能或宣称大规模性能。";AssetDatabase.CreateAsset(policy,PolicyPath);Require(policy.TryValidateDefinition(out string policyError),policyError);
                Require(AssetDatabase.CopyAsset(RobotExpressiveBuilder.Output+"/Trial/Battlefield.unity",BattleScene),"Cannot create fresh robot battlefield");
                var scene=EditorSceneManager.OpenScene(BattleScene,OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();Require(manager!=null,"No scene manager");manager.scenarioConfig=Load<ScenarioConfig>(ScenarioPath);manager.battleStarted=false;
                var controller=WarSandboxRuntimeBootstrap.EnsureControls(manager);var deployment=manager.GetComponent<WarSandboxRuntimeDeployment>();deployment.battlefieldCatalog=Load<WarSandboxBattlefieldCatalog>(CatalogPath);deployment.rosterPolicy=Load<WarSandboxRosterPolicy>(PolicyPath);
                foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if(b==null)continue;var s=new SerializedObject(b);var iterator=s.GetIterator();bool enter=true;
                    while(iterator.NextVisible(enter)){enter=false;if(iterator.propertyType==SerializedPropertyType.String&&(iterator.stringValue??"").Contains("Knight pilot")){iterator.stringValue="RobotExpressive / Quaternius (CC0) · 机器人军团 / KayKit 对照骑士 · 非性能验收";s.ApplyModifiedPropertiesWithoutUndo();}}
                }
                foreach(var t in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))if((t.text??"").Contains("Knight pilot"))t.text="RobotExpressive / Quaternius (CC0) · 机器人军团";
                EditorSceneManager.SaveScene(scene);
                Require(AssetDatabase.CopyAsset(Previous+"/LaunchMenu.unity",MenuScene),"Cannot create fresh current menu");
                scene=EditorSceneManager.OpenScene(MenuScene,OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();Require(session!=null,"No menu session");session.catalog=Load<WarSandboxBattlefieldCatalog>(CatalogPath);
                foreach(var cycle in Object.FindObjectsByType<WarSandboxTerrainCycle>(FindObjectsSortMode.None))cycle.session=session;
                var smoke=new GameObject("Opt-in formal robot workflow verification").AddComponent<WarSandboxModelTrialSmoke>();smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedSourceScenario=Load<ScenarioConfig>(ScenarioPath);smoke.expectedTemplate=Load<UnitTypeConfig>(UnitPath);smoke.battlefieldId=BattlefieldId;smoke.templateId=TemplateId;smoke.templateRevision=1;smoke.planSlot="RobotExpressiveFormal01";
                EditorSceneManager.SaveScene(scene);EditorBuildSettings.scenes=Scenes(session.catalog).Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
                var saved=Load<WarSandboxBattlefieldCatalog>(CatalogPath);Require(saved.TryValidate(File.Exists,out string error)&&saved.TryValidateTemplates(out error),error);ValidateFormalBinding();
                var report=new AdmissionReport{technicalPrepared=true,identityBattlefields=saved.entries.Length,selectableBattlefields=saved.SelectableEntryCount,identityTemplates=saved.templates.Length,selectableTemplates=saved.templates.Count(t=>!t.hiddenFromSelection)};
                WriteNew(evidence+"/admission-report.json",JsonUtility.ToJson(report,true));WriteNew(Root+"/Admission.json",JsonUtility.ToJson(report,true));
                Debug.Log("ROBOT_FORMAL_PREPARED " + Root);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        public static void AttachOverview08()
        {
            var evidence=Evidence();string from="Logs/AgentRobotFormal20261002/ui-01/robot-setup.png";Require(File.Exists(from)&&!File.Exists(OverviewPath),"Need a fresh actual formal-scene overview");
            File.Copy(from,OverviewPath);ImportImage(OverviewPath,1024);var catalog=Load<WarSandboxBattlefieldCatalog>(CatalogPath);catalog.entries.Single(e=>e.id==BattlefieldId).preview=Load<Texture2D>(OverviewPath);CharacterPipeline.Save(catalog);
            WriteNew(evidence+"/overview-receipt.json","{\"passed\":true,\"source\":\""+from+"\",\"scene\":\""+BattleScene+"\",\"noGeneratedArt\":true}");Debug.Log("ROBOT_FORMAL_OVERVIEW_READY");
        }
        public static void RepairOverview08()=>RobotExpressiveOverviewFix.AttachActualPlayer08();
        [MenuItem("MassEngine/Official Roster/Build Robot Version08 (fresh package)")]
        public static void Build08()
        {
            string evidence=Evidence();Require(!Directory.Exists(Output)&&!File.Exists(Output),"Never overwrite a historical package");var catalog=Load<WarSandboxBattlefieldCatalog>(CatalogPath);ValidateFormalBinding();
            Require(catalog.TryValidate(File.Exists,out string error)&&catalog.TryValidateTemplates(out error),error);Require(catalog.entries.Where(e=>!e.hiddenFromSelection).All(e=>e.preview!=null),"Actual battlefield overviews required");Require(catalog.templates.Where(t=>!t.hiddenFromSelection).All(t=>t.unitPreview!=null),"Actual unit portraits required");
            Directory.CreateDirectory(Output);var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=Scenes(catalog),target=BuildTarget.StandaloneWindows64,locationPathName=Output+"/WarSandbox.exe",options=BuildOptions.Development});Require(report.summary.result==BuildResult.Succeeded,"Formal Windows build failed");
            WriteNew(Output+"/Start-WarSandbox.cmd","@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n");
            WriteNew(Output+"/说明.txt","Version08：机器人正式入列。用户认可模型表现并授权入列；人工试玩仍暂停，非平衡、性能或 V1 签收。\r\n正式兵种 66 个、可选战场 29 个；旧身份与旧资源保留。机器人军团及兵种库可查看真实单体图；原生四动作、中性脸 VAT，无新增特殊技能。\r\n");
            WriteNew(evidence+"/build-report.json","{\"passed\":true,\"buildGuid\":\""+report.summary.guid+"\",\"package\":\""+Output+"\",\"formalRobotAdmitted\":true,\"manualPlaytestPerformed\":false}");Debug.Log("ROBOT_FORMAL_BUILD_READY "+Output);
        }
        public static string[] Scenes(WarSandboxBattlefieldCatalog catalog)=>new[]{MenuScene}.Concat(catalog.entries.Select(e=>e.scenePath)).Distinct().ToArray();
        static void WritePortrait(byte[] bytes)
        {
            var source=new Texture2D(2,2,TextureFormat.RGBA32,false);Texture2D target=null;
            try
            {
                Require(source.LoadImage(bytes),"Unreadable approved portrait");var pixels=source.GetPixels32();int x0=source.width,y0=source.height,x1=-1,y1=-1;
                for(int y=0;y<source.height;y++)for(int x=0;x<source.width;x++)if(pixels[y*source.width+x].a>=128){x0=Mathf.Min(x0,x);y0=Mathf.Min(y0,y);x1=Mathf.Max(x1,x);y1=Mathf.Max(y1,y);}
                Require(x0>=3&&y0>=3&&x1<source.width-3&&y1<source.height-3,"Cannot crop model parts for a portrait");
                const int w=448,h=560,pad=28;int pw=x1-x0+1,ph=y1-y0+1;float factor=Mathf.Min((w-2f*pad)/pw,(h-2f*pad)/ph);int tw=Mathf.RoundToInt(pw*factor),th=Mathf.RoundToInt(ph*factor),left=(w-tw)/2,bottom=(h-th)/2;
                var canvas=new Color32[w*h];for(int y=0;y<th;y++)for(int x=0;x<tw;x++){int sx=x0+Mathf.Min(pw-1,Mathf.FloorToInt(x*pw/(float)tw)),sy=y0+Mathf.Min(ph-1,Mathf.FloorToInt(y*ph/(float)th));canvas[(bottom+y)*w+left+x]=pixels[sy*source.width+sx];}
                target=new Texture2D(w,h,TextureFormat.RGBA32,false);target.SetPixels32(canvas);target.Apply();using(var f=new FileStream(PortraitPath,FileMode.CreateNew)){var data=target.EncodeToPNG();f.Write(data,0,data.Length);}ImportImage(PortraitPath,1024);
            }
            finally{Object.DestroyImmediate(source);if(target!=null)Object.DestroyImmediate(target);}
        }
        static void ImportImage(string path,int max){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var i=(TextureImporter)AssetImporter.GetAtPath(path);i.mipmapEnabled=false;i.maxTextureSize=max;i.textureCompression=TextureImporterCompression.Uncompressed;i.npotScale=TextureImporterNPOTScale.None;i.alphaIsTransparency=true;i.SaveAndReimport();}
        static T Load<T>(string path)where T:Object=>AssetDatabase.LoadAssetAtPath<T>(path)??throw new InvalidOperationException("Missing "+path);
        static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static void WriteNew(string path,string text){using(var f=new FileStream(path,FileMode.CreateNew))using(var w=new StreamWriter(f,new UTF8Encoding(false)))w.Write(text);}
    }
}
