using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

[ScriptedImporter(1, "xunmesh")]
public sealed class XunXianMeshImporter : ScriptedImporter
{
    [Serializable]
    private sealed class XunMeshFile
    {
        public string name;
        public XunMeshPart[] meshes;
    }

    [Serializable]
    private sealed class XunMeshPart
    {
        public string name;
        public string materialName;
        public string texturePath;
        public float[] vertices;
        public float[] normals;
        public float[] uvs;
        public int[] indices;
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        var json = File.ReadAllText(ctx.assetPath);
        var file = JsonUtility.FromJson<XunMeshFile>(json);
        var root = new GameObject(string.IsNullOrEmpty(file.name) ? Path.GetFileNameWithoutExtension(ctx.assetPath) : file.name);

        if (file.meshes == null)
        {
            ctx.AddObjectToAsset("root", root);
            ctx.SetMainObject(root);
            return;
        }

        for (var i = 0; i < file.meshes.Length; i++)
        {
            var part = file.meshes[i];
            var go = new GameObject(string.IsNullOrEmpty(part.name) ? $"mesh_{i}" : part.name);
            go.transform.SetParent(root.transform, false);

            var mesh = BuildMesh(part);
            mesh.name = go.name + "_Mesh";
            ctx.AddObjectToAsset(mesh.name, mesh);

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.name = string.IsNullOrEmpty(part.materialName) ? go.name + "_Mat" : part.materialName;
            ctx.AddObjectToAsset(material.name, material);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
        }

        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);
    }

    private static Mesh BuildMesh(XunMeshPart part)
    {
        var mesh = new Mesh();
        var vertexCount = part.vertices == null ? 0 : part.vertices.Length / 3;
        if (vertexCount > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        var vertices = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            // Keep the source coordinate system initially; axis conversion can be
            // added after visual verification.
            vertices[i] = new Vector3(part.vertices[i * 3], part.vertices[i * 3 + 1], part.vertices[i * 3 + 2]);
        }
        mesh.vertices = vertices;

        if (part.uvs != null && part.uvs.Length / 2 == vertexCount)
        {
            var uvs = new Vector2[vertexCount];
            for (var i = 0; i < vertexCount; i++)
                uvs[i] = new Vector2(part.uvs[i * 2], part.uvs[i * 2 + 1]);
            mesh.uv = uvs;
        }

        if (part.normals != null && part.normals.Length / 3 == vertexCount)
        {
            var normals = new Vector3[vertexCount];
            for (var i = 0; i < vertexCount; i++)
                normals[i] = new Vector3(part.normals[i * 3], part.normals[i * 3 + 1], part.normals[i * 3 + 2]);
            mesh.normals = normals;
        }

        if (part.indices != null && part.indices.Length >= 3)
        {
            mesh.triangles = part.indices;
        }

        if (mesh.normals == null || mesh.normals.Length != vertexCount)
            mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
