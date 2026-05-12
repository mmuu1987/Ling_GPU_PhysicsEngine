using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Unit Type Config")]
    public sealed class UnitTypeConfig : ScriptableObject
    {
        [Header("Identity")]
        public string unitTypeName = "Default Sword";
        [Tooltip("Fully qualified UnitTypeBase subclass name. Leave empty to use DefaultSwordUnit.")]
        public string unitTypeClassName = "MassGPUPhysics.Stage7.DefaultSwordUnit";
        public int teamId;

        [Header("Sub-Configs")]
        public SpawnConfig spawnConfig;
        public MovementConfig movementConfig;
        public FlockingConfig flockingConfig;
        public AnimationConfig animationConfig;
        public CombatConfig combatConfig;
        public RenderConfig renderConfig;
    }
}
