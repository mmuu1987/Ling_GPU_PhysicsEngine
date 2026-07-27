using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class XunXianFbxExportMenu
{
    private const string OutputDir = "Assets/xunmesh_fbx_exports";

    [MenuItem("XunXian/Export Selected XunMesh To FBX")]
    public static void ExportSelectedToFbx()
    {
        if (!TryGetExportObjectMethod(out var exportObject))
            return;

        Directory.CreateDirectory(OutputDir);

        var exported = 0;
        foreach (var obj in Selection.objects)
        {
            var go = ResolveGameObject(obj);
            if (go == null)
                continue;

            var safeName = MakeSafeName(go.name);
            var outPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputDir}/{safeName}.fbx");
            exportObject.Invoke(null, new object[] { outPath, go });
            Debug.Log($"Exported XunXian FBX: {outPath}", go);
            exported++;
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("XunXian FBX Export", $"Exported {exported} object(s) to {OutputDir}.", "OK");
    }

    [MenuItem("XunXian/Export All XunMesh Assets To FBX")]
    public static void ExportAllXunMeshAssetsToFbx()
    {
        if (!TryGetExportObjectMethod(out var exportObject))
            return;

        Directory.CreateDirectory(OutputDir);

        var guids = AssetDatabase.FindAssets("t:DefaultAsset", new[] { "Assets" });
        var exported = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".xunmesh", StringComparison.OrdinalIgnoreCase))
                continue;

            var go = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            if (go == null)
                continue;

            var rel = path.Substring("Assets/".Length);
            var safeName = MakeSafeName(Path.ChangeExtension(rel, null).Replace('/', '_').Replace('\\', '_'));
            var outPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputDir}/{safeName}.fbx");
            exportObject.Invoke(null, new object[] { outPath, go });
            Debug.Log($"Exported XunXian FBX: {outPath}", go);
            exported++;
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("XunXian FBX Export", $"Exported {exported} .xunmesh asset(s) to {OutputDir}.", "OK");
    }

    [MenuItem("XunXian/Export Selected XunMesh To FBX", true)]
    public static bool ExportSelectedToFbxValidate()
    {
        return Selection.objects != null && Selection.objects.Any(o => ResolveGameObject(o) != null);
    }

    private static GameObject ResolveGameObject(UnityEngine.Object obj)
    {
        if (obj is GameObject go)
            return go;

        var path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path))
            return null;

        var main = AssetDatabase.LoadMainAssetAtPath(path);
        return main as GameObject;
    }

    private static string MakeSafeName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(name) ? "xunmesh_export" : name;
    }

    private static bool TryGetExportObjectMethod(out MethodInfo exportObject)
    {
        exportObject = null;
        var exporterType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
        if (exporterType == null)
        {
            EditorUtility.DisplayDialog(
                "FBX Exporter not ready",
                "Unity FBX Exporter package is not loaded yet. Wait for Package Manager to finish resolving com.unity.formats.fbx, then try again.",
                "OK");
            return false;
        }

        exportObject = exporterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "ExportObject") return false;
                var p = m.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(string) && typeof(UnityEngine.Object).IsAssignableFrom(p[1].ParameterType);
            });

        if (exportObject != null)
            return true;

        EditorUtility.DisplayDialog("FBX Exporter API mismatch", "Could not find ModelExporter.ExportObject(string, Object).", "OK");
        return false;
    }
}
