using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MassEngine.Editor;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor
{
    // New-unit batch 5: three more regular-size troops (official CC0 Quaternius Ultimate Monsters "Big": Cactoro, Frog, Monkroose)
    // through the batch-4 path (Troops4Builder, itself the MonsterBatchBuilder path). Cactoro is the first regular-size ranged troop of
    // these batches (straight spine shots). Batch 4 and every earlier collection are read, never written: the new
    // regular policy is the batch-4 policy (16 templates, knight legion included) plus the three troops.
    public static class Troops5Builder
    {
        const string Source="Assets/Art/Source/CharacterPilot/QuaterniusTroops5";
        const string AtlasPath="Assets/Art/Source/CharacterPilot/QuaterniusMonsters/Atlas_Monsters.png";
        const string Log="Logs/AgentMonsters3";
        public const string Prepared="Assets/Game/Content/Characters/Troops5/Prepared01";
        public const string Integrated=Prepared+"/Integrated";
        const string Knight="Assets/Game/Authoring/CharacterPipeline/Generated/Knight04";
        public const string RegularScene="Assets/Game/Content/Characters/UnifiedRoster/Version03/Scenes/RegularBattlefield.unity";
        public const string RegularPolicyPath=Troops4Builder.Integrated+"/RegularPolicy.asset";
        public const string KnightLegion=Troops4Builder.Integrated+"/Library/"+Troops4Builder.KnightLegionKey+".asset";
        public const string BaseCatalog=Troops4Builder.Integrated+"/Catalog.asset";
        public static readonly string[] Names={"Cactoro","Frog","Monkroose"};
        static readonly string[] Files={"Cactoro.fbx","Frog.fbx","Monkroose.fbx"};
        public static readonly string[] Keys={"cactoro","frog","monkroose"};
        public static readonly string[] Titles={"仙人掌枪手","毒蛙","猴獴战士"};
        static readonly string[] Roles={"远程抛射","轻装群攻","均衡近战"};
        public static readonly bool[] Ranged={true,false,false};
        static readonly string[] MoveClips={"Run/Walk","Run/Walk","Run/Walk"};
        static readonly string[] AttackClips={"Weapon/Punch","Weapon/Punch","Weapon/Punch"};
        static readonly float[] Heights={1.9f,1.6f,2.0f};
        public static readonly float[] Radii={.5f,.45f,.55f};
        // Generated templates keep the first-pass values; Integrate01 writes the shipped values onto the library units.
        static readonly int[] FirstHp={80,60,120},FirstDamage={5,8,13};static readonly float[] FirstSpeed={3f,5f,4f},FirstMoveReference={3f,4f,3.5f};
        // Shipped values (first functional values against the 6 m/s knight legion, HP 100 / 10; not a balance claim).
        public static readonly int[] Hp={100,70,120},Damage={16,10,12},Counts={80,95,60};
        public static readonly float[] Speed={3f,5f,4f};
        static readonly float[] MoveReference={3f,4f,3.5f};
        public const float SpineRange=16f,SpineSpeed=18f,SpineHitRadius=.2f;
        public static int KnightCount=>Troops4Builder.KnightCount;
        const string IntegrationRun="01";
        static readonly string[] OutputNames={"Cactoro01","Frog01","Monkroose01"};
        public static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+OutputNames[i];
        public static string LibraryUnit(int i)=>Integrated+"/Library/troops5-"+Keys[i]+".asset";
        public static string Battlefield(int i)=>Integrated+"/"+Keys[i]+"Battlefield.unity";
        public static string EntryId(int i)=>"troops5-"+Keys[i];
        public static string TemplateId(int i)=>"roster-"+Keys[i];
        static string SourcePath(int i)=>Source+"/"+Files[i];
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static AnimationClip ClipAny(string path,string names){foreach(var n in names.Split('/')){var c=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview__")).FirstOrDefault(x=>x.name==n||x.name.EndsWith("|"+n,StringComparison.Ordinal));if(c!=null)return c;}throw new InvalidOperationException("No clip "+names+" in "+path+": "+string.Join(",",AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Select(x=>x.name)));}
        static bool ImportMatches(ModelImporter im)=>im.animationType==ModelImporterAnimationType.Generic&&im.isReadable&&im.materialImportMode==ModelImporterMaterialImportMode.ImportStandard&&im.importBlendShapes;
        // Only the batch-5 sources (new in this batch) may have their import settings set; nothing else is touched.
        static void ConfigureImport(string path){CharacterGeometry.Require(path.StartsWith(Source+"/"),"Only batch-5 sources may be configured: "+path);var im=(ModelImporter)AssetImporter.GetAtPath(path);CharacterGeometry.Require(im!=null,"Not imported: "+path);if(ImportMatches(im))return;im.animationType=ModelImporterAnimationType.Generic;im.isReadable=true;im.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;im.importBlendShapes=true;im.SaveAndReimport();}

        // Import settings + clip names + rest bounds of the batch-5 sources, to confirm the clip choices before preparing.
        public static void Inspect01()
        {
            Directory.CreateDirectory(Log);var sb=new StringBuilder();
            for(int i=0;i<Names.Length;i++)
            {
                string path=SourcePath(i);if(!File.Exists(path)){sb.AppendLine(Names[i]+": missing "+path);continue;}
                ConfigureImport(path);var im=(ModelImporter)AssetImporter.GetAtPath(path);var model=Load<GameObject>(path);
                var recipeRaw=ScriptableObject.CreateInstance<CharacterRecipe>();var b=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(model,recipeRaw,true));Object.DestroyImmediate(recipeRaw);
                var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>c.name+" "+c.length.ToString("F3")+"s");
                var meshes=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s=>s.name+" v="+s.sharedMesh.vertexCount+" bones="+s.bones.Length+" shapes="+s.sharedMesh.blendShapeCount);
                sb.AppendLine(Names[i]+": importMatches="+ImportMatches(im)+" rigid="+model.GetComponentsInChildren<MeshRenderer>(true).Length+" bounds="+b.size.ToString("F3")+" center="+b.center.ToString("F3")+" | meshes: "+string.Join("; ",meshes)+" | clips: "+string.Join(", ",clips));
            }
            File.WriteAllText(Log+"/troops5-inspect-01.txt",sb.ToString());Debug.Log("TROOPS5_INSPECT_READY\n"+sb);
        }

        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only (MonsterBatchBuilder path): static import scales removed while preserving world rest transforms; owned mesh vertices/normals/bindposes; authored local poses resampled from world transforms and compared at every baked frame; the pipeline applies targetBodyHeight at ScalePivot. Source UVs and the official monster atlas are kept.";
            public int frames,positions;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,targetHeight,scale,idleBodyRadius,agentRadius,attackRange,attackInterval;public bool passed;public string[] clips;
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Names[index];CharacterPipeline.EnsureFolder(folder);string modelPath=SourcePath(index);ConfigureImport(modelPath);
            var source=Load<GameObject>(modelPath);var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.teamId=0;template.unitTypeName=Titles[index];
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(AtlasPath));m.SetColor("_BaseColor",new Color(.8f,.8f,.8f,1));AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,Names[index]+"Near");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,Names[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive troops5 source preparation");stage.SetActive(false);
            var raw=Object.Instantiate(source,stage.transform,false);raw.name="RawReference";foreach(var a in raw.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var root=new GameObject(Names[index]+"Prepared");root.transform.SetParent(stage.transform,false);var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);
            var body=Object.Instantiate(source,pivot);body.name="Body";foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var evidence=new PreparationEvidence{model=Names[index],targetHeight=Heights[index],agentRadius=Radii[index]};
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
                var recipeRaw=ScriptableObject.CreateInstance<CharacterRecipe>();var rest=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(raw,recipeRaw,true));Object.DestroyImmediate(recipeRaw);
                evidence.sourceHeight=rest.size.y;float scale=evidence.targetHeight/evidence.sourceHeight;evidence.scale=scale;
                string[] actions={"Idle","Move","Attack","Death"};var originals=new[]{ClipAny(modelPath,"Idle"),ClipAny(modelPath,MoveClips[index]),ClipAny(modelPath,AttackClips[index]),ClipAny(modelPath,"Death")};evidence.clips=originals.Select(c=>c.name+" "+c.length.ToString("F3")+"s").ToArray();
                // Body footprint for the evidence (Idle, all frames, at final size): the agent radius is the regular-roster spacing, not the weapon reach.
                for(int frame=0;frame<=Mathf.CeilToInt(originals[0].length*24);frame++){originals[0].SampleAnimation(raw,Mathf.Min(frame/24f,originals[0].length));foreach(var s in raw.GetComponentsInChildren<SkinnedMeshRenderer>(true))foreach(var p in CharacterGeometry.Skin(raw.transform,s))evidence.idleBodyRadius=Mathf.Max(evidence.idleBodyRadius,new Vector2(p.x-rest.center.x,p.z-rest.center.z).magnitude*scale);}
                var mapped=new AnimationClip[4];var preparedPaths=transforms.ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,body.transform));
                var bindPositions=transforms.Select(t=>t.localPosition).ToArray();var bindRotations=transforms.Select(t=>t.localRotation).ToArray();
                for(int action=0;action<4;action++)
                {
                    var sourceClip=originals[action];int frames=Mathf.Max(1,Mathf.CeilToInt(sourceClip.length*24));var keys=preparedPaths.ToDictionary(p=>p.Key,p=>Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray());
                    // Source scale animation may only be constant import scale or <=0.02% numerical rest noise; real squash is NOT accepted.
                    foreach(var binding in AnimationUtility.GetCurveBindings(sourceClip))
                    {
                        CharacterGeometry.Require(binding.type==typeof(Transform)&&byPath.ContainsKey(binding.path),"Unmapped source binding: "+binding.path);
                        if(binding.propertyName.StartsWith("m_LocalScale."))foreach(var k in AnimationUtility.GetEditorCurve(sourceClip,binding).keys)
                        {int axis=binding.propertyName.EndsWith(".x")?0:binding.propertyName.EndsWith(".y")?1:2;float expected=restScales[binding.path][axis];float relative=Mathf.Abs(k.value/expected-1);evidence.maximumScaleDeviationFromRest=Mathf.Max(evidence.maximumScaleDeviationFromRest,relative);CharacterGeometry.Require(relative<.0002f,"Real animated scale not supported by canonical adapter.");}
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
                CharacterGeometry.Require(evidence.maximumScaledPositionError<=.00025f,"Original/canonical pose mismatch exceeds 0.25mm: "+evidence.maximumScaledPositionError);
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=bindPositions[i];transforms[i].localRotation=bindRotations[i];transforms[i].localScale=Vector3.one;}
                root.transform.SetParent(null,false);root.SetActive(true);CharacterGeometry.CheckHierarchy(root,"ScalePivot");var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");
                recipe.name=Names[index];recipe.model=prefab;recipe.sizeRootPath="ScalePivot";recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.frameRate=24;recipe.targetBodyHeight=evidence.targetHeight;recipe.agentRadius=Radii[index];recipe.paletteColumns=32;recipe.paletteRows=32;recipe.lowVertexBudget=1600;recipe.maxTextureMiB=32;recipe.outputName=OutputNames[index];recipe.unitTemplate=template;
                // Regular melee: reach = own radius + knight radius (0.55) + the orc's 0.75 slack (orc: 0.5 + 0.55 + 0.75 = 1.8).
                evidence.attackRange=Radii[index]+.55f+.75f;evidence.attackInterval=mapped[2].length;
                var c0=template.combatConfig;c0.projectileRange=0;c0.projectileSplashRadius=0;c0.targetAcquireRadius=12;c0.attackInterval=evidence.attackInterval;c0.attackRange=evidence.attackRange;c0.maxHp=FirstHp[index];c0.attackDamage=FirstDamage[index];c0.projectileTargetHeight=evidence.targetHeight*.5f;
                template.movementConfig.maxSpeed=FirstSpeed[index];template.animationConfig.moveReferenceSpeed=FirstMoveReference[index];
                if(Ranged[index])ConfigureRanged(template,evidence.targetHeight);
                var sp=template.spawnConfig;sp.unitCount=Counts[index];sp.spawnCenter=new Vector3(-24,0,0);sp.formationDensity=.12f;sp.formationAspect=1.5f;sp.spawnSize=Vector3.zero;
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+OutputNames[index]+".asset");evidence.passed=true;File.WriteAllText(Log+"/troops5-"+Keys[index]+"-canonical-01.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/troops5-"+Keys[index]+"-report-01.json",JsonUtility.ToJson(report,true));Debug.Log("TROOPS5_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices+" passed="+report.automatedPassed);
            CharacterGeometry.Require(report.automatedPassed,"Character pipeline gate failed: "+Keys[index]);
        }
        // Regular ranged profile (Cactoro): straight spine shots (gravity 0) launched just above the head (so the rear ranks shoot
        // over their own front ranks; friendly capsules absorb shots), released mid-throw; single target, no splash. Projectiles are unguided, so arcing spines aimed at a charging 6 m/s knight land behind it;
        // a direct shot sweeps the line at body height and meets the oncoming knights (unit collision). Applied at Prepare01 and again on the shipped library unit at
        // Integrate01 (so tuning the profile only needs a re-integration).
        static void ConfigureRanged(UnitTypeConfig u,float height)
        {
            var c=u.combatConfig;c.projectileRange=SpineRange;c.targetAcquireRadius=SpineRange+4f;c.projectileSpeed=SpineSpeed;c.projectileGravity=0f;c.projectileHitRadius=SpineHitRadius;c.projectileMaxLifetime=3f;c.projectileTrailLength=1.2f;
            c.projectileOriginHeight=height*1.1f;c.projectileTargetHeight=1f;c.attackReleasePhase=.5f;c.projectileSplashRadius=0f;CharacterPipeline.Save(c);
        }
        public static void Prepare01()
        {
            for(int i=0;i<Names.Length;i++){CharacterGeometry.Require(!Directory.Exists(Output(i))&&!File.Exists(CharacterPipeline.Root+"/Recipes/"+OutputNames[i]+".asset"),"Never overwrite "+OutputNames[i]);CharacterGeometry.Require(File.Exists(SourcePath(i)),"Missing source "+SourcePath(i));CharacterGeometry.Require(!Directory.Exists(Prepared+"/"+Names[i]),"Fresh preparation required: "+Names[i]);}
            Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            for(int i=0;i<Names.Length;i++)PrepareOne(i);
            for(int i=0;i<Names.Length;i++){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Model not accepted: "+OutputNames[i]);}
            Debug.Log("TROOPS5_PREPARATION_READY "+string.Join(",",OutputNames));
        }

        // Regular battlefields: each new troop vs the batch-4 80-knight legion on a copy of the Version03 regular battlefield. The new
        // regular policy = the batch-4 policy (12 Version03 templates + batch 4 + knight legion) + the three troops. Catalog = the
        // batch-4 collection + three entries.
        // Tuning reruns: discard this batch's own Integrated output through the AssetDatabase, then run Integrate01 in a NEW editor
        // session (AssetPathToGUID includes recently deleted assets of the current session, which the never-overwrite VAT path
        // check rejects). Only ever touches Troops5/Prepared01/Integrated.
        public static void DiscardIntegrated01()
        {
            CharacterGeometry.Require(AssetDatabase.IsValidFolder(Integrated)||Directory.Exists(Integrated),"Nothing to discard: "+Integrated);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if(AssetDatabase.IsValidFolder(Integrated))CharacterGeometry.Require(AssetDatabase.DeleteAsset(Integrated),"Cannot discard "+Integrated);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CharacterGeometry.Require(!Directory.Exists(Integrated)&&string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(LibraryUnit(0),AssetPathToGUIDOptions.OnlyExistingAssets)),"Integrated output still registered.");
            Debug.Log("TROOPS5_DISCARDED "+Integrated);
        }
        public static void Integrate01()
        {
            string folder=Integrated,lib=folder+"/Library";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated directory required.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(lib);
            for(int i=0;i<Names.Length;i++){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model: "+OutputNames[i]);}
            var oldPolicy=Load<WarSandboxRosterPolicy>(RegularPolicyPath);var units=new List<UnitTypeConfig>();
            for(int i=0;i<Names.Length;i++)
            {
                var u=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(Output(i)+"/Unit.asset"),lib,"troops5-"+Keys[i]);u.unitTypeName=Titles[i];u.teamId=0;
                u.spawnConfig.unitCount=Counts[i];u.spawnConfig.spawnCenter=new Vector3(-24,0,0);
                CharacterGeometry.Require(Mathf.Approximately(u.flockingConfig.agentRadius,Radii[i])&&u.flockingConfig.agentRadius<=oldPolicy.maximumRadius,"Radius drift: "+Keys[i]);
                CharacterGeometry.Require(u.combatConfig.maxHp==FirstHp[i]&&(u.combatConfig.projectileRange>0)==Ranged[i]&&!ReferenceEquals(u.combatConfig,Load<UnitTypeConfig>(Output(i)+"/Unit.asset").combatConfig),"Library unit must own its configs: "+Keys[i]);
                // Each unit keeps its own speed (no blanket maxSpeed=3 as in GiantBuilder).
                u.combatConfig.maxHp=Hp[i];u.combatConfig.attackDamage=Damage[i];u.movementConfig.maxSpeed=Speed[i];u.animationConfig.moveReferenceSpeed=MoveReference[i];
                if(Ranged[i])ConfigureRanged(u,Heights[i]);
                CharacterPipeline.Save(u.spawnConfig);CharacterPipeline.Save(u.combatConfig);CharacterPipeline.Save(u.movementConfig);CharacterPipeline.Save(u.animationConfig);CharacterPipeline.Save(u);
                units.Add(u);
            }
            var legion=Load<UnitTypeConfig>(KnightLegion);CharacterGeometry.Require(oldPolicy.templates.Contains(legion),"The batch-4 knight legion must already be a policy template.");
            var policy=Object.Instantiate(oldPolicy);policy.name="Troops5RegularPolicy";policy.templates=oldPolicy.templates.Concat(units).ToArray();
            policy.explanation=oldPolicy.explanation+" 新增仙人掌枪手（远程）/毒蛙/猴獴战士。";string policyPath=folder+"/RegularPolicy.asset";AssetDatabase.CreateAsset(policy,policyPath);
            CharacterGeometry.Require(policy.TryValidateDefinition(out string perr),perr);AssetDatabase.SaveAssets();
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<Names.Length;i++)
                {
                    var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{Load<UnitTypeConfig>(LibraryUnit(i)),Load<UnitTypeConfig>(KnightLegion)};AssetDatabase.CreateAsset(scenario,folder+"/"+Keys[i]+"Scenario.asset");
                    CharacterGeometry.Require(AssetDatabase.CopyAsset(RegularScene,Battlefield(i)),"Cannot copy regular scene.");
                    var scene=EditorSceneManager.OpenScene(Battlefield(i),OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+Keys[i]+"Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);
                    var deployment=manager.GetComponent<WarSandboxRuntimeDeployment>();CharacterGeometry.Require(deployment!=null,"Regular scene has no deployment.");deployment.rosterPolicy=Load<WarSandboxRosterPolicy>(policyPath);
                    CharacterGeometry.Require(manager.systemConfig.simulationConfig.cellSize+1e-4f>=2*oldPolicy.maximumRadius,"Regular grid must cover the policy radius.");EditorSceneManager.SaveScene(scene);
                }
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            // OpenScene(Single) unloads unreferenced assets: reload everything by path from here on.
            var oldCat=Load<WarSandboxBattlefieldCatalog>(BaseCatalog);var catalog=Object.Instantiate(oldCat);catalog.name="UnifiedTroops5";
            var source=oldCat.entries.Single(e=>e.id=="unified-regular");var newEntries=new List<WarSandboxBattlefieldEntry>();var templates=oldCat.templates.ToList();
            for(int i=0;i<Names.Length;i++)
            {
                var u=Load<UnitTypeConfig>(LibraryUnit(i));var c=u.combatConfig;
                var e=source.CopyIdentity();e.id=EntryId(i);e.displayName=Titles[i]+" · 第五批";e.scenePath=Battlefield(i);e.preview=null;
                e.description="官方CC0 Quaternius Ultimate Monsters "+Files[i].Replace(".fbx","")+"，常规体型兵团（身高约 "+Heights[i].ToString("0.0#")+"m）。"+Counts[i]+" 名"+Titles[i]+"对 "+KnightCount+" 名剑盾骑士。";
                e.briefing=Roles[i]+(Ranged[i]?"（射程 "+c.projectileRange.ToString("0")+"m，":"（")+"HP "+c.maxHp+" / 攻击 "+c.attackDamage+" / 移速 "+u.movementConfig.maxSpeed.ToString("0.#")+"m/s），单体攻击、无范围伤害；首版数值，未做平衡。";
                newEntries.Add(e);templates.Add(new WarSandboxUnitTemplateEntry{templateId=TemplateId(i),revision=1,config=u});
            }
            catalog.entries=oldCat.entries.Select(e=>e.CopyIdentity()).Concat(newEntries).ToArray();catalog.templates=templates.ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");
            CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);AssetDatabase.SaveAssets();
            var k=Load<UnitTypeConfig>(KnightLegion);
            File.WriteAllText(Log+"/troops5-integration-"+IntegrationRun+".json","{\"passed\":true,\"newModels\":"+Names.Length+",\"entries\":"+catalog.entries.Length+",\"policyTemplates\":"+policy.templates.Length+",\"knight\":{\"hp\":"+k.combatConfig.maxHp+",\"damage\":"+k.combatConfig.attackDamage+",\"speed\":"+k.movementConfig.maxSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"radius\":"+k.flockingConfig.agentRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)+"},\"oldSourceEdited\":false}");
            Debug.Log("TROOPS5_INTEGRATION_READY "+catalog.entries.Length);
        }
    }
}
