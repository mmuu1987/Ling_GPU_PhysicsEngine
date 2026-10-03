using System;
using System.IO;
using System.Linq;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxContentQualityTests
    {
        private static WarSandboxBattlefieldCatalog Catalog => AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(OfficialRosterQualityBuilder.CatalogPath);
        [Test]
        public void QualityVersionHidesChoicesWithoutDeletingAnyVersion06Identity()
        {
            var old = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(OfficialRosterQualityBuilder.Previous + "/Catalog.asset");
            var current = Catalog; Assert.That(current, Is.Not.Null);
            Assert.That(current.entries.Select(e => e.id), Is.EqualTo(old.entries.Select(e => e.id)));
            Assert.That(current.templates.Select(t => t.templateId), Is.EqualTo(old.templates.Select(t => t.templateId)));
            Assert.That(current.SelectableEntryCount, Is.EqualTo(old.entries.Length - 2));
            Assert.That(current.templates.Count(t => t.hiddenFromSelection), Is.EqualTo(2));
            foreach (var t in old.templates)
            {
                Assert.That(current.TryResolveTemplate(t.templateId, t.revision, out var config, out string error), Is.True, error);
                Assert.That(config, Is.SameAs(t.config), t.templateId);
            }
            foreach (var id in OfficialRosterQualityBuilder.WithdrawnBattlefields)
            {
                Assert.That(current.TryResolve(id, File.Exists, out var entry, out string error), Is.True, error);
                Assert.That(entry.hiddenFromSelection, Is.True);
                Assert.That(entry.selectionNote, Does.Contain("原始FBX"));
            }
        }
        [Test]
        public void OnlyTheApprovedTwoHeadCandidatesAreWithdrawn()
        {
            var current = Catalog;
            Assert.That(current.entries.Where(e => e.hiddenFromSelection).Select(e => e.id), Is.EquivalentTo(OfficialRosterQualityBuilder.WithdrawnBattlefields));
            Assert.That(current.templates.Where(t => t.hiddenFromSelection).Select(t => t.templateId), Is.EquivalentTo(OfficialRosterQualityBuilder.WithdrawnTemplates));
            Assert.That(current.FindTemplate("roster-platformer-crab").hiddenFromSelection, Is.False);
            Assert.That(current.FindTemplate("roster-giant-alien").unitPreview, Is.Not.Null);
            foreach (var t in current.templates.Where(t => t.hiddenFromSelection))
                Assert.That(current.IsSelectableTemplate(t.config), Is.False);
        }
        [Test]
        public void EveryFormalChoiceHasAnActualStaticPortraitAndValidRepresentative()
        {
            var current = Catalog;
            foreach (var t in current.templates.Where(t => !t.hiddenFromSelection))
            {
                Assert.That(t.unitPreview, Is.Not.Null, t.templateId);
                Assert.That(AssetDatabase.GetAssetPath(t.unitPreview), Does.StartWith(OfficialRosterQualityBuilder.Root + "/UnitPreviews/"));
                Assert.That(t.unitPreview.width, Is.EqualTo(448)); Assert.That(t.unitPreview.height, Is.EqualTo(560));
            }
            foreach (var e in current.entries.Where(e => !e.hiddenFromSelection))
            {
                var choices = WarSandboxFrontEnd.PreviewChoices(current, e);
                Assert.That(choices, Is.Not.Empty, e.id);
                Assert.That(choices.All(t => !t.hiddenFromSelection && t.unitPreview != null), Is.True, e.id);
            }
            var alien = current.entries.Single(e => e.id == "giants-alien");
            Assert.That(alien.featuredTemplateIds[0], Is.EqualTo("roster-giant-alien"), "The marked battlefield shows its own giant, not an arbitrary library unit.");
            Assert.That(WarSandboxFrontEnd.PreviewChoices(current, current.entries.Single(e => e.id == "unified-regular")).Count, Is.GreaterThan(1));
        }
        [Test]
        public void BattlefieldRequestCopyPreservesAvailabilityAndOwnsPreviewIdArray()
        {
            var source = new WarSandboxBattlefieldEntry { hiddenFromSelection = true, selectionNote = "withdrawn", featuredTemplateIds = new[] { "a", "b" } };
            var copy = source.CopyIdentity(); source.featuredTemplateIds[0] = "changed";
            Assert.That(copy.hiddenFromSelection, Is.True); Assert.That(copy.selectionNote, Is.EqualTo("withdrawn"));
            Assert.That(copy.featuredTemplateIds, Is.EqualTo(new[] { "a", "b" })); Assert.That(copy.featuredTemplateIds, Is.Not.SameAs(source.featuredTemplateIds));
        }
        [Test]
        public void LegacyAbsentAvailabilityMetadataStillAllowsItsOwnUnit()
        {
            var catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>(); var config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            try
            {
                Assert.That(catalog.IsSelectableTemplate(config), Is.True);
                catalog.templates = new[] { new WarSandboxUnitTemplateEntry { templateId = "legacy", config = config } };
                Assert.That(catalog.IsSelectableTemplate(config), Is.True);
            }
            finally { Object.DestroyImmediate(catalog); Object.DestroyImmediate(config); }
        }
        [TestCase(0, 1, 3)]
        [TestCase(3, -1, 0)]
        [TestCase(3, 1, 3)]
        [TestCase(0, -1, 0)]
        public void ArrowNavigationSkipsWithdrawnRowsWithoutChangingCardIndices(int current, int step, int expected)
        {
            var rows = new[] { new WarSandboxBattlefieldEntry(), new WarSandboxBattlefieldEntry { hiddenFromSelection = true },
                new WarSandboxBattlefieldEntry { hiddenFromSelection = true }, new WarSandboxBattlefieldEntry() };
            Assert.That(WarSandboxFrontEnd.NextCatalogIndex(rows, current, step), Is.EqualTo(expected));
        }
        [TestCase(1280f, 720f)]
        [TestCase(960f, 540f)]
        [TestCase(640f, 480f)]
        public void DetailOverviewAndFullBodyPortraitDoNotOverlapOrStretch(float screenWidth, float screenHeight)
        {
            float scale = Mathf.Clamp(Mathf.Min(screenWidth / 1280, screenHeight / 720), .85f, 1.5f);
            float w = screenWidth / scale, h = screenHeight / scale, left = Mathf.Clamp(w * .34f, 300, 440), x = 24 + left + 24 + 28;
            var area = new Rect(x, 318, w - (24 + left + 24) - 24 - 56, h - 24 - 70 - 16 - 318);
            WarSandboxFrontEnd.CatalogPreviewLayout(area, out var overview, out var unit);
            Assert.That(overview.xMax, Is.LessThan(unit.x));
            Assert.That(unit.xMax, Is.LessThanOrEqualTo(area.xMax)); Assert.That(unit.yMax, Is.LessThanOrEqualTo(area.yMax));
            var picture = WarSandboxUGUI.AspectFit(unit, 448, 560);
            Assert.That(picture.width / picture.height, Is.EqualTo(448f / 560f).Within(.00001f));
            Assert.That(picture.xMin, Is.GreaterThanOrEqualTo(unit.xMin - .001f)); Assert.That(picture.xMax, Is.LessThanOrEqualTo(unit.xMax + .001f));
            Assert.That(picture.yMin, Is.GreaterThanOrEqualTo(unit.yMin - .001f)); Assert.That(picture.yMax, Is.LessThanOrEqualTo(unit.yMax + .001f));
        }
        [Test]
        public void HistoricalAuthoringAndBuildCannotOverwriteVersion06()
        {
            Assert.Throws<InvalidOperationException>(OfficialRosterBuilder.Prepare06);
            Assert.Throws<InvalidOperationException>(OfficialRosterBuilder.Build09);
            Assert.That(OfficialRosterBuilder.Root, Is.EqualTo(OfficialRosterQualityBuilder.Root));
            Assert.That(OfficialRosterBuilder.Scenes(Catalog), Is.EqualTo(OfficialRosterQualityBuilder.Scenes(Catalog)));
        }
    }
}
