using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public abstract class UnitTypeBase : IUnitType
    {
        public UnitTypeConfig Config { get; private set; }
        public int TeamId { get { return Config != null ? Config.teamId : 0; } }
        public int UnitCount { get { return Config != null && Config.spawnConfig != null ? Mathf.Max(0, Config.spawnConfig.unitCount) : 0; } }
        public int BufferOffset { get; private set; }

        public ISpawnModule SpawnModule { get; protected set; }
        public IMovementModule MovementModule { get; protected set; }
        public IFlockingModule FlockingModule { get; protected set; }
        public IAnimationModule AnimationModule { get; protected set; }
        public ICombatModule CombatModule { get; protected set; }

        protected UnitTypeBase(UnitTypeConfig config)
        {
            Config = config;
            ConfigValidator.EnsureRuntimeDefaults(Config);
            CreateModules();
        }

        public virtual void Initialize(UnitTypeInitContext context)
        {
            BufferOffset = context.bufferOffset;
        }

        public virtual void OnBuffersBound(MassGpuBufferManager_Stage7 buffers)
        {
        }

        public virtual void Release()
        {
        }

        protected virtual void CreateModules()
        {
            SpawnModule = new DefaultSpawnModule(Config != null ? Config.spawnConfig : null);
            MovementModule = new DefaultMovementModule(Config != null ? Config.movementConfig : null);
            FlockingModule = new DefaultFlockingModule(Config != null ? Config.flockingConfig : null);
            AnimationModule = new DefaultAnimationModule(Config != null ? Config.animationConfig : null);
            CombatModule = new DefaultCombatModule(Config != null ? Config.combatConfig : null);
        }
    }
}
