using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class PlatformerBatch6CatalogTests
    {
        [TestCase("Crab.fbx", "edc99b2dc232e2b96a571b9d182fa8de38e269efa18d551efcfc86f4ff0ae49b")]
        [TestCase("Enemy.fbx", "ac2d8fb64dedde1c21e674096fe905a3ceaa7c39b25cc70326cc79beecad7867")]
        [TestCase("Skull.fbx", "05007750febc24323f400b7e7cdada6d124b6646831e07884323cc10cd1f6daa")]
        public void SourcesArePinnedAuthorOfficialArchiveFiles(string file, string expected)
        {
            using (var sha = SHA256.Create())
                Assert.That(string.Concat(sha.ComputeHash(File.ReadAllBytes(PlatformerBatch6Builder.SourceDirectory + "/" + file)).Select(b => b.ToString("x2"))), Is.EqualTo(expected));
            Assert.That(File.ReadAllText(PlatformerBatch6Builder.SourceDirectory + "/License.txt"), Does.Contain("CC0 1.0 Universal"));
            Assert.That(File.ReadAllText(PlatformerBatch6Builder.SourceDirectory + "/SourceProvenance.json"), Does.Contain("https://quaternius.itch.io/ultimate-platformer-pack"));
        }

        [Test]
        public void SharedSourceGateAcceptsThePinnedPlatformerAssets()
        {
            Assert.DoesNotThrow(() => PlatformerBatch6Builder.ValidateSources());
        }

        [Test]
        public void AllThreeModelsHaveRealPipelineEvidenceWithinUnchangedGates()
        {
            for (int i = 0; i < 3; i++)
            {
                var r = JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(PlatformerBatch6Builder.Output(i) + "/PipelineReport.json"));
                Assert.That(r.automatedPassed, Is.True, PlatformerBatch6Builder.Keys[i]);
                Assert.That(r.allowedPositionError, Is.EqualTo(.0025f).Within(1e-7f));
                Assert.That(r.maxPositionError, Is.LessThanOrEqualTo(.0025f));
                Assert.That(r.fullVertices, Is.GreaterThan(r.lowVertices));
                Assert.That(r.lowVertices, Is.LessThanOrEqualTo(PlatformerBatch6Builder.LowBudgets[i]));
                Assert.That(r.textureBytes, Is.LessThanOrEqualTo(32L * 1024 * 1024));
                Assert.That(r.positionComparisons, Is.GreaterThan(0));
                Assert.That(r.gpuSnapshots, Is.EqualTo(48));
                Assert.That(r.sourceReferenceSnapshots, Is.EqualTo(16));
                Assert.That(r.actions.Select(a => a.action), Is.EqualTo(new[] { "Idle", "Move", "Attack", "Death" }));
                foreach (var a in r.actions) Assert.That(a.maximumPositionError, Is.LessThanOrEqualTo(.0025f));
            }
        }

        [Test]
        public void PrivateLibraryUnitsKeepTheirOwnProfilesAndPaletteMaterials()
        {
            for (int i = 0; i < 3; i++)
            {
                var generated = Load<UnitTypeConfig>(PlatformerBatch6Builder.Output(i) + "/Unit.asset");
                var u = Load<UnitTypeConfig>(PlatformerBatch6Builder.LibraryUnit(i));
                Assert.That(u, Is.Not.SameAs(generated));
                Assert.That(u.combatConfig, Is.Not.SameAs(generated.combatConfig));
                Assert.That(u.teamId, Is.EqualTo(0));
                Assert.That(u.unitTypeName, Is.EqualTo(PlatformerBatch6Builder.Titles[i]));
                Assert.That(u.combatConfig.projectileRange, Is.EqualTo(0));
                Assert.That(u.combatConfig.attackRange, Is.GreaterThan(u.flockingConfig.agentRadius + .55f));
                Assert.That(u.combatConfig.maxHp, Is.EqualTo(PlatformerBatch6Builder.Hp[i]));
                Assert.That(u.combatConfig.attackDamage, Is.EqualTo(PlatformerBatch6Builder.Damage[i]));
                Assert.That(u.movementConfig.maxSpeed, Is.EqualTo(PlatformerBatch6Builder.Speed[i]).Within(1e-5f));
                Assert.That(u.spawnConfig.unitCount, Is.EqualTo(PlatformerBatch6Builder.Counts[i]));
                Assert.That(u.flockingConfig.agentRadius, Is.EqualTo(PlatformerBatch6Builder.Radii[i]).Within(1e-5f));
                Assert.That(ConfigValidator.Validate(u).IsValid, Is.True);
                var atlas = u.renderConfig.nearMaterial.GetTexture("_BaseMap");
                Assert.That(AssetDatabase.GetAssetPath(atlas), Does.StartWith(PlatformerBatch6Builder.Prepared + "/" + PlatformerBatch6Builder.Names[i] + "/"));
                Assert.That(atlas.width, Is.EqualTo(64));
                Assert.That(atlas.height, Is.EqualTo(64));
            }
        }

        [Test]
        public void RegularPolicyExtendsTheCompletedBatch5WithoutReplacingIt()
        {
            var old = Load<WarSandboxRosterPolicy>(PlatformerBatch6Builder.RegularPolicyPath);
            var policy = Load<WarSandboxRosterPolicy>(PlatformerBatch6Builder.Integrated + "/RegularPolicy.asset");
            Assert.That(old.templates.Length, Is.EqualTo(19));
            Assert.That(policy.templates.Length, Is.EqualTo(22));
            Assert.That(policy.templates.Take(old.templates.Length), Is.EqualTo(old.templates));
            Assert.That(policy.maximumRadius, Is.EqualTo(old.maximumRadius));
            Assert.That(policy.maximumUnits, Is.EqualTo(old.maximumUnits));
            Assert.That(policy.TryValidateDefinition(out string error), Is.True, error);
            Assert.That(old.templates, Does.Contain(Load<UnitTypeConfig>(PlatformerBatch6Builder.KnightLegion)));
        }

        [Test]
        public void NewCollectionAddsExactlyThreeEntriesAndKeepsOldTemplateIdentities()
        {
            var old = Load<WarSandboxBattlefieldCatalog>(PlatformerBatch6Builder.BaseCatalog);
            var current = Load<WarSandboxBattlefieldCatalog>(PlatformerBatch6Builder.Integrated + "/Catalog.asset");
            Assert.That(current.entries.Length, Is.EqualTo(old.entries.Length + 3));
            Assert.That(current.entries.Take(old.entries.Length).Select(e => e.id), Is.EqualTo(old.entries.Select(e => e.id)));
            Assert.That(current.entries.Skip(old.entries.Length).Select(e => e.id), Is.EqualTo(Enumerable.Range(0, 3).Select(PlatformerBatch6Builder.EntryId)));
            Assert.That(current.TryValidate(p => File.Exists(p), out string error), Is.True, error);
            Assert.That(current.TryValidateTemplates(out error), Is.True, error);
            foreach (var t in old.templates)
            {
                Assert.That(current.TryResolveTemplate(t.templateId, t.revision, out var config, out error), Is.True, error);
                Assert.That(config, Is.SameAs(t.config));
            }
        }

        [Test]
        public void OwnedScenesHaveTheNewScenarioPolicyAndValidInitialDeployments()
        {
            var policyPath = PlatformerBatch6Builder.Integrated + "/RegularPolicy.asset";
            var policy = Load<WarSandboxRosterPolicy>(policyPath);
            var simulation = Load<SimulationConfig>("Assets/Game/Content/Characters/UnifiedRoster/Version03/Scenes/RegularSimulation.asset");
            for (int i = 0; i < 3; i++)
            {
                var scenarioPath = PlatformerBatch6Builder.Integrated + "/" + PlatformerBatch6Builder.Keys[i] + "Scenario.asset";
                var text = File.ReadAllText(PlatformerBatch6Builder.Battlefield(i));
                Assert.That(text, Does.Contain("rosterPolicy: {fileID: 11400000, guid: " + AssetDatabase.AssetPathToGUID(policyPath)));
                Assert.That(text, Does.Contain("scenarioConfig: {fileID: 11400000, guid: " + AssetDatabase.AssetPathToGUID(scenarioPath)));
                var scenario = Load<ScenarioConfig>(scenarioPath);
                Assert.That(scenario.unitTypes, Is.EqualTo(new[] { Load<UnitTypeConfig>(PlatformerBatch6Builder.LibraryUnit(i)), Load<UnitTypeConfig>(PlatformerBatch6Builder.KnightLegion) }));
                Assert.That(WarSandboxDeploymentDraft.TryCapture(scenario, out var draft, out string error), Is.True, error);
                Assert.That(policy.TryValidate(draft, simulation, out error), Is.True, error);
            }
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(value, Is.Not.Null, path);
            return value;
        }
    }
}
