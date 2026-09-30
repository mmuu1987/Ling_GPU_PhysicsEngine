using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxModelTrialSmokeTests
    {
        private static MethodInfo Parser => typeof(WarSandboxModelTrialSmoke).GetMethod("AbsoluteDirectory", BindingFlags.Static | BindingFlags.NonPublic);

        [TestCase("C:/m54-smoke-path-test/output")]
        [TestCase(@"C:\m54-smoke-path-test\output")]
        [TestCase(@"\\example\share\m54-smoke-path-test")]
        public void ExplicitWindowsPathsAcceptBothSeparatorStyles(string path)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("Windows path contract.");
            Assert.NotNull(Parser);
            Assert.AreEqual(Path.GetFullPath(path), Parser.Invoke(null, new object[] { path }));
        }

        [TestCase("C:relative")]
        [TestCase(@"\root-relative")]
        [TestCase("relative/path")]
        [TestCase("C:/")]
        [TestCase("")]
        public void NonIsolatedOrRelativePathsRemainRejected(string path)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) Assert.Ignore("Windows path contract.");
            Assert.NotNull(Parser);
            var failure = Assert.Throws<TargetInvocationException>(() => Parser.Invoke(null, new object[] { path }));
            Assert.That(failure.InnerException, Is.TypeOf<InvalidOperationException>());
        }
    }
}
