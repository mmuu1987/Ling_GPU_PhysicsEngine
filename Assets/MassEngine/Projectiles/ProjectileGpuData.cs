using UnityEngine;
using System.Runtime.InteropServices;

namespace MassEngine.Projectiles
{
    /// <summary>
    /// GPU 弹道数据结构（64 字节对齐）
    /// 用于 ComputeBuffer 和 Compute Shader 之间的数据交换
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProjectileGpuData
    {
        public Vector3 position;        // 当前位置 (12 bytes)
        public float launchTime;        // 发射时间（用于 TTL 计算）(4 bytes)

        public Vector3 velocity;        // 当前速度（含重力影响）(12 bytes)
        public float damage;            // 伤害值 (4 bytes)

        public int targetAgentIndex;    // 目标 Agent 索引（-1 = 无目标/已销毁）(4 bytes)
        public int sourceTeamId;        // 发射方队伍（用于友军过滤）(4 bytes)
        public float hitRadius;         // 命中半径（碰撞检测）(4 bytes)
        public float gravity;           // 重力加速度（0 = 直线，-9.8 = 抛物线）(4 bytes)

        public float maxLifetime;       // 最大飞行时间（秒）(4 bytes)
        public float trailLength;       // 曳光长度（渲染用）(4 bytes)
        public Vector2 padding;         // 对齐填充 (8 bytes)

        // 总计：64 字节
        public const int Stride = 64;

        /// <summary>
        /// 创建一个新的弹道数据（初始化为空闲状态）
        /// </summary>
        public static ProjectileGpuData CreateEmpty()
        {
            return new ProjectileGpuData
            {
                targetAgentIndex = -1,  // -1 表示空闲槽位
                position = Vector3.zero,
                velocity = Vector3.zero,
                launchTime = 0f,
                damage = 0f,
                sourceTeamId = -1,
                hitRadius = 0.5f,
                gravity = 0f,
                maxLifetime = 5f,
                trailLength = 1f,
                padding = Vector2.zero
            };
        }
    }
}
