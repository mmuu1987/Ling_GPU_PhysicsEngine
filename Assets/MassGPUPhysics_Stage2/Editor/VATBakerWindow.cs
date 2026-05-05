using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace MassGPUPhysics
{
    public class VATBakerWindow : EditorWindow
    {
        private GameObject targetGameObject;
        private AnimationClip animationClip;
        private int targetFrameRate = 30;
        private string saveFolderName = "VAT_Data";
        private MeshRenderer[] extraRenderers = new MeshRenderer[0];
        private bool showExtraRenderers;

        [Header("Low LOD Bake")]
        private bool bakeLowLod = true;
        private float lowLodTriangleRatio = 0.25f;
        private int lowLodMaxVertices = 1200;
        private string lowLodSuffix = "_LowLOD";

        [MenuItem("MassGPUPhysics/VAT Baker")]
        public static void ShowWindow()
        {
            GetWindow<VATBakerWindow>("VAT Baker");
        }

        private void OnGUI()
        {
            GUILayout.Label("Vertex Animation Texture (VAT) Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            targetGameObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetGameObject, typeof(GameObject), true);
            animationClip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", animationClip, typeof(AnimationClip), false);
            targetFrameRate = EditorGUILayout.IntField("Target Frame Rate", targetFrameRate);
            saveFolderName = EditorGUILayout.TextField("Save Folder Name", saveFolderName);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Target GameObject must contain an Animator/Animation and a SkinnedMeshRenderer in its hierarchy.", MessageType.Info);

            EditorGUILayout.Space();
            GUILayout.Label("Low LOD VAT Bake", EditorStyles.boldLabel);
            bakeLowLod = EditorGUILayout.Toggle("Bake Low LOD", bakeLowLod);
            using (new EditorGUI.DisabledScope(!bakeLowLod))
            {
                lowLodTriangleRatio = EditorGUILayout.Slider("Vertex Keep Ratio", lowLodTriangleRatio, 0.02f, 1f);
                lowLodMaxVertices = EditorGUILayout.IntField("Max Vertices", lowLodMaxVertices);
                lowLodSuffix = EditorGUILayout.TextField("Asset Suffix", lowLodSuffix);
                EditorGUILayout.HelpBox(
                    "低模 VAT 使用顶点聚类减面：保留原始表面三角形覆盖关系，把相邻顶点合并成较少的代表顶点。\n" +
                    "相比抽掉三角面，这种方式不会把模型变成大片镂空面片，但细节和轮廓会随顶点数降低而变粗。",
                    MessageType.Info);
            }

            showExtraRenderers = EditorGUILayout.Foldout(showExtraRenderers, "Extra MeshRenderers (non-skinned)");
            if (showExtraRenderers)
            {
                EditorGUI.indentLevel++;
                int newSize = EditorGUILayout.IntField("Size", extraRenderers.Length);
                if (newSize != extraRenderers.Length)
                {
                    System.Array.Resize(ref extraRenderers, newSize);
                }
                for (int i = 0; i < extraRenderers.Length; i++)
                {
                    extraRenderers[i] = (MeshRenderer)EditorGUILayout.ObjectField(
                        $"Element {i}", extraRenderers[i], typeof(MeshRenderer), true);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.HelpBox("拖入需要烘焙的 MeshRenderer（需挂 MeshFilter）。常用于骨骼下的非蒙皮附件（如头部装饰、武器等）。", MessageType.Info);
            }

            if (GUILayout.Button("Bake VAT to Assets", GUILayout.Height(40)))
            {
                if (ValidateInputs())
                {
                    Bake();
                }
            }
        }

        private bool ValidateInputs()
        {
            if (targetGameObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a Target GameObject.", "OK");
                return false;
            }

            if (animationClip == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign an Animation Clip.", "OK");
                return false;
            }

            SkinnedMeshRenderer[] smrs = targetGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            bool hasValidExtra = false;
            foreach (var mr in extraRenderers)
            {
                if (mr != null && mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null)
                {
                    hasValidExtra = true;
                    break;
                }
            }

            if ((smrs == null || smrs.Length == 0) && !hasValidExtra)
            {
                EditorUtility.DisplayDialog("Error", "No SkinnedMeshRenderer found in the Target GameObject, and no valid MeshRenderer with MeshFilter in Extra Renderers.", "OK");
                return false;
            }

            lowLodTriangleRatio = Mathf.Clamp(lowLodTriangleRatio, 0.02f, 1f);
            lowLodMaxVertices = Mathf.Max(0, lowLodMaxVertices);

            return true;
        }

        private void Bake()
        {
            GameObject instObj = Instantiate(targetGameObject);
            instObj.transform.position = Vector3.zero;
            instObj.transform.rotation = Quaternion.identity;
            instObj.transform.localScale = Vector3.one;

            SkinnedMeshRenderer[] smrs = instObj.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // 记录 extraRenderers 在原始 hierarchy 中的路径，然后在 instObj 中查找对应副本
            string[] extraPaths = new string[extraRenderers.Length];
            for (int i = 0; i < extraRenderers.Length; i++)
            {
                if (extraRenderers[i] != null)
                {
                    string fullPath = GetHierarchyPath(extraRenderers[i].transform, targetGameObject.transform);
                    // Transform.Find 需要的是相对于 instObj 的子路径，去掉根节点名
                    int slashIdx = fullPath.IndexOf('/');
                    extraPaths[i] = slashIdx >= 0 ? fullPath.Substring(slashIdx + 1) : "";
                }
            }

            var instMRs = new System.Collections.Generic.List<MeshRenderer>();
            var instMFs = new System.Collections.Generic.List<MeshFilter>();
            foreach (string path in extraPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                Transform found = instObj.transform.Find(path);
                if (found != null)
                {
                    MeshRenderer mr = found.GetComponent<MeshRenderer>();
                    MeshFilter mf = found.GetComponent<MeshFilter>();
                    if (mr != null && mf != null && mf.sharedMesh != null)
                    {
                        instMRs.Add(mr);
                        instMFs.Add(mf);
                        Debug.Log($"[VAT Baker] 发现 MR: {found.name} (路径: {GetHierarchyPath(found, instObj.transform)}), 顶点数: {mf.sharedMesh.vertexCount}");
                    }
                    else
                    {
                        Debug.LogWarning($"[VAT Baker] MR 路径 {path} 上缺少 MeshFilter 或 sharedMesh，已跳过");
                    }
                }
                else
                {
                    Debug.LogWarning($"[VAT Baker] 在 instObj 中找不到路径: {path}");
                }
            }

            int totalVertices = 0;
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;
                totalVertices += smr.sharedMesh.vertexCount;
                Debug.Log($"[VAT Baker] 发现 SMR: {smr.name} (路径: {GetHierarchyPath(smr.transform, instObj.transform)}), 顶点数: {smr.sharedMesh.vertexCount}");
            }
            foreach (var mf in instMFs)
            {
                totalVertices += mf.sharedMesh.vertexCount;
            }

            if (totalVertices == 0)
            {
                EditorUtility.DisplayDialog("Error", "Total vertex count is 0. Check your SkinnedMeshRenderers and Extra MeshRenderers.", "OK");
                DestroyImmediate(instObj);
                return;
            }

            float animLength = animationClip.length;
            int frameCount = Mathf.CeilToInt(animLength * targetFrameRate);
            if (frameCount <= 0) frameCount = 1;

            int texWidth = Mathf.Min(totalVertices, 4096);
            int rowsPerFrame = Mathf.CeilToInt((float)totalVertices / texWidth);
            int texHeight = rowsPerFrame * frameCount;

            if (texHeight > 16384)
            {
                EditorUtility.DisplayDialog("Error", $"Animation too long or mesh too dense. Required texture height {texHeight} exceeds 16384.", "OK");
                DestroyImmediate(instObj);
                return;
            }

            Texture2D posTex = new Texture2D(texWidth, texHeight, TextureFormat.RGBAHalf, false, true);
            posTex.wrapMode = TextureWrapMode.Clamp;
            posTex.filterMode = FilterMode.Point;

            Texture2D normTex = new Texture2D(texWidth, texHeight, TextureFormat.RGBAHalf, false, true);
            normTex.wrapMode = TextureWrapMode.Clamp;
            normTex.filterMode = FilterMode.Point;

            Color[] posColors = new Color[texWidth * texHeight];
            Color[] normColors = new Color[texWidth * texHeight];

            EditorUtility.DisplayProgressBar("Baking VAT", "Sampling Frames...", 0f);

            Mesh tempBakedMesh = new Mesh();

            try
            {
                for (int f = 0; f < frameCount; f++)
                {
                    float t = (float)f / targetFrameRate;
                    if (t > animLength) t = animLength;

                    animationClip.SampleAnimation(instObj, t);

                    int vOffsetBake = 0;

                    foreach (var smr in smrs)
                    {
                        if (smr.sharedMesh == null) continue;

                        Matrix4x4 localToRootMatrix = instObj.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;

                        smr.BakeMesh(tempBakedMesh, true);

                        Vector3[] vBaked = tempBakedMesh.vertices;
                        Vector3[] nBaked = tempBakedMesh.normals;

                        for (int i = 0; i < vBaked.Length; i++)
                        {
                            int globalVertexIndex = vOffsetBake + i;
                            int vX = globalVertexIndex % texWidth;
                            int vY = (f * rowsPerFrame) + (globalVertexIndex / texWidth);
                            int pixelIndex = vY * texWidth + vX;

                            Vector3 vRootPos = localToRootMatrix.MultiplyPoint3x4(vBaked[i]);
                            posColors[pixelIndex] = new Color(vRootPos.x, vRootPos.y, vRootPos.z, 1.0f);

                            if (i < nBaked.Length)
                            {
                                Vector3 nRootDir = localToRootMatrix.MultiplyVector(nBaked[i]).normalized;
                                normColors[pixelIndex] = new Color(nRootDir.x, nRootDir.y, nRootDir.z, 1.0f);
                            }
                        }
                        vOffsetBake += vBaked.Length;
                    }

                    // 烘焙 MeshRenderer（非蒙皮）顶点
                    for (int i = 0; i < instMRs.Count; i++)
                    {
                        MeshFilter mf = instMFs[i];
                        MeshRenderer mr = instMRs[i];
                        Mesh mesh = mf.sharedMesh;
                        if (mesh == null) continue;

                        Matrix4x4 localToRootMatrix = instObj.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;

                        Vector3[] verts = mesh.vertices;
                        Vector3[] norms = mesh.normals;

                        for (int j = 0; j < verts.Length; j++)
                        {
                            int globalVertexIndex = vOffsetBake + j;
                            int vX = globalVertexIndex % texWidth;
                            int vY = (f * rowsPerFrame) + (globalVertexIndex / texWidth);
                            int pixelIndex = vY * texWidth + vX;

                            Vector3 vRootPos = localToRootMatrix.MultiplyPoint3x4(verts[j]);
                            posColors[pixelIndex] = new Color(vRootPos.x, vRootPos.y, vRootPos.z, 1.0f);

                            if (j < norms.Length)
                            {
                                Vector3 nRootDir = localToRootMatrix.MultiplyVector(norms[j]).normalized;
                                normColors[pixelIndex] = new Color(nRootDir.x, nRootDir.y, nRootDir.z, 1.0f);
                            }
                        }
                        vOffsetBake += verts.Length;
                    }

                    EditorUtility.DisplayProgressBar("Baking VAT", $"Sampling Frame {f + 1}/{frameCount}", (float)f / frameCount);
                }

                posTex.SetPixels(posColors);
                posTex.Apply();

                normTex.SetPixels(normColors);
                normTex.Apply();

                // 3. Create Clean Combined Mesh (SMR + MR)
                Vector3[] cleanVertices = new Vector3[totalVertices];
                Vector3[] cleanNormals = new Vector3[totalVertices];
                Vector2[] cleanUVs = new Vector2[totalVertices];
                var mergedTriangles = new System.Collections.Generic.List<int>();

                int vOffsetOrig = 0;

                foreach (var smr in smrs)
                {
                    Mesh sm = smr.sharedMesh;
                    if (sm == null) continue;

                    Matrix4x4 rootSpaceMat = instObj.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;

                    Vector3[] v = sm.vertices;
                    Vector3[] n = sm.normals;
                    Vector2[] uv = sm.uv;

                    for (int i = 0; i < v.Length; i++)
                    {
                        cleanVertices[vOffsetOrig + i] = rootSpaceMat.MultiplyPoint3x4(v[i]);
                        cleanNormals[vOffsetOrig + i] = rootSpaceMat.MultiplyVector(n[i]).normalized;
                        if (uv != null && uv.Length > i)
                        {
                            cleanUVs[vOffsetOrig + i] = uv[i];
                        }
                    }

                    for (int s = 0; s < sm.subMeshCount; s++)
                    {
                        int[] tris = sm.GetTriangles(s);
                        for (int t = 0; t < tris.Length; t++)
                        {
                            mergedTriangles.Add(tris[t] + vOffsetOrig);
                        }
                    }

                    vOffsetOrig += v.Length;
                }

                // 合并 MeshRenderer 网格
                for (int i = 0; i < instMRs.Count; i++)
                {
                    MeshRenderer mr = instMRs[i];
                    MeshFilter mf = instMFs[i];
                    Mesh sm = mf.sharedMesh;
                    if (sm == null) continue;

                    Matrix4x4 rootSpaceMat = instObj.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;

                    Vector3[] v = sm.vertices;
                    Vector3[] n = sm.normals;
                    Vector2[] uv = sm.uv;

                    for (int j = 0; j < v.Length; j++)
                    {
                        cleanVertices[vOffsetOrig + j] = rootSpaceMat.MultiplyPoint3x4(v[j]);
                        cleanNormals[vOffsetOrig + j] = rootSpaceMat.MultiplyVector(n[j]).normalized;
                        if (uv != null && uv.Length > j)
                        {
                            cleanUVs[vOffsetOrig + j] = uv[j];
                        }
                    }

                    for (int s = 0; s < sm.subMeshCount; s++)
                    {
                        int[] tris = sm.GetTriangles(s);
                        for (int t = 0; t < tris.Length; t++)
                        {
                            mergedTriangles.Add(tris[t] + vOffsetOrig);
                        }
                    }

                    vOffsetOrig += v.Length;
                }

                Mesh cleanMesh = new Mesh();
                cleanMesh.indexFormat = totalVertices > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                cleanMesh.vertices = cleanVertices;
                cleanMesh.normals = cleanNormals;
                if (totalVertices > 0 && cleanUVs[0] != null) cleanMesh.uv = cleanUVs;

                cleanMesh.subMeshCount = 1;
                cleanMesh.SetTriangles(mergedTriangles.ToArray(), 0);
                cleanMesh.RecalculateBounds();
                cleanMesh.RecalculateTangents();

                LowLodBakeResult lowLodResult = null;
                if (bakeLowLod)
                {
                    lowLodResult = BuildLowLodBakeResult(
                        cleanMesh,
                        posColors,
                        normColors,
                        texWidth,
                        rowsPerFrame,
                        frameCount);
                }

                // 4. Save Assets
                string folderPath = "Assets/" + saveFolderName;
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    AssetDatabase.CreateFolder("Assets", saveFolderName);
                }

                string baseName = targetGameObject.name.Replace("(Clone)", "") + "_" + animationClip.name;
                string validBaseName = SanitizeFileName(baseName);

                string posPath = folderPath + "/" + validBaseName + "_Pos.asset";
                string normPath = folderPath + "/" + validBaseName + "_Norm.asset";
                string cleanMeshPath = folderPath + "/" + SanitizeFileName(targetGameObject.name.Replace("(Clone)", "")) + "_CleanMesh.asset";

                AssetDatabase.CreateAsset(posTex, posPath);
                AssetDatabase.CreateAsset(normTex, normPath);
                AssetDatabase.CreateAsset(cleanMesh, cleanMeshPath);

                string lowLodStats = "Low LOD: disabled";
                if (lowLodResult != null)
                {
                    string validSuffix = SanitizeFileName(string.IsNullOrWhiteSpace(lowLodSuffix) ? "_LowLOD" : lowLodSuffix);
                    string lowPosPath = folderPath + "/" + validBaseName + validSuffix + "_Pos.asset";
                    string lowNormPath = folderPath + "/" + validBaseName + validSuffix + "_Norm.asset";
                    string lowMeshPath = folderPath + "/" + SanitizeFileName(targetGameObject.name.Replace("(Clone)", "")) + validSuffix + "_Mesh.asset";

                    AssetDatabase.CreateAsset(lowLodResult.positionTexture, lowPosPath);
                    AssetDatabase.CreateAsset(lowLodResult.normalTexture, lowNormPath);
                    AssetDatabase.CreateAsset(lowLodResult.mesh, lowMeshPath);

                    lowLodStats =
                        $"Low LOD Vertices: {lowLodResult.mesh.vertexCount}\n" +
                        $"Low LOD Triangles: {lowLodResult.mesh.triangles.Length / 3}\n" +
                        $"Low LOD Texture Width: {lowLodResult.textureWidth}\n" +
                        $"Low LOD Texture Height: {lowLodResult.textureHeight}\n" +
                        $"Low LOD Rows Per Frame: {lowLodResult.rowsPerFrame}";
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                int mrTotalVerts = 0;
                foreach (var mf in instMFs) mrTotalVerts += mf.sharedMesh.vertexCount;

                EditorUtility.DisplayDialog("Success",
                    $"Baking complete!\n\n" +
                    $"[Material Parameters needed]\n" +
                    $"VAT Target Texture Width: {texWidth}\n" +
                    $"VAT Target Texture Height: {texHeight}\n" +
                    $"VAT Rows Per Frame: {rowsPerFrame}\n\n" +
                    $"[Stats]\n" +
                    $"Frames: {frameCount}\n" +
                    $"SMRs: {smrs.Length}\n" +
                    $"MRs (extra): {instMRs.Count}\n" +
                    $"Total Vertices: {totalVertices} (SMR: {totalVertices - mrTotalVerts}, MR: {mrTotalVerts})\n\n" +
                    $"{lowLodStats}\n\n" +
                    $"Saved to: {folderPath}", "OK");

                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(posPath));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DestroyImmediate(instObj);
                DestroyImmediate(tempBakedMesh);
            }
        }

        private string SanitizeFileName(string fileName)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
        }

        private LowLodBakeResult BuildLowLodBakeResult(
            Mesh sourceMesh,
            Color[] fullPosColors,
            Color[] fullNormColors,
            int fullTexWidth,
            int fullRowsPerFrame,
            int frameCount)
        {
            int[] sourceTriangles = sourceMesh.triangles;
            int sourceTriangleCount = sourceTriangles.Length / 3;
            if (sourceTriangleCount == 0)
            {
                Debug.LogWarning("[VAT Baker] Low LOD skipped: source mesh has no triangles.");
                return null;
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] sourceNormals = sourceMesh.normals;
            Vector2[] sourceUVs = sourceMesh.uv;

            int targetVertexCount = Mathf.Max(4, Mathf.RoundToInt(sourceVertices.Length * lowLodTriangleRatio));
            if (lowLodMaxVertices > 0)
                targetVertexCount = Mathf.Min(targetVertexCount, lowLodMaxVertices);
            targetVertexCount = Mathf.Min(targetVertexCount, sourceVertices.Length);

            int clusterResolution = FindClusterResolution(sourceVertices, sourceMesh.bounds, targetVertexCount);
            int[] oldToNew = BuildVertexClusterMapping(
                sourceVertices,
                sourceNormals,
                sourceUVs,
                sourceMesh.bounds,
                clusterResolution,
                out List<LowLodCluster> clusters);

            var lowVertices = new List<Vector3>();
            var lowNormals = new List<Vector3>();
            var lowUVs = new List<Vector2>();

            for (int i = 0; i < clusters.Count; i++)
            {
                LowLodCluster cluster = clusters[i];
                float invCount = 1f / Mathf.Max(cluster.count, 1);
                lowVertices.Add(cluster.positionSum * invCount);

                Vector3 normal = cluster.normalSum.sqrMagnitude > 0.000001f
                    ? cluster.normalSum.normalized
                    : Vector3.up;
                lowNormals.Add(normal);

                lowUVs.Add(cluster.uvSum * invCount);
            }

            var lowTriangles = new List<int>();
            var usedTriangles = new HashSet<string>();

            for (int tri = 0; tri < sourceTriangleCount; tri++)
            {
                int a = oldToNew[sourceTriangles[tri * 3]];
                int b = oldToNew[sourceTriangles[tri * 3 + 1]];
                int c = oldToNew[sourceTriangles[tri * 3 + 2]];

                if (a == b || b == c || c == a)
                    continue;

                string key = MakeTriangleKey(a, b, c);
                if (!usedTriangles.Add(key))
                    continue;

                lowTriangles.Add(a);
                lowTriangles.Add(b);
                lowTriangles.Add(c);
            }

            if (lowVertices.Count == 0 || lowTriangles.Count == 0)
            {
                Debug.LogWarning("[VAT Baker] Low LOD skipped: reduction settings produced an empty mesh.");
                return null;
            }

            Mesh lowMesh = new Mesh
            {
                name = sourceMesh.name + "_LowLOD",
                indexFormat = lowVertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            lowMesh.SetVertices(lowVertices);
            lowMesh.SetNormals(lowNormals);
            lowMesh.SetUVs(0, lowUVs);
            lowMesh.SetTriangles(lowTriangles, 0);
            lowMesh.RecalculateBounds();
            lowMesh.RecalculateTangents();

            int lowVertexCount = lowVertices.Count;
            int lowTexWidth = Mathf.Min(lowVertexCount, 4096);
            int lowRowsPerFrame = Mathf.CeilToInt((float)lowVertexCount / lowTexWidth);
            int lowTexHeight = lowRowsPerFrame * frameCount;

            Texture2D lowPosTex = new Texture2D(lowTexWidth, lowTexHeight, TextureFormat.RGBAHalf, false, true);
            lowPosTex.wrapMode = TextureWrapMode.Clamp;
            lowPosTex.filterMode = FilterMode.Point;

            Texture2D lowNormTex = new Texture2D(lowTexWidth, lowTexHeight, TextureFormat.RGBAHalf, false, true);
            lowNormTex.wrapMode = TextureWrapMode.Clamp;
            lowNormTex.filterMode = FilterMode.Point;

            Color[] lowPosColors = new Color[lowTexWidth * lowTexHeight];
            Color[] lowNormColors = new Color[lowTexWidth * lowTexHeight];

            for (int f = 0; f < frameCount; f++)
            {
                for (int newIndex = 0; newIndex < lowVertexCount; newIndex++)
                {
                    int newX = newIndex % lowTexWidth;
                    int newY = f * lowRowsPerFrame + newIndex / lowTexWidth;
                    int newPixel = newY * lowTexWidth + newX;

                    lowPosColors[newPixel] = AverageClusterPositionColor(
                        clusters[newIndex], f, fullTexWidth, fullRowsPerFrame, fullPosColors);
                    lowNormColors[newPixel] = AverageClusterNormalColor(
                        clusters[newIndex], f, fullTexWidth, fullRowsPerFrame, fullNormColors);
                }
            }

            lowPosTex.SetPixels(lowPosColors);
            lowPosTex.Apply();

            lowNormTex.SetPixels(lowNormColors);
            lowNormTex.Apply();

            Debug.Log($"[VAT Baker] Low LOD generated by vertex clustering: {sourceMesh.vertexCount} -> {lowVertexCount} vertices, " +
                      $"{sourceTriangleCount} -> {lowTriangles.Count / 3} triangles, grid resolution {clusterResolution}.");

            return new LowLodBakeResult
            {
                mesh = lowMesh,
                positionTexture = lowPosTex,
                normalTexture = lowNormTex,
                textureWidth = lowTexWidth,
                textureHeight = lowTexHeight,
                rowsPerFrame = lowRowsPerFrame
            };
        }

        private static int FindClusterResolution(Vector3[] vertices, Bounds bounds, int targetVertexCount)
        {
            int bestResolution = 1;
            int bestCount = CountClusters(vertices, bounds, bestResolution);

            for (int resolution = 2; resolution <= 256; resolution++)
            {
                int count = CountClusters(vertices, bounds, resolution);

                if (count <= targetVertexCount && count > bestCount)
                {
                    bestCount = count;
                    bestResolution = resolution;
                }

                if (count > targetVertexCount)
                    break;
            }

            return bestResolution;
        }

        private static int CountClusters(Vector3[] vertices, Bounds bounds, int resolution)
        {
            var keys = new HashSet<Vector3Int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                keys.Add(QuantizeVertex(vertices[i], bounds, resolution));
            }
            return keys.Count;
        }

        private static int[] BuildVertexClusterMapping(
            Vector3[] sourceVertices,
            Vector3[] sourceNormals,
            Vector2[] sourceUVs,
            Bounds bounds,
            int resolution,
            out List<LowLodCluster> clusters)
        {
            clusters = new List<LowLodCluster>();
            var keyToCluster = new Dictionary<Vector3Int, int>();
            int[] oldToNew = new int[sourceVertices.Length];

            for (int oldIndex = 0; oldIndex < sourceVertices.Length; oldIndex++)
            {
                Vector3Int key = QuantizeVertex(sourceVertices[oldIndex], bounds, resolution);
                if (!keyToCluster.TryGetValue(key, out int clusterIndex))
                {
                    clusterIndex = clusters.Count;
                    keyToCluster.Add(key, clusterIndex);
                    clusters.Add(new LowLodCluster
                    {
                        oldIndices = new List<int>()
                    });
                }

                LowLodCluster cluster = clusters[clusterIndex];
                cluster.positionSum += sourceVertices[oldIndex];
                cluster.normalSum += sourceNormals != null && sourceNormals.Length > oldIndex ? sourceNormals[oldIndex] : Vector3.up;
                cluster.uvSum += sourceUVs != null && sourceUVs.Length > oldIndex ? sourceUVs[oldIndex] : Vector2.zero;
                cluster.count++;
                cluster.oldIndices.Add(oldIndex);
                clusters[clusterIndex] = cluster;

                oldToNew[oldIndex] = clusterIndex;
            }

            return oldToNew;
        }

        private static Vector3Int QuantizeVertex(Vector3 vertex, Bounds bounds, int resolution)
        {
            Vector3 size = bounds.size;
            size.x = Mathf.Max(size.x, 0.0001f);
            size.y = Mathf.Max(size.y, 0.0001f);
            size.z = Mathf.Max(size.z, 0.0001f);

            Vector3 normalized = new Vector3(
                (vertex.x - bounds.min.x) / size.x,
                (vertex.y - bounds.min.y) / size.y,
                (vertex.z - bounds.min.z) / size.z);

            int maxCell = Mathf.Max(0, resolution - 1);
            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(normalized.x * resolution), 0, maxCell),
                Mathf.Clamp(Mathf.FloorToInt(normalized.y * resolution), 0, maxCell),
                Mathf.Clamp(Mathf.FloorToInt(normalized.z * resolution), 0, maxCell));
        }

        private static string MakeTriangleKey(int a, int b, int c)
        {
            if (a > b) Swap(ref a, ref b);
            if (b > c) Swap(ref b, ref c);
            if (a > b) Swap(ref a, ref b);
            return $"{a}_{b}_{c}";
        }

        private static void Swap(ref int a, ref int b)
        {
            int temp = a;
            a = b;
            b = temp;
        }

        private static Color AverageClusterPositionColor(
            LowLodCluster cluster,
            int frame,
            int fullTexWidth,
            int fullRowsPerFrame,
            Color[] fullPosColors)
        {
            Vector4 sum = Vector4.zero;
            for (int i = 0; i < cluster.oldIndices.Count; i++)
            {
                Color color = ReadFullVatColor(cluster.oldIndices[i], frame, fullTexWidth, fullRowsPerFrame, fullPosColors);
                sum += new Vector4(color.r, color.g, color.b, color.a);
            }

            float invCount = 1f / Mathf.Max(cluster.oldIndices.Count, 1);
            sum *= invCount;
            return new Color(sum.x, sum.y, sum.z, sum.w);
        }

        private static Color AverageClusterNormalColor(
            LowLodCluster cluster,
            int frame,
            int fullTexWidth,
            int fullRowsPerFrame,
            Color[] fullNormColors)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < cluster.oldIndices.Count; i++)
            {
                Color color = ReadFullVatColor(cluster.oldIndices[i], frame, fullTexWidth, fullRowsPerFrame, fullNormColors);
                sum += new Vector3(color.r, color.g, color.b);
            }

            Vector3 normal = sum.sqrMagnitude > 0.000001f ? sum.normalized : Vector3.up;
            return new Color(normal.x, normal.y, normal.z, 1f);
        }

        private static Color ReadFullVatColor(
            int oldIndex,
            int frame,
            int fullTexWidth,
            int fullRowsPerFrame,
            Color[] colors)
        {
            int oldX = oldIndex % fullTexWidth;
            int oldY = frame * fullRowsPerFrame + oldIndex / fullTexWidth;
            return colors[oldY * fullTexWidth + oldX];
        }

        private struct LowLodCluster
        {
            public Vector3 positionSum;
            public Vector3 normalSum;
            public Vector2 uvSum;
            public int count;
            public List<int> oldIndices;
        }

        private class LowLodBakeResult
        {
            public Mesh mesh;
            public Texture2D positionTexture;
            public Texture2D normalTexture;
            public int textureWidth;
            public int textureHeight;
            public int rowsPerFrame;
        }

        private string GetHierarchyPath(Transform current, Transform root)
        {
            if (current == root) return current.name;
            return GetHierarchyPath(current.parent, root) + "/" + current.name;
        }
    }
}
