#if UNITY_EDITOR_WIN
using System;
using System.Reflection;
using NUnit.Framework;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxReleaseMemoryTests
    {
        [Test]
        public void WindowsCountersAreAvailableAndNonZero()
        {
            Assert.IsTrue(WarSandboxProcessMemory.TryRead(out long committed, out long resident));
            Assert.Greater(committed, 0); Assert.Greater(resident, 0);
        }

        [TestCase(false, 1024L, 1024L)]
        [TestCase(true, 0L, 1024L)]
        [TestCase(true, 1024L, 0L)]
        public void RetainedMemoryCannotPassWithUnavailableCounters(bool available, long committed, long resident)
        {
            var baseline = Sample(true, 1024, 1024);
            var current = Sample(available, committed, resident);
            var error = Assert.Throws<TargetInvocationException>(() => Compare(baseline, current));
            Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
            StringAssert.Contains("unavailable process memory", error.InnerException.Message);
        }

        [Test]
        public void PrivateCommitGrowthOverBudgetIsRejected()
        {
            const long MiB = 1024 * 1024;
            var error = Assert.Throws<TargetInvocationException>(() => Compare(Sample(true, 128 * MiB, 64 * MiB), Sample(true, 193 * MiB, 64 * MiB)));
            StringAssert.Contains("Retained memory grew", error.InnerException.Message);
        }

        [Test]
        public void EqualValidCountersRemainAccepted() => Compare(Sample(true, 1024, 1024), Sample(true, 1024, 1024));

        private static object Sample(bool available, long committed, long resident)
        {
            Type type = typeof(WarSandboxTerrainCycle).GetNestedType("MemorySample", BindingFlags.NonPublic);
            var sample = Activator.CreateInstance(type, true);
            type.GetField("processMemoryAvailable").SetValue(sample, available);
            type.GetField("privateBytes").SetValue(sample, committed);
            type.GetField("workingSet").SetValue(sample, resident);
            type.GetField("audioServices").SetValue(sample, 1);
            type.GetField("audioSources").SetValue(sample, 1);
            type.GetField("cueClips").SetValue(sample, 5);
            return sample;
        }
        private static void Compare(object baseline, object current) =>
            typeof(WarSandboxTerrainCycle).GetMethod("VerifyRetainedMemory", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new[] { baseline, current });
    }
}
#endif
