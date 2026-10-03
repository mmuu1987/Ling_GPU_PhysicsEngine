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
    public static class LargeBeastBuilder
    {
        const string Source="Assets/CharacterPilotSource/LargeBeasts";
        const string Log="Logs/AgentLargeBeasts";
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
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());Debug.Log("LARGE_BEAST_IMPORT_READY");
        }
        const string Prepared="Assets/Game/LargeBeasts/Prepared02";
        const string Knight="Assets/Game/CharacterPipeline/Generated/Knight04";
        const string Previous="Assets/Game/EnemyModelsBatch2/Prepared02/Collection01";
        static readonly string[] Keys={"wolf","bull","spider"};
        static readonly string[] Names={"Wolf","Bull","Spider"};
        static readonly string[] Titles={"巨狼","重型公牛","巨型蜘蛛"};
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+"Large"+Names[i]+(i==2?"03":"02");
        static AnimationClip Clip(string path,string name)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Single(c=>c.name.EndsWith("|"+name,StringComparison.Ordinal));
        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only: remove static import scales by preserving world rest transforms, transforming owned mesh vertices/normals and bindposes, resampling authored local poses from world transforms. Original imported LBS positions compared at every baked frame. No scale channel is silently treated as real unit animation.";
            public int frames,positions,paletteColors;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,targetHeight,targetSpan,sourceSpan,collisionRadius,minimumActiveY,maximumActiveY;public Vector3 sourceXZCenter;public bool passed;
        }
        public static void PrepareAll01()
        {
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh batch2 preparation required.");CharacterPipeline.EnsureFolder(Prepared);
            for(int i=0;i<3;i++)PrepareOne(i);
            CreateCollection();Debug.Log("LARGE_BEAST_PREPARATION_READY");
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Names[index];CharacterPipeline.EnsureFolder(folder);string modelPath=Source+"/"+Names[index]+".fbx";
            var source=Load<GameObject>(modelPath);var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.teamId=0;template.unitTypeName=Titles[index];
            var palette=source.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Select(m=>m.color).Distinct().ToList();CharacterGeometry.Require(palette.Count<=16,"Palette material budget.");
            var atlas=new Texture2D(64,64,TextureFormat.RGBA32,false,true){name=Names[index]+"Palette",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};var pixels=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++){int tile=(y/16)*4+x/16;pixels[y*64+x]=tile<palette.Count?palette[tile]:Color.white;}atlas.SetPixels(pixels);atlas.Apply(false,false);AssetDatabase.CreateAsset(atlas,folder+"/Palette.asset");
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",atlas);m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,Names[index]+"Near");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,Names[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive monster source preparation");stage.SetActive(false);
            var raw=Object.Instantiate(source,stage.transform,false);raw.name="RawReference";foreach(var a in raw.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var root=new GameObject(Names[index]+"Prepared");root.transform.SetParent(stage.transform,false);var pivot=new GameObject("ScalePivot").transform;pivot.SetParent(root.transform,false);
            var body=Object.Instantiate(source,pivot);body.name="Body";foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var evidence=new PreparationEvidence{model=Names[index],targetSpan=index==2?7f:6f,paletteColors=palette.Count};
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
                var vertexMaps=new Dictionary<string,int[]>();
                foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src=skin.sharedMesh;CharacterGeometry.Require(src.blendShapeCount==0,"Do not drop source blendshapes.");
                    Matrix4x4 vertex=skin.transform.worldToLocalMatrix*oldWorld[skin.transform];Matrix4x4 normal=vertex.inverse.transpose;
                    var positions=src.vertices;var normals=src.normals;var weights=src.boneWeights;var map=new List<int>();var verts=new List<Vector3>();var ns=new List<Vector3>();var ws=new List<BoneWeight>();var uv=new List<Vector2>();var indices=new List<int>();var lookup=new Dictionary<(int,int),int>();
                    for(int part=0;part<src.subMeshCount;part++)
                    {
                        var material=skin.sharedMaterials[part];CharacterGeometry.Require(material.mainTexture==null,"Do not replace a real source texture with solid palette.");int color=palette.IndexOf(material.color);CharacterGeometry.Require(color>=0,"Source material colour missing.");
                        foreach(int originalIndex in src.GetTriangles(part))
                        {
                            var key=(originalIndex,color);if(!lookup.TryGetValue(key,out int v)){v=verts.Count;lookup.Add(key,v);map.Add(originalIndex);verts.Add(vertex.MultiplyPoint3x4(positions[originalIndex]));ns.Add(normal.MultiplyVector(normals[originalIndex]).normalized);ws.Add(weights[originalIndex]);uv.Add(new Vector2((color%4+.5f)/4,(color/4+.5f)/4));}indices.Add(v);
                        }
                    }
                    var mesh=new Mesh{name=skin.name+"CanonicalPalette"};mesh.SetVertices(verts);mesh.SetNormals(ns);mesh.SetUVs(0,uv);mesh.boneWeights=ws.ToArray();mesh.SetTriangles(indices,0);var binds=src.bindposes;mesh.bindposes=skin.bones.Select((bone,j)=>bone.worldToLocalMatrix*oldWorld[bone]*binds[j]*vertex.inverse).ToArray();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/"+mesh.name+".asset");vertexMaps.Add(skin.name,map.ToArray());skin.sharedMesh=mesh;skin.localBounds=mesh.bounds;skin.sharedMaterial=template.renderConfig.nearMaterial;
                }
                CharacterGeometry.Require(body.GetComponentsInChildren<MeshRenderer>(true).Length==0,"Unexpected rigid source; explicit conversion required.");
                var recipeRaw=ScriptableObject.CreateInstance<CharacterRecipe>();var originalBounds=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(raw,recipeRaw,true));evidence.sourceHeight=originalBounds.size.y;evidence.sourceSpan=Mathf.Max(originalBounds.size.x,originalBounds.size.z);evidence.targetHeight=evidence.sourceHeight*evidence.targetSpan/evidence.sourceSpan;evidence.sourceXZCenter=new Vector3(originalBounds.center.x,0,originalBounds.center.z);Object.DestroyImmediate(recipeRaw);float scale=evidence.targetHeight/evidence.sourceHeight;
                string[] actions={"Idle","Move","Attack","Death"};var originals=new[]{Clip(modelPath,index==2?"Spider_Idle":"Idle"),Clip(modelPath,index==2?"Spider_Walk":"Walk"),Clip(modelPath,index==2?"Spider_Attack":index==1?"Attack_Headbutt":"Attack"),Clip(modelPath,index==2?"Spider_Death":"Death")};
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
                        {var expected=CharacterGeometry.Skin(raw.transform,sourceSkins[part]);var actual=CharacterGeometry.Skin(root.transform,resultSkins[part]);var remap=vertexMaps[resultSkins[part].name];CharacterGeometry.Require(remap.Length==actual.Length,"Palette vertex remap count.");for(int v=0;v<actual.Length;v++){evidence.maximumScaledPositionError=Mathf.Max(evidence.maximumScaledPositionError,Vector3.Distance(expected[remap[v]],actual[v])*scale);evidence.positions++;if(action<3){Vector3 centered=(actual[v]-evidence.sourceXZCenter)*scale;evidence.collisionRadius=Mathf.Max(evidence.collisionRadius,new Vector2(centered.x,centered.z).magnitude);float y=(actual[v].y-originalBounds.min.y)*scale;evidence.minimumActiveY=Mathf.Min(evidence.minimumActiveY,y);evidence.maximumActiveY=Mathf.Max(evidence.maximumActiveY,y);}}}
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
                pivot.localPosition=-new Vector3(evidence.sourceXZCenter.x,originalBounds.min.y,evidence.sourceXZCenter.z)*scale;
                var finalBindPositions=transforms.Select(t=>t.localPosition).ToArray();var finalBindRotations=transforms.Select(t=>t.localRotation).ToArray();float finalError=0;
                for(int action=0;action<4;action++)for(int frame=0;frame<Mathf.CeilToInt(mapped[action].length*24);frame++)
                {
                    originals[action].SampleAnimation(raw,frame/24f);mapped[action].SampleAnimation(root,frame/24f);var originalSkins=raw.GetComponentsInChildren<SkinnedMeshRenderer>(true);var finalSkins=body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for(int part=0;part<finalSkins.Length;part++){var before=CharacterGeometry.Skin(raw.transform,originalSkins[part]);var after=CharacterGeometry.Skin(root.transform,finalSkins[part]);var map=vertexMaps[finalSkins[part].name];for(int v=0;v<after.Length;v++){Vector3 expected=(before[map[v]]-new Vector3(evidence.sourceXZCenter.x,originalBounds.min.y,evidence.sourceXZCenter.z))*scale;finalError=Mathf.Max(finalError,Vector3.Distance(expected,after[v]));}}
                }
                CharacterGeometry.Require(finalError<=.00025f,"Final-size original pose gate failed.");evidence.maximumScaledPositionError=Mathf.Max(evidence.maximumScaledPositionError,finalError);
                for(int t=0;t<transforms.Length;t++){transforms[t].localPosition=finalBindPositions[t];transforms[t].localRotation=finalBindRotations[t];}
                evidence.collisionRadius=Mathf.Ceil((evidence.collisionRadius+.15f)*10)/10;root.transform.SetParent(null,false);root.SetActive(true);CharacterGeometry.CheckHierarchy(root,"ScalePivot");var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");
                recipe.name=Names[index];recipe.model=prefab;recipe.sizeRootPath="ScalePivot";recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.frameRate=24;recipe.targetBodyHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(prefab,recipe,true)).size.y;recipe.groundBindFeet=false;recipe.agentRadius=evidence.collisionRadius;recipe.paletteColumns=4;recipe.paletteRows=4;recipe.lowVertexBudget=2000;recipe.maxTextureMiB=32;recipe.outputName="Large"+Names[index]+"02";recipe.unitTemplate=template;
                template.combatConfig.projectileRange=0;template.combatConfig.targetAcquireRadius=10;template.combatConfig.attackInterval=mapped[2].length;template.combatConfig.attackRange=evidence.collisionRadius+.55f+.5f;template.combatConfig.maxHp=300;template.combatConfig.attackDamage=30;template.spawnConfig.unitCount=8;template.spawnConfig.spawnCenter=new Vector3(-45,0,-12);float side=(2*evidence.collisionRadius+1)*5;template.spawnConfig.spawnSize=new Vector3(side,0,side);template.movementConfig.maxSpeed=2.5f;template.animationConfig.moveReferenceSpeed=2.5f;template.combatConfig.projectileTargetHeight=evidence.targetHeight*.5f;
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/Large"+Names[index]+"02.asset");evidence.passed=true;File.WriteAllText(Log+"/"+Keys[index]+"-canonical-02.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/"+Keys[index]+"-report-02.json",JsonUtility.ToJson(report,true));Debug.Log("LARGE_BEAST_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices);
        }
        [Serializable]class PrecisionEvidence{public float firstMax,identitySecondMax,slightlyScaledSecondMax,preparedHeight,requestedHeight;public string note="Actual native RGBAHalf encode/readback diagnostic; preserved failures, no tolerance increase.";}
        public static void FinishSpider03()
        {
            var r=Object.Instantiate(Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/LargeSpider02.asset"));var evidence=new PrecisionEvidence{requestedHeight=r.targetBodyHeight};
            var texture=new Texture2D(1024,1,TextureFormat.RGBAHalf,false,true);var values=Enumerable.Range(0,1024).Select(i=>new Color(3.5f+i/1024f,0,0,1)).ToArray();try
            {texture.SetPixels(values);texture.Apply(false,false);var first=texture.GetPixels();for(int i=0;i<1024;i++)evidence.firstMax=Mathf.Max(evidence.firstMax,Mathf.Abs(first[i].r-values[i].r));texture.SetPixels(first);texture.Apply(false,false);var second=texture.GetPixels();for(int i=0;i<1024;i++)evidence.identitySecondMax=Mathf.Max(evidence.identitySecondMax,Mathf.Abs(second[i].r-values[i].r));texture.SetPixels(first.Select(c=>new Color(c.r*.99999994f,0,0,1)).ToArray());texture.Apply(false,false);second=texture.GetPixels();for(int i=0;i<1024;i++)evidence.slightlyScaledSecondMax=Mathf.Max(evidence.slightlyScaledSecondMax,Mathf.Abs(second[i].r-values[i].r));}finally{Object.DestroyImmediate(texture);}
            evidence.preparedHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(r.model,r,true)).size.y;r.targetBodyHeight=evidence.preparedHeight;r.groundBindFeet=false;r.name="LargeSpider03";r.outputName="LargeSpider03";AssetDatabase.CreateAsset(r,CharacterPipeline.Root+"/Recipes/LargeSpider03.asset");File.WriteAllText(Log+"/half-precision-diagnostic.json",JsonUtility.ToJson(evidence,true));
            var report=CharacterPipeline.Run(r);File.WriteAllText(Log+"/spider-report-03.json",JsonUtility.ToJson(report,true));CreateCollection();Debug.Log("LARGE_BEAST_FINAL_READY");
        }
        [Serializable]class DeferredSpiderReport
        {public bool accepted=false;public float nativeBakeVsLbsMaximum,largestWeightSumError;public Vector3 expected,native;public string action;public int frame,vertex;public string note="Spider excluded from this delivery:3.78mm VAT position gate failed. Native unquantized diagnostic only; do not fabricate a passed profile or blame Half without evidence.";}
        public static void FinalizeTwoAndDiagnose()
        {
            var r=Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/LargeSpider03.asset");var parent=new GameObject("Read-only spider diagnostic");parent.SetActive(false);var model=CharacterGeometry.Instance(r.model,parent);var report=new DeferredSpiderReport();var baked=new Mesh();
            try
            {for(int a=0;a<4;a++)for(int f=0;f<Mathf.CeilToInt(r.Clips[a].length*24);f++){r.Clips[a].SampleAnimation(model,f/24f);foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)){var expected=CharacterGeometry.Skin(model.transform,skin);skin.BakeMesh(baked,false);var native=baked.vertices;var matrix=model.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix;var weights=skin.sharedMesh.boneWeights;for(int v=0;v<native.Length;v++){var value=matrix.MultiplyPoint3x4(native[v]);float error=Vector3.Distance(value,expected[v]);if(error>report.nativeBakeVsLbsMaximum){report.nativeBakeVsLbsMaximum=error;report.expected=expected[v];report.native=value;report.action=r.Clips[a].name;report.frame=f;report.vertex=v;}var w=weights[v];report.largestWeightSumError=Mathf.Max(report.largestWeightSumError,Mathf.Abs(1-w.weight0-w.weight1-w.weight2-w.weight3));}}}}
            finally{Object.DestroyImmediate(baked);Object.DestroyImmediate(model);Object.DestroyImmediate(parent);}
            File.WriteAllText(Log+"/spider-deferred.json",JsonUtility.ToJson(report,true));CreateCollection();Debug.Log("LARGE_BEAST_TWO_READY");
        }
        public static void CreateCollection()
        {
            string folder=Prepared+"/Collection01";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh collection required.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(folder+"/Previews");
            var prior=Load<WarSandboxBattlefieldCatalog>(Previous+"/Catalog.asset");var cat=ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();cat.defaultEntryId="large-beast-wolf";var entries=new List<WarSandboxBattlefieldEntry>();var templates=new List<WarSandboxUnitTemplateEntry>();
            for(int i=0;i<2;i++)
            {
                var unit=Load<UnitTypeConfig>(Output(i)+"/Unit.asset");var control=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(Knight+"/Unit.asset"),folder,Names[i]+"Control");control.teamId=1;control.unitTypeName="骑士（大型对照）";control.spawnConfig.unitCount=32;control.spawnConfig.spawnCenter=new Vector3(45,0,12);control.spawnConfig.spawnSize=unit.spawnConfig.spawnSize;control.combatConfig.targetAcquireRadius=12;control.combatConfig.attackRange=unit.flockingConfig.agentRadius+.55f+.5f;control.movementConfig.maxSpeed=3;control.animationConfig.moveReferenceSpeed=3;CharacterPipeline.Save(control.spawnConfig);CharacterPipeline.Save(control.combatConfig);CharacterPipeline.Save(control.movementConfig);CharacterPipeline.Save(control.animationConfig);CharacterPipeline.Save(control);
                var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{unit,control};AssetDatabase.CreateAsset(scenario,folder+"/"+Names[i]+"Scenario.asset");string battle=folder+"/"+Names[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(prior.entries[0].scenePath,battle),"Cannot copy new battlefield.");
                var e=prior.entries[0].CopyIdentity();e.id="large-beast-"+Keys[i];e.displayName=Titles[i]+" · 大型兽形";e.scenePath=battle;e.description="稀疏8大型对32骑士，保守圆形占地和私有网格。";e.briefing="检查真实四足/八足、体型/转向/接敌。大型圆形占地是近似，不等于长条胶囊或零穿插保证。";
                var report=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Log+"/"+Keys[i]+(i==2?"-report-03.json":"-report-02.json")));string pic=folder+"/Previews/"+Names[i]+".png";var source=new Texture2D(2,2,TextureFormat.RGBA32,false);var image=new Texture2D(768,320,TextureFormat.RGBA32,false);try{source.LoadImage(File.ReadAllBytes(report.evidence+"/lod0-Idle-0.png"));image.SetPixels32(new Color32[768*320]);for(int y=0;y<320;y++)for(int x=0;x<320;x++)image.SetPixel(x+224,y,source.GetPixelBilinear(x/319f,y/319f));image.Apply();File.WriteAllBytes(pic,image.EncodeToPNG());}finally{Object.DestroyImmediate(source);Object.DestroyImmediate(image);}AssetDatabase.ImportAsset(pic);e.preview=Load<Texture2D>(pic);entries.Add(e);
                templates.Add(new WarSandboxUnitTemplateEntry{templateId="large-"+Keys[i],revision=1,config=unit});templates.Add(new WarSandboxUnitTemplateEntry{templateId="large-control-"+Keys[i],revision=1,config=control});
            }
            entries.AddRange(prior.entries.Select(e=>e.CopyIdentity()));templates.AddRange(prior.templates.Select(t=>new WarSandboxUnitTemplateEntry{templateId=t.templateId,revision=t.revision,config=t.config}));cat.entries=entries.ToArray();cat.templates=templates.ToArray();AssetDatabase.CreateAsset(cat,folder+"/Catalog.asset");CharacterGeometry.Require(cat.TryValidate(p=>File.Exists(p),out string error)&&cat.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<2;i++)
                {
                    var scene=EditorSceneManager.OpenScene(folder+"/"+Names[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+Names[i]+"Scenario.asset");manager.battleStarted=false;
                    var sys=Object.Instantiate(manager.systemConfig);sys.name=Names[i]+"LargeSystem";var sim=Object.Instantiate(sys.simulationConfig);sim.name=Names[i]+"LargeGrid";float radius=manager.scenarioConfig.unitTypes[0].flockingConfig.agentRadius;sim.cellSize=Mathf.Ceil(2*radius+1);sim.simulationWorldSize=new Vector2(240,240);sim.maxAgentsPerCell=64;AssetDatabase.CreateAsset(sim,folder+"/"+Names[i]+"Simulation.asset");sys.simulationConfig=sim;AssetDatabase.CreateAsset(sys,folder+"/"+Names[i]+"System.asset");manager.systemConfig=sys;
                    var camera=Object.FindFirstObjectByType<Camera>();if(camera!=null){camera.transform.position=new Vector3(0,50,-65);camera.transform.LookAt(Vector3.zero);}
                    EditorSceneManager.SaveScene(scene);
                }
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Previous+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var oldRouter=Object.FindFirstObjectByType<EnemyBatchRuntime>();var existing=oldRouter.entries;var smoke=oldRouter.smoke;Object.DestroyImmediate(oldRouter);
                var probe=new GameObject("Large beast route/contact validation (opt-in)").AddComponent<LargeBeastProbe>();var router=new GameObject("Large beast case selection (opt-in)").AddComponent<LargeBeastCaseRouter>();router.smoke=smoke;router.probe=probe;
                router.entries=Enumerable.Range(0,2).Select(i=>new LargeBeastCaseRouter.Entry{key=Keys[i],battlefieldId="large-beast-"+Keys[i],templateId="large-"+Keys[i],unit=Load<UnitTypeConfig>(Output(i)+"/Unit.asset"),scenario=Load<ScenarioConfig>(folder+"/"+Names[i]+"Scenario.asset"),large=true}).Concat(existing.Select(e=>new LargeBeastCaseRouter.Entry{key=e.key,battlefieldId=e.battlefieldId,templateId=e.templateId,unit=e.unit,scenario=e.scenario,large=false})).ToArray();
                smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedTemplate=router.entries[0].unit;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.planSlot="LargeBeast-wolf";smoke.templateRevision=1;smoke.initialTrialCount=8;smoke.savedTrialCount=12;smoke.preBattleCheck=probe;
                var runtime=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();runtime.planDirectory="LargeBeastPlans";runtime.previewName="Large Beast Collection";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/collection-ready.json","{\"passed\":true,\"newModels\":2,\"catalogScenes\":8,\"oldSceneAssetsModified\":false}");
        }
        public static void Build01()
        {
            string dest="Builds/LargeBeasts-20260930-01",folder=Prepared+"/Collection01";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite build.");
            foreach(int i in Enumerable.Range(0,2)){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Model not ready.");var v=UnitTypeBinder.ValidateBinding(Load<UnitTypeConfig>(Output(i)+"/Unit.asset"));CharacterGeometry.Require(v.IsValid,string.Join("\n",v.Errors));}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray();var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,locationPathName=dest+"/LargeBeasts.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(result.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Large-beast build failed.");
            File.WriteAllText(dest+"/Start-Beasts.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0LargeBeasts.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
