#if UNITY_EDITOR
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MassEngine.Tests
{
    public sealed class RangedBallisticsGpuTests
    {
        [TestCase(-9.8f,1f,3f,7f)] [TestCase(0f,1f,3f,7f)] [TestCase(-9.8f,2f,3f,7f)] [TestCase(-9.8f,1f,.3f,7f)]
        [TestCase(-9.8f,1f,3f,20f)]
        public void CpuAndGpuFlightTimeAgreeForLoftDirectScaleAndLifetime(float gravity,float scale,float life,float distance)
        {
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Tests/PlayMode/RangedBallisticsProbe.compute"));
            try
            {
                using (var result=new ComputeBuffer(1,4))
                {
                    var origin=new Vector3(.5f,23+1.3f*scale,1); var aim=new Vector3(distance,24,1);
                    shader.SetVector("originInput",new Vector4(origin.x,origin.y,origin.z,scale));
                    shader.SetVector("aimInput",new Vector4(aim.x,aim.y,aim.z,1));
                    shader.SetVector("parametersInput",new Vector4(12,gravity,life,distance));
                    int k=shader.FindKernel("Probe");shader.SetBuffer(k,"result",result);shader.Dispatch(k,1,1,1);
                    var data=new float[1];result.GetData(data);
                    float expected=ProjectileBallistics.FlightTime(origin,aim,12,gravity,life,ProjectileBallistics.Clearance(1,scale,1,1,distance));
                    Assert.That(data[0],Is.EqualTo(expected).Within(.0001f));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
#endif
