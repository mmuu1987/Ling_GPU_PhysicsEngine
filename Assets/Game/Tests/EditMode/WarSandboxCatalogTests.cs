using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxCatalogTests
    {
        private WarSandboxBattlefieldCatalog catalog;
        private WarSandboxBattlefieldConfig rules;

        [SetUp]
        public void SetUp()
        {
            rules = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
            catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
            catalog.defaultEntryId = "a";
            catalog.entries = new[] { Entry("a"), Entry("b") };
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(catalog); Object.DestroyImmediate(rules); }

        [Test]
        public void MultipleRulesCanShareASceneWithoutSharingIdentity()
        {
            string before = JsonUtility.ToJson(catalog);
            Assert.That(catalog.TryResolve("b", _ => true, out var entry, out var error), Is.True, error);
            Assert.That(entry.id, Is.EqualTo("b"));
            Assert.That(JsonUtility.ToJson(catalog), Is.EqualTo(before));
            Assert.That(catalog.TryResolve("missing", _ => true, out _, out error), Is.False);
            Assert.That(error, Does.Contain("Unknown battlefield"));
        }

        [TestCase("empty", "empty")]
        [TestCase("null-entry", "missing")]
        [TestCase("duplicate", "Duplicate")]
        [TestCase("missing-default", "defaultEntryId")]
        [TestCase("empty-id", "id")]
        [TestCase("spaced-id", "id")]
        [TestCase("name", "displayName")]
        [TestCase("rules", "rules")]
        [TestCase("bad-rules", "controlPointRadius")]
        [TestCase("path", "scenePath")]
        public void InvalidCatalogReportsItsFieldWithoutMutatingAnything(string kind, string expected)
        {
            switch (kind)
            {
                case "empty": catalog.entries = null; break;
                case "null-entry": catalog.entries[0] = null; break;
                case "duplicate": catalog.entries[1].id = "a"; break;
                case "missing-default": catalog.defaultEntryId = "none"; break;
                case "empty-id": catalog.entries[0].id = ""; break;
                case "spaced-id": catalog.entries[0].id = " a"; break;
                case "name": catalog.entries[0].displayName = ""; break;
                case "rules": catalog.entries[0].rules = null; break;
                case "bad-rules": rules.rules.controlPointRadius = float.NaN; break;
                case "path": catalog.entries[0].scenePath = "WarSandbox"; break;
            }
            // Unity's inline-class serializer materializes null entries as default objects.
            string before = kind == "null-entry" ? null : JsonUtility.ToJson(catalog);
            Assert.That(catalog.TryValidate(_ => true, out string error), Is.False);
            Assert.That(error, Does.Contain(expected));
            if (before == null) Assert.That(catalog.entries[0], Is.Null);
            else Assert.That(JsonUtility.ToJson(catalog), Is.EqualTo(before));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Assets/../Bad.unity")]
        [TestCase("Assets//Bad.unity")]
        [TestCase("Assets\\Bad.unity")]
        [TestCase("Assets/Bad.prefab")]
        [TestCase("Packages/Bad.unity")]
        public void InvalidScenePathsAreNotLoadable(string path)
        {
            Assert.That(WarSandboxBattlefieldEntry.IsScenePath(path), Is.False);
            Assert.That(WarSandboxSceneSession.CanLoadScene(path), Is.False);
        }

        [Test]
        public void SceneMissingFromBuildIsRejectedWithItsFullPath()
        {
            Assert.That(catalog.TryValidate(_ => false, out var error), Is.False);
            Assert.That(error, Does.Contain(catalog.entries[0].scenePath));
        }

        [Test]
        public void ShippingCatalogAndEntrySceneAreIncludedAndHaveUniqueIds()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(WarSandboxEntryBuilder.CatalogPath);
            Assert.That(shipped, Is.Not.Null);
            Assert.That(shipped.TryValidate(WarSandboxEntryBuilder.IsIncludedScene, out var error), Is.True, error);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(WarSandboxEntryBuilder.MenuScenePath));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
        }

        [TestCase(560, 800)]
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void NavigationStaysInsideTheViewport(float width, float height)
        {
            Rect rect = WarSandboxFrontEnd.NavigationRect(width, height);
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(rect.xMax, Is.LessThanOrEqualTo(width));
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0)); Assert.That(rect.yMax, Is.LessThanOrEqualTo(height));
        }

        private WarSandboxBattlefieldEntry Entry(string id) => new WarSandboxBattlefieldEntry
        { id = id, displayName = "Battle " + id, scenePath = "Assets/Game/Scenes/WarSandbox.unity", rules = rules };
    }
}
