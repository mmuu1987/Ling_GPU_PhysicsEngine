using UnityEngine;

namespace MassEngine
{
    public abstract class UnitTypeBase : IUnitType
    {
        public UnitTypeConfig Config { get; private set; }
        public int TeamId { get { return Config != null ? Config.teamId : 0; } }
        public int UnitCount { get { return Config != null && Config.spawnConfig != null ? Mathf.Max(0, Config.spawnConfig.unitCount) : 0; } }
        public int BufferOffset { get; private set; }
        public int UnitTypeIndex { get; private set; }

        public ISpawnModule SpawnModule { get; protected set; }
        public IMovementModule MovementModule { get; protected set; }
        public IFlockingModule FlockingModule { get; protected set; }
        public IAnimationModule AnimationModule { get; protected set; }
        public ICombatModule CombatModule { get; protected set; }

        public ResolvedUnitTypeRuntime RenderRuntime { get; private set; }

        protected UnitTypeBase(UnitTypeConfig config)
        {
            Config = config;
            CreateModules();
        }

        public void AttachRenderRuntime(ResolvedUnitTypeRuntime renderRuntime)
        {
            RenderRuntime = renderRuntime;
        }

        public virtual UnitTypeGpuSettings BuildGpuSettings()
        {
            UnitTypeGpuSettings settings = UnitTypeGpuSettings.CreateDefaults(TeamId);

            if (MovementModule != null)
                MovementModule.Contribute(ref settings);
            if (FlockingModule != null)
                FlockingModule.Contribute(ref settings);
            if (CombatModule != null)
                CombatModule.Contribute(ref settings);
            if (AnimationModule != null)
                AnimationModule.Contribute(ref settings);

            if (RenderRuntime != null)
            {
                settings.idleClipDuration = RenderRuntime.idleClipDuration;
                settings.moveClipDuration = RenderRuntime.moveClipDuration;
                settings.attackClipDuration = RenderRuntime.attackClipDuration;
                settings.deathClipDuration = RenderRuntime.deathClipDuration;
            }

            return settings;
        }

        public virtual void Initialize(UnitTypeInitContext context)
        {
            BufferOffset = context.bufferOffset;
            UnitTypeIndex = context.unitTypeIndex;
        }

        public virtual void OnBuffersBound(MassGpuBufferManager buffers)
        {
        }

        public virtual void Release()
        {
        }

        /// <summary>
        /// Override to swap module implementations for a new unit type. Null sub-configs
        /// are legal: the corresponding module simply leaves the built-in defaults.
        /// </summary>
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
