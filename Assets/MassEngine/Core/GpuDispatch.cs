using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// 2026-10-17 (no unit cap): one dispatch dimension holds at most 65535 thread groups (D3D11 / Vulkan), i.e.
    /// 4,194,240 agents for a 64-wide kernel. Larger 1D workloads wrap into rows of 65535 groups; every 1D 64-wide
    /// kernel folds the row back with <c>id.x += id.y * RowThreads</c> (id.y is 0 below the limit, so nothing changes there).
    /// </summary>
    public static class GpuDispatch
    {
        public const int MaxGroupsPerDimension = 65535;
        public const uint RowThreads = 65535u * 64u; // keep in sync with the 4194240u literal in the compute kernels

        public static void Groups1D(int groups, out int x, out int y)
        {
            groups = Mathf.Max(1, groups);
            x = Mathf.Min(groups, MaxGroupsPerDimension);
            y = (groups + MaxGroupsPerDimension - 1) / MaxGroupsPerDimension;
        }

        /// <summary>Dispatches a 1D 64-wide kernel over <paramref name="groups"/> thread groups.</summary>
        public static void Dispatch1D(ComputeShader shader, int kernel, int groups)
        {
            Groups1D(groups, out int x, out int y);
            shader.Dispatch(kernel, x, y, 1);
        }

        /// <summary>Thread groups for <paramref name="count"/> items on a 64-wide kernel, without int overflow.</summary>
        public static int GroupsFor(long count) => (int)System.Math.Max(1L, System.Math.Min(int.MaxValue, (count + 63L) / 64L));
    }
}
