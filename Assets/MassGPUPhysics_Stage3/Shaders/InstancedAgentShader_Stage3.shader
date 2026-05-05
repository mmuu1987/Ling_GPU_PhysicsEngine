Shader "Universal Render Pipeline/MassGPUPhysics_Stage3/InstancedAgentShader_Stage3"
{
    // ============================================================
    // InstancedAgentShader_Stage3 — 近处 LOD 的 VAT 顶点动画渲染 Shader。
    //
    // 【功能概述】
    // 1. 支持 GPU Instancing 程序化绘制（通过 agentBuffer 读取每个实例的 Transform）
    // 2. 用 VAT（顶点动画贴图）替换骨骼蒙皮：从纹理采样拿到当前动画帧的顶点位置/法线
    // 3. 简单的假光照计算（固定光源方向 + NdotL）
    //
    // 【Procedural Instancing 的工作原理】
    // setup() 函数在每个实例被绘制前由 GPU 调用，读取 agentBuffer 中该实例的
    // position/rotation/scale，覆盖 unity_ObjectToWorld 矩阵。vertex shader 随后
    // 像处理普通 GameObject 一样处理这个被"篡改"后的 Transform。
    //
    // 【VAT 采样的关键】
    // - vertexID（SV_VertexID）：GPU 给每个顶点的编号，决定从贴图的哪一行读数据
    // - _GlobalAnimationTime：当前实例的动画播放时间（setup 中从 agentBuffer 读取）
    // - GetVATUV()：把 (vertexID, 时间) 映射到 VAT 贴图的 (u, v) 坐标
    // - Point 采样：避免双线性插值混合了相邻顶点/帧的数据
    // ============================================================
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(VAT Vertex Animation Textures)]
        _VATPosTex  ("VAT Position Texture",  2D) = "black" {}
        _VATNormTex ("VAT Normal Texture",    2D) = "black" {}
        _VATTexWidth     ("VAT Texture Width",         Float) = 1
        _VATTexHeight    ("VAT Texture Height",        Float) = 1
        _VATFrameCount   ("VAT Total Frame Count",     Float) = 1
        _VATRowsPerFrame ("VAT Rows Per Frame",        Float) = 1
        _VATFrameRate    ("VAT Frame Rate (fps)",      Float) = 30
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

            // ── VAT 纹理采样资源（Point 精确采样，不插值原始位置数据）──
            TEXTURE2D(_VATPosTex);
            TEXTURE2D(_VATNormTex);
            // sampler_point_clamp 是 Unity 内置的点采样+夹紧寻址采样器状态
            SAMPLER(sampler_point_clamp);

            struct AgentData
            {
                float3 position; // 位置
                float3 rotation; // 欧拉角旋转
                float3 scale;    // 缩放
                float3 velocity;
                int currentState;
                float currentAnimationTime;
            };

            // 如果启用了程序化 Instancing，就引入显存中的 ComputeBuffer
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                // 完整 agent 数据，字段布局必须和 C# / Compute Shader 一致。
                StructuredBuffer<AgentData> agentBuffer;

                // 当前 draw call 要绘制的 agent 索引表。
                // 近处批次绑定 nearAgentIndexBuffer，远处批次绑定 farAgentIndexBuffer。
                // 这样 compute 分组只需要写 uint 索引，不需要复制整份 AgentData。
                StructuredBuffer<uint> visibleAgentIndices;

                // setup() 会把当前实例的动画时间存到这里，vertex shader 用它采 VAT。
                static float _GlobalAnimationTime;
            #endif

            // 【输入结构】从显卡送进顶点着色器的数据
            struct Attributes
            {
                float4 positionOS   : POSITION; // 原始网格自带的本地坐标
                float3 normalOS     : NORMAL;   // 原始网格的本地法线
                float2 uv           : TEXCOORD0;// 原始网格的UV
                uint   vertexID     : SV_VertexID; // 当前顶点在 Mesh 中的全局索引，用于 VAT 采样
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
                float _VATTexWidth;
                float _VATTexHeight;
                float _VATFrameCount;
                float _VATRowsPerFrame;
                float _VATFrameRate;
            CBUFFER_END

            // ──────────────────────────────────────────────────────────────
            // 【VAT 核心辅助函数】 GetVATUV
            // 输入：顶点 ID（模型中的第几个顶点）+ 动画时间（秒）
            // 输出：在 VAT 纹理中对应像素的 UV 坐标
            //
            // 【映射公式推导】
            // VAT 贴图布局：
            //   每行有 texWidth 个像素，每个像素存一个顶点的一帧数据
            //   第 f 帧的数据从 y = f * rowsPerFrame 开始
            //   顶点 #v 在第 f 帧的像素位置：
            //     pixelX = v % texWidth
            //     pixelY = f * rowsPerFrame + (v / texWidth)  ← 整数除法
            //   UV = ((pixelX + 0.5) / texWidth, (pixelY + 0.5) / texHeight)
            //   +0.5 是为了采样像素中心，避免浮点误差导致读到相邻像素
            // ──────────────────────────────────────────────────────────────
            float2 GetVATUV(uint vertexID, float animTime)
            {
                // 把时间转成帧号，并对总帧数取模实现动画循环
                int frame = (int)fmod(animTime * _VATFrameRate, _VATFrameCount);
                frame = clamp(frame, 0, (int)_VATFrameCount - 1);

                // 把一维的顶点 ID 映射到 VAT 中二维贴图坐标
                int vX = (int)vertexID % (int)_VATTexWidth;
                int vY = frame * (int)_VATRowsPerFrame + (int)vertexID / (int)_VATTexWidth;

                // +0.5 取像素中心，避免因浮点误差读到相邻像素
                return float2((vX + 0.5) / _VATTexWidth,
                              (vY + 0.5) / _VATTexHeight);
            }

            // 【矩阵运算】将欧拉角（度）转换为旋转矩阵。
            // 这是一个标准的 3D 欧拉角 → 旋转矩阵的公式。
            // 旋转顺序：ZXY（先绕 Z，再绕 X，最后绕 Y），对应 Unity 的欧拉角惯例。
            // 因为 Agent 只用 Y 旋转（rotation.x=0, rotation.z=0），
            // 所以实际效果等同于绕 Y 轴旋转 rotation.y 度。
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

            // 【核心钩子函数 setup()】
            // DrawMeshInstancedIndirect 触发时，GPU 会先为每个实例执行一次 setup()。
            //
            // setup() 的工作：
            // 1. 通过 visibleAgentIndices[unity_InstanceID] 找到真实 Agent 下标
            //    因为 near/mid/far 的 visibleAgentIndices 只包含"可见 Agent"，
            //    unity_InstanceID 不能直接当 agentBuffer 下标
            // 2. 从 agentBuffer 读取该 Agent 的 position/rotation/scale
            // 3. 构造 TRS 变换矩阵覆盖 unity_ObjectToWorld
            // 4. 保存动画时间到 _GlobalAnimationTime，vertex shader 用它计算 VAT 帧
            //
            // 【矩阵组合顺序】
            // unity_ObjectToWorld = T * R * S（先缩放、再旋转、最后平移）
            // HLSL 中 mul(A, B) 的含义是"先应用 B，再应用 A"
            // 所以 mul(trans, mul(rot, scale)) = 先 scale → 再 rot → 最后 trans
            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    // unity_InstanceID 是内置变量，代表当前正在画第几个球（0 到 99999）
                    // 但现在 near/far 批次都是压缩后的列表，所以要先通过 visibleAgentIndices 找真实 agent 下标。
                    uint agentIndex = visibleAgentIndices[unity_InstanceID];
                    AgentData data = agentBuffer[agentIndex];

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

                    // 保存当前实例的动画时间，后面的顶点阶段会用它计算 VAT 帧号。
                    _GlobalAnimationTime = data.currentAnimationTime;
                #endif
            }

            // 【顶点着色器】处理每个物体的顶点变换。
            //
            // 工作步骤：
            // 1. UNITY_SETUP_INSTANCE_ID：提取当前实例 ID（Procedural 模式下会触发 setup()）
            // 2. VAT 采样：用 vertexID + _GlobalAnimationTime 从贴图读取当前帧的顶点位置/法线
            // 3. 坐标变换：因为 setup() 已经更新了 unity_ObjectToWorld，
            //    TransformObjectToHClip 会自动应用实例的 TRS 矩阵
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                // 内部处理宏：提取出 Instance ID，并且在 Procedural 模式下调用上面的 setup() 方法！！（非常关键）
                UNITY_SETUP_INSTANCE_ID(input);
                // 把 ID 传递到下一阶段（片元）
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // ── 步骤 3：VAT 顶点置换 ──────────────────────────────────
                // 在 Procedural Instancing 模式下，从 AgentBuffer 读取当前实体的动画时间，
                // 计算出该顶点在 VAT 贴图中的 UV 坐标，然后采样并覆盖原始 Mesh 的顶点数据。
                // setup() 已经把 unity_ObjectToWorld 更新为该实体的 TRS 矩阵，
                // 所以后续的 TransformObjectToHClip / TransformObjectToWorldNormal 仍然照常工作。
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                {
                    // vertexID 决定“这是 mesh 的第几个顶点”，_GlobalAnimationTime 决定“当前播放到第几帧”。
                    // 二者合起来就能定位到 VAT 贴图中的唯一像素。
                    float2 vatUV = GetVATUV(input.vertexID, _GlobalAnimationTime);

                    // 用 SampleLevel(mip=0) 避免 mip 计算（VAT 纹理无 mipmap）
                    // 简化 shader 仍然采位置 + 法线，用于做一个廉价的假光照。
                    float3 vatPos  = SAMPLE_TEXTURE2D_LOD(_VATPosTex,  sampler_point_clamp, vatUV, 0).rgb;
                    float3 vatNorm = SAMPLE_TEXTURE2D_LOD(_VATNormTex, sampler_point_clamp, vatUV, 0).rgb;

                    // 用 VAT 烘焙出的根空间坐标替换原始 Mesh 的顶点坐标与法线
                    input.positionOS = float4(vatPos, 1.0);
                    input.normalOS   = normalize(vatNorm);
                }
                #endif
                // ─────────────────────────────────────────────────────────

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
                UNITY_SETUP_INSTANCE_ID(input);
                
                float3 lightDir = normalize(float3(0.5, 1.0, -0.5));
                float nDotL = saturate(dot(normalize(input.normalWS), lightDir));
                float3 lighting = nDotL * 0.7 + 0.3;

                float3 patternColor = float3(input.uv.x, input.uv.y, 1.0);

                float3 finalColor = patternColor * _BaseColor.rgb * lighting;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
