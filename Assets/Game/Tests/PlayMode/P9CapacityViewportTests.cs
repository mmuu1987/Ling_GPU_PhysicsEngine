#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
namespace MassEngine.Game.Tests {
 public sealed class P9CapacityViewportTests {
  const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;P9CapacityBreakdownTests owner;MassEngineManager m;string output;
  static string F(double x)=>x.ToString("R",CultureInfo.InvariantCulture);
  [UnitySetUp]public IEnumerator Setup(){owner=new P9CapacityBreakdownTests();yield return owner.Setup();m=(MassEngineManager)typeof(P9CapacityBreakdownTests).GetField("m",P).GetValue(owner);output=(string)typeof(P9CapacityBreakdownTests).GetField("output",P).GetValue(owner);yield return (IEnumerator)typeof(P9CapacityBreakdownTests).GetMethod("Install",P).Invoke(owner,new object[]{50000});}
  [UnityTearDown]public IEnumerator Cleanup(){if(owner!=null)yield return owner.Cleanup();}
  void Pose(){var p=new Vector3(0,400,-260);m.cullingCamera.transform.SetPositionAndRotation(p,Quaternion.LookRotation(-p));}
  [Serializable]class CameraRow{public string name,type,scene;public int frames,width,height;public bool primary,targetTexture;}
  [Serializable]class Window{public string phase;public int frames,sceneViews,alive,overflow;public uint[] lodInstances;public List<CameraRow> cameras;}
  [Serializable]class Report{public string scope="Original production simulation and unit materials throughout. Editor SceneView closed only in isolated owned Editor; no LOD/quality/mesh/feature changes. Original -> SceneView closed -> repeated closed (not reversed); 3s warm + 6s samples, same initialized scene. Camera callback counts deduplicated per camera per frame. Editor-only diagnosis, not Player qualification.";public List<Window> windows=new List<Window>();}
  struct Sample{public double wall,gpu,cpu,main,render,present;public bool fresh;}
  [UnityTest,Timeout(300000)]public IEnumerator Capacity01Viewport(){var report=new Report();var cameras=new Dictionary<Camera,CameraRow>();var last=new Dictionary<Camera,int>();Action<Camera> obs=cam=>{if(last.TryGetValue(cam,out int f)&&f==Time.frameCount)return;last[cam]=Time.frameCount;if(!cameras.TryGetValue(cam,out var row)){row=new CameraRow{name=cam.name,type=cam.cameraType.ToString(),scene=cam.gameObject.scene.path,primary=cam==m.cullingCamera,targetTexture=cam.targetTexture!=null,width=cam.pixelWidth,height=cam.pixelHeight};cameras[cam]=row;}row.frames++;};Camera.CameraCallback post=cam=>obs(cam);Action<ScriptableRenderContext,Camera> srp=(ctx,cam)=>obs(cam);Camera.onPostRender+=post;RenderPipelineManager.endCameraRendering+=srp;
   try{foreach(string phase in new[]{"original-editor","sceneview-closed","sceneview-closed-repeat"}){if(phase=="sceneview-closed")foreach(var view in Resources.FindObjectsOfTypeAll<SceneView>())view.Close();var gv=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));gv.Show();gv.Focus();double warm=Time.realtimeSinceStartupAsDouble+3;while(Time.realtimeSinceStartupAsDouble<warm){Pose();yield return null;}cameras.Clear();last.Clear();var raw=new List<Sample>(2048);var timings=new FrameTiming[1];ulong stamp=0;double start=Time.realtimeSinceStartupAsDouble,prev=start;
    while(Time.realtimeSinceStartupAsDouble-start<6){Pose();FrameTimingManager.CaptureFrameTimings();yield return null;double now=Time.realtimeSinceStartupAsDouble;bool fresh=FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].frameStartTimestamp>stamp;var t=timings[0];if(fresh)stamp=t.frameStartTimestamp;raw.Add(new Sample{wall=(now-prev)*1000,gpu=t.gpuFrameTime,cpu=t.cpuFrameTime,main=t.cpuMainThreadFrameTime,render=t.cpuRenderThreadFrameTime,present=t.cpuMainThreadPresentWaitTime,fresh=fresh});prev=now;}
    var lods=new uint[6];for(int i=0;i<2;i++)for(int l=0;l<3;l++){var a=new uint[5];m.Buffers.GetDrawArgsBuffer(i,l).GetData(a);lods[i*3+l]=a[1];}var hp=new int[50000];m.Buffers.combatBuffers.hpReadBuffer.GetData(hp);var stats=new int[4];m.Buffers.spatialHashStatsBuffer.GetData(stats);Assert.AreEqual(50000,hp.Count(x=>x>0));Assert.AreEqual(MassGpuBufferManager.DeviceResetSentinel,stats[3]);Assert.IsNull(m.LocalOrders);Assert.Greater(cameras.Values.Where(x=>x.primary).Sum(x=>x.frames),raw.Count/2);if(phase!="original-editor")Assert.False(cameras.Values.Any(x=>x.type=="SceneView"));Assert.AreEqual(1280,Screen.width);Assert.AreEqual(720,Screen.height);report.windows.Add(new Window{phase=phase,frames=raw.Count,sceneViews=Resources.FindObjectsOfTypeAll<SceneView>().Length,alive=50000,overflow=stats[0],lodInstances=lods,cameras=cameras.Values.ToList()});File.WriteAllLines(Path.Combine(output,phase+".csv"),new[]{"wall_ms,gpu_ms,ft_cpu_ms,ft_main_ms,ft_render_ms,ft_present_wait_ms,fresh"}.Concat(raw.Select(x=>string.Join(",",F(x.wall),x.fresh?F(x.gpu):"",x.fresh?F(x.cpu):"",x.fresh?F(x.main):"",x.fresh?F(x.render):"",x.fresh?F(x.present):"",x.fresh?1:0))));File.WriteAllText(Path.Combine(output,"viewport.json"),JsonUtility.ToJson(report,true));Debug.Log("P9_STAGE viewport "+phase+" frames="+raw.Count+" cameras="+string.Join(";",report.windows.Last().cameras.Select(x=>x.type+":"+x.name+"="+x.frames)));}
   }finally{Camera.onPostRender-=post;RenderPipelineManager.endCameraRendering-=srp;}
  }
  [UnityTest,Timeout(300000)]public IEnumerator Capacity02CpuBinary(){string path=Path.Combine(output,"cpu-capture.raw");string oldLog=UnityEngine.Profiling.Profiler.logFile;bool oldBinary=UnityEngine.Profiling.Profiler.enableBinaryLog,oldEnabled=UnityEngine.Profiling.Profiler.enabled;
   try{UnityEngine.Profiling.Profiler.logFile=path;UnityEngine.Profiling.Profiler.enableBinaryLog=true;UnityEngine.Profiling.Profiler.enabled=true;double stop=Time.realtimeSinceStartupAsDouble+4;while(Time.realtimeSinceStartupAsDouble<stop){Pose();yield return null;}}
   finally{UnityEngine.Profiling.Profiler.enabled=false;UnityEngine.Profiling.Profiler.enableBinaryLog=oldBinary;UnityEngine.Profiling.Profiler.logFile=oldLog;UnityEngine.Profiling.Profiler.enabled=oldEnabled;}
   yield return null;Assert.True(File.Exists(path),"Binary CPU profile absent");Assert.True(ProfilerDriver.LoadProfile(path,false),"Could not load captured binary profile");int first=ProfilerDriver.firstFrameIndex,last=ProfilerDriver.lastFrameIndex;File.WriteAllText(Path.Combine(output,"cpu-capture-status.txt"),"first="+first+";last="+last+";bytes="+new FileInfo(path).Length);var rows=new List<string>{"frame,thread,name,inclusive_ms,calls"};for(int f=first+2;f<=last-2;f++)for(int t=0;t<32;t++){using(var view=ProfilerDriver.GetRawFrameDataView(f,t)){if(!view.valid)break;var sums=new Dictionary<string,(double ms,int n)>();for(int i=0;i<view.sampleCount;i++){string name=view.GetSampleName(i)??"<unnamed-native-sample>";double ms=view.GetSampleTimeMs(i);if(ms<=0)continue;sums.TryGetValue(name,out var old);sums[name]=(old.ms+ms,old.n+1);}foreach(var x in sums.Where(x=>x.Value.ms>=.1).OrderByDescending(x=>x.Value.ms).Take(60))rows.Add(string.Join(",",f,"\""+(view.threadName??"<unnamed-thread>").Replace("\"","\"\"")+"\"","\""+x.Key.Replace("\"","\"\"")+"\"",F(x.Value.ms),x.Value.n));}}File.WriteAllLines(Path.Combine(output,"cpu-profile-inclusive.csv"),rows);Assert.Greater(rows.Count,1,"No CPU frames decoded; retain failure explicitly");}
 }
}
#endif
