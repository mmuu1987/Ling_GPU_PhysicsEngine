Shader "MassEngine/Terrain Plan B" {
 SubShader { Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Pass { Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float3 p:POSITION;float3 n:NORMAL;half4 c:COLOR;};
 struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;};
 V vert(A i){V o;o.world=TransformObjectToWorld(i.p);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(i.n);o.c=i.c;return o;}
 half4 frag(V i):SV_Target {Light l=GetMainLight(TransformWorldToShadowCoord(i.world));half lighting=.38+.62*saturate(dot(normalize(i.n),l.direction))*l.shadowAttenuation;return half4(i.c.rgb*lighting,1);}
 ENDHLSL
 }}
}
