namespace MassEngine
{
    /// <summary>
    /// One registered unit type. Owns its module set (parameter contributors + spawn),
    /// its resolved render/VAT runtime data, and its slice of the shared agent buffers.
    /// </summary>
    public interface IUnitType
    {
        UnitTypeConfig Config { get; }
        int TeamId { get; }
        int UnitCount { get; }
        int BufferOffset { get; }
        int UnitTypeIndex { get; }

        ISpawnModule SpawnModule { get; }
        IMovementModule MovementModule { get; }
        IFlockingModule FlockingModule { get; }
        IAnimationModule AnimationModule { get; }
        ICombatModule CombatModule { get; }

        /// <summary>Resolved render/VAT data; assigned by the system during Initialize.</summary>
        ResolvedUnitTypeRuntime RenderRuntime { get; }
        void AttachRenderRuntime(ResolvedUnitTypeRuntime renderRuntime);

        /// <summary>
        /// Builds this unit type's GPU settings record: defaults, then each module's
        /// Contribute, then VAT-derived clip durations. Called every frame so runtime
        /// config edits take effect next frame.
        /// </summary>
        UnitTypeGpuSettings BuildGpuSettings();

        void Initialize(UnitTypeInitContext context);
        void OnBuffersBound(MassGpuBufferManager buffers);
        void Release();
    }
}
