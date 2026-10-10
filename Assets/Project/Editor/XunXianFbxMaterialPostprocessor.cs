using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class XunXianFbxMaterialPostprocessor : AssetPostprocessor
{
    private static readonly bool ApplyDuringModelImport = false;

    [MenuItem("XunXian/Apply FBX Textures From XunMat Sidecars")]
    private static void ApplySelectedFbxTexturesFromSidecars()
    {
        var selectedPaths = Selection.assetGUIDs.Length > 0
            ? Array.ConvertAll(Selection.assetGUIDs, AssetDatabase.GUIDToAssetPath)
            : new[] { "Assets" };

        var fbxPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedPath in selectedPaths)
        {
            if (File.Exists(ToFullPath(selectedPath)) && selectedPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                fbxPaths.Add(selectedPath);
                continue;
            }

            if (!AssetDatabase.IsValidFolder(selectedPath))
                continue;

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { selectedPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    fbxPaths.Add(path);
            }
        }

        var count = 0;
        foreach (var fbxPath in fbxPaths)
        {
            var sidecarPath = fbxPath + ".xunmat.json";
            if (!File.Exists(ToFullPath(sidecarPath)))
                continue;

            count += ApplyMaterialAssetsFromSidecar(fbxPath, sidecarPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"XunXian: applied textures to {count} material asset(s) from xunmat sidecars.");
    }

    [Serializable]
    private sealed class Sidecar
    {
        public Binding[] materials = Array.Empty<Binding>();
    }

    [Serializable]
    private sealed class Binding
    {
        public string meshName = "";
        public string materialName = "";
        public string texture = "";
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (!ApplyDuringModelImport)
            return;

        if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            return;

        var sidecarAssetPath = assetPath + ".xunmat.json";
        var sidecarFullPath = ToFullPath(sidecarAssetPath);
        if (!File.Exists(sidecarFullPath))
            return;

        var sidecar = JsonUtility.FromJson<Sidecar>(File.ReadAllText(sidecarFullPath));
        if (sidecar?.materials == null || sidecar.materials.Length == 0)
            return;

        var byMeshName = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in sidecar.materials)
        {
            if (!string.IsNullOrWhiteSpace(binding.meshName))
                byMeshName[binding.meshName] = binding;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (!TryGetBinding(renderer, i, sidecar.materials, byMeshName, out var binding))
                continue;

            var material = GetOrCreateMaterial(binding);
            if (material == null)
                continue;

            var slots = renderer.sharedMaterials;
            if (slots == null || slots.Length == 0)
                slots = new Material[1];
            for (var slot = 0; slot < slots.Length; slot++)
                slots[slot] = material;
            renderer.sharedMaterials = slots;
        }

        Debug.Log($"XunXian: applied material sidecar for {assetPath}");
    }

    private static bool TryGetBinding(
        Renderer renderer,
        int rendererIndex,
        Binding[] ordered,
        Dictionary<string, Binding> byMeshName,
        out Binding binding)
    {
        if (byMeshName.TryGetValue(renderer.gameObject.name, out binding))
            return true;

        if (renderer is MeshRenderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var meshName = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "";
            if (byMeshName.TryGetValue(meshName, out binding))
                return true;
            if (meshName.EndsWith("_Mesh", StringComparison.OrdinalIgnoreCase) &&
                byMeshName.TryGetValue(meshName[..^5], out binding))
                return true;
        }

        if (rendererIndex >= 0 && rendererIndex < ordered.Length)
        {
            binding = ordered[rendererIndex];
            return true;
        }

        binding = null;
        return false;
    }

    private Material GetOrCreateMaterial(Binding binding)
    {
        var modelDirectory = Path.GetDirectoryName(assetPath)!.Replace('\\', '/');
        var materialDirectory = $"{modelDirectory}/Materials";
        if (!AssetDatabase.IsValidFolder(materialDirectory))
            AssetDatabase.CreateFolder(modelDirectory, "Materials");

        var safeMaterialName = MakeSafeAssetName(
            string.IsNullOrWhiteSpace(binding.materialName) ? binding.meshName : binding.materialName);
        var materialPath = $"{materialDirectory}/{Path.GetFileNameWithoutExtension(assetPath)}-{safeMaterialName}.mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(FindPreferredShader());
            material.name = $"{Path.GetFileNameWithoutExtension(assetPath)}-{safeMaterialName}";
            AssetDatabase.CreateAsset(material, materialPath);
        }

        if (!string.IsNullOrWhiteSpace(binding.texture))
        {
            var texturePath = $"{modelDirectory}/{binding.texture}".Replace('\\', '/');
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (texture == null)
                texture = AssetDatabase.LoadMainAssetAtPath(texturePath) as Texture;
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
                EditorUtility.SetDirty(material);
            }
        }

        return material;
    }

    private static int ApplyMaterialAssetsFromSidecar(string fbxPath, string sidecarPath)
    {
        var sidecar = JsonUtility.FromJson<Sidecar>(File.ReadAllText(ToFullPath(sidecarPath)));
        if (sidecar?.materials == null || sidecar.materials.Length == 0)
            return 0;

        var modelDirectory = Path.GetDirectoryName(fbxPath)!.Replace('\\', '/');
        var materialDirectory = $"{modelDirectory}/Materials";
        if (!AssetDatabase.IsValidFolder(materialDirectory))
            AssetDatabase.CreateFolder(modelDirectory, "Materials");

        var applied = 0;
        foreach (var binding in sidecar.materials)
        {
            if (string.IsNullOrWhiteSpace(binding.texture))
                continue;

            var materialName = MakeSafeAssetName(
                string.IsNullOrWhiteSpace(binding.materialName) ? binding.meshName : binding.materialName);
            var materialPath = $"{materialDirectory}/{Path.GetFileNameWithoutExtension(fbxPath)}-{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(FindPreferredShader());
                material.name = $"{Path.GetFileNameWithoutExtension(fbxPath)}-{materialName}";
                AssetDatabase.CreateAsset(material, materialPath);
            }

            var texturePath = $"{modelDirectory}/{binding.texture}".Replace('\\', '/');
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (texture == null)
                texture = AssetDatabase.LoadMainAssetAtPath(texturePath) as Texture;
            if (texture == null)
            {
                Debug.LogWarning($"XunXian: texture not loaded as Texture: {texturePath}");
                continue;
            }

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            applied++;
        }

        return applied;
    }

    private static Shader FindPreferredShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
               ?? Shader.Find("Standard")
               ?? Shader.Find("Unlit/Texture");
    }

    private static string MakeSafeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "material";

        foreach (var ch in Path.GetInvalidFileNameChars())
            value = value.Replace(ch, '_');
        return value;
    }

    private static string ToFullPath(string assetPath)
    {
        return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);
    }
}
