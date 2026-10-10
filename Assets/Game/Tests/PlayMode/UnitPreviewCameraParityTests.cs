#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace MassEngine.Game.Tests
{
    public sealed class UnitPreviewCameraParityTests
    {
        public static Color32[] Capture(RenderTexture rt, string name, bool flip)
        {
            var old = RenderTexture.active; var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt; image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); var p = image.GetPixels32();
                if (flip) for(int y=0;y<rt.height/2;y++) for(int x=0;x<rt.width;x++)
                { int a=y*rt.width+x,b=(rt.height-1-y)*rt.width+x; var c=p[a];p[a]=p[b];p[b]=c; }
                image.SetPixels32(p); image.Apply();
                var output = Environment.GetEnvironmentVariable("TOY_UI_OUTPUT");
                if (!string.IsNullOrEmpty(output)) File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
                return p;
            }
            finally { RenderTexture.active=old;Object.DestroyImmediate(image); }
        }
        // A real Unity Camera controls view/projection/depth/culling. This is deliberately
        // independent of the preview CommandBuffer setup; it renders the same VAT mesh.
        public static Color32[] StandardCamera(UnitTypeConfig config, float distance, string name, float time=0, Vector3? frameCenter=null)
        {
            var runtime = ResolvedUnitTypeRuntime.Resolve(config,1); var mesh = runtime.nearMesh;
            VatProfileReader.TryRead(config.renderConfig.vatProfile,out var profile,out _);
            var go=new GameObject("Independent camera reference"); var camera=go.AddComponent<Camera>();
            var lightGo=new GameObject("Reference key light"); var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.8f;light.shadows=LightShadows.None;
            light.transform.rotation=Quaternion.LookRotation(-new Vector3(.3f,.8f,-.6f).normalized);
            var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.55f,.55f);
            var target=new RenderTexture(320,320,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();
            var agents=new ComputeBuffer(1,Marshal.SizeOf<AgentData>(),ComputeBufferType.Structured);var visible=new ComputeBuffer(1,4);var args=new ComputeBuffer(5,4,ComputeBufferType.IndirectArguments);
            try
            {
                camera.enabled=false;camera.orthographic=false;camera.fieldOfView=30;camera.aspect=1;camera.nearClipPlane=.01f;camera.farClipPlane=distance+20;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.cullingMask=1<<30;camera.allowMSAA=false;camera.allowHDR=false;camera.targetTexture=target;
                Vector3 center=frameCenter ?? Vector3.zero;
                camera.transform.position=center+Quaternion.Euler(28,152,0)*new Vector3(0,0,-distance);camera.transform.LookAt(center);
                agents.SetData(new[]{new AgentData{scale=Vector3.one,currentState=0,presentationState=0,currentAnimationTime=time}});visible.SetData(new uint[]{0});args.SetData(new[]{mesh.GetIndexCount(0),1u,mesh.GetIndexStart(0),(uint)mesh.GetBaseVertex(0),0u});
                var block=runtime.nearBlock;block.SetBuffer("agentBuffer",agents);block.SetBuffer("visibleAgentIndices",visible);block.SetVector("_MassCorpseSink",Vector4.zero);
                Graphics.DrawMeshInstancedIndirect(mesh,0,runtime.nearMaterial,new Bounds(center,Vector3.one*100),args,0,block,ShadowCastingMode.Off,false,30,camera,LightProbeUsage.Off);
                camera.Render();return Capture(target,name,false);
            }
            finally
            {
                agents.Release();visible.Release();args.Release();Object.DestroyImmediate(go);Object.DestroyImmediate(lightGo);target.Release();Object.DestroyImmediate(target);RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;
            }
        }
        [TestCase("roster-male")]
        [TestCase("roster-knight")]
        [TestCase("roster-skeleton-rogue")]
        [TestCase("roster-ranger")]
        public void ReportedCharactersMatchStandardCameraAtTheDefaultView(string id)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset");
            var entry=catalog.FindTemplate(id);Assert.That(entry,Is.Not.Null,id);
            bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                using(var preview=new UnitModelPreviewRenderer(entry.config))
                {
                    preview.RadiusRingEnabled=false; preview.ShadowEnabled=false;float time=preview.IdleDuration*.15f;preview.Render(time,320,320);
                    var frame=(Bounds)typeof(UnitModelPreviewRenderer).GetField("bounds",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(preview);
                    var actual=Capture(preview.Texture,"character-preview-"+id,false);
                    var reference=StandardCamera(entry.config,preview.CameraDistance,"character-camera-"+id,time,frame.center);
                    int union=reference.Where((p,i)=>p.a>127||actual[i].a>127).Count();
                    int intersection=reference.Where((p,i)=>p.a>127&&actual[i].a>127).Count();
                    Assert.That(union,Is.GreaterThan(100));Assert.That((float)intersection/union,Is.GreaterThan(.99f),id+" silhouette/placement differs from Camera");
                    // Lighting can differ between the isolated studio and scene Camera.
                    // Compare normalized colour to catch a different surface/atlas region winning depth.
                    float error=0;int n=0;
                    for(int i=0;i<reference.Length;i++)
                    {
                        var a=actual[i];var b=reference[i];if(a.a<128||b.a<128)continue;
                        float sa=a.r+a.g+a.b,sb=b.r+b.g+b.b;if(sa<30||sb<30)continue;
                        error+=(Mathf.Abs(a.r/sa-b.r/sb)+Mathf.Abs(a.g/sa-b.g/sb)+Mathf.Abs(a.b/sa-b.b/sb))/3;n++;
                    }
                    float mean=error/Mathf.Max(1,n);Debug.Log($"CHARACTER_CAMERA_PARITY {id} IoU={(float)intersection/union} meanChromaError={mean}");
                    Assert.That(mean,Is.LessThan(.08f),id+" exposed surface colours disagree with Camera");
                }
            }
            finally{ShaderUtil.allowAsyncCompilation=async;}
        }
        [TestCase("VatInstancedNoShadow",false)]
        [TestCase("VatInstancedNoShadow",true)]
        [TestCase("LitInstancedAgent",false)]
        [TestCase("LitInstancedAgent",true)]
        public void NearGreenSurfaceOccludesFarRedInBothTriangleOrders(string shader,bool reversed)
        {
            bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            var mesh=new Mesh();var material=new Material(Shader.Find("Universal Render Pipeline/MassEngine/"+shader)){enableInstancing=true};
            var pos=new Texture2D(8,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};var norm=new Texture2D(8,1,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};
            var atlas=new Texture2D(2,1,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var profile=ScriptableObject.CreateInstance<VATProfile>();var render=ScriptableObject.CreateInstance<RenderConfig>();var config=ScriptableObject.CreateInstance<UnitTypeConfig>();config.name="Depth fixture";
            try
            {
                var direction=Quaternion.Euler(28,152,0)*Vector3.back;var basis=Quaternion.LookRotation(-direction);var right=basis*Vector3.right;var up=basis*Vector3.up;
                var vertices=new Vector3[8];var ns=new Vector3[8];var uv=new Vector2[8];var corners=new[]{new Vector2(-.7f,-.7f),new Vector2(-.7f,.7f),new Vector2(.7f,.7f),new Vector2(.7f,-.7f)};
                for(int i=0;i<8;i++){vertices[i]=right*corners[i%4].x+up*corners[i%4].y+direction*(i<4?.3f:-.3f);ns[i]=direction;uv[i]=new Vector2(i<4?.25f:.75f,.5f);pos.SetPixel(i,0,new Color(vertices[i].x,vertices[i].y,vertices[i].z,1));norm.SetPixel(i,0,new Color(direction.x,direction.y,direction.z,1));}
                mesh.vertices=vertices;mesh.normals=ns;mesh.uv=uv;mesh.triangles=reversed?new[]{4,5,6,4,6,7,0,1,2,0,2,3}:new[]{0,1,2,0,2,3,4,5,6,4,6,7};mesh.RecalculateBounds();pos.Apply();norm.Apply();atlas.SetPixels(new[]{Color.green,Color.red});atlas.Apply();material.SetTexture("_BaseMap",atlas);material.SetColor("_BaseColor",Color.white);
                profile.cleanMesh=mesh;profile.positionTexture=pos;profile.normalTexture=norm;profile.textureWidth=8;profile.textureHeight=profile.rowsPerFrame=profile.totalFrameCount=profile.frameRate=1;profile.idle=new VATProfile.VATClipWindow{frameCount=1,frameRate=1,loop=true};profile.move=profile.attack=profile.death=profile.idle;
                render.vatProfile=profile;render.nearMesh=mesh;render.nearMaterial=material;config.renderConfig=render;
                using(var preview=new UnitModelPreviewRenderer(config))
                {
                    preview.RadiusRingEnabled=false; preview.ShadowEnabled=false;preview.Render(0,320,320);var actual=Capture(preview.Texture,"depth-preview-"+shader+"-"+reversed,false);
                    var standard=StandardCamera(config,preview.CameraDistance,"depth-camera-"+shader+"-"+reversed);
                    int sr=standard.Count(p=>p.r>80&&p.r>p.g*2),sg=standard.Count(p=>p.g>80&&p.g>p.r*2);
                    int ar=actual.Count(p=>p.r>80&&p.r>p.g*2),ag=actual.Count(p=>p.g>80&&p.g>p.r*2);
                    Debug.Log($"DEPTH_PARITY {shader} reversed={reversed} standard green={sg} red={sr}; preview green={ag} red={ar}");
                    Assert.That(sg,Is.GreaterThan(200),"Real Camera must render the fixture");Assert.That(sr,Is.LessThan(10),"Reference Camera near face occlusion");
                    int union=standard.Where((p,i)=>p.a>127||actual[i].a>127).Count();
                    int intersection=standard.Where((p,i)=>p.a>127&&actual[i].a>127).Count();
                    Assert.That((float)intersection/union,Is.GreaterThan(.99f),"Projection silhouette must match the standard camera");
                    Assert.That(ag,Is.GreaterThan(200),"Preview must show near green, not far red");Assert.That(ar,Is.LessThan(10),"Far face must never overwrite near face, independent of triangle order");
                }
            }
            finally{foreach(var o in new Object[]{mesh,material,pos,norm,atlas,profile,render,config})Object.DestroyImmediate(o);ShaderUtil.allowAsyncCompilation=async;}
        }
    }
}
#endif

