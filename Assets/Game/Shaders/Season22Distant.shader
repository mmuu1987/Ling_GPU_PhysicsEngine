Shader "MassEngine/Season22 Distant Foliage" {
 Properties{_Season("Season",Float)=0 _BaseMap("Color texture",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Cutoff("Cutoff",Range(0,1))=.45 _AlphaClip("Alpha clip",Float)=0 _Cull("Cull",Float)=2 _MountainGrade("Mountain grading",Float)=0 _HazeColor("Haze linear",Vector)=(.38,.57,.62,1)}
 SubShader{Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Cull [_Cull] Pass{Tags{"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_HazeColor;float _Cutoff,_AlphaClip,_MountainGrade,_Season;
 CBUFFER_END
 struct A{float3 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;float2 uv:TEXCOORD2;};
 V vert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;o.w=TransformObjectToWorld(i.p);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;return o;}
 half4 frag(V i):SV_Target{half4 t=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;if(_AlphaClip>.5)clip(t.a-_Cutoff);if(_MountainGrade>.5){float moss=smoothstep(.03,.14,t.g-t.r);t.rgb=lerp(half3(.17,.22,.23),half3(.19,.30,.22),moss)*(.85+.35*dot(t.rgb,half3(.3,.6,.1)));}else{t.rgb=lerp(t.rgb,dot(t.rgb,half3(.3,.6,.1)).xxx,.20);}
 if(_MountainGrade>.5&&_Season>.5&&_Season<1.5)t.rgb*=half3(1.2,1.02,.79);
 if(_Season>1.5&&_MountainGrade>.5)t.rgb=lerp(t.rgb,half3(.65,.73,.76),.15+.7*smoothstep(.18,.75,normalize(i.n).y));
 Light l=GetMainLight();half lighting=.46+.54*saturate(dot(normalize(i.n),l.direction));float f=smoothstep(370,1100,length(i.w.xz));return half4(lerp(t.rgb*lighting,_HazeColor.rgb,f),1);}
 ENDHLSL
 }}
}
