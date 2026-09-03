using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Projectiles
{
    /// <summary>
    /// GPU 弹道管理器：负责弹道池分配、槽位搜索、发射请求处理、GPU 模拟调度
    /// 参考 TowerDefense ProjectileGpuManager 实现
    /// </summary>
    public sealed class ProjectileGpuManager : IDisposable
    {
        private ComputeShader _projectileShader;
        private ComputeShader _combatSimulationShader;
        private int _maxProjectiles;

        private ComputeBuffer _projectileBuffer;
        private ComputeBuffer _launchRequestBuffer;
        private bool _ownsBuffers; // 标记是否拥有 buffer 所有权

        private int _kernelSimulate;
        private int _kernelClear;
        private int _kernelClearLaunchRequests = -1;

        // 槽位搜索游标（循环复用）
        private int _searchCursor = 0;

        // 遥测计数器
        private int _overflowCount = 0;
        public int TotalLaunched { get; private set; }

        // §3.3 修复：基于时间窗口的发射历史追踪
        private readonly Queue<float> _launchTimestamps = new Queue<float>(1024);
        private float _estimatedMaxLifetime = 3f; // 默认弹道生命周期
        private float _simulationTime;

        // 发射请求处理 - 多阶段 AsyncGPUReadback
        private readonly List<ProjectileGpuData> _pendingSpawnList = new List<ProjectileGpuData>(4096);
        private AsyncGPUReadbackRequest _launchRequestReadback;
        private AsyncGPUReadbackRequest _positionReadback;
        private AsyncGPUReadbackRequest _targetIndexReadback;
        private bool _readbackPending = false;
        private int _readbackFrameIndex = 0;

        // 预分配接收缓冲（避免每帧 GetData 分配）
        private int[] _launchCountsCache;
        private Vector2[] _positionsCache;
        private int[] _targetIndicesCache;
        private int _lastAgentCount = 0;

        // 日志降频
        private int _logFrameCounter = 0;
        private const int LogInterval = 60;

        /// <summary>
        /// 无参构造函数：延迟初始化，buffer 由 MassGpuBufferManager 分配后通过 Initialize 注入
        /// </summary>
        public ProjectileGpuManager()
        {
            _ownsBuffers = false;
        }

        /// <summary>
        /// 构造函数：分配弹道池和发射请求缓冲
        /// </summary>
        /// <param name="projectileShader">ProjectileSimulation.compute</param>
        /// <param name="maxProjectiles">弹道池容量（默认 16384）</param>
        public ProjectileGpuManager(ComputeShader projectileShader, int maxProjectiles = 16384)
        {
            if (projectileShader == null)
                throw new ArgumentNullException(nameof(projectileShader));
            if (maxProjectiles <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxProjectiles));

            _projectileShader = projectileShader;
            _maxProjectiles = maxProjectiles;
            _ownsBuffers = true;

            // 查找 kernel 索引
            _kernelSimulate = _projectileShader.FindKernel("SimulateProjectiles");
            _kernelClear = _projectileShader.FindKernel("ClearProjectiles");

            // 分配弹道池 ComputeBuffer
            _projectileBuffer = new ComputeBuffer(_maxProjectiles, ProjectileGpuData.Stride);

            // 初始化：所有槽位标记为空闲（targetAgentIndex = -1）
            ClearAllProjectiles();
        }

        /// <summary>
        /// 注入式初始化：接收 BufferManager 分配的 buffer（不自行创建、不自行释放）
        /// </summary>
        public void Initialize(ComputeShader projectileShader, ComputeShader combatSimulationShader, ComputeBuffer projectileBuffer, int maxProjectiles, ComputeBuffer launchRequestBuffer, int agentCount)
        {
            if (projectileShader == null)
                throw new ArgumentNullException(nameof(projectileShader));
            if (combatSimulationShader == null)
                throw new ArgumentNullException(nameof(combatSimulationShader));
            if (projectileBuffer == null)
                throw new ArgumentNullException(nameof(projectileBuffer));
            if (launchRequestBuffer == null)
                throw new ArgumentNullException(nameof(launchRequestBuffer));
            if (maxProjectiles <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxProjectiles));
            if (agentCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(agentCount));

            _projectileShader = projectileShader;
            _combatSimulationShader = combatSimulationShader;
            _projectileBuffer = projectileBuffer;
            _maxProjectiles = maxProjectiles;
            _launchRequestBuffer = launchRequestBuffer;
            _ownsBuffers = false;

            _kernelSimulate = _projectileShader.FindKernel("SimulateProjectiles");
            _kernelClear = _projectileShader.FindKernel("ClearProjectiles");
            _kernelClearLaunchRequests = _combatSimulationShader != null && _combatSimulationShader.HasKernel("ClearLaunchRequests")
                ? _combatSimulationShader.FindKernel("ClearLaunchRequests")
                : -1;

            _searchCursor = 0;
            _overflowCount = 0;
            TotalLaunched = 0;

            // §3.3 修复：清空发射历史
            _launchTimestamps.Clear();
            _simulationTime = 0f;

            // 预分配接收缓冲
            _lastAgentCount = agentCount;
            _launchCountsCache = new int[agentCount];
            _positionsCache = new Vector2[agentCount];
            _targetIndicesCache = new int[agentCount];

            _pendingSpawnList.Clear();
        }

        /// <summary>
        /// 清空所有弹道槽位（调用 ClearProjectiles kernel）
        /// </summary>
        public void ClearAllProjectiles()
        {
            if (_projectileBuffer == null || _projectileShader == null)
                return;

            _projectileShader.SetBuffer(_kernelClear, MassGpuShaderPropertyIds.ProjectileBufferId, _projectileBuffer);
            _projectileShader.SetInt(MassGpuShaderPropertyIds.MaxProjectilesId, _maxProjectiles);

            int threadGroups = Mathf.Max(1, Mathf.CeilToInt(_maxProjectiles / 64f));
            _projectileShader.Dispatch(_kernelClear, threadGroups, 1, 1);

            _searchCursor = 0;
            _overflowCount = 0;

            // §3.3 修复：清空发射历史
            _launchTimestamps.Clear();
        }

        /// <summary>
        /// 分配弹道槽位并初始化新弹道
        /// </summary>
        public void LaunchProjectile(
            Vector3 sourcePos,
            Vector3 sourceVelocity,
            int targetIndex,
            Vector3 targetPos,
            float damage,
            int sourceTeamId,
            float projectileSpeed,
            float gravity,
            float hitRadius,
            float maxLifetime,
            float trailLength = 1f,
            float launchTime = -1f)
        {
            if (_projectileBuffer == null || _maxProjectiles <= 0 || projectileSpeed <= 0f)
                return;

            // 零距离守卫：避免 NaN
            Vector3 delta = targetPos - sourcePos;
            float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
            if (horizontalDistance < 0.001f)
                return; // 拒绝零距离发射

            // CPU does not synchronously know which GPU slots have hit. Use the maximum
            // configured lifetime as a conservative ownership window and drop new shots
            // rather than silently overwriting a projectile that may still be active.
            float resolvedLaunchTime = launchTime >= 0f ? launchTime : Time.time;
            _simulationTime = resolvedLaunchTime;
            int estimatedActive = EstimateActiveProjectiles();
            if (estimatedActive >= _maxProjectiles)
            {
                _overflowCount++;
                return;
            }

            // 计算初速度方向
            Vector3 velocity = delta.normalized * projectileSpeed + sourceVelocity;

            // 抛物线弹道：计算垂直初速度补偿重力
            if (gravity < -0.01f)
            {
                float timeToTarget = horizontalDistance / projectileSpeed;
                float heightDiff = targetPos.y - sourcePos.y;

                // 垂直分量：v_y = (Δh / t) - 0.5 * g * t
                velocity.x = delta.x / horizontalDistance * projectileSpeed + sourceVelocity.x;
                velocity.z = delta.z / horizontalDistance * projectileSpeed + sourceVelocity.z;
                velocity.y = sourceVelocity.y + (heightDiff / timeToTarget) - 0.5f * gravity * timeToTarget;
            }

            // 初始化弹道数据
            ProjectileGpuData projectile = new ProjectileGpuData
            {
                position = sourcePos,
                velocity = velocity,
                launchTime = resolvedLaunchTime,
                damage = damage,
                targetAgentIndex = targetIndex,
                sourceTeamId = sourceTeamId,
                hitRadius = hitRadius,
                gravity = gravity,
                maxLifetime = maxLifetime,
                trailLength = trailLength,
                padding = Vector2.zero
            };

            // 加入待发射列表（批量上传优化）
            _pendingSpawnList.Add(projectile);

            // §3.3 修复：记录发射时间戳用于活跃数估算
            _launchTimestamps.Enqueue(projectile.launchTime);

            TotalLaunched++;
        }

        /// <summary>
        /// 批量上传待发射弹道到 GPU（减少 SetData 调用次数）
        /// </summary>
        private void FlushPendingProjectiles()
        {
            if (_pendingSpawnList.Count == 0)
                return;

            // Upload at most two contiguous ranges when the ring wraps. Keeping the slot
            // cursor here (and only here) avoids double-advancing it while queuing shots.
            int startSlot = _searchCursor;
            int writeCount = Mathf.Min(_pendingSpawnList.Count, _maxProjectiles);
            int firstCount = Mathf.Min(writeCount, _maxProjectiles - startSlot);
            _projectileBuffer.SetData(_pendingSpawnList, 0, startSlot, firstCount);
            int wrappedCount = writeCount - firstCount;
            if (wrappedCount > 0)
                _projectileBuffer.SetData(_pendingSpawnList, firstCount, 0, wrappedCount);

            _searchCursor = (startSlot + writeCount) % _maxProjectiles;
            _pendingSpawnList.Clear();
        }

        /// <summary>
        /// 处理发射请求（从 launchRequestBuffer 读取 GPU 请求）
        /// 阶段 2/3 完整实现：真实位置 + 多兵种参数 + GC 优化
        /// </summary>
        public void ProcessLaunchRequests(
            ComputeBuffer launchRequestBuffer,
            ComputeBuffer agentPositionBuffer,
            ComputeBuffer targetAgentIndexBuffer,
            int[] unitTypeIndices,
            UnitTypeGpuSettings[] unitTypeSettings,
            int agentCount,
            float simulationTime)
        {
            if (launchRequestBuffer == null || agentPositionBuffer == null ||
                targetAgentIndexBuffer == null || unitTypeIndices == null ||
                unitTypeIndices.Length < agentCount || unitTypeSettings == null || agentCount <= 0)
                return;

            _simulationTime = Mathf.Max(_simulationTime, simulationTime);

            // §3.3 修复：更新最大生命周期估算（用于活跃数计算）
            UpdateMaxLifetimeEstimate(unitTypeSettings);

            // 重新分配缓冲（如果 agent 数量变化）
            if (agentCount != _lastAgentCount)
            {
                _lastAgentCount = agentCount;
                _launchCountsCache = new int[agentCount];
                _positionsCache = new Vector2[agentCount];
                _targetIndicesCache = new int[agentCount];
            }

            // 启动多个异步 GPU 回读（阶段 2/3）
            if (!_readbackPending)
            {
                _launchRequestReadback = AsyncGPUReadback.Request(launchRequestBuffer);
                _positionReadback = AsyncGPUReadback.Request(agentPositionBuffer);
                _targetIndexReadback = AsyncGPUReadback.Request(targetAgentIndexBuffer);

                // The readback commands above snapshot the counters before this clear in
                // the same GPU command stream. Clearing immediately gives subsequent
                // combat frames a fresh buffer instead of erasing requests accumulated
                // while the asynchronous copies are in flight.
                ClearLaunchRequestsGpu(launchRequestBuffer, agentCount);
                _readbackPending = true;
                _readbackFrameIndex = Time.frameCount;
            }

            // 检查所有回读是否完成
            if (_readbackPending &&
                _launchRequestReadback.done &&
                _positionReadback.done &&
                _targetIndexReadback.done)
            {
                _readbackPending = false;

                // 检查回读错误
                if (_launchRequestReadback.hasError || _positionReadback.hasError ||
                    _targetIndexReadback.hasError)
                {
                    Debug.LogWarning("ProjectileGpuManager: AsyncGPUReadback failed for launch requests.");
                    return;
                }

                // 读取所有数据到预分配缓冲（避免每次 GetData 分配）
                _launchRequestReadback.GetData<int>().CopyTo(_launchCountsCache);
                _positionReadback.GetData<Vector2>().CopyTo(_positionsCache);
                _targetIndexReadback.GetData<int>().CopyTo(_targetIndicesCache);

                // 遍历请求（限流：最多 4096 个/帧）
                int processedCount = 0;
                const int MaxLaunchesPerFrame = 4096;

                for (int agentIdx = 0; agentIdx < agentCount && processedCount < MaxLaunchesPerFrame; agentIdx++)
                {
                    int launchCount = _launchCountsCache[agentIdx];
                    if (launchCount <= 0)
                        continue;

                    int targetIdx = _targetIndicesCache[agentIdx];
                    if (targetIdx < 0 || targetIdx >= agentCount)
                        continue; // 无效目标

                    // 读取攻击者数据
                    Vector2 sourcePos2D = _positionsCache[agentIdx];
                    Vector3 sourcePos = new Vector3(sourcePos2D.x, 0f, sourcePos2D.y);
                    // 读取目标位置
                    Vector2 targetPos2D = _positionsCache[targetIdx];
                    Vector3 targetPos = new Vector3(targetPos2D.x, 0f, targetPos2D.y);

                    // 读取兵种参数（阶段 3）
                    int unitTypeIdx = unitTypeIndices[agentIdx];
                    if (unitTypeIdx < 0 || unitTypeIdx >= unitTypeSettings.Length)
                        continue; // 无效兵种索引

                    UnitTypeGpuSettings settings = unitTypeSettings[unitTypeIdx];
                    int sourceTeamId = settings.teamId;

                    // 验证是远程单位（projectileRange > 0）
                    if (settings.projectileRange <= 0f)
                        continue; // 近战单位不发射弹道

                    // 批量发射（LOD 降频补偿）
                    for (int shot = 0; shot < launchCount && processedCount < MaxLaunchesPerFrame; shot++)
                    {
                        LaunchProjectile(
                            sourcePos: sourcePos,
                            sourceVelocity: Vector3.zero,
                            targetIndex: targetIdx,
                            targetPos: targetPos,
                            damage: settings.attackDamage,
                            sourceTeamId: sourceTeamId,
                            projectileSpeed: settings.projectileSpeed,
                            gravity: settings.projectileGravity,
                            hitRadius: settings.projectileHitRadius,
                            maxLifetime: settings.projectileMaxLifetime,
                            trailLength: 1f,
                            launchTime: simulationTime
                        );

                        processedCount++;
                    }
                }

                // 批量上传所有待发射弹道
                FlushPendingProjectiles();

                // 降频日志（每60帧打一次，或无发射时不打）
                if (processedCount > 0)
                {
                    _logFrameCounter++;
                    if (_logFrameCounter >= LogInterval)
                    {
                        _logFrameCounter = 0;
                        int latency = Time.frameCount - _readbackFrameIndex;
                        Debug.Log($"ProjectileGpuManager: Launched {processedCount} projectiles (latency: {latency} frames)");
                    }
                }

            }
        }

        /// <summary>
        /// 派发 GPU kernel 清零发射请求计数器（避免采样稀释）
        /// </summary>
        private void ClearLaunchRequestsGpu(ComputeBuffer launchRequestBuffer, int agentCount)
        {
            if (_combatSimulationShader == null || launchRequestBuffer == null)
                return;

            if (_kernelClearLaunchRequests < 0)
                return;

            _combatSimulationShader.SetBuffer(_kernelClearLaunchRequests, MassGpuShaderPropertyIds.LaunchRequestBufferId, launchRequestBuffer);
            int threadGroups = Mathf.Max(1, Mathf.CeilToInt(agentCount / 64f));
            _combatSimulationShader.Dispatch(_kernelClearLaunchRequests, threadGroups, 1, 1);
        }

        /// <summary>
        /// 清理过期弹道（基于时间戳）
        /// </summary>
        public void ClearExpiredProjectiles(float currentTime)
        {
            // GPU 端已在 SimulateProjectiles kernel 中检查过期（age > maxLifetime）
            // CPU 端不需要额外清理
            // 保留此方法以保持 API 兼容性
        }

        /// <summary>
        /// 获取当前活跃弹道数量（targetIndex >= 0 的槽位）
        /// </summary>
        public int ActiveCount
        {
            get
            {
                // §3.3 修复：基于时间窗口的活跃数估算
                return EstimateActiveProjectiles();
            }
        }

        /// <summary>
        /// §3.3 修复：估算当前活跃弹道数
        /// 原理：统计时间窗口内的发射次数（窗口大小 = 最大弹道生命周期）
        /// </summary>
        private int EstimateActiveProjectiles()
        {
            float expirationThreshold = _simulationTime - _estimatedMaxLifetime;

            // 移除已过期的时间戳
            while (_launchTimestamps.Count > 0 && _launchTimestamps.Peek() < expirationThreshold)
            {
                _launchTimestamps.Dequeue();
            }

            // 活跃数 = min(时间窗口内发射数, 池容量)
            return Mathf.Min(_launchTimestamps.Count, _maxProjectiles);
        }

        /// <summary>
        /// §3.3 修复：更新最大弹道生命周期估算
        /// 从所有兵种配置中取最大值，用于活跃数时间窗口计算
        /// </summary>
        private void UpdateMaxLifetimeEstimate(UnitTypeGpuSettings[] unitTypeSettings)
        {
            if (unitTypeSettings == null || unitTypeSettings.Length == 0)
                return;

            float maxLifetime = 3f; // 默认值
            for (int i = 0; i < unitTypeSettings.Length; i++)
            {
                if (unitTypeSettings[i].projectileRange > 0f)
                {
                    maxLifetime = Mathf.Max(maxLifetime, unitTypeSettings[i].projectileMaxLifetime);
                }
            }

            _estimatedMaxLifetime = maxLifetime;
        }

        /// <summary>
        /// 获取槽位溢出次数
        /// </summary>
        public int OverflowCount
        {
            get { return _overflowCount; }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 只有自行分配的 buffer 才释放
            if (_ownsBuffers)
            {
                _projectileBuffer?.Release();
                _launchRequestBuffer?.Release();
            }

            _projectileBuffer = null;
            _launchRequestBuffer = null;
            _kernelClearLaunchRequests = -1;
            _readbackPending = false;
            _pendingSpawnList.Clear();
            _launchTimestamps.Clear();
        }
    }
}
