using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using MassEngine.Editor;
using Object=UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class EnemyModelsBuilder
    {
        const string Source="Assets/CharacterPilotSource/KayKitSkeletons";
        const string Log="Logs/AgentEnemyModels";
        public static void Inspect()
        {
            var sb=new StringBuilder();
            foreach(var path in Directory.GetFiles(Source,"*.fbx"))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.animationType=ModelImporterAnimationType.Generic;importer.isReadable=true;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importBlendShapes=true;importer.SaveAndReimport();
                sb.AppendLine("MODEL "+path+" scale="+importer.globalScale+" fileScale="+importer.fileScale);
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach(var t in model.GetComponentsInChildren<Transform>(true))sb.AppendLine("NODE "+AnimationUtility.CalculateTransformPath(t,model.transform)+" pos="+t.localPosition+" rot="+t.localEulerAngles+" scale="+t.localScale);
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m=skin.sharedMesh;sb.AppendLine("SKIN "+skin.name+" vertices="+m.vertexCount+" bones="+skin.bones.Length+" bindposes="+m.bindposes.Length+" weights="+m.boneWeights.Length+" shapes="+m.blendShapeCount);
                    for(int i=0;i<m.blendShapeCount;i++)sb.AppendLine("SHAPE "+m.GetBlendShapeName(i)+" frames="+m.GetBlendShapeFrameCount(i)+" weight="+m.GetBlendShapeFrameWeight(i,0));
                }
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))sb.AppendLine("MESH "+filter.name+" vertices="+filter.sharedMesh.vertexCount+" shapes="+filter.sharedMesh.blendShapeCount);
                foreach(var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")))sb.AppendLine("CLIP "+c.name+" length="+c.length+" fps="+c.frameRate+" bindings="+AnimationUtility.GetCurveBindings(c).Length);
            }
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());Debug.Log("ENEMY_IMPORT_INSPECTION_READY");
        }
        const string Prepared="Assets/Game/EnemyModelsBatch";
        const string Knight="Assets/Game/CharacterPipeline/Generated/Knight04";
        static readonly string[] Keys={"warrior","rogue","mage"};
        static readonly string[] Models={"Warrior","Rogue","Mage"};
        static readonly string[] Titles={"斧盾骷髅","双刃骷髅","持杖骷髅"};
        static T Load<T>(string p)where T:Object{var o=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(o!=null,"Missing "+p);return o;}
        static AnimationClip Clip(string p,string name)=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Single(c=>c.name==name);
        static string Output(int i)=>CharacterPipeline.Root+"/Generated/Skeleton"+Models[i]+(i==2?"03":"01");
        public static void PrepareAll01()
        {
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh enemy batch required.");CharacterPipeline.EnsureFolder(Prepared);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Source+"/skeleton_texture.png");importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=true;importer.SaveAndReimport();
            for(int i=0;i<3;i++)PrepareOne(i);
            CreateCollection();Debug.Log("ENEMY_BATCH_PREPARATION_READY");
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Models[index];CharacterPipeline.EnsureFolder(folder);
            var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.unitTypeName=Titles[index];template.teamId=0;
            Material CopyMaterial(Material source,string name){var m=new Material(source){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(Source+"/skeleton_texture.png"));m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=CopyMaterial(prior.renderConfig.nearMaterial,"Skeleton"+Models[index]+"Near");template.renderConfig.midMaterial=CopyMaterial(prior.renderConfig.midMaterial,"Skeleton"+Models[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var root=new GameObject("Skeleton"+Models[index]+"Prepared");root.SetActive(false);GameObject prefab;AnimationClip[] clips;string[] attachments;
            try
            {
                var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);var body=Object.Instantiate(Load<GameObject>(Source+"/Skeleton_"+Models[index]+".fbx"),pivot);body.name="Body";
                foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;
                foreach(var r in body.GetComponentsInChildren<Renderer>(true))r.sharedMaterial=template.renderConfig.nearMaterial;
                void Attach(string socket,string file,string name)
                {var slot=body.GetComponentsInChildren<Transform>(true).Single(t=>t.name==socket);var weapon=Object.Instantiate(Load<GameObject>(Source+"/"+file+".fbx"),slot);weapon.name=name;weapon.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);weapon.transform.localScale=Vector3.one;foreach(var r in weapon.GetComponentsInChildren<Renderer>(true))r.sharedMaterial=template.renderConfig.nearMaterial;foreach(var a in weapon.GetComponentsInChildren<Animator>(true))a.enabled=false;}
                if(index==0){Attach("handslot.r","Skeleton_Axe","Axe");Attach("handslot.l","Skeleton_Shield_Large_A","Shield");}
                else if(index==1){Attach("handslot.r","Skeleton_Blade","BladeRight");Attach("handslot.l","Skeleton_Blade","BladeLeft");}
                else Attach("handslot.r","Skeleton_Staff","Staff");
                string common="Assets/CharacterPilotSource/KayKitKnight/";
                var attack= index==2?Clip("Assets/CharacterPilotSource/KayKitRanger/Rig_Medium_CombatRanged.fbx","Ranged_Magic_Shoot") : Clip(common+"Rig_Medium_CombatMelee.fbx",index==0?"Melee_1H_Attack_Chop":"Melee_Dualwield_Attack_Slice");
                var sources=new[]{Clip(common+"Rig_Medium_General.fbx","Idle_A"),Clip(common+"Rig_Medium_MovementBasic.fbx","Running_A"),attack,Clip(common+"Rig_Medium_General.fbx","Death_A")};
                string[] names={"Idle","Move","Attack","Death"};clips=new AnimationClip[4];
                for(int j=0;j<4;j++)
                {
                    var c=new AnimationClip{name=names[j],frameRate=30};
                    foreach(var binding in AnimationUtility.GetCurveBindings(sources[j]))
                    {CharacterGeometry.Require(binding.type==typeof(Transform)&&body.transform.Find(binding.path)!=null,"Unmapped skeleton rig: "+binding.path);var mapped=binding;mapped.path="ScalePivot/Body/"+binding.path;AnimationUtility.SetEditorCurve(c,mapped,AnimationUtility.GetEditorCurve(sources[j],binding));}
                    c.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(c);settings.loopTime=j!=3;AnimationUtility.SetAnimationClipSettings(c,settings);AssetDatabase.CreateAsset(c,folder+"/"+names[j]+".anim");clips[j]=c;
                }
                if(index==2)CalibrateStaff(root,clips[0]);
                attachments=root.GetComponentsInChildren<MeshRenderer>(true).Select(r=>AnimationUtility.CalculateTransformPath(r.transform,root.transform)).ToArray();
                root.SetActive(true);prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");
            }
            finally{Object.DestroyImmediate(root);}
            var combat=template.combatConfig;combat.attackInterval=clips[2].length;combat.targetAcquireRadius=10;combat.attackRange=1.8f;combat.projectileRange=index==2?10:0;combat.projectileGravity=-9.8f;combat.projectileSpeed=12;combat.projectileHitRadius=.1f;combat.projectileMaxLifetime=3;combat.attackReleasePhase=.55f;combat.projectileOriginHeight=1.3f;combat.projectileTargetHeight=1;combat.projectileTrailLength=1.15f;
            template.spawnConfig.unitCount=64;template.spawnConfig.spawnCenter=new Vector3(-12,0,0);CharacterPipeline.Save(combat);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template);
            var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();recipe.name="Skeleton"+Models[index];recipe.model=prefab;recipe.sizeRootPath="ScalePivot";recipe.attachmentPaths=attachments;recipe.idle=clips[0];recipe.move=clips[1];recipe.attack=clips[2];recipe.death=clips[3];recipe.targetBodyHeight=index==0?1.9f:1.8f;recipe.agentRadius=index==0?.5f:.45f;recipe.frameRate=24;recipe.lowVertexBudget=2000;recipe.maxTextureMiB=32;recipe.outputName="Skeleton"+Models[index]+(index==2?"03":"01");recipe.unitTemplate=template;recipe.createTrial=false;
            AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/Skeleton"+Models[index]+(index==2?"03":"")+".asset");var result=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/"+Keys[index]+(index==2?"-report-03.json":"-report-01.json"),JsonUtility.ToJson(result,true));
            Debug.Log("ENEMY_MODEL_READY "+Keys[index]+" "+result.fullVertices+"/"+result.lowVertices);
        }
        public static void CreateCollection()
        {
            string folder=Prepared+"/Collection01";CharacterGeometry.Require(!Directory.Exists(folder),"Collection already exists.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(folder+"/Previews");
            var control=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(Knight+"/Unit.asset"),folder,"KnightControl");control.teamId=1;control.unitTypeName="骑士（对照）";control.spawnConfig.unitCount=64;control.spawnConfig.spawnCenter=new Vector3(12,0,0);control.combatConfig.targetAcquireRadius=10;CharacterPipeline.Save(control.combatConfig);CharacterPipeline.Save(control.spawnConfig);CharacterPipeline.Save(control);
            var old=Load<WarSandboxBattlefieldCatalog>(Knight+"/Trial/Catalog.asset");var catalog=ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();catalog.name="EnemyModelsCollection";catalog.defaultEntryId="enemy-model-warrior";catalog.entries=new WarSandboxBattlefieldEntry[3];catalog.templates=new WarSandboxUnitTemplateEntry[4];catalog.templates[3]=new WarSandboxUnitTemplateEntry{templateId="enemy-collection-knight-control",revision=1,config=control};
            var configs=new ScenarioConfig[3];var units=new UnitTypeConfig[3];
            for(int i=0;i<3;i++)
            {
                units[i]=Load<UnitTypeConfig>(Output(i)+"/Unit.asset");var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{units[i],control};AssetDatabase.CreateAsset(scenario,folder+"/"+Models[i]+"Scenario.asset");configs[i]=scenario;
                string battle=folder+"/"+Models[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(Knight+"/Trial/Battlefield.unity",battle),"Cannot copy isolated scene.");
                var entry=old.entries[0].CopyIdentity();entry.id="enemy-model-"+Keys[i];entry.displayName=Titles[i]+" · 模型展示";entry.description="新增独立敌方外观；复用现有"+(i==2?"远程投射":"近战")+"行为。";entry.briefing="攻方为"+Titles[i]+"，守方骑士作对照。拉近查看骨骼、武器、动作与LOD；本轮不做兵种配平。";entry.scenePath=battle;
                var report=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Log+"/"+Keys[i]+(i==2?"-report-03.json":"-report-01.json")));string picture=folder+"/Previews/"+Models[i]+".png";File.Copy(report.evidence+"/lod0-Idle-0.png",picture);AssetDatabase.ImportAsset(picture);entry.preview=Load<Texture2D>(picture);catalog.entries[i]=entry;
                catalog.templates[i]=new WarSandboxUnitTemplateEntry{templateId="skeleton-"+Keys[i],revision=1,config=units[i]};
            }
            AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                for(int i=0;i<3;i++)
                {
                    var scene=EditorSceneManager.OpenScene(folder+"/"+Models[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+Models[i]+"Scenario.asset");manager.battleStarted=false;
                    foreach(var text in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))if(text.text.Contains("Knight pilot"))text.text=text.text.Replace("Knight pilot","Enemy models");
                    EditorSceneManager.SaveScene(scene);
                }
                string menu=folder+"/Menu.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(Knight+"/Trial/Menu.unity",menu),"Cannot copy collection menu.");var menuScene=EditorSceneManager.OpenScene(menu,OpenSceneMode.Single);
                var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var smoke=Object.FindFirstObjectByType<WarSandboxModelTrialSmoke>();smoke.session=session;smoke.expectedCatalog=session.catalog;
                var router=new GameObject("Enemy collection test selection (opt-in)").AddComponent<EnemyBatchRuntime>();router.smoke=smoke;router.entries=Enumerable.Range(0,3).Select(i=>new EnemyBatchRuntime.Entry{key=Keys[i],battlefieldId="enemy-model-"+Keys[i],templateId="skeleton-"+Keys[i],unit=Load<UnitTypeConfig>(Output(i)+"/Unit.asset"),scenario=Load<ScenarioConfig>(folder+"/"+Models[i]+"Scenario.asset")}).ToArray();
                smoke.expectedTemplate=router.entries[0].unit;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.templateRevision=1;smoke.planSlot="EnemyCollection-warrior";
                var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="EnemyModelPlans";preview.previewName="Enemy Models Collection";
                new GameObject("Passive existing ranged probe (opt-in mage)").AddComponent<RangerFiringProbe>();
                EditorSceneManager.SaveScene(menuScene);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/collection-ready.json",JsonUtility.ToJson(new CollectionReceipt{passed=true,models=Keys,folder=folder},true));
        }
        [Serializable]class CollectionReceipt{public bool passed;public string[] models;public string folder;}
        public static void CorrectMage02()
        {
            string folder=Prepared+"/Mage/Corrected02";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh mage correction required.");CharacterPipeline.EnsureFolder(folder);
            var r=Object.Instantiate(Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/SkeletonMage.asset"));r.name="SkeletonMage02";r.outputName="SkeletonMage02";
            var go=PrefabUtility.LoadPrefabContents(Prepared+"/Mage/Prepared.prefab");try
            {
                var staff=go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Staff");var hand=go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="handslot.r");staff.SetParent(hand,false);staff.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);staff.localScale=Vector3.one;
                r.attachmentPaths=go.GetComponentsInChildren<MeshRenderer>(true).Select(x=>AnimationUtility.CalculateTransformPath(x.transform,go.transform)).ToArray();PrefabUtility.SaveAsPrefabAsset(go,folder+"/Prepared.prefab");
            }finally{PrefabUtility.UnloadPrefabContents(go);}
            r.model=Load<GameObject>(folder+"/Prepared.prefab");AssetDatabase.CreateAsset(r,CharacterPipeline.Root+"/Recipes/SkeletonMage02.asset");var result=CharacterPipeline.Run(r);File.WriteAllText(Log+"/mage-report-02.json",JsonUtility.ToJson(result,true));
            string collection=Prepared+"/Collection01";var unit=Load<UnitTypeConfig>(Output(2)+"/Unit.asset");var scenario=Load<ScenarioConfig>(collection+"/MageScenario.asset");scenario.unitTypes[0]=unit;CharacterPipeline.Save(scenario);
            string picture=collection+"/Previews/Mage02.png";File.Copy(result.evidence+"/lod0-Idle-0.png",picture);AssetDatabase.ImportAsset(picture);var cat=Load<WarSandboxBattlefieldCatalog>(collection+"/Catalog.asset");cat.templates[2].config=unit;cat.entries[2].preview=Load<Texture2D>(picture);CharacterPipeline.Save(cat);
            var setup=EditorSceneManager.GetSceneManagerSetup();try{var scene=EditorSceneManager.OpenScene(collection+"/Menu.unity",OpenSceneMode.Single);var router=Object.FindFirstObjectByType<EnemyBatchRuntime>();router.entries[2].unit=Load<UnitTypeConfig>(Output(2)+"/Unit.asset");EditorSceneManager.SaveScene(scene);}finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            Debug.Log("ENEMY_MAGE_CORRECTED02_READY");
        }
        public static void CorrectMage03()
        {
            string folder=Prepared+"/Mage/Corrected03";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh mage correction required.");CharacterPipeline.EnsureFolder(folder);
            var r=Object.Instantiate(Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/SkeletonMage02.asset"));r.name="SkeletonMage03";r.outputName="SkeletonMage03";
            var go=PrefabUtility.LoadPrefabContents(Prepared+"/Mage/Corrected02/Prepared.prefab");try
            {
                var staff=go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Staff");var hand=go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="handslot.r");staff.SetParent(hand,false);staff.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);staff.localScale=Vector3.one;
                CalibrateStaff(go,r.idle);
                r.attachmentPaths=go.GetComponentsInChildren<MeshRenderer>(true).Select(x=>AnimationUtility.CalculateTransformPath(x.transform,go.transform)).ToArray();PrefabUtility.SaveAsPrefabAsset(go,folder+"/Prepared.prefab");
            }finally{PrefabUtility.UnloadPrefabContents(go);}
            r.model=Load<GameObject>(folder+"/Prepared.prefab");AssetDatabase.CreateAsset(r,CharacterPipeline.Root+"/Recipes/SkeletonMage03.asset");var result=CharacterPipeline.Run(r);File.WriteAllText(Log+"/mage-report-03.json",JsonUtility.ToJson(result,true));
            string collection=Prepared+"/Collection01";var unit=Load<UnitTypeConfig>(Output(2)+"/Unit.asset");var scenario=Load<ScenarioConfig>(collection+"/MageScenario.asset");scenario.unitTypes[0]=unit;CharacterPipeline.Save(scenario);
            string picture=collection+"/Previews/Mage03.png";File.Copy(result.evidence+"/lod0-Idle-0.png",picture);AssetDatabase.ImportAsset(picture);var cat=Load<WarSandboxBattlefieldCatalog>(collection+"/Catalog.asset");cat.templates[2].config=unit;cat.entries[2].preview=Load<Texture2D>(picture);CharacterPipeline.Save(cat);
            var setup=EditorSceneManager.GetSceneManagerSetup();try{var scene=EditorSceneManager.OpenScene(collection+"/Menu.unity",OpenSceneMode.Single);var router=Object.FindFirstObjectByType<EnemyBatchRuntime>();router.entries[2].unit=Load<UnitTypeConfig>(Output(2)+"/Unit.asset");EditorSceneManager.SaveScene(scene);}finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            Debug.Log("ENEMY_MAGE_CORRECTED03_READY");
        }
        static void CalibrateStaff(GameObject root,AnimationClip idle)
        {
            var ts=root.GetComponentsInChildren<Transform>(true);var ps=ts.Select(t=>t.localPosition).ToArray();var qs=ts.Select(t=>t.localRotation).ToArray();idle.SampleAnimation(root,0);
            var staff=ts.Single(t=>t.name=="Staff");var mesh=staff.GetComponentInChildren<MeshFilter>().sharedMesh;var size=mesh.bounds.size;int axis=size.x>=size.y&&size.x>=size.z?0:(size.y>=size.z?1:2);
            float average=mesh.vertices.Average(v=>v[axis]);float sign=average>=mesh.bounds.center[axis]?1:-1;Vector3 direction=(axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward)*sign;
            var rotation=Quaternion.FromToRotation(direction,staff.parent.InverseTransformDirection(Vector3.up));
            for(int i=0;i<ts.Length;i++){ts[i].localPosition=ps[i];ts[i].localRotation=qs[i];}staff.localRotation=rotation;
            Debug.Log("STAFF_IDLE_ALIGNMENT axis="+axis+" sign="+sign+" rotation="+rotation);
        }
        public static void Build01()
        {
            string dest="Builds/EnemyModels-20260930-01",folder=Prepared+"/Collection01";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player package.");
            foreach(int i in Enumerable.Range(0,3)){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Model report failed.");var v=UnitTypeBinder.ValidateBinding(Load<UnitTypeConfig>(Output(i)+"/Unit.asset"));CharacterGeometry.Require(v.IsValid,string.Join("\n",v.Errors));}
            var scenes=new[]{folder+"/Menu.unity"}.Concat(Models.Select(m=>folder+"/"+m+"Battlefield.unity")).ToArray();var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=dest+"/EnemyModels.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(result.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Enemy collection build failed.");
            File.WriteAllText(dest+"/Start-Enemies.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0EnemyModels.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
