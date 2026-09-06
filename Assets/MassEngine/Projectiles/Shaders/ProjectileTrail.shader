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
            CBUFFER_END

            // Keep in sync with ProjectileRenderConfig.MaxTeamColors: the dispatcher always
            // uploads exactly this many entries, so a shorter authored palette is already
            // padded on the C# side.
            #define PROJECTILE_TEAM_COLOR_COUNT 8

            // Outside UnityPerMaterial on purpose: set per draw from a MaterialPropertyBlock,
            // and an array in the per-material cbuffer would break batching declarations.
            float4 _ProjectileTeamColors[PROJECTILE_TEAM_COLOR_COUNT];

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
                float2 padding;
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<ProjectileData> projectileBuffer;
                // Compacted by the CollectActiveProjectiles kernel, so instance i is always
                // a live slot; idle slots never reach the vertex stage at all.
                StructuredBuffer<uint> activeProjectileIndices;
                static half4 _TracerColor;
            #endif

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    uint slot = activeProjectileIndices[unity_InstanceID];
                    ProjectileData data = projectileBuffer[slot];

                    float speed = length(data.velocity);
                    float3 dir = speed > 1e-4 ? data.velocity / speed : float3(0, 0, 1);
                    float len = max(_ProjectileTrailMinLength, data.trailLength * _ProjectileTrailLengthScale);
                    float width = max(1e-3, _ProjectileTrailWidth);

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
                    float3 centre = data.position - dir * (len * 0.5);
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
                half alpha = input.color.a * head * head * across;
                half3 rgb = MixFog(input.color.rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
