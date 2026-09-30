using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>Independent position oracle, not BakeMesh. Deliberately bounded to four-weight Generic meshes.</summary>
    internal static class CharacterGeometry
    {
        internal static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        internal static bool Finite(Vector3 n) => Finite(n.x) && Finite(n.y) && Finite(n.z);
        internal static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        internal static bool Active(Renderer r, Transform root)
        {
            if (!r.enabled) return false;
            for (var t = r.transform; t != null; t = t.parent)
            { if (!t.gameObject.activeSelf) return false; if (t == root) return true; }
            return false;
        }
        internal static Transform SizeRoot(GameObject model, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var t = model.transform.Find(path);
            Require(t != null && t.parent == model.transform, "Size root must be an existing DIRECT child, or empty.");
            return t;
        }
        internal static void CheckHierarchy(GameObject model, string sizeRootPath, string[] blendshapeAttachmentPaths = null)
        {
            Require(model != null, "Prepared model is required.");
            Require(model.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Prepared model must not contain executable MonoBehaviours.");
            Transform pivot = SizeRoot(model, sizeRootPath);
            var morphs = new HashSet<string>(blendshapeAttachmentPaths ?? Array.Empty<string>());
            Require(morphs.Count == (blendshapeAttachmentPaths?.Length ?? 0), "Duplicate morph attachment path.");
            foreach (var path in morphs)
            {
                var node = string.IsNullOrEmpty(path) ? null : model.transform.Find(path);
                var morph = node == null ? null : node.GetComponent<SkinnedMeshRenderer>();
                Require(morph != null && Active(morph,model.transform), "Missing/inactive declared morph attachment: " + path);
                CheckMorph(morph);
            }
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                Vector3 s = t.localScale;
                Require(Finite(s) && s.x > 0 && Mathf.Abs(s.x-s.y)<1e-5f && Mathf.Abs(s.x-s.z)<1e-5f,
                    "Nonuniform/negative/nonfinite scale is unsupported: " + t.name);
                Require(t == pivot || (s-Vector3.one).sqrMagnitude < 1e-8f, "Only the declared size root may carry non-unit scale: " + t.name);
                Require(Finite(t.localPosition), "Nonfinite transform position: " + t.name);
            }
            foreach (var a in model.GetComponentsInChildren<Animator>(true))
                Require(a.avatar == null || !a.avatar.isHuman, "Humanoid retargeting is not supported by this Generic recipe.");
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Require(Active(skin, model.transform), "Disabled/inactive skin must be removed from the prepared model: " + skin.name);
                Require(pivot == null || skin.transform.IsChildOf(pivot), "All renderers must be inside the declared size root.");
                Mesh m = skin.sharedMesh;
                Require(m != null && m.isReadable && m.vertexCount > 0, "Missing/unreadable source mesh: " + skin.name);
                string skinPath = AnimationUtility.CalculateTransformPath(skin.transform,model.transform);
                if (morphs.Contains(skinPath)) { CheckMorph(skin); continue; }
                Require(m.blendShapeCount == 0, "Blendshapes require an explicitly declared bounded attachment: " + skin.name);
                Require(m.bindposes.Length == skin.bones.Length && skin.bones.Length > 0, "Invalid bindposes/bones: " + skin.name);
                foreach (var b in skin.bones) Require(b != null && b.IsChildOf(model.transform), "Missing/external bone: " + skin.name);
                using (var counts = m.GetBonesPerVertex())
                { Require(counts.Length == m.vertexCount, "Missing skin weights."); foreach (byte c in counts) Require(c > 0 && c <= 4, "Only 1–4 weights per vertex are supported."); }
                foreach (var w in m.boneWeights)
                {
                    float[] weights = { w.weight0,w.weight1,w.weight2,w.weight3 };
                    int[] bones = { w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3 };
                    Require(Mathf.Abs(weights.Sum()-1) < .001f, "Weights must sum to one.");
                    for (int i=0;i<4;i++) Require(Finite(weights[i]) && weights[i]>=0 && (weights[i]==0 || (bones[i]>=0 && bones[i]<skin.bones.Length)), "Invalid bone weight/index.");
                }
            }
        }
        internal static MeshRenderer[] Attachments(GameObject model, CharacterRecipe r)
        {
            var found = new List<MeshRenderer>();
            foreach (string path in r.attachmentPaths ?? Array.Empty<string>())
            {
                Require(!string.IsNullOrWhiteSpace(path), "Empty attachment path.");
                var t = model.transform.Find(path); var renderer = t == null ? null : t.GetComponent<MeshRenderer>();
                Require(renderer != null && Active(renderer,model.transform) && !found.Contains(renderer), "Missing/inactive/duplicate attachment: " + path);
                var pivot=SizeRoot(model,r.sizeRootPath);
                Require(pivot==null || t.IsChildOf(pivot), "Attachment lies outside size root: " + path);
                var filter=t.GetComponent<MeshFilter>();
                Require(filter!=null && filter.sharedMesh!=null && filter.sharedMesh.isReadable, "Attachment has no readable mesh: " + path);
                found.Add(renderer);
            }
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                Require(!Active(renderer,model.transform) || found.Contains(renderer), "Enabled rigid renderer is not explicitly declared: " + renderer.name);
            return found.ToArray();
        }
        internal static void CheckClips(CharacterRecipe r)
        {
            foreach (var clip in r.Clips)
            {
                Require(clip != null && clip.length>0 && Finite(clip.length) && !clip.humanMotion, "Four nonempty Generic clips are required.");
                Require(AnimationUtility.GetObjectReferenceCurveBindings(clip).Length==0, "Object/material swap animation is unsupported: " + clip.name);
                var bindings=AnimationUtility.GetCurveBindings(clip);
                Require(bindings.Length>0, "No animation bindings: " + clip.name);
                foreach (var binding in bindings)
                {
                    if (binding.type == typeof(SkinnedMeshRenderer))
                    {
                        Require((r.blendshapeAttachmentPaths ?? Array.Empty<string>()).Contains(binding.path), "Undeclared morph animation binding: " + binding.path);
                        var node = r.model.transform.Find(binding.path); var morph = node == null ? null : node.GetComponent<SkinnedMeshRenderer>();
                        Require(morph != null, "Missing morph animation target."); CheckMorph(morph);
                        Require(binding.propertyName == "blendShape." + morph.sharedMesh.GetBlendShapeName(0), "Only the declared single morph channel is supported.");
                        foreach (var key in AnimationUtility.GetEditorCurve(clip,binding).keys)
                            Require(Finite(key.value) && key.value>=0 && key.value<=100, "Morph keys must be in 0–100 range.");
                        continue;
                    }
                    Require(binding.type==typeof(Transform) && !string.IsNullOrEmpty(binding.path) && r.model.transform.Find(binding.path)!=null,
                        "Unmapped/non-transform/root animation binding: " + clip.name + "/" + binding.path);
                    Require(binding.path!=r.sizeRootPath, "Size root must not be animated.");
                    Require(binding.propertyName.StartsWith("m_LocalPosition.") || binding.propertyName.StartsWith("m_LocalRotation.") || binding.propertyName.StartsWith("localEulerAnglesRaw.") || binding.propertyName.StartsWith("m_LocalScale."), "Unsupported animation channel: " + binding.propertyName);
                    foreach (var key in AnimationUtility.GetEditorCurve(clip,binding).keys)
                    {
                        Require(Finite(key.value), "Nonfinite curve value.");
                        if (binding.propertyName.StartsWith("m_LocalScale."))
                            Require(Mathf.Abs(key.value-1)<1e-5f && key.inTangent==0 && key.outTangent==0, "Animated scale is unsupported: " + binding.path);
                    }
                }
            }
        }
        internal static void CheckMorph(SkinnedMeshRenderer skin)
        {
            var m=skin.sharedMesh;
            Require(m!=null && m.isReadable && skin.bones.Length==0 && m.bindposes.Length==0 && m.blendShapeCount==1,
                "Morph attachment must have no skin bones and exactly one blendshape.");
            Require(m.GetBlendShapeFrameCount(0)==1 && Mathf.Abs(m.GetBlendShapeFrameWeight(0,0)-100)<1e-5f,
                "Morph attachment requires one linear frame at weight100.");
        }
        internal static Vector3[] Skin(Transform root, SkinnedMeshRenderer skin)
        {
            var m=skin.sharedMesh; var input=m.vertices;
            if(m.blendShapeCount>0)
            {
                CheckMorph(skin);
                float weight=skin.GetBlendShapeWeight(0);
                Require(Finite(weight) && weight>=0 && weight<=100, "Sampled morph weight outside supported 0–100 range.");
                var delta=new Vector3[input.Length];m.GetBlendShapeFrameVertices(0,0,delta,null,null);
                var localToRoot=root.worldToLocalMatrix*skin.transform.localToWorldMatrix;
                for(int i=0;i<input.Length;i++) { input[i]=localToRoot.MultiplyPoint3x4(input[i]+delta[i]*(weight/100)); Require(Finite(input[i]),"Nonfinite morph reference position."); }
                return input;
            }
            var weights=m.boneWeights; var bind=m.bindposes;
            var matrices=skin.bones.Select((b,i)=>root.worldToLocalMatrix*b.localToWorldMatrix*bind[i]).ToArray();
            var result=new Vector3[input.Length];
            for(int i=0;i<input.Length;i++)
            {
                var w=weights[i]; var p=input[i]; Vector3 v=Vector3.zero;
                if(w.weight0>0)v+=matrices[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0;
                if(w.weight1>0)v+=matrices[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1;
                if(w.weight2>0)v+=matrices[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2;
                if(w.weight3>0)v+=matrices[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
                Require(Finite(v), "Nonfinite independent skin position.");result[i]=v;
            }
            return result;
        }
        internal static Vector3[] Positions(GameObject model, CharacterRecipe r, bool bodyOnly=false)
        {
            var vertices=new List<Vector3>();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string path=AnimationUtility.CalculateTransformPath(skin.transform,model.transform);
                if(bodyOnly && (r.blendshapeAttachmentPaths ?? Array.Empty<string>()).Contains(path))continue;
                vertices.AddRange(Skin(model.transform,skin));
            }
            if(!bodyOnly)foreach(var rigid in Attachments(model,r))
            {
                var matrix=model.transform.worldToLocalMatrix*rigid.transform.localToWorldMatrix;
                vertices.AddRange(rigid.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>matrix.MultiplyPoint3x4(v)));
            }
            return vertices.ToArray();
        }
        internal static Bounds BoundsOf(Vector3[] vertices)
        {
            Require(vertices.Length>0,"No skinned body vertices.");var b=new Bounds(vertices[0],Vector3.zero);
            foreach(var v in vertices){Require(Finite(v),"Nonfinite position.");b.Encapsulate(v);}return b;
        }
        internal static GameObject Instance(GameObject source, GameObject inactiveParent)
        {
            var go=Object.Instantiate(source,inactiveParent.transform,false);go.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var a in go.GetComponentsInChildren<Animator>(true))a.enabled=false;
            return go;
        }
    }
}
