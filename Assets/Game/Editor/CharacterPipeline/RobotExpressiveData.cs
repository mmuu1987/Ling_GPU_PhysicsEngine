using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    // Intentionally a pinned ONE-MODEL adapter, not a general glTF importer. The original GLB is retained.
    [Serializable] internal sealed class RobotExpressiveData
    {
        public int schemaVersion;
        public string sourceSha256;
        public int[] roots;
        public Node[] nodes;
        public Geometry[] meshes;
        public Skin[] skins;
        public Surface[] materials;
        public Motion[] animations;
        [Serializable] internal sealed class Node { public string name; public int parent,mesh,skin; public int[] children; public float[] translation,rotation,scale,matrix; }
        [Serializable] internal sealed class Geometry { public string name; public Primitive[] primitives; }
        [Serializable] internal sealed class Primitive { public int material; public float[] positions,normals,weights; public int[] indices,joints; public Morph[] targets; }
        [Serializable] internal sealed class Morph { public float[] positions,normals; }
        [Serializable] internal sealed class Skin { public int skeleton; public int[] joints; public float[] inverseBindMatrices; }
        [Serializable] internal sealed class Surface { public string name; public float[] color; public float metallic,roughness; }
        [Serializable] internal sealed class Motion { public string name; public float length; public Sampler[] samplers; public Channel[] channels; }
        [Serializable] internal sealed class Sampler { public float[] times,values; public string interpolation; }
        [Serializable] internal sealed class Channel { public int node,sampler; public string path; }

        internal static Vector3 V(float[] x,int offset=0) => new Vector3(-x[offset],x[offset+1],x[offset+2]);
        internal static Quaternion Q(float[] x,int offset=0) => new Quaternion(x[offset],-x[offset+1],-x[offset+2],x[offset+3]).normalized;
        internal static Matrix4x4 M(float[] x,int offset)
        {
            var m=new Matrix4x4();for(int i=0;i<16;i++)m[i]=x[offset+i];
            var reflection=Matrix4x4.Scale(new Vector3(-1,1,1));return reflection*m*reflection;
        }
        internal string Name(int index) => "N"+index.ToString("D3")+"_"+Regex.Replace(nodes[index].name,"[^A-Za-z0-9_]","_");
        internal Motion Clip(string name) => animations.Single(a=>a.name==name);
        internal int[] MeshOrder()
        {
            var result=new List<int>();
            void Walk(int n){if(nodes[n].mesh>=0)result.Add(n);foreach(int c in nodes[n].children)Walk(c);}
            foreach(int root in roots)Walk(root);return result.ToArray();
        }
        internal float[] Sample(Sampler s,float time,int components,bool quaternion=false)
        {
            if(s.interpolation!="LINEAR")throw new InvalidOperationException("Pinned robot requires LINEAR samplers.");
            int i=0;while(i+1<s.times.Length && s.times[i+1]<=time)i++;
            int j=Math.Min(i+1,s.times.Length-1);float f=i==j?0:Mathf.Clamp01((time-s.times[i])/(s.times[j]-s.times[i]));
            var result=new float[components];
            if(quaternion)
            {
                var a=new Quaternion(s.values[i*4],s.values[i*4+1],s.values[i*4+2],s.values[i*4+3]);
                var b=new Quaternion(s.values[j*4],s.values[j*4+1],s.values[j*4+2],s.values[j*4+3]);
                var q=Quaternion.Slerp(a,b,f);result[0]=q.x;result[1]=q.y;result[2]=q.z;result[3]=q.w;
            }
            else for(int c=0;c<components;c++)result[c]=Mathf.LerpUnclamped(s.values[i*components+c],s.values[j*components+c],f);
            return result;
        }
        internal Matrix4x4[] Worlds(Motion motion,float time)
        {
            var p=nodes.Select(n=>V(n.translation)).ToArray();var q=nodes.Select(n=>Q(n.rotation)).ToArray();
            var scales=nodes.Select(n=>new Vector3(n.scale[0],n.scale[1],n.scale[2])).ToArray();
            if(motion!=null)foreach(var c in motion.channels)
            {
                var s=motion.samplers[c.sampler];
                if(c.path=="translation")p[c.node]=V(Sample(s,time,3));
                else if(c.path=="rotation")q[c.node]=Q(Sample(s,time,4,true));
                else if(c.path=="scale"){var v=Sample(s,time,3);scales[c.node]=new Vector3(v[0],v[1],v[2]);}
                else if(c.path!="weights")throw new InvalidOperationException("Unsupported pinned channel "+c.path);
            }
            var world=new Matrix4x4[nodes.Length];var done=new bool[nodes.Length];var visiting=new bool[nodes.Length];
            Matrix4x4 Get(int n)
            {
                if(done[n])return world[n];if(visiting[n])throw new InvalidOperationException("Cycle");visiting[n]=true;
                var local=Matrix4x4.TRS(p[n],q[n],scales[n]);world[n]=nodes[n].parent<0?local:Get(nodes[n].parent)*local;done[n]=true;visiting[n]=false;return world[n];
            }
            for(int i=0;i<nodes.Length;i++)Get(i);return world;
        }
        internal Vector3[] Positions(Motion motion,float time)
        {
            var world=Worlds(motion,time);var output=new List<Vector3>();
            foreach(int ni in MeshOrder())
            {
                var node=nodes[ni];var geometry=meshes[node.mesh];var morph=motion?.channels.SingleOrDefault(c=>c.node==ni&&c.path=="weights");
                float[] shape=morph==null?null:Sample(motion.samplers[morph.sampler],time,geometry.primitives[0].targets.Length);
                var skin=node.skin<0?null:skins[node.skin];
                foreach(var part in geometry.primitives)
                {
                    for(int v=0;v<part.positions.Length/3;v++)
                    {
                        var p=V(part.positions,v*3);
                        if(shape!=null)for(int s=0;s<shape.Length;s++)if(part.targets[s].positions.Length>0)p+=V(part.targets[s].positions,v*3)*shape[s];
                        if(skin==null)p=world[ni].MultiplyPoint3x4(p);
                        else
                        {
                            var result=Vector3.zero;
                            // Match the originating Three.js r160 loader's normalizeSkinWeights contract.
                            float weightSum=0;for(int j=0;j<4;j++)weightSum+=Mathf.Abs(part.weights[v*4+j]);
                            CharacterGeometry.Require(weightSum>0&&float.IsFinite(weightSum),"Invalid pinned hand skin weights");
                            for(int j=0;j<4;j++)
                            {
                                float w=part.weights[v*4+j]/weightSum;if(w==0)continue;int joint=part.joints[v*4+j];
                                result+=(world[skin.joints[joint]]*M(skin.inverseBindMatrices,joint*16)).MultiplyPoint3x4(p)*w;
                            }
                            p=result;
                        }
                        output.Add(p);
                    }
                }
            }
            return output.ToArray();
        }
        internal void RequireNeutralBattleFace()
        {
            foreach(string name in new[]{"Idle","Walking","Punch","Death"})
                foreach(var c in Clip(name).channels.Where(c=>c.path=="weights"))
                {
                    var s=Clip(name).samplers[c.sampler];
                    CharacterGeometry.Require(s.interpolation=="LINEAR"&&s.values.All(v=>v==0),"Nonzero face expression: cannot silently neutralize "+name);
                }
        }
        internal GameObject InstantiateNative(string folder)
        {
            var root=new GameObject("RobotExpressiveNative");var transforms=new Transform[nodes.Length];
            for(int i=0;i<nodes.Length;i++)transforms[i]=new GameObject(Name(i)).transform;
            for(int i=0;i<nodes.Length;i++)
            {
                var n=nodes[i];var t=transforms[i];t.SetParent(n.parent<0?root.transform:transforms[n.parent],false);
                t.localPosition=V(n.translation);t.localRotation=Q(n.rotation);t.localScale=new Vector3(n.scale[0],n.scale[1],n.scale[2]);
                if(n.skin>=0)
                {
                    // glTF skin matrices produce model-space vertices: this skin node's own TRS is not applied again.
                    // The pinned hands are direct children of the identity scene root and have no animated node TRS.
                    CharacterGeometry.Require(n.parent==0&&!animations.Any(a=>a.channels.Any(c=>c.node==i)),"Unsupported animated/transformed skin-node parent");
                    t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;
                }
            }
            var materialsOwned=new Material[materials.Length];
            for(int i=0;i<materials.Length;i++)
            {
                var s=materials[i];var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Source_"+i+"_"+s.name};
                m.color=new Color(s.color[0],s.color[1],s.color[2],s.color[3]);m.SetFloat("_Metallic",s.metallic);m.SetFloat("_Smoothness",1-s.roughness);
                AssetDatabase.CreateAsset(m,folder+"/Material"+i+".mat");materialsOwned[i]=m;
            }
            foreach(int ni in MeshOrder())
            {
                var node=nodes[ni];var parts=meshes[node.mesh].primitives;
                var p=new List<Vector3>();var n=new List<Vector3>();var weights=new List<BoneWeight>();var triangles=new List<int[]>();
                int shapeCount=parts[0].targets.Length;
                var shapeP=Enumerable.Range(0,shapeCount).Select(_=>new List<Vector3>()).ToArray();
                var shapeN=Enumerable.Range(0,shapeCount).Select(_=>new List<Vector3>()).ToArray();
                foreach(var part in parts)
                {
                    CharacterGeometry.Require(part.targets.Length==shapeCount,"Morph count differs across submeshes");
                    int offset=p.Count;
                    for(int v=0;v<part.positions.Length/3;v++)
                    {
                        p.Add(V(part.positions,v*3));n.Add(V(part.normals,v*3));
                        if(node.skin>=0)
                        {
                            float sum=0;for(int j=0;j<4;j++){float w=part.weights[v*4+j];CharacterGeometry.Require(w>=0&&float.IsFinite(w),"Invalid pinned weight");sum+=Mathf.Abs(w);}
                            CharacterGeometry.Require(sum>0,"Zero pinned hand weight sum");
                            weights.Add(new BoneWeight{boneIndex0=part.joints[v*4],boneIndex1=part.joints[v*4+1],boneIndex2=part.joints[v*4+2],boneIndex3=part.joints[v*4+3],weight0=part.weights[v*4]/sum,weight1=part.weights[v*4+1]/sum,weight2=part.weights[v*4+2]/sum,weight3=part.weights[v*4+3]/sum});
                        }
                        for(int s=0;s<shapeCount;s++)
                        {
                            shapeP[s].Add(part.targets[s].positions.Length==0?Vector3.zero:V(part.targets[s].positions,v*3));
                            shapeN[s].Add(part.targets[s].normals.Length==0?Vector3.zero:V(part.targets[s].normals,v*3));
                        }
                    }
                    var indices=part.indices.Select(v=>v+offset).ToArray();for(int i=0;i<indices.Length;i+=3)(indices[i+1],indices[i+2])=(indices[i+2],indices[i+1]);triangles.Add(indices);
                }
                var mesh=new Mesh{name=Name(ni)};mesh.SetVertices(p);mesh.SetNormals(n);mesh.subMeshCount=triangles.Count;
                for(int i=0;i<triangles.Count;i++)mesh.SetTriangles(triangles[i],i);
                if(node.skin>=0){mesh.boneWeights=weights.ToArray();mesh.bindposes=Enumerable.Range(0,skins[node.skin].joints.Length).Select(j=>M(skins[node.skin].inverseBindMatrices,j*16)).ToArray();}
                for(int s=0;s<shapeCount;s++)mesh.AddBlendShapeFrame("SourceMorph"+s,100,shapeP[s].ToArray(),shapeN[s].ToArray(),null);
                mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/Mesh"+ni+".asset");
                Renderer renderer;
                if(node.skin>=0||shapeCount>0)
                {
                    var skin=transforms[ni].gameObject.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;
                    if(node.skin>=0){skin.bones=skins[node.skin].joints.Select(j=>transforms[j]).ToArray();if(skins[node.skin].skeleton>=0)skin.rootBone=transforms[skins[node.skin].skeleton];}
                    skin.updateWhenOffscreen=true;renderer=skin;
                }
                else{transforms[ni].gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=transforms[ni].gameObject.AddComponent<MeshRenderer>();}
                renderer.sharedMaterials=parts.Select(part=>materialsOwned[part.material]).ToArray();renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return root;
        }
        internal AnimationClip NativeClip(Motion motion,GameObject root)
        {
            var clip=new AnimationClip{name=motion.name,frameRate=24};
            foreach(var c in motion.channels)
            {
                var sampler=motion.samplers[c.sampler];int components=c.path=="rotation"?4:c.path=="weights"?meshes[nodes[c.node].mesh].primitives[0].targets.Length:3;
                var times=new SortedSet<float>(sampler.times);
                // Sample slerp densely; retain authored knots AND the death endpoint-inclusive VAT times.
                for(int f=0;f<=Mathf.CeilToInt(motion.length*120);f++)times.Add(Mathf.Min(motion.length,f/120f));
                int count=Mathf.Max(1,Mathf.CeilToInt(motion.length*24));if(motion.name=="Death"&&count>1)for(int f=0;f<count;f++)times.Add(motion.length*f/(count-1));
                var curves=Enumerable.Range(0,components).Select(_=>new List<Keyframe>()).ToArray();
                foreach(float time in times)
                {
                    var v=Sample(sampler,time,components,c.path=="rotation");
                    if(c.path=="translation")v[0]=-v[0];if(c.path=="rotation"){v[1]=-v[1];v[2]=-v[2];}
                    if(c.path=="weights")for(int j=0;j<v.Length;j++)v[j]*=100;
                    for(int j=0;j<components;j++)curves[j].Add(new Keyframe(time,v[j]));
                }
                var target=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==Name(c.node));string path=AnimationUtility.CalculateTransformPath(target,root.transform);
                for(int j=0;j<components;j++)
                {
                    string property=c.path=="weights"?"blendShape.SourceMorph"+j:(c.path=="translation"?"m_LocalPosition.":c.path=="rotation"?"m_LocalRotation.":"m_LocalScale.")+"xyzw"[j];
                    SetLinear(clip,path,c.path=="weights"?typeof(SkinnedMeshRenderer):typeof(Transform),property,curves[j].ToArray());
                }
            }
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=motion.name!="Death";AnimationUtility.SetAnimationClipSettings(clip,settings);return clip;
        }
        internal static void SetLinear(AnimationClip clip,string path,Type type,string property,Keyframe[] keys)
        {
            var curve=new AnimationCurve(keys);for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,type,property),curve);
        }
    }
}
