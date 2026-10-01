using System.IO;
using System.Linq;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    /// <summary>Official catalog v1 (OfficialRosterBuilder.Prepare01): M7.1 launch content first and unchanged, new-unit battlefields after.</summary>
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

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }
    }
}
