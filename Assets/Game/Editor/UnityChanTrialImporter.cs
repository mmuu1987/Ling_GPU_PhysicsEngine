using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>Fixed UnityChan input preparation for M5.4, not a general importer or a VAT baker.</summary>
    public static class UnityChanTrialImporter
    {
        public const string SourceDirectory = "Assets/Art/Source/ModelTrials/UnityChan/Models";
        public const string SourceModelPath = SourceDirectory + "/unitychan.fbx";
        private const int AtlasSize = 4096;
        private const int CellSize = 1024;
        private const int Padding = 8;
        private const int ImageSize = CellSize - Padding * 2;
        private const float MaxUvCoordinate = 32f;
        private const float MaxUvSpan = 8f;

        private static readonly string[] TextureNames =
        {
            "body_01.tga", "hair_01.tga", "face_00.tga", "skin_01.tga", "eyeline_00.tga",
            "eye_iris_L_00.tga", "eye_iris_R_00.tga", "", ""
        };
        private static readonly Dictionary<string, int> MaterialSlots = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "body", 0 }, { "hair", 1 }, { "face", 2 }, { "skin1", 3 },
            { "eyebase", 4 }, { "eyeline", 4 }, { "eye_L1", 5 }, { "eye_R1", 6 },
            { "Left", 7 }, { "Right", 8 }
        };

        /// <summary>
        /// Writes only new assets in an existing output folder, and returns a persisted standalone prefab.
        /// Source FBX, materials, textures and importer settings are never changed. Requires a graphics device.
        /// </summary>
        public static GameObject CreatePreparedPrefab(string outputDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before preparing UnityChan.");
            outputDirectory = (outputDirectory ?? "").Replace('\\', '/').TrimEnd('/');
            if (!outputDirectory.StartsWith("Assets/", StringComparison.Ordinal) ||
                outputDirectory.Contains("/../") || outputDirectory.Contains("/./") ||
                !AssetDatabase.IsValidFolder(outputDirectory) ||
                outputDirectory.StartsWith("Assets/Art/Source/ModelTrials", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use an existing, separate Assets output folder, not the source folder.", nameof(outputDirectory));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || SystemInfo.maxTextureSize < AtlasSize)
                throw new InvalidOperationException("UnityChan atlas preparation requires a graphics device supporting 4096 textures.");

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath);
            if (source == null) throw new InvalidOperationException("Import the source model first: " + SourceModelPath);
            Animator animator = source.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The source root must have its own valid Humanoid Avatar before preparation.");

            var createdPaths = new List<string>();
            var ownedObjects = new List<Object>();
            Scene preview = default;
            GameObject staging = null;
            var report = new PreparationReport
            {
                sourceModel = SourceModelPath,
                avatar = AssetDatabase.GetAssetPath(animator.avatar),
                atlasSize = AtlasSize,
                paddingPixels = Padding,
                simplifications = new[]
                {
                    "Fixed material-name mapping reads the original _MainTex color sources, not empty _BaseMap or missing toon shaders.",
                    "Cheek renderer and mat_cheek submeshes are disabled/omitted; hierarchy transforms are retained.",
                    "Left/right iris RGB is composited over white using source alpha. All atlas pixels are opaque; no runtime cutout/transparency.",
                    "Other base textures keep RGB and discard alpha. Toon ramps, specular, normal maps, outline, spring bones and facial animation are not reproduced.",
                    "Every non-cheek MeshRenderer is retained with its existing enabled/active state; pass active ones explicitly to VatBaker as extras.",
                    "Source transforms, skeleton, Avatar, arbitrary bone influences and all blendshape frames/weights are retained.",
                    "All triangle-referenced UV0 values are measured per submesh and combined per atlas slot. Each axis inside [0,1] keeps that domain; otherwise its measured domain (minimum span 1) is baked with the original texture wrap modes.",
                    "UV0 remapping is affine across the entire source domain, never per-vertex clamp/modulo. Cross-integer triangle interpolation is preserved; finite resampling/mip detail remains approximate. Absolute UV coordinates above 32 or slot spans above 8 are rejected.",
                    "Color atlas is persisted as lossless 4096 PNG and synchronously imported as sRGB, Clamp, mipmapped, uncompressed RGBA32. Preview material references the imported persistent Texture2D; VAT data remains unchanged.",
                    "Mesh vertices are separated per original submesh, atlas UV0 is remapped, then triangles are merged into submesh 0. Higher UV channels and vertex colors are copied when present.",
                    "Preview uses an opaque back-face-culled material, not the source double-sided toon appearance. VAT shaders must be assigned separately by the caller.",
                    "Full/Low VAT is NOT generated here. Existing spatial Low clustering can average UVs across atlas islands; inspect Low appearance before accepting it."
                }
            };
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                staging = new GameObject("UnityChan Preparation") { hideFlags = HideFlags.HideAndDontSave };
                staging.SetActive(false);
                SceneManager.MoveGameObjectToScene(staging, preview);
                GameObject instance = Object.Instantiate(source, staging.transform, false);
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "UnityChanPrepared";
                instance.hideFlags = HideFlags.None;

                var plans = new List<RendererPlan>();
                var domains = new Dictionary<int, UvDomain>();
                int skinnedCount = 0;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (skinned == null && !(renderer is MeshRenderer))
                        throw new InvalidOperationException("Unexpected renderer type: " + renderer.GetType().Name);
                    Mesh mesh = skinned != null ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                    if (mesh == null || mesh.vertexCount == 0)
                        throw new InvalidOperationException("Missing/empty mesh: " + renderer.name);
                    var record = new RendererRecord
                    {
                        path = AnimationUtility.CalculateTransformPath(renderer.transform, instance.transform),
                        rendererType = renderer.GetType().Name,
                        sourceMesh = mesh.name,
                        sourceVertexCount = mesh.vertexCount,
                        sourceSubmeshCount = mesh.subMeshCount,
                        enabled = renderer.enabled,
                        activeSelf = renderer.gameObject.activeSelf
                    };
                    report.renderers.Add(record);
                    if (renderer.name == "cheek")
                    {
                        renderer.enabled = false;
                        record.omitted = "cheek transparent overlay";
                        continue;
                    }
                    Material[] materials = renderer.sharedMaterials;
                    if (materials.Length != mesh.subMeshCount || mesh.subMeshCount == 0)
                        throw new InvalidOperationException(renderer.name + ": material count must exactly match submesh count.");
                    var slots = new int[mesh.subMeshCount];
                    bool hasGeometry = false;
                    for (int sub = 0; sub < slots.Length; sub++)
                    {
                        if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                            throw new InvalidOperationException(renderer.name + ": only triangle submeshes are supported.");
                        string name = materials[sub] != null ? materials[sub].name : "<null>";
                        if (name.EndsWith(" (Instance)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 11);
                        int slot;
                        if (name == "mat_cheek") slot = -1;
                        else if (!MaterialSlots.TryGetValue(name, out slot))
                            throw new InvalidOperationException(renderer.name + ": unknown material '" + name + "'; refusing guessed atlas mapping.");
                        slots[sub] = slot;
                        var materialRecord = new MaterialRecord { submesh = sub, material = name, atlasSlot = slot };
                        record.materials.Add(materialRecord);
                        if (slot >= 0 && mesh.GetIndexCount(sub) > 0)
                        {
                            hasGeometry = true;
                            UvDomain measured = MeasureUvDomain(mesh, sub);
                            materialRecord.hasUvRange = true;
                            materialRecord.uvMin = measured.min;
                            materialRecord.uvMax = measured.max;
                            Debug.Log("[UnityChan UV] " + record.path + " mesh=" + mesh.name + " submesh=" + sub +
                                " material=" + name + " atlasSlot=" + slot + " min=" + measured.min.ToString("F6") +
                                " max=" + measured.max.ToString("F6"));
                            if (domains.TryGetValue(slot, out UvDomain domain))
                            {
                                domain.min = Vector2.Min(domain.min, measured.min);
                                domain.max = Vector2.Max(domain.max, measured.max);
                            }
                            else domains.Add(slot, measured);
                        }
                    }
                    if (!hasGeometry)
                    {
                        renderer.enabled = false;
                        record.omitted = "no non-cheek triangles";
                        continue;
                    }
                    if (skinned != null) skinnedCount++;
                    plans.Add(new RendererPlan { renderer = renderer, source = mesh, slots = slots, record = record });
                }
                if (skinnedCount == 0) throw new InvalidOperationException("No retained SkinnedMeshRenderer: BoxUnityChan is not a valid substitute.");

                // Validate all slot ranges before creating any output assets, including shared-material unions.
                FinalizeUvDomains(domains);
                report.atlasPath = outputDirectory + "/UnityChanAtlas.png";
                Texture2D rawAtlas = BuildAtlas(domains, report);
                Texture2D atlas;
                try
                {
                    atlas = SaveNewAtlasPng(rawAtlas, report.atlasPath, createdPaths);
                }
                finally
                {
                    // Materials must reference the imported persistent texture, never this generated staging object.
                    Object.DestroyImmediate(rawAtlas);
                }
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("No supported preview shader found.");
                var material = new Material(shader) { name = "UnityChanPreparedPreview" };
                ownedObjects.Add(material);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", atlas);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", atlas);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
                report.materialPath = outputDirectory + "/UnityChanPreparedPreview.mat";
                SaveNewAsset(material, report.materialPath, createdPaths);

                for (int i = 0; i < plans.Count; i++)
                {
                    RendererPlan plan = plans[i];
                    Mesh mesh = RemapMesh(plan.source, plan.slots, domains);
                    ownedObjects.Add(mesh);
                    string meshPath = outputDirectory + "/UnityChanMesh_" + i.ToString("D2") + ".asset";
                    SaveNewAsset(mesh, meshPath, createdPaths);
                    var skinned = plan.renderer as SkinnedMeshRenderer;
                    if (skinned != null)
                    {
                        float[] weights = new float[plan.source.blendShapeCount];
                        for (int b = 0; b < weights.Length; b++) weights[b] = skinned.GetBlendShapeWeight(b);
                        skinned.sharedMesh = mesh;
                        for (int b = 0; b < weights.Length; b++) skinned.SetBlendShapeWeight(b, weights[b]);
                    }
                    else plan.renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
                    plan.renderer.sharedMaterials = new[] { material };
                    plan.record.preparedMesh = meshPath;
                    plan.record.preparedVertexCount = mesh.vertexCount;
                    plan.record.preparedSubmeshCount = mesh.subMeshCount;
                    plan.record.blendShapeCount = mesh.blendShapeCount;
                    report.preparedVertexCount += mesh.vertexCount;
                }

                report.prefabPath = outputDirectory + "/UnityChanPrepared.prefab";
                RequireNewPath(report.prefabPath);
                createdPaths.Add(report.prefabPath);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, report.prefabPath, out bool success);
                if (!success || prefab == null) throw new InvalidOperationException("Could not persist prepared UnityChan prefab.");
                string reportPath = outputDirectory + "/UnityChanPreparation.json";
                RequireNewPath(reportPath);
                createdPaths.Add(reportPath);
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + "\n");
                AssetDatabase.ImportAsset(reportPath, ImportAssetOptions.ForceSynchronousImport);
                // Do not flush unrelated dirty assets (especially source materials) from the caller's editor.
                foreach (Object value in ownedObjects) AssetDatabase.SaveAssetIfDirty(value);
                AssetDatabase.SaveAssetIfDirty(prefab);
                Debug.Log("UnityChan prepared (not VAT-baked): " + report.prefabPath + "; report: " + reportPath);
                return prefab;
            }
            catch
            {
                for (int i = createdPaths.Count - 1; i >= 0; i--)
                {
                    string path = createdPaths[i];
                    try
                    {
                        if (!AssetDatabase.DeleteAsset(path))
                        {
                            // A PNG/JSON write can fail before Unity registers it. Every tracked path was
                            // checked absent before this operation, so this never removes caller/source assets.
                            if (File.Exists(path)) File.Delete(path);
                            if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                        }
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogError("UnityChan preparation rollback could not remove " + path + ": " + cleanupError.Message);
                    }
                }
                throw;
            }
            finally
            {
                if (staging != null) Object.DestroyImmediate(staging);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                foreach (Object value in ownedObjects)
                    if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
            }
        }

        private static Rect SlotRect(int slot)
        {
            return new Rect(((slot % 4) * CellSize + Padding) / (float)AtlasSize,
                ((slot / 4) * CellSize + Padding) / (float)AtlasSize,
                ImageSize / (float)AtlasSize, ImageSize / (float)AtlasSize);
        }

        private static UvDomain MeasureUvDomain(Mesh mesh, int submesh)
        {
            Vector2[] uv = mesh.uv;
            if (uv.Length != mesh.vertexCount) throw new InvalidOperationException(mesh.name + ": complete UV0 is required.");
            var domain = new UvDomain
            {
                min = new Vector2(float.PositiveInfinity, float.PositiveInfinity),
                max = new Vector2(float.NegativeInfinity, float.NegativeInfinity)
            };
            foreach (int index in mesh.GetTriangles(submesh))
            {
                if (index < 0 || index >= uv.Length) throw new InvalidOperationException(mesh.name + ": invalid triangle index.");
                Vector2 value = uv[index];
                if (!IsFiniteUv(value)) throw new InvalidOperationException(mesh.name + ": non-finite UV0 at vertex " + index);
                domain.min = Vector2.Min(domain.min, value);
                domain.max = Vector2.Max(domain.max, value);
            }
            return domain;
        }

        private static void FinalizeUvDomains(Dictionary<int, UvDomain> domains)
        {
            for (int slot = 0; slot < TextureNames.Length; slot++)
            {
                if (!domains.TryGetValue(slot, out UvDomain domain)) continue;
                Vector2 x = AxisDomain(domain.min.x, domain.max.x);
                Vector2 y = AxisDomain(domain.min.y, domain.max.y);
                domain.sampleRect = Rect.MinMaxRect(x.x, y.x, x.y, y.y);
                Debug.Log("[UnityChan UV slot] slot=" + slot + " texture=" + TextureNames[slot] +
                    " measuredMin=" + domain.min.ToString("F6") + " measuredMax=" + domain.max.ToString("F6") +
                    " sampleMin=" + domain.sampleRect.min.ToString("F6") + " sampleSize=" + domain.sampleRect.size.ToString("F6"));
                if (Mathf.Abs(domain.min.x) > MaxUvCoordinate || Mathf.Abs(domain.min.y) > MaxUvCoordinate ||
                    Mathf.Abs(domain.max.x) > MaxUvCoordinate || Mathf.Abs(domain.max.y) > MaxUvCoordinate ||
                    domain.sampleRect.width > MaxUvSpan || domain.sampleRect.height > MaxUvSpan)
                    throw new InvalidOperationException("Atlas slot " + slot + ": UV range exceeds fixed trial limits (absolute coordinate 32, span 8). See [UnityChan UV] diagnostics.");
            }
        }

        // Preserve the existing 0..1 path. Out-of-range coordinates remain in source UV space,
        // including integer translations; texture sampling, not vertex modulo, applies wrapping.
        private static Vector2 AxisDomain(float min, float max)
        {
            if (min >= 0f && max <= 1f) return new Vector2(0f, 1f);
            if (max - min >= 1f) return new Vector2(min, max);
            float center = (min + max) * 0.5f;
            return new Vector2(center - 0.5f, center + 0.5f);
        }

        private static bool IsFiniteUv(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
        }

        private static Texture2D BuildAtlas(Dictionary<int, UvDomain> domains, PreparationReport report)
        {
            var pixels = new Color32[AtlasSize * AtlasSize];
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
            for (int slot = 0; slot < TextureNames.Length; slot++)
            {
                if (!domains.TryGetValue(slot, out UvDomain domain)) continue;
                Color32[] tile;
                string path = TextureNames[slot].Length > 0 ? SourceDirectory + "/Texture/" + TextureNames[slot] : "";
                var slotRecord = new AtlasRecord
                {
                    slot = slot, sourceTexture = path, uvRect = SlotRect(slot), sourceUvMin = domain.min,
                    sourceUvMax = domain.max, sampledSourceRect = domain.sampleRect,
                    wrapU = "ConstantColor", wrapV = "ConstantColor", sampledCellSize = ImageSize
                };
                if (path.Length > 0)
                {
                    Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (source == null) throw new InvalidOperationException("Missing color texture: " + path);
                    slotRecord.wrapU = source.wrapModeU.ToString();
                    slotRecord.wrapV = source.wrapModeV.ToString();
                    slotRecord.sourceWidth = source.width;
                    slotRecord.sourceHeight = source.height;
                    Debug.Log("[UnityChan UV sampling] slot=" + slot + " wrapU=" + slotRecord.wrapU + " wrapV=" + slotRecord.wrapV +
                        " source=" + source.width + "x" + source.height + " cell=" + ImageSize + "x" + ImageSize);
                    tile = ReadColorTexture(source, domain.sampleRect);
                }
                else
                {
                    tile = new Color32[ImageSize * ImageSize];
                    Color32 color = slot == 7 ? (Color32)new Color(0.07778566f, 0.7064363f, 0.8679245f, 1) : (Color32)Color.red;
                    for (int i = 0; i < tile.Length; i++) tile[i] = color;
                }
                bool whiteComposite = slot == 5 || slot == 6;
                for (int i = 0; i < tile.Length; i++)
                {
                    Color32 c = tile[i];
                    if (whiteComposite)
                    {
                        int a = c.a;
                        c.r = (byte)((c.r * a + 255 * (255 - a) + 127) / 255);
                        c.g = (byte)((c.g * a + 255 * (255 - a) + 127) / 255);
                        c.b = (byte)((c.b * a + 255 * (255 - a) + 127) / 255);
                    }
                    c.a = 255;
                    tile[i] = c;
                }
                int x0 = slot % 4 * CellSize, y0 = slot / 4 * CellSize;
                for (int y = 0; y < CellSize; y++)
                    for (int x = 0; x < CellSize; x++)
                        pixels[(y0 + y) * AtlasSize + x0 + x] = tile[Mathf.Clamp(y - Padding, 0, ImageSize - 1) * ImageSize + Mathf.Clamp(x - Padding, 0, ImageSize - 1)];
                slotRecord.irisWhiteComposite = whiteComposite;
                report.atlasSlots.Add(slotRecord);
            }
            var atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, true, false)
            {
                name = "UnityChanAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 1
            };
            try
            {
                atlas.SetPixels32(pixels);
                atlas.Apply(true, false);
                return atlas;
            }
            catch { Object.DestroyImmediate(atlas); throw; }
        }

        private static Color32[] ReadColorTexture(Texture2D source, Rect sourceRect)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = RenderTexture.GetTemporary(ImageSize, ImageSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                // The existing source sampler applies Repeat/Clamp/Mirror per axis without changing its importer.
                // Bake the whole measured domain, so triangles spanning integer UV seams stay continuous.
                Graphics.Blit(source, target, sourceRect.size, sourceRect.position);
                RenderTexture.active = target;
                readable = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                if (readable != null) Object.DestroyImmediate(readable);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Mesh RemapMesh(Mesh source, int[] slots, Dictionary<int, UvDomain> domains)
        {
            Vector3[] vertices = source.vertices;
            Vector2[] uv = source.uv;
            if (uv.Length != vertices.Length) throw new InvalidOperationException(source.name + ": complete UV0 is required.");
            var oldIndices = new List<int>();
            var atlasUv = new List<Vector2>();
            var triangles = new List<int>();
            for (int sub = 0; sub < slots.Length; sub++)
            {
                if (slots[sub] < 0) continue;
                // Empty submeshes may have no measured domain. They contribute no vertices or triangles.
                if (source.GetIndexCount(sub) == 0) continue;
                Rect rect = SlotRect(slots[sub]);
                UvDomain domain = domains[slots[sub]];
                Rect sourceRect = domain.sampleRect;
                var mapping = new Dictionary<int, int>();
                foreach (int old in source.GetTriangles(sub))
                {
                    if (old < 0 || old >= vertices.Length) throw new InvalidOperationException(source.name + ": invalid triangle index.");
                    if (!mapping.TryGetValue(old, out int index))
                    {
                        Vector2 v = uv[old];
                        if (!IsFiniteUv(v) || v.x < domain.min.x || v.x > domain.max.x || v.y < domain.min.y || v.y > domain.max.y)
                            throw new InvalidOperationException(source.name + ": UV0 changed after the input range scan.");
                        index = oldIndices.Count;
                        mapping.Add(old, index);
                        oldIndices.Add(old);
                        // Affine UV mapping preserves interpolation across triangles and integer wrap seams.
                        // No clamp/modulo here: the atlas already contains the source sampler's tiled/clamped result.
                        atlasUv.Add(new Vector2(rect.x + (v.x - sourceRect.x) / sourceRect.width * rect.width,
                            rect.y + (v.y - sourceRect.y) / sourceRect.height * rect.height));
                    }
                    triangles.Add(index);
                }
            }
            var result = new Mesh { name = source.name + "_Atlas", indexFormat = oldIndices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                result.vertices = Remap(vertices, oldIndices);
                if (source.normals.Length == vertices.Length) result.normals = Remap(source.normals, oldIndices);
                if (source.tangents.Length == vertices.Length) result.tangents = Remap(source.tangents, oldIndices);
                if (source.colors32.Length == vertices.Length) result.colors32 = Remap(source.colors32, oldIndices);
                result.SetUVs(0, atlasUv);
                for (int channel = 1; channel < 8; channel++)
                {
                    var values = new List<Vector4>();
                    source.GetUVs(channel, values);
                    if (values.Count == vertices.Length) result.SetUVs(channel, new List<Vector4>(Remap(values.ToArray(), oldIndices)));
                }
                result.bindposes = source.bindposes;
                CopyBoneWeights(source, result, oldIndices);
                result.SetTriangles(triangles, 0, false);
                if (source.normals.Length == 0) result.RecalculateNormals();
                if (source.tangents.Length == 0) result.RecalculateTangents();
                var deltaVertices = new Vector3[vertices.Length];
                var deltaNormals = new Vector3[vertices.Length];
                var deltaTangents = new Vector3[vertices.Length];
                for (int shape = 0; shape < source.blendShapeCount; shape++)
                    for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                    {
                        source.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                        result.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                            Remap(deltaVertices, oldIndices), Remap(deltaNormals, oldIndices), Remap(deltaTangents, oldIndices));
                    }
                result.bounds = source.bounds;
                return result;
            }
            catch { Object.DestroyImmediate(result); throw; }
        }

        private static void CopyBoneWeights(Mesh source, Mesh result, List<int> oldIndices)
        {
            // These two read-only arrays borrow Mesh storage; do not dispose them or modify the source mesh.
            var sourceCounts = source.GetBonesPerVertex();
            var sourceWeights = source.GetAllBoneWeights();
            if (sourceWeights.Length == 0) return;
            if (sourceCounts.Length != source.vertexCount) throw new InvalidOperationException(source.name + ": invalid bone weight counts.");
            var starts = new int[sourceCounts.Length + 1];
            for (int i = 0; i < sourceCounts.Length; i++) starts[i + 1] = starts[i] + sourceCounts[i];
            if (starts[sourceCounts.Length] != sourceWeights.Length) throw new InvalidOperationException(source.name + ": inconsistent bone weights.");
            int count = 0;
            foreach (int old in oldIndices) count += sourceCounts[old];
            var counts = new NativeArray<byte>(oldIndices.Count, Allocator.Temp);
            var weights = default(NativeArray<BoneWeight1>);
            try
            {
                weights = new NativeArray<BoneWeight1>(count, Allocator.Temp);
                int write = 0;
                for (int i = 0; i < oldIndices.Count; i++)
                {
                    int old = oldIndices[i];
                    counts[i] = sourceCounts[old];
                    for (int b = starts[old]; b < starts[old + 1]; b++) weights[write++] = sourceWeights[b];
                }
                result.SetBoneWeights(counts, weights);
            }
            finally
            {
                if (weights.IsCreated) weights.Dispose();
                counts.Dispose();
            }
        }

        private static T[] Remap<T>(T[] values, List<int> indices)
        {
            var output = new T[indices.Count];
            for (int i = 0; i < indices.Count; i++) output[i] = values[indices[i]];
            return output;
        }

        private static void RequireNewPath(string path)
        {
            if (File.Exists(path) || File.Exists(path + ".meta") || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new IOException("Preparation only creates new assets; path is occupied: " + path);
        }

        private static Texture2D SaveNewAtlasPng(Texture2D rawAtlas, string path, List<string> createdPaths)
        {
            RequireNewPath(path);
            byte[] png = ImageConversion.EncodeToPNG(rawAtlas);
            if (png == null || png.Length == 0) throw new IOException("Could not encode the generated UnityChan atlas as PNG.");
            // Track before writing so a partial write or failed import is also rolled back.
            createdPaths.Add(path);
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new IOException("PNG texture importer was not created: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.streamingMipmaps = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = AtlasSize;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.maxTextureSize = AtlasSize;
            defaults.format = TextureImporterFormat.RGBA32;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            defaults.crunchedCompression = false;
            importer.SetPlatformTextureSettings(defaults);
            // A project texture preset must not silently downsize/compress the Windows validation build.
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.name = "Standalone";
            standalone.overridden = true;
            standalone.maxTextureSize = AtlasSize;
            standalone.format = TextureImporterFormat.RGBA32;
            standalone.textureCompression = TextureImporterCompression.Uncompressed;
            standalone.crunchedCompression = false;
            importer.SetPlatformTextureSettings(standalone);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (atlas == null || !EditorUtility.IsPersistent(atlas) || atlas.width != AtlasSize || atlas.height != AtlasSize || atlas.mipmapCount <= 1)
                throw new IOException("The PNG atlas did not import as a persistent full-resolution mipmapped texture: " + path);
            Debug.Log("[UnityChan atlas PNG] " + path + " bytes=" + png.LongLength + " size=" + atlas.width + "x" + atlas.height +
                " mips=" + atlas.mipmapCount + " format=" + atlas.format + " (lossless PNG, uncompressed RGBA32 import)");
            return atlas;
        }

        private static void SaveNewAsset(Object asset, string path, List<string> createdPaths)
        {
            RequireNewPath(path);
            createdPaths.Add(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        private sealed class UvDomain
        {
            public Vector2 min, max;
            public Rect sampleRect;
        }

        private sealed class RendererPlan
        {
            public Renderer renderer;
            public Mesh source;
            public int[] slots;
            public RendererRecord record;
        }

        [Serializable] private sealed class PreparationReport
        {
            public string sourceModel, avatar, atlasPath, materialPath, prefabPath;
            public int atlasSize, paddingPixels, preparedVertexCount;
            public string[] simplifications;
            public List<RendererRecord> renderers = new List<RendererRecord>();
            public List<AtlasRecord> atlasSlots = new List<AtlasRecord>();
        }
        [Serializable] private sealed class RendererRecord
        {
            public string path, rendererType, sourceMesh, preparedMesh, omitted;
            public int sourceVertexCount, sourceSubmeshCount, preparedVertexCount, preparedSubmeshCount, blendShapeCount;
            public bool enabled, activeSelf;
            public List<MaterialRecord> materials = new List<MaterialRecord>();
        }
        [Serializable] private sealed class MaterialRecord
        {
            public int submesh, atlasSlot;
            public string material;
            public bool hasUvRange;
            public Vector2 uvMin, uvMax;
        }
        [Serializable] private sealed class AtlasRecord
        {
            public int slot, sourceWidth, sourceHeight, sampledCellSize;
            public string sourceTexture, wrapU, wrapV;
            public Vector2 sourceUvMin, sourceUvMax;
            public Rect uvRect, sampledSourceRect;
            public bool irisWhiteComposite;
        }
    }
}
