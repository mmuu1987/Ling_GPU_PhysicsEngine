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
    public static class NonhumanBatch2Builder
    {
        const string Source="Assets/CharacterPilotSource/NonhumanBatch2";
        const string Log="Logs/AgentNonhumanBatch2";
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
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());ReviewSpider();Debug.Log("NONHUMAN_IMPORT_READY");
        }
        [Serializable]class SpiderReview
        {public string method="One bounded read-only full bake plus independent expected positions; no output asset/old recipe edits.";public float maxError,maxAbsCoordinate,maxMathHalfError;public string action;public int frame,vertex;public Vector3 expected,encoded,mathHalf;public bool encodedMatchesMathHalf;}
        static void ReviewSpider()
        {
            var r=AssetDatabase.LoadAssetAtPath<CharacterRecipe>("Assets/Game/CharacterPipeline/Recipes/LargeSpider03.asset");var result=new SpiderReview();var parent=new GameObject("Read-only spider review");parent.SetActive(false);
            try
            {
                using(var baked=VatBaker.Bake(new VatBakeRequest{model=r.model,idle=r.idle,move=r.move,attack=r.attack,death=r.death,frameRate=r.frameRate,bakeLowLod=false}))
                {
                    var p=baked.Profile;var data=p.positionTexture.GetPixels();var windows=new[]{p.idle,p.move,p.attack,p.death};
                    for(int a=0;a<4;a++)
                    {
                        var go=CharacterGeometry.Instance(r.model,parent);try{for(int frame=0;frame<windows[a].frameCount;frame++)
                        {
                            float time=a==3&&windows[a].frameCount>1?r.Clips[a].length*frame/(windows[a].frameCount-1):Mathf.Min(frame/(float)r.frameRate,r.Clips[a].length);r.Clips[a].SampleAnimation(go,time);var expected=CharacterGeometry.Positions(go,r);
                            for(int v=0;v<expected.Length;v++)
                            {
                                var e=expected[v];result.maxAbsCoordinate=Mathf.Max(result.maxAbsCoordinate,Mathf.Max(Mathf.Abs(e.x),Mathf.Max(Mathf.Abs(e.y),Mathf.Abs(e.z))));var half=new Vector3(Mathf.HalfToFloat(Mathf.FloatToHalf(e.x)),Mathf.HalfToFloat(Mathf.FloatToHalf(e.y)),Mathf.HalfToFloat(Mathf.FloatToHalf(e.z)));result.maxMathHalfError=Mathf.Max(result.maxMathHalfError,Vector3.Distance(e,half));
                                int i=(windows[a].startFrame+frame)*p.rowsPerFrame*p.textureWidth+v;var c=data[i];var q=new Vector3(c.r,c.g,c.b);float error=Vector3.Distance(e,q);if(error>result.maxError){result.maxError=error;result.expected=e;result.encoded=q;result.mathHalf=half;result.action=windows[a].label;result.frame=frame;result.vertex=v;result.encodedMatchesMathHalf=(q-half).sqrMagnitude<1e-12f;}
                            }
                        }}finally{Object.DestroyImmediate(go);}
                    }
                }
            }finally{Object.DestroyImmediate(parent);}
            File.WriteAllText(Log+"/spider-review.json",JsonUtility.ToJson(result,true));
        }
        const string Prepared="Assets/Game/NonhumanBatch2/Prepared01";
        const string Knight="Assets/Game/CharacterPipeline/Generated/Knight04";
        const string Previous="Assets/Game/UnifiedRoster/Version03";
        static readonly string[] Keys={"triceratops","stegosaurus"};
        static readonly string[] Names={"Triceratops","Stegosaurus"};
        static readonly string[] Titles={"三角龙","剑龙"};
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+Names[i]+"01";
        static AnimationClip Clip(string path,string name)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Single(c=>c.name.EndsWith("|"+name,StringComparison.Ordinal));
        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only: remove static import scales by preserving world rest transforms, transforming owned mesh vertices/normals and bindposes, resampling authored local poses from world transforms. Original imported LBS positions compared at every baked frame. No scale channel is silently treated as real unit animation.";
            public int frames,positions,paletteColors;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,targetHeight,targetSpan,sourceSpan,collisionRadius,minimumActiveY,maximumActiveY;public Vector3 sourceXZCenter;public bool passed;
        }
        public static void PrepareAll01()
        {
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh batch2 preparation required.");CharacterPipeline.EnsureFolder(Prepared);
            for(int i=0;i<2;i++)PrepareOne(i);
            PrepareSpider04();CreateIntegratedCollection();Debug.Log("NONHUMAN_PREPARATION_READY");
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
            var evidence=new PreparationEvidence{model=Names[index],targetSpan=6f,paletteColors=palette.Count};
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
                string[] actions={"Idle","Move","Attack","Death"};var originals=new[]{Clip(modelPath,Names[index]+"_Idle"),Clip(modelPath,Names[index]+"_Run"),Clip(modelPath,Names[index]+"_Attack"),Clip(modelPath,Names[index]+"_Death")};
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
                recipe.name=Names[index];recipe.model=prefab;recipe.sizeRootPath="";recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.frameRate=24;recipe.targetBodyHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(prefab,recipe,true)).size.y;recipe.groundBindFeet=false;recipe.agentRadius=evidence.collisionRadius;recipe.paletteColumns=4;recipe.paletteRows=4;recipe.lowVertexBudget=2000;recipe.maxTextureMiB=32;recipe.outputName=Names[index]+"01";recipe.unitTemplate=template;
                template.combatConfig.projectileRange=0;template.combatConfig.targetAcquireRadius=10;template.combatConfig.attackInterval=mapped[2].length;template.combatConfig.attackRange=evidence.collisionRadius+.55f+.5f;template.combatConfig.maxHp=300;template.combatConfig.attackDamage=30;template.spawnConfig.unitCount=8;template.spawnConfig.spawnCenter=new Vector3(-45,0,-12);float side=(2*evidence.collisionRadius+1)*5;template.spawnConfig.spawnSize=new Vector3(side,0,side);template.movementConfig.maxSpeed=2.5f;template.animationConfig.moveReferenceSpeed=2.5f;template.combatConfig.projectileTargetHeight=evidence.targetHeight*.5f;
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+Names[index]+".asset");evidence.passed=true;File.WriteAllText(Log+"/"+Keys[index]+"-canonical.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/"+Keys[index]+"-report-01.json",JsonUtility.ToJson(report,true));Debug.Log("NONHUMAN_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices);
        }
        static void PrepareSpider04()
        {
            var r=Object.Instantiate(Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/LargeSpider03.asset"));r.name="LargeSpider04";r.outputName="LargeSpider04";r.sizeRootPath="";r.targetBodyHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(r.model,r,true)).size.y;r.groundBindFeet=false;r.createTrial=false;
            AssetDatabase.CreateAsset(r,CharacterPipeline.Root+"/Recipes/LargeSpider04.asset");var report=CharacterPipeline.Run(r);File.WriteAllText(Log+"/spider-report-04.json",JsonUtility.ToJson(report,true));
        }
        static readonly string LibrarySource="Assets/Game/UnifiedRoster/Version03";
        public static void CreateIntegratedCollection()
        {
            string folder=Prepared+"/Integrated",lib=folder+"/Library";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated directory required.");CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(lib);
            var oldCat=Load<WarSandboxBattlefieldCatalog>(LibrarySource+"/Catalog.asset");var oldPolicy=Load<WarSandboxRosterPolicy>(LibrarySource+"/LargePolicy.asset");var catalog=Object.Instantiate(oldCat);catalog.name="UnifiedNonhumanBatch2";catalog.defaultEntryId="nonhuman2-triceratops";
            var models=new[]{Load<UnitTypeConfig>(Output(0)+"/Unit.asset"),Load<UnitTypeConfig>(Output(1)+"/Unit.asset"),Load<UnitTypeConfig>(CharacterPipeline.Root+"/Generated/LargeSpider04/Unit.asset")};string[] keys={"triceratops","stegosaurus","spider"},titles={"三角龙","剑龙","巨型蜘蛛"};
            var newUnits=new UnitTypeConfig[3];var newEntries=new List<WarSandboxBattlefieldEntry>();var extraTemplates=new List<WarSandboxUnitTemplateEntry>();float maxRadius=Mathf.Max(4,models.Max(u=>u.flockingConfig.agentRadius));
            var control=CharacterPipeline.CloneUnit(oldPolicy.templates.Single(u=>u.flockingConfig.agentRadius<1),lib,"SizeAdaptedKnight");control.spawnConfig.unitCount=16;control.spawnConfig.spawnCenter=new Vector3(45,0,0);control.combatConfig.attackRange=maxRadius+.55f+.5f;CharacterPipeline.Save(control.spawnConfig);CharacterPipeline.Save(control.combatConfig);CharacterPipeline.Save(control);
            extraTemplates.Add(new WarSandboxUnitTemplateEntry{templateId="nonhuman2-knight-control",revision=1,config=control});
            for(int i=0;i<3;i++)
            {
                var u=CharacterPipeline.CloneUnit(models[i],lib,keys[i]);u.unitTypeName=titles[i];u.teamId=0;u.spawnConfig.unitCount=2;u.spawnConfig.spawnCenter=new Vector3(-45,0,0);CharacterPipeline.Save(u.spawnConfig);CharacterPipeline.Save(u);newUnits[i]=u;extraTemplates.Add(new WarSandboxUnitTemplateEntry{templateId="roster-"+keys[i],revision=1,config=u});
            }
            var policy=Object.Instantiate(oldPolicy);policy.name="ExpandedLargeRoster";policy.templates=oldPolicy.templates.Where(u=>u.flockingConfig.agentRadius>1).Concat(newUnits).Concat(new[]{control}).ToArray();policy.maximumRadius=maxRadius+.1f;policy.explanation="大型非人形库 · 大兽限攻方，守方使用适配骑士。切换采用安全默认人数/阵型，保留军团和位置；检查脚印后再应用。";AssetDatabase.CreateAsset(policy,folder+"/LargePolicy.asset");
            for(int i=0;i<3;i++)
            {
                var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{newUnits[i],control};AssetDatabase.CreateAsset(scenario,folder+"/"+keys[i]+"Scenario.asset");string battle=folder+"/"+keys[i]+"Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(LibrarySource+"/Scenes/LargeBattlefield.unity",battle),"Cannot copy new private scene.");
                var e=oldCat.entries.Single(e=>e.id=="unified-large").CopyIdentity();e.id="nonhuman2-"+keys[i];e.displayName=titles[i]+" · 非人形第二批";e.description="直接加入统一大型角色菜单，可选既有巨狼/公牛和本批新模型。";e.briefing="大型仍为保守圆与中心距离近战，非新技能/真实甲壳碰撞。";e.scenePath=battle;e.preview=null;newEntries.Add(e);
            }
            catalog.entries=newEntries.Concat(oldCat.entries.Select(e=>e.CopyIdentity())).ToArray();catalog.templates=oldCat.templates.Concat(extraTemplates).ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                for(int i=0;i<3;i++)
                {
                    var scene=EditorSceneManager.OpenScene(folder+"/"+keys[i]+"Battlefield.unity",OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/"+keys[i]+"Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(folder+"/LargePolicy.asset");
                    var system=Object.Instantiate(manager.systemConfig);var sim=Object.Instantiate(system.simulationConfig);sim.cellSize=Mathf.Ceil(maxRadius*2+1);sim.simulationWorldSize=new Vector2(240,240);sim.maxAgentsPerCell=64;AssetDatabase.CreateAsset(sim,folder+"/"+keys[i]+"Simulation.asset");system.simulationConfig=sim;AssetDatabase.CreateAsset(system,folder+"/"+keys[i]+"System.asset");manager.systemConfig=system;EditorSceneManager.SaveScene(scene);
                }
                CharacterGeometry.Require(AssetDatabase.CopyAsset(LibrarySource+"/Scenes/Menu.unity",folder+"/Menu.unity"),"Cannot copy integrated menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var prior=Object.FindFirstObjectByType<UnifiedRosterCaseRouter>();var smoke=prior.smoke;var probe=prior.probe;var oldEntries=prior.entries;Object.DestroyImmediate(prior);var router=new GameObject("Nonhuman integrated test selector").AddComponent<NonhumanBatchCaseRouter>();router.smoke=smoke;router.probe=probe;
                router.entries=Enumerable.Range(0,3).Select(i=>new NonhumanBatchCaseRouter.Entry{key=keys[i],battlefieldId="nonhuman2-"+keys[i],templateId="roster-"+keys[i],unit=Load<UnitTypeConfig>(lib+"/"+keys[i]+".asset"),scenario=Load<ScenarioConfig>(folder+"/"+keys[i]+"Scenario.asset"),initial=2,saved=3,templates=6,large=true}).Concat(oldEntries.Select(e=>new NonhumanBatchCaseRouter.Entry{key=e.key,battlefieldId=e.battlefieldId,templateId=e.templateId,unit=e.unit,scenario=e.scenario,initial=e.initial,saved=e.saved,templates=e.expectedTemplates,large=e.key=="large"})).ToArray();
                smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.expectedTemplate=router.entries[0].unit;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.planSlot="Nonhuman2-triceratops";smoke.initialTrialCount=2;smoke.savedTrialCount=3;smoke.preBattleCheck=probe;
                var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="UnifiedRosterPlans";preview.previewName="Unified Roster + Nonhuman Batch2";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/integration-ready.json","{\"passed\":true,\"newModels\":3,\"normalModelsUnchanged\":12,\"largeModels\":5,\"totalUniqueBodies\":17,\"largeMenuVariants\":6,\"oldSourceEdited\":false}");
        }
        public static void Build01()
        {
            string dest="Builds/UnifiedNonhuman2-20260930-01",folder=Prepared+"/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite player.");foreach(string p in new[]{Output(0),Output(1),CharacterPipeline.Root+"/Generated/LargeSpider04"}){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(p+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model must not be built.");}
            var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Nonhuman integration build failed.");File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
