using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class DefaultAnimationModule : IAnimationModule
    {
        public AnimationConfig Config { get; private set; }

        public DefaultAnimationModule(AnimationConfig config)
        {
            Config = config;
        }

        public VATClipParams GetClipForState(AgentState agentState)
        {
            AnimationConfig config = Config;

            if (agentState == AgentState.Move || agentState == AgentState.Engage)
                return CreateClip(config != null ? config.moveClipFrameRange : new Vector2(0f, 30f), config != null ? config.moveClipFrameRate : 30f, true);

            if (agentState == AgentState.Attack)
                return CreateClip(config != null ? config.attackClipFrameRange : new Vector2(0f, 30f), config != null ? config.attackClipFrameRate : 30f, true);

            if (agentState == AgentState.Dead)
                return CreateClip(config != null ? config.deathClipFrameRange : new Vector2(0f, 30f), config != null ? config.deathClipFrameRate : 30f, false);

            return CreateClip(config != null ? config.idleClipFrameRange : new Vector2(0f, 30f), config != null ? config.idleClipFrameRate : 30f, true);
        }

        public float AdvanceAnimationTime(float currentTime, AgentState agentState, float deltaTime, int lodLevel)
        {
            int interval = GetLodInterval(lodLevel);
            VATClipParams clip = GetClipForState(agentState);
            float duration = clip.frameCount / Mathf.Max(clip.frameRate, 0.001f);
            float nextTime = currentTime + Mathf.Max(0f, deltaTime) * interval;

            if (!clip.loop)
                return Mathf.Min(nextTime, duration);

            if (duration <= 0.0001f)
                return 0f;

            return nextTime % duration;
        }

        private int GetLodInterval(int lodLevel)
        {
            if (Config == null)
                return 1;

            if (lodLevel <= 0)
                return Mathf.Max(1, Config.nearAnimationInterval);
            if (lodLevel == 1)
                return Mathf.Max(1, Config.midAnimationInterval);
            return Mathf.Max(1, Config.farAnimationInterval);
        }

        private static VATClipParams CreateClip(Vector2 range, float frameRate, bool loop)
        {
            return new VATClipParams
            {
                startFrame = Mathf.Max(0f, range.x),
                frameCount = Mathf.Max(1f, range.y),
                frameRate = Mathf.Max(1f, frameRate),
                loop = loop
            };
        }
    }
}
