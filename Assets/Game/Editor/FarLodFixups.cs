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
    /// 2026-10-15 male warrior wrap-up (user-approved "1 改，2修"):
    ///  1  repoint the render configs still on the old Male_Far300F to the new MaleSword_VAT;
    ///  2  the female far mesh (61 tris, non-manifold) is rebuilt from the full mesh at the infantry budget (300 tris)
    ///     as a far variant of the female profile, and every render config on the female profile is repointed to it.
    /// Near/mid meshes stay the same objects. Explicit mesh overrides are remapped slot by slot only when they equal the
    /// old profile's mesh for that slot; any other override skips the config and is logged.
    /// </summary>
    public static class FarLodFixups
    {
        private const string FemaleSource = "Assets/Art/VAT/Characters/FemaleCharacterPBR_Stage5_MultiClip_Profile.asset";
        private const string FarParent = "Assets/Game/Content/FarLod";
        private const string FarFolderName = "Batch-20261015-01";
        private const string FemaleFar = FarParent + "/" + FarFolderName + "/Female_Far300F.asset";
        private const string MaleOld = "Assets/Game/Content/FarLod/Batch-20261010-07/Male_Far300F.asset";
        private const string Output = "outputs/LodCheck-20261015";
        public const int InfantryFarTriangles = 300;

        [MenuItem("Tools/角色/远景修复：预演（不改资产）")]
        public static void DryRun() => Run(false);

        [MenuItem("Tools/角色/远景修复：女角色重建远景 + 男对手改引用")]
        public static void Apply() => Run(true);

        private static void Run(bool apply)
        {
            string output = Path.Combine(Path.GetDirectoryName(Application.dataPath), Output);
            Directory.CreateDirectory(output);
            var log = new StringBuilder("FarLodFixups " + (apply ? "APPLY " : "DRY ") + DateTime.Now.ToString("s") + "\n");
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
                var femaleSource = Load<VATProfile>(FemaleSource);
                var maleOld = Load<VATProfile>(MaleOld);
                var maleNew = Load<VATProfile>(MaleWarriorRebuild.FinalPath);
                log.AppendLine("female source low=" + Facts(femaleSource.lowLodMesh) + " mid=" + Facts(femaleSource.midLodMesh) + " clean=" + Facts(femaleSource.cleanMesh));

                VATProfile femaleFar = AssetDatabase.LoadAssetAtPath<VATProfile>(FemaleFar);
                if (femaleFar == null && apply)
                {
                    if (!AssetDatabase.IsValidFolder(FarParent + "/" + FarFolderName)) AssetDatabase.CreateFolder(FarParent, FarFolderName);
                    var settings = new VatFarLodReducer.Settings
                    {
                        targetTriangles = InfantryFarTriangles, sourceSlot = "full",
                        preserveSeams = false, optimalPlacement = false, splitChartCorners = true
                    };
                    femaleFar = VatFarLodReducer.CreateFarVariant(femaleSource, FemaleFar, settings, out VatFarLodReducer.Report r);
                    log.AppendLine("created " + FemaleFar + ": src " + r.sourceFarSlot + " v" + r.sourceFarVertices + " t" + r.sourceFarTriangles +
                        " -> v" + r.resultVertices + " t" + r.resultTriangles + " reached " + r.reachedTarget +
                        " dev max " + r.maxDeviationOfHeight.ToString("P2") + " mean " + r.meanDeviationOfHeight.ToString("P2") +
                        " dropped " + r.droppedComponents + "(" + r.droppedComponentTriangles + "t) " + r.seconds.ToString("F1") + "s");
                }
                if (femaleFar != null)
                {
                    if (!VatProfileValidation.TryValidate(femaleFar, out string error)) throw new InvalidOperationException("female far invalid: " + error);
                    log.AppendLine("female far low=" + Facts(femaleFar.lowLodMesh) + " mid same=" + (femaleFar.midLodMesh == femaleSource.midLodMesh) +
                        " clean same=" + (femaleFar.cleanMesh == femaleSource.cleanMesh) + " valid=true");
                }
                if (!VatProfileValidation.TryValidate(maleNew, out string maleError)) throw new InvalidOperationException("male new invalid: " + maleError);

                Repoint(femaleSource, femaleFar, apply, log);
                Repoint(maleOld, maleNew, apply, log);
                AssetDatabase.SaveAssets();

                if (apply && femaleFar != null) PreviewFemale(femaleFar, output, log);
            }
            catch (Exception e) { log.AppendLine("!! " + e); }
            File.WriteAllText(Path.Combine(output, apply ? "apply.txt" : "dry.txt"), log.ToString());
        }

        private static void Repoint(VATProfile from, VATProfile to, bool apply, StringBuilder log)
        {
            log.AppendLine("-- " + AssetDatabase.GetAssetPath(from) + " -> " + (to != null ? AssetDatabase.GetAssetPath(to) : "(not built yet)"));
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:RenderConfig"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var render = AssetDatabase.LoadAssetAtPath<RenderConfig>(path);
                if (render == null || render.vatProfile != from) continue;
                n++;
                string why = null;
                Mesh near = Map(render.nearMesh, from.cleanMesh, to != null ? to.cleanMesh : null, "near", ref why);
                Mesh mid = Map(render.midMesh, from.midLodMesh, to != null ? to.midLodMesh : null, "mid", ref why);
                Mesh far = Map(render.farMesh, from.lowLodMesh, to != null ? to.lowLodMesh : null, "far", ref why);
                log.AppendLine("   " + path + "  overrides " + Name(render.nearMesh) + "/" + Name(render.midMesh) + "/" + Name(render.farMesh) +
                    (why != null ? "  SKIP " + why : apply && to != null ? "  -> repointed" : "  ok"));
                if (why != null || !apply || to == null) continue;
                render.vatProfile = to;
                render.nearMesh = near; render.midMesh = mid; render.farMesh = far;
                EditorUtility.SetDirty(render);
            }
            log.AppendLine("   configs: " + n);
        }

        private static Mesh Map(Mesh current, Mesh oldSlot, Mesh newSlot, string slot, ref string why)
        {
            if (current == null) return null;
            if (current == oldSlot) return newSlot;
            why = (why ?? "") + slot + " override " + current.name + " is not the profile mesh; ";
            return current;
        }

        private static void PreviewFemale(VATProfile far, string output, StringBuilder log)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UnitTypeConfig"))
            {
                var unit = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (unit == null || unit.renderConfig == null || unit.renderConfig.vatProfile != far) continue;
                try { UnitLodDiagnostics.RunUnitConfig(unit, "Female_Far300F_" + unit.name + ".asset", output, log); }
                catch (Exception e) { log.AppendLine("   preview fail: " + e.Message); }
                return;
            }
            log.AppendLine("   preview: no UnitTypeConfig uses the female far profile");
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Missing " + typeof(T).Name + ": " + path);
            return asset;
        }

        private static string Name(UnityEngine.Object o) => o == null ? "null" : o.name;
        private static string Facts(Mesh m) => m == null ? "null" : m.name + " v" + m.vertexCount + " t" + m.GetIndexCount(0) / 3;
    }
}
