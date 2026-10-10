Shader "MassEngine/MemberSelectionRings"
{
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 4.5
   #include "UnityCG.cginc"
   #include "MemberSelectionBuffers.hlsl"
   StructuredBuffer<AgentData> _Agents;
   StructuredBuffer<uint> _SelectedIndices;
   StructuredBuffer<int> _Hp,_Teams,_Types;
   StructuredBuffer<UnitTypeSettings> _Settings;
   float4 _Color;int _Team;
   struct Input {float4 vertex:POSITION;uint instance:SV_InstanceID;};
   struct Output {float4 pos:SV_POSITION;float alive:TEXCOORD0;};
   Output vert(Input v)
   {
    uint i=_SelectedIndices[v.instance];AgentData a=_Agents[i];
    float radius=max(.25,_Settings[_Types[i]].agentRadius*max(1,max(abs(a.scale.x),abs(a.scale.z))))+.08;
    float3 world=a.position+float3(v.vertex.x*radius,.06,v.vertex.z*radius);
    Output o;o.pos=mul(UNITY_MATRIX_VP,float4(world,1));o.alive=(_Hp[i]>0&&_Teams[i]==_Team)?1:0;return o;
   }
   float4 frag(Output i):SV_Target {clip(i.alive-.5);return _Color;}
   ENDHLSL
  }
 }
}
