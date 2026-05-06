Shader "Universal Render Pipeline/MassGPUPhysics_Stage6/BillboardInstancedAgentShader_Stage6"
{
    // ============================================================
    // BillboardInstancedAgentShader_Stage6 — 远距离 LOD 的广告牌着色器。
    //
    // 【什么是 Billboard（广告牌）？】
    // Billboard 是一个始终面向摄像机的四边形面片。无论摄像机从哪个角度看，
    // 这个面片都会自动旋转到正对摄像机。对于远处的小角色来说，人眼分辨不出
    // 它是一个 3D 模型还是一个 2D 贴片，所以用 Billboard 代替完整网格可以
    // 大幅减少三角形数量（从数万面减到 2 面）。
    //
    // 【这个 Shader 的工作流程】
    // 1. setup()：从 agentBuffer 读取当前 Agent 的位置（给 Unity 一个平移矩阵兜底）
    // 2. vert()：根据摄像机朝向，把 4 个顶点的四边形旋转到面向摄像机
    // 3. frag()：做一个简单的人形轮廓效果（上半身亮、下半身暗、边缘柔化）
    //
    // 【使用场景】
    // 这个 Shader 用于 Far LOD（midLodRadius 之外的 Agent）。
    // 不采样 VAT 纹理（省显存带宽），用动画时间做一个简单的亮度摆动模拟"在动"。
    // ============================================================
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

            Cull Off // 关闭背面剔除，让 BillBoard 从正反两面都可见

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing // 支持 GPU Instancing
            #pragma instancing_options procedural:setup // 声明程序化 Instancing，渲染前调用 setup()
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // AgentData：必须和 C# / Compute Shader 的布局完全一致。
            // Billboard 模式下只需要 position、scale 和 animationTime。
            struct AgentData
            {
                float3 position;
                float3 rotation;
                float3 scale;
                float3 velocity;
                int currentState;
                float currentAnimationTime;
            };

            // ── 程序化 Instancing 的 GPU Buffer ──
            // agentBuffer：所有 Agent 的完整数据
            // visibleAgentIndices：当前批次要绘制的 Agent 索引列表（只包含远距离可见的）
            // _CurrentAgent：当前实例的数据缓存（setup 填充，vert 使用）
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                StructuredBuffer<AgentData> agentBuffer;
                StructuredBuffer<uint> visibleAgentIndices;
                static AgentData _CurrentAgent;
            #endif

            // Material 级常量缓冲区（兼容 SRP Batcher）
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _BillboardWidth;
                float _BillboardHeight;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION; // 本地空间顶点坐标
                float2 uv : TEXCOORD0;        // UV 坐标
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION; // 裁剪空间坐标
                float2 uv : TEXCOORD0;           // 传递 UV
                half shade : TEXCOORD1;          // 亮度/阴影因子
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // ─────────────────────────────────────────────────────
            // setup()：程序化 Instancing 的钩子函数。
            // Unity 在绘制每个实例之前调用它，用来覆盖该实例的 Transform。
            //
            // Billboard 模式下只设置一个简单的平移矩阵（位置），因为
            // 朝向摄像机的旋转在 vertex shader 里实时计算。
            // ─────────────────────────────────────────────────────
            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    // 通过 visibleAgentIndices 把 unity_InstanceID 映射到真实的 agent 下标
                    uint agentIndex = visibleAgentIndices[unity_InstanceID];
                    _CurrentAgent = agentBuffer[agentIndex];

                    // 只设平移矩阵（朝向由 vertex shader 计算）。
                    // unity_ObjectToWorld = 单位矩阵 + 平移，这样顶点在 vertex shader 里
                    // 相当于处于世界空间原点附近的一个本地四边形。
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

            // ─────────────────────────────────────────────────────
            // vert()：Billboard 顶点着色器。
            //
            // Billboard 的核心算法：
            // 1. 从 UNITY_MATRIX_I_V（view 矩阵的逆）提取摄像机的 right 和 up 向量
            // 2. 把本地四边形的 x 映射到世界 right 方向，y 映射到世界 up 方向
            // 3. 以 Agent 位置为原点，构建面向摄像机的四边形
            //
            // 输入：本地四边形顶点 (-0.5~0.5, 0~1, 0)
            // 输出：世界空间中面向摄像机的四边形
            // ─────────────────────────────────────────────────────
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                    // 从逆视图矩阵提取摄像机的世界空间 right 和 up 轴。
                    // UNITY_MATRIX_I_V = 视图矩阵的逆矩阵。
                    // I_V 的第 0 列 = 摄像机在世界空间中的 right 方向
                    // I_V 的第 1 列 = 摄像机在世界空间中的 up 方向
                    // 取前 3 个分量并归一化，得到单位方向向量。
                    float3 cameraRightWS = normalize(float3(UNITY_MATRIX_I_V[0][0], UNITY_MATRIX_I_V[1][0], UNITY_MATRIX_I_V[2][0]));
                    float3 cameraUpWS = normalize(float3(UNITY_MATRIX_I_V[0][1], UNITY_MATRIX_I_V[1][1], UNITY_MATRIX_I_V[2][1]));

                    // 带缩放的实际 billboard 尺寸
                    float width = _BillboardWidth * max(_CurrentAgent.scale.x, 0.001);
                    float height = _BillboardHeight * max(_CurrentAgent.scale.y, 0.001);

                    // 构建世界空间位置：
                    // _CurrentAgent.position 是脚底位置
                    // + cameraRightWS * (input.positionOS.x * width)   ← 水平展开
                    // + cameraUpWS * (input.positionOS.y * height)     ← 垂直展开
                    // 因为 input.positionOS.y 的范围是 0~1（四边形从底部到顶部），
                    // 所以 billboard 从 Agent 脚底位置向上立起来。
                    float3 positionWS = _CurrentAgent.position
                        + cameraRightWS * (input.positionOS.x * width)
                        + cameraUpWS * (input.positionOS.y * height);

                    // 变换到裁剪空间
                    output.positionCS = TransformWorldToHClip(positionWS);
                    output.uv = input.uv;

                    // 用动画时间做轻微的亮度波动（sin 波形），
                    // 远处 Billboard 不采 VAT 纹理，靠这个给一点"活着"的感觉。
                    // sin(animTime * 2π) 在 -1~1 之间，映射到 0.6~1.0 的亮度范围。
                    output.shade = 0.8h + 0.2h * (half)sin(_CurrentAgent.currentAnimationTime * 6.28318);
                #else
                    // 非程序化 Instancing 的 fallback（编辑器预览用）
                    output.positionCS = TransformObjectToHClip(input.positionOS);
                    output.uv = input.uv;
                    output.shade = 1;
                #endif

                return output;
            }

            // ─────────────────────────────────────────────────────
            // frag()：片元着色器 —— 画出一个简单的人形轮廓。
            //
            // 视觉效果：
            // - edge：水平边缘柔化（smoothstep），让四边形不是硬边矩形
            // - vertical：从下到上由暗变亮（模拟光照：下半身暗、上半身亮）
            // - shade：动画时间驱动的亮度摆动
            // 合在一起就是"远处看像个人"的便宜效果。
            // ─────────────────────────────────────────────────────
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 水平边缘柔化：UV.x 在 0~0.12 淡入，0.88~1 淡出，中间是全亮
                half edge = smoothstep(0.0h, 0.12h, input.uv.x) * (1.0h - smoothstep(0.88h, 1.0h, input.uv.x));
                // 垂直渐变：底部 0.65 倍亮度，顶部 1.0 倍亮度
                half vertical = lerp(0.65h, 1.0h, input.uv.y);
                // 综合颜色：基础色 × 动画亮度 × 垂直渐变 × 边缘柔化
                half3 color = _BaseColor.rgb * input.shade * vertical * lerp(0.55h, 1.0h, edge);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
