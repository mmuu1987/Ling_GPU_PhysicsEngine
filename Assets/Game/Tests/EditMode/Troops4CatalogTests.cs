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
    /// New-unit batch 4 (Troops4Builder): pinned CC0 sources, accepted pipeline output, regular-roster safety (radius, own move
    /// speed), the extended regular policy, the three battlefields and their place in the official catalog.
    /// </summary>
    public sealed class Troops4CatalogTests
    {
        private const string Source = "Assets/Art/Source/CharacterPilot/QuaterniusTroops4/";
        private const string Legion = Troops4Builder.Integrated + "/Library/" + Troops4Builder.KnightLegionKey + ".asset";

        [TestCase("Orc_Skull.fbx", "9724b8659bda67fb8664af54467014c19517f3773e5224f1d09ca5da880d44dc")]
        [TestCase("Ninja.fbx", "9038cca159b4356044d08c03f3a7122cb8660ffb84c5d8813cc4d77460ba83da")]
        [TestCase("Tribal.fbx", "40a563ab109ca0fc59969c3a226352fc1eb0b00e7204795e714992b30a5ed2ac")]
        public void SourcesArePinnedOfficialCc0Files(string file, string sha256)
        {
            using (var sha = SHA256.Create())
                Assert.That(string.Concat(sha.ComputeHash(File.ReadAllBytes(Source + file)).Select(b => b.ToString("x2"))), Is.EqualTo(sha256));
            Assert.That(File.ReadAllText(Source + "License.txt"), Does.Contain("CC0 1.0 Universal"));
        }

        [Test]
        public void TroopsArePipelineAcceptedRegularMeleeUnitsWithTheirOwnSpeed()
        {
            var knight = Load<UnitTypeConfig>(Legion);
            for (int i = 0; i < Troops4Builder.Keys.Length; i++)
            {
                var report = JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(Troops4Builder.Output(i) + "/PipelineReport.json"));
                Assert.That(report.automatedPassed, Is.True, Troops4Builder.Keys[i]);
                var u = Load<UnitTypeConfig>(Troops4Builder.LibraryUnit(i));
                Assert.That(u.unitTypeName, Is.EqualTo(Troops4Builder.Titles[i]));
                Assert.That(u.teamId, Is.EqualTo(0));
                Assert.That(u.flockingConfig.agentRadius, Is.EqualTo(Troops4Builder.Radii[i]).Within(1e-4f));
                Assert.That(u.flockingConfig.agentRadius, Is.LessThanOrEqualTo(0.8f), "Regular roster radius.");
                Assert.That(u.combatConfig.projectileRange, Is.EqualTo(0f), "Melee.");
                Assert.That(u.combatConfig.attackRange, Is.GreaterThan(u.flockingConfig.agentRadius + knight.flockingConfig.agentRadius), "Reach must cover both bodies.");
                Assert.That(u.combatConfig.maxHp, Is.EqualTo(Troops4Builder.Hp[i]));
                Assert.That(u.combatConfig.attackDamage, Is.EqualTo(Troops4Builder.Damage[i]));
                Assert.That(u.movementConfig.maxSpeed, Is.EqualTo(Troops4Builder.Speed[i]).Within(1e-4f), "No blanket maxSpeed override.");
                Assert.That(u.spawnConfig.unitCount, Is.EqualTo(Troops4Builder.Counts[i]));
                Assert.That(ConfigValidator.Validate(u).IsValid, Is.True, Troops4Builder.Keys[i]);
            }
            int ninja = System.Array.IndexOf(Troops4Builder.Keys, "ninja");
            Assert.That(Load<UnitTypeConfig>(Troops4Builder.LibraryUnit(ninja)).movementConfig.maxSpeed, Is.GreaterThan(knight.movementConfig.maxSpeed), "The ninja outruns the knights.");
            Assert.That(knight.teamId, Is.EqualTo(1));
            Assert.That(knight.spawnConfig.unitCount, Is.EqualTo(Troops4Builder.KnightCount));
        }

        [Test]
        public void RegularPolicyIsExtendedNotEdited()
        {
            var old = Load<WarSandboxRosterPolicy>(Troops4Builder.RegularPolicyPath);
            var policy = Load<WarSandboxRosterPolicy>(Troops4Builder.Integrated + "/RegularPolicy.asset");
            Assert.That(old.templates.Length, Is.EqualTo(12), "Version03 regular policy stays as it is.");
            Assert.That(policy.templates.Take(old.templates.Length), Is.EqualTo(old.templates));
            var added = Enumerable.Range(0, Troops4Builder.Keys.Length).Select(i => Load<UnitTypeConfig>(Troops4Builder.LibraryUnit(i))).Concat(new[] { Load<UnitTypeConfig>(Legion) });
            Assert.That(policy.templates.Skip(old.templates.Length), Is.EqualTo(added));
            Assert.That(policy.maximumUnits, Is.EqualTo(old.maximumUnits));
            Assert.That(policy.maximumRadius, Is.EqualTo(old.maximumRadius));
            Assert.That(policy.TryValidateDefinition(out string error), Is.True, error);
        }

        [Test]
        public void BattlefieldsUseTheNewPolicyAndTheirDeploymentValidates()
        {
            string policyGuid = AssetDatabase.AssetPathToGUID(Troops4Builder.Integrated + "/RegularPolicy.asset");
            var policy = Load<WarSandboxRosterPolicy>(Troops4Builder.Integrated + "/RegularPolicy.asset");
            var simulation = Load<SimulationConfig>("Assets/Game/Content/Characters/UnifiedRoster/Version03/Scenes/RegularSimulation.asset");
            for (int i = 0; i < Troops4Builder.Keys.Length; i++)
            {
                string scene = Troops4Builder.Battlefield(i), scenarioPath = Troops4Builder.Integrated + "/" + Troops4Builder.Keys[i] + "Scenario.asset";
                string text = File.ReadAllText(scene);
                Assert.That(text, Does.Contain("rosterPolicy: {fileID: 11400000, guid: " + policyGuid));
                Assert.That(text, Does.Contain("scenarioConfig: {fileID: 11400000, guid: " + AssetDatabase.AssetPathToGUID(scenarioPath)));
                var scenario = Load<ScenarioConfig>(scenarioPath);
                Assert.That(scenario.unitTypes, Is.EqualTo(new[] { Load<UnitTypeConfig>(Troops4Builder.LibraryUnit(i)), Load<UnitTypeConfig>(Legion) }));
                // The opponent is a policy template, so editing the shipped deployment is accepted as is.
                Assert.That(WarSandboxDeploymentDraft.TryCapture(scenario, out var draft, out string error), Is.True, error);
                Assert.That(policy.TryValidate(draft, simulation, out error), Is.True, error);
            }
        }

        [Test]
        public void CollectionExtendsGiants3AndOfficialV4GroupsTheTroopsAsNewUnits()
        {
            var baseCatalog = Load<WarSandboxBattlefieldCatalog>(Troops4Builder.BaseCatalog);
            var catalog = Load<WarSandboxBattlefieldCatalog>(Troops4Builder.Integrated + "/Catalog.asset");
            Assert.That(catalog.TryValidate(p => File.Exists(p), out string error), Is.True, error);
            Assert.That(catalog.TryValidateTemplates(out error), Is.True, error);
            Assert.That(catalog.entries.Take(baseCatalog.entries.Length).Select(e => e.id), Is.EqualTo(baseCatalog.entries.Select(e => e.id)));
            Assert.That(catalog.entries.Skip(baseCatalog.entries.Length).Select(e => e.id), Is.EqualTo(Enumerable.Range(0, 3).Select(Troops4Builder.EntryId)));
            var official = Load<WarSandboxBattlefieldCatalog>(OfficialRosterBuilder.CatalogPath);
            for (int i = 0; i < Troops4Builder.Keys.Length; i++)
            {
                Assert.That(catalog.TryResolveTemplate(Troops4Builder.TemplateId(i), 1, out var config, out error), Is.True, error);
                Assert.That(config, Is.SameAs(Load<UnitTypeConfig>(Troops4Builder.LibraryUnit(i))));
                var entry = official.entries.Single(e => e.id == Troops4Builder.EntryId(i));
                Assert.That(entry.scenePath, Is.EqualTo(Troops4Builder.Battlefield(i)));
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
