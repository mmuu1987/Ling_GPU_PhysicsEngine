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
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 80

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
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
            CBUFFER_END

            struct AgentData
            {
                float3 position;
                float3 rotation;
                float3 scale;
                float3 velocity;
                int currentState;
                float currentAnimationTime;
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<AgentData> agentBuffer;
                StructuredBuffer<uint> visibleAgentIndices;
                static float _GlobalAnimationTime;
                static int _GlobalCurrentState;
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                    float4x4 transMatrix = float4x4(
                        1, 0, 0, data.position.x,
                        0, 1, 0, data.position.y,
                        0, 0, 1, data.position.z,
                        0, 0, 0, 1
                    );
                    unity_ObjectToWorld = mul(transMatrix, mul(rotMatrix, scaleMatrix));

                    float4x4 invTransMatrix = float4x4(
                        1, 0, 0, -data.position.x,
                        0, 1, 0, -data.position.y,
                        0, 0, 1, -data.position.z,
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

                    _GlobalAnimationTime = data.currentAnimationTime;
                    _GlobalCurrentState = data.currentState;
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

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
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half3 directLight = mainLight.color * (nDotL * mainLight.distanceAttenuation);
                half3 ambient = SampleSH(normalWS);
                half3 finalColor = albedo * max(directLight + ambient, half3(0.25, 0.25, 0.25));
                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
