using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using MassEngine.Editor;
using Object=UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class RangerPilotBuilder
    {
        const string Source="Assets/Art/Source/CharacterPilot/KayKitRanger";
        const string Log="Logs/AgentRangerPilot";
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
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());Debug.Log("RANGER_IMPORT_INSPECTION_READY");
        }
        const string Prepared="Assets/Game/Content/Characters/RangerPilot";
        const string Knight="Assets/Game/Authoring/CharacterPipeline/Generated/Knight04";
        const string RecipePath="Assets/Game/Authoring/CharacterPipeline/Recipes/Ranger.asset";
        static T Load<T>(string p) where T:Object {var o=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(o!=null,"Missing "+p);return o;}
        static AnimationClip Clip(string file,string name) => AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().Single(c=>c.name==name);
        static string PathOf(Transform t,Transform root)=>AnimationUtility.CalculateTransformPath(t,root);
        [Serializable] class PreparationReport
        {
            public string method="Ranger + original single Draw bow morph + authored Draw/Release clips stitched into one Attack. Explicit single linear unskinned morph oracle, no dropped morphs. Arrow is a new one-frame collapse attachment for presentation only; actual projectiles are emitted by existing GPU combat.";
            public float attackLength,releasePhase,legacyKnightMaximumError,nativeMorphMaximumError;
            public int fullVertices,frames;public string[] morphPaths;public string pipelineEvidence;
            public List<string> checks=new List<string>();public bool passed,humanAcceptance;
        }
        public static void Prepare01()
        {
            CharacterGeometry.Require(!AssetDatabase.IsValidFolder(Prepared),"Fresh Ranger preparation required.");
            CharacterPipeline.EnsureFolder(Prepared);CharacterPipeline.EnsureFolder(Prepared+"/Templates");
            var notes=new PreparationReport();
            var texImporter=(TextureImporter)AssetImporter.GetAtPath(Source+"/ranger_texture.png");texImporter.textureCompression=TextureImporterCompression.Uncompressed;texImporter.wrapMode=TextureWrapMode.Clamp;texImporter.filterMode=FilterMode.Bilinear;texImporter.mipmapEnabled=true;texImporter.SaveAndReimport();
            var oldUnit=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(oldUnit,Prepared+"/Templates","RangerTemplate");
            template.unitTypeName="KayKit 持弓游侠";
            Material MaterialFor(Material source,string name){var m=new Material(source){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(Source+"/ranger_texture.png"));m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,Prepared+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=MaterialFor(oldUnit.renderConfig.nearMaterial,"RangerNear");template.renderConfig.midMaterial=MaterialFor(oldUnit.renderConfig.midMaterial,"RangerMid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var root=new GameObject("RangerPrepared");root.SetActive(false);
            string bowPath,arrowPath;AnimationClip attack;
            try
            {
                var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);
                var body=Object.Instantiate(Load<GameObject>(Source+"/Ranger.fbx"),pivot);body.name="Body";
                foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;
                foreach(var renderer in body.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterial=template.renderConfig.nearMaterial;
                Transform Slot(string name)=>body.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
                var bow=Object.Instantiate(Load<GameObject>(Source+"/bow_withString.fbx"),Slot("handslot.l"));bow.name="Bow";bow.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);bow.transform.localScale=Vector3.one;
                var bowSkin=bow.GetComponent<SkinnedMeshRenderer>();CharacterGeometry.Require(bowSkin!=null,"Bow morph renderer absent.");bowSkin.sharedMaterial=template.renderConfig.nearMaterial;bowPath=PathOf(bowSkin.transform,root.transform);
                var arrow=Object.Instantiate(Load<GameObject>(Source+"/arrow_bow.fbx"),Slot("handslot.r"));arrow.name="NockedArrow";arrow.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);arrow.transform.localScale=Vector3.one;
                var filter=arrow.GetComponent<MeshFilter>();var arrowMesh=Object.Instantiate(filter.sharedMesh);arrowMesh.name="NockedArrowCollapse";
                var vertices=arrowMesh.vertices;Vector3 center=arrowMesh.bounds.center;arrowMesh.AddBlendShapeFrame("Hidden",100,vertices.Select(v=>center-v).ToArray(),null,null);AssetDatabase.CreateAsset(arrowMesh,Prepared+"/NockedArrowMesh.asset");
                Object.DestroyImmediate(arrow.GetComponent<MeshRenderer>());Object.DestroyImmediate(filter);
                var arrowSkin=arrow.AddComponent<SkinnedMeshRenderer>();arrowSkin.sharedMesh=arrowMesh;arrowSkin.sharedMaterial=template.renderConfig.nearMaterial;arrowSkin.bones=Array.Empty<Transform>();arrowSkin.localBounds=arrowMesh.bounds;arrowSkin.SetBlendShapeWeight(0,100);arrowPath=PathOf(arrow.transform,root.transform);
                foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;
                var draw=Clip(Source+"/Rig_Medium_CombatRanged.fbx","Ranged_Bow_Draw");var release=Clip(Source+"/Rig_Medium_CombatRanged.fbx","Ranged_Bow_Release");
                attack=MapClip(draw,release,"Attack",body);
                var idle=MapClip(Clip(Source+"/Rig_Medium_CombatRanged.fbx","Ranged_Bow_Idle"),null,"Idle",body);
                var move=MapClip(Clip("Assets/Art/Source/CharacterPilot/KayKitKnight/Rig_Medium_MovementBasic.fbx","Running_A"),null,"Move",body);
                var death=MapClip(Clip("Assets/Art/Source/CharacterPilot/KayKitKnight/Rig_Medium_General.fbx","Death_A"),null,"Death",body);
                foreach(var c in new[]{idle,move,death})
                {SetMorph(c,bowPath,"Draw",new[]{new Keyframe(0,0),new Keyframe(c.length,0)});SetMorph(c,arrowPath,"Hidden",new[]{new Keyframe(0,100),new Keyframe(c.length,100)});}
                float seam=draw.length,releaseTime=seam+.08f;
                SetMorph(attack,bowPath,"Draw",new[]{new Keyframe(0,0),new Keyframe(seam*.45f,0),new Keyframe(seam,100),new Keyframe(releaseTime,100),new Keyframe(releaseTime+.08f,0),new Keyframe(attack.length,0)});
                SetMorph(attack,arrowPath,"Hidden",new[]{new Keyframe(0,100),new Keyframe(seam*.38f,100),new Keyframe(seam*.45f,0),new Keyframe(releaseTime,0),new Keyframe(releaseTime+.04f,100),new Keyframe(attack.length,100)});
                foreach(var c in new[]{idle,move,attack,death}){c.EnsureQuaternionContinuity();AssetDatabase.CreateAsset(c,Prepared+"/"+c.name+".anim");}
                notes.attackLength=attack.length;notes.releasePhase=releaseTime/attack.length;
                // Compare independent morph math with native BakeMesh BEFORE normalization, at the three linear checkpoints.
                var baked=new Mesh();try
                {
                    foreach(var skin in new[]{bowSkin,arrowSkin})foreach(float weight in new[]{0f,50f,100f})
                    {
                        skin.SetBlendShapeWeight(0,weight);skin.BakeMesh(baked,false);var expected=CharacterGeometry.Skin(root.transform,skin);var m=root.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix;var actual=baked.vertices;
                        for(int i=0;i<actual.Length;i++)notes.nativeMorphMaximumError=Mathf.Max(notes.nativeMorphMaximumError,Vector3.Distance(expected[i],m.MultiplyPoint3x4(actual[i])));
                    }
                }finally{Object.DestroyImmediate(baked);}
                CharacterGeometry.Require(notes.nativeMorphMaximumError<.00001f,"Native morph/reference discrepancy.");bowSkin.SetBlendShapeWeight(0,0);arrowSkin.SetBlendShapeWeight(0,100);
                bool refused=false;root.SetActive(true);try{CharacterGeometry.CheckHierarchy(root,"ScalePivot");}catch(InvalidOperationException e){refused=e.Message.Contains("Blendshapes");}CharacterGeometry.Require(refused,"Undeclared morph was not rejected.");
                notes.checks.Add("Undeclared morph rejected; explicit zero-bone single-100-frame oracle agrees with native at0/50/100.");
                CharacterGeometry.CheckHierarchy(root,"ScalePivot",new[]{bowPath,arrowPath});
                arrowSkin.SetBlendShapeWeight(0,150);refused=false;try{CharacterGeometry.Skin(root.transform,arrowSkin);}catch(InvalidOperationException){refused=true;}CharacterGeometry.Require(refused,"Out of range morph accepted.");arrowSkin.SetBlendShapeWeight(0,100);notes.checks.Add("Out-of-range morph weight rejected.");
                PrefabUtility.SaveAsPrefabAsset(root,Prepared+"/Ranger.prefab");
            }
            finally{Object.DestroyImmediate(root);}
            template.combatConfig.projectileRange=10;template.combatConfig.targetAcquireRadius=10;template.combatConfig.attackRange=1.8f;template.combatConfig.attackInterval=notes.attackLength;template.combatConfig.attackReleasePhase=notes.releasePhase;template.combatConfig.projectileSpeed=12;template.combatConfig.projectileGravity=-9.8f;template.combatConfig.projectileHitRadius=.1f;template.combatConfig.projectileMaxLifetime=3;template.combatConfig.projectileOriginHeight=1.3f;template.combatConfig.projectileTargetHeight=1;template.combatConfig.projectileTrailLength=1.15f;
            template.spawnConfig.spawnCenter=new Vector3(-12,0,0);template.spawnConfig.unitCount=64;CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template);
            MakeTemplateTrial(template);
            // Regression of the original no-morph Knight04 position oracle; no old assets are saved or rebound.
            var knight=Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/Knight.asset");var stage=new GameObject("Inactive reference regression");stage.SetActive(false);
            try{var bounds=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(knight.model,knight,true));float factor=knight.targetBodyHeight/bounds.size.y;var report=new CharacterPipelineReport();var t=CharacterPipeline.VerifyAllFrames(knight,Load<VATProfile>(Knight+"/Full.asset"),stage,factor,new Vector3(0,-bounds.min.y*factor,0),report);Object.DestroyImmediate(t);notes.legacyKnightMaximumError=report.maxPositionError;notes.checks.Add("All759924 legacy Knight04 positions still pass unchanged.");}finally{Object.DestroyImmediate(stage);}
            var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();recipe.name="Ranger";recipe.outputName="Ranger01";recipe.model=Load<GameObject>(Prepared+"/Ranger.prefab");recipe.sizeRootPath="ScalePivot";recipe.blendshapeAttachmentPaths=new[]{bowPath,arrowPath};recipe.frameRate=24;recipe.targetBodyHeight=1.8f;recipe.agentRadius=.45f;recipe.unitTemplate=Load<UnitTypeConfig>(Prepared+"/Templates/RangerTemplate.asset");recipe.idle=Load<AnimationClip>(Prepared+"/Idle.anim");recipe.move=Load<AnimationClip>(Prepared+"/Move.anim");recipe.attack=Load<AnimationClip>(Prepared+"/Attack.anim");recipe.death=Load<AnimationClip>(Prepared+"/Death.anim");recipe.lowVertexBudget=1400;recipe.maxTextureMiB=32;
            recipe.createTrial=true;recipe.trialMenu=Load<SceneAsset>(Knight+"/Trial/Menu.unity");recipe.trialBattlefield=Load<SceneAsset>(Prepared+"/Templates/Battlefield.unity");recipe.trialCatalog=Load<WarSandboxBattlefieldCatalog>(Prepared+"/Templates/Catalog.asset");recipe.battlefieldId="ranger-pilot";recipe.templateId="kaykit-ranger";AssetDatabase.CreateAsset(recipe,RecipePath);
            var result=CharacterPipeline.Run(recipe);
            AddProbe(result.output);
            notes.fullVertices=result.fullVertices;notes.frames=result.totalFrames;notes.morphPaths=new[]{bowPath,arrowPath};notes.pipelineEvidence=result.evidence;notes.passed=true;
            File.WriteAllText(Log+"/preparation.json",JsonUtility.ToJson(notes,true));File.WriteAllText(Log+"/recipe-report-01.json",JsonUtility.ToJson(result,true));Debug.Log("RANGER_PREPARATION_READY");
        }
        static AnimationClip MapClip(AnimationClip first,AnimationClip second,string name,GameObject body)
        {
            var clip=new AnimationClip{name=name,frameRate=30};
            foreach(var binding in AnimationUtility.GetCurveBindings(first))
            {
                CharacterGeometry.Require(binding.type==typeof(Transform) && body.transform.Find(binding.path)!=null,"Unmapped source rig: "+binding.path);
                var curve=AnimationUtility.GetEditorCurve(first,binding);var mapped=binding;mapped.path="ScalePivot/Body/"+binding.path;
                if(second!=null)
                {
                    var next=AnimationUtility.GetEditorCurve(second,binding);CharacterGeometry.Require(next!=null,"Release clip missing binding.");
                    var keys=curve.keys.Where(k=>k.time<first.length-1e-5f).ToList();foreach(var k in next.keys){var shifted=k;shifted.time+=first.length;keys.Add(shifted);}curve=new AnimationCurve(keys.ToArray());
                }
                AnimationUtility.SetEditorCurve(clip,mapped,curve);
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=name!="Death";AnimationUtility.SetAnimationClipSettings(clip,settings);return clip;
        }
        static void SetMorph(AnimationClip clip,string path,string shape,Keyframe[] keys)=>AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(SkinnedMeshRenderer),"blendShape."+shape),new AnimationCurve(keys));
        static void MakeTemplateTrial(UnitTypeConfig ranger)
        {
            var baseScenario=Load<ScenarioConfig>(Knight+"/Trial/Scenario.asset");var opponent=CharacterPipeline.CloneUnit(baseScenario.unitTypes[1],Prepared+"/Templates","OpponentTemplate");opponent.movementConfig.maxSpeed=2;opponent.combatConfig.targetAcquireRadius=10;opponent.spawnConfig.spawnCenter=new Vector3(12,0,0);opponent.spawnConfig.unitCount=64;CharacterPipeline.Save(opponent.movementConfig);CharacterPipeline.Save(opponent.combatConfig);CharacterPipeline.Save(opponent.spawnConfig);
            var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{ranger,opponent};AssetDatabase.CreateAsset(scenario,Prepared+"/Templates/Scenario.asset");
            var catalog=Object.Instantiate(Load<WarSandboxBattlefieldCatalog>(Knight+"/Trial/Catalog.asset"));catalog.templates[0].config=ranger;catalog.templates[1].config=opponent;catalog.entries[0].scenePath=Prepared+"/Templates/Battlefield.unity";AssetDatabase.CreateAsset(catalog,Prepared+"/Templates/Catalog.asset");
            CharacterGeometry.Require(AssetDatabase.CopyAsset(Knight+"/Trial/Battlefield.unity",Prepared+"/Templates/Battlefield.unity"),"Cannot copy template scene.");
            var setup=EditorSceneManager.GetSceneManagerSetup();try{var scene=EditorSceneManager.OpenScene(Prepared+"/Templates/Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(Prepared+"/Templates/Scenario.asset");EditorSceneManager.SaveScene(scene);}finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        static void AddProbe(string output)
        {
            var setup=EditorSceneManager.GetSceneManagerSetup();try{var scene=EditorSceneManager.OpenScene(output+"/Trial/Menu.unity",OpenSceneMode.Single);new GameObject("Ranger passive firing evidence (opt-in)").AddComponent<RangerFiringProbe>();var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="RangerPilotPlans";EditorSceneManager.SaveScene(scene);}finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
        [Serializable] class AlignmentReport
        {
            public float bowRotationDegrees,drawWeight,drawWeightUnclamped,nockToHandMetres,bowPullDotBefore,bowPullDotAfter;
            public int arrowAxis,tipEndUniqueVertices,tailEndUniqueVertices;public Vector3 arrowNockLocal;public Quaternion arrowRotation;
            public string note="Fresh02 only: bow rotated about its long axis toward drawing hand; arrow long axis aims through bow hand and tail anchors to right socket. No body bone/head offsets.";
        }
        public static void AlignAndPrepare02()
        {
            string folder=Prepared+"/Alignment02";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh Alignment02 required.");CharacterPipeline.EnsureFolder(folder);
            var recipe=Object.Instantiate(Load<CharacterRecipe>(RecipePath));recipe.name="Ranger02";recipe.outputName="Ranger02";
            var go=PrefabUtility.LoadPrefabContents(Prepared+"/Ranger.prefab");var report=new AlignmentReport();
            try
            {
                var transforms=go.GetComponentsInChildren<Transform>(true);var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();
                var bow=go.transform.Find(recipe.blendshapeAttachmentPaths[0]);var arrow=go.transform.Find(recipe.blendshapeAttachmentPaths[1]);var bowSkin=bow.GetComponent<SkinnedMeshRenderer>();
                var bodyBounds=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(go,recipe,true));float displayScale=recipe.targetBodyHeight/bodyBounds.size.y;
                recipe.attack.SampleAnimation(go,1.30f);
                var bm=bowSkin.sharedMesh;var delta=new Vector3[bm.vertexCount];bm.GetBlendShapeFrameVertices(0,0,delta,null,null);Vector3 pull=delta.Aggregate(Vector3.zero,(a,b)=>a+b)/delta.Length;
                Vector3 axis=LongestAxis(bm.bounds.size,out _);Vector3 desired=bow.parent.InverseTransformPoint(arrow.parent.position)-bow.localPosition;
                report.bowPullDotBefore=Vector3.Dot(pull.normalized,desired.normalized);
                float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(pull,axis),Vector3.ProjectOnPlane(desired,axis),axis);Quaternion bowRotation=Quaternion.AngleAxis(angle,axis);
                report.bowRotationDegrees=angle;report.bowPullDotAfter=Vector3.Dot((bowRotation*pull).normalized,desired.normalized);
                // Most-displaced central string ring identifies the nock, not a bow-limb endpoint.
                float maximum=delta.Max(d=>d.magnitude);int[] nock=Enumerable.Range(0,delta.Length).Where(i=>delta[i].magnitude>=maximum*.995f).ToArray();var bv=bm.vertices;
                Vector3 baseline=nock.Select(i=>bv[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/nock.Length;
                Vector3 change=nock.Select(i=>delta[i]).Aggregate(Vector3.zero,(a,b)=>a+b)/nock.Length;
                var d=bowRotation*change;var rest=bowRotation*baseline;float weight=Vector3.Dot(desired-rest,d)/d.sqrMagnitude*100;
                report.drawWeightUnclamped=weight;report.drawWeight=Mathf.Clamp(weight,0,100);report.nockToHandMetres=Vector3.Distance(rest+d*(report.drawWeight/100),desired)*displayScale;
                var am=arrow.GetComponent<SkinnedMeshRenderer>().sharedMesh;Vector3 arrowAxis=LongestAxis(am.bounds.size,out int index);report.arrowAxis=index;
                int UniqueAt(float coordinate)=>am.vertices.Where(v=>Mathf.Abs(v[index]-coordinate)<.0001f).Select(v=>new Vector3(Mathf.Round(v.x*10000),Mathf.Round(v.y*10000),Mathf.Round(v.z*10000))).Distinct().Count();
                int atMax=UniqueAt(am.bounds.max[index]),atMin=UniqueAt(am.bounds.min[index]);float sign=atMax<=atMin?1:-1;report.tipEndUniqueVertices=sign>0?atMax:atMin;report.tailEndUniqueVertices=sign>0?atMin:atMax;
                Vector3 nockLocal=am.bounds.center;nockLocal[index]=sign>0?am.bounds.min[index]:am.bounds.max[index];report.arrowNockLocal=nockLocal;
                Vector3 direction=arrow.parent.InverseTransformDirection(bow.parent.position-arrow.parent.position);var arrowRotation=Quaternion.FromToRotation(arrowAxis*sign,direction);report.arrowRotation=arrowRotation;
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];}
                bow.localRotation=bowRotation;arrow.localRotation=arrowRotation;arrow.localPosition=-(arrowRotation*nockLocal);bowSkin.SetBlendShapeWeight(0,0);arrow.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0,100);
                CharacterGeometry.Require(report.bowPullDotAfter>0.7f,"Bow draw direction failed to face drawing hand.");
                PrefabUtility.SaveAsPrefabAsset(go,folder+"/Ranger02.prefab");
            }
            finally{PrefabUtility.UnloadPrefabContents(go);}
            recipe.model=Load<GameObject>(folder+"/Ranger02.prefab");var attack=Object.Instantiate(recipe.attack);attack.name="Attack";var binding=EditorCurveBinding.FloatCurve(recipe.blendshapeAttachmentPaths[0],typeof(SkinnedMeshRenderer),"blendShape.Draw");var curve=AnimationUtility.GetEditorCurve(attack,binding);var keys=curve.keys;for(int i=0;i<keys.Length;i++)keys[i].value*=report.drawWeight/100;AnimationUtility.SetEditorCurve(attack,binding,new AnimationCurve(keys));AssetDatabase.CreateAsset(attack,folder+"/Attack.anim");recipe.attack=attack;
            AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/Ranger02.asset");var result=CharacterPipeline.Run(recipe);AddProbe(result.output);CaptureRelease(result.output,"release02-");
            File.WriteAllText(Log+"/alignment-02.json",JsonUtility.ToJson(report,true));File.WriteAllText(Log+"/recipe-report-02.json",JsonUtility.ToJson(result,true));Debug.Log("RANGER_ALIGNMENT02_READY");
        }
        static Vector3 LongestAxis(Vector3 size,out int index){index=size.x>=size.y && size.x>=size.z?0:(size.y>=size.z?1:2);return index==0?Vector3.right:(index==1?Vector3.up:Vector3.forward);}
        static void CaptureRelease(string output,string prefix)
        {
            var unit=Load<UnitTypeConfig>(output+"/Unit.asset");var profile=(VATProfile)unit.renderConfig.vatProfile;
            using(var capture=new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(profile),512))foreach(int frame in new[]{30,32,34,36})
            {var pixels=capture.Capture(ResolvedUnitTypeRuntime.Resolve(unit,1),0,AgentState.Attack,(frame+.25f)/24);var t=new Texture2D(512,512,TextureFormat.RGBA32,false);try{t.SetPixels32(pixels);t.Apply(false,false);File.WriteAllBytes(Log+"/"+prefix+frame+".png",t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}}
        }
        public static void Build02(){CharacterPipelineTrial.Build(CharacterPipeline.Root+"/Generated/Ranger02","Builds/RangerPilot-20260930-02");File.Copy("Builds/RangerPilot-20260930-02/Start-Knight.cmd","Builds/RangerPilot-20260930-02/Start-Ranger.cmd");}
        public static void Build01()
        {
            var unit=Load<UnitTypeConfig>(CharacterPipeline.Root+"/Generated/Ranger01/Unit.asset");var profile=(VATProfile)unit.renderConfig.vatProfile;
            using(var capture=new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(profile),512))
                foreach(int frame in new[]{30,32,34,36})
                {
                    var pixels=capture.Capture(ResolvedUnitTypeRuntime.Resolve(unit,1),0,AgentState.Attack,(frame+.25f)/24);
                    var t=new Texture2D(512,512,TextureFormat.RGBA32,false);try{t.SetPixels32(pixels);t.Apply(false,false);File.WriteAllBytes(Log+"/release-"+frame+".png",t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}
                }
            CharacterPipelineTrial.Build(CharacterPipeline.Root+"/Generated/Ranger01","Builds/RangerPilot-20260930-01");
            File.Copy("Builds/RangerPilot-20260930-01/Start-Knight.cmd","Builds/RangerPilot-20260930-01/Start-Ranger.cmd");
        }
    }
}
