using System;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    /// <summary>Preparation-only checks. These do not assert an imported model, GPU bake, battle result or new release.</summary>
    public sealed class Troops6PreparationTests
    {
        [Test]
        public void ProposedModelsHaveUniqueIdentitiesAndFreshOutputNames()
        {
            CollectionAssert.AreEqual(new[] { "Birb", "Bunny", "Fish" }, Troops6Builder.Names);
            CollectionAssert.AreEqual(new[] { "birb", "bunny", "fish" }, Troops6Builder.Keys);
            Assert.That(Troops6Builder.Keys.Distinct().Count(), Is.EqualTo(3));
            Assert.That(Troops6Builder.SourceIds.Distinct().Count(), Is.EqualTo(3));
            var outputs = Enumerable.Range(0, 3).Select(Troops6Builder.Output).ToArray();
            Assert.That(outputs.Distinct().Count(), Is.EqualTo(3));
            Assert.That(outputs.All(p => p.StartsWith("Assets/Game/Authoring/CharacterPipeline/Generated/", StringComparison.Ordinal)), Is.True);
            CollectionAssert.AreEqual(new[] { "roster-birb", "roster-bunny", "roster-fish" }, Enumerable.Range(0, 3).Select(Troops6Builder.TemplateId));
            CollectionAssert.AreEqual(new[] { "troops6-birb", "troops6-bunny", "troops6-fish" }, Enumerable.Range(0, 3).Select(Troops6Builder.EntryId));
        }

        [Test]
        public void ProposedProfilesStayWithinTheExistingRegularPolicy()
        {
            var policy = AssetDatabase.LoadAssetAtPath<WarSandboxRosterPolicy>(Troops6Builder.RegularPolicyPath);
            Assert.That(policy, Is.Not.Null);
            Assert.That(policy.templates.Length, Is.EqualTo(19), "The completed batch-5 policy remains the inherited input.");
            Assert.That(Troops6Builder.Radii.All(r => r > 0 && r <= policy.maximumRadius), Is.True);
            CollectionAssert.AreEqual(new[] { false, false, false }, Troops6Builder.Ranged);
            Assert.That(Troops6Builder.Hp.All(n => n > 0), Is.True);
            Assert.That(Troops6Builder.Damage.All(n => n > 0), Is.True);
            Assert.That(Troops6Builder.Counts.All(n => n > 0), Is.True);
            Assert.That(Troops6Builder.Speed.All(v => v > 0 && v <= 20), Is.True);
        }

        [Test]
        public void HttpSuccessQuotaHtmlIsNotAnFbxAsset()
        {
            var html = Encoding.UTF8.GetBytes("<!DOCTYPE html><title>Google Drive - Quota exceeded</title>" + new string(' ', 2000));
            Assert.That(Troops6Builder.IsFbxPayload(html, "text/html; charset=utf-8"), Is.False);
            Assert.That(CharacterPipeline.IsFbxPayload(html, "text/html; charset=utf-8"), Is.False);
            Assert.That(Troops6Builder.IsFbxPayload(html, "application/octet-stream"), Is.False, "A misleading MIME type cannot make quota HTML an FBX.");
            Assert.That(Troops6Builder.IsFbxPayload(null), Is.False);
            Assert.That(Troops6Builder.IsFbxPayload(new byte[32]), Is.False);
        }

        [TestCase("text/html")]
        [TestCase("text/html; charset=utf-8")]
        public void HtmlMimeIsRejectedEvenIfTheBodyStartsWithAnFbxMarker(string mime)
        {
            var bytes = new byte[4096];
            var marker = Encoding.ASCII.GetBytes("Kaydara FBX Binary  ");
            Array.Copy(marker, bytes, marker.Length);
            Assert.That(Troops6Builder.IsFbxPayload(bytes, mime), Is.False);
        }

        [Test]
        public void PayloadSniffRecognizesBinaryAndAsciiHeadersButDoesNotReplaceAnImporter()
        {
            var binary = new byte[4096];
            var marker = Encoding.ASCII.GetBytes("Kaydara FBX Binary  ");
            Array.Copy(marker, binary, marker.Length);
            Assert.That(Troops6Builder.IsFbxPayload(binary, "application/octet-stream"), Is.True, "Header sniff only; this synthetic buffer is not a complete FBX.");
            var ascii = Encoding.ASCII.GetBytes("; FBX 7.4.0 project file\nFBXHeaderExtension: { }\n" + new string(' ', 2000));
            Assert.That(Troops6Builder.IsFbxPayload(ascii, "text/plain"), Is.True);
            Assert.That(Troops6Builder.IsFbxPayload(new byte[4096]), Is.False);
        }

        [Test]
        public void MissingSourcesFailBeforeAnyNewOutputDirectoryIsCreated()
        {
            bool allPresent = Troops6Builder.Names.All(n => File.Exists(Troops6Builder.SourceDirectory + "/" + n + ".fbx"));
            if (allPresent) Assert.Ignore("Official sources now exist; this missing-source fixture is no longer applicable. Run model/pipeline checks instead.");
            var paths = Enumerable.Range(0, 3).Select(Troops6Builder.Output).Concat(new[] { Troops6Builder.Prepared, Troops6Builder.Integrated }).ToArray();
            var before = paths.Select(Directory.Exists).ToArray();
            var error = Assert.Throws<InvalidOperationException>(() => Troops6Builder.ValidateSources());
            Assert.That(error.Message, Does.Contain("waiting for the official source"));
            CollectionAssert.AreEqual(before, paths.Select(Directory.Exists), "Missing inputs must not leave authored outputs.");
        }

        [Test]
        public void ExistingVersion05StillContainsOnlyItsCompletedContent()
        {
            const string catalogPath = "Assets/Game/Content/Characters/OfficialRoster/Version05/Catalog.asset";
            var before = File.ReadAllBytes(catalogPath);
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(catalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.entries.Length, Is.EqualTo(27));
            foreach (string id in Enumerable.Range(0, 3).Select(Troops6Builder.EntryId))
                Assert.That(catalog.entries.Any(e => e.id == id), Is.False, "Unprepared models must not be advertised as released.");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(catalogPath));
        }
    }
}
