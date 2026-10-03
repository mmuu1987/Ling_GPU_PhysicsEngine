using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Projectiles
{
    public sealed partial class ProjectileGpuManager
    {
        private readonly Stack<int> _freeSlots = new Stack<int>();
        private readonly List<int> _pendingSpawnSlots = new List<int>(4096);
        private bool[] _slotInUse;
        private ulong[] _slotLease;
        private ulong _leaseSerial;
        private int _reservedSlots, _poolGeneration, _poolHighWater;
        private bool _poolSnapshotPending;
        private ComputeBuffer _poolOccupancy, _spawnUpload, _spawnSlotUpload;
        private int _snapshotKernel, _spawnKernel;
        public long TotalRequested { get; private set; }
        public int CapacityDeferralCount { get; private set; }
        public int ReclaimedSlots { get; private set; }

        private void ResetPoolStorage()
        {
            _poolGeneration++;
            _poolSnapshotPending = false;
            _leaseSerial = 0;
            _reservedSlots = 0;
            _poolHighWater = 0;
            CapacityDeferralCount = 0;
            ReclaimedSlots = 0;
            _pendingSpawnSlots.Clear();
            _freeSlots.Clear();
            if (_poolOccupancy == null)
            {
                _poolOccupancy = new ComputeBuffer(_maxProjectiles, sizeof(int));
                _spawnUpload = new ComputeBuffer(Mathf.Min(4096, _maxProjectiles), ProjectileGpuData.Stride);
                _spawnSlotUpload = new ComputeBuffer(Mathf.Min(4096, _maxProjectiles), sizeof(int));
                _snapshotKernel = _projectileShader.FindKernel("SnapshotProjectilePool");
                _spawnKernel = _projectileShader.FindKernel("ApplyProjectileSpawns");
            }
            _slotInUse = new bool[_maxProjectiles];
            _slotLease = new ulong[_maxProjectiles];
            for (int i = _maxProjectiles-1; i >= 0; i--) _freeSlots.Push(i);
        }

        private void ReleasePoolStorage()
        {
            // Invalidate callbacks before releasing any native resource. Borrowed battle buffers
            // remain owned by MassGpuBufferManager; only these small staging buffers belong here.
            _poolGeneration++;
            _poolSnapshotPending = false;
            _poolOccupancy?.Release(); _poolOccupancy = null;
            _spawnUpload?.Release(); _spawnUpload = null;
            _spawnSlotUpload?.Release(); _spawnSlotUpload = null;
            _slotInUse = null;
            _slotLease = null;
            _reservedSlots = 0;
            _poolHighWater = 0;
            _freeSlots.Clear();
            _pendingSpawnSlots.Clear();
        }

        private void RequestPoolSnapshot()
        {
            if (_poolSnapshotPending || _reservedSlots == 0 || _poolOccupancy == null) return;
            FlushPendingProjectiles(); // Snapshot must see all leases up to its watermark.
            int generation = _poolGeneration;
            ulong watermark = _leaseSerial;
            _poolSnapshotPending = true;
            int observedSlots = _poolHighWater;
            _projectileShader.SetInt(MassGpuShaderPropertyIds.MaxProjectilesId, observedSlots);
            _projectileShader.SetBuffer(_snapshotKernel, MassGpuShaderPropertyIds.ProjectileBufferId, _projectileBuffer);
            _projectileShader.SetBuffer(_snapshotKernel, "projectileSlotOccupancy", _poolOccupancy);
            _projectileShader.Dispatch(_snapshotKernel, Mathf.CeilToInt(observedSlots / 64f), 1, 1);
            AsyncGPUReadback.Request(_poolOccupancy, observedSlots * sizeof(int), 0, request =>
            {
                if (generation != _poolGeneration) return;
                _poolSnapshotPending = false;
                if (request.hasError) return; // Fail closed: a missing snapshot cannot free a live slot.
                var occupied = request.GetData<int>();
                for (int i = 0; i < occupied.Length; i++)
                {
                    // A free observation predating a new lease must never reclaim the new shot.
                    if (!_slotInUse[i] || occupied[i] != 0 || _slotLease[i] > watermark) continue;
                    _slotInUse[i] = false;
                    _freeSlots.Push(i);
                    _reservedSlots--;
                    ReclaimedSlots++;
                }
            });
        }

        private void FlushPendingProjectiles()
        {
            int total = _pendingSpawnList.Count;
            if (total == 0) return;
            _projectileShader.SetInt(MassGpuShaderPropertyIds.MaxProjectilesId, _maxProjectiles);
            _projectileShader.SetBuffer(_spawnKernel, MassGpuShaderPropertyIds.ProjectileBufferId, _projectileBuffer);
            _projectileShader.SetBuffer(_spawnKernel, "projectileSpawnData", _spawnUpload);
            _projectileShader.SetBuffer(_spawnKernel, "projectileSpawnSlots", _spawnSlotUpload);
            for (int start = 0; start < total; start += _spawnUpload.count)
            {
                int count = Mathf.Min(_spawnUpload.count, total - start);
                _spawnUpload.SetData(_pendingSpawnList, start, 0, count);
                _spawnSlotUpload.SetData(_pendingSpawnSlots, start, 0, count);
                _projectileShader.SetInt("projectileSpawnCount", count);
                _projectileShader.Dispatch(_spawnKernel, Mathf.CeilToInt(count / 64f), 1, 1);
            }
            _pendingSpawnList.Clear();
            _pendingSpawnSlots.Clear();
        }
    }
}
