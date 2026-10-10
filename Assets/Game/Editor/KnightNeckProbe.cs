using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class KnightNeckProbe
    {
        const string Src = "Assets/Art/Source/CharacterPilot/KayKitKnight";
        const string Prepared = "Assets/Game/Content/Characters/CharacterPilotPlayable/Prepared";
        const string Output = "Logs/AgentKnightNeck";
        [Serializable] class Part { public string name; public Vector3 min,max; public float bakeVsLinearSkinMaxError; }
        [Serializable] class Shape { public string stage,action; public float time; public List<Part> parts = new List<Part>(); public Vector3[] vertices,linearVertices; public Vector2[] uv; public int[] triangles; }
        [Serializable] class Difference { public string action,part; public float sourceScaledVsPrepared,preparedVsVat,linearSkinVsVat; }
        [Serializable] class Result { public List<Difference> differences=new List<Difference>(); public string method="Raw source vs prepared BakeMesh vs independently computed linear skinning vs persisted Full VAT. No source changes."; }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var report=new Result();
            string[] names={"Idle","Move","Attack","Death"};
            string[] sourceNames={"Idle_A","Running_A","Melee_1H_Attack_Chop","Death_A"};
            string[] files={"General","MovementBasic","CombatMelee","General"};
            var profile=AssetDatabase.LoadAssetAtPath<VATProfile>("Assets/Game/Content/Characters/CharacterPilotPlayable/KnightPaletteSafeVAT.asset");
            Color[] vat=profile.positionTexture.GetPixels();
            var windows=new[]{profile.idle,profile.move,profile.attack,profile.death};
            for(int action=0;action<4;action++)
            {
                var original=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Src+"/Knight.fbx"));
                var ready=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prepared+"/KayKitKnightPrepared.prefab"));
                try
                {
                    foreach(var a in original.GetComponentsInChildren<Animator>())a.enabled=false;
                    foreach(var a in ready.GetComponentsInChildren<Animator>())a.enabled=false;
                    var raw=AssetDatabase.LoadAllAssetsAtPath(Src+"/Rig_Medium_"+files[action]+".fbx").OfType<AnimationClip>().Single(c=>c.name==sourceNames[action]);
                    var prepared=AssetDatabase.LoadAssetAtPath<AnimationClip>(Prepared+"/"+names[action]+".anim");
                    // The same exact local frame is compared to persisted VAT, including death's endpoint sampling.
                    int local= action==0?0:windows[action].frameCount/2;
                    float time=action==3?raw.length*local/(windows[action].frameCount-1):local/30f;
                    if(action==0){WriteShape(original,"source-bind","Bind",0);WriteShape(ready,"prepared-bind","Bind",0);}
                    raw.SampleAnimation(original,time); prepared.SampleAnimation(ready,time);
                    Shape source=WriteShape(original,"source",names[action],time);
                    Shape prep=WriteShape(ready,"prepared",names[action],time);
                    Transform pivot=ready.transform.Find("ScalePivot");
                    int offset=0;
                    var r1=original.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach(var skin in ready.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var sourceSkin=r1.Single(r=>r.name==skin.name);
                        var a=BakedVertices(original.transform,sourceSkin);
                        var b=BakedVertices(ready.transform,skin);
                        var linear=LinearVertices(ready.transform,skin);
                        float expectedError=0,vatError=0,linearError=0;
                        for(int i=0;i<b.Length;i++)
                        {
                            Vector3 expected=pivot.localPosition + pivot.localRotation*Vector3.Scale(pivot.localScale,a[i]);
                            int p=(windows[action].startFrame+local)*profile.rowsPerFrame*profile.textureWidth+offset+i;
                            Vector3 stored=new Vector3(vat[p].r,vat[p].g,vat[p].b);
                            expectedError=Mathf.Max(expectedError,Vector3.Distance(expected,b[i]));
                            vatError=Mathf.Max(vatError,Vector3.Distance(stored,b[i]));
                            linearError=Mathf.Max(linearError,Vector3.Distance(stored,linear[i]));
                        }
                        report.differences.Add(new Difference{action=names[action],part=skin.name,sourceScaledVsPrepared=expectedError,preparedVsVat=vatError,linearSkinVsVat=linearError});
                        offset+=b.Length;
                    }
                }
                finally{Object.DestroyImmediate(original);Object.DestroyImmediate(ready);}
            }
            File.WriteAllText(Output+"/comparison.json",JsonUtility.ToJson(report,true));
            Debug.Log("KNIGHT_NECK_PROBE_COMPLETE");
        }
        static Shape WriteShape(GameObject root,string stage,string action,float time)
        {
            var s=new Shape{stage=stage,action=action,time=time};
            var vertices=new List<Vector3>();var linear=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Vector3[] p=BakedVertices(root.transform,skin),reference=LinearVertices(root.transform,skin);
                var bounds=new Bounds(p[0],Vector3.zero);float error=0;
                for(int i=0;i<p.Length;i++){bounds.Encapsulate(p[i]);error=Mathf.Max(error,Vector3.Distance(p[i],reference[i]));}
                s.parts.Add(new Part{name=skin.name,min=bounds.min,max=bounds.max,bakeVsLinearSkinMaxError=error});
                triangles.AddRange(skin.sharedMesh.triangles.Select(i=>i+vertices.Count));vertices.AddRange(p);linear.AddRange(reference);uv.AddRange(skin.sharedMesh.uv);
            }
            s.vertices=vertices.ToArray();s.linearVertices=linear.ToArray();s.uv=uv.ToArray();s.triangles=triangles.ToArray();
            File.WriteAllText(Output+"/"+stage+"-"+action+".json",JsonUtility.ToJson(s));return s;
        }
        static Vector3[] BakedVertices(Transform root,SkinnedMeshRenderer skin)
        {
            var mesh=new Mesh();try{skin.BakeMesh(mesh,false);var matrix=root.worldToLocalMatrix*skin.transform.localToWorldMatrix;return mesh.vertices.Select(v=>matrix.MultiplyPoint3x4(v)).ToArray();}finally{Object.DestroyImmediate(mesh);}
        }
        internal static Vector3[] LinearVertices(Transform root,SkinnedMeshRenderer skin)
        {
            var mesh=skin.sharedMesh;var p=mesh.vertices;var w=mesh.boneWeights;var bind=mesh.bindposes;
            var matrices=skin.bones.Select((b,i)=>root.worldToLocalMatrix*b.localToWorldMatrix*bind[i]).ToArray();
            var result=new Vector3[p.Length];
            for(int i=0;i<p.Length;i++)result[i]=matrices[w[i].boneIndex0].MultiplyPoint3x4(p[i])*w[i].weight0+matrices[w[i].boneIndex1].MultiplyPoint3x4(p[i])*w[i].weight1+matrices[w[i].boneIndex2].MultiplyPoint3x4(p[i])*w[i].weight2+matrices[w[i].boneIndex3].MultiplyPoint3x4(p[i])*w[i].weight3;
            return result;
        }
    }
}
