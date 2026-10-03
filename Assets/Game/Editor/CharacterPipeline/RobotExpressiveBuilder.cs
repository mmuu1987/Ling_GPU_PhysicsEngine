using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MassEngine.Editor;
using Object=UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>One pinned CC0 RobotExpressive conversion. Never edits formal catalogs, old assets or generic gates.</summary>
    public static class RobotExpressiveBuilder
    {
        public const string Source="Assets/CharacterPilotSource/TowerDefenseRobotExpressive01";
        public const string SourceSha="047f5e5fb3bb6d378bd1df16ca6137f2a596c99b3a1b5690b4020c05aaf6f319";
        public const string Root="Assets/Game/RobotExpressivePilot/Version01";
        public const string Native=Root+"/Native03";
        public const string Prepared=Root+"/Prepared04";
        public const string RecipePath=Root+"/RobotRecipe.asset";
        public const string Output="Assets/Game/CharacterPipeline/Generated/TowerDefenseRobot01";
        const string Knight="Assets/Game/CharacterPipeline/Generated/Knight04";
        [Serializable] sealed class Provenance { public string sourceSha256,bundleSha256,license,commit; }
        [Serializable] public sealed class ConversionEvidence
        {
            public bool passed,humanAcceptance,formalRosterChanged;
            public string sourceSha256,method,graphicsDevice,pipelineEvidence;
            public int nativeVertices,nativeTriangles,preparedVertices,preparedTriangles,rendererCount,sourceMorphTargets,neutralMorphChannels,verifiedFrames;
            public long comparedPositions;
            public float fullSourceHeight,targetHeight,maximumNativeScaledError,maximumPreparedScaledError,maximumVatScaledError;
            public string[] authoredClips,selectedClips;
        }
        public static string Hash(string p){using(var h=SHA256.Create())return string.Concat(h.ComputeHash(File.ReadAllBytes(p)).Select(b=>b.ToString("x2")));}
        static RobotExpressiveData Data()
        {
            var p=JsonUtility.FromJson<Provenance>(File.ReadAllText(Source+"/SourceProvenance.json"));
            Require(p.sourceSha256==SourceSha&&p.license=="CC0-1.0"&&p.commit=="8019f9267749ec7b9bb87b472b92e04a2dde4761","Unexpected provenance");
            Require(Hash(Source+"/RobotExpressive.glb")==SourceSha&&Hash(Source+"/RobotData.json")==p.bundleSha256,"Pinned source/bundle mismatch");
            Require(File.ReadAllText(Source+"/License-and-Credits.txt").Contains("CC0 1.0"),"Original model license required");
            var d=JsonUtility.FromJson<RobotExpressiveData>(File.ReadAllText(Source+"/RobotData.json"));
            Require(d.schemaVersion==1&&d.sourceSha256==SourceSha&&d.nodes.Length==74&&d.animations.Length==14,"Pinned model layout changed");
            Require(d.meshes.Sum(m=>m.primitives.Sum(s=>s.positions.Length/3))==7214&&d.meshes.Sum(m=>m.primitives.Sum(s=>s.indices.Length/3))==3237,"Do not omit original geometry");
            d.RequireNeutralBattleFace();return d;
        }
        public static bool ValidatePinnedSource(){Data();return true;}
        static string EvidenceDir()
        {
            string p=Environment.GetEnvironmentVariable("ROBOT_PILOT_OUTPUT");Require(!string.IsNullOrWhiteSpace(p),"Runner must supply fresh evidence directory");Directory.CreateDirectory(p);return p;
        }
        static ConversionEvidence NewEvidence(RobotExpressiveData d) => new ConversionEvidence{sourceSha256=SourceSha,targetHeight=1.8f,sourceMorphTargets=3,neutralMorphChannels=4,nativeVertices=7214,nativeTriangles=3237,rendererCount=d.MeshOrder().Length,authoredClips=d.animations.Select(a=>a.name).ToArray(),selectedClips=new[]{"Idle","Walking","Punch","Death"},graphicsDevice=SystemInfo.graphicsDeviceType+" / "+SystemInfo.graphicsDeviceName,humanAcceptance=false,formalRosterChanged=false};
        public static void Import01()
        {
            var d=Data();string evidence=EvidenceDir();Require(!Directory.Exists(Native),"Native conversion folder already exists; never overwrite");
            CharacterPipeline.EnsureFolder(Native);var source=d.InstantiateNative(Native);
            try
            {
                foreach(var motion in d.animations)AssetDatabase.CreateAsset(d.NativeClip(motion,source),Native+"/"+motion.name+".anim");
                var controller=UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(Native+"/Robot.controller");
                foreach(var motion in d.animations)controller.AddMotion(Load<AnimationClip>(Native+"/"+motion.name+".anim"));
                source.AddComponent<Animator>().runtimeAnimatorController=controller;
                Require(PrefabUtility.SaveAsPrefabAsset(source,Native+"/RobotExpressive.prefab")!=null,"Cannot save full native prefab");
            }
            finally{Object.DestroyImmediate(source);}
            var report=NewEvidence(d);report.method="Pinned GLB accessor expansion to native Unity hierarchy/meshes/materials, with X-reflection handedness conversion and all14 authored clips/all3 facial targets preserved. Native positions checked against separate GLB matrix/weight/morph oracle after originating Three.js r160 GLTFLoader L1 hand-weight normalization. Raw GLB weights remain immutable. Source PNGs use neutral source-colour geometry lighting, not author PBR shader acceptance.";
            var stage=new GameObject("Inactive robot native check");stage.SetActive(false);var raw=Object.Instantiate(Load<GameObject>(Native+"/RobotExpressive.prefab"),stage.transform,false);Disable(raw);
            try
            {
                float height=Bounds(d.Positions(null,0)).size.y;report.fullSourceHeight=height;float scale=report.targetHeight/height;
                foreach(string clipName in report.selectedClips)
                {
                    Reset(raw,d);var clip=Load<AnimationClip>(Native+"/"+clipName+".anim");int count=Mathf.Max(1,Mathf.CeilToInt(clip.length*24));
                    for(int frame=0;frame<count;frame++)
                    {
                        float time=clipName=="Death"&&count>1?clip.length*frame/(count-1):Mathf.Min(frame/24f,clip.length);clip.SampleAnimation(raw,time);
                        var actual=NativePositions(raw);var expected=d.Positions(d.Clip(clipName),time);Require(actual.Length==expected.Length,"Native vertex order/count changed");
                        for(int v=0;v<actual.Length;v++)report.maximumNativeScaledError=Mathf.Max(report.maximumNativeScaledError,Vector3.Distance(actual[v],expected[v])*scale);
                        report.comparedPositions+=actual.Length;report.verifiedFrames++;
                    }
                }
                Require(report.maximumNativeScaledError<.0005f,"Native GLB position discrepancy: "+report.maximumNativeScaledError);
                Reset(raw,d); // Do not carry unsampled Death pose properties into the Idle source fixture.
                foreach(var view in new[]{new Vector3(3,1.8f,5),new Vector3(5,1,0)})
                    RobotExpressiveCapture.Native(raw,Load<AnimationClip>(Native+"/Idle.anim"),.4f,Path.Combine(evidence,view.z>0?"native-front.png":"native-side.png"),view);
            }
            finally{Object.DestroyImmediate(stage);}
            report.passed=true;WriteNew(Path.Combine(evidence,"native-report.json"),JsonUtility.ToJson(report,true));Debug.Log("ROBOT_NATIVE_READY "+report.maximumNativeScaledError);
        }
        internal static Vector3[] NativePositions(GameObject root)
        {
            var result=new List<Vector3>();var temporary=new Mesh();
            try
            {
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Vector3[] p;if(r is SkinnedMeshRenderer s){temporary.Clear();s.BakeMesh(temporary,false);p=temporary.vertices;}
                    else if(r is MeshRenderer)p=r.GetComponent<MeshFilter>().sharedMesh.vertices;else throw new InvalidOperationException("Unexpected source renderer");
                    var m=root.transform.worldToLocalMatrix*r.localToWorldMatrix;result.AddRange(p.Select(v=>m.MultiplyPoint3x4(v)));
                }
                return result.ToArray();
            }
            finally{Object.DestroyImmediate(temporary);}
        }
        static void Reset(GameObject root,RobotExpressiveData d)
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(!t.name.StartsWith("N"))continue;
                int index;if(t.name.Length<4||!int.TryParse(t.name.Substring(1,3),out index))continue;var n=d.nodes[index];
                t.localPosition=RobotExpressiveData.V(n.translation);t.localRotation=RobotExpressiveData.Q(n.rotation);t.localScale=new Vector3(n.scale[0],n.scale[1],n.scale[2]);
                if(n.skin>=0){t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;}
            }
            foreach(var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))for(int i=0;i<s.sharedMesh.blendShapeCount;i++)s.SetBlendShapeWeight(i,0);
        }
        public static void Prepare01()
        {
            var d=Data();string evidence=EvidenceDir();Require(!Directory.Exists(Prepared)&&!Directory.Exists(Output)&&!File.Exists(RecipePath),"Fresh preparation and bake output required");
            CharacterPipeline.EnsureFolder(Prepared);var source=Load<GameObject>(Native+"/RobotExpressive.prefab");
            var report=NewEvidence(d);report.method="Prepared-only canonical Generic adapter: preserves world rest geometry, removes constant import scales, rigid animated parts become one-bone skins, existing hand skinning preserved. Only zero-weight face targets/curves in selected4 clips are omitted in the prepared derivative; native original remains intact. NO generic gate changes, NO original position tracks removed.";
            var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,Prepared,"RobotTemplate");template.unitTypeName="机器人试转 · 原生出拳";CharacterPipeline.Save(template);
            // Source GLB has no textures. Preserve each original solid colour in a declared 8x4 palette.
            // Use serialized native colours to avoid exact-float mismatches after Unity material serialization.
            var palette=source.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Select(m=>m.color).Distinct().ToArray();
            Require(palette.Length==d.materials.Length&&palette.All(c=>d.materials.Any(s=>Vector4.Distance(new Vector4(c.r,c.g,c.b,c.a),new Vector4(s.color[0],s.color[1],s.color[2],s.color[3]))<.00001f)),"Native palette differs from authored colours");
            Require(palette.Length<=32,"Bounded palette exceeded");
            var atlas=new Texture2D(128,64,TextureFormat.RGBA32,false,true){name="RobotSourcePalette",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[128*64];for(int y=0;y<64;y++)for(int x=0;x<128;x++){int tile=y/16*8+x/16;pixels[y*128+x]=tile<palette.Length?palette[tile]:Color.white;}atlas.SetPixels(pixels);atlas.Apply(false,false);AssetDatabase.CreateAsset(atlas,Prepared+"/Palette.asset");
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",atlas);m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,Prepared+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,"RobotNear");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,"RobotMid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive robot canonical preparation");stage.SetActive(false);
            var raw=Object.Instantiate(source,stage.transform,false);Disable(raw);
            var root=new GameObject("RobotExpressivePrepared");root.transform.SetParent(stage.transform,false);
            var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);
            var body=Object.Instantiate(source,pivot,false);body.name="Body";Disable(body);
            var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();
            try
            {
                // Native face mesh is retained. The derivative is eligible ONLY because every selected weight is identically zero.
                foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(skin.sharedMesh.blendShapeCount>0)
                {
                    Require(skin.bones.Length==0&&skin.sharedMesh.blendShapeCount==3,"Unexpected morph/skin combination");
                    var owner=skin.gameObject;var originalMesh=skin.sharedMesh;var originalMaterials=skin.sharedMaterials;Object.DestroyImmediate(skin);
                    owner.AddComponent<MeshFilter>().sharedMesh=originalMesh;owner.AddComponent<MeshRenderer>().sharedMaterials=originalMaterials;
                }
                var originalTransforms=body.GetComponentsInChildren<Transform>(true);var oldWorld=originalTransforms.ToDictionary(t=>t,t=>t.localToWorldMatrix);
                var oldPosition=originalTransforms.ToDictionary(t=>t,t=>t.position);var oldRotation=originalTransforms.ToDictionary(t=>t,t=>t.rotation);
                foreach(var t in originalTransforms)
                {
                    var s=t.localScale;float average=(s.x+s.y+s.z)/3;Require(s.x>0&&s.y>0&&s.z>0&&(s-Vector3.one*average).magnitude/average<.0002f,"Unsupported real nonuniform import scale");
                    t.localScale=Vector3.one;t.SetPositionAndRotation(oldPosition[t],oldRotation[t]);
                }
                foreach(var renderer in body.GetComponentsInChildren<Renderer>(true))
                {
                    var skin=renderer as SkinnedMeshRenderer;var filter=renderer.GetComponent<MeshFilter>();var src=skin!=null?skin.sharedMesh:filter.sharedMesh;
                    var vertex=renderer.transform.worldToLocalMatrix*oldWorld[renderer.transform];var normal=vertex.inverse.transpose;
                    var mesh=Object.Instantiate(src);mesh.name=renderer.name+"Canonical";mesh.ClearBlendShapes();
                    mesh.vertices=src.vertices.Select(v=>vertex.MultiplyPoint3x4(v)).ToArray();mesh.normals=src.normals.Select(v=>normal.MultiplyVector(v).normalized).ToArray();
                    var uv=new Vector2[mesh.vertexCount];var assigned=new bool[uv.Length];
                    for(int sub=0;sub<src.subMeshCount;sub++)
                    {
                        var color=renderer.sharedMaterials[sub].color;int index=Array.IndexOf(palette,color);Require(index>=0,"Source material colour not in verified palette");
                        var tile=new Vector2((index%8+.5f)/8,(index/8+.5f)/4);
                        foreach(int v in src.GetTriangles(sub)){Require(!assigned[v]||uv[v]==tile,"Source shared vertex crosses material palette; explicit split required");uv[v]=tile;assigned[v]=true;}
                    }
                    mesh.uv=uv;
                    if(skin!=null)
                    {
                        mesh.bindposes=skin.bones.Select((bone,j)=>bone.worldToLocalMatrix*oldWorld[bone]*src.bindposes[j]*vertex.inverse).ToArray();
                    }
                    else
                    {
                        var bone=new GameObject("RigidPartBone").transform;bone.SetParent(renderer.transform,false);
                        mesh.boneWeights=Enumerable.Range(0,mesh.vertexCount).Select(_=>new BoneWeight{boneIndex0=0,weight0=1}).ToArray();mesh.bindposes=new[]{Matrix4x4.identity};
                        var owner=renderer.gameObject;Object.DestroyImmediate(renderer);Object.DestroyImmediate(filter);
                        skin=owner.AddComponent<SkinnedMeshRenderer>();skin.bones=new[]{bone};skin.rootBone=bone;
                    }
                    mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Prepared+"/"+mesh.name+".asset");skin.sharedMesh=mesh;skin.localBounds=mesh.bounds;skin.sharedMaterials=Enumerable.Repeat(template.renderConfig.nearMaterial,mesh.subMeshCount).ToArray();skin.updateWhenOffscreen=true;
                }
                report.fullSourceHeight=Bounds(d.Positions(null,0)).size.y;float factor=report.targetHeight/report.fullSourceHeight;
                var rawByPath=raw.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,raw.transform));
                var preparedByPath=originalTransforms.ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,body.transform));
                var bindPosition=originalTransforms.Select(t=>t.localPosition).ToArray();var bindRotation=originalTransforms.Select(t=>t.localRotation).ToArray();
                var mapped=new AnimationClip[4];string[] names={"Idle","Move","Attack","Death"};
                for(int action=0;action<4;action++)
                {
                    Reset(raw,d);var nativeClip=Load<AnimationClip>(Native+"/"+report.selectedClips[action]+".anim");int frames=Mathf.Max(1,Mathf.CeilToInt(nativeClip.length*24));
                    var times=new SortedSet<float>{0,nativeClip.length};for(int f=0;f<=Mathf.CeilToInt(nativeClip.length*120);f++)times.Add(Mathf.Min(nativeClip.length,f/120f));
                    if(action==3&&frames>1)for(int f=0;f<frames;f++)times.Add(nativeClip.length*f/(frames-1));
                    var keys=preparedByPath.ToDictionary(p=>p.Key,p=>Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray());
                    foreach(float time in times)
                    {
                        nativeClip.SampleAnimation(raw,time);
                        foreach(var p in preparedByPath)p.Value.SetPositionAndRotation(rawByPath[p.Key].position,rawByPath[p.Key].rotation);
                        foreach(var p in preparedByPath)
                        {
                            var t=p.Value;float[] v={t.localPosition.x,t.localPosition.y,t.localPosition.z,t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w};
                            for(int k=0;k<7;k++)keys[p.Key][k].Add(new Keyframe(time,v[k]));
                        }
                    }
                    var clip=new AnimationClip{name=names[action],frameRate=24};
                    foreach(var p in keys)
                    {
                        string path="ScalePivot/Body"+(p.Key.Length>0?"/"+p.Key:"");
                        for(int k=0;k<7;k++)RobotExpressiveData.SetLinear(clip,path,typeof(Transform),(k<3?"m_LocalPosition.":"m_LocalRotation.")+"xyzw"[k<3?k:k-3],p.Value[k].ToArray());
                    }
                    clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=action!=3;AnimationUtility.SetAnimationClipSettings(clip,settings);AssetDatabase.CreateAsset(clip,Prepared+"/"+names[action]+".anim");mapped[action]=clip;
                    for(int f=0;f<frames;f++)
                    {
                        float time=action==3&&frames>1?nativeClip.length*f/(frames-1):Mathf.Min(f/24f,nativeClip.length);clip.SampleAnimation(root,time);
                        var actual=CharacterGeometry.Positions(root,recipe);var expected=d.Positions(d.Clip(report.selectedClips[action]),time);Require(actual.Length==expected.Length,"Prepared vertex count/order changed");
                        for(int v=0;v<actual.Length;v++)report.maximumPreparedScaledError=Mathf.Max(report.maximumPreparedScaledError,Vector3.Distance(actual[v],expected[v])*factor);
                        report.comparedPositions+=actual.Length;report.verifiedFrames++;
                    }
                    for(int i=0;i<originalTransforms.Length;i++){originalTransforms[i].localPosition=bindPosition[i];originalTransforms[i].localRotation=bindRotation[i];}
                }
                Require(report.maximumPreparedScaledError<.0005f,"Prepared/source discrepancy: "+report.maximumPreparedScaledError);
                // This is the unchanged Generic gate; all original body parts now contribute to measured body height.
                CharacterGeometry.CheckHierarchy(root,"ScalePivot");Require(body.GetComponentsInChildren<MeshRenderer>(true).Length==0,"Incomplete rigid conversion");
                Require(PrefabUtility.SaveAsPrefabAsset(root,Prepared+"/Robot.prefab")!=null,"Cannot save prepared prefab");
                report.preparedVertices=body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s=>s.sharedMesh.vertexCount);report.preparedTriangles=body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s=>s.sharedMesh.triangles.Length/3);
                Require(report.preparedVertices==7214&&report.preparedTriangles==3237,"All original triangles and vertices must remain present");
                recipe.name="TowerDefenseRobot01";recipe.outputName="TowerDefenseRobot01";recipe.model=Load<GameObject>(Prepared+"/Robot.prefab");recipe.sizeRootPath="ScalePivot";recipe.frameRate=24;recipe.targetBodyHeight=1.8f;recipe.agentRadius=.45f;recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.unitTemplate=template;recipe.paletteColumns=8;recipe.paletteRows=4;recipe.lowPolicy=CharacterLowLodPolicy.PaletteGrid;recipe.lowVertexBudget=1400;recipe.fullVertexBudget=8000;recipe.maxTextureMiB=32;recipe.maximumPositionError=.0025f;
                AssetDatabase.CreateAsset(recipe,RecipePath); // Persist before scene switching can unload unused transient objects.
                MakeTrialTemplates(template);recipe=Load<CharacterRecipe>(RecipePath);recipe.createTrial=true;recipe.trialMenu=Load<SceneAsset>(Knight+"/Trial/Menu.unity");recipe.trialBattlefield=Load<SceneAsset>(Prepared+"/TrialTemplates/Battlefield.unity");recipe.trialCatalog=Load<WarSandboxBattlefieldCatalog>(Prepared+"/TrialTemplates/Catalog.asset");recipe.battlefieldId="robot-expressive-pilot";recipe.templateId="tower-defense-robot-pilot";
                CharacterPipeline.Save(recipe);
            }
            finally{if(stage!=null)Object.DestroyImmediate(stage);}
            var pipeline=CharacterPipeline.Run(recipe);report.pipelineEvidence=pipeline.evidence;report.maximumVatScaledError=pipeline.maxPositionError;report.passed=pipeline.automatedPassed;
            Require(report.passed,"VAT pipeline failed");WriteNew(Path.Combine(evidence,"preparation-report.json"),JsonUtility.ToJson(report,true));Debug.Log("ROBOT_VAT_READY "+Output);
        }
        static void MakeTrialTemplates(UnitTypeConfig robot)
        {
            string folder=Prepared+"/TrialTemplates";CharacterPipeline.EnsureFolder(folder);
            var prior=Load<ScenarioConfig>(Knight+"/Trial/Scenario.asset");Require(prior.unitTypes.Length==2,"Expected two-unit trial template");
            var opponent=CharacterPipeline.CloneUnit(prior.unitTypes[1],folder,"Opponent");
            robot.spawnConfig.unitCount=64;opponent.spawnConfig.unitCount=64;CharacterPipeline.Save(robot.spawnConfig);CharacterPipeline.Save(opponent.spawnConfig);
            var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{robot,opponent};AssetDatabase.CreateAsset(scenario,folder+"/Scenario.asset");
            Require(AssetDatabase.CopyAsset(Knight+"/Trial/Battlefield.unity",folder+"/Battlefield.unity"),"Cannot create isolated trial battlefield");
            var setup=EditorSceneManager.GetSceneManagerSetup();try{var scene=EditorSceneManager.OpenScene(folder+"/Battlefield.unity",OpenSceneMode.Single);Object.FindFirstObjectByType<MassEngineManager>().scenarioConfig=scenario;EditorSceneManager.SaveScene(scene);}finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            var catalog=Object.Instantiate(Load<WarSandboxBattlefieldCatalog>(Knight+"/Trial/Catalog.asset"));catalog.entries[0].scenePath=folder+"/Battlefield.unity";catalog.templates[0].config=robot;catalog.templates[1].config=opponent;AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");
        }
        public static void Capture01()
        {
            string evidence=EvidenceDir();var unit=Load<UnitTypeConfig>(Output+"/Unit.asset");var profile=(VATProfile)unit.renderConfig.vatProfile;var runtime=ResolvedUnitTypeRuntime.Resolve(unit,1);
            // Fresh saved prefab, not the temporary validator left at Death's final pose.
            var native=Load<GameObject>(Native+"/RobotExpressive.prefab");var idle=Load<AnimationClip>(Native+"/Idle.anim");
            RobotExpressiveCapture.Native(native,idle,.4f,Path.Combine(evidence,"source-native-front.png"),new Vector3(3,1.8f,-5));
            RobotExpressiveCapture.Native(native,idle,.4f,Path.Combine(evidence,"source-native-side.png"),new Vector3(5,1,0));
            var bounds=VatAppearanceRegression.CombinedBounds(profile);var states=new[]{AgentState.Idle,AgentState.Move,AgentState.Attack,AgentState.Dead};
            using(var capture=new VatAppearanceRegressionCapture(bounds,640,new Vector3(3,1.8f,-5)))
            {
                foreach(var state in states)
                {
                    var clip=VatAppearanceRegression.GetClip(profile,state);var images=new List<Color32[]>();
                    foreach(int f in Enumerable.Range(0,12))
                    {
                        float time=(clip.frameCount-1)*f/11f/clip.frameRate;var p=capture.Capture(runtime,0,state,time);RobotExpressiveCapture.Check(p);images.Add(p);RobotExpressiveCapture.Save(Path.Combine(evidence,"gpu-"+state+"-"+f.ToString("D2")+".png"),640,p);
                    }
                    Require(VatAppearanceRegressionImageChecks.HasMotion(images,out int changed),"No rendered motion: "+state);
                }
                // Tight true GPU portrait from the baked Idle frames, rather than the very wide all-action/death envelope.
                Require(profile.rowsPerFrame*profile.textureWidth>=profile.cleanMesh.vertexCount&&profile.idle.frameCount>8,"Unexpected bounded VAT layout");
                var baked=profile.positionTexture.GetPixels();var idlePoints=new Vector3[profile.cleanMesh.vertexCount];
                for(int v=0;v<idlePoints.Length;v++){Color a=baked[(profile.idle.startFrame+7)*profile.rowsPerFrame*profile.textureWidth+v],b=baked[(profile.idle.startFrame+8)*profile.rowsPerFrame*profile.textureWidth+v];idlePoints[v]=Vector3.Lerp(new Vector3(a.r,a.g,a.b),new Vector3(b.r,b.g,b.b),.2f);}
                Color32[] portrait;using(var portraitCapture=new VatAppearanceRegressionCapture(Bounds(idlePoints),640,new Vector3(3,1.8f,-5)))portrait=portraitCapture.Capture(runtime,0,AgentState.Idle,.3f);
                RobotExpressiveCapture.Check(portrait);RobotExpressiveCapture.Save(Path.Combine(evidence,"robot-full-body.png"),640,portrait);
                string path=Output+"/Trial/RobotPreview02.png";RobotExpressiveCapture.Save(path,640,portrait);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                var catalog=Load<WarSandboxBattlefieldCatalog>(Output+"/Trial/Catalog.asset");catalog.templates[0].unitPreview=Load<Texture2D>(path);catalog.entries[0].featuredTemplateIds=new[]{catalog.templates[0].templateId};CharacterPipeline.Save(catalog);
            }
            using(var side=new VatAppearanceRegressionCapture(bounds,640,new Vector3(5,1,0)))RobotExpressiveCapture.Save(Path.Combine(evidence,"gpu-side.png"),640,side.Capture(runtime,0,AgentState.Idle,.3f));
            WriteNew(Path.Combine(evidence,"capture-report.json"),"{\"passed\":true,\"gpuActionFrames\":48,\"formalRosterChanged\":false,\"humanAcceptance\":false}");Debug.Log("ROBOT_CAPTURE_READY");
        }
        public static bool ValidateGeneratedBinding() => UnitTypeBinder.ValidateBinding(Load<UnitTypeConfig>(Output+"/Unit.asset")).IsValid;
        public static void Build01()
        {
            Require(ValidateGeneratedBinding(),"Cannot build invalid pilot binding");string evidence=EvidenceDir();
            string folder="Builds/RobotExpressivePilot-20261002-02";Require(!Directory.Exists(folder),"Fresh isolated player build required");Directory.CreateDirectory(folder);
            var build=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Output+"/Trial/Menu.unity",Output+"/Trial/Battlefield.unity"},locationPathName=folder+"/RobotExpressivePilot.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            Require(build.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"Isolated robot build failed");
            WriteNew(Path.Combine(evidence,"build-report.json"),"{\"passed\":true,\"buildGuid\":\""+build.summary.guid+"\",\"package\":\""+folder+"\",\"formalRosterChanged\":false,\"humanAcceptance\":false}");Debug.Log("ROBOT_ISOLATED_BUILD_READY "+folder);
        }
        static void Disable(GameObject root){foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;}
        internal static Bounds Bounds(Vector3[] positions){var b=new Bounds(positions[0],Vector3.zero);foreach(var p in positions)b.Encapsulate(p);return b;}
        static T Load<T>(string p)where T:Object => AssetDatabase.LoadAssetAtPath<T>(p)??throw new InvalidOperationException("Missing "+p);
        static void Require(bool ok,string reason)=>CharacterGeometry.Require(ok,reason);
        static void WriteNew(string p,string text){using(var f=new FileStream(p,FileMode.CreateNew))using(var w=new StreamWriter(f))w.Write(text);}
    }
}
