Shader "MassEngine/Battlefield Backdrop Haze" {
 Properties {
  _BaseColor("Base Color",Color)=(1,1,1,1)
  _VertexTint("Vertex Tint",Float)=1
  _HazeColor("Haze Color Linear",Vector)=(.38,.57,.62,1)
  _HazeStart("Haze Start (world radius)",Float)=190
  _HazeEnd("Haze End (world radius)",Float)=540
 }
 SubShader { Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Pass { Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_HazeColor;float _VertexTint,_HazeStart,_HazeEnd;
 CBUFFER_END
 struct A {float3 p:POSITION;float3 n:NORMAL;half4 c:COLOR;};
 struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;};
 V vert(A i){V o;o.world=TransformObjectToWorld(i.p);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(i.n);o.c=lerp(half4(1,1,1,1),i.c,_VertexTint)*_BaseColor;return o;}
 half4 frag(V i):SV_Target {
  Light l=GetMainLight(TransformWorldToShadowCoord(i.world));
  half lighting=.38+.62*saturate(dot(normalize(i.n),l.direction))*l.shadowAttenuation;
  float t=saturate((length(i.world.xz)-_HazeStart)/max(1,_HazeEnd-_HazeStart));
  t=t*t*(3-2*t);
  return half4(lerp(i.c.rgb*lighting,_HazeColor.rgb,t),1);
 }
 ENDHLSL
 }}
}
