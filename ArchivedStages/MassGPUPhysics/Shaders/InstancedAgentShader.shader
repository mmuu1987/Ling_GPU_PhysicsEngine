Shader "Universal Render Pipeline/MassGPUPhysics/InstancedAgentShader"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // 告诉光栅化器：这个 Shader 支持 GPU Instancing (合批渲染)
            #pragma multi_compile_instancing
            // 声明我们将使用程序化 Instancing (Procedural Instancing)，并让底层在渲染前先调用 setup 函数
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 【数据结构】与 C# ComputeShader 中的 AgentData 结构保持完全一致
            struct AgentData
            {
                float3 position; // 位置
                float3 rotation; // 欧拉角旋转
                float3 scale;    // 缩放
            };

            // 如果启用了程序化 Instancing，就引入显存中的 ComputeBuffer
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<AgentData> agentBuffer;
            #endif

            // 【输入结构】从显卡送进顶点着色器的数据
            struct Attributes
            {
                float4 positionOS   : POSITION; // 原始网格自带的本地坐标
                float3 normalOS     : NORMAL;   // 原始网格的本地法线
                float2 uv           : TEXCOORD0;// 原始网格的UV
                UNITY_VERTEX_INPUT_INSTANCE_ID  // 提取 Instancing 下当前物体的 ID (相当于数组索引)
            };

            // 【输出结构】从顶点着色器传给片元着色器的数据
            struct Varyings
            {
                float4 positionCS   : SV_POSITION; // 裁剪空间下的坐标 (必须要有)
                float3 normalWS     : TEXCOORD0;   // 转换到世界空间下的法线 (用于计算假光照)
                float2 uv           : TEXCOORD1;   // 传递UV
                UNITY_VERTEX_INPUT_INSTANCE_ID     // 将 ID 传递给片元环节，这样片元也能知道当前是在画哪个小球
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            // 【矩阵运算】将欧拉角转换为旋转矩阵。这是基础的图形学3D数学公式。
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

            // 【核心钩子函数】DrawMeshInstancedIndirect 触发时，Unity会自动对每个实例执行这个 setup！
            // 目的：覆盖物体原本的 Transform 变换矩阵，改用我们从 GPU Buffer 里面读出来的矩阵。
            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    // unity_InstanceID 是内置变量，代表当前正在画第几个球（0 到 99999）
                    AgentData data = agentBuffer[unity_InstanceID];

                    // 1. 构建缩放矩阵
                    float4x4 scaleMatrix = float4x4(
                        data.scale.x, 0, 0, 0,
                        0, data.scale.y, 0, 0,
                        0, 0, data.scale.z, 0,
                        0, 0, 0, 1
                    );

                    // 2. 构建旋转矩阵 (将度数转为弧度再算)
                    float4x4 rotMatrix = CreateEulerRotationMatrix(radians(data.rotation));

                    // 3. 构建平移(位置)矩阵
                    float4x4 transMatrix = float4x4(
                        1, 0, 0, data.position.x,
                        0, 1, 0, data.position.y,
                        0, 0, 1, data.position.z,
                        0, 0, 0, 1
                    );

                    // 4. TRS 矩阵乘法组合 (注意 HLSL 中 mul 的顺序是从右往左执行：先缩放，再旋转，最后平移)
                    unity_ObjectToWorld = mul(transMatrix, mul(rotMatrix, scaleMatrix));

                    // 5. 逆矩阵计算 (处理法线和有些特效时需要用到原本的逆矩阵)
                    float4x4 invTransMatrix = float4x4(
                        1, 0, 0, -data.position.x,
                        0, 1, 0, -data.position.y,
                        0, 0, 1, -data.position.z,
                        0, 0, 0, 1
                    );
                    
                    float4x4 invRotMatrix = transpose(rotMatrix); // 旋转的逆矩阵等于它的转置矩阵
                    
                    float4x4 invScaleMatrix = float4x4(
                        1.0/data.scale.x, 0, 0, 0,
                        0, 1.0/data.scale.y, 0, 0,
                        0, 0, 1.0/data.scale.z, 0,
                        0, 0, 0, 1
                    );

                    // Object To World 矩阵的逆向反向操作
                    unity_WorldToObject = mul(invScaleMatrix, mul(invRotMatrix, invTransMatrix));
                #endif
            }

            // 【顶点着色器】处理每个物体的形状
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                // 内部处理宏：提取出 Instance ID，并且在 Procedural 模式下调用上面的 setup() 方法！！（非常关键）
                UNITY_SETUP_INSTANCE_ID(input);
                // 把 ID 传递到下一阶段（片元）
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // 此时，unity_ObjectToWorld 的数据已经被 setup 篡改成了计算着色器算出来的数据了。
                // 我们直接照常把本地坐标变换到屏幕裁剪空间
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                
                // 转换法线到世界空间，留到下面计算假光照用
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;

                return output;
            }

            // 【片元着色器】最终上色：注意，当前这个 Shader 是简单的"无光照"纯假光效果
            half4 frag(Varyings input) : SV_Target
            {
                // 先激活 ID，这样下面才能用
                UNITY_SETUP_INSTANCE_ID(input);
                
                // 1. 模拟一个简单的平行假光照 (打在球体上产生立体感，否则球看起来就是个纯色扁平圆面)
                float3 lightDir = normalize(float3(0.5, 1.0, -0.5));
                // N dot L: 物理学最基本法则，法线跟光线方向的点乘决定了受光强弱
                float nDotL = saturate(dot(normalize(input.normalWS), lightDir));
                // 混入一点环境光（0.3），避免背光处死黑
                float3 lighting = nDotL * 0.7 + 0.3; 
                
                // 2. 利用 InstanceID 给每个球生成一个随机的个性化底色
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    float id = (float)input.instanceID;
                #else
                    float id = 0; // 为了防止没开启宏时导致编译报错的保护处理
                #endif
                
                // 故意乘上一些质数再取小数部分，这样就能生成出各种看似纯随机的伪随机 RGB 颜色！
                float3 randomColor = frac(float3(id * 0.017, id * 0.023, id * 0.029));
                
                // 3. 利用 UV 坐标来形成花纹：UV跟着物体的旋转而旋转，从而实现球体"真在滚"的视觉感受。
                float3 patternColor = float3(input.uv.x, input.uv.y, 1.0);
                
                // 混合相乘求出最终颜色
                float3 finalColor = patternColor * randomColor * lighting;
                
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
