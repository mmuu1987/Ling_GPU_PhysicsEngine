using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Spawn Config")]
    public sealed class SpawnConfig : ScriptableObject
    {
        [Min(1)] public int unitCount = 50000;
        public Vector3 spawnCenter = Vector3.zero;
        public Vector3 spawnSize = new Vector3(35f, 0f, 80f);
    }
}
