using UnityEngine;
using UnityEngine.Serialization;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/LOD Config")]
    public sealed class LodConfig : ScriptableObject
    {
        [FormerlySerializedAs("shadowCastingRadius")]
        [Min(0f)] public float nearLodRadius = 18f;
        [Min(0f)] public float midLodRadius = 75f;
        [Min(1)] public int nearAnimationInterval = 1;
        [Min(1)] public int midAnimationInterval = 2;
        [Min(1)] public int farAnimationInterval = 4;

        [Header("Simulation Cadence")]
        [Tooltip("Decision-pass frequency per LOD tier: agents run their expensive simulation step every N-th frame. Rate-dependent quantities are dt-compensated, so DPS and movement speed match full rate. Powers of two recommended.")]
        [Min(1)] public int nearSimulationInterval = 1;
        [Min(1)] public int midSimulationInterval = 2;
        [Min(1)] public int farSimulationInterval = 4;
        public bool enableFrustumCulling = true;
        [Min(0f)] public float cullingRadius = 2f;
        [Tooltip("Agents farther than this from lodCenter are not submitted for rendering (0 = unlimited). On km-scale battlefields units beyond ~900m are sub-pixel; this caps the worst-case visible instance count when the camera sees the whole field.")]
        [Min(0f)] public float maxRenderDistance = 0f;
        [Tooltip("Render corpses in the far LOD tier too. Off = bodies vanish beyond midLodRadius, forming a visible pop line as the camera moves.")]
        public bool farIncludeDead = true;

        [Header("LOD Transition")]
        [Tooltip("中景闪动修复 B (2026-10-13): an agent keeps its finer LOD tier until it is this fraction past the ring (0.1 = it enters the 30 m near ring at 30 m and leaves it at 33 m), so bodies standing on a ring no longer flip mesh every frame. 0 = hard ring (old behaviour).")]
        [Range(0f, 0.5f)] public float lodHysteresis = 0.1f;
        [Tooltip("Seconds of dithered crossfade when an agent changes LOD tier (both meshes drawn, complementary pixel patterns). 0 = switch instantly (old behaviour).")]
        [Range(0f, 1f)] public float lodCrossfadeSeconds = 0.25f;

        [Header("Corpses")]
        [Tooltip("Seconds a body lies where it fell before it starts sinking. 0 = corpses never despawn (they pile up for the whole battle). After this the body sinks for corpseSinkSeconds and is then dropped from every visible list AND out of the simulation, so it costs no draw, no animation work and no combat pass.")]
        [Min(0f)] public float corpseLingerSeconds = 15f;
        [Tooltip("Seconds spent sinking into the ground. The opaque terrain does the hiding, so this only has to be long enough not to read as a pop.")]
        [Min(0f)] public float corpseSinkSeconds = 1.5f;
        [Tooltip("How far a body sinks. Must clear the tallest agent's death pose, otherwise a sliver stays above ground when the corpse is dropped.")]
        [Min(0f)] public float corpseSinkDepth = 2.2f;

        [Header("Presentation")]
        [Tooltip("Seconds a wanted Idle/Run clip change must persist before the shown clip switches. Filters one-frame logic blips that would otherwise cut the pose to frame 0 (there is no cross-fade). 0 = switch immediately.")]
        [Min(0f)] public float presentationHoldSeconds = 0.2f;
        [Tooltip("Seconds the Attack clip keeps playing after the logic leaves Attack (target died, shoved out of reach). A return to Attack inside this window continues the same swing instead of restarting it. Entering Attack and dying are always immediate. 0 = legacy.")]
        [Min(0f)] public float attackPresentationHoldSeconds = 0.3f;

        [Header("站定 / 起步（第 6 轮）")]
        [Tooltip("起步速度 m/s（按体型缩放）。站着的单位只有被推到这个速度以上才迈步、播跑步动作；更慢的挪动一律原地站定（只做重叠修正）。与兵种的 moveStartSpeed 取较大值。0 = 只用兵种值（旧行为）。实测（11 万单位）：0.6 后排推进最快但起步/停步最频繁；0.9 为默认折中；1.2 碎步再减半，但后排推进比 0.9 再慢约 13%。")]
        [Min(0f)] public float locomotionStartSpeed = 0.9f;
        [Tooltip("停步速度 m/s（按体型缩放）。在走的单位慢到这个速度以下就停下站定。与兵种的 moveStopSpeed 取较大值，并且应小于起步速度。0 = 只用兵种值（旧行为）。")]
        [Min(0f)] public float locomotionStopSpeed = 0.3f;
        [Tooltip("站定最短时间（秒）：单位停下后至少站这么久才会再迈步，除非被明显推动（超过起步速度的 2.5 倍）。消除走一下停一下的碎步。0 = 关闭。")]
        [Min(0f)] public float minStandSeconds = 0.5f;
        [Tooltip("行走最短时间（秒）：开始走之后这么久之内，除非几乎完全停住，否则不切回站立。0 = 关闭。")]
        [Min(0f)] public float minWalkSeconds = 0.4f;
        [Tooltip("跑步动画最低倍速（第 6 轮方案 b）。与兵种的 moveAnimationSpeedMin 取较大值。慢速移动时腿仍明显在迈，而不是几乎冻住的跑步动作在地上滑。代价：很慢时脚步比实际位移快，脚下略有打滑。0 = 只用兵种值（旧行为，0.2）。")]
        [Range(0f, 1f)] public float moveAnimationSpeedFloor = 0.5f;
    }
}

