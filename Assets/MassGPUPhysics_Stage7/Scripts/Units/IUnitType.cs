namespace MassGPUPhysics.Stage7
{
    public interface IUnitType
    {
        UnitTypeConfig Config { get; }
        int TeamId { get; }
        int UnitCount { get; }
        int BufferOffset { get; }

        ISpawnModule SpawnModule { get; }
        IMovementModule MovementModule { get; }
        IFlockingModule FlockingModule { get; }
        IAnimationModule AnimationModule { get; }
        ICombatModule CombatModule { get; }

        void Initialize(UnitTypeInitContext context);
        void OnBuffersBound(MassGpuBufferManager_Stage7 buffers);
        void Release();
    }
}
