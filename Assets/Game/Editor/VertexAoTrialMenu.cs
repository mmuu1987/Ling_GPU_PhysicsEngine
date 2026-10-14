using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Vertex AO trial (one unit type first): bakes the default attacker's profile (male sword warrior). Output only
    /// under Assets/Game/Content/VertexAO/MaleSword plus the library entry; the profile and its meshes are untouched.</summary>
    internal static class VertexAoTrialMenu
    {
        const string Profile = "Assets/Game/Content/Characters/MaleWarrior-20261012/MaleSword_VAT.asset";
        const string Library = "Assets/Game/Resources/" + global::MassEngine.VertexAoLibrary.ResourcePath + ".asset";

        [MenuItem("MassEngine/顶点AO/试烘：男剑士 MaleSword（默认攻方）", false, 40)]
        static void BakeMaleSword()
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Profile);
            if (so == null) { Debug.LogError("[VertexAO] missing " + Profile); return; }
            var report = global::MassEngine.Editor.VertexAoBaker.Bake(so, "Assets/Game/Content/VertexAO/MaleSword", Library,
                new global::MassEngine.Editor.VertexAoBaker.Settings(), Path.GetFullPath("outputs/VertexAO/MaleSword_near.txt"));
            if (report.ok) Debug.Log(report.text.ToString()); else Debug.LogError(report.text.ToString());
        }
    }
}
