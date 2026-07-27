using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// MassEngine module contract (architecture decision "A", see design.md):
    /// per-frame simulation runs 100% on the GPU, so unit-type modules are NOT per-agent
    /// behaviour objects. A module owns one functional domain of a unit type and
    /// CONTRIBUTES that domain's parameters to the UnitTypeGpuSettings record that the
    /// pipeline uploads each frame. Adding a unit type = subclass UnitTypeBase (swap any
    /// module implementations) + author ScriptableObject configs; the core pipeline is
    /// never modified.
    /// </summary>
    public interface IUnitParameterContributor
    {
        /// <summary>Writes this module's parameters into the unit type's GPU settings.</summary>
        void Contribute(ref UnitTypeGpuSettings settings);
    }

    /// <summary>CPU-side one-shot responsibility: initial agent placement.</summary>
    public interface ISpawnModule
    {
        SpawnConfig Config { get; }
        void GenerateAgents(AgentData[] buffer, int offset, int count, int teamId);
    }

    public interface IMovementModule : IUnitParameterContributor
    {
        MovementConfig Config { get; }
        FlowFieldTarget Target { get; }
        void SetTargetPoint(Vector3 point);
        void SetTargetArea(Vector3 center, Vector3 size);
    }

    public interface IFlockingModule : IUnitParameterContributor
    {
        FlockingConfig Config { get; }
    }

    public interface IAnimationModule : IUnitParameterContributor
    {
        AnimationConfig Config { get; }
    }

    public interface ICombatModule : IUnitParameterContributor
    {
        CombatConfig Config { get; }
        int MaxHp { get; }
    }

    public enum FlowFieldPreviewMode
    {
        FlowDirection = 0,
        DensityTarget = 1
    }

    public enum FlowFieldTargetMode
    {
        Point = 0,
        Area = 1
    }

    public struct FlowFieldTarget
    {
        public bool active;
        public FlowFieldTargetMode mode;
        public Vector3 center;
        public Vector3 size;
    }
}
