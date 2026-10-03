using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MassEngine.Game.Editor;

namespace MassEngine.Game.Tests
{
    public sealed class RobotExpressivePilotTests
    {
        static T Load<T>(string path)where T:UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);
        [Test] public void PinnedOriginalAndAccessorExpansionHaveVerifiedProvenance() => Assert.IsTrue(RobotExpressiveBuilder.ValidatePinnedSource());
        [Test] public void NativePrefabRetainsAllOriginalGeometryAndThreeFaceTargets()
        {
            var model=Load<GameObject>(RobotExpressiveBuilder.Native+"/RobotExpressive.prefab");Assert.NotNull(model);
            var renderers=model.GetComponentsInChildren<Renderer>(true);Assert.AreEqual(14,renderers.Length);
            var meshes=renderers.Select(r=>r is SkinnedMeshRenderer s?s.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh).ToArray();
            Assert.AreEqual(7214,meshes.Sum(m=>m.vertexCount));Assert.AreEqual(3237,meshes.Sum(m=>m.triangles.Length/3));Assert.AreEqual(3,meshes.Sum(m=>m.blendShapeCount));
        }
        [Test] public void NativePrefabPreservesFourteenAuthoredClips()
        {
            var clips=AssetDatabase.FindAssets("t:AnimationClip",new[]{RobotExpressiveBuilder.Native}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".anim")).Select(Load<AnimationClip>).ToArray();
            Assert.AreEqual(14,clips.Length);foreach(var name in new[]{"Idle","Walking","Punch","Death"})Assert.IsTrue(clips.Any(c=>c.name==name&&c.length>0));
        }
        [Test] public void SelectedNativeFaceCurvesAreIdenticallyZeroNotDeletedFromTheOriginal()
        {
            foreach(string name in new[]{"Idle","Walking","Punch","Death"})
            {
                var clip=Load<AnimationClip>(RobotExpressiveBuilder.Native+"/"+name+".anim");var morphs=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(SkinnedMeshRenderer)).ToArray();
                Assert.AreEqual(3,morphs.Length);foreach(var binding in morphs)Assert.IsTrue(AnimationUtility.GetEditorCurve(clip,binding).keys.All(k=>k.value==0));
            }
        }
        [Test] public void PreparedBodyRetainsHeadTorsoHandsAndFeetAsGenericSkins()
        {
            var model=Load<GameObject>(RobotExpressiveBuilder.Prepared+"/Robot.prefab");Assert.NotNull(model);var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(14,skins.Length);Assert.AreEqual(7214,skins.Sum(s=>s.sharedMesh.vertexCount));Assert.AreEqual(3237,skins.Sum(s=>s.sharedMesh.triangles.Length/3));
            Assert.IsTrue(skins.All(s=>s.sharedMesh.blendShapeCount==0&&s.bones.Length>0));Assert.AreEqual(0,model.GetComponentsInChildren<MeshRenderer>(true).Length);
            foreach(string name in new[]{"Head","Torso","Hand_L","Hand_R","Foot_L","Foot_R"})Assert.IsTrue(skins.Any(s=>s.name.EndsWith(name)),name);
        }
        [Test] public void RecipeUsesFourOriginalActionsWithoutHumanoidOrNewAbilities()
        {
            var r=Load<CharacterRecipe>(RobotExpressiveBuilder.RecipePath);Assert.NotNull(r);CollectionAssert.AreEqual(new[]{"Idle","Move","Attack","Death"},r.Clips.Select(c=>c.name));
            Assert.IsTrue(r.Clips.All(c=>!c.humanMotion&&c.length>0));Assert.AreEqual(24,r.frameRate);Assert.AreEqual(1.8f,r.targetBodyHeight);Assert.IsEmpty(r.blendshapeAttachmentPaths);
            Assert.AreEqual(CharacterLowLodPolicy.PaletteGrid,r.lowPolicy);Assert.AreEqual("TowerDefenseRobot01",r.outputName);
        }
        [Test] public void GeneratedVatBindingAndPrivateConfigsAreValid()
        {
            var unit=Load<UnitTypeConfig>(RobotExpressiveBuilder.Output+"/Unit.asset");Assert.NotNull(unit);StringAssert.Contains("机器人",unit.unitTypeName);Assert.IsTrue(RobotExpressiveBuilder.ValidateGeneratedBinding());
            var profile=unit.renderConfig.vatProfile as VATProfile;Assert.NotNull(profile);Assert.IsTrue(profile.HasLowLod);Assert.AreEqual(7214,profile.cleanMesh.vertexCount);
            Assert.IsTrue(AssetDatabase.GetAssetPath(unit.renderConfig).StartsWith(RobotExpressiveBuilder.Output+"/"));Assert.IsTrue(AssetDatabase.GetAssetPath(unit.combatConfig).StartsWith(RobotExpressiveBuilder.Output+"/"));
        }
        [Test] public void PipelineReportIsTechnicalEvidenceNotHumanAcceptance()
        {
            var r=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(RobotExpressiveBuilder.Output+"/PipelineReport.json"));Assert.IsTrue(r.automatedPassed);Assert.IsFalse(r.humanAcceptance);Assert.LessOrEqual(r.maxPositionError,r.allowedPositionError);Assert.Greater(r.positionComparisons,1000000);
        }
        [Test] public void IsolatedTrialShowsTrueRobotPortraitWithoutAddingItToFormalV07()
        {
            var trial=Load<WarSandboxBattlefieldCatalog>(RobotExpressiveBuilder.Output+"/Trial/Catalog.asset");Assert.NotNull(trial);Assert.AreEqual(1,trial.entries.Length);Assert.AreEqual("robot-expressive-pilot",trial.defaultEntryId);Assert.NotNull(trial.templates[0].unitPreview);
            var formal=Load<WarSandboxBattlefieldCatalog>("Assets/Game/OfficialRoster/Version07/Catalog.asset");Assert.AreEqual(28,formal.SelectableEntryCount);Assert.AreEqual(65,formal.templates.Count(t=>!t.hiddenFromSelection));Assert.IsFalse(formal.templates.Any(t=>t.templateId.Contains("robot-pilot")));
        }
        [Test] public void CapturedGpuFramesCoverAllFourStates()
        {
            string capture="Logs/AgentRobotExpressive20261002/capture-03";
            foreach(string state in new[]{"Idle","Move","Attack","Dead"})Assert.AreEqual(12,Directory.GetFiles(capture,"gpu-"+state+"-*.png").Length);
            Assert.IsTrue(File.Exists(capture+"/robot-full-body.png"));Assert.IsTrue(File.Exists(capture+"/gpu-side.png"));
        }
    }
}
