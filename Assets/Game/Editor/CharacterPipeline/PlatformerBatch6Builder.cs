using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MassEngine.Editor;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor
{
    // Available author-official CC0 Platformer Pack sources: Crab, Enemy and Skull.
    // Separate from the quota-blocked Birb/Bunny/Fish proposal; no Google Drive identity is claimed.
    // Reuses the regular canonical adapter and the already-used solid-material palette conversion.
    // Does not switch the official catalog or modify existing units/scenes.
    public static class PlatformerBatch6Builder
    {
        public const string SourceDirectory="Assets/CharacterPilotSource/QuaterniusPlatformerBatch6";
        const string Source=SourceDirectory;
        const string Log="Logs/AgentPlatformer6";
        public const string Prepared="Assets/Game/PlatformerBatch6/Prepared01";
        public const string Integrated=Prepared+"/Integrated";
        const string Knight="Assets/Game/CharacterPipeline/Generated/Knight04";
        public const string RegularScene="Assets/Game/UnifiedRoster/Version03/Scenes/RegularBattlefield.unity";
        public const string RegularPolicyPath=Troops5Builder.Integrated+"/RegularPolicy.asset";
        public const string KnightLegion=Troops5Builder.KnightLegion;
        public const string BaseCatalog=Troops5Builder.Integrated+"/Catalog.asset";
        public static readonly string[] Names={"Crab","Enemy","Skull"};
        static readonly string[] Files={"Crab.fbx","Enemy.fbx","Skull.fbx"};
        public static readonly string[] Keys={"crab","enemy","skull"};
        public static readonly string[] Titles={"蟹怪","绿皮小怪","骷髅头"};
        static readonly string[] Roles={"低矮近战","均衡近战","轻装近战"};
        public static readonly bool[] Ranged={false,false,false};
        static readonly string[] MoveClips={"Run/Walk","Run/Walk","Run/Walk"};
        static readonly string[] AttackClips={"Bite_InPlace/Bite_Front","Bite_InPlace/Bite_Front","Bite_InPlace/Bite_Front"};
        static readonly float[] Heights={.75f,1.0f,1.05f};
        public static readonly float[] Radii={.75f,.75f,.75f};
        public static readonly int[] LowBudgets={1600,1000,1000};
        // Generated templates keep the first-pass values; Integrate01 writes the shipped values onto the library units.
        static readonly int[] FirstHp={90,110,85},FirstDamage={12,12,10};static readonly float[] FirstSpeed={3.5f,4.5f,5f},FirstMoveReference={2.5f,3f,3.5f};
        // Proposed initial values only: no model import, battlefield test or balance result is claimed yet.
        public static readonly int[] Hp={90,110,85},Damage={12,12,10},Counts={100,75,95};
        public static readonly float[] Speed={3.5f,4.5f,5f};
        static readonly float[] MoveReference={2.5f,3f,3.5f};
        public static int KnightCount=>Troops5Builder.KnightCount;
        const string IntegrationRun="01";
        static readonly string[] OutputNames={"PlatformerCrab01","PlatformerEnemy01","PlatformerSkull01"};
        public static string Output(int i)=>CharacterPipeline.Root+"/Generated/"+OutputNames[i];
        public static string LibraryUnit(int i)=>Integrated+"/Library/troops6-"+Keys[i]+".asset";
        public static string Battlefield(int i)=>Integrated+"/"+Keys[i]+"Battlefield.unity";
        public static string EntryId(int i)=>"troops6-"+Keys[i];
        public static string TemplateId(int i)=>"roster-platformer-"+Keys[i];

        public static readonly string[] SourceSha256={"edc99b2dc232e2b96a571b9d182fa8de38e269efa18d551efcfc86f4ff0ae49b", "ac2d8fb64dedde1c21e674096fe905a3ceaa7c39b25cc70326cc79beecad7867", "05007750febc24323f400b7e7cdada6d124b6646831e07884323cc10cd1f6daa"};
        const string ArchiveSha="2d0cac0f3cb58f6845f779a4c6b4a92be6fa27d118ee0b976ead55c6834a53d4";
        const string LicenseSha="de990ef6fc68cffd7fd1ae342c4d0c823b541b8848d8f76bca5d3339f4de6f6e";
        [Serializable]class AcquiredFile { public string name,archiveEntry,sha256; public bool accepted; }
        [Serializable]class Acquisition { public bool published; public string sourceMode,officialPage,archiveSha256; public AcquiredFile[] files; }
        static string Sha(byte[] bytes){using(var hash=SHA256.Create())return string.Concat(hash.ComputeHash(bytes).Select(b=>b.ToString("x2")));}
        public static void ValidateSources()
        {
            for(int i=0;i<Names.Length;i++)CharacterGeometry.Require(File.Exists(SourcePath(i)),"Missing author-official Platformer source: "+SourcePath(i));
            var acquisition=JsonUtility.FromJson<Acquisition>(File.ReadAllText(Source+"/SourceProvenance.json"));
            CharacterGeometry.Require(acquisition!=null&&acquisition.published&&acquisition.sourceMode=="author_itch_free_archive"&&acquisition.officialPage=="https://quaternius.itch.io/ultimate-platformer-pack"&&acquisition.archiveSha256==ArchiveSha&&acquisition.files!=null&&acquisition.files.Length==Names.Length,"Unexpected source provenance.");
            for(int i=0;i<Names.Length;i++)
            {
                var row=acquisition.files.SingleOrDefault(f=>f.name==Files[i]);
                CharacterGeometry.Require(row!=null&&row.accepted&&row.archiveEntry=="Ultimate Platformer Pack - Dec 2021/Enemies/FBX/"+Files[i]&&row.sha256==SourceSha256[i],"Unexpected archive entry: "+Files[i]);
                var bytes=File.ReadAllBytes(SourcePath(i));
                CharacterGeometry.Require(CharacterPipeline.IsFbxPayload(bytes)&&Sha(bytes)==SourceSha256[i],"Pinned source payload mismatch: "+Files[i]);
            }
            var license=File.ReadAllBytes(Source+"/License.txt");
            CharacterGeometry.Require(Sha(license)==LicenseSha&&Encoding.UTF8.GetString(license).Contains("CC0 1.0 Universal"),"Pinned original pack license required.");
        }
        static string SourcePath(int i)=>Source+"/"+Files[i];
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static AnimationClip ClipAny(string path,string names){foreach(var n in names.Split('/')){var c=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview__")).FirstOrDefault(x=>x.name==n||x.name.EndsWith("|"+n,StringComparison.Ordinal));if(c!=null)return c;}throw new InvalidOperationException("No clip "+names+" in "+path+": "+string.Join(",",AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Select(x=>x.name)));}
        static bool ImportMatches(ModelImporter im)=>im.animationType==ModelImporterAnimationType.Generic&&im.isReadable&&im.materialImportMode==ModelImporterMaterialImportMode.ImportStandard&&im.importBlendShapes;
        // Only the batch-6 sources (new in this batch) may have their import settings set; nothing else is touched.
        static void ConfigureImport(string path){CharacterGeometry.Require(path.StartsWith(Source+"/"),"Only owned platformer sources may be configured: "+path);var im=(ModelImporter)AssetImporter.GetAtPath(path);CharacterGeometry.Require(im!=null,"Not imported: "+path);if(ImportMatches(im))return;im.animationType=ModelImporterAnimationType.Generic;im.isReadable=true;im.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;im.importBlendShapes=true;im.SaveAndReimport();}

        // Import settings + clip names + rest bounds of the batch-6 sources, to confirm the clip choices before preparing.
        public static void Inspect01()
        {
            ValidateSources();
            Directory.CreateDirectory(Log);var sb=new StringBuilder();
            for(int i=0;i<Names.Length;i++)
            {
                string path=SourcePath(i);
                ConfigureImport(path);var im=(ModelImporter)AssetImporter.GetAtPath(path);var model=Load<GameObject>(path);
                ClipAny(path,"Idle");ClipAny(path,MoveClips[i]);ClipAny(path,AttackClips[i]);ClipAny(path,"Death");
                var recipeRaw=ScriptableObject.CreateInstance<CharacterRecipe>();var b=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(model,recipeRaw,true));Object.DestroyImmediate(recipeRaw);
                var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Select(c=>c.name+" "+c.length.ToString("F3")+"s");
                var meshes=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s=>s.name+" v="+s.sharedMesh.vertexCount+" bones="+s.bones.Length+" shapes="+s.sharedMesh.blendShapeCount);
                sb.AppendLine(Names[i]+": importMatches="+ImportMatches(im)+" rigid="+model.GetComponentsInChildren<MeshRenderer>(true).Length+" bounds="+b.size.ToString("F3")+" center="+b.center.ToString("F3")+" | meshes: "+string.Join("; ",meshes)+" | clips: "+string.Join(", ",clips));
            }
            File.WriteAllText(Log+"/troops6-inspect-01.txt",sb.ToString());Debug.Log("PLATFORMER6_INSPECT_READY\n"+sb);
        }

        [Serializable]class PreparationEvidence
        {
            public string model,method="Prepared only (MonsterBatchBuilder path): static import scales removed while preserving world rest transforms; owned mesh vertices/normals/bindposes; authored local poses resampled from world transforms and compared at every baked frame; the pipeline applies targetBodyHeight at ScalePivot. Source material colours are mapped to an owned linear 4x4 atlas, with original-to-palette vertex remaps preserved for independent pose comparisons.";
            public int frames,positions;public float maximumScaledPositionError,maximumScaleDeviationFromRest,sourceHeight,targetHeight,scale,idleBodyRadius,agentRadius,attackRange,attackInterval;public bool passed;public string[] clips;
        }
        static void PrepareOne(int index)
        {
            string folder=Prepared+"/"+Names[index];CharacterPipeline.EnsureFolder(folder);string modelPath=SourcePath(index);ConfigureImport(modelPath);
            var source=Load<GameObject>(modelPath);var prior=Load<UnitTypeConfig>(Knight+"/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,folder,"Template");template.teamId=0;template.unitTypeName=Titles[index];
            var palette=source.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Select(m=>m.color).Distinct().ToList();
            CharacterGeometry.Require(palette.Count>0&&palette.Count<=16,"Bounded source material palette required.");
            var atlas=new Texture2D(64,64,TextureFormat.RGBA32,false,true){name=Names[index]+"Palette",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[64*64];for(int y=0;y<64;y++)for(int x=0;x<64;x++){int tile=(y/16)*4+x/16;pixels[y*64+x]=tile<palette.Count?palette[tile]:Color.white;}atlas.SetPixels(pixels);atlas.Apply(false,false);AssetDatabase.CreateAsset(atlas,folder+"/Palette.asset");
            Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",atlas);m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;}
            template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,Names[index]+"Near");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,Names[index]+"Mid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
            var stage=new GameObject("Inactive troops6 source preparation");stage.SetActive(false);
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
                        {var expected=CharacterGeometry.Skin(raw.transform,sourceSkins[part]);var actual=CharacterGeometry.Skin(root.transform,resultSkins[part]);var remap=vertexMaps[resultSkins[part].name];CharacterGeometry.Require(remap.Length==actual.Length,"Palette vertex remap count.");for(int v=0;v<actual.Length;v++){evidence.maximumScaledPositionError=Mathf.Max(evidence.maximumScaledPositionError,Vector3.Distance(expected[remap[v]],actual[v])*scale);evidence.positions++;}}
                        evidence.frames++;
                    }
                }
                CharacterGeometry.Require(evidence.maximumScaledPositionError<=.00025f,"Original/canonical pose mismatch exceeds 0.25mm: "+evidence.maximumScaledPositionError);
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=bindPositions[i];transforms[i].localRotation=bindRotations[i];transforms[i].localScale=Vector3.one;}
                root.transform.SetParent(null,false);root.SetActive(true);CharacterGeometry.CheckHierarchy(root,"ScalePivot");var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prepared.prefab");
                recipe.name=Names[index];recipe.model=prefab;recipe.sizeRootPath="ScalePivot";recipe.idle=mapped[0];recipe.move=mapped[1];recipe.attack=mapped[2];recipe.death=mapped[3];recipe.frameRate=24;recipe.targetBodyHeight=evidence.targetHeight;recipe.agentRadius=Radii[index];recipe.paletteColumns=4;recipe.paletteRows=4;recipe.lowVertexBudget=LowBudgets[index];recipe.maxTextureMiB=32;recipe.outputName=OutputNames[index];recipe.unitTemplate=template;
                // Regular melee: reach = own radius + knight radius (0.55) + the orc's 0.75 slack (orc: 0.5 + 0.55 + 0.75 = 1.8).
                evidence.attackRange=Radii[index]+.55f+.75f;evidence.attackInterval=mapped[2].length;
                var c0=template.combatConfig;c0.projectileRange=0;c0.projectileSplashRadius=0;c0.targetAcquireRadius=12;c0.attackInterval=evidence.attackInterval;c0.attackRange=evidence.attackRange;c0.maxHp=FirstHp[index];c0.attackDamage=FirstDamage[index];c0.projectileTargetHeight=evidence.targetHeight*.5f;
                template.movementConfig.maxSpeed=FirstSpeed[index];template.animationConfig.moveReferenceSpeed=FirstMoveReference[index];
                var sp=template.spawnConfig;sp.unitCount=Counts[index];sp.spawnCenter=new Vector3(-24,0,0);sp.formationDensity=.12f;sp.formationAspect=1.5f;sp.spawnSize=Vector3.zero;
                CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);
                AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+OutputNames[index]+".asset");evidence.passed=true;File.WriteAllText(Log+"/troops6-"+Keys[index]+"-canonical-01.json",JsonUtility.ToJson(evidence,true));
            }
            finally{Object.DestroyImmediate(raw);Object.DestroyImmediate(root);Object.DestroyImmediate(stage);}
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/troops6-"+Keys[index]+"-report-01.json",JsonUtility.ToJson(report,true));Debug.Log("PLATFORMER6_READY "+Keys[index]+" "+report.fullVertices+"/"+report.lowVertices+" passed="+report.automatedPassed);
            CharacterGeometry.Require(report.automatedPassed,"Character pipeline gate failed: "+Keys[index]);
        }
        // Resume this owned preparation after the small Enemy mesh rejected the overly large Low budget.
        // The accepted Crab output stays untouched; no tolerance or core pipeline change is involved.
        public static void CompleteRemaining01()
        {
            ValidateSources();
            var crab=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(0)+"/PipelineReport.json"));
            CharacterGeometry.Require(crab.automatedPassed,"Accepted Crab output must remain available.");
            CharacterGeometry.Require(!Directory.Exists(Output(1)),"Never overwrite Enemy output.");
            var recipe=Load<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/"+OutputNames[1]+".asset");
            CharacterGeometry.Require(recipe.outputName==OutputNames[1]&&AssetDatabase.GetAssetPath(recipe.model).StartsWith(Prepared+"/Enemy/"),"Only this batch's incomplete Enemy recipe may be resumed.");
            recipe.lowVertexBudget=LowBudgets[1];CharacterPipeline.Save(recipe);
            var report=CharacterPipeline.Run(recipe);File.WriteAllText(Log+"/troops6-enemy-report-02.json",JsonUtility.ToJson(report,true));
            CharacterGeometry.Require(report.automatedPassed,"Enemy pipeline gate failed.");
            CharacterGeometry.Require(!Directory.Exists(Output(2))&&!File.Exists(CharacterPipeline.Root+"/Recipes/"+OutputNames[2]+".asset")&&!Directory.Exists(Prepared+"/Skull"),"Fresh Skull preparation required.");
            PrepareOne(2);
            for(int i=0;i<Names.Length;i++){var accepted=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(accepted.automatedPassed,"Model gate failed: "+Names[i]);}
            Debug.Log("PLATFORMER6_PREPARATION_READY "+string.Join(",",OutputNames));
        }

        public static void Prepare01()
        {
            ValidateSources();
            for(int i=0;i<Names.Length;i++){CharacterGeometry.Require(!Directory.Exists(Output(i))&&!File.Exists(CharacterPipeline.Root+"/Recipes/"+OutputNames[i]+".asset"),"Never overwrite "+OutputNames[i]);CharacterGeometry.Require(File.Exists(SourcePath(i)),"Missing source "+SourcePath(i));CharacterGeometry.Require(!Directory.Exists(Prepared+"/"+Names[i]),"Fresh preparation required: "+Names[i]);}
            Directory.CreateDirectory(Log);CharacterPipeline.EnsureFolder(Prepared);
            for(int i=0;i<Names.Length;i++)PrepareOne(i);
            for(int i=0;i<Names.Length;i++){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Model not accepted: "+OutputNames[i]);}
            Debug.Log("PLATFORMER6_PREPARATION_READY "+string.Join(",",OutputNames));
        }

        // Three new regular battlefields, inheriting the batch-5 knight legion, 19-template policy and catalog.
        // Existing templates, scenes and source materials remain read-only. Only batch-6 owned paths are authored.
        // Never overwrite prepared or integrated outputs. A revised attempt must use fresh paths.
        public static void Integrate01()
        {
            string folder=Integrated,lib=folder+"/Library";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated directory required.");
            for(int i=0;i<Names.Length;i++){var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Output(i)+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Unaccepted model: "+OutputNames[i]);}
            CharacterPipeline.EnsureFolder(folder);CharacterPipeline.EnsureFolder(lib);
            var oldPolicy=Load<WarSandboxRosterPolicy>(RegularPolicyPath);var units=new List<UnitTypeConfig>();
            for(int i=0;i<Names.Length;i++)
            {
                var u=CharacterPipeline.CloneUnit(Load<UnitTypeConfig>(Output(i)+"/Unit.asset"),lib,"troops6-"+Keys[i]);u.unitTypeName=Titles[i];u.teamId=0;
                u.spawnConfig.unitCount=Counts[i];u.spawnConfig.spawnCenter=new Vector3(-24,0,0);
                CharacterGeometry.Require(Mathf.Approximately(u.flockingConfig.agentRadius,Radii[i])&&u.flockingConfig.agentRadius<=oldPolicy.maximumRadius,"Radius drift: "+Keys[i]);
                CharacterGeometry.Require(u.combatConfig.maxHp==FirstHp[i]&&(u.combatConfig.projectileRange>0)==Ranged[i]&&!ReferenceEquals(u.combatConfig,Load<UnitTypeConfig>(Output(i)+"/Unit.asset").combatConfig),"Library unit must own its configs: "+Keys[i]);
                // Each unit keeps its own speed (no blanket maxSpeed=3 as in GiantBuilder).
                u.combatConfig.maxHp=Hp[i];u.combatConfig.attackDamage=Damage[i];u.movementConfig.maxSpeed=Speed[i];u.animationConfig.moveReferenceSpeed=MoveReference[i];
                CharacterPipeline.Save(u.spawnConfig);CharacterPipeline.Save(u.combatConfig);CharacterPipeline.Save(u.movementConfig);CharacterPipeline.Save(u.animationConfig);CharacterPipeline.Save(u);
                units.Add(u);
            }
            var legion=Load<UnitTypeConfig>(KnightLegion);CharacterGeometry.Require(oldPolicy.templates.Contains(legion),"The inherited knight legion must already be a policy template.");
            var policy=Object.Instantiate(oldPolicy);policy.name="Troops6RegularPolicy";policy.templates=oldPolicy.templates.Concat(units).ToArray();
            policy.explanation=oldPolicy.explanation+" 新增蟹怪/绿皮小怪/骷髅头（作者官方CC0包，使用已有近战逻辑）。";string policyPath=folder+"/RegularPolicy.asset";AssetDatabase.CreateAsset(policy,policyPath);
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
            var oldCat=Load<WarSandboxBattlefieldCatalog>(BaseCatalog);var catalog=Object.Instantiate(oldCat);catalog.name="UnifiedTroops6";
            var source=oldCat.entries.Single(e=>e.id=="unified-regular");var newEntries=new List<WarSandboxBattlefieldEntry>();var templates=oldCat.templates.ToList();
            for(int i=0;i<Names.Length;i++)
            {
                var u=Load<UnitTypeConfig>(LibraryUnit(i));var c=u.combatConfig;
                var e=source.CopyIdentity();e.id=EntryId(i);e.displayName=Titles[i]+" · 第六批";e.scenePath=Battlefield(i);e.preview=null;
                e.description="官方CC0 Quaternius Ultimate Platformer Pack "+Files[i].Replace(".fbx","")+"，常规体型兵团（身高约 "+Heights[i].ToString("0.0#")+"m）。"+Counts[i]+" 名"+Titles[i]+"对 "+KnightCount+" 名剑盾骑士。";
                e.briefing=Roles[i]+(Ranged[i]?"（射程 "+c.projectileRange.ToString("0")+"m，":"（")+"HP "+c.maxHp+" / 攻击 "+c.attackDamage+" / 移速 "+u.movementConfig.maxSpeed.ToString("0.#")+"m/s），单体攻击、无范围伤害；首版数值，未做平衡。";
                newEntries.Add(e);templates.Add(new WarSandboxUnitTemplateEntry{templateId=TemplateId(i),revision=1,config=u});
            }
            catalog.entries=oldCat.entries.Select(e=>e.CopyIdentity()).Concat(newEntries).ToArray();catalog.templates=templates.ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");
            CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);AssetDatabase.SaveAssets();
            var k=Load<UnitTypeConfig>(KnightLegion);
            File.WriteAllText(Log+"/troops6-integration-"+IntegrationRun+".json","{\"passed\":true,\"newModels\":"+Names.Length+",\"entries\":"+catalog.entries.Length+",\"policyTemplates\":"+policy.templates.Length+",\"knight\":{\"hp\":"+k.combatConfig.maxHp+",\"damage\":"+k.combatConfig.attackDamage+",\"speed\":"+k.movementConfig.maxSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"radius\":"+k.flockingConfig.agentRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)+"},\"oldSourceEdited\":false}");
            Debug.Log("PLATFORMER6_INTEGRATION_READY "+catalog.entries.Length);
        }
    }
}

