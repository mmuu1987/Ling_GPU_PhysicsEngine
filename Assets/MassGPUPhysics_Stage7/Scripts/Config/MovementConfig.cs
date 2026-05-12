using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Movement Config")]
    public sealed class MovementConfig : ScriptableObject
    {
        [Min(0.01f)] public float maxSpeed = 6f;
        [Min(0f)] public float flowFieldResponsiveness = 6f;
        [Range(0f, 1f)] public float flowFieldWeight = 1f;
        [Range(0f, 20f)] public float velocityDamping = 5f;
        public bool useConfiguredFlowTarget;
        public FlowFieldTargetMode flowTargetMode = FlowFieldTargetMode.Point;
        public Vector3 targetPoint = Vector3.zero;
        public Vector3 targetAreaCenter = Vector3.zero;
        public Vector3 targetAreaSize = new Vector3(12f, 0f, 12f);
    }
}
