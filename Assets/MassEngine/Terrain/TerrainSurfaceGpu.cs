using System;
using UnityEngine;

namespace MassEngine
{
    /// <summary>Shared static surface buffers; owned and disposed by their caller.</summary>
    public sealed class TerrainSurfaceGpu : IDisposable
    {
        private ComputeBuffer heights, walkable;
        private readonly TerrainSurface surface;
        public bool IsAllocated => heights != null && walkable != null;
        public long Bytes => surface.GpuBytes;

        public TerrainSurfaceGpu(TerrainSurface source)
        {
            surface = source ?? throw new ArgumentNullException(nameof(source));
            try
            {
                heights = new ComputeBuffer(source.Width * source.Depth, 4);
                walkable = new ComputeBuffer(source.CellCount, 4);
                heights.SetData(source.CopyHeights()); walkable.SetData(source.CopyWalkable());
            }
            catch { Dispose(); throw; }
        }

        public void Bind(ComputeShader shader, int kernel)
        {
            if (!IsAllocated) throw new ObjectDisposedException(nameof(TerrainSurfaceGpu));
            shader.SetBuffer(kernel, "_SurfaceHeights", heights);
            shader.SetBuffer(kernel, "_SurfaceWalkable", walkable);
            shader.SetInts("_SurfaceDimensions", surface.Width, surface.Depth);
            shader.SetVector("_SurfaceBounds", new Vector4(surface.Origin.x, surface.Origin.y, surface.Size.x, surface.Size.y));
        }

        public void Dispose()
        {
            heights?.Release(); walkable?.Release(); heights = null; walkable = null;
        }
    }
}
