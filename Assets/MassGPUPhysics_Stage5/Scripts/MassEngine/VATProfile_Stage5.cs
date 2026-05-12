using System;
using UnityEngine;

[CreateAssetMenu(menuName = "MassGPUPhysics/Stage5/VAT Profile")]
public sealed class VATProfile_Stage5 : ScriptableObject
{
    [Header("Full LOD")]
    public Mesh cleanMesh;
    public Texture2D positionTexture;
    public Texture2D normalTexture;

    [Header("Mid LOD")]
    public Mesh midLodMesh;
    public Texture2D midLodPositionTexture;
    public Texture2D midLodNormalTexture;
    public int midLodTextureWidth;
    public int midLodTextureHeight;
    public int midLodRowsPerFrame;

    [Header("Low LOD")]
    public Mesh lowLodMesh;
    public Texture2D lowLodPositionTexture;
    public Texture2D lowLodNormalTexture;
    public int lowLodTextureWidth;
    public int lowLodTextureHeight;
    public int lowLodRowsPerFrame;

    [Header("VAT Layout")]
    public int textureWidth;
    public int textureHeight;
    public int rowsPerFrame;
    public int totalFrameCount;
    public int frameRate;

    [Header("Clip Windows")]
    public VATClipWindow idle;
    public VATClipWindow move;
    public VATClipWindow attack;
    public VATClipWindow death;

    public bool HasMidLod =>
        midLodMesh != null &&
        midLodPositionTexture != null &&
        midLodNormalTexture != null &&
        midLodTextureWidth > 0 &&
        midLodTextureHeight > 0 &&
        midLodRowsPerFrame > 0;

    public bool HasLowLod =>
        lowLodMesh != null &&
        lowLodPositionTexture != null &&
        lowLodNormalTexture != null &&
        lowLodTextureWidth > 0 &&
        lowLodTextureHeight > 0 &&
        lowLodRowsPerFrame > 0;

    public bool IsValid(out string error)
    {
        if (cleanMesh == null)
        {
            error = "Profile is missing cleanMesh.";
            return false;
        }

        if (positionTexture == null || normalTexture == null)
        {
            error = "Profile is missing full LOD VAT position or normal texture.";
            return false;
        }

        if (textureWidth <= 0 || textureHeight <= 0 || rowsPerFrame <= 0 || totalFrameCount <= 0 || frameRate <= 0)
        {
            error = "Profile has invalid full LOD VAT layout values.";
            return false;
        }

        error = string.Empty;
        return true;
    }

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
