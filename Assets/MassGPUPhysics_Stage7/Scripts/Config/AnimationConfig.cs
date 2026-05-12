using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [CreateAssetMenu(menuName = "MassGPUPhysics/Stage7/Animation Config")]
    public sealed class AnimationConfig : ScriptableObject
    {
        [HideInInspector] public Vector2 idleClipFrameRange = new Vector2(0f, 30f);
        [HideInInspector] public Vector2 moveClipFrameRange = new Vector2(0f, 30f);
        [HideInInspector] public Vector2 attackClipFrameRange = new Vector2(0f, 30f);
        [HideInInspector] public Vector2 deathClipFrameRange = new Vector2(0f, 30f);
        [HideInInspector, Min(1f)] public float idleClipFrameRate = 30f;
        [HideInInspector, Min(1f)] public float moveClipFrameRate = 30f;
        [HideInInspector, Min(1f)] public float attackClipFrameRate = 30f;
        [HideInInspector, Min(1f)] public float deathClipFrameRate = 30f;
        [Min(1)] public int nearAnimationInterval = 1;
        [Min(1)] public int midAnimationInterval = 2;
        [Min(1)] public int farAnimationInterval = 4;
    }
}
