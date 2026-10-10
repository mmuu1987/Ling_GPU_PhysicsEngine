using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public class UnitPreviewRadiusTests
    {
        [TestCase(.001f)] [TestCase(.45f)] [TestCase(.55f)] [TestCase(4.8f)]
        public void ReadOnlyValueUsesActualGpuBuilder(float radius)
        {
            var config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            var flock = ScriptableObject.CreateInstance<FlockingConfig>(); config.flockingConfig = flock; flock.agentRadius = radius;
            try
            {
                string before = JsonUtility.ToJson(config), fb = JsonUtility.ToJson(flock);
                var info = UnitPreviewRadius.Read(config);
                Assert.That(info.Available, Is.True);
                Assert.That(info.BaseRadius, Is.EqualTo(new DefaultSwordUnit(config).BuildGpuSettings().agentRadius));
                Assert.That(info.Diameter, Is.EqualTo(info.BaseRadius * 2));
                Assert.That(info.ContactRadius(new Vector3(2, 5, .5f)), Is.EqualTo(info.BaseRadius * 2));
                Assert.That(info.ContactRadius(new Vector3(-.5f, 9, -3)), Is.EqualTo(info.BaseRadius * 3));
                Assert.That(JsonUtility.ToJson(config), Is.EqualTo(before)); Assert.That(JsonUtility.ToJson(flock), Is.EqualTo(fb));
                StringAssert.Contains("FlockingConfig.agentRadius", info.Source);
            }
            finally { Object.DestroyImmediate(config); Object.DestroyImmediate(flock); }
        }
        [Test]
        public void MissingConfigHasExplicitRuntimeDefaultAndCustomModuleIsNotGuessed()
        {
            var config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            try
            {
                var info = UnitPreviewRadius.Read(config);
                Assert.That(info.BaseRadius, Is.EqualTo(UnitTypeGpuSettings.CreateDefaults(0).agentRadius));
                StringAssert.Contains("默认", info.Source);
                config.unitTypeClassName = "Unverified.CustomModule";
                Assert.That(UnitPreviewRadius.Read(config).Available, Is.False);
                Assert.That(UnitPreviewRadius.Read(null).Available, Is.False);
            }
            finally { Object.DestroyImmediate(config); }
        }
        [Test]
        public void AnnulusCentrelineIsExactlyOneWorldUnitAndNeverBoundsCentred()
        {
            var mesh = UnitModelPreviewRenderer.CreateRadiusRingMesh();
            try
            {
                var points = mesh.vertices; Assert.That(points.Length, Is.EqualTo(UnitModelPreviewRenderer.RingSegments * 2));
                for (int i = 0; i < points.Length; i += 2)
                {
                    var centreline = (points[i] + points[i + 1]) * .5f;
                    Assert.That(centreline.y, Is.Zero);
                    Assert.That(centreline.magnitude, Is.EqualTo(1).Within(.000001));
                    Assert.That(points[i].magnitude, Is.EqualTo(1 - UnitModelPreviewRenderer.RingHalfWidth).Within(.000001));
                }
                Assert.That(mesh.bounds.center.sqrMagnitude, Is.LessThan(.0000001));
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
