using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MassEngine.Editor;
using Object=UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    internal static class RobotExpressiveCapture
    {
        sealed class Part : IDisposable
        {
            public Mesh mesh;public Matrix4x4 matrix;public Material[] materials;
            public void Dispose(){Object.DestroyImmediate(mesh);foreach(var m in materials)Object.DestroyImmediate(m);}
        }
        internal static void Check(Color32[] pixels)=>CharacterGeometry.Require(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels),out string error),error);
        internal static void Save(string path,int size,Color32[] pixels)
        {
            CharacterGeometry.Require(!File.Exists(path),"Fresh image only: "+path);var t=new Texture2D(size,size,TextureFormat.RGBA32,false);
            try{t.SetPixels32(pixels);t.Apply(false,false);using(var f=new FileStream(path,FileMode.CreateNew)){var b=t.EncodeToPNG();f.Write(b,0,b.Length);}}finally{Object.DestroyImmediate(t);}
        }
        internal static void Native(GameObject source,AnimationClip clip,float time,string path,Vector3 direction)
        {
            var stage=new GameObject("Inactive complete native robot source view");stage.SetActive(false);var model=Object.Instantiate(source,stage.transform,false);
            foreach(var a in model.GetComponentsInChildren<Animator>(true))a.enabled=false;
            var parts=new List<Part>();RenderTexture target=null;Texture2D texture=null;Shader shader=null;
            var old=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;var oldView=Shader.GetGlobalMatrix("unity_MatrixV");var oldProjection=Shader.GetGlobalMatrix("unity_MatrixP");ShaderUtil.allowAsyncCompilation=false;
            try
            {
                clip.SampleAnimation(model,time);
                shader=ShaderUtil.CreateShaderAsset(@"Shader ""Hidden/RobotExpressive/SourceColours"" {
Properties { _Color (""Original solid colour"", Color) = (1,1,1,1) }
SubShader { Tags { ""RenderType""=""Opaque"" } Pass { Cull Off ZWrite On ZTest LEqual
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""UnityCG.cginc""
struct appdata { float4 vertex : POSITION; }; struct v2f { float4 position : SV_POSITION; };
float4 _Color; v2f vert(appdata v) { v2f o; o.position=UnityObjectToClipPos(v.vertex);return o; }
fixed4 frag(v2f i) : SV_Target { return fixed4(_Color.rgb,1); }
ENDCG
} } }");shader.hideFlags=HideFlags.HideAndDontSave;
                CharacterGeometry.Require(shader.isSupported,"Native neutral colour shader unavailable");
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh;if(renderer is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh,false);}else if(renderer is MeshRenderer)mesh=Object.Instantiate(renderer.GetComponent<MeshFilter>().sharedMesh);else throw new InvalidOperationException("Do not omit a renderer");
                    var materials=renderer.sharedMaterials.Take(mesh.subMeshCount).Select(from=>{var m=new Material(shader);m.SetColor("_Color",from.color);return m;}).ToArray();
                    parts.Add(new Part{mesh=mesh,matrix=renderer.localToWorldMatrix,materials=materials});
                }
                var points=parts.SelectMany(p=>p.mesh.vertices.Select(v=>p.matrix.MultiplyPoint3x4(v))).ToArray();var bounds=RobotExpressiveBuilder.Bounds(points);
                float radius=Mathf.Max(.1f,bounds.extents.magnitude)*1.12f;var eye=bounds.center+direction.normalized*radius*4;
                var view=Matrix4x4.Scale(new Vector3(1,1,-1))*Matrix4x4.TRS(eye,Quaternion.LookRotation(bounds.center-eye,Vector3.up),Vector3.one).inverse;
                var projection=GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-radius,radius,-radius,radius,.01f,radius*10+10),true);
                target=new RenderTexture(640,640,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);CharacterGeometry.Require(target.Create(),"Native render target failed");
                using(var cmd=new CommandBuffer())
                {
                    cmd.SetRenderTarget(target);cmd.ClearRenderTarget(true,true,new Color(.04f,.055f,.08f,0));cmd.SetViewProjectionMatrices(view,projection);cmd.SetViewport(new Rect(0,0,640,640));
                    foreach(var p in parts)for(int sub=0;sub<p.mesh.subMeshCount;sub++)cmd.DrawMesh(p.mesh,p.matrix,p.materials[sub],sub,0);Graphics.ExecuteCommandBuffer(cmd);
                }
                RenderTexture.active=target;texture=new Texture2D(640,640,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,640,640),0,0,false);texture.Apply(false,false);var pixels=texture.GetPixels32();
                VatAppearanceRegressionImageChecks.NormalizeReadbackOrientationInPlace(pixels,640,640,SystemInfo.graphicsUVStartsAtTop);Save(path,640,pixels);Check(pixels);CharacterGeometry.Require(!ShaderUtil.ShaderHasError(shader),"Source shader compile failure");
            }
            finally
            {
                foreach(var p in parts)p.Dispose();RenderTexture.active=old;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(texture!=null)Object.DestroyImmediate(texture);if(shader!=null)Object.DestroyImmediate(shader);Object.DestroyImmediate(stage);ShaderUtil.allowAsyncCompilation=async;
                using(var cmd=new CommandBuffer()){cmd.SetViewProjectionMatrices(oldView,oldProjection);Graphics.ExecuteCommandBuffer(cmd);}
            }
        }
    }
}
