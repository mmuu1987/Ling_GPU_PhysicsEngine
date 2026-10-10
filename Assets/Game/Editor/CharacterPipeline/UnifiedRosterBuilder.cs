using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using MassEngine.Editor;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor
{
    public static class UnifiedRosterBuilder
    {
        const string Source="Assets/Art/Source/CharacterPilot/LegacySkeleton01";
        const string Root="Assets/Game/Content/Characters/UnifiedRoster/Version03";
        const string Log="Logs/AgentUnifiedRoster";
        static T Load<T>(string p)where T:Object{var o=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(o!=null,"Missing "+p);return o;}
        public static void Inspect()
        {
            var text=new StringBuilder();
            foreach(var path in Directory.GetFiles(Source,"*.fbx"))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.animationType=ModelImporterAnimationType.Generic;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.isReadable=true;importer.importBlendShapes=true;importer.SaveAndReimport();
                text.AppendLine("FILE "+path+" fileScale="+importer.fileScale);var model=Load<GameObject>(path);
                foreach(var t in model.GetComponentsInChildren<Transform>(true))text.AppendLine("NODE "+AnimationUtility.CalculateTransformPath(t,model.transform)+" pos="+t.localPosition+" rot="+t.localEulerAngles+" scale="+t.localScale);
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)){var m=skin.sharedMesh;text.AppendLine("SKIN "+skin.name+" vertices="+m.vertexCount+" bones="+skin.bones.Length+" shapes="+m.blendShapeCount+" submeshes="+m.subMeshCount+" uv="+m.uv.Length);}
                foreach(var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")))
                {
                    text.AppendLine("CLIP "+c.name+" length="+c.length+" frames="+c.frameRate);
                    foreach(var b in AnimationUtility.GetCurveBindings(c)){var cv=AnimationUtility.GetEditorCurve(c,b);if(b.propertyName.StartsWith("m_LocalScale.")&&cv.keys.Any(k=>Mathf.Abs(k.value-1)>1e-5f))text.AppendLine("SCALE "+b.path+" "+b.propertyName+" "+cv.keys.Min(k=>k.value)+"/"+cv.keys.Max(k=>k.value));}
                }
            }
            foreach(var path in new[]{"Assets/SazenGames/Skeleton/Prefabs/Skeleton_110.prefab","Assets/SazenGames/Skeleton/Prefabs/Falchion_01.prefab"})
            {var model=Load<GameObject>(path);text.AppendLine("ORIGINAL_PREFAB "+path);foreach(var t in model.GetComponentsInChildren<Transform>(true))text.AppendLine("PREFAB_NODE "+AnimationUtility.CalculateTransformPath(t,model.transform)+" p="+t.localPosition+" r="+t.localEulerAngles+" s="+t.localScale);}
            File.WriteAllText(Log+"/skeleton-inspection.txt",text.ToString());Debug.Log("UNIFIED_ROSTER_INSPECTION_READY");
        }
        const string Knight="Assets/Game/Authoring/CharacterPipeline/Generated/Knight04";
        const string LegacyOutput="Assets/Game/Authoring/CharacterPipeline/Generated/LegacySkeleton01";
        static AnimationClip OnlyClip(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
        static Texture2D ReadTexture(Texture source)
        {
            var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var before=RenderTexture.active;
            try{Graphics.Blit(source,rt);RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();return t;}finally{RenderTexture.active=before;RenderTexture.ReleaseTemporary(rt);}
        }
        [Serializable]class SkeletonPreparation{public bool passed;public int frames,positions;public float maximumScaledPositionError;public string method="Private copies of existing officially-acquired skeleton; original hand socket pose; UV atlas remap, constant near-unit scale noise normalized and original positions checked. Eye emission colour is flattened into albedo, not bloom.";}
        public static void Prepare01()
        {
            CharacterGeometry.Require(!Directory.Exists(Root),"Fresh unified-roster root required.");CharacterPipeline.EnsureFolder(Root);PrepareSkeleton();CreateLibraryAndScenes();Debug.Log("UNIFIED_ROSTER_READY");
        }
        static void PrepareSkeleton()
        {
            string folder=Root+"/LegacySkeleton";CharacterPipeline.EnsureFolder(folder);var report=new SkeletonPreparation();
            var sourceMaterial=Load<Material>("Assets/SazenGames/Skeleton/Art/Materials/Skeleton_Mat.mat");var weaponMaterial=Load<Material>("Assets/SazenGames/Skeleton/Art/Materials/Falchion_Mat.mat");
            var bodyTexture=ReadTexture(sourceMaterial.mainTexture);var bladeTexture=ReadTexture(weaponMaterial.mainTexture);
            Texture2D atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);Rect[] rects;
            try
            {
                var emission=sourceMaterial.GetTexture("_EmissionMap");if(emission!=null){var e=ReadTexture(emission);try{Color tint=sourceMaterial.GetColor("_EmissionColor");var values=bodyTexture.GetPixels();for(int y=0;y<bodyTexture.height;y++)for(int x=0;x<bodyTexture.width;x++){int n=y*bodyTexture.width+x;var glow=e.GetPixelBilinear((x+.5f)/bodyTexture.width,(y+.5f)/bodyTexture.height)*tint;var c=values[n]+glow;values[n]=new Color(Mathf.Clamp01(c.r),Mathf.Clamp01(c.g),Mathf.Clamp01(c.b),1);}bodyTexture.SetPixels(values);bodyTexture.Apply();}finally{Object.DestroyImmediate(e);}}
                rects=atlas.PackTextures(new[]{bodyTexture,bladeTexture},8,2048,false);File.WriteAllBytes(folder+"/Atlas.png",atlas.EncodeToPNG());
            }finally{Object.DestroyImmediate(bodyTexture);Object.DestroyImmediate(bladeTexture);Object.DestroyImmediate(atlas);}
            AssetDatabase.ImportAsset(folder+"/Atlas.png");var ti=(TextureImporter)AssetImporter.GetAtPath(folder+"/Atlas.png");ti.textureCompression=TextureImporterCompression.Uncompressed;ti.wrapMode=TextureWrapMode.Clamp;ti.mipmapEnabled=true;ti.SaveAndReimport();
            var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var unit=CharacterPipeline.CloneUnit(prior,folder,"Template");unit.unitTypeName="旧资源·Sazen骷髅";
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(folder+"/Atlas.png"));m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            unit.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,"LegacySkeletonNear");unit.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,"LegacySkeletonMid");unit.renderConfig.farMaterial=unit.renderConfig.midMaterial;CharacterPipeline.Save(unit.renderConfig);
            var root=new GameObject("LegacySkeletonPrepared");root.SetActive(false);var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);var body=Object.Instantiate(Load<GameObject>(Source+"/Skeleton_Model_110.fbx"),pivot);body.name="Body";
            var reference=new GameObject("Original skeleton anchored reference");var original=Object.Instantiate(Load<GameObject>(Source+"/Skeleton_Model_110.fbx"),reference.transform,false);original.SetActive(false);
            var referencePrefab=Load<GameObject>("Assets/SazenGames/Skeleton/Prefabs/Skeleton_110.prefab");var oldSword=referencePrefab.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Falchion_01");
            void Attach(GameObject go){var hand=go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="hand.r");var sword=Object.Instantiate(Load<GameObject>(Source+"/Falchion_01.fbx"),hand);sword.name="Falchion";sword.transform.localPosition=oldSword.localPosition;sword.transform.localRotation=oldSword.localRotation;sword.transform.localScale=oldSword.localScale;}
            Attach(body);Attach(original);
            try
            {
                foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;foreach(var a in original.GetComponentsInChildren<Animator>(true))a.enabled=false;
                int counter=0;foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)){var mesh=Object.Instantiate(skin.sharedMesh);mesh.name="BodyAtlas"+counter;var rect=rects[0];mesh.uv=mesh.uv.Select(v=>new Vector2(rect.x+v.x*rect.width,rect.y+v.y*rect.height)).ToArray();AssetDatabase.CreateAsset(mesh,folder+"/Body"+(counter++)+".asset");skin.sharedMesh=mesh;skin.sharedMaterial=unit.renderConfig.nearMaterial;}
                foreach(var filter in body.GetComponentsInChildren<MeshFilter>(true)){var mesh=Object.Instantiate(filter.sharedMesh);mesh.name="BladeAtlas";var rect=rects[1];mesh.uv=mesh.uv.Select(v=>new Vector2(rect.x+v.x*rect.width,rect.y+v.y*rect.height)).ToArray();AssetDatabase.CreateAsset(mesh,folder+"/BladeMesh.asset");filter.sharedMesh=mesh;filter.GetComponent<MeshRenderer>().sharedMaterial=unit.renderConfig.nearMaterial;}
                var transforms=root.GetComponentsInChildren<Transform>(true);var bindPositions=transforms.Select(t=>t.localPosition).ToArray();var bindRotations=transforms.Select(t=>t.localRotation).ToArray();var bindScales=transforms.Select(t=>t.localScale).ToArray();
                var files=new[]{"Skeleton_idle.fbx","Skeleton_run_forward.fbx","Skeleton_slash01.fbx","Skeleton_death.fbx"};var actions=new[]{"Idle","Move","Attack","Death"};var clips=new AnimationClip[4];
                root.SetActive(true);original.SetActive(true);
                var rawRecipe=ScriptableObject.CreateInstance<CharacterRecipe>();rawRecipe.attachmentPaths=reference.GetComponentsInChildren<MeshRenderer>(true).Select(m=>AnimationUtility.CalculateTransformPath(m.transform,reference.transform)).ToArray();float factor=1.9f/CharacterGeometry.BoundsOf(CharacterGeometry.Positions(reference,rawRecipe,true)).size.y;
                var preparedRecipe=ScriptableObject.CreateInstance<CharacterRecipe>();preparedRecipe.attachmentPaths=root.GetComponentsInChildren<MeshRenderer>(true).Select(m=>AnimationUtility.CalculateTransformPath(m.transform,root.transform)).ToArray();
                for(int a=0;a<4;a++)
                {
                    var source=OnlyClip(Source+"/"+files[a]);var c=new AnimationClip{name=actions[a],frameRate=source.frameRate};
                    var originalPaths=original.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,original.transform));
                    var bodyPaths=body.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,body.transform));
                    var sampled=bodyPaths.ToDictionary(v=>v.Key,v=>Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray());
                    int frameCount=Mathf.CeilToInt(source.length*24);
                    for(int f=0;f<=frameCount;f++)
                    {
                        float time=Mathf.Min(f/24f,source.length);source.SampleAnimation(original,time);
                        foreach(var pair in bodyPaths)
                        {
                            CharacterGeometry.Require(originalPaths.TryGetValue(pair.Key,out var t),"Source transform missing: "+pair.Key);CharacterGeometry.Require((t.localScale-Vector3.one).magnitude<.0003f,"Real source scale animation not supported.");
                            var pos=t.localPosition;var q=t.localRotation;float[] values={pos.x,pos.y,pos.z,q.x,q.y,q.z,q.w};for(int channel=0;channel<7;channel++)sampled[pair.Key][channel].Add(new Keyframe(time,values[channel]));
                        }
                    }
                    string[] channels={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
                    foreach(var pair in sampled)for(int channel=0;channel<7;channel++)
                    {
                        var curve=new AnimationCurve(pair.Value[channel].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                        string path="ScalePivot/Body"+(pair.Key.Length==0?"":"/"+pair.Key);AnimationUtility.SetEditorCurve(c,EditorCurveBinding.FloatCurve(path,typeof(Transform),channels[channel]),curve);
                    }
                    c.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(c);settings.loopTime=a!=3;AnimationUtility.SetAnimationClipSettings(c,settings);AssetDatabase.CreateAsset(c,folder+"/"+actions[a]+".anim");clips[a]=c;
                    for(int f=0;f<Mathf.CeilToInt(c.length*24);f++){source.SampleAnimation(original,f/24f);c.SampleAnimation(root,f/24f);var expected=CharacterGeometry.Positions(reference,rawRecipe);var actual=CharacterGeometry.Positions(root,preparedRecipe);CharacterGeometry.Require(expected.Length==actual.Length,"Atlas topology count changed unexpectedly.");for(int v=0;v<actual.Length;v++){report.maximumScaledPositionError=Mathf.Max(report.maximumScaledPositionError,Vector3.Distance(actual[v],expected[v])*factor);report.positions++;}report.frames++;}
                }
                File.WriteAllText(Log+"/skeleton-preparation-03.json",JsonUtility.ToJson(report,true));CharacterGeometry.Require(report.maximumScaledPositionError<=.00025f,"Original skeleton pose mismatch: "+report.maximumScaledPositionError);Object.DestroyImmediate(rawRecipe);Object.DestroyImmediate(preparedRecipe);for(int t=0;t<transforms.Length;t++){transforms[t].localPosition=bindPositions[t];transforms[t].localRotation=bindRotations[t];transforms[t].localScale=Vector3.one;}
                root.SetActive(true);var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");unit.combatConfig.projectileRange=0;unit.combatConfig.attackInterval=clips[2].length;CharacterPipeline.Save(unit.combatConfig);
                var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();recipe.name="LegacySkeleton";recipe.model=prefab;recipe.sizeRootPath="ScalePivot";recipe.attachmentPaths=root.GetComponentsInChildren<MeshRenderer>(true).Select(m=>AnimationUtility.CalculateTransformPath(m.transform,root.transform)).ToArray();recipe.idle=clips[0];recipe.move=clips[1];recipe.attack=clips[2];recipe.death=clips[3];recipe.unitTemplate=unit;recipe.targetBodyHeight=1.9f;recipe.agentRadius=.5f;recipe.frameRate=24;recipe.lowPolicy=CharacterLowLodPolicy.StandardClustering;recipe.lowVertexBudget=2500;recipe.outputName="LegacySkeleton01";recipe.maxTextureMiB=32;AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/LegacySkeleton.asset");report.passed=true;File.WriteAllText(Log+"/skeleton-preparation.json",JsonUtility.ToJson(report,true));
            }
            finally{Object.DestroyImmediate(original);Object.DestroyImmediate(reference);Object.DestroyImmediate(root);}
            var result=CharacterPipeline.Run(Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/LegacySkeleton.asset"));File.WriteAllText(Log+"/skeleton-report-01.json",JsonUtility.ToJson(result,true));
        }
        static readonly string[] Keys={"male","female","unitychan","legacy-skeleton","knight","ranger","skeleton-warrior","skeleton-rogue","skeleton-mage","orc","yeti","mushroom"};
        static readonly string[] Titles={"原有·男战士","原有·女角色","UnityChan（UCL）","原有·Sazen骷髅","剑盾骑士","持弓游侠","斧盾骷髅","双刃骷髅","持杖骷髅","兽人","雪怪","蘑菇怪"};
        static string[] Sources()=>new[]{"Assets/Game/Settings/AttackerMeleeUnitConfig.asset","Assets/Game/Settings/DefenderUnitConfig.asset","Assets/Game/Content/Characters/M54TrialPlayable/Settings/UnityChanTrial.asset",LegacyOutput+"/Unit.asset",Knight+"/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/Ranger02/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/SkeletonWarrior01/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/SkeletonRogue01/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/SkeletonMage03/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/Orc01/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/Yeti01/Unit.asset","Assets/Game/Authoring/CharacterPipeline/Generated/MushroomKing01/Unit.asset"};
        [Serializable]class LibraryEntry{public string key,title,source,unit,profile;public int vertices;public bool large;}
        [Serializable]class LibraryReport{public bool passed;public List<LibraryEntry> models=new List<LibraryEntry>();public int regularTemplates,largeTemplates;}
        public static void CreateLibraryAndScenes()
        {
            string lib=Root+"/Library",scenes=Root+"/Scenes";CharacterGeometry.Require(!Directory.Exists(lib)&&!Directory.Exists(scenes),"Fresh unified library/scenes required.");CharacterPipeline.EnsureFolder(lib);CharacterPipeline.EnsureFolder(scenes);var report=new LibraryReport();var normal=new UnitTypeConfig[12];var templates=new List<WarSandboxUnitTemplateEntry>();var sourcePaths=Sources();
            for(int i=0;i<12;i++)
            {
                var unit=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(sourcePaths[i]),lib,Keys[i]);unit.unitTypeName=Titles[i];unit.teamId=i%2;unit.spawnConfig.unitCount=6;unit.spawnConfig.formationDensity=.12f;unit.spawnConfig.formationAspect=1.5f;unit.spawnConfig.spawnSize=Vector3.zero;unit.spawnConfig.spawnCenter=new Vector3(unit.teamId==0?-28:28,0,(i/2-2.5f)*16);
                if(unit.combatConfig.projectileRange>0)unit.combatConfig.projectileRange=Mathf.Min(unit.combatConfig.projectileRange,12);unit.combatConfig.targetAcquireRadius=12;CharacterPipeline.Save(unit.spawnConfig);CharacterPipeline.Save(unit.combatConfig);CharacterPipeline.Save(unit);normal[i]=unit;
                var valid=UnitTypeBinder.ValidateBinding(unit);CharacterGeometry.Require(valid.IsValid,string.Join("\n",valid.Errors));templates.Add(new WarSandboxUnitTemplateEntry{templateId="roster-"+Keys[i],revision=1,config=unit});report.models.Add(new LibraryEntry{key=Keys[i],title=Titles[i],source=sourcePaths[i],unit=AssetDatabase.GetAssetPath(unit),profile=AssetDatabase.GetAssetPath(unit.renderConfig.vatProfile),vertices=unit.renderConfig.nearMesh.vertexCount});
            }
            var normalPolicy=ScriptableObject.CreateInstance<WarSandboxRosterPolicy>();normalPolicy.name="RegularRoster";normalPolicy.templates=normal;normalPolicy.maximumRadius=.8f;normalPolicy.maximumUnits=256;normalPolicy.explanation="常规角色库 · 换角色会采用安全默认人数/阵型，保留军团和中心位置；调整后再应用。大型单位请用大型适配场。";AssetDatabase.CreateAsset(normalPolicy,Root+"/RegularPolicy.asset");
            var large=new UnitTypeConfig[3];string largeRoot="Assets/Game/Content/Characters/LargeBeasts/Prepared02/Collection01";
            for(int i=0;i<3;i++)
            {
                string path=i==0?"Assets/Game/Authoring/CharacterPipeline/Generated/LargeWolf02/Unit.asset":i==1?"Assets/Game/Authoring/CharacterPipeline/Generated/LargeBull02/Unit.asset":largeRoot+"/BullControl.asset";string key=i==0?"large-wolf":i==1?"large-bull":"large-knight-control";
                var unit=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(path),lib,key);unit.unitTypeName=i==0?"大型·巨狼":i==1?"大型·公牛":"大型对照·骑士";unit.teamId=i==2?1:0;unit.spawnConfig.unitCount=i==2?16:2;unit.spawnConfig.spawnCenter=new Vector3(i==2?45:-45,0,0);CharacterPipeline.Save(unit.spawnConfig);CharacterPipeline.Save(unit);large[i]=unit;templates.Add(new WarSandboxUnitTemplateEntry{templateId="roster-"+key,revision=1,config=unit});
                if(i<2)report.models.Add(new LibraryEntry{key=key,title=unit.unitTypeName,source=path,unit=AssetDatabase.GetAssetPath(unit),profile=AssetDatabase.GetAssetPath(unit.renderConfig.vatProfile),vertices=unit.renderConfig.nearMesh.vertexCount,large=true});
            }
            var largePolicy=ScriptableObject.CreateInstance<WarSandboxRosterPolicy>();largePolicy.name="LargeRoster";largePolicy.templates=large;largePolicy.maximumUnits=64;largePolicy.maximumRadius=4.1f;largePolicy.restrictLargeToTeamZero=true;largePolicy.explanation="大型适配场 · 大兽限攻方，守方用适配骑士。换角色采用安全默认人数/阵型；中心位置仍需检查，未验证大兽互斗。";AssetDatabase.CreateAsset(largePolicy,Root+"/LargePolicy.asset");
            var old=Load<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/EnemyModelsBatch/Collection01/Catalog.asset");string legacyBattlefieldId=old.entries[0].id;var cat=ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();cat.name="UnifiedRosterCatalog";cat.defaultEntryId="unified-regular";cat.templates=templates.Concat(new[]{old.templates.First(t=>t.templateId=="skeleton-warrior"),old.templates.First(t=>t.templateId=="enemy-collection-knight-control")}).ToArray();
            string[] names={"Regular","AllRegular","Large"},ids={"unified-regular","unified-all-regular","unified-large"},titles={"自由编成 · 12种常规角色","全角色示例 · 新旧同场","大型适配 · 受限角色库"};var entries=new List<WarSandboxBattlefieldEntry>();
            for(int i=0;i<3;i++)
            {
                var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=i==0?new[]{normal[0],normal[1]}:i==1?normal:new[]{large[0],large[2]};AssetDatabase.CreateAsset(scenario,scenes+"/"+names[i]+"Scenario.asset");string battle=scenes+"/"+names[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(i==2?largeRoot+"/WolfBattlefield.unity":old.entries[0].scenePath,battle),"Cannot copy isolated scene.");var e=old.entries[0].CopyIdentity();e.id=ids[i];e.scenePath=battle;e.displayName=titles[i];e.preview=null;e.description=i==0?"初始只有男女角色，布阵兵种菜单可选全部12常规模型。":i==1?"12种角色直接同场；可自由改编成并保存重载。":"巨狼/公牛与适配骑士；私有网格、占地和接敌限制。";e.briefing="模型整合预览，非兵种配平或100k性能验收。UnityChan依UCL使用，不是整包CC0。";entries.Add(e);
            }
            entries.Add(old.entries[0].CopyIdentity());cat.entries=entries.ToArray();AssetDatabase.CreateAsset(cat,Root+"/Catalog.asset");CharacterGeometry.Require(cat.TryValidate(p=>File.Exists(p),out string error)&&cat.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<3;i++)
                {
                    var scene=EditorSceneManager.OpenScene(scenes+"/"+names[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(scenes+"/"+names[i]+"Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);var deployment=manager.GetComponent<WarSandboxRuntimeDeployment>();deployment.rosterPolicy=Load<WarSandboxRosterPolicy>(Root+(i==2?"/LargePolicy.asset":"/RegularPolicy.asset"));
                    if(i<2){var system=Object.Instantiate(manager.systemConfig);var sim=Object.Instantiate(system.simulationConfig);sim.simulationWorldSize=new Vector2(220,220);sim.cellSize=3;AssetDatabase.CreateAsset(sim,scenes+"/"+names[i]+"Simulation.asset");system.simulationConfig=sim;AssetDatabase.CreateAsset(system,scenes+"/"+names[i]+"System.asset");manager.systemConfig=system;}
                    var camera=Object.FindFirstObjectByType<Camera>();if(camera!=null&&i<2){var center=i==0?new Vector3(0,0,-40):Vector3.zero;camera.transform.position=center+(i==0?new Vector3(0,28,-45):new Vector3(0,60,-85));camera.transform.LookAt(center);}
                    CreateNotice();EditorSceneManager.SaveScene(scene);
                }
                CharacterGeometry.Require(AssetDatabase.CopyAsset("Assets/Game/Content/Characters/EnemyModelsBatch/Collection01/Menu.unity",scenes+"/Menu.unity"),"Cannot copy menu.");var menu=EditorSceneManager.OpenScene(scenes+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");session.enterDefaultOnStart=true;
                var oldRouter=Object.FindFirstObjectByType<EnemyBatchRuntime>();var smoke=oldRouter.smoke;Object.DestroyImmediate(oldRouter);var probe=new GameObject("Roster UI and binding checks (opt-in)").AddComponent<UnifiedRosterProbe>();var router=new GameObject("Unified roster test case selector").AddComponent<UnifiedRosterCaseRouter>();router.smoke=smoke;router.probe=probe;
                router.entries=Enumerable.Range(0,3).Select(i=>new UnifiedRosterCaseRouter.Entry{key=i==0?"regular":i==1?"all":"large",battlefieldId=ids[i],templateId=i==2?"roster-large-wolf":"roster-male",unit=Load<UnitTypeConfig>(lib+(i==2?"/large-wolf.asset":"/male.asset")),scenario=Load<ScenarioConfig>(scenes+"/"+names[i]+"Scenario.asset"),initial=i==2?2:6,saved=i==2?3:8,expectedTemplates=i==2?3:12}).Concat(new[]{new UnifiedRosterCaseRouter.Entry{key="warrior",battlefieldId=legacyBattlefieldId,templateId="skeleton-warrior",unit=Load<UnitTypeConfig>("Assets/Game/Authoring/CharacterPipeline/Generated/SkeletonWarrior01/Unit.asset"),scenario=Load<ScenarioConfig>("Assets/Game/Content/Characters/EnemyModelsBatch/Collection01/WarriorScenario.asset"),initial=64,saved=96,expectedTemplates=2}}).ToArray();probe.excludedLargeTemplate=Load<UnitTypeConfig>(lib+"/large-wolf.asset");
                smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.expectedTemplate=router.entries[0].unit;smoke.battlefieldId=ids[0];smoke.templateId="roster-male";smoke.planSlot="Unified-regular";smoke.initialTrialCount=6;smoke.savedTrialCount=8;smoke.preBattleCheck=probe;
                var runtime=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();runtime.planDirectory="UnifiedRosterPlans";runtime.previewName="Unified Roster";CreateNotice();EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            report.regularTemplates=12;report.largeTemplates=3;report.passed=true;File.WriteAllText(Log+"/library-report.json",JsonUtility.ToJson(report,true));
        }
        public static void FinishExistingScenes()
        {
            string lib=Root+"/Library",scenes=Root+"/Scenes";string[] names={"Regular","AllRegular","Large"},ids={"unified-regular","unified-all-regular","unified-large"};
            string legacyBattlefieldId=Load<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset").entries[3].id;
            var report=new LibraryReport();var sources=Sources();for(int i=0;i<12;i++){var unit=Load<UnitTypeConfig>(lib+"/"+Keys[i]+".asset");report.models.Add(new LibraryEntry{key=Keys[i],title=unit.unitTypeName,source=sources[i],unit=AssetDatabase.GetAssetPath(unit),profile=AssetDatabase.GetAssetPath(unit.renderConfig.vatProfile),vertices=unit.renderConfig.nearMesh.vertexCount});}
            foreach(string key in new[]{"large-wolf","large-bull"}){var unit=Load<UnitTypeConfig>(lib+"/"+key+".asset");report.models.Add(new LibraryEntry{key=key,title=unit.unitTypeName,unit=AssetDatabase.GetAssetPath(unit),profile=AssetDatabase.GetAssetPath(unit.renderConfig.vatProfile),vertices=unit.renderConfig.nearMesh.vertexCount,large=true});}
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<3;i++)
                {
                    var scene=EditorSceneManager.OpenScene(scenes+"/"+names[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(scenes+"/"+names[i]+"Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);var deployment=manager.GetComponent<WarSandboxRuntimeDeployment>();deployment.rosterPolicy=Load<WarSandboxRosterPolicy>(Root+(i==2?"/LargePolicy.asset":"/RegularPolicy.asset"));
                    if(i<2){var system=Object.Instantiate(manager.systemConfig);var sim=Object.Instantiate(system.simulationConfig);sim.simulationWorldSize=new Vector2(220,220);sim.cellSize=3;AssetDatabase.CreateAsset(sim,scenes+"/"+names[i]+"Simulation.asset");system.simulationConfig=sim;AssetDatabase.CreateAsset(system,scenes+"/"+names[i]+"System.asset");manager.systemConfig=system;}
                    var camera=Object.FindFirstObjectByType<Camera>();if(camera!=null&&i<2){var center=i==0?new Vector3(0,0,-40):Vector3.zero;camera.transform.position=center+(i==0?new Vector3(0,28,-45):new Vector3(0,60,-85));camera.transform.LookAt(center);}
                    CreateNotice();EditorSceneManager.SaveScene(scene);
                }
                CharacterGeometry.Require(AssetDatabase.CopyAsset("Assets/Game/Content/Characters/EnemyModelsBatch/Collection01/Menu.unity",scenes+"/Menu.unity"),"Cannot copy menu.");var menu=EditorSceneManager.OpenScene(scenes+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");session.enterDefaultOnStart=true;
                var oldRouter=Object.FindFirstObjectByType<EnemyBatchRuntime>();var smoke=oldRouter.smoke;Object.DestroyImmediate(oldRouter);var probe=new GameObject("Roster UI and binding checks (opt-in)").AddComponent<UnifiedRosterProbe>();var router=new GameObject("Unified roster test case selector").AddComponent<UnifiedRosterCaseRouter>();router.smoke=smoke;router.probe=probe;
                router.entries=Enumerable.Range(0,3).Select(i=>new UnifiedRosterCaseRouter.Entry{key=i==0?"regular":i==1?"all":"large",battlefieldId=ids[i],templateId=i==2?"roster-large-wolf":"roster-male",unit=Load<UnitTypeConfig>(lib+(i==2?"/large-wolf.asset":"/male.asset")),scenario=Load<ScenarioConfig>(scenes+"/"+names[i]+"Scenario.asset"),initial=i==2?2:6,saved=i==2?3:8,expectedTemplates=i==2?3:12}).Concat(new[]{new UnifiedRosterCaseRouter.Entry{key="warrior",battlefieldId=legacyBattlefieldId,templateId="skeleton-warrior",unit=Load<UnitTypeConfig>("Assets/Game/Authoring/CharacterPipeline/Generated/SkeletonWarrior01/Unit.asset"),scenario=Load<ScenarioConfig>("Assets/Game/Content/Characters/EnemyModelsBatch/Collection01/WarriorScenario.asset"),initial=64,saved=96,expectedTemplates=2}}).ToArray();probe.excludedLargeTemplate=Load<UnitTypeConfig>(lib+"/large-wolf.asset");
                smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.expectedTemplate=router.entries[0].unit;smoke.battlefieldId=ids[0];smoke.templateId="roster-male";smoke.planSlot="Unified-regular";smoke.initialTrialCount=6;smoke.savedTrialCount=8;smoke.preBattleCheck=probe;
                var runtime=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();runtime.planDirectory="UnifiedRosterPlans";runtime.previewName="Unified Roster";CreateNotice();EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            report.regularTemplates=12;report.largeTemplates=3;report.passed=true;File.WriteAllText(Log+"/library-report.json",JsonUtility.ToJson(report,true));Debug.Log("UNIFIED_SCENES_READY");
        }
        static void CreateNotice()
        {
            foreach(var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))if(t.text.Contains("Knight pilot"))t.text="统一角色库 · 非官方本地整合预览";
            var go=new GameObject("UnityChan UCL required attribution",typeof(Canvas));var c=go.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=600;var label=new GameObject("UCL Notice",typeof(RectTransform),typeof(UnityEngine.UI.Text));label.transform.SetParent(go.transform,false);var r=label.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,0);r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(12,20);r.sizeDelta=new Vector2(950,20);var text=label.GetComponent<UnityEngine.UI.Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.text="This work is provided under Unity-Chan License Terms. © Unity Technologies Japan/UCL · Unofficial local preview";text.fontSize=11;text.color=Color.white;text.raycastTarget=false;label.AddComponent<UnityEngine.UI.Outline>().effectColor=Color.black;
        }
        public static void Build01()
        {
            string dest="Builds/UnifiedRoster-20260930-01";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");var cat=Load<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");var scenes=new[]{Root+"/Scenes/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray();var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Unified roster build failed.");File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
