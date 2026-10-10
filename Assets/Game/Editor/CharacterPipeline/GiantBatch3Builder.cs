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
    // Giant batch 3: more super-large (6-8 m class) ground units through the accepted GiantBuilder path. PrepareOne is the
    // GiantBuilder copy with per-unit source paths, clip choices and an optional ranged profile; GiantBuilder, DragonBuilder
    // and the shared pipeline are untouched. Yeti reuses the already imported official CC0 Yeti.fbx (the regular 2.4 m
    // "雪怪" stays as it is; this is a separate super-large template). BlueDemon / Alien are prepared once their official
    // CC0 sources are imported into QuaterniusGiants3.
    public static class GiantBatch3Builder
    {
        const string AtlasPath="Assets/Art/Source/CharacterPilot/QuaterniusMonsters/Atlas_Monsters.png";
        const string Source3="Assets/Art/Source/CharacterPilot/QuaterniusGiants3";
        static string Log="Logs/AgentMonsters3";
        static string Prepared="Assets/Game/Content/Characters/Giants3/Prepared01";
        const string Knight="Assets/Game/Authoring/CharacterPipeline/Generated/Knight04";
        const string LargeScene="Assets/Game/Content/Characters/UnifiedRoster/Version03/Scenes/LargeBattlefield.unity";
        const string KnightControl="Assets/Game/Content/Characters/Dragons/Prepared13/Integrated/Library/DragonSizedKnight.asset";
        static string Base="Assets/Game/Content/Characters/Giants/Prepared02/Integrated";
        static readonly string[] Sources={"Assets/Art/Source/CharacterPilot/QuaterniusMonsters/Yeti.fbx",Source3+"/BlueDemon.fbx",Source3+"/Alien.fbx"};
        static readonly string[] Names={"Yeti","BlueDemon","Alien"};
        static readonly string[] Keys={"giant-yeti","giant-bluedemon","giant-alien"};
        static readonly string[] Titles={"巨型雪人（超大）","巨型蓝魔（超大）","巨型外星人（超大）"};
        static readonly string[] MoveClips={"Walk/Run","Walk/Run","Walk/Run"};
        static readonly string[] AttackClips={"Punch","Punch","Weapon/Punch"};
        static readonly bool[] Ranged={false,false,true};
        static readonly float[] TargetHeights={7.2f,7.2f,6.5f};
        static readonly int[] Hp={1600,1500,1100},Damage={75,85,45};static readonly float[] Speed={3f,3f,3f};
        static readonly string[] OutputNames={"GiantYeti01","BlueDemon01","Alien01"};
        static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+OutputNames[i];
        const float CoordinateLimit=7.9f,PrecisionBudget=.00235f;
        static float HalfAxis(float v){v=Mathf.Abs(v);if(v<6.1e-5f)return 3e-8f;int e=Mathf.FloorToInt(Mathf.Log(v,2));return Mathf.Pow(2,e-11);}
        static float HalfBound(Vector3 c){float x=HalfAxis(c.x),y=HalfAxis(c.y),z=HalfAxis(c.z);return Mathf.Sqrt(x*x+y*y+z*z);}
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static AnimationClip ClipAny(string path,string names){foreach(var n in names.Split('/')){var c=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview__")).FirstOrDefault(x=>x.name==n||x.name.EndsWith("|"+n,StringComparison.Ordinal));if(c!=null)return c;}throw new InvalidOperationException("No clip "+names+" in "+path+": "+string.Join(",",AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Select(x=>x.name)));}
        static AnimationClip Clip(string path,string name)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Single(c=>c.name==name||c.name.EndsWith("|"+name,StringComparison.Ordinal));
        static bool ImportMatches(ModelImporter im)=>im.animationType==ModelImporterAnimationType.Generic&&im.isReadable&&im.materialImportMode==ModelImporterMaterialImportMode.ImportStandard&&im.importBlendShapes;
        // Pre-existing sources (Yeti, shared with the regular 雪怪) must already match: never edit their .meta.
        static void ConfigureImport(string path,bool mayEdit){var im=(ModelImporter)AssetImporter.GetAtPath(path);if(ImportMatches(im))return;CharacterGeometry.Require(mayEdit,"Existing source import settings differ; refusing to edit "+path);im.animationType=ModelImporterAnimationType.Generic;im.isReadable=true;im.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;im.importBlendShapes=true;im.SaveAndReimport();}
        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only: static import scales removed while preserving world rest transforms; owned mesh vertices/normals/bindposes; authored local poses resampled from world transforms; final size applied before the first Half VAT encoding. Source UVs and the official monster atlas are kept.";
            public int frames,positions;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,requestedHeight,targetHeight,scale,sourceMaxAnimatedExtent,finalMaxAbsCoordinate,collisionRadius,minimumActiveY,maximumActiveY;public float halfPrecisionBoundMm,wingspan,bodyLength,hoverTop,groundOffsetSource;public bool heightLimitedByCoordinateBudget,passed;public Vector3 sourceXZCenter;public string[] clips;
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Names[index];CharacterPipeline.EnsureFolder(folder);string modelPath=Sources[index];ConfigureImport(modelPath,Sources[index].StartsWith(Source3+"/"));
            var source=Load<GameObject>(modelPath);var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.teamId=0;template.unitTypeName=Titles[index];
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(AtlasPath));m.SetColor("_BaseColor",new Color(.8f,.8f,.8f,1));AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,Names[index]+"Near");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,Names[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive giant batch 3 source preparation");stage.SetActive(false);
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
                string[] sourceNames={"Idle",MoveClips[index],AttackClips[index],"Death"};var originals=sourceNames.Select(n=>ClipAny(modelPath,n)).ToArray();evidence.clips=originals.Select(c=>c.name+" "+c.length.ToString("F3")+"s").ToArray();
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
                template.combatConfig.projectileRange=0;template.combatConfig.targetAcquireRadius=16;template.combatConfig.attackInterval=mapped[2].length;template.combatConfig.attackRange=evidence.collisionRadius+.55f+.5f;template.combatConfig.maxHp=Hp[index];template.combatConfig.attackDamage=Damage[index];template.movementConfig.maxSpeed=Speed[index];template.spawnConfig.unitCount=2;template.spawnConfig.spawnCenter=new Vector3(-45,0,0);float side=(2*evidence.collisionRadius+1)*3;template.spawnConfig.spawnSize=new Vector3(side,0,side);template.movementConfig.maxSpeed=3f;template.animationConfig.moveReferenceSpeed=3f;template.combatConfig.projectileTargetHeight=evidence.targetHeight*.5f;if(Ranged[index])ConfigureRanged(template,evidence.targetHeight);
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+OutputNames[index]+".asset");evidence.passed=true;File.WriteAllText(Log+"/"+Keys[index]+"-canonical-01.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/"+Keys[index]+"-report-01.json",JsonUtility.ToJson(report,true));Debug.Log("GIANT3_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices+" passed="+report.automatedPassed);
            CharacterGeometry.Require(report.automatedPassed,"Dragon pipeline gate failed: "+Keys[index]);
        }
        // Ranged super-large profile (Alien): direct shot without splash, released mid-attack from chest height. First functional values.
        static void ConfigureRanged(UnitTypeConfig u,float height)
        {
            var c=u.combatConfig;c.projectileRange=16f;c.targetAcquireRadius=22f;c.projectileSpeed=22f;c.projectileGravity=0f;c.projectileHitRadius=.6f;c.projectileMaxLifetime=2f;c.projectileTrailLength=2.5f;
            c.projectileOriginHeight=height*.55f;c.attackReleasePhase=.5f;c.projectileSplashRadius=0f;CharacterPipeline.Save(c);
        }
        static void PrepareSet(params int[] units)
        {
            foreach(int i in units){CharacterGeometry.Require(!Directory.Exists(Output(i))&&!File.Exists(CharacterPipeline.Root+"/Recipes/"+OutputNames[i]+".asset"),"Never overwrite "+OutputNames[i]);CharacterGeometry.Require(File.Exists(Sources[i]),"Missing source "+Sources[i]);CharacterGeometry.Require(!Directory.Exists(Prepared+"/"+Names[i]),"Fresh preparation required: "+Names[i]);}
            Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            foreach(int i in units)PrepareOne(i);
            foreach(int i in units){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Giant model not accepted: "+OutputNames[i]);}
            Debug.Log("GIANT3_PREPARATION_READY "+string.Join(",",units.Select(i=>OutputNames[i])));
        }
        public static void PrepareYeti01()=>PrepareSet(0);
        public static void PrepareBlueDemonAlien01()=>PrepareSet(1,2);

        // Read-only: clip names and bounds of the batch-3 sources, to pick move/attack clips before preparing.
        public static void Inspect01()
        {
            Directory.CreateDirectory(Log);var sb=new StringBuilder();
            for(int i=0;i<Sources.Length;i++)
            {
                if(!File.Exists(Sources[i])){sb.AppendLine(Names[i]+": missing "+Sources[i]);continue;}
                var clips=AssetDatabase.LoadAllAssetsAtPath(Sources[i]).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>c.name+" "+c.length.ToString("F3")+"s");
                var im=(ModelImporter)AssetImporter.GetAtPath(Sources[i]);sb.AppendLine(Names[i]+": importMatches="+ImportMatches(im)+" type="+im.animationType+" readable="+im.isReadable+" materials="+im.materialImportMode+" blendShapes="+im.importBlendShapes+" | "+string.Join(", ",clips));
            }
            File.WriteAllText(Log+"/inspect-01.txt",sb.ToString());Debug.Log("GIANT3_INSPECT_READY\n"+sb);
        }

        // Super-large battlefields (2 giants vs the 16 dragon-sized knights, like the giant showcase) on top of the newest giant
        // collection (Giants/Prepared02: dragons, phalanx 150, stronger cavalry, demon, dino); the new giants join the large menu.
        static void Integrate(int[] units)
        {
            string folder=Prepared+"/Integrated",lib=folder+"/Library";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated directory required.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(lib);
            foreach(int i in units){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model: "+OutputNames[i]);}
            var oldCat=Load<WarSandboxBattlefieldCatalog>(Base+"/Catalog.asset");var oldPolicy=Load<WarSandboxRosterPolicy>(Base+"/LargePolicy.asset");
            var units3=new List<UnitTypeConfig>();
            foreach(int i in units){var u=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(Output(i)+"/Unit.asset"),lib,Keys[i]);u.unitTypeName=Titles[i];u.teamId=0;u.spawnConfig.unitCount=2;u.spawnConfig.spawnCenter=new Vector3(-45,0,0);CharacterPipeline.Save(u.spawnConfig);CharacterPipeline.Save(u);units3.Add(u);}
            CharacterGeometry.Require(units3.All(u=>u.flockingConfig.agentRadius<=oldPolicy.maximumRadius-.1f+.001f),"Giant wider than the large menu radius; the dragon-sized knights would not reach it.");
            var policy=Object.Instantiate(oldPolicy);policy.name="SuperLargeRosterGiants3";policy.templates=oldPolicy.templates.Concat(units3).ToArray();policy.explanation=oldPolicy.explanation+" 另有"+string.Join("/",units.Select(i=>Titles[i].Replace("（超大）","")))+"。";AssetDatabase.CreateAsset(policy,folder+"/LargePolicy.asset");
            CharacterGeometry.Require(policy.TryValidateDefinition(out string perr),perr);AssetDatabase.SaveAssets();
            string policyPath=folder+"/LargePolicy.asset";float cell=Mathf.Ceil((oldPolicy.maximumRadius-.1f)*2+1);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                foreach(int i in units)
                {
                    var giant=Load<UnitTypeConfig>(lib+"/"+Keys[i]+".asset");var knight=Load<UnitTypeConfig>(KnightControl);
                    var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{giant,knight};AssetDatabase.CreateAsset(scenario,folder+"/"+Keys[i]+"Scenario.asset");
                    string battle=folder+"/"+Keys[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(LargeScene,battle),"Cannot copy giant scene.");
                    var scene=EditorSceneManager.OpenScene(battle,OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+Keys[i]+"Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(policyPath);
                    var system=Object.Instantiate(manager.systemConfig);var sim=Object.Instantiate(system.simulationConfig);sim.cellSize=cell;sim.simulationWorldSize=new Vector2(240,240);sim.maxAgentsPerCell=64;AssetDatabase.CreateAsset(sim,folder+"/"+Keys[i]+"Simulation.asset");system.simulationConfig=sim;AssetDatabase.CreateAsset(system,folder+"/"+Keys[i]+"System.asset");manager.systemConfig=system;EditorSceneManager.SaveScene(scene);
                }
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Base+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy menu.");
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            // OpenScene(Single) unloads unreferenced assets: reload everything by path from here on.
            oldCat=Load<WarSandboxBattlefieldCatalog>(Base+"/Catalog.asset");var catalog=Object.Instantiate(oldCat);catalog.name="UnifiedGiants3";
            var source=oldCat.entries.Single(e=>e.id=="giants-demon");var newEntries=new List<WarSandboxBattlefieldEntry>();var templates=oldCat.templates.ToList();
            foreach(int i in units)
            {
                var giant=Load<UnitTypeConfig>(lib+"/"+Keys[i]+".asset");var c=giant.combatConfig;
                var e=source.CopyIdentity();e.id="giants-"+Names[i].ToLowerInvariant();e.displayName=Titles[i]+" · 超大第三批";e.scenePath=folder+"/"+Keys[i]+"Battlefield.unity";e.preview=null;
                e.description="官方CC0 Quaternius Ultimate Monsters "+Names[i]+"，沿用巨人的超大流程：尺寸在VAT编码前处理，全动作坐标≤7.9m。2 只对 16 名适配骑士。";
                e.briefing=(Ranged[i]?"地面超大远程（射程 "+c.projectileRange.ToString("0")+"m）":"地面超大近战")+"（HP "+c.maxHp+" / 攻击 "+c.attackDamage+" / 移速 "+giant.movementConfig.maxSpeed.ToString("0.#")+"m/s），单体攻击、无范围伤害；首版数值，未做平衡。";
                newEntries.Add(e);templates.Add(new WarSandboxUnitTemplateEntry{templateId="roster-"+Keys[i],revision=1,config=giant});
            }
            catalog.entries=oldCat.entries.Select(e=>e.CopyIdentity()).Concat(newEntries).ToArray();catalog.templates=templates.ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");
            CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);AssetDatabase.SaveAssets();
            setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var router=Object.FindFirstObjectByType<NonhumanBatchCaseRouter>();int templateCount=Load<WarSandboxRosterPolicy>(policyPath).templates.Length;
                router.entries=router.entries.Concat(units.Select(i=>new NonhumanBatchCaseRouter.Entry{key=Keys[i],battlefieldId="giants-"+Names[i].ToLowerInvariant(),templateId="roster-"+Keys[i],unit=Load<UnitTypeConfig>(lib+"/"+Keys[i]+".asset"),scenario=Load<ScenarioConfig>(folder+"/"+Keys[i]+"Scenario.asset"),initial=2,saved=3,templates=templateCount,large=true})).ToArray();
                router.smoke.session=session;router.smoke.expectedCatalog=session.catalog;
                var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.previewName="Unified Roster + Dragons + Giants 3";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/integration-"+Path.GetFileName(Prepared)+".json","{\"passed\":true,\"newModels\":"+units.Length+",\"entries\":"+catalog.entries.Length+",\"oldSourceEdited\":false}");Debug.Log("GIANT3_INTEGRATION_READY "+catalog.entries.Length);
        }
        /// <summary>Yeti only (the other two sources are not imported yet).</summary>
        public static void IntegrateYeti01()=>Integrate(new[]{0});
        /// <summary>BlueDemon + Alien on top of the Yeti collection, once their sources are prepared.</summary>
        public static void IntegrateBlueDemonAlien02(){Base="Assets/Game/Content/Characters/Giants3/Prepared01/Integrated";Prepared="Assets/Game/Content/Characters/Giants3/Prepared02";Integrate(new[]{1,2});}
    }
}
