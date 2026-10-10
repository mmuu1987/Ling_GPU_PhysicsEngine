Shader "MassEngine/Seasonal Props 22" {
 Properties { _BaseMap("Color",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Cutoff("Cutoff",Range(0,1))=.45 _AlphaClip("Clip",Float)=0 _Cull("Cull",Float)=2 _Season("Season",Float)=0 _Kind("Foliage 1 rock 2 mountain 3",Float)=0 _Far("Far",Float)=0 _Palette("Autumn palette",Float)=0 _MountainGrade("Mountain",Float)=0 _HazeColor("Haze linear",Vector)=(.38,.57,.62,1) }
 SubShader {Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Cull [_Cull]
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor,_BaseMap_ST,_HazeColor;float _Cutoff,_AlphaClip,_Season,_Kind,_Far,_Palette,_MountainGrade;
 CBUFFER_END
 ENDHLSL
 Pass {Tags{"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma target 3.5
 #pragma multi_compile_instancing
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 struct A {float3 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;float2 uv:TEXCOORD2;};
 V vert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;o.w=TransformObjectToWorld(i.p);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
 half4 frag(V i):SV_Target {half4 t=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;if(_AlphaClip>.5)clip(t.a-_Cutoff);half3 n=normalize(i.n);float leaf=smoothstep(.005,.10,t.g-max(t.r,t.b)*1.02);float value=dot(t.rgb,half3(.3,.6,.1));
 if(_MountainGrade>.5){float moss=smoothstep(.03,.14,t.g-t.r);t.rgb=lerp(half3(.17,.22,.23),half3(.19,.30,.22),moss)*(.85+.35*value);}
 if(_Season>.5&&_Season<1.5){if(_Kind>.5&&_Kind<1.5){half3 warm=lerp(half3(.56,.32,.045),half3(.43,.095,.018),_Palette);t.rgb=lerp(t.rgb,warm*(.64+1.15*value),leaf*.96);}if(_Kind>2.5)t.rgb*=half3(1.12,.94,.70);}
 if(_Season>1.5){if(_Kind>.5&&_Kind<1.5)t.rgb=lerp(t.rgb,t.rgb*half3(.55,.78,.85),leaf*.85);float eligibility=(_Kind>1.5)?1:leaf;float patch=.96;float snow=smoothstep(.05,.63,n.y)*eligibility*patch;t.rgb=lerp(t.rgb,half3(.70,.77,.79),snow*.94);}
 Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half illumination=.43+.57*saturate(dot(n,l.direction))*lerp(l.shadowAttenuation,1,_Far);if(_Far>.5)illumination=.46+.54*saturate(dot(n,l.direction));float haze=smoothstep(370,1100,length(i.w.xz));return half4(lerp(t.rgb*illumination,_HazeColor.rgb,haze),1);}
 ENDHLSL
 }
 Pass {Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ZTest LEqual ColorMask 0
 HLSLPROGRAM
 #pragma vertex shadowVert
 #pragma fragment shadowFrag
 #pragma multi_compile_instancing
 #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
 float3 _LightDirection;float3 _LightPosition;
 struct SA {float3 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};struct SV {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
 SV shadowVert(SA i){UNITY_SETUP_INSTANCE_ID(i);SV o;float3 w=TransformObjectToWorld(i.p),n=TransformObjectToWorldNormal(i.n);float3 ld=_LightDirection;
 #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
 ld=normalize(_LightPosition-w);
 #endif
 o.p=TransformWorldToHClip(ApplyShadowBias(w,n,ld));
 #if UNITY_REVERSED_Z
 o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
 #else
 o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
 #endif
 o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
 half4 shadowFrag(SV i):SV_Target {if(_AlphaClip>.5)clip(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a*_BaseColor.a-_Cutoff);return 0;}
 ENDHLSL
 }
 }
}
