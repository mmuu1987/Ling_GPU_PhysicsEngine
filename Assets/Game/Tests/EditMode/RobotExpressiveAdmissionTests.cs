using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MassEngine.Game.Editor;

namespace MassEngine.Game.Tests
{
    public sealed class RobotExpressiveAdmissionTests
    {
        static T Load<T>(string path)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>(path);
        static WarSandboxBattlefieldCatalog Catalog=>Load<WarSandboxBattlefieldCatalog>(RobotExpressiveAdmissionBuilder.CatalogPath);
        static WarSandboxBattlefieldCatalog Previous=>Load<WarSandboxBattlefieldCatalog>(RobotExpressiveAdmissionBuilder.Previous+"/Catalog.asset");
        [Test]public void NewOfficialCatalogAdmitsExactlyOneRobotAndOnePlayableEntry()
        {
            var c=Catalog;Assert.NotNull(c);Assert.IsTrue(c.TryValidate(File.Exists,out string error),error);Assert.IsTrue(c.TryValidateTemplates(out error),error);
            Assert.AreEqual(31,c.entries.Length);Assert.AreEqual(29,c.SelectableEntryCount);Assert.AreEqual(68,c.templates.Length);Assert.AreEqual(66,c.templates.Count(t=>!t.hiddenFromSelection));Assert.AreEqual("launch-open",c.defaultEntryId);
            CollectionAssert.AreEqual(new[]{RobotExpressiveAdmissionBuilder.TemplateId},c.templates.Select(t=>t.templateId).Except(Previous.templates.Select(t=>t.templateId)).ToArray());
            CollectionAssert.AreEqual(new[]{RobotExpressiveAdmissionBuilder.BattlefieldId},c.entries.Select(e=>e.id).Except(Previous.entries.Select(e=>e.id)).ToArray());
        }
        [Test]public void EveryHistoricalUnitIdStillResolvesToTheSameAssetAndRevision()
        {
            foreach(var old in Previous.templates){Assert.IsTrue(Catalog.TryResolveTemplate(old.templateId,old.revision,out var actual,out string error),error);Assert.AreSame(old.config,actual);var now=Catalog.FindTemplate(old.templateId);Assert.AreEqual(old.hiddenFromSelection,now.hiddenFromSelection);Assert.AreEqual(old.selectionNote,now.selectionNote);if(old.unitPreview==null)Assert.IsTrue(now.unitPreview==null,old.templateId);else Assert.AreSame(old.unitPreview,now.unitPreview);}
        }
        [Test]public void EveryHistoricalBattlefieldKeepsAuthoredPathsRulesAndRelativeOrder()
        {
            CollectionAssert.AreEqual(Previous.entries.Select(e=>e.id),Catalog.entries.Take(Previous.entries.Length).Select(e=>e.id));
            foreach(var old in Previous.entries){var now=Catalog.entries.Single(e=>e.id==old.id);Assert.AreEqual(old.scenePath,now.scenePath);Assert.AreEqual(old.contentVersion,now.contentVersion);Assert.AreSame(old.rules,now.rules);Assert.AreSame(old.preview,now.preview);Assert.AreEqual(old.description,now.description);Assert.AreEqual(old.briefing,now.briefing);CollectionAssert.AreEqual(old.featuredTemplateIds,now.featuredTemplateIds);}
        }
        [Test]public void PreviousQualityWithdrawalsRemainWithdrawnAndResolvable()
        {
            CollectionAssert.AreEquivalent(new[]{"troops6-enemy","troops6-skull"},Catalog.entries.Where(e=>e.hiddenFromSelection).Select(e=>e.id));CollectionAssert.AreEquivalent(new[]{"roster-platformer-enemy","roster-platformer-skull"},Catalog.templates.Where(t=>t.hiddenFromSelection).Select(t=>t.templateId));
            foreach(var id in OfficialRosterQualityBuilder.WithdrawnTemplates)Assert.IsTrue(Catalog.TryResolveTemplate(id,1,out _,out _));
        }
        [Test]public void FormalRobotOwnsItsConfigsAndReusesOnlyTheApprovedGpuRenderData()
        {
            Assert.IsTrue(RobotExpressiveAdmissionBuilder.ValidateFormalBinding());var robot=Catalog.FindTemplate(RobotExpressiveAdmissionBuilder.TemplateId);Assert.IsFalse(robot.hiddenFromSelection);Assert.AreEqual(1,robot.revision);Assert.AreEqual("机器人 · 近战",robot.config.unitTypeName);
            var u=robot.config;foreach(var o in new UnityEngine.Object[]{u,u.spawnConfig,u.renderConfig,u.movementConfig,u.flockingConfig,u.animationConfig,u.combatConfig})StringAssert.StartsWith(RobotExpressiveAdmissionBuilder.Root+"/Robot/",AssetDatabase.GetAssetPath(o));
            Assert.AreEqual(7214,u.renderConfig.nearMesh.vertexCount);Assert.AreEqual("MassEngine.DefaultSwordUnit",u.unitTypeClassName);
        }
        [Test]public void FormalPortraitRetainsFullBodyAndTruthfulPreviewBinding()
        {
            var robot=Catalog.FindTemplate(RobotExpressiveAdmissionBuilder.TemplateId);Assert.NotNull(robot.unitPreview);Assert.AreEqual(448,robot.unitPreview.width);Assert.AreEqual(560,robot.unitPreview.height);Assert.AreEqual(RobotExpressiveAdmissionBuilder.PortraitPath,AssetDatabase.GetAssetPath(robot.unitPreview));
            var entry=Catalog.entries.Single(e=>e.id==RobotExpressiveAdmissionBuilder.BattlefieldId);var choices=WarSandboxFrontEnd.PreviewChoices(Catalog,entry);Assert.AreEqual(RobotExpressiveAdmissionBuilder.TemplateId,choices[0].templateId);Assert.AreSame(robot.unitPreview,choices[0].unitPreview);
            Assert.IsTrue(Catalog.templates.Where(t=>!t.hiddenFromSelection).All(t=>t.unitPreview!=null));
        }
        [Test]public void FormalScenarioUsesRobotAndAnExistingKnightNotAnotherNewCharacter()
        {
            var s=Load<ScenarioConfig>(RobotExpressiveAdmissionBuilder.ScenarioPath);Assert.AreEqual(2,s.unitTypes.Length);Assert.AreSame(Catalog.FindTemplate(RobotExpressiveAdmissionBuilder.TemplateId).config,s.unitTypes[0]);Assert.AreSame(Previous.FindTemplate(RobotExpressiveAdmissionBuilder.OpponentId).config,s.unitTypes[1]);
            CollectionAssert.AreEqual(new[]{0,1},s.unitTypes.Select(u=>u.teamId));Assert.AreEqual(128,s.unitTypes.Sum(u=>u.spawnConfig.unitCount));
        }
        [Test]public void BattlefieldRosterAllowsRobotAndTheExistingRegularLibraryWithoutChangingOldPolicies()
        {
            var p=Load<WarSandboxRosterPolicy>(RobotExpressiveAdmissionBuilder.PolicyPath);Assert.IsTrue(p.TryValidateDefinition(out string error),error);Assert.IsTrue(p.templates.Contains(Catalog.FindTemplate(RobotExpressiveAdmissionBuilder.TemplateId).config));Assert.AreEqual(256,p.maximumUnits);
            foreach(var id in Previous.entries.Single(e=>e.id=="unified-regular").featuredTemplateIds)Assert.IsTrue(p.templates.Contains(Previous.FindTemplate(id).config),id);
        }
        [Test]public void HistoricalVersion08MenuAndScenesRemainAvailableButDoNotReplaceShippingEntry()
        {
            foreach (string path in RobotExpressiveAdmissionBuilder.Scenes(Catalog))
                Assert.NotNull(Load<SceneAsset>(path), path);
            Assert.IsFalse(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == RobotExpressiveAdmissionBuilder.MenuScene));
            StringAssert.Contains("guid: "+AssetDatabase.AssetPathToGUID(RobotExpressiveAdmissionBuilder.CatalogPath),File.ReadAllText(RobotExpressiveAdmissionBuilder.MenuScene));
            StringAssert.Contains("guid: "+AssetDatabase.AssetPathToGUID(RobotExpressiveAdmissionBuilder.ScenarioPath),File.ReadAllText(RobotExpressiveAdmissionBuilder.BattleScene));
        }
        [Test]public void AdmissionRecordsOnlyTheUsersRealScopeAndLeavesPilotEvidenceUntouched()
        {
            var r=JsonUtility.FromJson<RobotExpressiveAdmissionBuilder.AdmissionReport>(File.ReadAllText(RobotExpressiveAdmissionBuilder.Root+"/Admission.json"));Assert.IsTrue(r.userVisualApproval);Assert.IsTrue(r.formalAdmissionAuthorized);Assert.AreEqual(RobotExpressiveAdmissionBuilder.UserApproval,r.userMessage);Assert.IsFalse(r.manualPlaytestPerformed);Assert.IsFalse(r.balanceAccepted);Assert.IsFalse(r.performanceAccepted);Assert.IsFalse(r.rebaked);
            var pilot=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(RobotExpressiveBuilder.Output+"/PipelineReport.json"));Assert.IsFalse(pilot.humanAcceptance,"Do not retroactively rewrite earlier technical receipts");
        }
    }
}
