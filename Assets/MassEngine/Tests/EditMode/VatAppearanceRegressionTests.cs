using System;
using System.Collections.Generic;
using MassEngine.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Tests
{
    public sealed class VatAppearanceRegressionTests
    {
        [Test]
        public void EmptyReadbackFailsEvenWhenClearHasColor()
        {
            Color32[] pixels = Solid(64, new Color32(50, 80, 110, 0));
            var metrics = VatAppearanceRegressionImageChecks.Measure(pixels);
            Assert.AreEqual(0, metrics.foregroundPixels);
            Assert.IsFalse(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(metrics, out _));
        }

        [Test]
        public void MagentaErrorShaderFails()
        {
            var metrics = VatAppearanceRegressionImageChecks.Measure(Solid(64, new Color32(255, 0, 255, 255)));
            Assert.AreEqual(64, metrics.magentaPixels);
            Assert.IsFalse(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(metrics, out string error));
            StringAssert.Contains("Magenta", error);
        }

        [Test]
        public void MixedAtlasColorsIncludingMagentaAreNotAnErrorShader()
        {
            // The shipped 39-vertex Female far mesh samples a multicolored atlas. Some magenta
            // triangles are legitimate texture content, unlike an almost entirely magenta error pass.
            Color32[] pixels = Solid(64, new Color32(60, 120, 180, 255));
            for (int i = 0; i < 16; i++) pixels[i] = new Color32(255, 0, 255, 255);
            var metrics = VatAppearanceRegressionImageChecks.Measure(pixels);
            Assert.AreEqual(16, metrics.magentaPixels);
            Assert.IsTrue(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(metrics, out string error), error);
        }

        [TestCase(89, true)]
        [TestCase(90, false)]
        [TestCase(99, false)]
        public void AlmostEntirelyMagentaForegroundFails(int magentaPixels, bool expectedPass)
        {
            Color32[] pixels = Solid(100, new Color32(60, 120, 180, 255));
            for (int i = 0; i < magentaPixels; i++) pixels[i] = new Color32(255, 0, 255, 255);
            Assert.AreEqual(expectedPass, VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(
                VatAppearanceRegressionImageChecks.Measure(pixels), out _));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReadbackOrientationNormalizesOnlyTopOriginApis(bool topOrigin)
        {
            var pixels = new Color32[6];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)i, 40, 90, 255);
            VatAppearanceRegressionImageChecks.NormalizeReadbackOrientationInPlace(pixels, 2, 3, topOrigin);
            byte[] expected = topOrigin ? new byte[] { 4, 5, 2, 3, 0, 1 } : new byte[] { 0, 1, 2, 3, 4, 5 };
            for (int i = 0; i < pixels.Length; i++) Assert.AreEqual(expected[i], pixels[i].r);
            // The odd center row and left-to-right order stay intact; this is not a model rotation.
            Assert.AreEqual(2, pixels[2].r);
            Assert.AreEqual(3, pixels[3].r);
        }

        [Test]
        public void ReadbackOrientationRejectsWrongDimensions()
        {
            Assert.Throws<ArgumentException>(() => VatAppearanceRegressionImageChecks.NormalizeReadbackOrientationInPlace(
                new Color32[5], 2, 3, true));
        }

        [Test]
        public void OrdinaryOpaqueCharacterPassesVisibilityAndPinkChecks()
        {
            var metrics = VatAppearanceRegressionImageChecks.Measure(Solid(64, new Color32(60, 120, 180, 255)));
            Assert.IsTrue(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(metrics, out string error), error);
        }

        [Test]
        public void OneOpaquePixelCannotMakeEmptyFramePass()
        {
            Color32[] pixels = Solid(64, new Color32(50, 80, 110, 0));
            pixels[0] = new Color32(60, 120, 180, 255);
            Assert.IsFalse(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(
                VatAppearanceRegressionImageChecks.Measure(pixels), out _));
        }

        [Test]
        public void IdenticalBindingPoseCannotPassAnimationGate()
        {
            Color32[] first = Solid(64, new Color32(60, 120, 180, 255));
            Assert.IsFalse(VatAppearanceRegressionImageChecks.HasMotion(
                new[] { first, (Color32[])first.Clone(), (Color32[])first.Clone() }, out int changed));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void BackgroundChangesCannotFakeAnimation()
        {
            Color32[] first = Solid(64, new Color32(20, 30, 40, 0));
            Color32[] second = Solid(64, new Color32(180, 120, 80, 0));
            Assert.IsFalse(VatAppearanceRegressionImageChecks.HasMotion(new[] { first, second }, out int changed));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void FourChangedForegroundPixelsPassAnimationGate()
        {
            Color32[] first = Solid(64, new Color32(60, 120, 180, 255));
            Color32[] second = (Color32[])first.Clone();
            for (int i = 0; i < 4; i++) second[i] = new Color32(150, 120, 180, 255);
            Assert.IsTrue(VatAppearanceRegressionImageChecks.HasMotion(new[] { first, second }, out int changed));
            Assert.AreEqual(4, changed);
        }

        [Test]
        public void SubThresholdColorNoiseCannotFakeAnimation()
        {
            Color32[] first = Solid(64, new Color32(60, 120, 180, 255));
            Color32[] second = Solid(64, new Color32(64, 124, 184, 255));
            Assert.IsFalse(VatAppearanceRegressionImageChecks.HasMotion(new[] { first, second }, out _));
        }

        [Test]
        public void PixelPairRequiresMatchingNonemptyArrays()
        {
            Assert.Throws<ArgumentException>(() => VatAppearanceRegressionImageChecks.CountChanged(new Color32[0], new Color32[0]));
            Assert.Throws<ArgumentException>(() => VatAppearanceRegressionImageChecks.CountChanged(new Color32[2], new Color32[3]));
            Assert.Throws<ArgumentException>(() => VatAppearanceRegressionImageChecks.HasMotion(new[] { new Color32[8] }, out _));
        }

        [Test]
        public void EmptySilhouettesDoNotScoreAsPerfectAgreement()
        {
            Assert.AreEqual(0f, VatAppearanceRegressionImageChecks.SilhouetteIoU(new Color32[8], new Color32[8]));
            Color32[] opaque = Solid(8, new Color32(50, 100, 150, 255));
            Assert.AreEqual(1f, VatAppearanceRegressionImageChecks.SilhouetteIoU(opaque, opaque));
        }

        [TestCase(141, 0, 46, 93, 140)]
        [TestCase(16, 0, 5, 10, 15)]
        [TestCase(15, 0, 4, 9, 14)]
        public void SampleFramesStayInsideClipAndIncludeLast(int count, int a, int b, int c, int d)
        {
            var clip = new VATProfile.VATClipWindow { frameCount = count, frameRate = 30, startFrame = 173 };
            CollectionAssert.AreEqual(new[] { a, b, c, d }, VatAppearanceRegression.SampleLocalFrames(clip));
            foreach (int frame in VatAppearanceRegression.SampleLocalFrames(clip))
                Assert.Less((frame + .25f) / clip.frameRate, count / (float)clip.frameRate,
                    "The last looping sample must not wrap to frame zero.");
        }

        [Test]
        public void InvalidClipSamplingIsRejected()
        {
            Assert.Throws<ArgumentException>(() => VatAppearanceRegression.SampleLocalFrames(default));
        }

        // These tests are intentionally real GPU draws. A plain MeshRenderer, a missing procedural
        // variant, or a binding-pose-only implementation renders the same image at every time and FAILS.
        // Explicitly skipped only when the test host has no graphics device; the CLI runner instead fails.
        [TestCase(VatAppearanceRegression.MaleUnitPath, 0)]
        [TestCase(VatAppearanceRegression.MaleUnitPath, 1)]
        [TestCase(VatAppearanceRegression.MaleUnitPath, 2)]
        [TestCase(VatAppearanceRegression.FemaleUnitPath, 0)]
        [TestCase(VatAppearanceRegression.FemaleUnitPath, 1)]
        [TestCase(VatAppearanceRegression.FemaleUnitPath, 2)]
        [Category("GPU")]
        public void ShippedRuntimeLodRendersAllFourAnimatedClips(string path, int lod)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsInstancing || !SystemInfo.supportsComputeShaders)
                Assert.Ignore("M5.3 GPU test needs a real graphics device; never run the regression CLI with -nographics.");
            UnitTypeConfig unit = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(path);
            Assert.NotNull(unit);
            VATProfile profile = unit.renderConfig.vatProfile as VATProfile;
            Assert.NotNull(profile);
            ResolvedUnitTypeRuntime runtime = ResolvedUnitTypeRuntime.Resolve(unit, 1f);
            bool wasDirty = EditorUtility.IsDirty(unit.renderConfig);
            using (var capture = new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(profile)))
            {
                foreach (AgentState state in new[] { AgentState.Idle, AgentState.Move, AgentState.Attack, AgentState.Dead })
                {
                    VATProfile.VATClipWindow clip = VatAppearanceRegression.GetClip(profile, state);
                    var frames = new List<Color32[]>();
                    foreach (int frame in VatAppearanceRegression.SampleLocalFrames(clip))
                    {
                        Color32[] image = capture.Capture(runtime, lod, state, (frame + .25f) / clip.frameRate);
                        Assert.IsTrue(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(
                            VatAppearanceRegressionImageChecks.Measure(image), out string error), path + "/" + lod + "/" + state + ": " + error);
                        frames.Add(image);
                    }
                    Assert.IsTrue(VatAppearanceRegressionImageChecks.HasMotion(frames, out int changed),
                        path + "/" + lod + "/" + state + " is frozen; max GPU pixel difference=" + changed);
                    // Negative control: identical agent time must produce an identical readback.
                    Color32[] repeat = capture.Capture(runtime, lod, state, .25f / clip.frameRate);
                    Assert.AreEqual(0, VatAppearanceRegressionImageChecks.CountChanged(frames[0], repeat),
                        "The fixture itself must be deterministic; camera/light/background changes cannot fake animation.");
                    if (state == AgentState.Dead)
                    {
                        Color32[] clamped = capture.Capture(runtime, lod, state, (clip.frameCount + 10.25f) / clip.frameRate);
                        Assert.AreEqual(0, VatAppearanceRegressionImageChecks.CountChanged(frames[3], clamped), "Death should clamp on the GPU.");
                    }
                }
            }
            Assert.AreEqual(wasDirty, EditorUtility.IsDirty(unit.renderConfig), "The fixture must not dirty the source RenderConfig.");
        }

        private static Color32[] Solid(int count, Color32 color)
        {
            var pixels = new Color32[count];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            return pixels;
        }
    }
}
