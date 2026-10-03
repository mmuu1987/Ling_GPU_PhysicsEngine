using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using MassEngine.Projectiles;
using MassEngine.Editor;
using UnityEditor;

namespace MassEngine.Tests
{
    public sealed class MotionProjectileContractTests
    {
        [TestCase(0)]
        [TestCase(2)]
        public void ActualM7VatDrawUsesPresentationNotTacticalEngage(int lod)
        {
            // Ten tiny in-memory draws total, no baking, screenshots, scenes or browsers.
            var unit = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>("Assets/Game/M71LaunchPresets/launch-open/Unit1.asset");
            Assert.NotNull(unit);
            var profile = unit.renderConfig.vatProfile as VATProfile;
            Assert.NotNull(profile);
            bool dirty = EditorUtility.IsDirty(unit.renderConfig);
            var runtime = ResolvedUnitTypeRuntime.Resolve(unit, 1f);
            using (var capture = new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(profile), 128))
            {
                var idle = capture.Capture(runtime, lod, AgentState.Idle, .2f);
                var move = capture.Capture(runtime, lod, AgentState.Move, .2f);
                Assert.True(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(
                    VatAppearanceRegressionImageChecks.Measure(idle), out var idleError), idleError);
                Assert.True(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(
                    VatAppearanceRegressionImageChecks.Measure(move), out var moveError), moveError);
                Assert.Greater(VatAppearanceRegressionImageChecks.CountChanged(idle, move), 4,
                    "Move must not accidentally sample Idle after the AgentData layout change.");
                var blocked = capture.Capture(runtime, lod, AgentState.Engage, .2f, AgentState.Idle);
                var advancing = capture.Capture(runtime, lod, AgentState.Engage, .2f, AgentState.Move);
                Assert.AreEqual(0, VatAppearanceRegressionImageChecks.CountChanged(idle, blocked));
                Assert.AreEqual(0, VatAppearanceRegressionImageChecks.CountChanged(move, advancing));
                var later = capture.Capture(runtime, lod, AgentState.Move, .4f);
                Assert.Greater(VatAppearanceRegressionImageChecks.CountChanged(move, later), 4);
            }
            Assert.AreEqual(dirty, EditorUtility.IsDirty(unit.renderConfig));
        }

        [Test] public void LayoutOffsetsAreExplicitAndExistingOffsetsStayStable()
        {
            Assert.AreEqual(64, Marshal.SizeOf<AgentData>());
            Assert.AreEqual(56, Marshal.OffsetOf<AgentData>(nameof(AgentData.presentationState)).ToInt32());
            Assert.AreEqual(60, Marshal.OffsetOf<AgentData>(nameof(AgentData.locomotionSpeed)).ToInt32());
            Assert.AreEqual(144, Marshal.SizeOf<UnitTypeGpuSettings>());
            Assert.AreEqual(120, Marshal.OffsetOf<UnitTypeGpuSettings>(nameof(UnitTypeGpuSettings.moveReferenceSpeed)).ToInt32());
            Assert.AreEqual(140, Marshal.OffsetOf<UnitTypeGpuSettings>(nameof(UnitTypeGpuSettings.projectileTargetHeight)).ToInt32());
            Assert.AreEqual(64, Marshal.SizeOf<ProjectileGpuData>());
            Assert.AreEqual(56, Marshal.OffsetOf<ProjectileGpuData>(nameof(ProjectileGpuData.sourceAgentIndexPlusOne)).ToInt32());
        }
        [Test] public void DefaultsSupportIdleHysteresisAndBodyRelativeLaunch()
        {
            var s = UnitTypeGpuSettings.CreateDefaults(0);
            Assert.Greater(s.moveStartSpeed, s.moveStopSpeed);
            Assert.Less(s.moveAnimationSpeedMin, .85f);
            Assert.That(s.attackReleasePhase, Is.InRange(.05f, .95f));
            Assert.Greater(s.projectileOriginHeight, 0);
            Assert.Greater(s.projectileTargetHeight, 0);
            Assert.AreEqual(0, ProjectileGpuData.CreateEmpty().sourceAgentIndexPlusOne);
        }
        [Test] public void ModulesClampTuningWithoutChangingAssets()
        {
            var animation = ScriptableObject.CreateInstance<AnimationConfig>();
            var combat = ScriptableObject.CreateInstance<CombatConfig>();
            try
            {
                animation.moveStopSpeed = .4f; animation.moveStartSpeed = .1f;
                animation.moveReferenceSpeed = -1; combat.attackReleasePhase = 2;
                combat.projectileOriginHeight = -3; combat.projectileTargetHeight = -4;
                var s = UnitTypeGpuSettings.CreateDefaults(0);
                new DefaultAnimationModule(animation).Contribute(ref s);
                new DefaultCombatModule(combat).Contribute(ref s);
                Assert.Greater(s.moveStartSpeed, s.moveStopSpeed);
                Assert.AreEqual(0, s.moveReferenceSpeed); Assert.AreEqual(.95f, s.attackReleasePhase);
                Assert.Greater(s.projectileOriginHeight, 0); Assert.Greater(s.projectileTargetHeight, 0);
                Assert.AreEqual(.1f, animation.moveStartSpeed, "Runtime validation must not rewrite shared assets.");
                animation.moveReferenceSpeed = float.NaN; animation.moveStartSpeed = float.PositiveInfinity;
                combat.attackReleasePhase = float.NaN; combat.projectileTargetHeight = float.PositiveInfinity;
                new DefaultAnimationModule(animation).Contribute(ref s);
                new DefaultCombatModule(combat).Contribute(ref s);
                Assert.AreEqual(0f, s.moveReferenceSpeed);
                Assert.Greater(s.moveStartSpeed, s.moveStopSpeed);
                Assert.AreEqual(.55f, s.attackReleasePhase); Assert.AreEqual(1f, s.projectileTargetHeight);
            }
            finally { Object.DestroyImmediate(animation); Object.DestroyImmediate(combat); }
        }
    }
}
