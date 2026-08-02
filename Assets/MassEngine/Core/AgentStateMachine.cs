namespace MassEngine
{
    /// <summary>
    /// C#-side mirror of the GPU crowd-state model implemented in
    /// AgentCombatSimulation.compute (ResolveAliveState + the Dead override).
    ///
    /// The authoritative model is:
    /// - Alive states (Idle/Move/Engage/Attack) are RE-DERIVED every frame from the
    ///   combat situation by priority Attack > Engage > Move > Idle. Any alive state may
    ///   therefore follow any other alive state; there are no edge constraints between
    ///   alive states.
    /// - Dead has absolute priority, is entered exactly when hp reaches zero, and is
    ///   terminal: no transition leaves Dead.
    ///
    /// This class exists so tests and tooling can reason about the state model without
    /// reading HLSL; it carries no runtime simulation responsibility.
    /// </summary>
    public static class AgentStateMachine
    {
        /// <summary>Numeric value doubles as priority: higher wins.</summary>
        public static int Priority(AgentState state)
        {
            return (int)state;
        }

        public static bool IsTerminal(AgentState state)
        {
            return state == AgentState.Dead;
        }

        /// <summary>
        /// A transition is legal iff the source is not Dead (Dead is terminal;
        /// Dead -> Dead is the only self-consistent request from a dead agent).
        /// </summary>
        public static bool CanTransition(AgentState current, AgentState requested)
        {
            if (current == AgentState.Dead)
                return requested == AgentState.Dead;

            return true;
        }

        public static bool TryTransition(AgentState current, AgentState requested, out AgentState result)
        {
            if (CanTransition(current, requested))
            {
                result = requested;
                return true;
            }

            result = current;
            return false;
        }

        /// <summary>
        /// Mirrors the per-frame state derivation on the GPU: Dead wins outright, then
        /// Attack > Engage > Move > Idle.
        /// </summary>
        public static AgentState Resolve(bool isDead, bool inAttackHold, bool hasEngageTarget, bool hasMoveDirection)
        {
            if (isDead)
                return AgentState.Dead;
            if (inAttackHold)
                return AgentState.Attack;
            if (hasEngageTarget)
                return AgentState.Engage;
            if (hasMoveDirection)
                return AgentState.Move;
            return AgentState.Idle;
        }

        /// <summary>Picks the highest-priority legal request; keeps current if none is legal.</summary>
        public static AgentState ResolveConflict(AgentState current, params AgentState[] requests)
        {
            AgentState best = current;

            for (int i = 0; i < requests.Length; i++)
            {
                AgentState candidate;
                if (TryTransition(current, requests[i], out candidate) && Priority(candidate) > Priority(best))
                    best = candidate;
            }

            return best;
        }
    }
}
