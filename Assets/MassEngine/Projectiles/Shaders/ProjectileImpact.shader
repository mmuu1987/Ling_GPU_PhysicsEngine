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

    float _ProjectileFxEnabled;
    #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
        StructuredBuffer<ProjectileImpactData> projectileImpactRing;
        static float _ImpactAge01;
        // Fireball look (2026-10-16): styled bursts take the shooter's unit-type tint (padding.x = asfloat(shooter + 1)).
        StructuredBuffer<int> projectileAgentUnitType;
        StructuredBuffer<float4> projectileFxByType;
        static float4 _ImpactTint;

        float4 ResolveImpactTint(ProjectileImpactData d)
        {
            int shooter = asint(d.padding.x);
            if (_ProjectileFxEnabled < 0.5 || shooter <= 0)
                return 0;
            uint agentCount, stride, typeCount;
            projectileAgentUnitType.GetDimensions(agentCount, stride);
            projectileFxByType.GetDimensions(typeCount, stride);
            uint agent = (uint)(shooter - 1);
            if (agent >= agentCount)
                return 0;
            int type = projectileAgentUnitType[agent];
            if (type < 0 || (uint)type >= typeCount)
                return 0;
            return projectileFxByType[type];
        }
    #endif

    // Styled bursts live longer than the legacy flash (seconds = duration x these).
    #define STYLED_RING_LIFE 1.6
    #define STYLED_FIRE_LIFE 0.6
    #define STYLED_SMOKE_LIFE 3.0
    #define STYLED_SCORCH_SECONDS 12.0 // ground scorch of a styled impact; fades over the last 4 s

    float Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float ValueNoise(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), e = Hash21(i + float2(1, 1));
        return lerp(lerp(a, b, f.x), lerp(c, e, f.x), f.y);
    }

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
            _ImpactTint = ResolveImpactTint(d);
            bool styled = _ImpactTint.a > 0.0;
            float duration = max(0.05, _ProjectileImpactDuration) * (styled ? STYLED_RING_LIFE : 1.0);
            float age = _ProjectileImpactNow - d.time;
            // Styled: the slot stays drawn for the scorch; age stays in ring lifetimes (> 1 after the shock ring).
            bool alive = d.radius > 0.0 && age >= 0.0 && age < (styled ? max(duration, STYLED_SCORCH_SECONDS) : duration);
            // Styled: the quad covers 1.2 x the splash radius so the shock ring can overshoot the damage edge a little.
            float size = alive ? d.radius * 2.0 * (styled ? 1.2 : 1.0) : 0.0;
            _ImpactAge01 = alive ? age / duration : 1.0;
            BuildMatrices(float3(d.position.x, d.groundY + (styled ? 0.1 : 0.06), d.position.z), float3(size, 0, 0), float3(0, 0, size));
        #endif
    }

    void setupFlash()
    {
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            ProjectileImpactData d = projectileImpactRing[unity_InstanceID];
            _ImpactTint = ResolveImpactTint(d);
            bool styled = _ImpactTint.a > 0.0;
            float duration = max(0.05, _ProjectileImpactDuration) * (styled ? STYLED_FIRE_LIFE : 0.5);
            float age = _ProjectileImpactNow - d.time;
            bool alive = d.radius > 0.0 && age >= 0.0 && age < duration;
            float t = alive ? age / duration : 1.0;
            // Styled: the fire ball swells fast (ease-out) to ~2.4 x the splash radius across, then burns out.
            float grow = 1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t);
            float size = alive ? (styled ? d.radius * (1.0 + 1.0 * grow) : d.radius * (0.7 + 0.6 * t)) : 0.0;
            _ImpactAge01 = t;
            float3 centre = float3(d.position.x, max(d.position.y, d.groundY + d.radius * (styled ? 0.45 + 0.35 * t : 0.25)), d.position.z);
            float3 toCam = normalize(_WorldSpaceCameraPos - centre + float3(0, 1e-4, 0));
            float3 right = normalize(cross(float3(0, 1, 0), toCam) + float3(1e-5, 0, 0));
            float3 up = cross(toCam, right);
            BuildMatrices(centre, right * size, up * size);
        #endif
    }

    void setupSmoke()
    {
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            ProjectileImpactData d = projectileImpactRing[unity_InstanceID];
            _ImpactTint = ResolveImpactTint(d);
            float duration = max(0.05, _ProjectileImpactDuration) * STYLED_SMOKE_LIFE;
            float age = _ProjectileImpactNow - d.time;
            bool alive = _ImpactTint.a > 0.0 && d.radius > 0.0 && age >= 0.0 && age < duration;  // legacy: no smoke
            float t = alive ? age / duration : 1.0;
            float size = alive ? d.radius * (1.4 + 1.4 * sqrt(t)) : 0.0;
            _ImpactAge01 = t;
            float3 centre = float3(d.position.x, max(d.position.y, d.groundY) + d.radius * (0.5 + 1.6 * t), d.position.z);
            float3 toCam = normalize(_WorldSpaceCameraPos - centre + float3(0, 1e-4, 0));
            float3 right = normalize(cross(float3(0, 1, 0), toCam) + float3(1e-5, 0, 0));
            float3 up = cross(toCam, right);
            BuildMatrices(centre, right * size, up * size);
        #endif
    }

    struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
    struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float age : TEXCOORD1; float4 tint : TEXCOORD2; };

    Varyings vert(Attributes input)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        o.uv = input.uv;
        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            o.age = _ImpactAge01;
            o.tint = _ImpactTint;
        #else
            o.age = 1.0;
            o.tint = 0;
        #endif
        return o;
    }

    half4 fragRing(Varyings i) : SV_Target
    {
        float d = length(i.uv * 2.0 - 1.0);
        float t = saturate(i.age);
        if (i.tint.a > 0.5)
        {
            // Styled ground: a bright shock ring racing out to just past the damage edge (1/1.2 of the quad), then a
            // ragged soot scorch (~0.8 x splash radius) with cooling embers that lasts STYLED_SCORCH_SECONDS.
            float ringLife = max(0.05, _ProjectileImpactDuration) * STYLED_RING_LIFE;
            float ageSec = i.age * ringLife;
            float edge = 1.0 / 1.2;
            float2 p = i.uv * 2.0 - 1.0;
            float shockR = edge * (0.15 + 1.0 * (1.0 - (1.0 - t) * (1.0 - t)));
            float shock = exp(-pow((d - shockR) / (0.05 + 0.05 * t), 2.0)) * (1.0 - t);
            float ang = atan2(p.y, p.x);
            float rim = edge * (0.62 + 0.22 * ValueNoise(float2(ang * 2.2 + 7.0, 1.3)) + 0.1 * ValueNoise(float2(ang * 6.0, 4.1)));
            float n = ValueNoise(i.uv * 9.0 + 3.7);
            float blotch = (1.0 - smoothstep(rim * 0.7, rim, d)) * (0.7 + 0.3 * n);
            float fade = 1.0 - smoothstep(STYLED_SCORCH_SECONDS - 4.0, STYLED_SCORCH_SECONDS, ageSec);
            float appear = smoothstep(0.0, 0.25, t);
            float emberFade = saturate(1.0 - ageSec / 1.5);
            float ember = smoothstep(0.82, 0.9, ValueNoise(i.uv * 48.0 + 11.0)) * (1.0 - smoothstep(rim * 0.5, rim, d));
            float heat = saturate(1.0 - t * 2.5) * (1.0 - d);
            float3 soot = float3(0.05, 0.04, 0.035);
            float3 glowRgb = saturate(lerp(i.tint.rgb, float3(1.0, 0.55, 0.15), 0.4) * 1.5 + 0.05);
            float3 discRgb = lerp(soot, glowRgb, saturate(ember * emberFade + heat));
            float discA = blotch * appear * fade * 0.7;
            float3 shockRgb = lerp(i.tint.rgb, float3(1.0, 0.95, 0.8), 0.5);
            float a = saturate(shock + discA);
            float3 rgb = lerp(discRgb, shockRgb, saturate(shock / max(a, 1e-3)));
            return half4(rgb, a * step(d, 1.0));
        }
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
        if (i.tint.a > 0.5)
        {
            // Styled fire ball (additive): boiling noise edge, white-hot core shrinking as it burns out into the tint.
            float2 p = i.uv * 2.0 - 1.0;
            float ang = atan2(p.y, p.x);
            float boil = ValueNoise(float2(ang * 2.5, t * 6.0)) * 0.35 + ValueNoise(p * 5.0 + t * 3.0) * 0.25;
            float body = saturate((1.0 - d / (0.72 + boil * 0.5)) * 3.0);
            float coreR = lerp(0.55, 0.0, t);
            float core = saturate(1.0 - d / max(coreR, 1e-3));
            float fade = (1.0 - t) * (1.0 - t * 0.5);
            float3 rgb = lerp(i.tint.rgb, float3(1.0, 0.92, 0.7), core);
            rgb = lerp(rgb, i.tint.rgb * 0.5, saturate(t * 1.4 - 0.4) * (1.0 - core));
            return half4(rgb, body * fade * 0.6); // glow only; the burst pass carries the body
        }
        float core = saturate(1.0 - d);
        float a = core * core * (1.0 - t);
        return half4(_ImpactFlashColor.rgb, a);
    }

    // Styled burst, premultiplied alpha: an opaque boiling fire ball (white-hot core -> tint -> deep) that
    // burns out through a noisy edge into a rising soot puff. Shows on bright ground, unlike additive light.
    half4 fragSmoke(Varyings i) : SV_Target
    {
        float2 p = i.uv * 2.0 - 1.0;
        float d = length(p);
        float t = saturate(i.age);
        float n = ValueNoise(p * 3.0 + float2(0.0, -t * 2.0)) * 0.6 + ValueNoise(p * 7.0 + t * 1.7) * 0.4;
        float puff = saturate((1.0 - d / (0.55 + 0.4 * n)) * 2.5);
        float3 tint = i.tint.rgb;
        float3 deep = tint * tint * 0.8;
        float3 hot = float3(1.0, 0.95, 0.8);
        float fireT = saturate(t / 0.3);
        float coreW = saturate(1.0 - d / (0.55 * (1.0 - fireT) + 1e-3));
        float3 fire = lerp(lerp(saturate(tint * 1.4), deep, saturate(d * 1.3 + n * 0.3 - 0.2)), hot, coreW);
        float burn = 1.0 - smoothstep(0.12, 0.42, t + (n - 0.5) * 0.15);
        float3 rgb = lerp(float3(0.2, 0.18, 0.16), fire, burn);
        float a = puff * lerp(0.55, 1.0, burn) * (1.0 - smoothstep(0.5, 1.0, t)) * smoothstep(0.0, 0.03, t);
        return half4(rgb * a + fire * burn * puff * 0.25, a);
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
        Pass
        {
            // Styled bursts only (legacy impacts collapse to nothing here): rising soot puff after the fire ball.
            Name "ProjectileImpactSmoke"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragSmoke
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setupSmoke
            #pragma target 4.5
            ENDHLSL
        }
    }
    Fallback Off
}
