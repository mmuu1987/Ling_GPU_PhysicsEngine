Shader "MassEngine/Season22 Ground" {
 Properties {
  _Grass("Painted moss and grass",2D)="white"{} _Road("Worn earth",2D)="white"{} _Rock("Stone",2D)="white"{}
  _SurfaceNormals("World normals",2D)="gray"{} _DetailMask("Forest floor mask",2D)="black"{}
  _Season("Season",Float)=0
  _GrassTint("Grass tint",Color)=(.9,1,.83,1) _RoadTint("Road tint",Color)=(1,.95,.82,1)
  _HazeColor("Haze linear",Vector)=(.38,.57,.62,1) _HazeStart("Haze start",Float)=420 _HazeEnd("Haze end",Float)=1150
 }
 SubShader {Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Pass {Tags{"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_SurfaceNormals);SAMPLER(sampler_SurfaceNormals);TEXTURE2D(_DetailMask);SAMPLER(sampler_DetailMask);
 TEXTURE2D(_Grass);SAMPLER(sampler_Grass);TEXTURE2D(_Road);SAMPLER(sampler_Road);TEXTURE2D(_Rock);SAMPLER(sampler_Rock);
 CBUFFER_START(UnityPerMaterial)
 float4 _GrassTint,_RoadTint,_HazeColor;float _HazeStart,_HazeEnd,_Season;
 CBUFFER_END
 struct A{float3 p:POSITION;float3 n:NORMAL;};struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;};
 V vert(A i){V o;o.world=TransformObjectToWorld(i.p);o.p=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.n);return o;}
 float hash21(float2 p){p=frac(p*float2(.1031,.11369));p+=dot(p,p.yx+19.19);return frac((p.x+p.y)*p.x);}
 float fieldNoise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);}
 half4 frag(V i):SV_Target{
  float2 p=i.world.xz;half3 normal=normalize(SAMPLE_TEXTURE2D_LOD(_SurfaceNormals,sampler_SurfaceNormals,(p+768)/1536,0).rgb*2-1);normal=normalize(lerp(normal,normalize(i.normal),smoothstep(650,750,max(abs(p.x),abs(p.y)))));half3 detail=SAMPLE_TEXTURE2D_LOD(_DetailMask,sampler_DetailMask,(p+640)/1280,0).rgb;float mip=clamp(log2(max(1,length(i.world-_WorldSpaceCameraPos)*.08)),0,8);float broad=fieldNoise(p*.021+17),fine=fieldNoise(p*.17+43);
  half3 grass=SAMPLE_TEXTURE2D_LOD(_Grass,sampler_Grass,p*.12,mip).rgb;
  grass=lerp(grass,SAMPLE_TEXTURE2D_LOD(_Grass,sampler_Grass,float2(-p.y,p.x)*.037+3.71,max(0,mip-1.7)).rgb,.32);
  float grassValue=saturate(dot(grass,half3(.3,.6,.1))*2.4);grass=lerp(grass,lerp(half3(.14,.205,.07),half3(.30,.36,.15),grassValue),.57);
  grass*=.83+.34*SAMPLE_TEXTURE2D_LOD(_Grass,sampler_Grass,p*.38+7,mip+1.7).g;
  if(_Season>.5&&_Season<1.5)grass=lerp(grass,half3(.30,.265,.10)*(.70+.48*grassValue),.72);
  if(_Season>1.5)grass=lerp(grass,half3(.16,.205,.19),.72);
  grass*=lerp(half3(.66,.81,.54),half3(1.07,1.04,.80),broad)*_GrassTint.rgb;
  float d1=abs(p.y-16*sin(p.x/54));float d2=abs(p.y-(90+8*sin(p.x/40)));
  float distance=min(d1,d2+4);float width=8+2*sin(p.x*.017)+1.2*(broad-.5);
  float road=1-smoothstep(width-3,width+5+(fine-.5)*5,distance);
  float grooves=exp(-pow((d1-3.6)*.9,2))*.10; // faint worn tracks, not raised geometry
  half3 dirt=SAMPLE_TEXTURE2D_LOD(_Road,sampler_Road,p*.16,max(0,mip-.6)).rgb*_RoadTint.rgb*(.86+.22*broad-grooves);
  float dirtValue=saturate(dot(dirt,half3(.3,.6,.1))*2.2);dirt=lerp(half3(.27,.21,.125),half3(.43,.34,.20),dirtValue);
  grass=lerp(grass,dirt,.18*smoothstep(.60,.85,broad));
  grass=lerp(grass,dirt*half3(.67,.74,.54),detail.r*.60);
  float slope=1-saturate(normal.y);float rim=smoothstep(223,285,max(abs(p.x),abs(p.y)));
  float rocky=saturate(smoothstep(.09,.31,slope)*.75+rim*smoothstep(.45,.85,broad)*.28+detail.g*.45);
  half3 stone=SAMPLE_TEXTURE2D_LOD(_Rock,sampler_Rock,p*.09,max(0,mip-1.4)).rgb*half3(.79,.88,.81);
  half3 albedo=lerp(lerp(grass,stone,rocky),dirt,road*(1-smoothstep(260,420,max(abs(p.x),abs(p.y)))));
  if(_Season>1.5){float snow=(.38+.56*smoothstep(.22,.72,broad+fine*.14))*(1-road*.82)*(1-detail.r*.20);snow*=1-saturate(slope*2);albedo=lerp(albedo,half3(.64,.71,.73)*(.94+.07*fine),snow);}
  albedo*=1-detail.b*(_Season>1.5?.18:.32);
  Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half illumination=.43+.57*saturate(dot(normal,light.direction))*light.shadowAttenuation;
  float haze=smoothstep(_HazeStart,_HazeEnd,length(p));return half4(lerp(albedo*illumination,_HazeColor.rgb,haze),1);
 }
 ENDHLSL
 }}
}
