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
    public static class MonsterBatchBuilder
    {
        const string Source="Assets/Art/Source/CharacterPilot/QuaterniusMonsters";
        const string Log="Logs/AgentEnemyBatch2";
        public static void Inspect()
        {
            var sb=new StringBuilder();
            foreach(string path in Directory.GetFiles(Source,"*.fbx"))
            {
                var im=(ModelImporter)AssetImporter.GetAtPath(path);im.animationType=ModelImporterAnimationType.Generic;im.isReadable=true;im.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;im.importBlendShapes=true;im.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);sb.AppendLine("MODEL "+path+" globalScale="+im.globalScale+" fileScale="+im.fileScale);
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
                        if(b.type!=typeof(Transform)||string.IsNullOrEmpty(b.path)||(b.propertyName.StartsWith("m_LocalScale") && (Mathf.Abs(min-1)>1e-5f||Mathf.Abs(max-1)>1e-5f)))sb.AppendLine("SPECIAL "+b.path+" "+b.type.Name+" "+b.propertyName+" range="+min.ToString("R")+","+max.ToString("R"));
                    }
                }
            }
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());Debug.Log("MONSTER_IMPORT_INSPECTION_READY");
        }
        const string Prepared="Assets/Game/Content/Characters/EnemyModelsBatch2/Prepared02";
        const string Knight="Assets/Game/Authoring/CharacterPipeline/Generated/Knight04";
        const string Previous="Assets/Game/Content/Characters/EnemyModelsBatch/Collection01";
        static readonly string[] Keys={"orc","yeti","mushroom"};
        static readonly string[] Names={"Orc","Yeti","MushroomKing"};
        static readonly string[] Titles={"兽人","雪怪","蘑菇怪"};
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+Names[i]+"01";
        static AnimationClip Clip(string path,string name)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Single(c=>c.name.EndsWith("|"+name,StringComparison.Ordinal));
        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only: remove static import scales by preserving world rest transforms, transforming owned mesh vertices/normals and bindposes, resampling authored local poses from world transforms. Original imported LBS positions compared at every baked frame. No scale channel is silently treated as real unit animation.";
            public int frames,positions;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,targetHeight;public bool passed;
        }
        public static void ResumePrepareAll01()
        {
            foreach(int i in Enumerable.Range(0,3)){CharacterGeometry.Require(!Directory.Exists(Output(i))&&!File.Exists(CharacterPipeline.Root+"/Recipes/"+Names[i]+".asset"),"Refuse cleanup if any completed recipe/output exists.");}
            CharacterGeometry.Require(Directory.Exists(Prepared)&&File.ReadAllText(Log+"/prepare-01b.log").Contains("Sequence contains more than one matching element"),"Requires known owned incomplete preparation failure.");
            File.WriteAllLines(Log+"/rolled-back-incomplete-preparation-01b.txt",Directory.GetFiles(Prepared,"*",SearchOption.AllDirectories));
            CharacterGeometry.Require(AssetDatabase.DeleteAsset(Prepared),"Cannot remove owned incomplete preparation.");PrepareAll01();
        }
        public static void PrepareAll01()
        {
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh batch2 preparation required.");CharacterPipeline.EnsureFolder(Prepared);
            var ti=(TextureImporter)AssetImporter.GetAtPath(Source+"/Atlas_Monsters.png");ti.textureCompression=TextureImporterCompression.Uncompressed;ti.wrapMode=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Point;ti.mipmapEnabled=true;ti.SaveAndReimport();
            for(int i=0;i<3;i++)PrepareOne(i);
            CreateCollection();Debug.Log("MONSTER_BATCH_PREPARATION_READY");
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Names[index];CharacterPipeline.EnsureFolder(folder);string modelPath=Source+"/"+Names[index]+".fbx";
            var source=Load<GameObject>(modelPath);var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.teamId=0;template.unitTypeName=Titles[index];
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(Source+"/Atlas_Monsters.png"));m.SetColor("_BaseColor",new Color(.8f,.8f,.8f,1));AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,Names[index]+"Near");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,Names[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive monster source preparation");stage.SetActive(false);
            var raw=Object.Instantiate(source,stage.transform,false);raw.name="RawReference";foreach(var a in raw.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var root=new GameObject(Names[index]+"Prepared");root.transform.SetParent(stage.transform,false);var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);
            var body=Object.Instantiate(source,pivot);body.name="Body";foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var evidence=new PreparationEvidence{model=Names[index],targetHeight=index==0?2f:index==1?2.4f:1.8f};
            var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();
            try
            {
                var originalTransforms=raw.GetComponentsInChildren<Transform>(true);var byPath=originalTransforms.ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,raw.transform));var restScales=byPath.ToDictionary(p=>p.Key,p=>p.Value.localScale);
                var transforms=body.GetComponentsInChildren<Transform>(true);var oldWorld=transforms.ToDictionary(t=>t,t=>t.localToWorldMatrix);var worldPositions=transforms.ToDictionary(t=>t,t=>t.position);var worldRotations=transforms.ToDictionary(t=>t,t=>t.rotation);
                foreach(var t in transforms)
                {
                    Vector3 s=t.localScale;float mean=(s.x+s.y+s.z)/3;
                    CharacterGeometry.Require(s.x>0&&s.y>0&&s.z>0&&(s-Vector3.one*mean).magnitude/mean<.0002f,"Not bounded static scale noise: "+t.name);
                    t.localScale=Vector3.one;t.SetPositionAndRotation(worldPositions[t],worldRotations[t]);
                }
                foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src=skin.sharedMesh;CharacterGeometry.Require(src.blendShapeCount==0,"Do not drop source blendshapes.");var mesh=Object.Instantiate(src);mesh.name=skin.name+"Canonical";
                    Matrix4x4 vertex=skin.transform.worldToLocalMatrix*oldWorld[skin.transform];Matrix4x4 normal=vertex.inverse.transpose;
                    mesh.vertices=src.vertices.Select(v=>vertex.MultiplyPoint3x4(v)).ToArray();mesh.normals=src.normals.Select(n=>normal.MultiplyVector(n).normalized).ToArray();
                    var binds=src.bindposes;mesh.bindposes=skin.bones.Select((bone,j)=>bone.worldToLocalMatrix*oldWorld[bone]*binds[j]*vertex.inverse).ToArray();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/"+mesh.name+".asset");skin.sharedMesh=mesh;skin.localBounds=mesh.bounds;skin.sharedMaterial=template.renderConfig.nearMaterial;
                }
                CharacterGeometry.Require(body.GetComponentsInChildren<MeshRenderer>(true).Length==0,"Unexpected rigid source; explicit conversion required.");
                var recipeRaw=ScriptableObject.CreateInstance<CharacterRecipe>();evidence.sourceHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(raw,recipeRaw,true)).size.y;Object.DestroyImmediate(recipeRaw);float scale=evidence.targetHeight/evidence.sourceHeight;
                string[] actions={"Idle","Move","Attack","Death"};var originals=new[]{Clip(modelPath,"Idle"),Clip(modelPath,"Run"),Clip(modelPath,index==0?"Weapon":"Punch"),Clip(modelPath,"Death")};
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
                CharacterGeometry.Require(evidence.maximumScaledPositionError<=.00025f,"Original/canonical pose mismatch exceeds0.25mm: "+evidence.maximumScaledPositionError);
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=bindPositions[i];transforms[i].localRotation=bindRotations[i];transforms[i].localScale=Vector3.one;}
                root.transform.SetParent(null,false);root.SetActive(true);CharacterGeometry.CheckHierarchy(root,"ScalePivot");var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");
                recipe.name=Names[index];recipe.model=prefab;recipe.sizeRootPath="ScalePivot";recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.frameRate=24;recipe.targetBodyHeight=evidence.targetHeight;recipe.agentRadius=index==1?.65f:.5f;recipe.paletteColumns=32;recipe.paletteRows=32;recipe.lowVertexBudget=1600;recipe.maxTextureMiB=32;recipe.outputName=Names[index]+"01";recipe.unitTemplate=template;
                template.combatConfig.projectileRange=0;template.combatConfig.targetAcquireRadius=10;template.combatConfig.attackInterval=mapped[2].length;template.combatConfig.attackRange=index==1?2.2f:1.8f;template.spawnConfig.unitCount=64;template.spawnConfig.spawnCenter=new Vector3(-12,0,0);template.movementConfig.maxSpeed=3;template.animationConfig.moveReferenceSpeed=3;template.combatConfig.projectileTargetHeight=evidence.targetHeight*.5f;
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+Names[index]+".asset");evidence.passed=true;File.WriteAllText(Log+"/"+Keys[index]+"-canonical.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/"+Keys[index]+"-report-01.json",JsonUtility.ToJson(report,true));Debug.Log("MONSTER_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices);
        }
        public static void CreateCollection()
        {
            string folder=Prepared+"/Collection01";CharacterGeometry.Require(!Directory.Exists(folder),"Collection already exists.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(folder+"/Previews");
            var old=Load<WarSandboxBattlefieldCatalog>(Previous+"/Catalog.asset");var control=Load<UnitTypeConfig>(Previous+"/KnightControl.asset");var catalog=ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();catalog.name="EnemyModelsBatch2";catalog.defaultEntryId="enemy-batch2-orc";
            var entries=new List<WarSandboxBattlefieldEntry>();var templates=new List<WarSandboxUnitTemplateEntry>();
            for(int i=0;i<3;i++)
            {
                var unit=Load<UnitTypeConfig>(Output(i)+"/Unit.asset");var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{unit,control};AssetDatabase.CreateAsset(scenario,folder+"/"+Names[i]+"Scenario.asset");
                string battle=folder+"/"+Names[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(Previous+"/WarriorBattlefield.unity",battle),"Cannot make fresh scene.");
                var entry=old.entries[0].CopyIdentity();entry.id="enemy-batch2-"+Keys[i];entry.displayName=Titles[i]+" · 第二批";entry.description="不同轮廓怪物，自带动作，复用既有近战。";entry.briefing="攻方"+Titles[i]+"，守方骑士；调整镜头看四动作/体型/LOD。本批不是新技能或配平专项。";entry.scenePath=battle;
                var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Log+"/"+Keys[i]+"-report-01.json"));string preview=folder+"/Previews/"+Names[i]+".png";
                // New assets use a wide transparent letterbox rather than stretching a square thumbnail in the inherited UI.
                var input=new Texture2D(2,2,TextureFormat.RGBA32,false);var image=new Texture2D(768,320,TextureFormat.RGBA32,false);try{input.LoadImage(File.ReadAllBytes(r.evidence+"/lod0-Idle-0.png"));image.SetPixels32(new Color32[768*320]);for(int y=0;y<320;y++)for(int x=0;x<320;x++)image.SetPixel(x+224,y,input.GetPixelBilinear(x/319f,y/319f));image.Apply(false,false);File.WriteAllBytes(preview,image.EncodeToPNG());}finally{Object.DestroyImmediate(input);Object.DestroyImmediate(image);}AssetDatabase.ImportAsset(preview);entry.preview=Load<Texture2D>(preview);entries.Add(entry);templates.Add(new WarSandboxUnitTemplateEntry{templateId="monster-"+Keys[i],revision=1,config=unit});
            }
            entries.AddRange(old.entries.Select(e=>e.CopyIdentity()));templates.AddRange(old.templates.Select(t=>new WarSandboxUnitTemplateEntry{templateId=t.templateId,revision=t.revision,config=t.config}));catalog.entries=entries.ToArray();catalog.templates=templates.ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<3;i++){var scene=EditorSceneManager.OpenScene(folder+"/"+Names[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+Names[i]+"Scenario.asset");manager.battleStarted=false;EditorSceneManager.SaveScene(scene);}
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Previous+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy new menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var router=Object.FindFirstObjectByType<EnemyBatchRuntime>();var oldEntries=router.entries;router.entries=Enumerable.Range(0,3).Select(i=>new EnemyBatchRuntime.Entry{key=Keys[i],battlefieldId="enemy-batch2-"+Keys[i],templateId="monster-"+Keys[i],unit=Load<UnitTypeConfig>(Output(i)+"/Unit.asset"),scenario=Load<ScenarioConfig>(folder+"/"+Names[i]+"Scenario.asset")}).Concat(oldEntries).ToArray();
                var smoke=router.smoke;smoke.expectedCatalog=session.catalog;smoke.session=session;smoke.expectedTemplate=router.entries[0].unit;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.templateRevision=1;smoke.planSlot="EnemyCollection-orc";
                var runtime=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();runtime.planDirectory="EnemyModelBatch2Plans";runtime.previewName="Enemy Models Batch2";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/collection-ready.json","{\"passed\":true,\"newModels\":3,\"catalogScenes\":6,\"oldSceneAssetsModified\":false}");
        }
        public static void Build01()
        {
            string dest="Builds/EnemyModelsBatch2-20260930-01",folder=Prepared+"/Collection01";CharacterGeometry.Require(!Directory.Exists(dest),"Do not overwrite build.");
            foreach(int i in Enumerable.Range(0,3)){var report=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(report.automatedPassed,"Model not ready.");var v=UnitTypeBinder.ValidateBinding(Load<UnitTypeConfig>(Output(i)+"/Unit.asset"));CharacterGeometry.Require(v.IsValid,string.Join("\n",v.Errors));}
            var catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var scenes=new[]{folder+"/Menu.unity"}.Concat(catalog.entries.Select(e=>e.scenePath)).ToArray();var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=dest+"/EnemyModels.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(result.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Batch2 build failed.");
            File.WriteAllText(dest+"/Start-Enemies.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0EnemyModels.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
