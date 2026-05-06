using UnityEngine;

[CreateAssetMenu(fileName = "Stage6RenderConfig", menuName = "MassGPUPhysics/Stage6/Config/Render Config")]
public sealed class Stage6RenderConfig_Stage6 : ScriptableObject
{
    [Header("VAT")]
    public VATProfile_Stage5 vatProfile;

    [Header("Meshes")]
    public Mesh nearMesh;
    public Mesh midMesh;
    public Mesh farMesh;

    [Header("Materials")]
    public Material nearMaterial;
    public Material midMaterial;
    public Material farMaterial;
}
