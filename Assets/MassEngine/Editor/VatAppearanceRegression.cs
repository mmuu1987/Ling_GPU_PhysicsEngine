using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MassEngine.Editor
{
    /// <summary>
    /// M5.3 reproducible appearance regression. Run with a graphics device (never -nographics):
    /// -executeMethod MassEngine.Editor.VatAppearanceRegression.Run
    /// --vat-appearance-output=Logs/M53Appearance
    /// Optional: --vat-appearance-rebaked-male=Assets/VAT_Data/M53Verification_GUID/MaleRebaked.asset
    /// The first run creates a UNIQUE persistent Male asset, suitable for a later player performance build.
    /// No source assets are changed; the caller owns cleanup of exactly the directory named in the manifest.
    /// </summary>
    public static class VatAppearanceRegression
    {
        public const string MaleUnitPath = "Assets/Game/Settings/AttackerUnitConfig.asset";
        public const string FemaleUnitPath = "Assets/Game/Settings/DefenderUnitConfig.asset";
        public const string MalePrefabPath = "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab";
        private const string ClipFolder = "Assets/RPG Tiny Hero Duo/Animation/SwordAndShield";
        private static readonly AgentState[] States = { AgentState.Idle, AgentState.Move, AgentState.Attack, AgentState.Dead };
        private static readonly string[] ClipNames = { "idle", "move", "attack", "death" };
        private static readonly string[] LodNames = { "near", "mid", "far" };

        [Serializable]
        public sealed class Manifest
        {
            public int version = 1;
            public bool passed;
            public string outputDirectory;
            public string unityVersion;
            public string graphicsDevice;
            public string graphicsApi;
            public int tileSize = VatAppearanceRegressionCapture.DefaultSize;
            public bool readbackYFlipped;
            public bool rebakedThisRun;
            public string rebakedMaleAssetPath;
            public string verificationAssetDirectory;
            public string coverage = "Male reference + Male rebaked + Female reference; each near/mid/far x idle/move/attack/death x four local frames. Female is NOT rebaked.";
            public string renderMethod = "Actual ResolvedUnitTypeRuntime mesh/material/MPB; 56-byte AgentData + visible indices + indirect args; CommandBuffer.DrawMeshInstancedIndirect ForwardLit/ForwardNoShadow into GPU RT. Fixed orthographic camera/light; forced LOD; no scene postprocessing/shadows or LOD classifier.";
            public string samplePolicy = "Local frames 0, floor((count-1)/3), floor(2*(count-1)/3), count-1; time=(localFrame+0.25)/rate. Death additionally sampled beyond duration to verify final-frame clamp.";
            public Vector3 framingCenter;
            public Vector3 framingSize;
            public double elapsedSeconds;
            public List<CoverageRow> rows = new List<CoverageRow>();
            public List<MaleComparison> maleComparisons = new List<MaleComparison>();
            public List<SourceEvidence> sources = new List<SourceEvidence>();
            public List<string> failures = new List<string>();
        }

        [Serializable]
        public sealed class CoverageRow
        {
            public string variant;
            public string lod;
            public string clip;
            public int state;
            public string profileAsset;
            public string meshName;
            public string meshAsset;
            public int vertexCount;
            public string materialAsset;
            public string shader;
            public string positionTexture;
            public int clipStartFrame;
            public int clipFrameCount;
            public int clipFrameRate;
            public int maximumChangedPixels;
            public bool animationChanged;
            public int deathClampChangedPixels = -1;
            public List<FrameEvidence> frames = new List<FrameEvidence>();
        }

        [Serializable]
        public sealed class FrameEvidence
        {
            public int localFrame;
            public float animationTime;
            public string png;
            public VatAppearanceRegressionImageChecks.Metrics pixels;
        }

        [Serializable]
        public sealed class MaleComparison
        {
            public string lod;
            public string clip;
            public int sample;
            public int changedPixels;
            public float silhouetteIoU;
        }

        [Serializable]
        public sealed class SourceEvidence
        {
            public string path;
            public string sha256Before;
            public string sha256After;
            public bool unchanged;
        }

        [MenuItem("MassEngine/Regression/M5.3 GPU VAT Appearance")]
        public static void Run()
        {
            try
            {
                Manifest result = Execute(Argument("--vat-appearance-output=") ?? "Logs/M53Appearance",
                    Argument("--vat-appearance-rebaked-male="));
                Debug.Log("[M5.3 GPU VAT] " + (result.passed ? "PASS: " : "FAIL: ") +
                    Path.Combine(result.outputDirectory, "appearance-report.md") + "\nPersistent Male: " + result.rebakedMaleAssetPath);
                if (Application.isBatchMode) EditorApplication.Exit(result.passed ? 0 : 2);
                else if (!result.passed) Debug.LogError(string.Join("\n", result.failures));
            }
            catch (Exception exception)
            {
                Debug.LogError("[M5.3 GPU VAT] " + exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>Returns pixel gates + source hashes; writes a report even when a regression fails. Does not exit Unity.</summary>
        public static Manifest Execute(string outputDirectory, string rebakedMaleAssetPath = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run the appearance regression in Edit Mode.");
            string root = Path.GetDirectoryName(Application.dataPath);
            string output = Path.GetFullPath(Path.IsPathRooted(outputDirectory) ? outputDirectory : Path.Combine(root, outputDirectory));
            string assets = Path.GetFullPath(Application.dataPath);
            if (output.Equals(assets, StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reports must be outside Assets (for example Logs/M53Appearance).", nameof(outputDirectory));
            Directory.CreateDirectory(output);
            var manifest = new Manifest
            {
                outputDirectory = output, unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                readbackYFlipped = SystemInfo.graphicsUVStartsAtTop
            };
            var timer = Stopwatch.StartNew();
            UnitTypeConfig clonedUnit = null;
            RenderConfig clonedRender = null;
            try
            {
                UnitTypeConfig male = Load<UnitTypeConfig>(MaleUnitPath);
                UnitTypeConfig female = Load<UnitTypeConfig>(FemaleUnitPath);
                VATProfile maleProfile = male.renderConfig.vatProfile as VATProfile;
                VATProfile femaleProfile = female.renderConfig.vatProfile as VATProfile;
                if (maleProfile == null || femaleProfile == null)
                    throw new InvalidOperationException("Shipped Male/Female must have VATProfile assets.");
                VatBakeRequest request = CreateMaleBakeRequest(maleProfile.frameRate);
                var sourceRoots = new List<string> { MaleUnitPath, FemaleUnitPath, MalePrefabPath,
                    AssetDatabase.GetAssetPath(request.idle), AssetDatabase.GetAssetPath(request.move),
                    AssetDatabase.GetAssetPath(request.attack), AssetDatabase.GetAssetPath(request.death) };
                if (!string.IsNullOrEmpty(rebakedMaleAssetPath)) sourceRoots.Add(rebakedMaleAssetPath);
                SnapshotSources(sourceRoots.ToArray(), manifest.sources);

                VATProfile rebaked;
                if (string.IsNullOrEmpty(rebakedMaleAssetPath))
                {
                    manifest.rebakedThisRun = true;
                    string folderName = "M53Verification_" + Guid.NewGuid().ToString("N");
                    if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/VAT_Data", folderName)))
                        throw new IOException("Could not create a unique M5.3 verification asset folder.");
                    manifest.verificationAssetDirectory = "Assets/VAT_Data/" + folderName;
                    manifest.rebakedMaleAssetPath = manifest.verificationAssetDirectory + "/MaleRebaked.asset";
                    Debug.Log("[M5.3 GPU VAT] Rebaking Male; persistent output: " + manifest.rebakedMaleAssetPath);
                    using (VatBakeResult result = VatBaker.Bake(request))
                        rebaked = result.SaveNew(manifest.rebakedMaleAssetPath);
                }
                else
                {
                    rebaked = Load<VATProfile>(rebakedMaleAssetPath);
                    if (rebaked == maleProfile || rebaked == femaleProfile)
                        throw new ArgumentException("The rebaked input cannot be either shipped reference profile.");
                    manifest.rebakedMaleAssetPath = AssetDatabase.GetAssetPath(rebaked);
                    manifest.verificationAssetDirectory = Path.GetDirectoryName(manifest.rebakedMaleAssetPath).Replace('\\', '/');
                }
                // Write the interoperability pointer immediately, including when a later pixel gate fails.
                File.WriteAllText(Path.Combine(output, "rebaked-male-path.txt"), manifest.rebakedMaleAssetPath + "\n");
                if (!VatProfileValidation.TryValidate(rebaked, out string profileError))
                    throw new InvalidOperationException("Rebaked Male profile is invalid: " + profileError);

                // The authored assets remain read-only. Runtime resolver pairs the new textures/meshes itself.
                clonedUnit = Object.Instantiate(male);
                clonedUnit.hideFlags = HideFlags.HideAndDontSave;
                clonedRender = Object.Instantiate(male.renderConfig);
                clonedRender.hideFlags = HideFlags.HideAndDontSave;
                clonedUnit.renderConfig = clonedRender;
                clonedRender.vatProfile = rebaked;
                clonedRender.nearMesh = clonedRender.midMesh = clonedRender.farMesh = null;
                ResolvedUnitTypeRuntime maleRuntime = ResolvedUnitTypeRuntime.Resolve(male, 1f);
                ResolvedUnitTypeRuntime newRuntime = ResolvedUnitTypeRuntime.Resolve(clonedUnit, 1f);
                ResolvedUnitTypeRuntime femaleRuntime = ResolvedUnitTypeRuntime.Resolve(female, 1f);
                VerifyFemaleThreeTiers(femaleProfile, femaleRuntime);
                Bounds bounds = CombinedBounds(maleProfile, rebaked, femaleProfile);
                manifest.framingCenter = bounds.center;
                manifest.framingSize = bounds.size;
                var rendered = new Dictionary<string, Color32[][]>();
                using (var capture = new VatAppearanceRegressionCapture(bounds, manifest.tileSize))
                {
                    CaptureVariant("male-reference", maleProfile, maleRuntime, capture, manifest, rendered);
                    CaptureVariant("male-rebaked", rebaked, newRuntime, capture, manifest, rendered);
                    CaptureVariant("female-reference", femaleProfile, femaleRuntime, capture, manifest, rendered);
                }
                CompareMale(rendered, manifest);
                if (manifest.rows.Count != 36) manifest.failures.Add("Coverage incomplete: expected 36 variant/LOD/clip rows.");
            }
            catch (Exception exception)
            {
                manifest.failures.Add(exception.ToString());
            }
            finally
            {
                if (clonedUnit != null) Object.DestroyImmediate(clonedUnit);
                if (clonedRender != null) Object.DestroyImmediate(clonedRender);
                VerifySources(manifest);
                timer.Stop();
                manifest.elapsedSeconds = timer.Elapsed.TotalSeconds;
                manifest.passed = manifest.failures.Count == 0 && manifest.rows.Count == 36;
                WriteReports(manifest);
            }
            return manifest;
        }

        private static void CaptureVariant(string variant, VATProfile profile, ResolvedUnitTypeRuntime runtime,
            VatAppearanceRegressionCapture capture, Manifest manifest, Dictionary<string, Color32[][]> rendered)
        {
            for (int lod = 0; lod < 3; lod++)
            {
                var tiles = new Color32[16][];
                for (int clip = 0; clip < States.Length; clip++)
                {
                    VATProfile.VATClipWindow window = GetClip(profile, States[clip]);
                    var row = new CoverageRow
                    {
                        variant = variant, lod = LodNames[lod], clip = ClipNames[clip], state = (int)States[clip],
                        profileAsset = AssetDatabase.GetAssetPath(profile), meshName = runtime.GetMesh(lod).name,
                        meshAsset = AssetDatabase.GetAssetPath(runtime.GetMesh(lod)), vertexCount = runtime.GetMesh(lod).vertexCount,
                        materialAsset = AssetDatabase.GetAssetPath(runtime.GetMaterial(lod)), shader = runtime.GetMaterial(lod).shader.name,
                        positionTexture = runtime.GetBlock(lod).GetTexture("_VATPosTex").name,
                        clipStartFrame = window.startFrame, clipFrameCount = window.frameCount, clipFrameRate = window.frameRate
                    };
                    manifest.rows.Add(row);
                    int[] localFrames = SampleLocalFrames(window);
                    var samples = new List<Color32[]>();
                    for (int sample = 0; sample < localFrames.Length; sample++)
                    {
                        float time = (localFrames[sample] + .25f) / window.frameRate;
                        Color32[] pixels = capture.Capture(runtime, lod, States[clip], time);
                        samples.Add(pixels);
                        tiles[clip * 4 + sample] = pixels;
                        SaveFrame(row, pixels, localFrames[sample], time,
                            variant + "-" + row.lod + "-" + row.clip + "-s" + sample + ".png", manifest);
                    }
                    row.animationChanged = VatAppearanceRegressionImageChecks.HasMotion(samples, out row.maximumChangedPixels);
                    if (!row.animationChanged)
                        manifest.failures.Add(variant + "/" + row.lod + "/" + row.clip + ": frozen GPU animation (max changed pixels " + row.maximumChangedPixels + ").");
                    if (States[clip] == AgentState.Dead)
                    {
                        float beyondEnd = (window.frameCount + 10.25f) / window.frameRate;
                        Color32[] clamped = capture.Capture(runtime, lod, AgentState.Dead, beyondEnd);
                        row.deathClampChangedPixels = VatAppearanceRegressionImageChecks.CountChanged(samples[3], clamped);
                        SaveFrame(row, clamped, window.frameCount - 1, beyondEnd,
                            variant + "-" + row.lod + "-death-clamped.png", manifest);
                        if (row.deathClampChangedPixels != 0)
                            manifest.failures.Add(variant + "/" + row.lod + ": death did not clamp at its final GPU frame.");
                    }
                }
                string key = variant + "-" + LodNames[lod];
                rendered.Add(key, tiles);
                SaveAtlas(Path.Combine(manifest.outputDirectory, key + "-sheet.png"), tiles, manifest.tileSize, 4);
            }
        }

        private static void SaveFrame(CoverageRow row, Color32[] pixels, int localFrame, float time, string file, Manifest manifest)
        {
            var evidence = new FrameEvidence
            {
                localFrame = localFrame, animationTime = time, png = file,
                pixels = VatAppearanceRegressionImageChecks.Measure(pixels)
            };
            row.frames.Add(evidence);
            SavePng(Path.Combine(manifest.outputDirectory, file), pixels, manifest.tileSize, manifest.tileSize);
            if (!VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(evidence.pixels, out string error))
                manifest.failures.Add(file + ": " + error);
        }

        private static void CompareMale(Dictionary<string, Color32[][]> rendered, Manifest manifest)
        {
            for (int lod = 0; lod < 3; lod++)
            {
                Color32[][] original = rendered["male-reference-" + LodNames[lod]];
                Color32[][] rebaked = rendered["male-rebaked-" + LodNames[lod]];
                var sideBySide = new Color32[32][];
                for (int clip = 0; clip < 4; clip++)
                    for (int sample = 0; sample < 4; sample++)
                    {
                        int index = clip * 4 + sample;
                        sideBySide[clip * 8 + sample] = original[index];
                        sideBySide[clip * 8 + sample + 4] = rebaked[index];
                        manifest.maleComparisons.Add(new MaleComparison
                        {
                            lod = LodNames[lod], clip = ClipNames[clip], sample = sample,
                            changedPixels = VatAppearanceRegressionImageChecks.CountChanged(original[index], rebaked[index]),
                            silhouetteIoU = VatAppearanceRegressionImageChecks.SilhouetteIoU(original[index], rebaked[index])
                        });
                    }
                SaveAtlas(Path.Combine(manifest.outputDirectory, "male-" + LodNames[lod] + "-comparison.png"), sideBySide, manifest.tileSize, 8);
            }
        }

        public static VATProfile.VATClipWindow GetClip(VATProfile profile, AgentState state)
        {
            switch (state)
            {
                case AgentState.Idle: return profile.idle;
                case AgentState.Move: return profile.move;
                case AgentState.Attack: return profile.attack;
                case AgentState.Dead: return profile.death;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        public static int[] SampleLocalFrames(VATProfile.VATClipWindow window)
        {
            if (window.frameCount <= 0 || window.frameRate <= 0)
                throw new ArgumentException("A clip must have positive frame count and frame rate.");
            int last = window.frameCount - 1;
            return new[] { 0, last / 3, last * 2 / 3, last };
        }

        public static Bounds CombinedBounds(params VATProfile[] profiles)
        {
            if (profiles == null || profiles.Length == 0) throw new ArgumentException("Profiles are required.");
            Bounds bounds = profiles[0].cleanMesh.bounds;
            foreach (VATProfile profile in profiles)
            {
                bounds.Encapsulate(profile.cleanMesh.bounds);
                if (profile.HasMidLod) bounds.Encapsulate(profile.midLodMesh.bounds);
                if (profile.HasLowLod) bounds.Encapsulate(profile.lowLodMesh.bounds);
            }
            return bounds;
        }

        private static void VerifyFemaleThreeTiers(VATProfile profile, ResolvedUnitTypeRuntime runtime)
        {
            if (!profile.HasMidLod || !profile.HasLowLod || runtime.GetMesh(0) != profile.cleanMesh ||
                runtime.GetMesh(1) != profile.midLodMesh || runtime.GetMesh(2) != profile.lowLodMesh ||
                runtime.GetBlock(0).GetTexture("_VATPosTex") != profile.positionTexture ||
                runtime.GetBlock(1).GetTexture("_VATPosTex") != profile.midLodPositionTexture ||
                runtime.GetBlock(2).GetTexture("_VATPosTex") != profile.lowLodPositionTexture)
                throw new InvalidOperationException("Female reference must exercise its actual full/mid/low mesh+VAT pairs, not a fallback.");
        }

        private static VatBakeRequest CreateMaleBakeRequest(int frameRate)
        {
            GameObject prefab = Load<GameObject>(MalePrefabPath);
            var byName = new Dictionary<string, MeshRenderer>();
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (!byName.ContainsKey(renderer.name)) byName.Add(renderer.name, renderer);
            var extras = new List<MeshRenderer>();
            foreach (string name in new[] { "Hair01", "Head01_Male", "Shield08", "Eye01", "Mouth01" })
            {
                if (!byName.TryGetValue(name, out MeshRenderer renderer))
                    throw new InvalidOperationException("Male prefab is missing the reference attachment " + name);
                extras.Add(renderer);
            }
            return new VatBakeRequest
            {
                model = prefab, frameRate = frameRate, bakeLowLod = true, lowLodRatio = .25f, lowLodMaxVertices = 1200,
                extraRenderers = extras.ToArray(), idle = LoadClip("Idle_Normal_SwordAndShield"),
                move = LoadClip("MoveFWD_Battle_InPlace_SwordAndShield"), attack = LoadClip("Attack01_SwordAndShiled"),
                death = LoadClip("Die01_SwordAndShield")
            };
        }

        private static AnimationClip LoadClip(string name)
        {
            foreach (string folder in new[] { ClipFolder, ClipFolder + "/InPlace" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "/" + name + ".fbx");
                if (clip != null) return clip;
            }
            throw new FileNotFoundException("Missing reference Male animation: " + name);
        }

        private static T Load<T>(string path) where T : Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new FileNotFoundException("Missing " + typeof(T).Name + ": " + path);
            return value;
        }

        private static void SnapshotSources(string[] roots, List<SourceEvidence> entries)
        {
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in AssetDatabase.GetDependencies(roots, true))
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (File.Exists(path)) paths.Add(path);
                if (File.Exists(path + ".meta")) paths.Add(path + ".meta");
            }
            foreach (string path in paths) entries.Add(new SourceEvidence { path = path, sha256Before = Hash(path) });
        }

        private static void VerifySources(Manifest manifest)
        {
            foreach (SourceEvidence source in manifest.sources)
            {
                try
                {
                    source.sha256After = File.Exists(source.path) ? Hash(source.path) : "MISSING";
                    source.unchanged = source.sha256Before == source.sha256After;
                    if (!source.unchanged) manifest.failures.Add("Source asset changed: " + source.path);
                }
                catch (Exception exception)
                {
                    manifest.failures.Add("Could not verify source " + source.path + ": " + exception.Message);
                }
            }
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void SavePng(string path, Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void SaveAtlas(string path, Color32[][] tiles, int tileSize, int columns)
        {
            int rows = tiles.Length / columns;
            var atlas = new Texture2D(tileSize * columns, tileSize * rows, TextureFormat.RGBA32, false);
            try
            {
                for (int i = 0; i < tiles.Length; i++)
                    atlas.SetPixels32(i % columns * tileSize, (rows - 1 - i / columns) * tileSize, tileSize, tileSize, tiles[i]);
                atlas.Apply(false, false);
                File.WriteAllBytes(path, atlas.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(atlas); }
        }

        private static void WriteReports(Manifest manifest)
        {
            File.WriteAllText(Path.Combine(manifest.outputDirectory, "manifest.json"), JsonUtility.ToJson(manifest, true));
            var text = new StringBuilder();
            text.AppendLine("# M5.3 GPU VAT appearance: " + (manifest.passed ? "PASS" : "FAIL"));
            text.AppendLine();
            text.AppendLine("- GPU: " + manifest.graphicsDevice + " / " + manifest.graphicsApi + "; Unity " + manifest.unityVersion);
            text.AppendLine("- Persistent Male: `" + manifest.rebakedMaleAssetPath + "` (" + (manifest.rebakedThisRun ? "rebaked this run" : "explicitly reused") + ").");
            text.AppendLine("- " + manifest.coverage);
            text.AppendLine("- " + manifest.renderMethod);
            text.AppendLine("- " + manifest.samplePolicy);
            text.AppendLine("- Gates: opaque foreground, no shader compilation errors, and error-magenta below 90% of foreground (isolated atlas colors are allowed); each clip changes >=4 pixels across fixed phases; death final frame clamps; source files/metas SHA-256 unchanged.");
            text.AppendLine("- Female was not rebaked: the baker intentionally has no Mid output; the shipped Female's distinct full/mid/low mesh and texture bindings ARE rendered and checked.");
            text.AppendLine("- Far death is a forced shader-path check, not a claim that runtime LOD classification normally retains dead agents in its far bucket.");
            text.AppendLine("- Male reference vs rebaked pixel differences/IoU are recorded, not required to be identical: known attachment bind-pose/Low topology differences are not silently called regressions.");
            text.AppendLine("- This is reproducible GPU appearance evidence, not human visual approval or a complete in-scene lighting/performance test.");
            text.AppendLine("- Source files checked: " + manifest.sources.Count + "; elapsed " + manifest.elapsedSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s.");
            text.AppendLine();
            text.AppendLine("## Contact sheets");
            text.AppendLine("Rows top-to-bottom: idle, move, attack, death. Columns: four fixed local-frame samples. Male comparisons: original four columns LEFT, rebaked four RIGHT. All LODs use the same camera so low-poly changes remain inspectable.");
            foreach (string lod in LodNames)
            {
                text.AppendLine();
                text.AppendLine("### Male " + lod + " — original / rebaked");
                text.AppendLine("![Male " + lod + "](male-" + lod + "-comparison.png)");
                text.AppendLine("### Female " + lod + " — shipped path (not rebaked)");
                text.AppendLine("![Female " + lod + "](female-reference-" + lod + "-sheet.png)");
            }
            text.AppendLine();
            text.AppendLine("## GPU coverage and animation gates");
            text.AppendLine("| Variant | LOD | Clip | Vertices | Max changed pixels | Death clamp changes | Result |");
            text.AppendLine("|---|---|---|---:|---:|---:|---|");
            foreach (CoverageRow row in manifest.rows)
                text.AppendLine("| " + row.variant + " | " + row.lod + " | " + row.clip + " | " + row.vertexCount + " | " +
                    row.maximumChangedPixels + " | " + row.deathClampChangedPixels + " | " + (row.animationChanged ? "motion detected" : "FROZEN") + " |");
            text.AppendLine();
            text.AppendLine("## Failures");
            if (manifest.failures.Count == 0) text.AppendLine("None.");
            else foreach (string failure in manifest.failures) text.AppendLine("- " + failure);
            text.AppendLine();
            text.AppendLine("Full per-frame foreground/magenta statistics, resolved bindings, comparison IoU and source hashes: [manifest.json](manifest.json). PNGs are GPU readbacks, not CPU-reconstructed vertices. Cleanup only the uniquely created verification folder after the performance run; do not delete a reused caller-owned folder.");
            File.WriteAllText(Path.Combine(manifest.outputDirectory, "appearance-report.md"), text.ToString());
        }

        private static string Argument(string prefix)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix, StringComparison.Ordinal)) return argument.Substring(prefix.Length).Trim('"');
            return null;
        }
    }
}
