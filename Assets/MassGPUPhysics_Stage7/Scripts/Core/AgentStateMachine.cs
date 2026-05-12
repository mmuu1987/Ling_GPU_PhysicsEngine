using System.Collections.Generic;

namespace MassGPUPhysics.Stage7
{
    public static class AgentStateMachine
    {
        private static readonly Dictionary<AgentState, HashSet<AgentState>> ValidTransitions =
            new Dictionary<AgentState, HashSet<AgentState>>
            {
                { AgentState.Idle, new HashSet<AgentState> { AgentState.Move } },
                { AgentState.Move, new HashSet<AgentState> { AgentState.Engage } },
                { AgentState.Engage, new HashSet<AgentState> { AgentState.Attack, AgentState.Move } },
                { AgentState.Attack, new HashSet<AgentState> { AgentState.Dead } },
                { AgentState.Dead, new HashSet<AgentState>() }
            };

        public static bool TryTransition(AgentState current, AgentState requested, out AgentState result)
        {
            HashSet<AgentState> valid;
            if (ValidTransitions.TryGetValue(current, out valid) && valid.Contains(requested))
            {
                result = requested;
                return true;
            }

            result = current;
            return false;
        }

        public static AgentState ResolveConflict(AgentState current, params AgentState[] requests)
        {
            AgentState best = current;

            for (int i = 0; i < requests.Length; i++)
            {
                AgentState candidate;
                if (TryTransition(current, requests[i], out candidate) && (int)candidate > (int)best)
                    best = candidate;
            }

            return best;
        }

        public static bool IsTerminal(AgentState state)
        {
            return state == AgentState.Dead;
        }
    }
}
