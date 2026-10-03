Shader "Universal Render Pipeline/MassEngine/ProjectileImpact"
{
    // Opt-in splash impact effect (see ProjectileImpactFx). One procedural instance per ring slot:
    // pass 1 = expanding fire ring on the ground sized to the splash radius (shows the damage area),
    // pass 2 = short camera-facing flash at the impact point. Empty/expired slots collapse to nothing.
    Properties
    {
        [HDR] _ImpactRingColor ("Ring Color", Color) = (1.0, 0.5, 0.08, 1)
        [HDR] _ImpactCoreColor ("Core Color", Color) = (0.9, 0.2, 0.03, 1)
        [HDR] _ImpactFlashColor ("Flash Color", Color) = (1.0, 0.72, 0.3, 1)
        _ProjectileImpactDuration ("Duration", Float) = 0.9
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _ImpactRingColor;
        float4 _ImpactCoreColor;
        float4 _ImpactFlashColor;
        float _ProjectileImpactDuration;
    CBUFFER_END
    float _ProjectileImpactNow;

    struct ProjectileImpactData
    {
        float3 position;
        float radius;
        float time;
        float groundY;
        float2 padding;
    };

    #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        StructuredBuffer<ProjectileImpactData> projectileImpactRing;
        static float _ImpactAge01;
    #endif

    void BuildMatrices(float3 centre, float3 xAxis, float3 yAxis)
    {
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            float3 zAxis = normalize(cross(xAxis, yAxis) + 1e-6) * 1e-3;
            unity_ObjectToWorld = float4x4(
                xAxis.x, yAxis.x, zAxis.x, centre.x,
                xAxis.y, yAxis.y, zAxis.y, centre.y,
                xAxis.z, yAxis.z, zAxis.z, centre.z,
                0, 0, 0, 1);
            float lx = max(1e-8, dot(xAxis, xAxis)), ly = max(1e-8, dot(yAxis, yAxis)), lz = max(1e-8, dot(zAxis, zAxis));
            float3 r0 = xAxis / lx, r1 = yAxis / ly, r2 = zAxis / lz;
            unity_WorldToObject = float4x4(
                r0.x, r0.y, r0.z, -dot(r0, centre),
                r1.x, r1.y, r1.z, -dot(r1, centre),
                r2.x, r2.y, r2.z, -dot(r2, centre),
                0, 0, 0, 1);
        #endif
    }

    void setupRing()
    {
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            ProjectileImpactData d = projectileImpactRing[unity_InstanceID];
            float duration = max(0.05, _ProjectileImpactDuration);
            float age = _ProjectileImpactNow - d.time;
            bool alive = d.radius > 0.0 && age >= 0.0 && age < duration;
            float size = alive ? d.radius * 2.0 : 0.0;
            _ImpactAge01 = alive ? age / duration : 1.0;
            BuildMatrices(float3(d.position.x, d.groundY + 0.06, d.position.z), float3(size, 0, 0), float3(0, 0, size));
        #endif
    }

    void setupFlash()
    {
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            ProjectileImpactData d = projectileImpactRing[unity_InstanceID];
            float duration = max(0.05, _ProjectileImpactDuration) * 0.5;
            float age = _ProjectileImpactNow - d.time;
            bool alive = d.radius > 0.0 && age >= 0.0 && age < duration;
            float t = alive ? age / duration : 1.0;
            float size = alive ? d.radius * (0.7 + 0.6 * t) : 0.0;
            _ImpactAge01 = t;
            float3 centre = float3(d.position.x, max(d.position.y, d.groundY + d.radius * 0.25), d.position.z);
            float3 toCam = normalize(_WorldSpaceCameraPos - centre + float3(0, 1e-4, 0));
            float3 right = normalize(cross(float3(0, 1, 0), toCam) + float3(1e-5, 0, 0));
            float3 up = cross(toCam, right);
            BuildMatrices(centre, right * size, up * size);
        #endif
    }

    struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float age : TEXCOORD1; };

    Varyings vert(Attributes input)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        o.uv = input.uv;
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            o.age = _ImpactAge01;
        #else
            o.age = 1.0;
        #endif
        return o;
    }

    half4 fragRing(Varyings i) : SV_Target
    {
        float d = length(i.uv * 2.0 - 1.0);
        float t = saturate(i.age);
        // The disc covers the whole damage area almost immediately (0.6 -> 1.0 of the splash radius in the
        // first ~15 % of the lifetime), so what the player sees matches who gets hit.
        float ringR = lerp(0.6, 1.0, saturate(t * 6.0));
        float ring = exp(-pow((d - ringR) / 0.08, 2.0));
        float fill = (1.0 - smoothstep(ringR - 0.15, ringR, d)) * lerp(0.7, 0.35, saturate(d / max(ringR, 1e-3)));
        float fade = 1.0 - t * t;
        float a = saturate((ring + fill) * fade) * step(d, 1.0);
        float3 rgb = lerp(_ImpactCoreColor.rgb, _ImpactRingColor.rgb, saturate(ring * 1.5 + d * 0.3));
        return half4(rgb, a);
    }

    half4 fragFlash(Varyings i) : SV_Target
    {
        float d = length(i.uv * 2.0 - 1.0);
        float t = saturate(i.age);
        float core = saturate(1.0 - d);
        float a = core * core * (1.0 - t);
        return half4(_ImpactFlashColor.rgb, a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "ProjectileImpactRing"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragRing
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setupRing
            #pragma target 4.5
            ENDHLSL
        }
        Pass
        {
            Name "ProjectileImpactFlash"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFlash
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setupFlash
            #pragma target 4.5
            ENDHLSL
        }
    }
    Fallback Off
}
