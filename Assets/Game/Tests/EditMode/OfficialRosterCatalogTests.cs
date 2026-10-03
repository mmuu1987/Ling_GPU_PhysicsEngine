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
    /// <summary>Current official catalog (OfficialRosterBuilder.Prepare07, curated v7): M7.1 launch content first and unchanged, new-unit battlefields after.</summary>
    public sealed class OfficialRosterCatalogTests
    {
        private static WarSandboxBattlefieldCatalog Official => Load<WarSandboxBattlefieldCatalog>(OfficialRosterBuilder.CatalogPath);

        [Test]
        public void OfficialCatalogValidatesWithEveryScene()
        {
            var catalog = Official;
            Assert.That(catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out string error), Is.True, error);
            Assert.That(catalog.TryValidateTemplates(out error), Is.True, error);
            Assert.That(catalog.defaultEntryId, Is.EqualTo("launch-open"), "First run still enters the quick 512-unit battlefield.");
            Assert.That(catalog.entries.Length, Is.EqualTo(5 + OfficialRosterBuilder.NewEntries.Length));
        }

        [Test]
        public void LaunchPresetsComeFirstAndAreUnchanged()
        {
            var launch = Load<WarSandboxBattlefieldCatalog>(WarSandboxLaunchPresetsBuilder.CatalogPath);
            var official = Official;
            for (int i = 0; i < launch.entries.Length; i++)
            {
                WarSandboxBattlefieldEntry a = launch.entries[i], b = official.entries[i];
                Assert.That(b.id, Is.EqualTo(a.id)); Assert.That(b.displayName, Is.EqualTo(a.displayName));
                Assert.That(b.scenePath, Is.EqualTo(a.scenePath)); Assert.That(b.contentVersion, Is.EqualTo(a.contentVersion));
                Assert.That(b.rules, Is.SameAs(a.rules)); Assert.That(b.preview, Is.SameAs(a.preview));
                Assert.That(b.terrainSurface, Is.SameAs(a.terrainSurface)); Assert.That(b.description, Is.EqualTo(a.description));
            }
            foreach (var t in launch.templates)
            {
                Assert.That(official.TryResolveTemplate(t.templateId, t.revision, out var config, out string error), Is.True, error);
                Assert.That(config, Is.SameAs(t.config));
            }
        }

        [Test]
        public void NewUnitBattlefieldsUsePlayerFacingTextPreviewsAndSourceTemplates()
        {
            var source = Load<WarSandboxBattlefieldCatalog>(OfficialRosterBuilder.SourceCatalog);
            var official = Official;
            string[] internalLabels = { "第一批", "第二批", "超大", "受限", "示例", "模型展示" };
            for (int i = 0; i < OfficialRosterBuilder.NewEntries.Length; i++)
            {
                var spec = OfficialRosterBuilder.NewEntries[i];
                var entry = official.entries[5 + i];
                var original = source.entries.Single(e => e.id == spec[0]);
                Assert.That(entry.id, Is.EqualTo(spec[0]));
                Assert.That(entry.displayName, Is.EqualTo(spec[1]));
                Assert.That(entry.scenePath, Is.EqualTo(original.scenePath));
                Assert.That(entry.rules, Is.SameAs(original.rules));
                Assert.That(entry.description, Does.Contain(spec[2]).And.Contain(" 支军团 · "));
                Assert.That(entry.briefing, Is.Not.Null.And.Not.Empty);
                foreach (string label in internalLabels)
                    Assert.That(entry.displayName + entry.description + entry.briefing, Does.Not.Contain(label), entry.id);
                Assert.That(entry.preview, Is.Not.Null, "Catalog cards need a preview: " + entry.id);
                Assert.That(AssetDatabase.GetAssetPath(entry.preview), Does.StartWith(OfficialRosterBuilder.Root + "/Previews/"));
            }
            foreach (var t in source.templates)
            {
                Assert.That(official.TryResolveTemplate(t.templateId, t.revision, out var config, out string error), Is.True, error);
                Assert.That(config, Is.SameAs(t.config));
            }
        }

        [Test]
        public void OfficialMenuAndBuildSettingsPointAtOfficialCatalog()
        {
            string guid = AssetDatabase.AssetPathToGUID(OfficialRosterBuilder.CatalogPath);
            Assert.That(File.ReadAllText(OfficialRosterBuilder.MenuScene), Does.Contain("guid: " + guid), "Menu session must reference the official catalog.");
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes[0].path, Is.EqualTo(OfficialRosterBuilder.MenuScene));
            Assert.That(scenes.Select(s => s.path), Is.EqualTo(OfficialRosterBuilder.Scenes(Official)));
            Assert.That(scenes.All(s => s.enabled), Is.True);
        }

        [TestCase(OfficialRosterBuilder.Version01Root)]
        [TestCase(OfficialRosterBuilder.Version02Root)]
        [TestCase(OfficialRosterBuilder.Version03Root)]
        [TestCase(OfficialRosterBuilder.Version04Root)]
        [TestCase(OfficialRosterBuilder.Version05Root)]
        [TestCase(OfficialRosterBuilder.Version06Root)]
        public void PreviousVersionIsKeptAndTheCurrentCatalogExtendsIt(string root)
        {
            var v1 = Load<WarSandboxBattlefieldCatalog>(root + "/Catalog.asset");
            var current = Official;
            Assert.That(OfficialRosterBuilder.Root, Is.Not.EqualTo(root));
            Assert.That(current.entries.Length, Is.GreaterThanOrEqualTo(v1.entries.Length), "Quality curation keeps identities; increasing their count is not acceptance.");
            // Every v1 battlefield is kept with identical content and in the same relative order (new ones may sit next to their kind).
            int previous = -1;
            foreach (var a in v1.entries)
            {
                int index = System.Array.FindIndex(current.entries, e => e.id == a.id);
                Assert.That(index, Is.GreaterThan(previous), a.id); previous = index;
                var b = current.entries[index];
                Assert.That(b.id, Is.EqualTo(a.id)); Assert.That(b.displayName, Is.EqualTo(a.displayName));
                Assert.That(b.scenePath, Is.EqualTo(a.scenePath)); Assert.That(b.rules, Is.SameAs(a.rules));
                Assert.That(b.description, Is.EqualTo(a.description)); Assert.That(b.briefing, Is.EqualTo(a.briefing));
            }
            // Old saved plans keep resolving: every v1 template resolves to the same config in the current catalog.
            foreach (var t in v1.templates)
            {
                Assert.That(current.TryResolveTemplate(t.templateId, t.revision, out var config, out string error), Is.True, error);
                Assert.That(config, Is.SameAs(t.config));
            }
            Assert.That(File.Exists(root + "/LaunchMenu.unity"), Is.True);
        }

        [Test]
        public void HistoricalBuildEntrypointsCannotEmitVersion07IntoOldOutputSlots()
        {
            Action[] historicalBuilds =
            {
                OfficialRosterBuilder.Build01, OfficialRosterBuilder.Build02, OfficialRosterBuilder.Build03, OfficialRosterBuilder.Build04,
                OfficialRosterBuilder.Build05, OfficialRosterBuilder.Build06, OfficialRosterBuilder.Build07, OfficialRosterBuilder.Build08, OfficialRosterBuilder.Build09
            };
            foreach (Action build in historicalBuilds)
            {
                var error = Assert.Throws<InvalidOperationException>(() => build());
                Assert.That(error.Message, Does.Contain("Build10"));
            }
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }
    }
}

