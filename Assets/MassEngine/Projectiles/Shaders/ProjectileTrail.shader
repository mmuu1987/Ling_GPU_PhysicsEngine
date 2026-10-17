Shader "Universal Render Pipeline/MassEngine/ProjectileTrail"
{
    Properties
    {
        // Tracer colors live in _ProjectileTeamColors, a per-team array the dispatcher
        // uploads through the MaterialPropertyBlock. Arrays cannot be material properties,
        // so there is nothing to expose here.
        _ProjectileTrailWidth ("Trail Width", Float) = 0.15
        _ProjectileTrailLengthScale ("Trail Length Scale", Float) = 2
        _ProjectileTrailMinLength ("Trail Min Length", Float) = 0.8
        // Splash shots (splashRadius > 0, e.g. dragon fireballs) render as a wider fire streak.
        _SplashTrailWidth ("Splash Trail Width", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "ProjectileTrailForward"
            Tags { "LightMode"="UniversalForward" }

            // ZWrite Off keeps thousands of overlapping tracers from fighting each other,
            // while ZTest LEqual still lets terrain and agents occlude them properly.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma multi_compile_fog
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ProjectileTrailWidth;
                float _ProjectileTrailLengthScale;
                float _ProjectileTrailMinLength;
                float _SplashTrailWidth;
            CBUFFER_END

            // Keep in sync with ProjectileRenderConfig.MaxTeamColors: the dispatcher always
            // uploads exactly this many entries, so a shorter authored palette is already
            // padded on the C# side.
            #define PROJECTILE_TEAM_COLOR_COUNT 8

            // Outside UnityPerMaterial on purpose: set per draw from a MaterialPropertyBlock,
            // and an array in the per-material cbuffer would break batching declarations.
            float4 _ProjectileTeamColors[PROJECTILE_TEAM_COLOR_COUNT];
            float _MassProjectileTime; // global, MassEngineManager (projectile simulation clock)

            // Mirrors ProjectileGpuData / ProjectileData exactly: 64 bytes, same field
            // order. Changing either side without the other silently misreads every field.
            struct ProjectileData
            {
                float3 position;
                float launchTime;
                float3 velocity;
                float damage;
                int targetAgentIndex;
                int sourceTeamId;
                float hitRadius;
                float gravity;
                float maxLifetime;
                float trailLength;
                int sourceAgentIndexPlusOne;
                float splashRadius; // former padding; > 0 = splash shot (fire streak)
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<ProjectileData> projectileBuffer;
                // Compacted by the CollectActiveProjectiles kernel, so instance i is always
                // a live slot; idle slots never reach the vertex stage at all.
                StructuredBuffer<uint> activeProjectileIndices;
                struct LoftAgentData { float3 position; float3 rotation; float3 scale; float3 velocity; int currentState; float currentAnimationTime; int presentationState; float locomotionSpeed; };
                StructuredBuffer<LoftAgentData> agentBuffer;
                static half4 _TracerColor;
                static half _TracerFire;
                // Fireball look (2026-10-16): tint per shooter unit type (ProjectileGpuRenderDispatcher.BindFx).
                StructuredBuffer<int> projectileAgentUnitType;
                StructuredBuffer<float4> projectileFxByType;
                static half4 _FxTint;
                static float2 _FxSize;
                static float _FxScale; // 2026-10-17: CombatConfig.projectileFxScale of the shooter type
            #endif
            float _ProjectileFxEnabled;

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            half4 ResolveShooterTint(int sourceAgentIndexPlusOne)
            {
                if (_ProjectileFxEnabled < 0.5 || sourceAgentIndexPlusOne <= 0)
                    return 0;
                uint agentCount, stride, typeCount;
                projectileAgentUnitType.GetDimensions(agentCount, stride);
                projectileFxByType.GetDimensions(typeCount, stride);
                typeCount /= 2; // [2t] = tint, [2t + 1] = (size scale, ...)
                uint agent = (uint)(sourceAgentIndexPlusOne - 1);
                if (agent >= agentCount)
                    return 0;
                int type = projectileAgentUnitType[agent];
                if (type < 0 || (uint)type >= typeCount)
                    return 0;
                _FxScale = projectileFxByType[type * 2 + 1].x;
                return (half4)projectileFxByType[type * 2];
            }
            #endif

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    uint slot = activeProjectileIndices[unity_InstanceID];
                    ProjectileData data = projectileBuffer[slot];

                    // Volley loft (2026-10-15, render only): long gravity shots get an extra, per-arrow random arc on
                    // top of the simulated one, so a volley reads as a spread of heights instead of a flat sheet. The
                    // bulge is zero at launch and at impact, so hits, timing and splash stay exactly as simulated.
                    if (data.gravity < -0.01)
                    {
                        float g = -data.gravity;
                        float age = max(0.0, _MassProjectileTime - data.launchTime);
                        float vy0 = data.velocity.y + g * age;                         // exact: pure ballistic integration
                        float y0 = data.position.y - vy0 * age + 0.5 * g * age * age;
                        float y1 = y0;
                        uint agentCount, agentStride;
                        agentBuffer.GetDimensions(agentCount, agentStride);
                        if (data.targetAgentIndex >= 0 && (uint)data.targetAgentIndex < agentCount)
                        {
                            LoftAgentData target = agentBuffer[(uint)data.targetAgentIndex];
                            y1 = target.position.y + 0.9 * abs(target.scale.y);
                        }
                        float flight = (vy0 + sqrt(max(0.0, vy0 * vy0 - 2.0 * g * (y1 - y0)))) / g;
                        float range = length(data.velocity.xz) * flight;
                        if (flight > 1e-3 && age < flight && range > 20.0)
                        {
                            uint h = (slot * 2654435761u) ^ (asuint(data.launchTime) * 2246822519u);
                            h ^= h >> 15; h *= 2654435761u; h ^= h >> 13;
                            float extraApex = range * (0.06 + 0.22 * (float)(h & 0xFFFFu) / 65536.0) * smoothstep(20.0, 80.0, range);
                            float u = age / flight;
                            data.position.y += 4.0 * extraApex * u * (1.0 - u);
                            data.velocity.y += 4.0 * extraApex * (1.0 - 2.0 * u) / flight;
                        }
                    }

                    float speed = length(data.velocity);
                    float3 dir = speed > 1e-4 ? data.velocity / speed : float3(0, 0, 1);
                    float len = max(_ProjectileTrailMinLength, data.trailLength * _ProjectileTrailLengthScale);
                    float width = max(1e-3, _ProjectileTrailWidth);
                    _TracerFire = data.splashRadius > 0.0 ? 1.0h : 0.0h;
                    if (data.splashRadius > 0.0)
                        width = max(width, _SplashTrailWidth);
                    // Styled fireball: a ball ~0.2 x splash radius across with a halo, and a flame tail >= 5 balls long.
                    // The quad reaches half a width past the projectile so the ball sits whole at the true position.
                    _FxScale = 1.0;
                    _FxTint = data.splashRadius > 0.0 ? ResolveShooterTint(data.sourceAgentIndexPlusOne) : 0;
                    float headPad = 0.0;
                    if (_FxTint.a > 0.0)
                    {
                        width = max(1.6, data.splashRadius * 0.55) * clamp(_FxScale, 0.1, 3.0);
                        len = max(len, width * 3.2);
                        headPad = width * 0.5;
                    }
                    _FxSize = float2(len, width);

                    // Billboard around the flight axis: the quad keeps its length along the
                    // velocity and rolls to face the camera, so a tracer never degenerates
                    // into an invisible edge-on sliver.
                    float3 toCam = _WorldSpaceCameraPos - data.position;
                    float3 side = cross(dir, toCam);
                    float sideLen = length(side);
                    if (sideLen < 1e-4)
                    {
                        side = cross(dir, float3(0, 1, 0));
                        sideLen = length(side);
                        if (sideLen < 1e-4)
                        {
                            side = float3(1, 0, 0);
                            sideLen = 1.0;
                        }
                    }
                    side /= sideLen;
                    float3 up = cross(side, dir);

                    // The tracer trails BEHIND the projectile: local +x is the head, so the
                    // quad centre sits half a length back along the flight direction.
                    float3 centre = data.position + dir * headPad - dir * (len * 0.5);
                    float3 xAxis = dir * len;
                    float3 yAxis = side * width;
                    float3 zAxis = up * width;

                    unity_ObjectToWorld = float4x4(
                        xAxis.x, yAxis.x, zAxis.x, centre.x,
                        xAxis.y, yAxis.y, zAxis.y, centre.y,
                        xAxis.z, yAxis.z, zAxis.z, centre.z,
                        0, 0, 0, 1);

                    // Orthogonal axes with per-axis scale, so the inverse rows are the
                    // scaled axes divided by scale squared.
                    float invLen2 = 1.0 / max(1e-8, len * len);
                    float invWidth2 = 1.0 / max(1e-8, width * width);
                    float3 r0 = xAxis * invLen2;
                    float3 r1 = yAxis * invWidth2;
                    float3 r2 = zAxis * invWidth2;
                    unity_WorldToObject = float4x4(
                        r0.x, r0.y, r0.z, -dot(r0, centre),
                        r1.x, r1.y, r1.z, -dot(r1, centre),
                        r2.x, r2.y, r2.z, -dot(r2, centre),
                        0, 0, 0, 1);

                    // Indexed by raw team id, clamped rather than wrapped: a team past the
                    // palette reuses the last slot instead of impersonating team 0.
                    int teamSlot = clamp(data.sourceTeamId, 0, PROJECTILE_TEAM_COLOR_COUNT - 1);
                    _TracerColor = (half4)_ProjectileTeamColors[teamSlot];
                #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half fire : TEXCOORD3;
                half4 fxTint : TEXCOORD4;
                float2 fxSize : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    output.color = _TracerColor;
                    output.fire = _TracerFire;
                    output.fxTint = _FxTint;
                    output.fxSize = _FxSize;
                #else
                    // No procedural instancing means no projectile data to read a team from.
                    output.color = (half4)_ProjectileTeamColors[0];
                #endif
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // uv.x runs 0 at the tail to 1 at the head; uv.y softens the long edges.
                half head = (half)input.uv.x;
                half across = 1.0h - abs((half)input.uv.y * 2.0h - 1.0h);
                if (input.fxTint.a > 0.5h)
                {
                    // Styled fireball, in metres along the quad: ball (radius r) at the front, halo to the quad edge,
                    // then a flickering flame tail that tapers and cools from white-hot through the tint into smoke.
                    float L = max(input.fxSize.x, 1e-3), W = max(input.fxSize.y, 1e-3);
                    float r = W * 0.32;
                    float fromHead = (1.0 - input.uv.x) * L;
                    float side = (input.uv.y - 0.5) * W;
                    float time = _MassProjectileTime;
                    float dBall = length(float2(fromHead - W * 0.5, side)) / r;
                    float k = saturate((fromHead - W * 0.5) / max(L - W * 0.5, 1e-3));
                    float flick = 0.55 * sin(fromHead * 2.1 - time * 29.0) + 0.45 * sin(fromHead * 0.9 + side * 2.7 - time * 17.0);
                    float tailR = r * lerp(0.95, 0.12, k) * (1.0 + 0.22 * flick);
                    float tail = fromHead > W * 0.5 ? saturate(1.0 - abs(side) / max(tailR, 1e-3)) : 0.0;
                    float ball = saturate((1.0 - dBall) * 3.0);
                    float halo = saturate(1.0 - dBall / 1.55);
                    halo *= halo;
                    float core = saturate(1.0 - dBall / 0.45);
                    float3 tint = input.fxTint.rgb;
                    float3 deep = tint * tint * 0.8;
                    float3 bright = saturate(tint * 1.6 + 0.04);
                    float3 hot = float3(1.0, 0.95, 0.8);
                    // ball: small white-hot core -> bright tint -> deep rim
                    float3 ballRgb = lerp(lerp(deep, bright, saturate(1.6 - dBall * 1.6)), hot, core * core);
                    // tail: bright tint behind the ball -> deep -> smoke, with a flickering brighter inner flame
                    float3 tailRgb = lerp(bright, deep, saturate(k * 2.0));
                    tailRgb = lerp(tailRgb, float3(0.16, 0.13, 0.12), saturate((k - 0.55) * 2.2));
                    float inner = saturate(1.0 - abs(side) / max(tailR * 0.4, 1e-3)) * (1.0 - k);
                    tailRgb = lerp(tailRgb, lerp(bright, hot, 0.35), inner * (0.5 + 0.2 * flick));
                    float3 rgb = lerp(tailRgb, ballRgb, ball);
                    rgb = lerp(rgb, bright, halo * (1.0 - ball) * (1.0 - tail));
                    float a = max(ball, max(tail * lerp(0.95, 0.3, k), halo * 0.35));
                    a *= saturate((input.fxTint.a - 0.5) * 2.0); // density (2026-10-17): tint.a = 0.5 + 0.5 x density
                    return half4(MixFog((half3)rgb, input.fogFactor), (half)saturate(a));
                }
                if (input.fire > 0.5h)
                {
                    // Fire streak: saturated orange-red body (LDR, so it stays orange without bloom) with a short
                    // hot core at the head; the body keeps most of its opacity so the whole streak reads as a thick line.
                    half3 fireRgb = lerp(half3(0.85h, 0.14h, 0.02h), half3(1.0h, 0.45h, 0.06h), head);
                    fireRgb = lerp(fireRgb, half3(1.0h, 0.78h, 0.32h), saturate((head - 0.85h) * 6.0h) * across);
                    half fireAlpha = saturate((0.3h + 0.7h * head) * sqrt(across) * 1.25h);
                    return half4(MixFog(fireRgb, input.fogFactor), fireAlpha);
                }
                half alpha = input.color.a * head * head * across;
                half3 rgb = MixFog(input.color.rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

