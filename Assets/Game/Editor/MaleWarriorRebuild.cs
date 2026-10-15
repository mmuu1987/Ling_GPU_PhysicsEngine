using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MassEngine.Editor;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-12 male warrior fix (user-approved A + B1):
    ///  A  rebake the male VAT with the right-hand sword (OHS03) included as an attachment;
    ///  B1 rebuild mid (QEM ~2000 tris) and far (QEM 300 tris) from the new full mesh with the
    ///     seam-aware multi-pose QEM reducer, replacing the UV-welded 994-vertex mid mesh.
    /// Only creates NEW assets under <see cref="Folder"/> (needed after a clean clone; the assets are git-ignored).
    /// The one-off preview / "switch to new VAT" menus were archived 2026-10-15 to outputs/Cleanup-20261015-01;
    /// FarLodFixups repoints the render configs.
    /// </summary>
    public static class MaleWarriorRebuild
    {
        private const string Parent = "Assets/Game/Content/Characters";
        private const string FolderName = "MaleWarrior-20261012";
        public const string Folder = Parent + "/" + FolderName;
        private const string BasePath = Folder + "/MaleSword_Base.asset";
        private const string MidPath = Folder + "/MaleSword_Mid.asset";
        public const string FinalPath = Folder + "/MaleSword_VAT.asset";
        private const string PrefabPath = "Assets/MassEngine/Tests/Editor/LegacyHumanoid/Prefab/MaleCharacterPBR.prefab";
        private const string ClipFolder = "Assets/MassEngine/Tests/Editor/LegacyHumanoid/Animation/SwordAndShield";
        private const string ReferencePath = "Assets/Art/VAT/Characters/MaleCharacter_Stage5_MultiClip_Profile.asset";
        private const string Output = "outputs/LodCheck-20261012";
        public const int MidTriangles = 2000;
        public const int FarTriangles = 300;
        private static readonly string[] Attachments = { "Hair01", "Head01_Male", "Shield08", "OHS03", "Eye01", "Mouth01" };
        [MenuItem("Tools/角色/男战士：重烘加剑 + 重建中远景（只生成新资产）")]
        public static void Build()
        {
            string output = Path.Combine(Path.GetDirectoryName(Application.dataPath), Output);
            Directory.CreateDirectory(output);
            var log = new StringBuilder("MaleWarriorRebuild.Build " + DateTime.Now.ToString("s") + "\n");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, FolderName);

                var prefab = Load<GameObject>(PrefabPath);
                var reference = Load<VATProfile>(ReferencePath);
                var byName = new Dictionary<string, MeshRenderer>();
                foreach (MeshRenderer r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (!byName.ContainsKey(r.name)) byName.Add(r.name, r);
                var extras = new List<MeshRenderer>();
                foreach (string name in Attachments)
                {
                    if (!byName.TryGetValue(name, out MeshRenderer r)) throw new InvalidOperationException("Prefab is missing attachment " + name);
                    extras.Add(r);
                }

                // ---- A: full bake with the sword ----
                var request = new VatBakeRequest
                {
                    model = prefab, frameRate = reference.frameRate, bakeLowLod = false, extraRenderers = extras.ToArray(),
                    idle = Clip("Idle_Normal_SwordAndShield"), move = Clip("InPlace/MoveFWD_Battle_InPlace_SwordAndShield"),
                    attack = Clip("Attack01_SwordAndShiled"), death = Clip("Die01_SwordAndShield")
                };
                VATProfile baseProfile;
                using (VatBakeResult baked = VatBaker.Bake(request)) baseProfile = baked.SaveNew(BasePath);
                log.AppendLine("base " + BasePath + ": clean v" + baseProfile.cleanMesh.vertexCount + " tris " + baseProfile.cleanMesh.GetIndexCount(0) / 3 +
                    " tex " + baseProfile.textureWidth + "x" + baseProfile.textureHeight + " rpf " + baseProfile.rowsPerFrame +
                    " frames " + baseProfile.totalFrameCount + "@" + baseProfile.frameRate + " readable " + baseProfile.positionTexture.isReadable);
                CompareWindow(log, "idle", reference.idle, baseProfile.idle);
                CompareWindow(log, "move", reference.move, baseProfile.move);
                CompareWindow(log, "attack", reference.attack, baseProfile.attack);
                CompareWindow(log, "death", reference.death, baseProfile.death);

                // ---- B1 (mid): QEM of the new full mesh; the reducer writes it into the low slot, then we move it to mid ----
                var midSettings = new VatFarLodReducer.Settings
                {
                    targetTriangles = MidTriangles, sourceSlot = "full", preserveSeams = false, optimalPlacement = false, splitChartCorners = true
                };
                VATProfile midProfile = VatFarLodReducer.CreateFarVariant(baseProfile, MidPath, midSettings, out VatFarLodReducer.Report midReport);
                midProfile.midLodMesh = midProfile.lowLodMesh;
                midProfile.midLodPositionTexture = midProfile.lowLodPositionTexture;
                midProfile.midLodNormalTexture = midProfile.lowLodNormalTexture;
                midProfile.midLodTextureWidth = midProfile.lowLodTextureWidth;
                midProfile.midLodTextureHeight = midProfile.lowLodTextureHeight;
                midProfile.midLodRowsPerFrame = midProfile.lowLodRowsPerFrame;
                midProfile.lowLodMesh = null;
                midProfile.lowLodPositionTexture = null;
                midProfile.lowLodNormalTexture = null;
                midProfile.lowLodTextureWidth = midProfile.lowLodTextureHeight = midProfile.lowLodRowsPerFrame = 0;
                if (!VatProfileValidation.TryValidate(midProfile, out string midError)) throw new InvalidOperationException("mid profile: " + midError);
                EditorUtility.SetDirty(midProfile);
                AssetDatabase.SaveAssetIfDirty(midProfile);
                log.AppendLine("mid report " + JsonUtility.ToJson(midReport));

                // ---- B1 (far): QEM 300 of the full mesh; mid carries over from midProfile ----
                var farSettings = new VatFarLodReducer.Settings
                {
                    targetTriangles = FarTriangles, sourceSlot = "full", preserveSeams = false, optimalPlacement = false, splitChartCorners = true
                };
                VATProfile final = VatFarLodReducer.CreateFarVariant(midProfile, FinalPath, farSettings, out VatFarLodReducer.Report farReport);
                log.AppendLine("far report " + JsonUtility.ToJson(farReport));
                log.AppendLine("final " + FinalPath + ": clean " + final.cleanMesh.name + " v" + final.cleanMesh.vertexCount +
                    " | mid " + final.midLodMesh.name + " v" + final.midLodMesh.vertexCount + " tris " + final.midLodMesh.GetIndexCount(0) / 3 +
                    " | low " + final.lowLodMesh.name + " v" + final.lowLodMesh.vertexCount + " tris " + final.lowLodMesh.GetIndexCount(0) / 3);
                AssetDatabase.SaveAssets();

                Preview(final, output, log);
            }
            catch (Exception e)
            {
                log.AppendLine("!! " + e);
                Debug.LogError("[MaleWarriorRebuild] " + e);
            }
            File.WriteAllText(Path.Combine(output, "rebuild.txt"), log.ToString());
            Debug.Log("[MaleWarriorRebuild] build finished: " + Path.Combine(output, "rebuild.txt"));
        }

        private static void Preview(VATProfile profile, string output, StringBuilder log)
        {
            var unit = Load<UnitTypeConfig>("Assets/Game/Settings/AttackerMeleeUnitConfig.asset");
            var clone = UnityEngine.Object.Instantiate(unit);
            var render = UnityEngine.Object.Instantiate(unit.renderConfig);
            clone.hideFlags = render.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                clone.renderConfig = render;
                render.vatProfile = profile;
                render.nearMesh = render.midMesh = render.farMesh = null;
                UnitLodDiagnostics.RunUnitConfig(clone, "MaleSword_new.asset", output, log);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(render);
            }
        }

        private static void CompareWindow(StringBuilder log, string label, VATProfile.VATClipWindow a, VATProfile.VATClipWindow b)
        {
            bool same = a.startFrame == b.startFrame && a.frameCount == b.frameCount && a.frameRate == b.frameRate && a.loop == b.loop;
            log.AppendLine("   window " + label + " ref " + a.startFrame + "+" + a.frameCount + "@" + a.frameRate + (a.loop ? " loop" : "") +
                " new " + b.startFrame + "+" + b.frameCount + "@" + b.frameRate + (b.loop ? " loop" : "") + (same ? " OK" : " DIFF"));
        }

        private static AnimationClip Clip(string relative)
        {
            string path = ClipFolder + "/" + relative + ".fbx";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) throw new FileNotFoundException("Missing clip " + path);
            return clip;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException("Missing " + typeof(T).Name + " " + path);
            return asset;
        }
    }
}

