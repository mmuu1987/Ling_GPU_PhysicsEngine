#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MassEngine.Tests {
 public sealed partial class MassEngineGpuKernelTests {
  [Test] public void P9HostMembershipDefaultShaderSetHasNoPartitions() {
   var property=typeof(MassGpuShaderSet).GetProperty("HasLocalPartitions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   Assert.NotNull(property);Assert.False((bool)property.GetValue(default(MassGpuShaderSet)));
   Assert.False((bool)property.GetValue(MassGpuShaderSet.Find(null,null,null,null,null)));
  }
  [TestCase(0),TestCase(1),TestCase(4096),TestCase(16384)]
  public void P9HostMembershipUniformPreservesAllIds(int count) {
   var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Tests/PlayMode/P9HostMembership.compute");Assert.NotNull(asset);
   var shader=UnityEngine.Object.Instantiate(asset);
   var ids=new int[16384];for(int i=0;i<ids.Length;i++)ids[i]=17000001+3*i;
   uint[] queries={0,1,17000000,17000001,17000002,17000004,17003070,17003073,17012286,17012289,17049147,17049150,17049151,2147483647};
   ComputeBuffer q=null,r=null;
   try {
    q=new ComputeBuffer(queries.Length,4);r=new ComputeBuffer(queries.Length,4);q.SetData(queries);r.SetData(new uint[queries.Length]);
    int k=shader.FindKernel("Probe");shader.SetInts("_LocalPartitionIds",ids);shader.SetInt("_LocalPartitionCount",count);shader.SetBuffer(k,"_Queries",q);shader.SetBuffer(k,"_MembershipResult",r);shader.Dispatch(k,1,1,1);
    var actual=new uint[queries.Length];r.GetData(actual);
    for(int i=0;i<queries.Length;i++)Assert.AreEqual(Array.BinarySearch(ids,0,count,(int)queries[i])>=0?1u:0u,actual[i],"membership count="+count+" query="+queries[i]);
   }finally{q?.Release();r?.Release();UnityEngine.Object.DestroyImmediate(shader);}
  }
 }
}
#endif
