using System;
using UnityEngine;

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage5/VAT Clip Manifest")]
public sealed class VATClipManifest_Stage5 : ScriptableObject
{
    public int textureWidth;
    public int textureHeight;
    public int rowsPerFrame;
    public int totalFrameCount;
    public int frameRate;
    public VATClipWindow idle;
    public VATClipWindow move;
    public VATClipWindow attack;
    public VATClipWindow death;

    [Serializable]
    public struct VATClipWindow
    {
        public string label;
        public int startFrame;
        public int frameCount;
        public int frameRate;
        public bool loop;

        public Vector2 ToRange()
        {
            return new Vector2(startFrame, Mathf.Max(1, frameCount));
        }
    }
}
