Shader "Universal Render Pipeline/MassGPUPhysics_Stage2/BillboardInstancedAgentShader"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.55, 0.75, 1, 1)
        _BillboardWidth ("Billboard Width", Float) = 0.65
        _BillboardHeight ("Billboard Height", Float) = 1.75
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 50

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AgentData
            {
                float3 position;
                float3 rotation;
                float3 scale;
                int currentState;
                float currentAnimationTime;
            };

            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<AgentData> agentBuffer;
                StructuredBuffer<uint> visibleAgentIndices;
                static AgentData _CurrentAgent;
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _BillboardWidth;
                float _BillboardHeight;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half shade : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    uint agentIndex = visibleAgentIndices[unity_InstanceID];
                    _CurrentAgent = agentBuffer[agentIndex];

                    // Billboard 自己在 vertex 里朝向摄像机，这里只给 Unity 一个平移矩阵兜底。
                    unity_ObjectToWorld = float4x4(
                        1, 0, 0, _CurrentAgent.position.x,
                        0, 1, 0, _CurrentAgent.position.y,
                        0, 0, 1, _CurrentAgent.position.z,
                        0, 0, 0, 1);
                    unity_WorldToObject = float4x4(
                        1, 0, 0, -_CurrentAgent.position.x,
                        0, 1, 0, -_CurrentAgent.position.y,
                        0, 0, 1, -_CurrentAgent.position.z,
                        0, 0, 0, 1);
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    // 取摄像机的世界空间 right/up，让四边形永远面向镜头。
                    float3 cameraRightWS = normalize(float3(UNITY_MATRIX_I_V[0][0], UNITY_MATRIX_I_V[1][0], UNITY_MATRIX_I_V[2][0]));
                    float3 cameraUpWS = normalize(float3(UNITY_MATRIX_I_V[0][1], UNITY_MATRIX_I_V[1][1], UNITY_MATRIX_I_V[2][1]));

                    float width = _BillboardWidth * max(_CurrentAgent.scale.x, 0.001);
                    float height = _BillboardHeight * max(_CurrentAgent.scale.y, 0.001);

                    // runtime quad 的 x 是 -0.5..0.5，y 是 0..1，所以 billboard 从脚底向上立起来。
                    float3 positionWS = _CurrentAgent.position
                        + cameraRightWS * (input.positionOS.x * width)
                        + cameraUpWS * (input.positionOS.y * height);

                    output.positionCS = TransformWorldToHClip(positionWS);
                    output.uv = input.uv;

                    // 用动画时间做一点很轻的亮度摆动，远处仍然有“在动”的感觉，不采 VAT。
                    output.shade = 0.8h + 0.2h * (half)sin(_CurrentAgent.currentAnimationTime * 6.28318);
                #else
                    output.positionCS = TransformObjectToHClip(input.positionOS);
                    output.uv = input.uv;
                    output.shade = 1;
                #endif

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 简单做人形轮廓：上半身略亮，下半身略暗，边缘收一点。
                half edge = smoothstep(0.0h, 0.12h, input.uv.x) * (1.0h - smoothstep(0.88h, 1.0h, input.uv.x));
                half vertical = lerp(0.65h, 1.0h, input.uv.y);
                half3 color = _BaseColor.rgb * input.shade * vertical * lerp(0.55h, 1.0h, edge);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
