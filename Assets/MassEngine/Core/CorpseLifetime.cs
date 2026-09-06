namespace MassEngine
{
    /// <summary>
    /// C#-side mirror of the corpse despawn rule implemented in AgentDataCommon.hlsl
    /// (UpdateAnimationTime + AppendVisibleAgentForUnitType) and in the two agent
    /// vertex shaders (the sink offset applied in setup()).
    ///
    /// The model:
    /// - A dead agent's <c>currentAnimationTime</c> doubles as its CORPSE AGE. It is
    ///   zeroed on the transition into Dead, keeps accumulating past the death clip
    ///   (the VAT sampler already clamps that clip to its last frame), and is capped
    ///   at <see cref="DespawnSeconds"/> so it never grows without bound.
    /// - For the first <c>lingerSeconds</c> the body lies where it fell.
    /// - Over the next <c>sinkSeconds</c> it sinks <c>sinkDepth</c> metres into the
    ///   ground, which hides it behind the opaque terrain instead of popping it out.
    /// - At <see cref="DespawnSeconds"/> the body is retired: the classification kernel
    ///   stops appending it to any visible list and the combat kernel stops writing it
    ///   at all, so from then on it costs no draw, no animation work and no simulation
    ///   work. Retiring is only sound because a corpse is immutable by then - every
    ///   write the combat kernel skips would have stored the value that is already in
    ///   both sides of every double buffer.
    /// - <c>lingerSeconds &lt;= 0</c> disables the whole thing: corpses stay forever,
    ///   which is the behaviour that predates this rule.
    ///
    /// This class carries no runtime responsibility; it exists so tests and tooling can
    /// reason about the rule without reading HLSL, exactly like
    /// <see cref="AgentStateMachine"/>.
    /// </summary>
    public static class CorpseLifetime
    {
        /// <summary>Guards the divide when sinkSeconds is authored as 0 (instant sink).</summary>
        public const float MinSinkSeconds = 0.0001f;

        public static bool DespawnEnabled(float lingerSeconds)
        {
            return lingerSeconds > 0f;
        }

        /// <summary>
        /// Corpse age at which the body is fully gone. Also the ceiling the GPU clamps
        /// the age accumulator to. 0 when despawn is disabled.
        /// </summary>
        public static float DespawnSeconds(float lingerSeconds, float sinkSeconds)
        {
            if (!DespawnEnabled(lingerSeconds))
                return 0f;

            return lingerSeconds + (sinkSeconds > 0f ? sinkSeconds : 0f);
        }

        /// <summary>How far below its death position a corpse of this age is drawn.</summary>
        public static float SinkOffset(float corpseAgeSeconds, float lingerSeconds, float sinkSeconds, float sinkDepth)
        {
            if (!DespawnEnabled(lingerSeconds))
                return 0f;

            float span = sinkSeconds > MinSinkSeconds ? sinkSeconds : MinSinkSeconds;
            float t = (corpseAgeSeconds - lingerSeconds) / span;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * sinkDepth;
        }

        /// <summary>True once the corpse must no longer be submitted for rendering.</summary>
        public static bool IsDespawned(float corpseAgeSeconds, float lingerSeconds, float sinkSeconds)
        {
            return DespawnEnabled(lingerSeconds) && corpseAgeSeconds >= DespawnSeconds(lingerSeconds, sinkSeconds);
        }

        /// <summary>
        /// Advances a corpse age by one animation step, mirroring the Dead branch of
        /// UpdateAnimationTime: unbounded accumulation, capped at the despawn point.
        /// </summary>
        public static float Advance(float corpseAgeSeconds, float deltaSeconds, float lingerSeconds, float sinkSeconds, float deathClipDuration)
        {
            float next = corpseAgeSeconds + deltaSeconds;
            float ceiling = DespawnEnabled(lingerSeconds)
                ? DespawnSeconds(lingerSeconds, sinkSeconds)
                : (deathClipDuration > 0f ? deathClipDuration : 0f);
            return next > ceiling ? ceiling : next;
        }
    }
}
