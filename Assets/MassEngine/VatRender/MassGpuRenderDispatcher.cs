using UnityEngine;
using static MassEngine.MassGpuShaderPropertyIds;

namespace MassEngine
{
    /// <summary>
    /// Issues one indirect draw per unit type per LOD, consuming the ResolvedUnitTypeRuntime
    /// prepared at initialization (prefilled MaterialPropertyBlocks — no reflection and no
    /// VAT re-binding on the render path). Buffers are addressed per unit type; team id
    /// plays no role in rendering.
    /// </summary>
    public sealed class MassGpuRenderDispatcher
    {
        private readonly MaterialPropertyBlock fallbackBlock = new MaterialPropertyBlock();
        // One warning per unit-type x LOD batch: a silently skipped draw otherwise
        // reads as "units teleport in and out at the LOD ring" with nothing to go on.
        private readonly System.Collections.Generic.HashSet<int> reportedSkippedBatches = new System.Collections.Generic.HashSet<int>();

        public void Draw(UnitTypeRegistry registry, MassGpuBufferManager buffers, Bounds bounds)
        {
            Draw(registry, buffers, bounds, Vector4.zero);
        }

        /// <param name="corpseSink">
        /// xyz = (linger seconds, sink seconds, sink depth) for the corpse sink applied in
        /// the agent vertex shaders. x &lt;= 0 keeps bodies at their death position.
        /// </param>
        public void Draw(UnitTypeRegistry registry, MassGpuBufferManager buffers, Bounds bounds, Vector4 corpseSink)
        {
            if (registry == null || buffers == null || !buffers.IsAllocated)
                return;

            for (int i = 0; i < registry.RegisteredTypes.Count; i++)
            {
                IUnitType unitType = registry.RegisteredTypes[i];
                ResolvedUnitTypeRuntime runtime = unitType.RenderRuntime;
                if (runtime == null)
                    continue;

                for (int lod = 0; lod < MassGpuBufferManager.LodLevels; lod++)
                {
                    DrawLod(
                        runtime,
                        unitType.UnitTypeIndex,
                        lod,
                        buffers.agentBuffer,
                        buffers.GetVisibleIndexBuffer(unitType.UnitTypeIndex, lod),
                        buffers.GetDrawArgsBuffer(unitType.UnitTypeIndex, lod),
                        bounds,
                        corpseSink);
                }
            }
        }

        private void DrawLod(ResolvedUnitTypeRuntime runtime, int unitTypeIndex, int lodLevel, ComputeBuffer agentBuffer, ComputeBuffer visibleIndices, ComputeBuffer argsBuffer, Bounds bounds, Vector4 corpseSink)
        {
            Mesh mesh = runtime.GetMesh(lodLevel);
            Material material = runtime.GetMaterial(lodLevel);
            if (mesh == null || material == null || agentBuffer == null || visibleIndices == null || argsBuffer == null)
            {
                int batchKey = unitTypeIndex * MassGpuBufferManager.LodLevels + lodLevel;
                if (reportedSkippedBatches.Add(batchKey))
                {
                    Debug.LogWarning("MassEngine: draw skipped for unit type " + unitTypeIndex + " LOD " + lodLevel +
                        " (missing: " + (mesh == null ? "mesh " : "") + (material == null ? "material " : "") +
                        (agentBuffer == null ? "agentBuffer " : "") + (visibleIndices == null ? "visibleIndices " : "") +
                        (argsBuffer == null ? "argsBuffer" : "") + ") - that tier renders nothing for this type.");
                }
                return;
            }

            MaterialPropertyBlock block = runtime.GetBlock(lodLevel);
            if (block == null)
            {
                // Unit types without a VAT profile still render with plain instancing.
                block = fallbackBlock;
                block.Clear();
            }

            block.SetBuffer(AgentBufferId, agentBuffer);
            block.SetBuffer(VisibleAgentIndicesId, visibleIndices);
            block.SetVector(CorpseSinkId, corpseSink);
            Graphics.DrawMeshInstancedIndirect(
                mesh,
                0,
                material,
                bounds,
                argsBuffer,
                0,
                block,
                runtime.GetShadowCasting(lodLevel),
                runtime.GetReceiveShadows(lodLevel));
        }
    }
}
