using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    /// <summary>
    /// New-unit batch 5 (Troops5Builder): pinned CC0 sources, accepted pipeline output, regular-roster safety (radius, own move
    /// speed), the ranged profile of the cactoro, the batch-4 policy extended (not edited), the three battlefields and their place in
    /// the official catalog.
    /// </summary>
    public sealed class Troops5CatalogTests
    {
        private const string Source = "Assets/CharacterPilotSource/QuaterniusTroops5/";
        private const string Legion = Troops5Builder.KnightLegion;

        [TestCase("Cactoro.fbx", "109561ce7e6db75f5f5238215fed0fbfceb171151db6dae5d02997523b8a95ae")]
        [TestCase("Frog.fbx", "77c8cb364de1f63605fb5411c70237d02aa416a6ae118f2db3c7e88c7e5be897")]
        [TestCase("Monkroose.fbx", "00c225a500b15bd360cd903bcc0a0f84439395eed7e84bfe413e058f4125af4c")]
        public void SourcesArePinnedOfficialCc0Files(string file, string sha256)
        {
            using (var sha = SHA256.Create())
                Assert.That(string.Concat(sha.ComputeHash(File.ReadAllBytes(Source + file)).Select(b => b.ToString("x2"))), Is.EqualTo(sha256));
            Assert.That(File.ReadAllText(Source + "License.txt"), Does.Contain("CC0 1.0 Universal"));
        }

        [Test]
        public void TroopsArePipelineAcceptedRegularUnitsWithTheirOwnSpeed()
        {
            var knight = Load<UnitTypeConfig>(Legion);
            for (int i = 0; i < Troops5Builder.Keys.Length; i++)
            {
                var report = JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Troops5Builder.Output(i) + "/PipelineReport.json"));
                Assert.That(report.automatedPassed, Is.True, Troops5Builder.Keys[i]);
                var u = Load<UnitTypeConfig>(Troops5Builder.LibraryUnit(i));
                Assert.That(u.unitTypeName, Is.EqualTo(Troops5Builder.Titles[i]));
                Assert.That(u.teamId, Is.EqualTo(0));
                Assert.That(u.flockingConfig.agentRadius, Is.EqualTo(Troops5Builder.Radii[i]).Within(1e-4f));
                Assert.That(u.flockingConfig.agentRadius, Is.LessThanOrEqualTo(0.8f), "Regular roster radius.");
                if (Troops5Builder.Ranged[i])
                {
                    var c = u.combatConfig;
                    Assert.That(c.projectileRange, Is.EqualTo(Troops5Builder.SpineRange).Within(1e-4f), "Ranged.");
                    Assert.That(c.targetAcquireRadius, Is.GreaterThan(c.projectileRange));
                    Assert.That(c.projectileGravity, Is.EqualTo(0f), "Straight spine shots (unguided arcs miss charging knights).");
                    Assert.That(c.projectileSplashRadius, Is.EqualTo(0f), "Single target, no splash.");
                    Assert.That(c.projectileOriginHeight, Is.GreaterThan(1f).And.LessThan(3f), "Launched just above the head.");
                }
                else
                {
                    Assert.That(u.combatConfig.projectileRange, Is.EqualTo(0f), "Melee.");
                    Assert.That(u.combatConfig.attackRange, Is.GreaterThan(u.flockingConfig.agentRadius + knight.flockingConfig.agentRadius), "Reach must cover both bodies.");
                }
                Assert.That(u.combatConfig.maxHp, Is.EqualTo(Troops5Builder.Hp[i]));
                Assert.That(u.combatConfig.attackDamage, Is.EqualTo(Troops5Builder.Damage[i]));
                Assert.That(u.movementConfig.maxSpeed, Is.EqualTo(Troops5Builder.Speed[i]).Within(1e-4f), "No blanket maxSpeed override.");
                Assert.That(u.spawnConfig.unitCount, Is.EqualTo(Troops5Builder.Counts[i]));
                Assert.That(ConfigValidator.Validate(u).IsValid, Is.True, Troops5Builder.Keys[i]);
            }
            Assert.That(Troops5Builder.Ranged, Is.EqualTo(new[] { true, false, false }), "Only the cactoro is ranged.");
            Assert.That(knight.teamId, Is.EqualTo(1));
            Assert.That(knight.spawnConfig.unitCount, Is.EqualTo(Troops5Builder.KnightCount));
        }

        [Test]
        public void RegularPolicyIsExtendedNotEdited()
        {
            var old = Load<WarSandboxRosterPolicy>(Troops5Builder.RegularPolicyPath);
            var policy = Load<WarSandboxRosterPolicy>(Troops5Builder.Integrated + "/RegularPolicy.asset");
            Assert.That(old.templates.Length, Is.EqualTo(16), "Batch-4 regular policy stays as it is.");
            Assert.That(old.templates, Does.Contain(Load<UnitTypeConfig>(Legion)), "The knight legion comes from batch 4.");
            Assert.That(policy.templates.Take(old.templates.Length), Is.EqualTo(old.templates));
            var added = Enumerable.Range(0, Troops5Builder.Keys.Length).Select(i => Load<UnitTypeConfig>(Troops5Builder.LibraryUnit(i)));
            Assert.That(policy.templates.Skip(old.templates.Length), Is.EqualTo(added));
            Assert.That(policy.maximumUnits, Is.EqualTo(old.maximumUnits));
            Assert.That(policy.maximumRadius, Is.EqualTo(old.maximumRadius));
            Assert.That(policy.TryValidateDefinition(out string error), Is.True, error);
        }

        [Test]
        public void BattlefieldsUseTheNewPolicyAndTheirDeploymentValidates()
        {
            string policyGuid = AssetDatabase.AssetPathToGUID(Troops5Builder.Integrated + "/RegularPolicy.asset");
            var policy = Load<WarSandboxRosterPolicy>(Troops5Builder.Integrated + "/RegularPolicy.asset");
            var simulation = Load<SimulationConfig>("Assets/Game/UnifiedRoster/Version03/Scenes/RegularSimulation.asset");
            for (int i = 0; i < Troops5Builder.Keys.Length; i++)
            {
                string scene = Troops5Builder.Battlefield(i), scenarioPath = Troops5Builder.Integrated + "/" + Troops5Builder.Keys[i] + "Scenario.asset";
                string text = File.ReadAllText(scene);
                Assert.That(text, Does.Contain("rosterPolicy: {fileID: 11400000, guid: " + policyGuid));
                Assert.That(text, Does.Contain("scenarioConfig: {fileID: 11400000, guid: " + AssetDatabase.AssetPathToGUID(scenarioPath)));
                var scenario = Load<ScenarioConfig>(scenarioPath);
                Assert.That(scenario.unitTypes, Is.EqualTo(new[] { Load<UnitTypeConfig>(Troops5Builder.LibraryUnit(i)), Load<UnitTypeConfig>(Legion) }));
                // The opponent is a policy template, so editing the shipped deployment is accepted as is.
                Assert.That(WarSandboxDeploymentDraft.TryCapture(scenario, out var draft, out string error), Is.True, error);
                Assert.That(policy.TryValidate(draft, simulation, out error), Is.True, error);
            }
        }

        [Test]
        public void CollectionExtendsBatch4AndOfficialCatalogGroupsTheTroopsAsNewUnits()
        {
            var baseCatalog = Load<WarSandboxBattlefieldCatalog>(Troops5Builder.BaseCatalog);
            var catalog = Load<WarSandboxBattlefieldCatalog>(Troops5Builder.Integrated + "/Catalog.asset");
            Assert.That(catalog.TryValidate(p => File.Exists(p), out string error), Is.True, error);
            Assert.That(catalog.TryValidateTemplates(out error), Is.True, error);
            Assert.That(catalog.entries.Take(baseCatalog.entries.Length).Select(e => e.id), Is.EqualTo(baseCatalog.entries.Select(e => e.id)));
            Assert.That(catalog.entries.Skip(baseCatalog.entries.Length).Select(e => e.id), Is.EqualTo(Enumerable.Range(0, 3).Select(Troops5Builder.EntryId)));
            var official = Load<WarSandboxBattlefieldCatalog>(OfficialRosterBuilder.CatalogPath);
            for (int i = 0; i < Troops5Builder.Keys.Length; i++)
            {
                Assert.That(catalog.TryResolveTemplate(Troops5Builder.TemplateId(i), 1, out var config, out error), Is.True, error);
                Assert.That(config, Is.SameAs(Load<UnitTypeConfig>(Troops5Builder.LibraryUnit(i))));
                var entry = official.entries.Single(e => e.id == Troops5Builder.EntryId(i));
                Assert.That(entry.scenePath, Is.EqualTo(Troops5Builder.Battlefield(i)));
                Assert.That(entry.preview, Is.Not.Null, entry.id);
                Assert.That(WarSandboxFrontEnd.CatalogGroup(entry.id), Is.EqualTo("新兵种"));
            }
            Assert.That(official.TryResolveTemplate(Troops4Builder.KnightLegionTemplate, 1, out var legion, out error), Is.True, error);
            Assert.That(legion, Is.SameAs(Load<UnitTypeConfig>(Legion)));
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }
    }
}
