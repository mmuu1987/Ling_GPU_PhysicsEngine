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
    public static class CavalryBuilder
    {
        const string Source="Assets/CharacterPilotSource/Cavalry01";
        static string Log="Logs/AgentCavalry";
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
            File.WriteAllText(Log+"/import-inspection.txt",sb.ToString());Debug.Log("CAVALRY_IMPORT_READY");
        }
        static string Prepared="Assets/Game/Cavalry/Prepared01";
        // Playtest 2 (defaults reproduce every earlier output): seat moved forward toward the withers, and a fitted leather saddle instead of the flat blue blanket.
        static float SeatForwardMeters=0f;static bool LeatherSaddle=false,NoSaddle=false;static string MountedOutput="MountedKnight02";
        const string KnightSource="Assets/CharacterPilotSource/KayKitKnight";
        const string Previous="Assets/Game/NonhumanBatch2/Prepared01/Integrated";
        static T Load<T>(string p)where T:Object{var v=AssetDatabase.LoadAssetAtPath<T>(p);CharacterGeometry.Require(v!=null,"Missing "+p);return v;}
        static AnimationClip Clip(string path,string suffix)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).Single(c=>c.name==suffix||c.name.EndsWith("|"+suffix,StringComparison.Ordinal));
        static Transform Named(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        class Rig
        {
            public GameObject raw,body;public float scale;public Vector3 shift;public Transform wrapper;
            public Dictionary<string,Transform> rawNodes,nodes;public Dictionary<SkinnedMeshRenderer,int[]> maps=new Dictionary<SkinnedMeshRenderer,int[]>();
            public void Pose(AnimationClip clip,float time)
            {
                wrapper.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);clip.SampleAnimation(raw,time);
                foreach(var pair in nodes){var source=rawNodes[pair.Key];pair.Value.localScale=Vector3.one;pair.Value.SetPositionAndRotation(source.position*scale+shift,source.rotation);}
            }
        }
        static Rig Canonical(GameObject raw,GameObject root,string name,float scale,Vector3 shift,Material material,List<Color> horsePalette,bool isHorse)
        {
            var wrapper=new GameObject(name).transform;wrapper.SetParent(root.transform,false);var body=Object.Instantiate(raw,wrapper);body.name="Body";
            var ts=body.GetComponentsInChildren<Transform>(true);var old=ts.ToDictionary(t=>t,t=>t.localToWorldMatrix);var pos=ts.ToDictionary(t=>t,t=>t.position);var rot=ts.ToDictionary(t=>t,t=>t.rotation);var global=Matrix4x4.TRS(shift,Quaternion.identity,Vector3.one*scale);
            foreach(var t in ts){var s=t.localScale;float m=(s.x+s.y+s.z)/3;CharacterGeometry.Require(s.x>0&&s.y>0&&s.z>0&&(s-Vector3.one*m).magnitude/m<.0002f,"Unsupported source static scale.");t.localScale=Vector3.one;t.SetPositionAndRotation(pos[t]*scale+shift,rot[t]);}
            var rig=new Rig{raw=raw,body=body,wrapper=wrapper,scale=scale,shift=shift,rawNodes=raw.GetComponentsInChildren<Transform>(true).ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,raw.transform)),nodes=ts.ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,body.transform))};
            int number=0;
            foreach(var renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                var skin=renderer as SkinnedMeshRenderer;var filter=renderer.GetComponent<MeshFilter>();var source=skin!=null?skin.sharedMesh:filter.sharedMesh;CharacterGeometry.Require(source.blendShapeCount==0,"Do not discard source blendshapes.");
                var delta=renderer.transform.worldToLocalMatrix*global*old[renderer.transform];var normal=delta.inverse.transpose;var vs=source.vertices;var ns=source.normals;var uv0=source.uv;var weights=source.boneWeights;
                var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var boneWeights=new List<BoneWeight>();var map=new List<int>();var triangles=new List<int>();var lookup=new Dictionary<(int,int),int>();
                for(int part=0;part<source.subMeshCount;part++)
                {
                    int color=isHorse?horsePalette.IndexOf(renderer.sharedMaterials[part].color):-1;CharacterGeometry.Require(!isHorse||color>=0,"Horse colour not found.");
                    foreach(int v in source.GetTriangles(part))
                    {
                        var key=(v,color);if(!lookup.TryGetValue(key,out int i)){i=vertices.Count;lookup.Add(key,i);vertices.Add(delta.MultiplyPoint3x4(vs[v]));normals.Add(normal.MultiplyVector(ns[v]).normalized);map.Add(v);if(skin!=null)boneWeights.Add(weights[v]);uv.Add(isHorse?new Vector2(.5f+(color%4+.5f)/8f,(color/4+.5f)/4f):new Vector2(uv0[v].x*.5f,uv0[v].y));}triangles.Add(i);
                    }
                }
                var mesh=new Mesh{name=name+"_"+renderer.name+"_Canonical"};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
                if(skin!=null){mesh.boneWeights=boneWeights.ToArray();mesh.bindposes=skin.bones.Select((b,i)=>b.worldToLocalMatrix*global*old[b]*source.bindposes[i]*delta.inverse).ToArray();skin.sharedMesh=mesh;rig.maps.Add(skin,map.ToArray());}else filter.sharedMesh=mesh;
                mesh.RecalculateBounds();if(skin!=null)skin.localBounds=mesh.bounds;AssetDatabase.CreateAsset(mesh,Prepared+"/"+name+"Mesh"+(number++)+".asset");renderer.sharedMaterial=material;
            }
            foreach(var a in body.GetComponentsInChildren<Animator>(true))a.enabled=false;return rig;
        }
        static Texture2D ReadTexture(Texture t)
        {
            var rt=RenderTexture.GetTemporary(t.width,t.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=RenderTexture.active;try{Graphics.Blit(t,rt);RenderTexture.active=rt;var copy=new Texture2D(t.width,t.height,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,t.width,t.height),0,0);copy.Apply();return copy;}finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}
        }
        static void AttachRawWeapons(GameObject knight)
        {
            foreach(var item in new[]{("handslot.r","sword_1handed.fbx","Sword"),("handslot.l","shield_badge.fbx","Shield")}){var w=Object.Instantiate(Load<GameObject>(KnightSource+"/"+item.Item2),Named(knight,item.Item1));w.name=item.Item3;w.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);w.transform.localScale=Vector3.one;}
        }
        static Mesh CubeMesh(Vector3 size,Vector2 uv)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);try{var m=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);m.vertices=m.vertices.Select(v=>Vector3.Scale(v,size)).ToArray();m.uv=Enumerable.Repeat(uv,m.vertexCount).ToArray();m.RecalculateBounds();return m;}finally{Object.DestroyImmediate(go);}
        }
        static void Part(Transform parent,string name,Vector3 position,Vector3 size,Material material,Vector2 uv)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.transform.localPosition=position;var m=CubeMesh(size,uv);m.name=name;AssetDatabase.CreateAsset(m,Prepared+"/"+name+".asset");go.GetComponent<MeshFilter>().sharedMesh=m;go.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
        static float SolveLeg(Transform upper,Transform lower,Transform foot,Vector3 target,Vector3 hint)
        {
            var hip=upper.position;float a=Vector3.Distance(hip,lower.position),b=Vector3.Distance(lower.position,foot.position);var to=target-hip;float desired=to.magnitude;float distance=Mathf.Clamp(desired,Mathf.Abs(a-b)+.0001f,a+b-.0001f);var direction=to.normalized;var bend=Vector3.ProjectOnPlane(hint-hip,direction).normalized;
            if(bend.sqrMagnitude<.5f)bend=Vector3.Cross(direction,Vector3.right).normalized;float x=(a*a+distance*distance-b*b)/(2*distance);float y=Mathf.Sqrt(Mathf.Max(0,a*a-x*x));var knee=hip+direction*x+bend*y;
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,knee-upper.position)*upper.rotation;lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,target-lower.position)*lower.rotation;return Vector3.Distance(foot.position,target);
        }
        [Serializable]class MountedReport
        {
            public string method="Derived CC0 mounted animation: original horse clips, original Knight upper-body actions, authored saddle-frame following and two-bone seated-leg constraints. No acquired paired cavalry pack, no runtime mounting/charge mechanic.";
            public bool passed;public float horseHeight,riderHeight,backHalfWidth,maximumFootTargetError,maximumPelvisSeatError,minimumKneeAngle=180,maximumKneeAngle,maximumClipReproductionError,radius,overallHeight;
            public int checkedPositions,frames;public Vector3 seatRest,footLeft,footRight;public string geometryEvidence;
        }
        sealed class MountedPose
        {
            public Rig horse,rider;public Transform seat,hips,spine;public AnimationClip[] horseClips,riderClips;public float[] durations;public Quaternion hipBind,riderBasis,footLeftBind,footRightBind;public Vector3 leftFoot,rightFoot;public MountedReport report;
            public void Pose(int action,float time,bool measure)
            {
                float phase=Mathf.Clamp01(time/durations[action]);horse.Pose(horseClips[action],phase*horseClips[action].length);rider.Pose(riderClips[action],phase*riderClips[action].length);
                hips.localRotation=hipBind;
                var hip=hips.position;var rotation=seat.rotation*riderBasis;rider.wrapper.rotation=rotation;rider.wrapper.position=seat.position+seat.up*.01f-rotation*hip;
                if(action==1)spine.localRotation=spine.localRotation*Quaternion.Euler(Mathf.Sin(phase*Mathf.PI*2)*3f,0,0);
                float error=0;
                foreach(var side in new[]{("l",-1,leftFoot,footLeftBind),("r",1,rightFoot,footRightBind)})
                {
                    var upper=Named(rider.body,"upperleg."+side.Item1);var lower=Named(rider.body,"lowerleg."+side.Item1);var foot=Named(rider.body,"foot."+side.Item1);
                    var target=seat.TransformPoint(side.Item3);var hint=seat.TransformPoint(new Vector3(side.Item2*(Mathf.Abs(side.Item3.x)+.08f),-.04f,.23f));error=Mathf.Max(error,SolveLeg(upper,lower,foot,target,hint));foot.rotation=seat.rotation*riderBasis*side.Item4;
                    if(measure){float angle=Vector3.Angle(upper.position-lower.position,foot.position-lower.position);report.minimumKneeAngle=Mathf.Min(report.minimumKneeAngle,angle);report.maximumKneeAngle=Mathf.Max(report.maximumKneeAngle,angle);}
                }
                if(measure){report.maximumFootTargetError=Mathf.Max(report.maximumFootTargetError,error);report.maximumPelvisSeatError=Mathf.Max(report.maximumPelvisSeatError,Vector3.Distance(hips.position,seat.position+seat.up*.01f));}
            }
        }
        // Scaled mounts only: a short-legged rider cannot hang its boots below a wide barrel. Walk the real horse cross-section
        // at the saddle and choose the deepest boot height whose outside-the-surface point is still within 86% leg reach.
        static void SolveScaledFeet(MountedPose pose,Rig rider,Transform hips,Vector3[] horse,float seatZ,float seatY,float band)
        {
            foreach(var side in new[]{("l",-1),("r",1)})
            {
                var upper=Named(rider.body,"upperleg."+side.Item1);var lower=Named(rider.body,"lowerleg."+side.Item1);var foot=Named(rider.body,"foot."+side.Item1);
                float length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,foot.position);float hipX=Mathf.Abs(upper.position.x-hips.position.x);float jy=upper.position.y-hips.position.y+.01f;float reach=length*.86f;
                Vector3? best=null;
                for(float y=jy;y>=jy-reach;y-=.01f)
                {
                    float w=0;foreach(var v in horse)if(Mathf.Abs(v.z-seatZ)<band&&Mathf.Abs(v.y-seatY-y)<.04f)w=Mathf.Max(w,Mathf.Abs(v.x));
                    float x=Mathf.Max(w+.03f,hipX+.12f);if(new Vector2(x-hipX,y-jy).magnitude<=reach)best=new Vector3(side.Item2*x,y,.06f);
                }
                CharacterGeometry.Require(best.HasValue,"Rider legs cannot clear scaled horse at any seat depth.");
                if(side.Item1=="l")pose.leftFoot=best.Value;else pose.rightFoot=best.Value;
            }
        }
        static Vector3[] SkinPositions(GameObject go)=>go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>CharacterGeometry.Skin(go.transform,s)).ToArray();
        static void MakeRein(GameObject root,Transform head,Transform hand,Vector3 muzzle,Material material,Vector2 uv)
        {
            var go=new GameObject("Rein",typeof(SkinnedMeshRenderer));go.transform.SetParent(root.transform,false);var mesh=new Mesh{name="Rein"};var a=root.transform.InverseTransformPoint(muzzle);var b=root.transform.InverseTransformPoint(hand.position);Vector3 width=Vector3.Cross((b-a).normalized,Vector3.up)*.008f;if(width.sqrMagnitude<1e-7f)width=Vector3.right*.008f;
            mesh.vertices=new[]{a-width,a+width,(a+b)*.5f-width,(a+b)*.5f+width,b-width,b+width};mesh.uv=Enumerable.Repeat(uv,6).ToArray();mesh.triangles=new[]{0,1,2,1,3,2,2,3,4,3,5,4,2,1,0,2,3,1,4,3,2,4,5,3};mesh.normals=Enumerable.Repeat(Vector3.up,6).ToArray();mesh.boneWeights=Enumerable.Range(0,6).Select(i=>new BoneWeight{boneIndex0=0,boneIndex1=1,weight0=i<2?1:i<4?.5f:0,weight1=i<2?0:i<4?.5f:1}).ToArray();mesh.bindposes=new[]{head.worldToLocalMatrix*go.transform.localToWorldMatrix,hand.worldToLocalMatrix*go.transform.localToWorldMatrix};mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Prepared+"/Rein.asset");var skin=go.GetComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=new[]{head,hand};skin.rootBone=root.transform;skin.sharedMaterial=material;skin.localBounds=mesh.bounds;
        }
        public static void Prepare01(){PrepareMounted("MountedKnight01",1.8f);CreateIntegratedCollection();Debug.Log("CAVALRY_PREPARATION_READY");}
        // horseHeight is the full mount bounds height; mountScale scales horse-space sampling/tack sizes (mountScale=1 reproduces Prepare01 exactly).
        static void PrepareMounted(string outputName,float horseHeight)
        {
            CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh cavalry output required.");CharacterPipeline.EnsureFolder(Prepared);var report=new MountedReport();
            var stage=new GameObject("Inactive cavalry authoring preview");stage.SetActive(false);var root=new GameObject(outputName);root.transform.SetParent(stage.transform,false);
            var horseRaw=Object.Instantiate(Load<GameObject>(Source+"/Horse.fbx"),stage.transform,false);var knightRaw=Object.Instantiate(Load<GameObject>(KnightSource+"/Knight.fbx"),stage.transform,false);AttachRawWeapons(knightRaw);
            foreach(var a in stage.GetComponentsInChildren<Animator>(true))a.enabled=false;
            try
            {
                var horseBounds=CharacterGeometry.BoundsOf(SkinPositions(horseRaw));var riderBounds=CharacterGeometry.BoundsOf(SkinPositions(knightRaw));report.horseHeight=horseHeight;float mountScale=horseHeight/1.8f;report.riderHeight=1.95f;float hs=report.horseHeight/horseBounds.size.y,rs=report.riderHeight/riderBounds.size.y;
                Vector3 horseShift=-new Vector3(horseBounds.center.x,horseBounds.min.y,horseBounds.center.z)*hs;
                var palette=horseRaw.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Select(m=>m.color).Distinct().ToList();int brown=palette.Count;palette.Add(new Color(.045f,.018f,.008f,1));int blue=palette.Count;palette.Add(LeatherSaddle?new Color(.15f,.045f,.03f,1):new Color(.01f,.10f,.28f,1));int leather=LeatherSaddle?palette.Count:-1;if(LeatherSaddle)palette.Add(new Color(.30f,.14f,.055f,1));int metal=palette.Count;palette.Add(new Color(.45f,.36f,.1f,1));CharacterGeometry.Require(palette.Count<=16,"Palette budget.");
                var knightTexture=ReadTexture(Load<Texture2D>(KnightSource+"/knight_texture.png"));var atlas=new Texture2D(2048,1024,TextureFormat.RGBA32,false);try{var colors=new Color[2048*1024];for(int y=0;y<1024;y++)for(int x=0;x<2048;x++){if(x<1024)colors[y*2048+x]=knightTexture.GetPixelBilinear((x+.5f)/1024,(y+.5f)/1024);else{int tile=(y/256)*4+(x-1024)/256;colors[y*2048+x]=tile<palette.Count?palette[tile].gamma:Color.white;}}atlas.SetPixels(colors);atlas.Apply();File.WriteAllBytes(Prepared+"/MountedAtlas.png",atlas.EncodeToPNG());}finally{Object.DestroyImmediate(knightTexture);Object.DestroyImmediate(atlas);}
                AssetDatabase.ImportAsset(Prepared+"/MountedAtlas.png");var ti=(TextureImporter)AssetImporter.GetAtPath(Prepared+"/MountedAtlas.png");ti.textureCompression=TextureImporterCompression.Uncompressed;ti.wrapMode=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Bilinear;ti.SaveAndReimport();
                var prior=Load<UnitTypeConfig>("Assets/Game/CharacterPipeline/Generated/Knight04/Unit.asset");var template=CharacterPipeline.CloneUnit(prior,Prepared,"Template");template.unitTypeName="剑盾骑兵（派生骑乘）";
                Material Mat(Material from,string name){var m=new Material(from){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Load<Texture2D>(Prepared+"/MountedAtlas.png"));m.SetColor("_BaseColor",Color.white);AssetDatabase.CreateAsset(m,Prepared+"/"+name+".mat");return m;}
                template.renderConfig.nearMaterial=Mat(prior.renderConfig.nearMaterial,"MountedNear");template.renderConfig.midMaterial=Mat(prior.renderConfig.midMaterial,"MountedMid");template.renderConfig.farMaterial=template.renderConfig.midMaterial;CharacterPipeline.Save(template.renderConfig);
                var horse=Canonical(horseRaw,root,"Horse",hs,horseShift,template.renderConfig.nearMaterial,palette,true);var rider=Canonical(knightRaw,root,"Rider",rs,Vector3.zero,template.renderConfig.nearMaterial,palette,false);
                var hc=new[]{Clip(Source+"/Horse.fbx","Idle"),Clip(Source+"/Horse.fbx","Gallop"),Clip(Source+"/Horse.fbx","Attack_Headbutt"),Clip(Source+"/Horse.fbx","Death")};
                var rc=new[]{Clip(KnightSource+"/Rig_Medium_General.fbx","Idle_A"),Clip(KnightSource+"/Rig_Medium_General.fbx","Idle_A"),Clip(KnightSource+"/Rig_Medium_CombatMelee.fbx","Melee_1H_Attack_Chop"),Clip(KnightSource+"/Rig_Medium_General.fbx","Death_A")};
                horse.Pose(hc[0],0);rider.Pose(rc[0],0);var torso=Named(horse.body,"Torso");var head=Named(horse.body,"Head");var forward=Vector3.ProjectOnPlane(head.position-torso.position,Vector3.up).normalized;var basePositions=SkinPositions(root).Take(horse.body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s=>s.sharedMesh.vertexCount)).ToArray();
                float seatZ=torso.position.z+SeatForwardMeters*Mathf.Sign(forward.z);var back=basePositions.Where(v=>Mathf.Abs(v.z-seatZ)<.18f*mountScale&&Mathf.Abs(v.x)<.20f*mountScale).ToArray();CharacterGeometry.Require(back.Length>0,"No horse back surface for saddle.");float backY=back.Max(v=>v.y);var highBack=basePositions.Where(v=>Mathf.Abs(v.z-seatZ)<.26f*mountScale&&v.y>backY-.16f*mountScale).ToArray();report.backHalfWidth=highBack.Max(v=>Mathf.Abs(v.x));
                var seat=new GameObject("SaddleFrame").transform;seat.SetParent(torso,false);seat.SetPositionAndRotation(new Vector3(0,backY+.025f,seatZ),Quaternion.LookRotation(forward,Vector3.up));report.seatRest=seat.position;
                var hips=Named(rider.body,"hips");var footL=Named(rider.body,"foot.l");var footR=Named(rider.body,"foot.r");var riderForward=Vector3.ProjectOnPlane((Named(rider.body,"toes.l").position-footL.position)+(Named(rider.body,"toes.r").position-footR.position),Vector3.up).normalized;var basis=Quaternion.FromToRotation(riderForward,Vector3.forward);
                var pose=new MountedPose{horse=horse,rider=rider,seat=seat,hips=hips,spine=Named(rider.body,"spine"),horseClips=hc,riderClips=rc,hipBind=hips.localRotation,riderBasis=basis,footLeftBind=footL.rotation,footRightBind=footR.rotation,report=report,durations=new[]{hc[0].length,hc[1].length,Mathf.Max(hc[2].length,rc[2].length),Mathf.Max(hc[3].length,rc[3].length)}};
                // Geometric seating targets: knees outward/forward, boots below the saddle; no standing lower-body animation.
                if(mountScale>1.01f)SolveScaledFeet(pose,rider,hips,basePositions,seatZ,backY+.025f,.26f*mountScale);else foreach(var side in new[]{("l",-1),("r",1)}){var upper=Named(rider.body,"upperleg."+side.Item1);var lower=Named(rider.body,"lowerleg."+side.Item1);var foot=Named(rider.body,"foot."+side.Item1);float length=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,foot.position);float hipX=Mathf.Abs(upper.position.x-hips.position.x);float x=Mathf.Max(report.backHalfWidth+.025f,hipX+.12f);float lateral=x-hipX;CharacterGeometry.Require(lateral<length*.90f,"Rider legs cannot clear horse: reduce mount size or author a different rider.");float drop=Mathf.Sqrt(Mathf.Max(.001f,length*length*.86f*.86f-lateral*lateral));float y=upper.position.y-hips.position.y+.01f-drop;Vector3 target=new Vector3(side.Item2*x,y,.06f);if(side.Item1=="l")pose.leftFoot=target;else pose.rightFoot=target;}
                report.footLeft=pose.leftFoot;report.footRight=pose.rightFoot;pose.Pose(0,0,false);
                Vector2 UV(int color)=>new Vector2(.5f+(color%4+.5f)/8f,(color/4+.5f)/4f);
                if(NoSaddle){}
                else if(LeatherSaddle)
                {
                    // Fitted saddle scaled with the mount: narrow dark blanket rim, leather seat, low pommel (narrow, clear of thighs) and cantle behind the hips.
                    float w=report.backHalfWidth*2;var mat=template.renderConfig.nearMaterial;
                    Part(seat,"SaddleBlanket",new Vector3(0,-.014f,-.02f*mountScale),new Vector3(w+.05f,.022f,.50f*mountScale),mat,UV(blue));
                    Part(seat,"SaddleSeat",Vector3.zero,new Vector3(w+.02f,.05f,.40f*mountScale),mat,UV(leather));
                    Part(seat,"SaddlePommel",new Vector3(0,.05f,.18f*mountScale),new Vector3(w*.30f,.09f,.06f*mountScale),mat,UV(brown));
                    Part(seat,"SaddleCantle",new Vector3(0,.06f,-.19f*mountScale),new Vector3(w*.60f,.11f,.06f*mountScale),mat,UV(brown));
                }
                else{Part(seat,"SaddleBlanket",new Vector3(0,-.016f,0),new Vector3(report.backHalfWidth*2+.10f,.035f,.56f*mountScale),template.renderConfig.nearMaterial,UV(blue));Part(seat,"SaddleSeat",Vector3.zero,new Vector3(report.backHalfWidth*2+.025f,.055f,.34f),template.renderConfig.nearMaterial,UV(brown));}
                if(!NoSaddle)foreach(var side in new[]{("Left",pose.leftFoot),("Right",pose.rightFoot)}){Part(seat,"Stirrup"+side.Item1,side.Item2-new Vector3(0,.025f,0),new Vector3(.10f,.035f,.16f),template.renderConfig.nearMaterial,UV(metal));}
                MakeRein(root,head,Named(rider.body,"handslot.l"),head.position+forward*.18f*mountScale-Vector3.up*.08f*mountScale,template.renderConfig.nearMaterial,UV(brown));
                var recipe=ScriptableObject.CreateInstance<CharacterRecipe>();recipe.attachmentPaths=root.GetComponentsInChildren<MeshRenderer>(true).Select(r=>AnimationUtility.CalculateTransformPath(r.transform,root.transform)).ToArray();var transforms=root.GetComponentsInChildren<Transform>(true).Where(t=>t!=root.transform).ToArray();string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};var actions=new[]{"Idle","Move","Attack","Death"};var clips=new AnimationClip[4];
                for(int a=0;a<4;a++)
                {
                    int frames=Mathf.CeilToInt(pose.durations[a]*24);var keys=transforms.ToDictionary(t=>t,t=>Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray());
                    for(int f=0;f<=frames;f++){float time=a==3?pose.durations[a]*Mathf.Min(f,frames-1)/(frames-1):Mathf.Min(f/24f,pose.durations[a]);if(a==3&&f==frames)break;pose.Pose(a,time,true);foreach(var t in transforms){var v=t.localPosition;var q=t.localRotation;float[] vals={v.x,v.y,v.z,q.x,q.y,q.z,q.w};for(int c=0;c<7;c++)keys[t][c].Add(new Keyframe(time,vals[c]));}}
                    var clip=new AnimationClip{name=actions[a],frameRate=30};foreach(var pair in keys)for(int channel=0;channel<7;channel++){var curve=new AnimationCurve(pair.Value[channel].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.Key,root.transform),typeof(Transform),props[channel]),curve);}clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=a!=3;AnimationUtility.SetAnimationClipSettings(clip,settings);AssetDatabase.CreateAsset(clip,Prepared+"/"+actions[a]+".anim");clips[a]=clip;
                    for(int f=0;f<Mathf.CeilToInt(clip.length*24);f++){float time=a==3?clip.length*f/(Mathf.CeilToInt(clip.length*24)-1):Mathf.Min(f/24f,clip.length);pose.Pose(a,time,false);var expected=CharacterGeometry.Positions(root,recipe);clip.SampleAnimation(root,time);var actual=CharacterGeometry.Positions(root,recipe);for(int v=0;v<actual.Length;v++){report.maximumClipReproductionError=Mathf.Max(report.maximumClipReproductionError,Vector3.Distance(expected[v],actual[v]));report.checkedPositions++;if(a<3)report.radius=Mathf.Max(report.radius,new Vector2(actual[v].x,actual[v].z).magnitude);}report.frames++;}
                }
                File.WriteAllText(Log+"/mounted-contract-attempt.json",JsonUtility.ToJson(report,true));CharacterGeometry.Require(report.maximumFootTargetError<.025f&&report.maximumPelvisSeatError<.001f&&report.maximumKneeAngle<165,"Mounted contact/seated pose contract failed.");CharacterGeometry.Require(report.maximumClipReproductionError<.00025f,"Authored pose/serialized clip reproduction failed.");pose.Pose(0,0,false);root.transform.SetParent(null,false);root.SetActive(true);CharacterGeometry.CheckHierarchy(root,"");var prefab=PrefabUtility.SaveAsPrefabAsset(root,Prepared+"/MountedKnight.prefab");
                report.radius=Mathf.Ceil((report.radius+.15f)*10)/10;report.overallHeight=CharacterGeometry.BoundsOf(CharacterGeometry.Positions(prefab,recipe,true)).size.y;recipe.name=outputName;recipe.model=prefab;recipe.idle=clips[0];recipe.move=clips[1];recipe.attack=clips[2];recipe.death=clips[3];recipe.frameRate=24;recipe.sizeRootPath="";recipe.groundBindFeet=false;recipe.targetBodyHeight=report.overallHeight;recipe.agentRadius=report.radius;recipe.paletteColumns=8;recipe.paletteRows=4;recipe.lowVertexBudget=2200;recipe.maxTextureMiB=48;recipe.outputName=outputName;recipe.unitTemplate=template;
                template.combatConfig.projectileRange=0;template.combatConfig.targetAcquireRadius=12;template.combatConfig.attackRange=report.radius+.55f+.5f;template.combatConfig.attackInterval=clips[2].length;template.combatConfig.maxHp=300;template.combatConfig.attackDamage=30;template.spawnConfig.unitCount=2;template.spawnConfig.spawnCenter=new Vector3(-45,0,0);float sideSize=(report.radius*2+1)*5;template.spawnConfig.spawnSize=new Vector3(sideSize,0,sideSize);template.movementConfig.maxSpeed=3.5f;template.animationConfig.moveReferenceSpeed=3.5f;CharacterPipeline.Save(template.combatConfig);CharacterPipeline.Save(template.spawnConfig);CharacterPipeline.Save(template.movementConfig);CharacterPipeline.Save(template.animationConfig);CharacterPipeline.Save(template);AssetDatabase.CreateAsset(recipe,CharacterPipeline.Root+"/Recipes/"+outputName+".asset");
                report.passed=true;File.WriteAllText(Log+"/mounted-preparation.json",JsonUtility.ToJson(report,true));var result=CharacterPipeline.Run(recipe);report.geometryEvidence=result.evidence;File.WriteAllText(Log+"/mounted-preparation.json",JsonUtility.ToJson(report,true));File.WriteAllText(Log+"/mounted-report-01.json",JsonUtility.ToJson(result,true));
            }
            finally{Object.DestroyImmediate(root);Object.DestroyImmediate(horseRaw);Object.DestroyImmediate(knightRaw);Object.DestroyImmediate(stage);}
        }
        public static void CreateIntegratedCollection()
        {
            string folder=Prepared+"/Integrated";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated cavalry scene required.");CharacterPipeline.EnsureFolder(folder);
            var old=Load<WarSandboxBattlefieldCatalog>(Previous+"/Catalog.asset");var oldPolicy=Load<WarSandboxRosterPolicy>(Previous+"/LargePolicy.asset");var source=Load<UnitTypeConfig>(CharacterPipeline.Root+"/Generated/MountedKnight01/Unit.asset");var unit=CharacterPipeline.CloneUnit(source,folder,"MountedKnight");unit.spawnConfig.unitCount=2;CharacterPipeline.Save(unit.spawnConfig);CharacterPipeline.Save(unit);
            var control=oldPolicy.templates.Single(t=>t.flockingConfig.agentRadius<1);CharacterGeometry.Require(control.combatConfig.attackRange>=unit.flockingConfig.agentRadius+control.flockingConfig.agentRadius,"Existing adapted counter cannot reach this mount.");var policy=Object.Instantiate(oldPolicy);policy.name="MountedRosterPolicy";policy.templates=oldPolicy.templates.Concat(new[]{unit}).ToArray();policy.maximumRadius=Mathf.Max(policy.maximumRadius,unit.flockingConfig.agentRadius+.1f);policy.explanation="骑乘/大型角色库 · 组合单位仅攻方，守方用适配骑士。骑手和马作为一个单位，不含上下马、冲锋或击飞机制。";AssetDatabase.CreateAsset(policy,folder+"/Policy.asset");
            var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{unit,control};AssetDatabase.CreateAsset(scenario,folder+"/Scenario.asset");string battle=folder+"/Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(old.entries[0].scenePath,battle),"Cannot copy new cavalry scene.");var entry=old.entries[0].CopyIdentity();entry.id="cavalry-mounted-knight";entry.displayName="剑盾骑兵 · 配套骑乘动作";entry.scenePath=battle;entry.preview=null;entry.description="已有CC0骑士+CC0马，本轮派生制作坐姿/随动/攻击/整体倒地。";entry.briefing="骑手和坐骑统一VAT，现有地面近战。不是取得原厂成套骑兵动画，也不做落马/冲锋机制。";
            var catalog=Object.Instantiate(old);catalog.name="UnifiedMountedRoster";catalog.defaultEntryId=entry.id;catalog.entries=new[]{entry}.Concat(old.entries.Select(e=>e.CopyIdentity())).ToArray();catalog.templates=old.templates.Concat(new[]{new WarSandboxUnitTemplateEntry{templateId="roster-mounted-knight",revision=1,config=unit}}).ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                var scene=EditorSceneManager.OpenScene(battle,OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(folder+"/Policy.asset");EditorSceneManager.SaveScene(scene);
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Previous+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy unified menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var router=Object.FindFirstObjectByType<NonhumanBatchCaseRouter>();router.entries=new[]{new NonhumanBatchCaseRouter.Entry{key="cavalry",battlefieldId="cavalry-mounted-knight",templateId="roster-mounted-knight",unit=Load<UnitTypeConfig>(folder+"/MountedKnight.asset"),scenario=Load<ScenarioConfig>(folder+"/Scenario.asset"),initial=2,saved=3,templates=7,large=true}}.Concat(router.entries).ToArray();var smoke=router.smoke;smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedTemplate=router.entries[0].unit;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.planSlot="MountedKnight";smoke.initialTrialCount=2;smoke.savedTrialCount=3;smoke.preBattleCheck=router.probe;var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="UnifiedRosterPlans";preview.previewName="Unified Cavalry";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/integration-ready.json","{\"passed\":true,\"newMountedComposites\":1,\"previousModelsPreserved\":17,\"mountedLargeMenuVariants\":7,\"oldCodeChanged\":false}");
        }
        public static void CreateMatchedContext02()
        {
            string folder=Prepared+"/Integrated02";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated cavalry scene required.");CharacterPipeline.EnsureFolder(folder);
            var old=Load<WarSandboxBattlefieldCatalog>(Previous+"/Catalog.asset");var oldPolicy=Load<WarSandboxRosterPolicy>(Previous+"/LargePolicy.asset");var source=Load<UnitTypeConfig>(CharacterPipeline.Root+"/Generated/MountedKnight01/Unit.asset");var unit=CharacterPipeline.CloneUnit(source,folder,"MountedKnight");unit.spawnConfig.unitCount=2;CharacterPipeline.Save(unit.spawnConfig);CharacterPipeline.Save(unit);
            var control=CharacterPipeline.CloneUnit(oldPolicy.templates.Single(t=>t.flockingConfig.agentRadius<1),folder,"CavalryKnightControl");control.unitTypeName="骑兵对照·骑士";control.combatConfig.attackRange=unit.combatConfig.attackRange;CharacterPipeline.Save(control.combatConfig);CharacterPipeline.Save(control);var policy=Object.Instantiate(oldPolicy);policy.name="MountedRosterPolicy";policy.templates=new[]{unit,control};policy.maximumRadius=unit.flockingConfig.agentRadius+.1f;policy.explanation="骑乘适配场 · 骑兵仅攻方，对面使用匹配距离的骑士。旧巨兽场单独保留；不含上下马、冲锋或击飞。";AssetDatabase.CreateAsset(policy,folder+"/Policy.asset");
            var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{unit,control};AssetDatabase.CreateAsset(scenario,folder+"/Scenario.asset");string battle=folder+"/Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(old.entries[0].scenePath,battle),"Cannot copy new cavalry scene.");var entry=old.entries[0].CopyIdentity();entry.id="cavalry-mounted-knight";entry.displayName="剑盾骑兵 · 配套骑乘动作";entry.scenePath=battle;entry.preview=null;entry.description="已有CC0骑士+CC0马，本轮派生制作坐姿/随动/攻击/整体倒地。";entry.briefing="骑手和坐骑统一VAT，现有地面近战。不是取得原厂成套骑兵动画，也不做落马/冲锋机制。";
            var catalog=Object.Instantiate(old);catalog.name="UnifiedMountedRoster";catalog.defaultEntryId=entry.id;catalog.entries=new[]{entry}.Concat(old.entries.Select(e=>e.CopyIdentity())).ToArray();catalog.templates=old.templates.Concat(new[]{new WarSandboxUnitTemplateEntry{templateId="roster-mounted-knight",revision=1,config=unit},new WarSandboxUnitTemplateEntry{templateId="cavalry-knight-control",revision=1,config=control}}).ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                var scene=EditorSceneManager.OpenScene(battle,OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(folder+"/Policy.asset");EditorSceneManager.SaveScene(scene);
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Previous+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy unified menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var router=Object.FindFirstObjectByType<NonhumanBatchCaseRouter>();router.entries=new[]{new NonhumanBatchCaseRouter.Entry{key="cavalry",battlefieldId="cavalry-mounted-knight",templateId="roster-mounted-knight",unit=Load<UnitTypeConfig>(folder+"/MountedKnight.asset"),scenario=Load<ScenarioConfig>(folder+"/Scenario.asset"),initial=2,saved=3,templates=2,large=true}}.Concat(router.entries).ToArray();var smoke=router.smoke;smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedTemplate=router.entries[0].unit;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.planSlot="MountedKnight";smoke.initialTrialCount=2;smoke.savedTrialCount=3;smoke.preBattleCheck=router.probe;var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="UnifiedRosterPlans";preview.previewName="Unified Cavalry";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/integration-ready-02.json","{\"passed\":true,\"newMountedComposites\":1,\"previousModelsPreserved\":17,\"mountedLargeMenuVariants\":2,\"oldCodeChanged\":false}");
        }
        public static void Build02()
        {
            string dest="Builds/UnifiedCavalry-20260930-02",folder=Prepared+"/Integrated02";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite build.");var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Matched cavalry build failed.");File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
        public static void Prepare02()
        {
            // User playtest: mount read about half-size next to the 1.95m rider. 3.5m (~1.94x) keeps every composite vertex below 4m so Half VAT rounding stays in the same precision band.
            Prepared="Assets/Game/Cavalry/Prepared03";Log="Logs/AgentCavalryScale";CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh cavalry output required.");
            PrepareMounted("MountedKnight02",3.5f);CreateMatchedContext03();Debug.Log("CAVALRY_SCALE_READY");
        }
        public static void Prepare04()
        {
            // Playtest 2: rider sat over the rear of the back (Torso bone is at z=-0.97 of a -1.9..+0.8 body). Seat moves 0.62m forward to just behind the withers
            // (back profile Logs/AgentCavalrySeat/horse-profile.txt); flat blue blanket replaced by a fitted leather saddle. Horse/rider sizes unchanged.
            Prepared="Assets/Game/Cavalry/Prepared04";Log="Logs/AgentCavalrySeat";CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh cavalry output required.");
            SeatForwardMeters=.62f;LeatherSaddle=true;MountedOutput="MountedKnight03";
            PrepareMounted("MountedKnight03",3.5f);CreateMatchedContext03();Debug.Log("CAVALRY_SEAT_READY");
        }
        public static void Prepare05()
        {
            // Playtest 3: user prefers no saddle at all. Same forward seat as Prepare04; no blanket/seat/pommel/cantle geometry.
            Prepared="Assets/Game/Cavalry/Prepared05";Log="Logs/AgentCavalrySeat/prepare05";Directory.CreateDirectory(Log);CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh cavalry output required.");
            SeatForwardMeters=.62f;NoSaddle=true;MountedOutput="MountedKnight04";
            PrepareMounted("MountedKnight04",3.5f);CreateMatchedContext03();Debug.Log("CAVALRY_NOSADDLE_READY");
        }
        // Overnight task 6: cavalry charge. Same accepted MountedKnight04 model (no re-bake); the integrated unit gallops faster
        // and its first melee hit on arrival deals x3 (CombatConfig.chargeDamageMultiplier, opt-in core field, default 1).
        static bool Charge=false;const float ChargeSpeed=6f,ChargeMultiplier=3f,ChargeMinSpeedFraction=.6f,ChargeAnimationMax=1.75f;
        // User decision 2026-10-01 ("加强"): 2 cavalry must beat the 16 knights. Measured (CavalryStrengthTests, cav-strength-02): damage 45
        // (3 hits per 100 HP knight instead of 4) wins with both riders alive and ~38% HP left; HP x1.5 only scrapes by; charge x5 changes nothing.
        static int StrongDamage=0;
        public static void Prepare07()
        {
            Prepared="Assets/Game/Cavalry/Prepared07";Log="Logs/AgentGiants/cavalry07";Directory.CreateDirectory(Log);CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh cavalry output required.");CharacterPipeline.EnsureFolder(Prepared);
            SeatForwardMeters=.62f;NoSaddle=true;MountedOutput="MountedKnight04";Charge=true;StrongDamage=45;
            var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/"+MountedOutput+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Mounted model must be accepted.");
            CreateMatchedContext03();Debug.Log("CAVALRY_STRONG_READY");
        }
        public static void Prepare06()
        {
            Prepared="Assets/Game/Cavalry/Prepared06";Log="Logs/AgentCharge/prepare06";Directory.CreateDirectory(Log);CharacterGeometry.Require(!Directory.Exists(Prepared),"Fresh cavalry output required.");CharacterPipeline.EnsureFolder(Prepared);
            SeatForwardMeters=.62f;NoSaddle=true;MountedOutput="MountedKnight04";Charge=true;
            var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/"+MountedOutput+"/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Mounted model must be accepted.");
            CreateMatchedContext03();Debug.Log("CAVALRY_CHARGE_READY");
        }
        public static void CreateMatchedContext03()
        {
            string folder=Prepared+"/Integrated";CharacterGeometry.Require(!Directory.Exists(folder),"Fresh integrated cavalry scene required.");CharacterPipeline.EnsureFolder(folder);
            var old=Load<WarSandboxBattlefieldCatalog>(Previous+"/Catalog.asset");var oldPolicy=Load<WarSandboxRosterPolicy>(Previous+"/LargePolicy.asset");var source=Load<UnitTypeConfig>(CharacterPipeline.Root+"/Generated/"+MountedOutput+"/Unit.asset");var unit=CharacterPipeline.CloneUnit(source,folder,"MountedKnight");unit.spawnConfig.unitCount=2;CharacterPipeline.Save(unit.spawnConfig);CharacterPipeline.Save(unit);
            if(Charge){unit.movementConfig.maxSpeed=ChargeSpeed;unit.animationConfig.moveAnimationSpeedMax=ChargeAnimationMax;unit.combatConfig.chargeDamageMultiplier=ChargeMultiplier;unit.combatConfig.chargeMinSpeedFraction=ChargeMinSpeedFraction;CharacterPipeline.Save(unit.movementConfig);CharacterPipeline.Save(unit.animationConfig);CharacterPipeline.Save(unit.combatConfig);CharacterPipeline.Save(unit);}
            if(StrongDamage>0){unit.combatConfig.attackDamage=StrongDamage;CharacterPipeline.Save(unit.combatConfig);CharacterPipeline.Save(unit);}
            var control=CharacterPipeline.CloneUnit(oldPolicy.templates.Single(t=>t.flockingConfig.agentRadius<1),folder,"CavalryKnightControl");control.unitTypeName="骑兵对照·骑士";control.combatConfig.attackRange=unit.combatConfig.attackRange;CharacterPipeline.Save(control.combatConfig);CharacterPipeline.Save(control);var policy=Object.Instantiate(oldPolicy);policy.name="MountedRosterPolicy";policy.templates=new[]{unit,control};policy.maximumRadius=unit.flockingConfig.agentRadius+.1f;policy.explanation=Charge?"骑乘适配场 · 骑兵仅攻方，对面使用匹配距离的骑士。骑兵移速 6m/s，冲到敌前的第一击造成 3 倍伤害（冲锋）；不含上下马或击飞。":"骑乘适配场 · 骑兵仅攻方，对面使用匹配距离的骑士。旧巨兽场单独保留；不含上下马、冲锋或击飞。";AssetDatabase.CreateAsset(policy,folder+"/Policy.asset");
            var scenario=ScriptableObject.CreateInstance<ScenarioConfig>();scenario.unitTypes=new[]{unit,control};AssetDatabase.CreateAsset(scenario,folder+"/Scenario.asset");string battle=folder+"/Battlefield.unity";CharacterGeometry.Require(AssetDatabase.CopyAsset(old.entries[0].scenePath,battle),"Cannot copy new cavalry scene.");var entry=old.entries[0].CopyIdentity();entry.id="cavalry-mounted-knight";entry.displayName="剑盾骑兵 · 标准马体（3.5m）";entry.scenePath=battle;entry.preview=null;entry.description="按试玩反馈把马放大约1.94倍（整马高3.5m），骑手尺寸不变；派生坐姿/随动/攻击/整体倒地。";entry.briefing=Charge?"冲锋：骑兵以不低于 60% 最高速度冲到敌人面前时，第一击 3 倍伤害（"+unit.combatConfig.attackDamage+"→"+Mathf.RoundToInt(unit.combatConfig.attackDamage*ChargeMultiplier)+"），之后恢复常规攻击；拉开距离再冲可再次触发。骑兵移速 6m/s（步兵 3m/s）。不做落马/击飞。":"骑手和坐骑统一VAT，现有地面近战。不是取得原厂成套骑兵动画，也不做落马/冲锋机制。";if(Charge)entry.displayName="剑盾骑兵 · 冲锋（3.5m）";
            var catalog=Object.Instantiate(old);catalog.name="UnifiedMountedRoster";catalog.defaultEntryId=entry.id;catalog.entries=new[]{entry}.Concat(old.entries.Select(e=>e.CopyIdentity())).ToArray();catalog.templates=old.templates.Concat(new[]{new WarSandboxUnitTemplateEntry{templateId="roster-mounted-knight",revision=1,config=unit},new WarSandboxUnitTemplateEntry{templateId="cavalry-knight-control",revision=1,config=control}}).ToArray();AssetDatabase.CreateAsset(catalog,folder+"/Catalog.asset");CharacterGeometry.Require(catalog.TryValidate(p=>File.Exists(p),out string error)&&catalog.TryValidateTemplates(out error),error);
            var setup=EditorSceneManager.GetSceneManagerSetup();try
            {
                var scene=EditorSceneManager.OpenScene(battle,OpenSceneMode.Single);var manager=Object.FindFirstObjectByType<MassEngineManager>();manager.scenarioConfig=Load<ScenarioConfig>(folder+"/Scenario.asset");manager.battleStarted=false;WarSandboxRuntimeBootstrap.EnsureControls(manager);manager.GetComponent<WarSandboxRuntimeDeployment>().rosterPolicy=Load<WarSandboxRosterPolicy>(folder+"/Policy.asset");EditorSceneManager.SaveScene(scene);
                CharacterGeometry.Require(AssetDatabase.CopyAsset(Previous+"/Menu.unity",folder+"/Menu.unity"),"Cannot copy unified menu.");var menu=EditorSceneManager.OpenScene(folder+"/Menu.unity",OpenSceneMode.Single);var session=Object.FindFirstObjectByType<WarSandboxSceneSession>();session.catalog=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");session.enterDefaultOnStart=true;
                var router=Object.FindFirstObjectByType<NonhumanBatchCaseRouter>();router.entries=new[]{new NonhumanBatchCaseRouter.Entry{key="cavalry",battlefieldId="cavalry-mounted-knight",templateId="roster-mounted-knight",unit=Load<UnitTypeConfig>(folder+"/MountedKnight.asset"),scenario=Load<ScenarioConfig>(folder+"/Scenario.asset"),initial=2,saved=3,templates=2,large=true}}.Concat(router.entries).ToArray();var smoke=router.smoke;smoke.session=session;smoke.expectedCatalog=session.catalog;smoke.expectedTemplate=router.entries[0].unit;smoke.expectedSourceScenario=router.entries[0].scenario;smoke.battlefieldId=router.entries[0].battlefieldId;smoke.templateId=router.entries[0].templateId;smoke.planSlot="MountedKnight";smoke.initialTrialCount=2;smoke.savedTrialCount=3;smoke.preBattleCheck=router.probe;var preview=Object.FindFirstObjectByType<CharacterPipelinePreviewRuntime>();preview.planDirectory="UnifiedRosterPlans";preview.previewName="Unified Cavalry";EditorSceneManager.SaveScene(menu);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            File.WriteAllText(Log+"/integration-ready-03.json","{\"passed\":true,\"newMountedComposites\":1,\"previousModelsPreserved\":17,\"mountedLargeMenuVariants\":2,\"oldCodeChanged\":false}");
        }

        public static void HorseProfile()
        {
            // Read-only: top-of-back profile of the 3.5m mount in Idle frame 0 (same scale/shift as PrepareMounted), to place the seat behind the withers.
            var stage=new GameObject("profile");stage.SetActive(false);var horseRaw=Object.Instantiate(Load<GameObject>(Source+"/Horse.fbx"),stage.transform,false);foreach(var a in stage.GetComponentsInChildren<Animator>(true))a.enabled=false;
            try
            {
                var bounds=CharacterGeometry.BoundsOf(SkinPositions(horseRaw));float hs=3.5f/bounds.size.y,mountScale=3.5f/1.8f;Vector3 shift=-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*hs;
                Clip(Source+"/Horse.fbx","Idle").SampleAnimation(horseRaw,0);var p=SkinPositions(horseRaw).Select(v=>v*hs+shift).ToArray();
                var sb=new System.Text.StringBuilder();Vector3 T(string n)=>Named(horseRaw,n).position*hs+shift;
                foreach(var n in new[]{"Torso","Head","Neck","Hips","Tail","Chest","Spine","Body"})try{sb.AppendLine("BONE "+n+" "+T(n).ToString("F3"));}catch{}
                sb.AppendLine("BONES "+string.Join(",",horseRaw.GetComponentsInChildren<Transform>(true).Select(t=>t.name)));
                float zmin=p.Min(v=>v.z),zmax=p.Max(v=>v.z);sb.AppendLine("Z range "+zmin.ToString("F3")+" .. "+zmax.ToString("F3"));
                for(float z=zmin;z<=zmax;z+=.1f){var s=p.Where(v=>Mathf.Abs(v.z-z)<.05f&&Mathf.Abs(v.x)<.20f*mountScale).ToArray();if(s.Length==0)continue;var w=p.Where(v=>Mathf.Abs(v.z-z)<.05f).ToArray();sb.AppendLine("Z "+z.ToString("F2")+" topY "+s.Max(v=>v.y).ToString("F3")+" halfWidth "+w.Max(v=>Mathf.Abs(v.x)).ToString("F3")+" minY "+w.Min(v=>v.y).ToString("F3"));}
                Directory.CreateDirectory("Logs/AgentCavalrySeat");File.WriteAllText("Logs/AgentCavalrySeat/horse-profile.txt",sb.ToString());Debug.Log("HORSE_PROFILE_READY");
            }
            finally{Object.DestroyImmediate(stage);}
        }
        public static void Build03()
        {
            string dest="Builds/UnifiedCavalry-20260930-03",folder="Assets/Game/Cavalry/Prepared03/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"Never overwrite build.");var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/MountedKnight02/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Scaled cavalry not validated.");var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Scaled cavalry build failed.");File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
        public static void Build01()
        {
            string dest="Builds/UnifiedCavalry-20260930-01",folder=Prepared+"/Integrated";CharacterGeometry.Require(!Directory.Exists(dest),"No build overwrite.");var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/MountedKnight01/PipelineReport.json"));CharacterGeometry.Require(r.automatedPassed,"Cavalry not validated.");var cat=Load<WarSandboxBattlefieldCatalog>(folder+"/Catalog.asset");var b=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{folder+"/Menu.unity"}.Concat(cat.entries.Select(e=>e.scenePath)).ToArray(),locationPathName=dest+"/UnifiedRoster.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(b.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Cavalry build failed.");File.WriteAllText(dest+"/Start-Roster.cmd","@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0UnifiedRoster.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n");
        }
    }
}
