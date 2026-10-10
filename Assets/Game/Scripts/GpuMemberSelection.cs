using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassEngine.Game
{
    /// <summary>Opt-in, read-only observer. Entity indices are the stable agentBuffer slots, never LOD instance indices.
    /// One GPU readback pair in flight, latest queued rectangle wins. No synchronous GetData/Wait calls.
    /// Only uint selection masks and two counts cross to CPU, on request / at most 4Hz for liveness.</summary>
    public sealed class GpuMemberSelection : IDisposable
    {
        private static long nextEpoch;
        private readonly MassEngineManager manager; private readonly MassGpuBufferManager buffers;
        private readonly ComputeBuffer identity, mask, visible, counts, args;
        private readonly ComputeShader shader; private readonly int selectKernel,refreshKernel;
        private readonly Material material; private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private readonly Mesh ring; private bool faulted; private bool disposed,queued,inFlight,whole=true;
        private MemberSelectionProjection projection;
        private MemberSelectionState.Ticket flightTicket; private bool flightWhole;
        private AsyncGPUReadbackRequest maskRequest,countRequest; private uint[] maskData,countData;
        private double readbackStarted,nextRefresh;
        private readonly uint[] zeros=new uint[2];
        public MemberSelectionState State {get;}
        public bool IsCurrent=>!disposed&&manager!=null&&manager.Buffers==buffers&&buffers.IsAllocated&&buffers.agentBuffer==identity;
        public int Dispatches {get;private set;} public int ReadbackPairs {get;private set;} public int DrawCalls {get;private set;}
        public long LogicalGpuBytes=>disposed?0:buffers.AgentCount*8L+28;
        public bool Busy=>queued||inFlight;
        public GpuMemberSelection(MassEngineManager manager,int team)
        {
            if(manager==null||manager.Buffers==null||!manager.Buffers.IsAllocated)throw new InvalidOperationException("Initialize the battle before selection");
            if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsAsyncGPUReadback||!SystemInfo.supportsInstancing)throw new NotSupportedException("Compute, async readback and instancing required");
            this.manager=manager;buffers=manager.Buffers;identity=buffers.agentBuffer;
            State=new MemberSelectionState(System.Threading.Interlocked.Increment(ref nextEpoch),team);
            try
            {
                var source=Resources.Load<ComputeShader>("MemberSelection");var ringShader=Resources.Load<Shader>("MemberSelectionRings");
                if(source==null||ringShader==null||!ringShader.isSupported)throw new InvalidOperationException("Selection shaders unavailable");
                shader=UnityEngine.Object.Instantiate(source);shader.hideFlags=HideFlags.HideAndDontSave;
                selectKernel=shader.FindKernel("SelectMembers");refreshKernel=shader.FindKernel("RefreshMembers");
                mask=new ComputeBuffer(buffers.AgentCount,4);mask.SetData(new uint[buffers.AgentCount]);
                visible=new ComputeBuffer(buffers.AgentCount,4,ComputeBufferType.Append);
                counts=new ComputeBuffer(2,4);args=new ComputeBuffer(5,4,ComputeBufferType.IndirectArguments);
                ring=BuildRing();material=new Material(ringShader){hideFlags=HideFlags.HideAndDontSave,enableInstancing=true};
                args.SetData(new uint[]{ring.GetIndexCount(0),0,ring.GetIndexStart(0),(uint)ring.GetBaseVertex(0),0});queued=true;
            }
            catch{Dispose();throw;}
        }
        public bool Request(MemberSelectionProjection captured)
        {
            if(faulted||!IsCurrent){Invalidate("Battle generation changed or selection unavailable");return false;}
            projection=captured;whole=false;State.Begin();queued=true;return true;
        }
        public void Clear(int team)
        {if(disposed)return;State.Clear(team);whole=true;queued=true;}
        public void Invalidate(string reason)
        {if(!disposed){State.Invalidate(reason);queued=false;}}
        public void Tick()
        {
            if(disposed||faulted)return;
            if(!IsCurrent){Invalidate("Battle generation changed");return;}
            try
            {
                if(inFlight)
                {
                    // Copy each result in the frame it becomes available (Unity request data is transient).
                    if(maskData==null&&maskRequest.done){if(maskRequest.hasError)throw new InvalidOperationException("Selection mask readback failed");maskData=maskRequest.GetData<uint>().ToArray();}
                    if(countData==null&&countRequest.done){if(countRequest.hasError)throw new InvalidOperationException("Selection count readback failed");countData=countRequest.GetData<uint>().ToArray();}
                    if(maskData!=null&&countData!=null)
                    {
                        if(State.Current.Equals(flightTicket)&&State.Scope!=MemberSelectionScope.Unavailable)
                        {
                            var members=new List<int>((int)countData[0]);for(int i=0;i<maskData.Length;i++)if(maskData[i]!=0)members.Add(i);
                            if(members.Count!=countData[0])throw new InvalidOperationException("Mask/count mismatch");
                            State.Apply(flightTicket,members.ToArray(),(int)countData[1],flightWhole);
                        }
                        inFlight=false;maskData=countData=null;
                    }
                    else if(Time.realtimeSinceStartupAsDouble-readbackStarted>10)throw new TimeoutException("Selection GPU confirmation timed out");
                }
                if(!inFlight&&State.Scope!=MemberSelectionScope.Unavailable&&(queued||Time.realtimeSinceStartupAsDouble>=nextRefresh))
                {bool replace=queued;queued=false;Dispatch(replace);nextRefresh=Time.realtimeSinceStartupAsDouble+.25;}
            }
            catch(Exception ex){faulted=true;State.Invalidate(ex.Message);queued=false;/* Do not reuse an in-flight resource after a GPU failure. Explicit close/reopen required. */}
        }
        private void Dispatch(bool replace)
        {
            int kernel=replace?selectKernel:refreshKernel;
            shader.SetInt("_Count",buffers.AgentCount);shader.SetInt("_Team",State.Team);shader.SetInt("_Whole",whole?1:0);
            shader.SetMatrix("_View",projection.View);shader.SetMatrix("_VP",projection.ViewProjection);
            var v=projection.Viewport;var b=projection.Box;
            shader.SetVector("_Viewport",new Vector4(v.x,v.y,v.width,v.height));shader.SetVector("_Box",new Vector4(b.xMin,b.yMin,b.xMax,b.yMax));
            shader.SetVector("_Clip",new Vector4(projection.Near,projection.Far,0,0));
            shader.SetBuffer(kernel,"_Agents",identity);shader.SetBuffer(kernel,"_Teams",buffers.combatBuffers.teamIdBuffer);shader.SetBuffer(kernel,"_Hp",buffers.combatBuffers.hpReadBuffer);
            shader.SetBuffer(kernel,"_Mask",mask);shader.SetBuffer(kernel,"_SelectedIndices",visible);shader.SetBuffer(kernel,"_Counts",counts);
            visible.SetCounterValue(0);counts.SetData(zeros);shader.Dispatch(kernel,(buffers.AgentCount+63)/64,1,1);ComputeBuffer.CopyCount(visible,args,4);
            flightTicket=State.Current;flightWhole=whole;maskData=countData=null;
            maskRequest=AsyncGPUReadback.Request(mask);countRequest=AsyncGPUReadback.Request(counts);inFlight=true;readbackStarted=Time.realtimeSinceStartupAsDouble;Dispatches++;ReadbackPairs++;
        }
        public void Draw(Camera camera,int layer=0)
        {
            if(!IsCurrent||camera==null||State.Scope!=MemberSelectionScope.Local||queued)return;
            properties.SetBuffer("_Agents",identity);properties.SetBuffer("_SelectedIndices",visible);
            properties.SetBuffer("_Hp",buffers.combatBuffers.hpReadBuffer);properties.SetBuffer("_Teams",buffers.combatBuffers.teamIdBuffer);properties.SetInt("_Team",State.Team);
            properties.SetBuffer("_Types",buffers.unitTypeIndexBuffer);properties.SetBuffer("_Settings",buffers.unitTypeSettingsBuffer);
            properties.SetColor("_Color",new Color(.15f,1f,.85f,.85f));
            // Bounds enclose the camera's frustum; the GPU still clips individual rings normally.
            Graphics.DrawMeshInstancedIndirect(ring,0,material,new Bounds(camera.transform.position,Vector3.one*Mathf.Max(100,camera.farClipPlane*3)),args,0,properties,ShadowCastingMode.Off,false,layer,camera,LightProbeUsage.Off);DrawCalls++;
        }
        public bool TrySnapshot(out int[] members,out int version)
        {if(!IsCurrent||queued||State.Scope==MemberSelectionScope.Pending){members=Array.Empty<int>();version=State.Version;return false;}return State.TrySnapshot(out members,out version);}
        private static Mesh BuildRing()
        {
            const int n=32;var vertices=new Vector3[n*2];var triangles=new int[n*6];
            for(int i=0;i<n;i++){float a=i*Mathf.PI*2/n;var p=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));vertices[i*2]=p;vertices[i*2+1]=p*.84f;int j=(i+1)%n;int k=i*6;triangles[k]=i*2;triangles[k+1]=j*2;triangles[k+2]=i*2+1;triangles[k+3]=i*2+1;triangles[k+4]=j*2;triangles[k+5]=j*2+1;}
            var mesh=new Mesh{name="P7 batched selection ring",hideFlags=HideFlags.HideAndDontSave};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateBounds();return mesh;
        }
        public void Dispose()
        {if(disposed)return;disposed=true;State?.Invalidate("Selection preview closed");mask?.Release();visible?.Release();counts?.Release();args?.Release();Destroy(shader);Destroy(material);Destroy(ring);}
        private static void Destroy(UnityEngine.Object o){if(o==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
    }
}

