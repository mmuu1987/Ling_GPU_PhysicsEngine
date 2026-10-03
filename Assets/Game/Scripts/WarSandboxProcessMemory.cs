#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace MassEngine.Game
{
    /// <summary>Windows-only diagnostic counters. Some Unity Mono Process properties return zero without an error.</summary>
    public static class WarSandboxProcessMemory
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Counters
        {
            public uint size, pageFaults;
            public UIntPtr peakWorkingSet, workingSet, peakPagedPool, pagedPool;
            public UIntPtr peakNonPagedPool, nonPagedPool, pagefile, peakPagefile, privateUsage;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, ref Counters counters, uint size);
#endif

        public static bool TryRead(out long privateBytes, out long workingSet)
        {
            privateBytes = workingSet = 0;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var counters = new Counters { size = (uint)Marshal.SizeOf(typeof(Counters)) };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.size)) return false;
            ulong committed = counters.privateUsage.ToUInt64(), resident = counters.workingSet.ToUInt64();
            if (committed == 0 || resident == 0 || committed > long.MaxValue || resident > long.MaxValue) return false;
            privateBytes = (long)committed; workingSet = (long)resident;
            return true;
#else
            return false;
#endif
        }
    }
}
#endif
