namespace MassEngine
{
    /// <summary>
    /// What a whole team is currently doing with its feet. Uploaded one entry per teamId in
    /// MassGpuBufferManager.teamStanceBuffer and mirrored by the TEAM_STANCE_* defines in
    /// AgentDataCommon.hlsl - the numeric values are a GPU contract, do not renumber them.
    /// </summary>
    public enum TeamStance
    {
        /// <summary>
        /// Stand still and only engage what walks into attack range. Zero on purpose: a stance
        /// buffer nobody uploaded freezes every team, which is visible in one frame, instead of
        /// marching the teams that were meant to hold.
        /// </summary>
        Hold = 0,

        /// <summary>Follow the team's flow field toward its objective.</summary>
        Advance = 1,

        /// <summary>
        /// Stay within defenderGuardRadius of the spawn anchor. No producer yet; this is the
        /// leash an explicit "defend this spot" order will use.
        /// </summary>
        GuardHome = 2
    }
}
