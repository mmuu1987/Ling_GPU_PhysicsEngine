using System.Linq;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxLaunchPresetTests
    {
        private const string Root = WarSandboxLaunchPresetsBuilder.Root;

        [TestCase("launch-open", 512, 2, false, WarSandboxGameMode.Annihilation)]
        [TestCase("launch-mountain", 384, 3, true, WarSandboxGameMode.Annihilation)]
        [TestCase("launch-point", 512, 2, false, WarSandboxGameMode.ControlPoint)]
        [TestCase("launch-three", 768, 3, false, WarSandboxGameMode.Annihilation)]
        [TestCase("launch-standard", 100000, 2, false, WarSandboxGameMode.Annihilation)]
        public void AuthoredPresetHasPlayableDeploymentAndResolvablePlanTemplates(string id, int population,
            int armies, bool terrain, WarSandboxGameMode mode)
        {
            var catalog = Load<WarSandboxBattlefieldCatalog>(WarSandboxLaunchPresetsBuilder.CatalogPath);
            Assert.That(catalog.TryResolve(id, p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null,
                out var entry, out string error), Is.True, error);
            Assert.That(entry.description, Is.Not.Null.And.Not.Empty);
            Assert.That(entry.briefing, Is.Not.Null.And.Not.Empty);
            Assert.That(entry.terrainSurface != null, Is.EqualTo(terrain));
            Assert.That(entry.rules.rules.gameMode, Is.EqualTo(mode));
            var scenario = Load<ScenarioConfig>(Root + "/" + id + "/Scenario.asset");
            var simulation = Load<SimulationConfig>("Assets/Game/Settings/SimulationConfig.asset");
            Assert.That(scenario.unitTypes.Sum(u => u.spawnConfig.unitCount), Is.EqualTo(population));
            Assert.That(scenario.unitTypes.Select(u => u.teamId).Distinct().Count(), Is.EqualTo(armies));
            Assert.That(WarSandboxDeploymentDraft.TryCapture(scenario, out var draft, out error), Is.True, error);
            Assert.That(draft.TryValidate(simulation.simulationWorldSize - Vector2.one * simulation.boundaryPadding * 2,
                entry.rules.rules, out error), Is.True, error);
            foreach (var unit in scenario.unitTypes)
            {
                Assert.That(catalog.TryGetTemplateId(unit, out string templateId, out int revision), Is.True);
                Assert.That(catalog.TryResolveTemplate(templateId, revision, out var resolved, out error), Is.True, error);
                Assert.That(resolved, Is.SameAs(unit));
                Assert.That(AssetDatabase.GetAssetPath(unit), Does.StartWith(Root + "/" + id + "/"));
                Assert.That(AssetDatabase.GetAssetPath(unit.spawnConfig), Does.StartWith(Root + "/" + id + "/"));
            }
        }

        [Test]
        public void QuickDefaultAndStandardOptionPreserveOriginal110kContent()
        {
            var catalog = Load<WarSandboxBattlefieldCatalog>(WarSandboxLaunchPresetsBuilder.CatalogPath);
            Assert.That(catalog.defaultEntryId, Is.EqualTo("launch-open"));
            Assert.That(catalog.entries.Length, Is.EqualTo(5));
            Assert.That(catalog.TryValidateTemplates(out string error), Is.True, error);
            var original = Load<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            var standard = Load<ScenarioConfig>(Root + "/launch-standard/Scenario.asset");
            Assert.That(original.unitTypes.Sum(u => u.spawnConfig.unitCount), Is.EqualTo(110000));
            Assert.That(standard.unitTypes.Length, Is.EqualTo(4));
            for (int i = 0; i < standard.unitTypes.Length; i++)
            {
                var source = original.unitTypes[i]; var copy = standard.unitTypes[i];
                Assert.That(copy, Is.Not.SameAs(source));
                Assert.That(copy.spawnConfig, Is.Not.SameAs(source.spawnConfig));
                Assert.That(JsonUtility.ToJson(copy.spawnConfig), Is.EqualTo(JsonUtility.ToJson(source.spawnConfig)));
                Assert.That(copy.teamId, Is.EqualTo(source.teamId));
                Assert.That(copy.combatConfig, Is.SameAs(source.combatConfig));
                var originalRender = source.renderConfig;
                var launchRender = copy.renderConfig;
                var originalProfile = originalRender.vatProfile as VATProfile;
                var farVariant = Load<VATProfile>(WarSandboxFarLodBuilder.ProfilePath);
                Assert.That(originalProfile, Is.Not.Null);
                Assert.That(farVariant, Is.Not.Null);
                if (AssetDatabase.GetAssetPath(originalProfile) == "Assets/Art/VAT/Characters/MaleCharacter_Stage5_MultiClip_Profile.asset")
                {
                    // M7.3 deliberately owns a cheaper FAR variant, not a byte-identical RenderConfig.
                    Assert.That(launchRender, Is.Not.SameAs(originalRender));
                    StringAssert.StartsWith(WarSandboxFarLodBuilder.Root + "/", AssetDatabase.GetAssetPath(launchRender));
                    Assert.That(launchRender.vatProfile, Is.SameAs(farVariant));
                    Assert.That(launchRender.nearMesh, Is.SameAs(originalRender.nearMesh));
                    Assert.That(launchRender.midMesh, Is.SameAs(originalRender.midMesh));
                    Assert.That(launchRender.farMesh, Is.SameAs(farVariant.lowLodMesh));
                    Assert.That(launchRender.farMesh.vertexCount, Is.InRange(1, 128));
                    Assert.That(farVariant.positionTexture, Is.SameAs(originalProfile.positionTexture));
                    Assert.That(farVariant.normalTexture, Is.SameAs(originalProfile.normalTexture));
                    // The reducer promotes an original LOW tier to MID when no explicit MID existed.
                    Assert.That(farVariant.midLodMesh, Is.SameAs(originalProfile.HasMidLod ? originalProfile.midLodMesh : originalProfile.lowLodMesh));
                    Assert.That(farVariant.midLodPositionTexture, Is.SameAs(originalProfile.HasMidLod ? originalProfile.midLodPositionTexture : originalProfile.lowLodPositionTexture));
                    Assert.That(farVariant.midLodNormalTexture, Is.SameAs(originalProfile.HasMidLod ? originalProfile.midLodNormalTexture : originalProfile.lowLodNormalTexture));
                    Assert.That(farVariant.midLodTextureWidth, Is.EqualTo(originalProfile.HasMidLod ? originalProfile.midLodTextureWidth : originalProfile.lowLodTextureWidth));
                    Assert.That(farVariant.midLodTextureHeight, Is.EqualTo(originalProfile.HasMidLod ? originalProfile.midLodTextureHeight : originalProfile.lowLodTextureHeight));
                    Assert.That(farVariant.midLodRowsPerFrame, Is.EqualTo(originalProfile.HasMidLod ? originalProfile.midLodRowsPerFrame : originalProfile.lowLodRowsPerFrame));
                    Assert.That(farVariant.totalFrameCount, Is.EqualTo(originalProfile.totalFrameCount));
                    Assert.That(launchRender.nearMaterial, Is.SameAs(originalRender.nearMaterial));
                    Assert.That(launchRender.midMaterial, Is.SameAs(originalRender.midMaterial));
                    Assert.That(launchRender.farMaterial, Is.SameAs(originalRender.farMaterial));
                    Assert.That(launchRender.nearShadowCasting, Is.EqualTo(originalRender.nearShadowCasting));
                    Assert.That(launchRender.nearReceiveShadows, Is.EqualTo(originalRender.nearReceiveShadows));
                    Assert.That(launchRender.midShadowCasting, Is.EqualTo(originalRender.midShadowCasting));
                    Assert.That(launchRender.midReceiveShadows, Is.EqualTo(originalRender.midReceiveShadows));
                    Assert.That(launchRender.farShadowCasting, Is.EqualTo(originalRender.farShadowCasting));
                    Assert.That(launchRender.farReceiveShadows, Is.EqualTo(originalRender.farReceiveShadows));
                }
                else Assert.That(launchRender, Is.SameAs(originalRender));
            }
            var units = catalog.entries.SelectMany(e => Load<ScenarioConfig>(Root + "/" + e.id + "/Scenario.asset").unitTypes).ToArray();
            Assert.That(units.Distinct().Count(), Is.EqualTo(units.Length), "Presets must own their unit configuration.");
            Assert.That(units.Select(u => u.spawnConfig).Distinct().Count(), Is.EqualTo(units.Length), "Presets must own their spawns.");
            Assert.That(catalog.templates.Length, Is.EqualTo(units.Length));
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path); return asset;
        }
    }
}
