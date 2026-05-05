using UnityEngine;

public static class MassGpuDrawUtility_Stage5
{
    public static ComputeBuffer CreateAppendIndexBuffer(int instanceCount)
    {
        return new ComputeBuffer(Mathf.Max(1, instanceCount), sizeof(uint), ComputeBufferType.Append);
    }

    public static ComputeBuffer CreateArgsBuffer(Mesh mesh)
    {
        var args = new uint[5];
        if (mesh != null)
        {
            args[0] = (uint)mesh.GetIndexCount(0);
            args[2] = (uint)mesh.GetIndexStart(0);
            args[3] = (uint)mesh.GetBaseVertex(0);
        }

        var buffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        buffer.SetData(args);
        return buffer;
    }

    public static MaterialPropertyBlock CreatePropertyBlock(
        ComputeBuffer agentBuffer,
        ComputeBuffer visibleIndexBuffer,
        int agentBufferId,
        int visibleAgentIndicesId)
    {
        var block = new MaterialPropertyBlock();
        block.SetBuffer(agentBufferId, agentBuffer);
        block.SetBuffer(visibleAgentIndicesId, visibleIndexBuffer);
        return block;
    }

    public static void SyncVatMaterial(Material material, float vatFrameCount, float vatFrameRate, int frameCountId, int frameRateId)
    {
        if (material == null)
            return;

        material.SetFloat(frameCountId, vatFrameCount);
        material.SetFloat(frameRateId, vatFrameRate);
    }

    public static Mesh CreateBillboardQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "Runtime Far LOD Billboard Quad"
        };

        mesh.SetVertices(new[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, 1f, 0f)
        });
        mesh.SetUVs(0, new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        });
        mesh.SetIndices(new[] { 0, 2, 1, 2, 3, 1 }, MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
