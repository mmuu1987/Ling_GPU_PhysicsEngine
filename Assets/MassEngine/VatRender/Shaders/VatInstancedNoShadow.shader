Shader "Universal Render Pipeline/MassEngine/VatInstancedNoShadow"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BaseMap ("Base Map", 2D) = "white" {}

        _VATPosTex ("VAT Position Texture", 2D) = "black" {}
        _VATNormTex ("VAT Normal Texture", 2D) = "black" {}
        _VATTexWidth ("VAT Texture Width", Float) = 1
        _VATTexHeight ("VAT Texture Height", Float) = 1
        _VATFrameCount ("VAT Total Frame Count", Float) = 1
        _VATRowsPerFrame ("VAT Rows Per Frame", Float) = 1
        _VATFrameRate ("VAT Frame Rate", Float) = 30
        _IdleClipStartFrame ("Idle Clip Start Frame", Float) = 0
        _IdleClipFrameCount ("Idle Clip Frame Count", Float) = 30
        _IdleClipFrameRate ("Idle Clip Frame Rate", Float) = 30
        _MoveClipStartFrame ("Move Clip Start Frame", Float) = 0
        _MoveClipFrameCount ("Move Clip Frame Count", Float) = 30
        _MoveClipFrameRate ("Move Clip Frame Rate", Float) = 30
        _AttackClipStartFrame ("Attack Clip Start Frame", Float) = 0
        _AttackClipFrameCount ("Attack Clip Frame Count", Float) = 30
        _AttackClipFrameRate ("Attack Clip Frame Rate", Float) = 30
        _DeathClipStartFrame ("Death Clip Start Frame", Float) = 0
        _DeathClipFrameCount ("Death Clip Frame Count", Float) = 30
        _DeathClipFrameRate ("Death Clip Frame Rate", Float) = 30
        [Header(Vertex AO)]
        _VertexAO       ("Vertex AO (per unit type, MPB)", Range(0, 1)) = 0
        _VertexAODirect ("Vertex AO on direct light (MPB)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "MassLodCrossfade"="1" }
        LOD 80

        // Shared across ForwardNoShadow / DepthOnly / DepthNormals (same layout as
        // LitInstancedAgentShader): VAT sampling, agent buffers and the procedural
        // instancing setup live here once.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_VATPosTex);
            TEXTURE2D(_VATNormTex);
            SAMPLER(sampler_point_clamp);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _VATTexWidth;
                float _VATTexHeight;
                float _VATFrameCount;
                float _VATRowsPerFrame;
                float _VATFrameRate;
                float _IdleClipStartFrame;
                float _IdleClipFrameCount;
                float _IdleClipFrameRate;
                float _MoveClipStartFrame;
                float _MoveClipFrameCount;
                float _MoveClipFrameRate;
                float _AttackClipStartFrame;
                float _AttackClipFrameCount;
                float _AttackClipFrameRate;
                float _DeathClipStartFrame;
                float _DeathClipFrameCount;
                float _DeathClipFrameRate;
                float _VertexAO;
                float _VertexAODirect;
            CBUFFER_END

            // 尸体下沉参数，每帧由 MassGpuRenderDispatcher 通过 MaterialPropertyBlock 写入。
            // xyz = (停留秒数, 下沉秒数, 下沉深度)；x <= 0 表示不下沉（尸体永久保留）。
            // 由 MPB 提供，所以必须放在 UnityPerMaterial 之外。
            float4 _MassCorpseSink;
            float4 _MassFlight; // MPB, see ResolveFlightLift


            // LOD crossfade (2026-10-13, 中景闪动修复 B). _MassLodTierPlusOne = tier of this draw + 1
            // (MPB; 0 = no crossfade). _MassLodState = per-agent word from the LOD classification
            // (AgentDataCommon.hlsl ResolveLodTier). While an agent changes tier it is drawn in both
            // tiers; the incoming mesh keeps the dither cells below the progress, the outgoing mesh
            // the rest, so every pixel shows exactly one of them.
            float _MassLodTierPlusOne;
            static float _GlobalLodVis = 2.0; // > 1.5 = fully shown

            float MassLodVisibility(uint state)
            {
                if (_MassLodTierPlusOne < 0.5 || (state & 16u) == 0u)
                    return 2.0;
                int cur = (int)(state & 3u);
                int prev = (int)((state >> 2) & 3u);
                float f = (float)(state >> 16) * (1.0 / 65535.0);
                if (f >= 1.0 || cur == prev)
                    return 2.0;
                int tier = (int)(_MassLodTierPlusOne - 0.5);
                if (tier == cur) return f;
                if (tier == prev) return -1.0 - f;
                return 2.0;
            }

            void MassLodDitherClip(float vis, float4 positionCS)
            {
                if (vis > 1.5)
                    return;
                static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                uint2 p = (uint2)positionCS.xy & 3u;
                float t = (bayer[p.y * 4u + p.x] + 0.5) / 16.0;
                if (vis >= 0.0)
                    clip(vis - t);
                else
                    clip(t - (-1.0 - vis));
            }

            // 与 LitInstancedAgentShader / AgentDataCommon.hlsl / CorpseLifetime.cs 同一套算法。
            float ResolveCorpseSink(int state, float age)
            {
                if (state != 4 || _MassCorpseSink.x <= 0.0)
                    return 0.0;

                float t = saturate((age - _MassCorpseSink.x) / max(_MassCorpseSink.y, 0.0001));
                return t * _MassCorpseSink.z;
            }

            // Flight (2026-10-15): flyers hang _MassFlight.x metres up and fall (ease-in) over the death clip (_MassFlight.y s).
            // Takeoff (2026-10-16): _MassFlightGrounded (global) 1 = on the ground before the battle, 0 = airborne.
            float _MassFlightGrounded;
            float ResolveFlightLift(int state, float age)
            {
                if (_MassFlight.x <= 0.0)
                    return 0.0;
                float lift = _MassFlight.x * (1.0 - saturate(_MassFlightGrounded));
                if (state != 4)
                    return lift;
                float t = saturate(age / max(_MassFlight.y, 0.0001));
                return lift * (1.0 - t * t);
            }

            struct AgentData
            {
                float3 position;
                float3 rotation;
                float3 scale;
                float3 velocity;
                int currentState;
                float currentAnimationTime;
                int presentationState;
                float locomotionSpeed;
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<AgentData> agentBuffer;
                StructuredBuffer<uint> visibleAgentIndices;
                StructuredBuffer<uint> _MassLodState;
                static float _GlobalAnimationTime;
                static int _GlobalCurrentState;
            #endif

            #include "MassAgentKnock.hlsl" // 炸飞 (2026-10-17, render only)

            float4x4 CreateEulerRotationMatrix(float3 euler)
            {
                float cx = cos(euler.x);
                float sx = sin(euler.x);
                float cy = cos(euler.y);
                float sy = sin(euler.y);
                float cz = cos(euler.z);
                float sz = sin(euler.z);

                return float4x4(
                    cy * cz, -cy * sz, sy, 0.0,
                    cx * sz + sx * sy * cz, cx * cz - sx * sy * sz, -sx * cy, 0.0,
                    sx * sz - cx * sy * cz, sx * cz + cx * sy * sz, cx * cy, 0.0,
                    0.0, 0.0, 0.0, 1.0
                );
            }

            float2 GetVATUV(uint vertexID, float animTime, int state)
            {
                float clipStart = state == 0 ? _IdleClipStartFrame : _MoveClipStartFrame;
                float clipCount = state == 0 ? _IdleClipFrameCount : _MoveClipFrameCount;
                float clipRate = state == 0 ? _IdleClipFrameRate : _MoveClipFrameRate;
                if (state == 3)
                {
                    clipStart = _AttackClipStartFrame;
                    clipCount = _AttackClipFrameCount;
                    clipRate = _AttackClipFrameRate;
                }
                else if (state == 4)
                {
                    clipStart = _DeathClipStartFrame;
                    clipCount = _DeathClipFrameCount;
                    clipRate = _DeathClipFrameRate;
                }

                clipCount = max(1.0, clipCount);
                clipRate = max(1.0, clipRate);
                int localFrame = (int)fmod(animTime * clipRate, clipCount);
                if (state == 4)
                    localFrame = min((int)(animTime * clipRate), (int)clipCount - 1);

                int frame = clamp((int)clipStart + localFrame, 0, (int)_VATFrameCount - 1);
                int vX = (int)vertexID % (int)_VATTexWidth;
                int vY = frame * (int)_VATRowsPerFrame + (int)vertexID / (int)_VATTexWidth;
                return float2((vX + 0.5) / _VATTexWidth, (vY + 0.5) / _VATTexHeight);
            }

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    uint agentIndex = visibleAgentIndices[unity_InstanceID];
                    AgentData data = agentBuffer[agentIndex];

                    float4x4 scaleMatrix = float4x4(
                        data.scale.x, 0, 0, 0,
                        0, data.scale.y, 0, 0,
                        0, 0, data.scale.z, 0,
                        0, 0, 0, 1
                    );

                    float4x4 rotMatrix = CreateEulerRotationMatrix(radians(data.rotation));
                    // 尸体下沉只改渲染位置，agentBuffer 里仍是它倒下的坐标。
                    float3 renderPosition = data.position;
                    renderPosition.y -= ResolveCorpseSink(data.currentState, data.currentAnimationTime);
                renderPosition.y += ResolveFlightLift(data.currentState, data.currentAnimationTime);
                    float knockAngle;
                    float4 knock = MassKnockFetch(agentIndex, data.currentState);
                    renderPosition += MassKnockOffset(knock, data.currentAnimationTime, knockAngle);
                    float4x4 transMatrix = float4x4(
                        1, 0, 0, renderPosition.x,
                        0, 1, 0, renderPosition.y,
                        0, 0, 1, renderPosition.z,
                        0, 0, 0, 1
                    );
                    unity_ObjectToWorld = mul(transMatrix, mul(rotMatrix, scaleMatrix));

                    float4x4 invTransMatrix = float4x4(
                        1, 0, 0, -renderPosition.x,
                        0, 1, 0, -renderPosition.y,
                        0, 0, 1, -renderPosition.z,
                        0, 0, 0, 1
                    );
                    float4x4 invRotMatrix = transpose(rotMatrix);
                    float4x4 invScaleMatrix = float4x4(
                        1.0 / data.scale.x, 0, 0, 0,
                        0, 1.0 / data.scale.y, 0, 0,
                        0, 0, 1.0 / data.scale.z, 0,
                        0, 0, 0, 1
                    );
                    unity_WorldToObject = mul(invScaleMatrix, mul(invRotMatrix, invTransMatrix));
                    MassKnockTumble(knock, knockAngle, renderPosition + float3(0.0, 0.9 * data.scale.y, 0.0));

                    _GlobalAnimationTime = data.currentAnimationTime;
                    _GlobalCurrentState = data.currentState >= 3 ? data.currentState : data.presentationState;
                    // Idle: fixed per-agent phase so a standing army never moves in unison (render only, 2026-10-15).
                    if (_GlobalCurrentState == 0)
                    {
                        uint idleHash = agentIndex * 2654435761u; idleHash ^= idleHash >> 15; idleHash *= 2246822519u; idleHash ^= idleHash >> 13;
                        _GlobalAnimationTime += (float)(idleHash & 0xFFFFu) / 65536.0 * (max(1.0, _IdleClipFrameCount) / max(1.0, _IdleClipFrameRate));
                    }
                    _GlobalLodVis = MassLodVisibility(_MassLodState[agentIndex]);
                #endif
            }
        ENDHLSL

        Pass
        {
            Name "ForwardNoShadow"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma multi_compile_fog
            // C (2026-10-13): receive main-light shadows + vertex additional lights like LitInstancedAgent,
            // so an agent does not change brightness at the near/mid ring. This tier still casts no shadow.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 aoUV : TEXCOORD3; // 顶点AO（uv3.x）
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float lodVis : TEXCOORD7; // LOD crossfade
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half ao : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                half3 vertexLight : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.lodVis = _GlobalLodVis;

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    float2 vatUV = GetVATUV(input.vertexID, _GlobalAnimationTime, _GlobalCurrentState);
                    float3 vatPos = SAMPLE_TEXTURE2D_LOD(_VATPosTex, sampler_point_clamp, vatUV, 0).rgb;
                    float3 vatNorm = SAMPLE_TEXTURE2D_LOD(_VATNormTex, sampler_point_clamp, vatUV, 0).rgb;
                    input.positionOS = float4(vatPos, 1.0);
                    input.normalOS = normalize(vatNorm);
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.ao = (half)lerp(1.0, saturate(input.aoUV.x), _VertexAO);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    output.vertexLight = VertexLighting(output.positionWS, normalize(output.normalWS));
                #else
                    output.vertexLight = 0;
                #endif
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
MassLodDitherClip(input.lodVis, input.positionCS);

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                float3 normalWS = normalize(input.normalWS);
                // Same lighting as LitInstancedAgent ForwardLit (C, 2026-10-13): main-light shadows, AO on
                // ambient (and partly on direct), vertex additional lights. The old 0.25 floor is gone.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half3 directLight = mainLight.color * (nDotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(normalWS) * input.ao;
                directLight *= lerp(1.0h, input.ao, (half)_VertexAODirect);
                half3 finalColor = albedo * (directLight + ambient);
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    finalColor += albedo * input.vertexLight;
                #endif
                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // =========================================================
        // Pass: DepthOnly - fills the URP depth prepass / _CameraDepthTexture.
        // Without it depth-primed renderers and depth-based effects (SSAO with
        // Source=Depth, depth of field, motion blur) see straight through every
        // agent. Vertex path = ShadowCaster without the shadow bias.
        // =========================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float lodVis : TEXCOORD7; // LOD crossfade
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.lodVis = _GlobalLodVis;

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                {
                    float2 vatUV = GetVATUV(input.vertexID, _GlobalAnimationTime, _GlobalCurrentState);
                    float3 vatPos = SAMPLE_TEXTURE2D_LOD(_VATPosTex, sampler_point_clamp, vatUV, 0).rgb;
                    input.positionOS = float4(vatPos, 1.0);
                }
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                MassLodDitherClip(input.lodVis, input.positionCS);
                return 0;
            }
            ENDHLSL
        }

        // =========================================================
        // Pass: DepthNormals - fills _CameraNormalsTexture. The shipped
        // PC_Renderer runs SSAO with Source=DepthNormals: agents missing this
        // pass simply do not exist for ambient occlusion.
        // =========================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float lodVis : TEXCOORD7; // LOD crossfade
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.lodVis = _GlobalLodVis;

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                {
                    float2 vatUV = GetVATUV(input.vertexID, _GlobalAnimationTime, _GlobalCurrentState);
                    float3 vatPos = SAMPLE_TEXTURE2D_LOD(_VATPosTex, sampler_point_clamp, vatUV, 0).rgb;
                    float3 vatNorm = SAMPLE_TEXTURE2D_LOD(_VATNormTex, sampler_point_clamp, vatUV, 0).rgb;
                    input.positionOS = float4(vatPos, 1.0);
                    input.normalOS = normalize(vatNorm);
                }
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                MassLodDitherClip(input.lodVis, input.positionCS);
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}

