Shader "Universal Render Pipeline/MassGPUPhysics/LitInstancedAgentShader"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        // 降低 LOD 值或者配置 LOD 设置可以协助以后的性能管理
        LOD 300

        // =========================================================
        // HLSLINCLUDE 把通用的结构体和函数(比如Instancing相关)提取出来。
        // 因为我们这次有 ForwardLit (画颜色) 和 ShadowCaster (画阴影深度) 两个 Passes，
        // 把这些公用代码写在这里可以被两个 Pass 共享，防止代码冗余。
        // =========================================================
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 与 C# 中的 struct 内存对齐一致
        struct AgentData
        {
            float3 position;
            float3 rotation;
            float3 scale;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<AgentData> agentBuffer; // 显存放着咱们几十万个小球的心脏
        #endif

        // 用来生成欧拉角旋转矩阵的方法
        float4x4 CreateEulerRotationMatrix(float3 euler)
        {
            float cx = cos(euler.x); float sx = sin(euler.x);
            float cy = cos(euler.y); float sy = sin(euler.y);
            float cz = cos(euler.z); float sz = sin(euler.z);

            return float4x4(
                cy * cz, -cy * sz, sy, 0.0,
                cx * sz + sx * sy * cz, cx * cz - sx * sy * sz, -sx * cy, 0.0,
                sx * sz - cx * sy * cz, sx * cz + cx * sy * sz, cx * cy, 0.0,
                0.0, 0.0, 0.0, 1.0
            );
        }

        // Setup 函数：覆盖引擎内部原来的 Transform 矩阵，狸猫换太子！
        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                AgentData data = agentBuffer[unity_InstanceID];

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

                // 准备逆矩阵...
                float4x4 invTransMatrix = float4x4(
                    1, 0, 0, -data.position.x,
                    0, 1, 0, -data.position.y,
                    0, 0, 1, -data.position.z,
                    0, 0, 0, 1
                );
                
                float4x4 invRotMatrix = transpose(rotMatrix);
                
                float4x4 invScaleMatrix = float4x4(
                    1.0/data.scale.x, 0, 0, 0,
                    0, 1.0/data.scale.y, 0, 0,
                    0, 0, 1.0/data.scale.z, 0,
                    0, 0, 0, 1
                );

                unity_WorldToObject = mul(invScaleMatrix, mul(invRotMatrix, invTransMatrix));
            #endif
        }
        ENDHLSL

        // =========================================================
        // Pass 1: 获取光照、接受场景阴影并渲染表面 (ForwardLit)
        // =========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            // 【非常重要】：打开 URP 相关的所有光照和阴影宏！这样 Shader 才会去接受场景的主光源和级联阴影。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0; // 这次要保存世界坐标了，因为真实光照计算必须在世界坐标下进行
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                half fogFactor      : TEXCOORD3; // 用于存放计算好的雾气浓度数据
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;

                // 计算该顶点受雾效影响的程度（离相机越远，浓度越高）
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // 1. 生成个性化随机图案基底色 (和第一个Shader一样原理)
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    float id = (float)input.instanceID;
                #else
                    float id = 0.0;
                #endif
                float3 randomColor = frac(float3(id * 0.017, id * 0.023, id * 0.029));
                float3 patternColor = float3(input.uv.x, input.uv.y, 1.0);
                float3 albedo = patternColor * randomColor;
                
                // 记得要把法线重新归一化 (光栅化插值后法线长度可能不为1)
                float3 normalWS = normalize(input.normalWS);

                // 2.【真正的 URP 光照体系接入】 
                // 获取当前像素在阴影级联贴图中的对应坐标
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                // 获取场景主光源的数据（方向、颜色、是否有阴影等）
                Light mainLight = GetMainLight(shadowCoord);
                
                // 算点乘(N dot L)：物体表面朝向到底迎接了多少光。
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                // 综合得出这束光带来的最终光照量：本身光颜色 * 迎接角度衰减 * 阴影图衰减 * 距离衰减
                half3 lightColor = mainLight.color * (NdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                
                // 加上场景全局环境光 (球形谐波 SH 函数)，否则阴影处会变成纯死黑色
                half3 ambient = SampleSH(normalWS);
                
                // 初步合并光照 (本身颜色 × 叠加所有的光环境)
                float3 finalColor = albedo * (lightColor + ambient);

                // 3. 【点光源与聚光灯扩展】(可选)
                // 获取所有照射到了这个片元的附加光源（如灯泡，手电筒，烟火光）
                int pixelLightCount = GetAdditionalLightsCount();
                for (int i = 0; i < pixelLightCount; ++i)
                {
                    Light light = GetAdditionalLight(i, input.positionWS);
                    half addNdotL = saturate(dot(normalWS, light.direction));
                    // 循环往最终颜色里累加这些多出来的聚光灯效果！
                    finalColor += albedo * light.color * (addNdotL * light.distanceAttenuation * light.shadowAttenuation);
                }
                
                // 4. 将雾色与最终表面颜色进行混合
                finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // =========================================================
        // Pass 2: 阴影投射 (ShadowCaster)，让这个球去遮挡光线并产生影子
        // =========================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            // 投射阴影本身不需要画出任何颜色，只需要往深度的 Z-Buffer 里面写深度即可！
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            // 支持点光源产生阴影的宏
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            // 前面我们在修 bug 时引入的 URP 内部文件，为后续 Shadow 计算提供底层框架支持
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // URP 渲染阴影时由外界控制的光源信息，手写阴影通道时必须声明接住它们。
            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // 将被照物体的顶点先算到世界坐标
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // 核心：计算阴影偏移偏见 (Shadow Bias)。
                // 原理就是渲染深度图的时候，稍微把顶点顺着法线方向外推（膨胀）一点点，
                // 防止自己把影子投到自己身上形成密密麻麻的条纹网格干扰 (Acne 现象)。
                float3 lightDirectionWS = _LightDirection;
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    lightDirectionWS = normalize(_LightPosition.xyz - positionWS);
                #endif
                
                // 将偏移后的坐标转换为裁剪空间 (这就成了深度图上的形状)
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                // 平台差异性处理，控制深度裁切范围
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;

                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // 片元着色器直接返回 0，因为在阴影 Pass 里我们只关心深度(Z-Depth)写满了没有，不关心具体的RGB颜色！
                return 0;
            }
            ENDHLSL
        }
    }
}
