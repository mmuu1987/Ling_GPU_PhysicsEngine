using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class RosterChoiceDedupTests
    {
        private static WarSandboxBattlefieldCatalog Catalog => AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset");
        [Test] public void LibraryShows33RepresentativesButKeepsAll68Identities()
        {
            var catalog = Catalog; string before = EditorJsonUtility.ToJson(catalog);
            var list = WarSandboxRosterChoices.Library(catalog);
            Assert.That(list.Count, Is.EqualTo(33)); Assert.That(catalog.templates.Length, Is.EqualTo(68));
            foreach (string id in new[] { "roster-male", "roster-female", "roster-knight", "roster-skeleton-warrior", "roster-ranger", "roster-robot-expressive" })
                Assert.That(list.Any(x => x.templateId == id), Is.True, id);
            Assert.That(list.Any(x => x.hiddenFromSelection), Is.False);
            for (int i = 0; i < list.Count; i++) for (int j = 0; j < i; j++)
                Assert.That(WarSandboxRosterChoices.SameAppearance(list[i].config, list[j].config), Is.False);
            foreach (var entry in catalog.templates)
            {
                Assert.That(catalog.TryResolveTemplate(entry.templateId, entry.revision, out var unit, out var error), Is.True, error);
                Assert.That(unit, Is.SameAs(entry.config), entry.templateId);
            }
            Assert.That(EditorJsonUtility.ToJson(catalog), Is.EqualTo(before));
        }
        [Test] public void SameVisualStillKeepsMeleeAndRangedInLocalChooser()
        {
            var c = Catalog; var source = c.templates.Where(t => t.templateId.StartsWith("launch-mountain-unit-")).Select(t => t.config).ToArray();
            Assert.That(source.Length, Is.EqualTo(6));
            var choices = WarSandboxRosterChoices.Local(c, source);
            Assert.That(choices.Count, Is.EqualTo(4));
            Assert.That(choices.Count(WarSandboxUnitStats.IsRanged), Is.EqualTo(2));
            Assert.That(source.Length, Is.EqualTo(6), "Source roster must not be rewritten");
            foreach (var unit in choices) Assert.That(source.Contains(unit), Is.True);
        }
        private static T Reference<T>(string yaml, string field) where T : UnityEngine.Object
        {
            var match = Regex.Match(yaml, @"(?m)^\s*" + field + @": \{fileID: [^,]+, guid: (\w+)");
            return match.Success ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)) : null;
        }
        [Test] public void EverySelectableBattlefieldHasLegalChoicesAndKeepsItsExistingRoster()
        {
            var c = Catalog; int checkedScenes = 0;
            foreach (var entry in c.entries.Where(e => !e.hiddenFromSelection))
            {
                string scene = File.ReadAllText(entry.scenePath);
                var source = Reference<ScenarioConfig>(scene, "scenarioConfig");
                Assert.That(source, Is.Not.Null, entry.id);
                var policy = Reference<WarSandboxRosterPolicy>(scene, "rosterPolicy");
                var all = new List<UnitTypeConfig>(source.unitTypes);
                if (policy != null) all.AddRange(policy.templates);
                var allowed = all.Where(x => x != null && c.IsSelectableTemplate(x)).Distinct().ToList();
                string before = EditorJsonUtility.ToJson(source);
                var choices = WarSandboxRosterChoices.Local(c, allowed);
                Assert.That(choices.Count, Is.GreaterThan(0), entry.id);
                foreach (var item in choices) Assert.That(allowed.Contains(item), Is.True, entry.id);
                foreach (var item in allowed)
                    Assert.That(choices.Any(x => WarSandboxRosterChoices.SameAppearance(x, item) && WarSandboxUnitStats.IsRanged(x) == WarSandboxUnitStats.IsRanged(item)), Is.True, entry.id);
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before)); checkedScenes++;
                Debug.Log("ROSTER_CHOICES " + entry.id + " " + allowed.Count + " -> " + choices.Count);
            }
            Assert.That(checkedScenes, Is.EqualTo(29));
        }
        [Test] public void DedupDoesNotReplaceLocalConfigWithAnOutOfPolicyRepresentative()
        {
            var c = Catalog; var old = c.FindTemplate("enemy-collection-knight-control").config;
            var list = WarSandboxRosterChoices.Local(c, new[] { old });
            Assert.That(list.Count, Is.EqualTo(1)); Assert.That(list[0], Is.SameAs(old));
            Assert.That(list[0], Is.Not.SameAs(c.FindTemplate("roster-knight").config));
        }
    }
}
