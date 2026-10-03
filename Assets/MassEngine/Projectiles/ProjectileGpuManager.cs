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
    public sealed partial class ProjectileGpuManager : IDisposable
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

        // 遥测计数器
        private int _overflowCount = 0;
        public int TotalLaunched { get; private set; }

        /// <summary>
        /// Optional immutable terrain snapshot, injected after Initialize by the owner.
        /// Null preserves the legacy y=0 launch path; this manager does not own the surface.
        /// </summary>
        public TerrainSurface Surface { get; set; }

        /// <summary>
        /// Optional CPU-side per-unit-type impact splash radius (index = unit type index), filled by the owner.
        /// Null or 0 keeps the single-target shot. Not uploaded in UnitTypeGpuSettings (its 144-byte layout is unchanged).
        /// </summary>
        public float[] UnitTypeSplashRadii { get; set; }
        // Spawn scale metadata is supplied by the manager; never read a full AgentData
        // array back each frame just to resolve a fixed-size launch offset.

        // 发射请求处理 - 多阶段 AsyncGPUReadback
        private readonly List<ProjectileGpuData> _pendingSpawnList = new List<ProjectileGpuData>(4096);
        private bool _readbackPending = false;
        private int _launchAgentCursor; // Retain a completed snapshot until its bounded drain finishes.
        private int _readbackGeneration, _readbacksRemaining;
        private bool _readbackFailed;
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

            CancelReadbacks();
            ReleasePoolStorage();
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

            _overflowCount = 0;
            TotalLaunched = 0;

            TotalRequested = 0;
            ResetPoolStorage();

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
            CancelReadbacks();
            _pendingSpawnList.Clear();
            if (_projectileBuffer == null || _projectileShader == null)
                return;

            _projectileShader.SetBuffer(_kernelClear, MassGpuShaderPropertyIds.ProjectileBufferId, _projectileBuffer);
            _projectileShader.SetInt(MassGpuShaderPropertyIds.MaxProjectilesId, _maxProjectiles);

            int threadGroups = Mathf.Max(1, Mathf.CeilToInt(_maxProjectiles / 64f));
            _projectileShader.Dispatch(_kernelClear, threadGroups, 1, 1);

            _overflowCount = 0;

            ResetPoolStorage();
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
            float launchTime = -1f,
            int sourceAgentIndex = -1,
            float arcClearance = 0f,
            float splashRadius = 0f)
        {
            if (_projectileBuffer == null || _maxProjectiles <= 0 || projectileSpeed <= 0f)
                return;

            // 零距离守卫：避免 NaN
            Vector3 delta = targetPos - sourcePos;
            float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
            if (horizontalDistance < 0.001f)
                return; // 拒绝零距离发射

            float timeToTarget = arcClearance > 0f
                ? ProjectileBallistics.FlightTime(sourcePos, targetPos, projectileSpeed, gravity, maxLifetime, arcClearance)
                : horizontalDistance / projectileSpeed;
            if (timeToTarget <= 0) return;

            // Reuse only slots confirmed empty by a generation/watermark guarded GPU snapshot.
            float resolvedLaunchTime = launchTime >= 0f ? launchTime : Time.time;
            if (_freeSlots.Count == 0)
            {
                _overflowCount++; // Direct API rejection; production request draining defers instead.
                return;
            }
            int slot = _freeSlots.Pop();
            _poolHighWater = Mathf.Max(_poolHighWater, slot + 1);
            _slotInUse[slot] = true;
            _slotLease[slot] = ++_leaseSerial;
            _reservedSlots++;
            _pendingSpawnSlots.Add(slot);

            // 计算初速度方向
            Vector3 velocity = delta.normalized * projectileSpeed + sourceVelocity;

            // 抛物线弹道：计算垂直初速度补偿重力
            if (gravity < -0.01f)
            {
                float heightDiff = targetPos.y - sourcePos.y;

                // 垂直分量：v_y = (Δh / t) - 0.5 * g * t
                velocity.x = delta.x / timeToTarget + sourceVelocity.x;
                velocity.z = delta.z / timeToTarget + sourceVelocity.z;
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
                sourceAgentIndexPlusOne = sourceAgentIndex + 1,
                splashRadius = float.IsNaN(splashRadius) || float.IsInfinity(splashRadius) ? 0f : Mathf.Max(0f, splashRadius)
            };

            // 加入待发射列表（批量上传优化）
            _pendingSpawnList.Add(projectile);

            TotalLaunched++;
        }

        /// <summary>
        /// 批量上传待发射弹道到 GPU（减少 SetData 调用次数）
        /// </summary>
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
            float simulationTime,
            Vector3[] agentScales = null,
            float[] flatGroundHeights = null)
        {
            if (launchRequestBuffer == null || agentPositionBuffer == null ||
                targetAgentIndexBuffer == null || unitTypeIndices == null ||
                unitTypeIndices.Length < agentCount || unitTypeSettings == null || agentCount <= 0)
                return;

            RequestPoolSnapshot();

            // 重新分配缓冲（如果 agent 数量变化）
            if (agentCount != _lastAgentCount)
            {
                CancelReadbacks();
                _lastAgentCount = agentCount;
                _launchCountsCache = new int[agentCount];
                _positionsCache = new Vector2[agentCount];
                _targetIndicesCache = new int[agentCount];
            }

            // 启动多个异步 GPU 回读（阶段 2/3）
            if (!_readbackPending)
            {
                _readbackPending = true;
                _readbackFailed = false;
                _readbacksRemaining = 3;
                _launchAgentCursor = 0;
                int generation = ++_readbackGeneration;
                // A completed request's native data expires after one frame. Copy each
                // part in its completion callback; polling three handles together can
                // observe an already-expired first part while waiting for the last.
                AsyncGPUReadback.Request(launchRequestBuffer, request => CaptureReadback(request, generation, 0));
                AsyncGPUReadback.Request(agentPositionBuffer, request => CaptureReadback(request, generation, 1));
                AsyncGPUReadback.Request(targetAgentIndexBuffer, request => CaptureReadback(request, generation, 2));

                // The readback commands above snapshot the counters before this clear in
                // the same GPU command stream. Clearing immediately gives subsequent
                // combat frames a fresh buffer instead of erasing requests accumulated
                // while the asynchronous copies are in flight.
                ClearLaunchRequestsGpu(launchRequestBuffer, agentCount);
                _readbackFrameIndex = Time.frameCount;
            }

            // 检查所有回读是否完成
            if (_readbackPending && _readbacksRemaining == 0)
            {
                // Keep ownership of this snapshot across frames; the GPU counters
                // were already cleared when the asynchronous snapshot was issued.
                // 检查回读错误
                if (_readbackFailed)
                {
                    _readbackPending = false;
                    _launchAgentCursor = 0;
                    Debug.LogWarning("ProjectileGpuManager: AsyncGPUReadback failed for launch requests.");
                    return;
                }

                // 遍历请求（限流：最多 4096 个/帧）
                int processedCount = 0;
                const int MaxLaunchesPerFrame = 4096;

                for (int agentIdx = _launchAgentCursor; agentIdx < agentCount && processedCount < MaxLaunchesPerFrame; agentIdx++)
                {
                    _launchAgentCursor = agentIdx + 1;
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
                    if (Surface != null)
                    {
                        // Never silently fall back to flat ground for an invalid terrain query.
                        // Positions are the same XZ snapshot as the request; only Y is reconstructed.
                        if (!Surface.TrySample(sourcePos2D, out TerrainSurfaceSample sourceSample) ||
                            !Surface.TrySample(targetPos2D, out TerrainSurfaceSample targetSample))
                            continue;
                        sourcePos.y = sourceSample.Position.y;
                        targetPos.y = targetSample.Position.y;
                    }

                    // 读取兵种参数（阶段 3）
                    int unitTypeIdx = unitTypeIndices[agentIdx];
                    if (unitTypeIdx < 0 || unitTypeIdx >= unitTypeSettings.Length)
                        continue; // 无效兵种索引

                    UnitTypeGpuSettings settings = unitTypeSettings[unitTypeIdx];
                    int targetType = unitTypeIndices[targetIdx];
                    if (targetType < 0 || targetType >= unitTypeSettings.Length) continue;
                    UnitTypeGpuSettings targetSettings = unitTypeSettings[targetType];
                    Vector3 sourceScale = agentScales != null && agentIdx < agentScales.Length ? agentScales[agentIdx] : Vector3.one;
                    Vector3 targetScale = agentScales != null && targetIdx < agentScales.Length ? agentScales[targetIdx] : Vector3.one;
                    if (Surface == null && flatGroundHeights != null)
                    {
                        if (agentIdx < flatGroundHeights.Length) sourcePos.y = flatGroundHeights[agentIdx];
                        if (targetIdx < flatGroundHeights.Length) targetPos.y = flatGroundHeights[targetIdx];
                    }
                    sourcePos.y += Mathf.Max(0.05f, settings.projectileOriginHeight) * Mathf.Max(0.01f, Mathf.Abs(sourceScale.y));
                    targetPos.y += Mathf.Max(0.05f, targetSettings.projectileTargetHeight) * Mathf.Max(0.01f, Mathf.Abs(targetScale.y));
                    // Approximate facing by shot direction; no per-frame bone/rotation readback.
                    Vector3 forward = new Vector3(targetPos.x - sourcePos.x, 0f, targetPos.z - sourcePos.z);
                    float offset = Mathf.Max(0.05f, settings.agentRadius) * Mathf.Max(Mathf.Abs(sourceScale.x), Mathf.Abs(sourceScale.z));
                    // Do not move the muzzle beyond a very close target.
                    sourcePos += forward.normalized * Mathf.Min(offset, forward.magnitude * 0.25f);
                    int sourceTeamId = settings.teamId;

                    // 验证是远程单位（projectileRange > 0）
                    if (settings.projectileRange <= 0f)
                        continue; // 近战单位不发射弹道

                    // 批量发射（LOD 降频补偿）
                    for (int shot = 0; shot < launchCount && processedCount < MaxLaunchesPerFrame; shot++)
                    {
                        if (_freeSlots.Count == 0)
                        {
                            CapacityDeferralCount++;
                            break; // Keep this shot and the snapshot tail; never count it as consumed.
                        }
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
                            trailLength: settings.projectileTrailLength,
                            launchTime: simulationTime,
                            sourceAgentIndex: agentIdx,
                            arcClearance: ProjectileBallistics.Clearance(settings.projectileTargetHeight, sourceScale.y,
                                targetSettings.projectileTargetHeight, targetScale.y, forward.magnitude),
                            splashRadius: UnitTypeSplashRadii != null && unitTypeIdx < UnitTypeSplashRadii.Length ? UnitTypeSplashRadii[unitTypeIdx] : 0f
                        );

                        processedCount++;
                        _launchCountsCache[agentIdx]--;
                    }
                    if (_launchCountsCache[agentIdx] > 0)
                    {
                        _launchAgentCursor = agentIdx;
                        break;
                    }
                }
                if (_launchAgentCursor >= agentCount) _readbackPending = false;

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

        private void CaptureReadback(AsyncGPUReadbackRequest request, int generation, int part)
        {
            if (!_readbackPending || generation != _readbackGeneration) return;
            if (request.hasError) _readbackFailed = true;
            else if (part == 0)
            {
                request.GetData<int>().CopyTo(_launchCountsCache);
                for (int i = 0; i < _launchCountsCache.Length; i++) TotalRequested += Mathf.Max(0, _launchCountsCache[i]);
            }
            else if (part == 1) request.GetData<Vector2>().CopyTo(_positionsCache);
            else request.GetData<int>().CopyTo(_targetIndicesCache);
            _readbacksRemaining--;
        }

        private void CancelReadbacks()
        {
            _readbackGeneration++;
            _readbackPending = false;
            _readbacksRemaining = 0;
            _launchAgentCursor = 0;
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

        /// <summary>CPU leases, including pending uploads and asynchronous recycling delay; not draw count.</summary>
        public int ActiveCount => _reservedSlots;

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
            Surface = null;
            _kernelClearLaunchRequests = -1;
            CancelReadbacks();
            _pendingSpawnList.Clear();
            ReleasePoolStorage();
        }
    }
}
