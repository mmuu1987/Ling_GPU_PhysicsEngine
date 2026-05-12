using System.Runtime.InteropServices;
using UnityEngine;

namespace MassGPUPhysics.Stage7
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
    }

    [System.Serializable]
    public struct VATClipParams
    {
        public float startFrame;
        public float frameCount;
        public float frameRate;
        public bool loop;
    }
}
