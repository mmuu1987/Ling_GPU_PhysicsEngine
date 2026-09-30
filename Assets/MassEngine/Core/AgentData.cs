using System.Runtime.InteropServices;
using UnityEngine;

namespace MassEngine
{
    public enum AgentState
    {
        Idle = 0,
        Move = 1,
        Engage = 2,
        Attack = 3,
        Dead = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    [System.Serializable]
    public struct AgentData
    {
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 velocity;
        public int currentState;
        public float currentAnimationTime;
        // Presentation only: tactical currentState retains its original meaning.
        public int presentationState;
        public float locomotionSpeed; // filtered final displacement / dt (including slopes)
        public const int StrideBytes = 64;
    }
}
