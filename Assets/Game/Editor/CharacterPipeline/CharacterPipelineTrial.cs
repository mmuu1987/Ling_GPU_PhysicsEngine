using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object=UnityEngine.Object;
using static MassEngine.Game.Editor.CharacterGeometry;

namespace MassEngine.Game.Editor
{
    internal static class CharacterPipelineTrial
    {
        internal static void CheckTemplates(CharacterRecipe r)
        {
            Require(r.trialMenu!=null && r.trialBattlefield!=null && r.trialCatalog!=null,"Preview needs existing menu, battlefield and catalog templates.");
            Require(r.trialCatalog.templates!=null && r.trialCatalog.templates.Any(t=>t.config==r.unitTemplate),"Unit template must occur in preview catalog.");
            Require(!string.IsNullOrWhiteSpace(r.battlefieldId) && !string.IsNullOrWhiteSpace(r.templateId) && r.battlefieldId==r.battlefieldId.Trim() && r.templateId==r.templateId.Trim(),"Explicit stable preview IDs required.");
            Require(r.trialCatalog.entries.Length==1,"First version supports a single-battlefield preview template.");
        }
        internal static void Create(CharacterRecipe r, UnitTypeConfig unit, string output)
        {
            Require(Application.isBatchMode || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo(),"Cancelled scene switch; fresh output will be rolled back.");
            string menuSource=AssetDatabase.GetAssetPath(r.trialMenu),battleSource=AssetDatabase.GetAssetPath(r.trialBattlefield);
            string catalogSource=AssetDatabase.GetAssetPath(r.trialCatalog),originalUnit=AssetDatabase.GetAssetPath(r.unitTemplate),unitPath=AssetDatabase.GetAssetPath(unit);
            string battlefieldId=r.battlefieldId,templateId=r.templateId,outputName=r.outputName;
            string folder=output+"/Trial";CharacterPipeline.EnsureFolder(folder);
            string battle=folder+"/Battlefield.unity",menu=folder+"/Menu.unity",scenarioPath=folder+"/Scenario.asset",catalogPath=folder+"/Catalog.asset";
            Require(!File.Exists(battle) && AssetDatabase.CopyAsset(battleSource,battle),"Cannot make fresh battlefield.");
            Require(!File.Exists(menu) && AssetDatabase.CopyAsset(menuSource,menu),"Cannot make fresh menu.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene=EditorSceneManager.OpenScene(battle,OpenSceneMode.Single);
                var manager=Object.FindFirstObjectByType<MassEngineManager>();Require(manager!=null && manager.scenarioConfig!=null,"Preview template has no manager/scenario.");
                var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();var prior=manager.scenarioConfig.unitTypes;
                Require(prior.Count(u=>AssetDatabase.GetAssetPath(u)==originalUnit)==1,"Preview scenario must contain exactly one source trial unit.");
                scenario.unitTypes=prior.Select((u,i)=>AssetDatabase.GetAssetPath(u)==originalUnit?AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(unitPath):CharacterPipeline.CloneUnit(u,folder,"Opponent"+i)).ToArray();
                AssetDatabase.CreateAsset(scenario,scenarioPath);manager.scenarioConfig=scenario;manager.battleStarted=false;EditorSceneManager.SaveScene(scene);
                var old=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(catalogSource);
                var catalog=Object.Instantiate(old);catalog.defaultEntryId=battlefieldId;
                catalog.entries[0].id=battlefieldId;catalog.entries[0].scenePath=battle;catalog.entries[0].displayName="角色流程复现 · "+outputName;
                catalog.templates=scenario.unitTypes.Select((u,i)=>new WarSandboxUnitTemplateEntry{templateId=AssetDatabase.GetAssetPath(u)==unitPath?templateId:templateId+"-opponent-"+i,revision=1,config=u}).ToArray();
                AssetDatabase.CreateAsset(catalog,catalogPath);
                Require(catalog.TryValidate(p=>AssetDatabase.LoadAssetAtPath<SceneAsset>(p)!=null,out string error) && catalog.TryValidateTemplates(out error),error);
                scene=EditorSceneManager.OpenScene(menu,OpenSceneMode.Single);
                var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();Require(session!=null,"Preview menu has no scene session.");
                session.catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(catalogPath);session.enterDefaultOnStart=true;
                var smoke=Object.FindFirstObjectByType<WarSandboxModelTrialSmoke>();Require(smoke!=null,"Preview menu must contain existing opt-in smoke component.");
                smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedSourceScenario=AssetDatabase.LoadAssetAtPath<ScenarioConfig>(scenarioPath);
                smoke.expectedTemplate=AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(unitPath);smoke.battlefieldId=battlefieldId;smoke.templateId=templateId;smoke.templateRevision=1;smoke.planSlot=outputName;
                foreach(var legacy in Object.FindObjectsByType<WarSandboxCharacterPilotRuntime>(FindObjectsSortMode.None))Object.DestroyImmediate(legacy);
                var runtime=new GameObject("Character Pipeline isolated preview").AddComponent<CharacterPipelinePreviewRuntime>();runtime.planDirectory="CharacterPipelinePlans";runtime.previewName=outputName;
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        internal static void Build(string output,string buildFolder)
        {
            Require(!Directory.Exists(buildFolder) && !File.Exists(buildFolder),"Never overwrite a player package.");
            var report=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(output+"/PipelineReport.json"));Require(report.automatedPassed,"Pipeline report is not ready.");
            var unit=AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(output+"/Unit.asset");Require(unit!=null,"Missing persisted output unit.");
            var validation=MassEngine.Editor.UnitTypeBinder.ValidateBinding(unit);Require(validation.IsValid,string.Join("\n",validation.Errors));
            var build=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{output+"/Trial/Menu.unity",output+"/Trial/Battlefield.unity"},locationPathName=buildFolder+"/KnightPilot.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(build.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Character pipeline player build failed.");
            File.WriteAllText(buildFolder+"/Start-Knight.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0KnightPilot.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
