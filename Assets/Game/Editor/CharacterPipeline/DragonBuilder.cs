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
    // Super-large tier, first batch: two official Quaternius Ultimate Monsters dragons (CC0).
    // Every baked coordinate is kept inside +-7.8m so the existing Half VAT encoding stays in the <=1.95mm rounding band;
    // no shared pipeline, engine, or old asset is modified.
    public static class DragonBuilder
    {
        const string Source="Assets/Art/Source/CharacterPilot/QuaterniusDragons";
        const string AtlasPath="Assets/Art/Source/CharacterPilot/QuaterniusMonsters/Atlas_Monsters.png";
        static string Log="Logs/AgentDragons";
        static string Prepared="Assets/Game/Content/Characters/Dragons/Prepared02";
        const string Knight="Assets/Game/Authoring/CharacterPipeline/Generated/Knight04";
        const string LargeLibrary="Assets/Game/Content/Characters/NonhumanBatch2/Prepared01/Integrated";
        const string LargeScene="Assets/Game/Content/Characters/UnifiedRoster/Version03/Scenes/LargeBattlefield.unity";
        static string Newest="Assets/Game/Content/Characters/Cavalry/Prepared03/Integrated";
        // Playtest 2: fireball with impact splash (user-approved opt-in core field CombatConfig.projectileSplashRadius). Off = earlier melee dragons.
        static bool Fireball=false;static float FireGravity=-4f,FireSpeed=16f;static readonly float[] SplashRadii={3f,3.5f};static readonly float[] MouthHeights={3.8f,3.4f};
        static void ConfigureFireball(UnitTypeConfig u,int i)
        {
            var c=u.combatConfig;c.projectileRange=14f;c.targetAcquireRadius=20f;c.projectileSpeed=FireSpeed;c.projectileGravity=FireGravity;c.projectileHitRadius=.6f;c.projectileMaxLifetime=3f;c.projectileTrailLength=3.5f;
            c.projectileOriginHeight=MouthHeights[i];c.attackReleasePhase=.5f;c.projectileSplashRadius=SplashRadii[i];CharacterPipeline.Save(c);
        }
        public static void IntegrateFire01()
        {
            Prepared="Assets/Game/Content/Characters/Dragons/Prepared03";Newest="Assets/Game/Content/Characters/Cavalry/Prepared04/Integrated";Log="Logs/AgentDragonFire";Fireball=true;
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon fire integration required.");CharacterPipeline.EnsureFolder(Prepared);CreateIntegratedCollection();Debug.Log("DRAGON_FIRE_READY");
        }
        public static void IntegrateFire02()
        {
            // Fire01 lobbed (gravity -4 => engine loft apex ~10m, ~2.8s flight): charging knights left the 3m splash before impact, so kills were slower than melee.
            // Fire02: flat direct fireball (~0.8s at 14m); a miss continues into the ground and still splashes.
            Prepared="Assets/Game/Content/Characters/Dragons/Prepared04";Newest="Assets/Game/Content/Characters/Cavalry/Prepared04/Integrated";Log="Logs/AgentDragonFire/fire02";Fireball=true;FireGravity=0f;FireSpeed=18f;
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon fire integration required.");Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);CreateIntegratedCollection();Debug.Log("DRAGON_FIRE_READY");
        }
        public static void BuildFire02()
        {
            string dest="Builds/UnifiedRoster-20260930-05",folder="Assets/Game/Content/Characters/Dragons/Prepared04/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");
            foreach(string g in new[]{"Dragon02","Dragon_Evolved02","MountedKnight03"}){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/"+g+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model must not be built: "+g);}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Fire integration build failed.");
            File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");Debug.Log("FIRE_BUILD_SUCCEEDED");
        }
        // User option 2: both dragon battlefields use the bodied Dragon_Evolved. The 飞龙 slot is a 78% re-bake (wingspan ~5.8m) keeping its own stats (HP1000/ATK60/splash 3m).
        const float YoungHeight=3.3f;const string YoungOutput="Dragon_EvolvedYoung03";
        static void UseYoungSlot(){ModelSource=new[]{1,1};OutputNames=new[]{YoungOutput,"Dragon_Evolved02"};TargetHeights[0]=YoungHeight;}
        public static void PrepareYoung03()
        {
            Prepared="Assets/Game/Content/Characters/Dragons/Prepared05";Log="Logs/AgentDragonFire/young03";UseYoungSlot();
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon preparation required.");CharacterGeometry.Require(!File.Exists(CharacterPipeline.Root+"/Recipes/"+YoungOutput+".asset"),"Never overwrite recipe.");
            Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);PrepareOne(0);Debug.Log("DRAGON_YOUNG_READY");
        }
        public static void IntegrateFire03()
        {
            Prepared="Assets/Game/Content/Characters/Dragons/Prepared05";Newest="Assets/Game/Content/Characters/Cavalry/Prepared05/Integrated";Log="Logs/AgentDragonFire/young03";UseYoungSlot();Fireball=true;FireGravity=0f;FireSpeed=18f;
            var young=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(0)+"/PipelineReport.json"));var adult=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(1)+"/PipelineReport.json"));
            MouthHeights[0]=MouthHeights[1]*young.targetBodyHeight/adult.targetBodyHeight;Debug.Log("YOUNG_MOUTH "+MouthHeights[0].ToString("F3"));
            CreateIntegratedCollection();Debug.Log("DRAGON_FIRE_READY");
        }
        public static void BuildFire03()
        {
            string dest="Builds/UnifiedRoster-20260930-06",folder="Assets/Game/Content/Characters/Dragons/Prepared05/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");
            foreach(string g in new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"}){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/"+g+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model must not be built: "+g);}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Fire integration build failed.");
            File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");Debug.Log("FIRE_BUILD_SUCCEEDED");
        }
        // Overnight task 2: visible splash. Dragon scenes get their own ProjectileRenderConfig with ProjectileImpact.mat
        // (ground fire ring = splash radius + flash); the trail shader draws splash shots as a fire streak on its own.
        static Material ImpactMaterial;static float ImpactDuration=.6f;
        // Fire05: same integration with the LDR fire palette (readable without bloom) and a longer 0.9 s ring.
        public static void IntegrateFire05()
        {
            Prepared="Assets/Game/Content/Characters/Dragons/Prepared07";Newest="Assets/Game/Content/Characters/Cavalry/Prepared05/Integrated";Log="Logs/AgentImpactFx/fire05";UseYoungSlot();Fireball=true;FireGravity=0f;FireSpeed=18f;ImpactDuration=.9f;
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon integration required.");Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            var young=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(0)+"/PipelineReport.json"));var adult=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(1)+"/PipelineReport.json"));
            MouthHeights[0]=MouthHeights[1]*young.targetBodyHeight/adult.targetBodyHeight;
            var shader=Load<Shader>("Assets/MassEngine/Projectiles/Shaders/ProjectileImpact.shader");ImpactMaterial=new Material(shader){name="DragonFireImpact",enableInstancing=true};ImpactMaterial.SetFloat("_ProjectileImpactDuration",ImpactDuration);AssetDatabase.CreateAsset(ImpactMaterial,Prepared+"/DragonFireImpact.mat");
            CreateIntegratedCollection();Debug.Log("DRAGON_FIRE_FX_READY");
        }
        public static void BuildFire05(){BuildRoster("Builds/UnifiedRoster-20260930-08","Assets/Game/Content/Characters/Dragons/Prepared07/Integrated",new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"});}
        // Overnight task 3: "dragon vs dense phalanx" battlefields. Break-even measured by DragonPhalanxBalanceTests
        // (2 dragons vs a 0.8/m2 knight block, both armies on the HUD default Attack order): young ~90-95, evolved ~160-170.
        // Own roster policy (256 units) and per-cell capacity 256 = the unit cap, so the grid can never overflow
        // (the shipped 64/64 pair is safe only because the dragon menus stop at 64 units).
        static int[] PhalanxKnights={90,160};
        public static void IntegrateFire06(){IntegratePhalanx("Assets/Game/Content/Characters/Dragons/Prepared09","Logs/AgentPhalanx/fire06b");}
        // Fire07 = Fire06 with the briefing matching the shipped-scene measurement (knights win narrowly, 3 and 15 left).
        public static void IntegrateFire07(){IntegratePhalanx("Assets/Game/Content/Characters/Dragons/Prepared10","Logs/AgentPhalanx/fire07");}
        // Fire08: at break-even the GPU outcome flips between runs (Fire07 scenes: dragons won with 1 left / knights won with 3-21 left),
        // so the briefing states that instead of naming a winner.
        public static void IntegrateFire08(){IntegratePhalanx("Assets/Game/Content/Characters/Dragons/Prepared11","Logs/AgentPhalanx/fire08");}
        public static void BuildFire08(){BuildRoster("Builds/UnifiedRoster-20260930-11","Assets/Game/Content/Characters/Dragons/Prepared11/Integrated",new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"});}
        public static void BuildFire07(){BuildRoster("Builds/UnifiedRoster-20260930-10","Assets/Game/Content/Characters/Dragons/Prepared10/Integrated",new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"});}
        // Fire09 (overnight task 6): same collection on top of the charging cavalry (Cavalry/Prepared06).
        public static void IntegrateFire09(){IntegratePhalanx("Assets/Game/Content/Characters/Dragons/Prepared12","Logs/AgentCharge/fire09","Assets/Game/Content/Characters/Cavalry/Prepared06/Integrated");}
        // User decision 2026-10-01: evolved-dragon phalanx 160 -> 150 knights (knights won narrowly three times at 160); cavalry = Prepared07 (damage 45).
        public static void IntegrateFire10(){PhalanxKnights=new[]{90,150};IntegratePhalanx("Assets/Game/Content/Characters/Dragons/Prepared13","Logs/AgentGiants/fire10","Assets/Game/Content/Characters/Cavalry/Prepared07/Integrated");}
        public static void BuildFire09(){BuildRoster("Builds/UnifiedRoster-20260930-12","Assets/Game/Content/Characters/Dragons/Prepared12/Integrated",new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"});}
        static void IntegratePhalanx(string prepared,string log,string newest="Assets/Game/Content/Characters/Cavalry/Prepared05/Integrated")
        {
            Prepared=prepared;Newest=newest;Log=log;UseYoungSlot();Fireball=true;FireGravity=0f;FireSpeed=18f;ImpactDuration=.9f;
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon integration required.");Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            var young=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(0)+"/PipelineReport.json"));var adult=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(1)+"/PipelineReport.json"));
            MouthHeights[0]=MouthHeights[1]*young.targetBodyHeight/adult.targetBodyHeight;
            var shader=Load<Shader>("Assets/MassEngine/Projectiles/Shaders/ProjectileImpact.shader");ImpactMaterial=new Material(shader){name="DragonFireImpact",enableInstancing=true};ImpactMaterial.SetFloat("_ProjectileImpactDuration",ImpactDuration);AssetDatabase.CreateAsset(ImpactMaterial,Prepared+"/DragonFireImpact.mat");
            CreateIntegratedCollection();AddPhalanx();Debug.Log("DRAGON_PHALANX_READY");
        }
        static void AddPhalanx()
        {
            string folder=Prepared+"/Integrated",lib=folder+"/Library";
            // OpenScene(Single) unloads unreferenced assets, so everything is (re)loaded by path where it is used.
            var basePolicy=Load<WarSandboxRosterPolicy>(folder+"/LargePolicy.asset");var control=Load<UnitTypeConfig>(lib+"/DragonSizedKnight.asset");
            float maxRadius=basePolicy.maximumRadius-.1f;
            for(int i=0;i<2;i++)
            {
                var k=CharacterPipeline.CloneUnit(control,lib,"DragonPhalanxKnight"+PhalanxKnights[i]);k.unitTypeName="密集方阵骑士（"+PhalanxKnights[i]+"）";k.teamId=1;
                k.spawnConfig.unitCount=PhalanxKnights[i];k.spawnConfig.spawnCenter=new Vector3(40,0,0);k.spawnConfig.spawnSize=Vector3.zero;k.spawnConfig.formationDensity=.8f;k.spawnConfig.formationAspect=2f;
                CharacterPipeline.Save(k.spawnConfig);CharacterPipeline.Save(k);
            }
            var policy=Object.Instantiate(basePolicy);policy.name="PhalanxRoster";policy.maximumUnits=256;policy.templates=basePolicy.templates.Concat(Enumerable.Range(0,2).Select(i=>Load<UnitTypeConfig>(lib+"/DragonPhalanxKnight"+PhalanxKnights[i]+".asset"))).ToArray();
            policy.explanation=basePolicy.explanation+" 方阵战场：守方为密集骑士方阵（0.8 人/㎡），火球溅射对密集阵杀伤最大；上限 256 单位。";AssetDatabase.CreateAsset(policy,folder+"/PhalanxPolicy.asset");
            CharacterGeometry.Require(policy.TryValidateDefinition(out string perr),perr);
            string policyPath=folder+"/PhalanxPolicy.asset",matPath=Prepared+"/DragonFireImpact.mat";AssetDatabase.SaveAssets();
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<2;i++)
                {
                    var dragon=Load<UnitTypeConfig>(lib+"/"+Keys[i]+".asset");var knight=Load<UnitTypeConfig>(lib+"/DragonPhalanxKnight"+PhalanxKnights[i]+".asset");
                    CharacterGeometry.Require(dragon!=null&&knight!=null,"Phalanx units missing.");
                    var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{dragon,knight};AssetDatabase.CreateAsset(scenario,folder+"/"+Keys[i]+"PhalanxScenario.asset");
                    string battle=folder+"/"+Keys[i]+"PhalanxBattlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(LargeScene,battle),"Cannot copy phalanx scene.");
                    var scene=EditorSceneManager.OpenScene(battle,OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=scenario;manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(policyPath);
                    var system=Object.Instantiate(manager.systemConfig);var sim=Object.Instantiate(system.simulationConfig);sim.cellSize=Mathf.Ceil(maxRadius*2+1);sim.simulationWorldSize=new Vector2(240,240);sim.maxAgentsPerCell=256;AssetDatabase.CreateAsset(sim,folder+"/"+Keys[i]+"PhalanxSimulation.asset");system.simulationConfig=sim;
                    var impact=Load<Material>(matPath);CharacterGeometry.Require(system.projectileRenderConfig!=null&&impact!=null,"Phalanx scenes need the impact effect.");var prc=Object.Instantiate(system.projectileRenderConfig);prc.impactMaterial=impact;prc.impactDuration=ImpactDuration;AssetDatabase.CreateAsset(prc,folder+"/"+Keys[i]+"PhalanxProjectileRender.asset");system.projectileRenderConfig=prc;
                    AssetDatabase.CreateAsset(system,folder+"/"+Keys[i]+"PhalanxSystem.asset");manager.systemConfig=system;EditorSceneManager.SaveScene(scene);
                }
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            var catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var entries=catalog.entries.ToList();var templates=catalog.templates.ToList();
            for(int i=0;i<2;i++)
            {
                var source=entries.Single(x=>x.id=="dragons-"+Keys[i]);var e=source.CopyIdentity();e.id="dragons-"+Keys[i]+"-phalanx";e.displayName=Titles[i]+" vs 密集方阵";e.scenePath=folder+"/"+Keys[i]+"PhalanxBattlefield.unity";e.preview=null;
                e.description="2 只"+Titles[i]+"对 "+PhalanxKnights[i]+" 名密集方阵骑士（0.8 人/㎡），按实测势均力敌点配置：默认命令下双方几乎同归于尽，胜负在一线之间（多次实测互有胜负）；调整阵型或命令即可左右结果。";
                e.briefing="火球落点半径 "+SplashRadii[i]+"m 内敌人全部受伤，密集阵平均每发伤及 5–10 人；散开阵型能明显减少损失。守方下达“坚守”会被巨龙在 14m 外持续轰炸。";
                entries.Insert(entries.IndexOf(source)+1,e);templates.Add(new WarSandboxUnitTemplateEntry{templateId="dragons-phalanx-knight-"+PhalanxKnights[i],revision=1,config=Load<UnitTypeConfig>(lib+"/DragonPhalanxKnight"+PhalanxKnights[i]+".asset")});
            }
            catalog.entries=entries.ToArray();catalog.templates=templates.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            File.WriteAllText(Log+"/phalanx-ready.json","{\"passed\":true,\"scenes\":2,\"knights\":["+PhalanxKnights[0]+","+PhalanxKnights[1]+"],\"maximumUnits\":256,\"maxAgentsPerCell\":256,\"catalogEntries\":"+catalog.entries.Length+"}");
        }
        public static void BuildFire06(){BuildRoster("Builds/UnifiedRoster-20260930-09","Assets/Game/Content/Characters/Dragons/Prepared09/Integrated",new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"});}
        public static void IntegrateFire04()
        {
            Prepared="Assets/Game/Content/Characters/Dragons/Prepared06";Newest="Assets/Game/Content/Characters/Cavalry/Prepared05/Integrated";Log="Logs/AgentImpactFx/fire04";UseYoungSlot();Fireball=true;FireGravity=0f;FireSpeed=18f;
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon integration required.");Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            var young=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(0)+"/PipelineReport.json"));var adult=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(1)+"/PipelineReport.json"));
            MouthHeights[0]=MouthHeights[1]*young.targetBodyHeight/adult.targetBodyHeight;
            var shader=Load<Shader>("Assets/MassEngine/Projectiles/Shaders/ProjectileImpact.shader");ImpactMaterial=new Material(shader){name="DragonFireImpact",enableInstancing=true};AssetDatabase.CreateAsset(ImpactMaterial,Prepared+"/DragonFireImpact.mat");
            CreateIntegratedCollection();Debug.Log("DRAGON_FIRE_FX_READY");
        }
        static void BuildRoster(string dest,string folder,string[] models)
        {
            CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");
            foreach(string g in models){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/"+g+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model must not be built: "+g);}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Roster build failed.");
            File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");Debug.Log("FIRE_BUILD_SUCCEEDED");
        }
        public static void BuildFire04(){BuildRoster("Builds/UnifiedRoster-20260930-07","Assets/Game/Content/Characters/Dragons/Prepared06/Integrated",new[]{YoungOutput,"Dragon_Evolved02","MountedKnight04"});}
        public static void BuildFire01()
        {
            string dest="Builds/UnifiedRoster-20260930-04",folder="Assets/Game/Content/Characters/Dragons/Prepared03/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");
            foreach(string g in new[]{"Dragon02","Dragon_Evolved02","MountedKnight03"}){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/"+g+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model must not be built: "+g);}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Fire integration build failed.");
            File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
        const float CoordinateLimit=7.9f,PrecisionBudget=.00235f;
        // Worst-case Half rounding (half ULP per axis) combined as a 3D distance, matching the independent geometry gate.
        static float HalfAxis(float v){v=Mathf.Abs(v);if(v<6.1e-5f)return 3e-8f;int e=Mathf.FloorToInt(Mathf.Log(v,2));return Mathf.Pow(2,e-11);}
        static float HalfBound(Vector3 c){float x=HalfAxis(c.x),y=HalfAxis(c.y),z=HalfAxis(c.z);return Mathf.Sqrt(x*x+y*y+z*z);}
        static readonly string[] Names={"Dragon","Dragon_Evolved"};
        static readonly string[] Keys={"dragon","dragon-evolved"};
        static readonly string[] Titles={"飞龙（超大）","进化巨龙（超大）"};
        static readonly float[] TargetHeights={6f,7.5f};
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        // Playtest 3: Quaternius "Dragon" is by design a winged head without a body. Slots can bake another source model (defaults = earlier outputs).
        static int[] ModelSource={0,1};static string[] OutputNames={"Dragon02","Dragon_Evolved02"};
        static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+OutputNames[i];
        static AnimationClip Clip(string path,string name)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Single(c=>c.name==name||c.name.EndsWith("|"+name,StringComparison.Ordinal));
        static void ConfigureImport(string path){var im=(ModelImporter)AssetImporter.GetAtPath(path);im.animationType=ModelImporterAnimationType.Generic;im.isReadable=true;im.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;im.importBlendShapes=true;im.SaveAndReimport();}
        public static void Inspect()
        {
            Directory.CreateDirectory(Log);var sb=new StringBuilder();
            foreach(string path in Directory.GetFiles(Source,"*.fbx"))
            {
                ConfigureImport(path);var im=(ModelImporter)AssetImporter.GetAtPath(path);var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);sb.AppendLine("MODEL "+path+" globalScale="+im.globalScale+" fileScale="+im.fileScale);
                foreach(var t in model.GetComponentsInChildren<Transform>(true))sb.AppendLine("NODE "+AnimationUtility.CalculateTransformPath(t,model.transform)+" p="+t.localPosition.ToString("F5")+" r="+t.localEulerAngles.ToString("F3")+" s="+t.localScale.ToString("F6"));
                foreach(var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var skin=r as SkinnedMeshRenderer;var mesh=skin!=null?skin.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
                    sb.AppendLine("MESH "+r.name+" verts="+mesh.vertexCount+" sub="+mesh.subMeshCount+" bones="+(skin==null?0:skin.bones.Length)+" shapes="+mesh.blendShapeCount+" uv="+mesh.uv.Length+" bounds="+mesh.bounds);
                    foreach(var mat in r.sharedMaterials)sb.AppendLine("MATERIAL "+mat.name+" shader="+mat.shader.name+" color="+mat.color+" texture="+(mat.mainTexture==null?"null":mat.mainTexture.name));
                    if(mesh.uv.Length>0)sb.AppendLine("UVRANGE "+mesh.uv.Min(v=>v.x)+","+mesh.uv.Max(v=>v.x)+" / "+mesh.uv.Min(v=>v.y)+","+mesh.uv.Max(v=>v.y));
                }
                foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")))
                {
                    var binds=AnimationUtility.GetCurveBindings(clip);sb.AppendLine("CLIP "+clip.name+" length="+clip.length+" rate="+clip.frameRate+" curves="+binds.Length);
                    foreach(var b in binds)
                    {
                        var c=AnimationUtility.GetEditorCurve(clip,b);float min=c.keys.Min(k=>k.value),max=c.keys.Max(k=>k.value);
                        if(b.type!=typeof(Transform)||string.IsNullOrEmpty(b.path)||(b.propertyName.StartsWith("m_LocalScale")&&(Mathf.Abs(min-1)>1e-5f||Mathf.Abs(max-1)>1e-5f)))sb.AppendLine("SPECIAL "+b.path+" "+b.type.Name+" "+b.propertyName+" range="+min.ToString("R")+","+max.ToString("R"));
                    }
                }
            }
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());Debug.Log("DRAGON_IMPORT_READY");
        }
        public static void Extents()
        {
            var sb=new StringBuilder();var rr=ScriptableObject.CreateInstance<CharacterRecipe>();
            foreach(var name in Names)
            {
                string path=Source+"/"+name+".fbx";var stage=new GameObject("x");stage.SetActive(false);var raw=Object.Instantiate(Load<GameObject>(path),stage.transform,false);foreach(var a in raw.GetComponentsInChildren<Animator>(true))a.enabled=false;
                var bind=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(raw,rr,true));sb.AppendLine("MODEL "+name+" bind min="+bind.min.ToString("F3")+" max="+bind.max.ToString("F3"));
                foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")))
                {
                    int n=Mathf.CeilToInt(clip.length*24);
                    for(int f=0;f<=n;f+=Mathf.Max(1,n/6))
                    {
                        float t=Mathf.Min(f/24f,clip.length);clip.SampleAnimation(raw,t);var b=CharacterGeometry.BoundsOf(raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>CharacterGeometry.Skin(raw.transform,s)).ToArray());
                        var rootBone=raw.GetComponentsInChildren<Transform>(true).First(x=>x.name=="Root");sb.AppendLine("  "+clip.name+" t="+t.ToString("F3")+" min="+b.min.ToString("F3")+" max="+b.max.ToString("F3")+" root="+rootBone.position.ToString("F3"));
                    }
                    clip.SampleAnimation(raw,clip.length);var e=CharacterGeometry.BoundsOf(raw.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>CharacterGeometry.Skin(raw.transform,s)).ToArray());sb.AppendLine("  "+clip.name+" END min="+e.min.ToString("F3")+" max="+e.max.ToString("F3"));
                }
                Object.DestroyImmediate(stage);
            }
            File.WriteAllText(Log+"/extents.txt",sb.ToString());Debug.Log("DRAGON_EXTENTS_READY");
        }
        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only: static import scales removed while preserving world rest transforms; owned mesh vertices/normals/bindposes; authored local poses resampled from world transforms; final size applied before the first Half VAT encoding. Source UVs and the official monster atlas are kept.";
            public int frames,positions;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,requestedHeight,targetHeight,scale,sourceMaxAnimatedExtent,finalMaxAbsCoordinate,collisionRadius,minimumActiveY,maximumActiveY;public float halfPrecisionBoundMm,wingspan,bodyLength,hoverTop,groundOffsetSource;public bool heightLimitedByCoordinateBudget,passed;public Vector3 sourceXZCenter;public string[] clips;
        }
        public static void PrepareAll01()
        {
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh dragon preparation required.");foreach(var n in Names)CharacterGeometry.Require(!File.Exists(CharacterPipeline.Root+"/Recipes/"+n+"02.asset"),"Never overwrite recipe "+n);
            Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            for(int i=0;i<2;i++)PrepareOne(i);
            CreateIntegratedCollection();Debug.Log("DRAGON_PREPARATION_READY");
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Names[index];CharacterPipeline.EnsureFolder(folder);string modelPath=Source+"/"+Names[ModelSource[index]]+".fbx";ConfigureImport(modelPath);
            var source=Load<GameObject>(modelPath);var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.teamId=0;template.unitTypeName=Titles[index];
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(AtlasPath));m.SetColor("_BaseColor",new Color(.8f,.8f,.8f,1));AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,Names[index]+"Near");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,Names[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive dragon source preparation");stage.SetActive(false);
            var raw=Object.Instantiate(source,stage.transform,false);raw.name="RawReference";foreach(var a in raw.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var root=new GameObject(Names[index]+"Prepared");root.transform.SetParent(stage.transform,false);var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);
            var body=Object.Instantiate(source,pivot);body.name="Body";foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var evidence=new PreparationEvidence{model=Names[index],requestedHeight=TargetHeights[index]};
            var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();
            try
            {
                var byPath=raw.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,raw.transform));var restScales=byPath.ToDictionary(p=>p.Key,p=>p.Value.localScale);
                var transforms=body.GetComponentsInChildren<Transform>(true);var oldWorld=transforms.ToDictionary(t=>t,t=>t.localToWorldMatrix);var worldPositions=transforms.ToDictionary(t=>t,t=>t.position);var worldRotations=transforms.ToDictionary(t=>t,t=>t.rotation);
                foreach(var t in transforms)
                {
                    Vector3 s=t.localScale;float mean=(s.x+s.y+s.z)/3;
                    CharacterGeometry.Require(s.x>0&&s.y>0&&s.z>0&&(s-Vector3.one*mean).magnitude/mean<.0002f,"Not bounded static scale noise: "+t.name);
                    t.localScale=Vector3.one;t.SetPositionAndRotation(worldPositions[t],worldRotations[t]);
                }
                foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src=skin.sharedMesh;CharacterGeometry.Require(src.blendShapeCount==0,"Do not drop source blendshapes.");CharacterGeometry.Require(src.uv.Length==src.vertexCount,"Atlas UVs required.");var mesh=Object.Instantiate(src);mesh.name=skin.name+"Canonical";
                    Matrix4x4 vertex=skin.transform.worldToLocalMatrix*oldWorld[skin.transform];Matrix4x4 normal=vertex.inverse.transpose;
                    mesh.vertices=src.vertices.Select(v=>vertex.MultiplyPoint3x4(v)).ToArray();mesh.normals=src.normals.Select(n=>normal.MultiplyVector(n).normalized).ToArray();
                    var binds=src.bindposes;mesh.bindposes=skin.bones.Select((bone,j)=>bone.worldToLocalMatrix*oldWorld[bone]*binds[j]*vertex.inverse).ToArray();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/"+mesh.name+".asset");skin.sharedMesh=mesh;skin.localBounds=mesh.bounds;skin.sharedMaterial=template.renderConfig.nearMaterial;
                }
                CharacterGeometry.Require(body.GetComponentsInChildren<MeshRenderer>(true).Length==0,"Unexpected rigid source; explicit conversion required.");
                string[] sourceNames={"Flying_Idle","Fast_Flying","Headbutt","Death"};var originals=sourceNames.Select(n=>Clip(modelPath,n)).ToArray();evidence.clips=originals.Select(c=>c.name+" "+c.length.ToString("F3")+"s").ToArray();
                var recipeRaw=ScriptableObject.CreateInstance<CharacterRecipe>();var originalBounds=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(raw,recipeRaw,true));Object.DestroyImmediate(recipeRaw);
                evidence.sourceHeight=originalBounds.size.y;evidence.sourceXZCenter=new Vector3(originalBounds.center.x,0,originalBounds.center.z);
                // Hovering flyer: source origin is the ground. Sample every action exactly like the VAT gate (Death stretched to its end), keep all frames above ground.
                var samples=new List<Vector3>();
                for(int action=0;action<4;action++){int n=Mathf.CeilToInt(originals[action].length*24);for(int frame=0;frame<=n+(action==3?n:0);frame++){float time=action==3?originals[action].length*frame/(2f*n):Mathf.Min(frame/24f,originals[action].length);originals[action].SampleAnimation(raw,time);foreach(var s in raw.GetComponentsInChildren<SkinnedMeshRenderer>(true))samples.AddRange(CharacterGeometry.Skin(raw.transform,s));}}
                float groundY=Mathf.Min(0,samples.Min(p=>p.y));var anchor=new Vector3(evidence.sourceXZCenter.x,groundY,evidence.sourceXZCenter.z);var rel=samples.Select(p=>p-anchor).ToArray();
                evidence.sourceMaxAnimatedExtent=rel.Max(c=>Mathf.Max(Mathf.Abs(c.x),Mathf.Max(Mathf.Abs(c.y),Mathf.Abs(c.z))));
                // Half VAT gate is a 3D distance: bound the per-vertex worst case over all axes, then take the largest passing scale.
                float scale=evidence.requestedHeight/evidence.sourceHeight;
                while(scale*evidence.sourceMaxAnimatedExtent>CoordinateLimit||rel.Max(c=>HalfBound(c*scale))>PrecisionBudget){scale*=.995f;evidence.heightLimitedByCoordinateBudget=true;}
                evidence.halfPrecisionBoundMm=rel.Max(c=>HalfBound(c*scale))*1000;evidence.wingspan=(rel.Max(c=>c.x)-rel.Min(c=>c.x))*scale;evidence.bodyLength=(rel.Max(c=>c.z)-rel.Min(c=>c.z))*scale;evidence.hoverTop=rel.Max(c=>c.y)*scale;evidence.groundOffsetSource=groundY;
                evidence.scale=scale;evidence.targetHeight=evidence.sourceHeight*scale;
                string[] actions={"Idle","Move","Attack","Death"};var mapped=new AnimationClip[4];var preparedPaths=transforms.ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,body.transform));
                var bindPositions=transforms.Select(t=>t.localPosition).ToArray();var bindRotations=transforms.Select(t=>t.localRotation).ToArray();
                for(int action=0;action<4;action++)
                {
                    var sourceClip=originals[action];int frames=Mathf.Max(1,Mathf.CeilToInt(sourceClip.length*24));var keys=preparedPaths.ToDictionary(p=>p.Key,p=>Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray());
                    foreach(var binding in AnimationUtility.GetCurveBindings(sourceClip))
                    {
                        CharacterGeometry.Require(binding.type==typeof(Transform)&&byPath.ContainsKey(binding.path),"Unmapped source binding: "+binding.path);
                        if(binding.propertyName.StartsWith("m_LocalScale."))foreach(var key in AnimationUtility.GetEditorCurve(sourceClip,binding).keys)
                        {int axis=binding.propertyName.EndsWith(".x")?0:binding.propertyName.EndsWith(".y")?1:2;float expected=restScales[binding.path][axis];float relative=Mathf.Abs(key.value/expected-1);evidence.maximumScaleDeviationFromRest=Mathf.Max(evidence.maximumScaleDeviationFromRest,relative);CharacterGeometry.Require(relative<.0002f,"Real animated scale not supported by canonical adapter.");}
                    }
                    for(int frame=0;frame<=frames;frame++)
                    {
                        float time=Mathf.Min(frame/24f,sourceClip.length);sourceClip.SampleAnimation(raw,time);
                        foreach(var pair in preparedPaths)
                        {
                            var target=pair.Value;var original=byPath[pair.Key];target.SetPositionAndRotation(original.position,original.rotation);target.localScale=Vector3.one;
                            Vector3 p=target.localPosition;Quaternion q=target.localRotation;float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int c=0;c<7;c++)keys[pair.Key][c].Add(new Keyframe(time,values[c]));
                        }
                    }
                    var clip=new AnimationClip{name=actions[action],frameRate=sourceClip.frameRate};string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
                    foreach(var pair in keys)for(int c=0;c<7;c++)
                    {
                        var curve=new AnimationCurve(pair.Value[c].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                        string path="ScalePivot/Body"+(pair.Key.Length==0?"":"/"+pair.Key);AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),props[c]),curve);
                    }
                    clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=action!=3;AnimationUtility.SetAnimationClipSettings(clip,settings);AssetDatabase.CreateAsset(clip,folder+"/"+actions[action]+".anim");mapped[action]=clip;
                    for(int frame=0;frame<frames;frame++)
                    {
                        float time=frame/24f;sourceClip.SampleAnimation(raw,time);clip.SampleAnimation(root,time);var sourceSkins=raw.GetComponentsInChildren<SkinnedMeshRenderer>(true);var resultSkins=body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        for(int part=0;part<sourceSkins.Length;part++)
                        {var expected=CharacterGeometry.Skin(raw.transform,sourceSkins[part]);var actual=CharacterGeometry.Skin(root.transform,resultSkins[part]);CharacterGeometry.Require(expected.Length==actual.Length,"Canonical topology count mismatch.");for(int v=0;v<actual.Length;v++){evidence.maximumScaledPositionError=Mathf.Max(evidence.maximumScaledPositionError,Vector3.Distance(expected[v],actual[v])*scale);evidence.positions++;}}
                        evidence.frames++;
                    }
                }
                CharacterGeometry.Require(evidence.maximumScaledPositionError<=.00025f,"Original/canonical pose mismatch exceeds0.25mm: "+evidence.maximumScaledPositionError);
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=bindPositions[i];transforms[i].localRotation=bindRotations[i];transforms[i].localScale=Vector3.one;}
                // Final dimensions are applied to owned mesh/bind translations and clip positions BEFORE the first Half VAT encoding.
                var globalScale=Matrix4x4.Scale(Vector3.one*scale);var inverseScale=Matrix4x4.Scale(Vector3.one/scale);
                foreach(var t in transforms)t.localPosition*=scale;
                foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true)){var mesh=skin.sharedMesh;mesh.vertices=mesh.vertices.Select(v=>v*scale).ToArray();mesh.bindposes=mesh.bindposes.Select(m=>globalScale*m*inverseScale).ToArray();mesh.RecalculateBounds();skin.localBounds=mesh.bounds;CharacterPipeline.Save(mesh);}
                foreach(var clip in mapped)
                {
                    foreach(var binding in AnimationUtility.GetCurveBindings(clip))if(binding.propertyName.StartsWith("m_LocalPosition.")){var curve=AnimationUtility.GetEditorCurve(clip,binding);var ks=curve.keys;for(int k=0;k<ks.Length;k++){ks[k].value*=scale;ks[k].inTangent*=scale;ks[k].outTangent*=scale;}AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(ks));}CharacterPipeline.Save(clip);
                }
                pivot.localPosition=-anchor*scale;
                var finalBindPositions=transforms.Select(t=>t.localPosition).ToArray();var finalBindRotations=transforms.Select(t=>t.localRotation).ToArray();float finalError=0;evidence.minimumActiveY=float.MaxValue;evidence.maximumActiveY=float.MinValue;
                for(int action=0;action<4;action++)for(int frame=0;frame<Mathf.CeilToInt(mapped[action].length*24);frame++)
                {
                    originals[action].SampleAnimation(raw,frame/24f);mapped[action].SampleAnimation(root,frame/24f);var originalSkins=raw.GetComponentsInChildren<SkinnedMeshRenderer>(true);var finalSkins=body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for(int part=0;part<finalSkins.Length;part++)
                    {
                        var before=CharacterGeometry.Skin(raw.transform,originalSkins[part]);var after=CharacterGeometry.Skin(root.transform,finalSkins[part]);
                        for(int v=0;v<after.Length;v++)
                        {
                            Vector3 expected=(before[v]-anchor)*scale;finalError=Mathf.Max(finalError,Vector3.Distance(expected,after[v]));var a=after[v];
                            evidence.finalMaxAbsCoordinate=Mathf.Max(evidence.finalMaxAbsCoordinate,Mathf.Max(Mathf.Abs(a.x),Mathf.Max(Mathf.Abs(a.y),Mathf.Abs(a.z))));
                            if(action<3){evidence.collisionRadius=Mathf.Max(evidence.collisionRadius,new Vector2(a.x,a.z).magnitude);evidence.minimumActiveY=Mathf.Min(evidence.minimumActiveY,a.y);evidence.maximumActiveY=Mathf.Max(evidence.maximumActiveY,a.y);}
                        }
                    }
                }
                CharacterGeometry.Require(finalError<=.00025f,"Final-size original pose gate failed: "+finalError);evidence.maximumScaledPositionError=Mathf.Max(evidence.maximumScaledPositionError,finalError);
                CharacterGeometry.Require(evidence.finalMaxAbsCoordinate<=CoordinateLimit+.05f,"Coordinate budget exceeded: "+evidence.finalMaxAbsCoordinate);CharacterGeometry.Require(evidence.halfPrecisionBoundMm<=PrecisionBudget*1000+.001f,"Half precision bound exceeded: "+evidence.halfPrecisionBoundMm);
                for(int t=0;t<transforms.Length;t++){transforms[t].localPosition=finalBindPositions[t];transforms[t].localRotation=finalBindRotations[t];}
                evidence.collisionRadius=Mathf.Ceil((evidence.collisionRadius+.15f)*10)/10;root.transform.SetParent(null,false);root.SetActive(true);CharacterGeometry.CheckHierarchy(root,"ScalePivot");var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");
                recipe.name=Names[index];recipe.model=prefab;recipe.sizeRootPath="";recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.frameRate=24;recipe.targetBodyHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(prefab,recipe,true)).size.y;recipe.groundBindFeet=false;recipe.agentRadius=evidence.collisionRadius;recipe.paletteColumns=32;recipe.paletteRows=32;recipe.lowVertexBudget=2000;recipe.maxTextureMiB=48;recipe.outputName=OutputNames[index];recipe.unitTemplate=template;
                // Super-large stats are a first functional setting, not a balance claim.
                template.combatConfig.projectileRange=0;template.combatConfig.targetAcquireRadius=16;template.combatConfig.attackInterval=mapped[2].length;template.combatConfig.attackRange=evidence.collisionRadius+.55f+.5f;template.combatConfig.maxHp=index==0?1000:1500;template.combatConfig.attackDamage=index==0?60:90;template.spawnConfig.unitCount=2;template.spawnConfig.spawnCenter=new Vector3(-45,0,0);float side=(2*evidence.collisionRadius+1)*3;template.spawnConfig.spawnSize=new Vector3(side,0,side);template.movementConfig.maxSpeed=3f;template.animationConfig.moveReferenceSpeed=3f;template.combatConfig.projectileTargetHeight=evidence.targetHeight*.5f;
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+OutputNames[index]+".asset");evidence.passed=true;File.WriteAllText(Log+"/"+Keys[index]+"-canonical-02.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/"+Keys[index]+"-report-02.json",JsonUtility.ToJson(report,true));Debug.Log("DRAGON_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices+" passed="+report.automatedPassed);
            CharacterGeometry.Require(report.automatedPassed,"Dragon pipeline gate failed: "+Keys[index]);
        }
        public static void CreateIntegratedCollection()
        {
            string folder=Prepared+"/Integrated",lib=folder+"/Library";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated directory required.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(lib);
            var oldCat=Load<WarSandboxBattlefieldCatalog>(Newest+"/Catalog.asset");var oldPolicy=Load<WarSandboxRosterPolicy>(LargeLibrary+"/LargePolicy.asset");var catalog=Object.Instantiate(oldCat);catalog.name="UnifiedDragons";catalog.defaultEntryId="dragons-"+Keys[1];
            var models=Enumerable.Range(0,2).Select(i=>Load<UnitTypeConfig>(Output(i)+"/Unit.asset")).ToArray();float maxRadius=Mathf.Max(oldPolicy.maximumRadius-.1f,models.Max(u=>u.flockingConfig.agentRadius));
            var control=CharacterPipeline.CloneUnit(oldPolicy.templates.Single(u=>u.flockingConfig.agentRadius<1),lib,"DragonSizedKnight");control.spawnConfig.unitCount=16;control.spawnConfig.spawnCenter=new Vector3(45,0,0);control.combatConfig.attackRange=maxRadius+.55f+.5f;CharacterPipeline.Save(control.spawnConfig);CharacterPipeline.Save(control.combatConfig);CharacterPipeline.Save(control);
            var extraTemplates=new List<WarSandboxUnitTemplateEntry>{new WarSandboxUnitTemplateEntry{templateId="dragons-knight-control",revision=1,config=control}};var newUnits=new UnitTypeConfig[2];var newEntries=new List<WarSandboxBattlefieldEntry>();
            for(int i=0;i<2;i++){var u=CharacterPipeline.CloneUnit(models[i],lib,Keys[i]);u.unitTypeName=Titles[i];u.teamId=0;u.spawnConfig.unitCount=2;u.spawnConfig.spawnCenter=new Vector3(-45,0,0);CharacterPipeline.Save(u.spawnConfig);if(Fireball)ConfigureFireball(u,i);CharacterPipeline.Save(u);newUnits[i]=u;extraTemplates.Add(new WarSandboxUnitTemplateEntry{templateId="roster-"+Keys[i],revision=1,config=u});}
            var policy=Object.Instantiate(oldPolicy);policy.name="SuperLargeRoster";policy.templates=oldPolicy.templates.Where(u=>u.flockingConfig.agentRadius>1).Concat(newUnits).Concat(new[]{control}).ToArray();policy.maximumRadius=maxRadius+.1f;policy.explanation=Fireball?"超大/大型角色库 · 巨龙与大兽限攻方，守方使用适配骑士。巨龙远程火球，落点范围伤害；飞行仅为外观，不做空中寻路。":"超大/大型角色库 · 巨龙与大兽限攻方，守方使用适配骑士。巨龙为保守圆近战，不含飞行寻路、喷火或空中碰撞。";AssetDatabase.CreateAsset(policy,folder+"/LargePolicy.asset");
            var large=oldCat.entries.Single(e=>e.id=="unified-large");
            for(int i=0;i<2;i++)
            {
                var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{newUnits[i],control};AssetDatabase.CreateAsset(scenario,folder+"/"+Keys[i]+"Scenario.asset");string battle=folder+"/"+Keys[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(LargeScene,battle),"Cannot copy new private scene.");
                var e=large.CopyIdentity();e.id="dragons-"+Keys[i];e.displayName=Titles[i]+" · 超大第一批";e.description="官方CC0 Quaternius巨龙，最终尺寸在VAT编码前处理，全动作坐标≤7.8m。可在大型菜单中与巨狼/公牛/恐龙等切换。";e.briefing=Fireball?"巨龙吐火球（射程14m，落点半径"+SplashRadii[i]+"m内敌人全部受伤，无友伤）；飞行动作仅为外观，不做空中寻路。":"超大单位仍是地面保守圆近战；飞行动作仅为外观，不做空中寻路/喷火/精确碰撞。";e.scenePath=battle;e.preview=null;newEntries.Add(e);
            }
            catalog.entries=newEntries.Concat(oldCat.entries.Select(e=>e.CopyIdentity())).ToArray();catalog.templates=oldCat.templates.Concat(extraTemplates).ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<2;i++)
                {
                    var scene=EditorSceneManager.OpenScene(folder+"/"+Keys[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+Keys[i]+"Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(folder+"/LargePolicy.asset");
                    var system=Object.Instantiate(manager.systemConfig);var sim=Object.Instantiate(system.simulationConfig);sim.cellSize=Mathf.Ceil(maxRadius*2+1);sim.simulationWorldSize=new Vector2(240,240);sim.maxAgentsPerCell=64;AssetDatabase.CreateAsset(sim,folder+"/"+Keys[i]+"Simulation.asset");system.simulationConfig=sim;if(ImpactMaterial!=null){CharacterGeometry.Require(system.projectileRenderConfig!=null,"Scene has no projectile render config.");var prc=Object.Instantiate(system.projectileRenderConfig);prc.impactMaterial=ImpactMaterial;prc.impactDuration=ImpactDuration;AssetDatabase.CreateAsset(prc,folder+"/"+Keys[i]+"ProjectileRender.asset");system.projectileRenderConfig=prc;}AssetDatabase.CreateAsset(system,folder+"/"+Keys[i]+"System.asset");manager.systemConfig=system;EditorSceneManager.SaveScene(scene);
                }
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Newest+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy integrated menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var router=Object.FindFirstObjectByType<NonhumanBatchCaseRouter>();
                router.entries=Enumerable.Range(0,2).Select(i=>new NonhumanBatchCaseRouter.Entry{key=Keys[i],battlefieldId="dragons-"+Keys[i],templateId="roster-"+Keys[i],unit=Load<UnitTypeConfig>(lib+"/"+Keys[i]+".asset"),scenario=Load<ScenarioConfig>(folder+"/"+Keys[i]+"Scenario.asset"),initial=2,saved=3,templates=policy.templates.Length,large=true}).Concat(router.entries).ToArray();
                var smoke=router.smoke;smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.expectedTemplate=router.entries[0].unit;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.planSlot="Dragons-"+Keys[0];smoke.initialTrialCount=2;smoke.savedTrialCount=3;smoke.preBattleCheck=router.probe;
                var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="UnifiedRosterPlans";preview.previewName="Unified Roster + Dragons";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/integration-ready.json","{\"passed\":true,\"newModels\":2,\"largeMenuTemplates\":"+policy.templates.Length+",\"maximumRadius\":"+policy.maximumRadius.ToString("R")+",\"oldSourceEdited\":false}");
        }
        public static void Build01()
        {
            string dest="Builds/UnifiedDragons-20260930-01",folder=Prepared+"/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");foreach(int i in new[]{0,1}){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model must not be built.");}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Dragon integration build failed.");File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}

