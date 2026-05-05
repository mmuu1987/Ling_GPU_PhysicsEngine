using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassGPUPhysics.Stage5
{
    public class VATBakerWindow_Stage5 : EditorWindow
    {
        private const int MaxTextureSize = 16384;
        private const int MaxTextureWidth = 4096;

        private GameObject targetGameObject;
        private int targetFrameRate = 30;
        private string saveFolderName = "VAT_Data";
        private MeshRenderer[] extraRenderers = new MeshRenderer[0];
        private bool showExtraRenderers;

        private ClipBakeSlot idleSlot = new ClipBakeSlot("Idle", true);
        private ClipBakeSlot moveSlot = new ClipBakeSlot("Move", true);
        private ClipBakeSlot attackSlot = new ClipBakeSlot("Attack", true);
        private ClipBakeSlot deathSlot = new ClipBakeSlot("Death", false);

        [Header("Low LOD Bake")]
        private bool bakeLowLod = true;
        private float lowLodTriangleRatio = 0.25f;
        private int lowLodMaxVertices = 1200;
        private string lowLodSuffix = "_LowLOD";

        [MenuItem("MassGPUPhysics/Stage5/VAT Baker")]
        public static void ShowWindow()
        {
            GetWindow<VATBakerWindow_Stage5>("VAT Baker Stage5");
        }

        private void OnGUI()
        {
            GUILayout.Label("Stage5 Multi-Clip Vertex Animation Texture Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            targetGameObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetGameObject, typeof(GameObject), true);
            targetFrameRate = Mathf.Max(1, EditorGUILayout.IntField("Target Frame Rate", targetFrameRate));
            saveFolderName = EditorGUILayout.TextField("Save Folder Name", saveFolderName);

            EditorGUILayout.Space();
            DrawClipSlot(idleSlot);
            DrawClipSlot(moveSlot);
            DrawClipSlot(attackSlot);
            DrawClipSlot(deathSlot);
            EditorGUILayout.HelpBox("Only Move is required. Empty Idle/Attack/Death slots reuse Move and emit a warning.", MessageType.Info);

            EditorGUILayout.Space();
            GUILayout.Label("Low LOD VAT Bake", EditorStyles.boldLabel);
            bakeLowLod = EditorGUILayout.Toggle("Bake Low LOD", bakeLowLod);
            using (new EditorGUI.DisabledScope(!bakeLowLod))
            {
                lowLodTriangleRatio = EditorGUILayout.Slider("Vertex Keep Ratio", lowLodTriangleRatio, 0.02f, 1f);
                lowLodMaxVertices = Mathf.Max(0, EditorGUILayout.IntField("Max Vertices", lowLodMaxVertices));
                lowLodSuffix = EditorGUILayout.TextField("Asset Suffix", lowLodSuffix);
            }

            showExtraRenderers = EditorGUILayout.Foldout(showExtraRenderers, "Extra MeshRenderers (non-skinned)");
            if (showExtraRenderers)
                DrawExtraRenderers();

            EditorGUILayout.Space();
            if (GUILayout.Button("Bake Multi-Clip VAT to Assets", GUILayout.Height(40)) && ValidateInputs())
                Bake();
        }

        private static void DrawClipSlot(ClipBakeSlot slot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(slot.label, EditorStyles.boldLabel);
                slot.clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", slot.clip, typeof(AnimationClip), false);
                slot.loop = EditorGUILayout.Toggle("Loop", slot.loop);
                slot.useCustomFrameRate = EditorGUILayout.Toggle("Custom Frame Rate", slot.useCustomFrameRate);
                using (new EditorGUI.DisabledScope(!slot.useCustomFrameRate))
                    slot.frameRateOverride = Mathf.Max(1, EditorGUILayout.IntField("Frame Rate Override", slot.frameRateOverride));
            }
        }

        private void DrawExtraRenderers()
        {
            EditorGUI.indentLevel++;
            int newSize = Mathf.Max(0, EditorGUILayout.IntField("Size", extraRenderers.Length));
            if (newSize != extraRenderers.Length)
                System.Array.Resize(ref extraRenderers, newSize);

            for (int i = 0; i < extraRenderers.Length; i++)
                extraRenderers[i] = (MeshRenderer)EditorGUILayout.ObjectField($"Element {i}", extraRenderers[i], typeof(MeshRenderer), true);

            EditorGUI.indentLevel--;
            EditorGUILayout.HelpBox("Extra MeshRenderers need a MeshFilter with a shared mesh. Use this for weapons or other non-skinned attachments.", MessageType.Info);
        }

        private bool ValidateInputs()
        {
            if (targetGameObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a Target GameObject.", "OK");
                return false;
            }

            if (moveSlot.clip == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign at least the Move clip. Idle/Attack/Death can reuse Move.", "OK");
                return false;
            }

            SkinnedMeshRenderer[] smrs = targetGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            bool hasValidExtra = extraRenderers.Any(mr => mr != null && mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null);
            if ((smrs == null || smrs.Length == 0) && !hasValidExtra)
            {
                EditorUtility.DisplayDialog("Error", "No SkinnedMeshRenderer found in the Target GameObject, and no valid Extra MeshRenderer found.", "OK");
                return false;
            }

            lowLodTriangleRatio = Mathf.Clamp(lowLodTriangleRatio, 0.02f, 1f);
            lowLodMaxVertices = Mathf.Max(0, lowLodMaxVertices);
            return true;
        }

        private void Bake()
        {
            ClipBakeInfo[] clips = BuildClipInfos();
            int totalFrameCount = clips.Sum(c => c.frameCount);
            WarnAboutFallbackClips(clips);

            GameObject instObj = Instantiate(targetGameObject);
            instObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instObj.transform.localScale = Vector3.one;

            SkinnedMeshRenderer[] smrs = instObj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            List<MeshRenderer> instMRs;
            List<MeshFilter> instMFs;
            FindInstanceExtraRenderers(instObj, out instMRs, out instMFs);

            int totalVertices = CountVertices(smrs, instMFs);
            if (totalVertices == 0)
            {
                EditorUtility.DisplayDialog("Error", "Total vertex count is 0. Check your renderers.", "OK");
                DestroyImmediate(instObj);
                return;
            }

            int texWidth = Mathf.Min(totalVertices, MaxTextureWidth);
            int rowsPerFrame = Mathf.CeilToInt((float)totalVertices / texWidth);
            int texHeight = rowsPerFrame * totalFrameCount;
            if (texHeight > MaxTextureSize)
            {
                EditorUtility.DisplayDialog("Error", $"Required texture height {texHeight} exceeds {MaxTextureSize}. Lower frame rate, shorten clips, or reduce mesh density.", "OK");
                DestroyImmediate(instObj);
                return;
            }

            Texture2D posTex = CreateVatTexture(texWidth, texHeight, "Stage5 Multi-Clip VAT Pos");
            Texture2D normTex = CreateVatTexture(texWidth, texHeight, "Stage5 Multi-Clip VAT Norm");
            Color[] posColors = new Color[texWidth * texHeight];
            Color[] normColors = new Color[texWidth * texHeight];

            Mesh tempBakedMesh = new Mesh();
            try
            {
                BakeClipFrames(instObj, smrs, instMRs, instMFs, clips, texWidth, rowsPerFrame, totalFrameCount, posColors, normColors, tempBakedMesh);

                posTex.SetPixels(posColors);
                posTex.Apply();
                normTex.SetPixels(normColors);
                normTex.Apply();

                Mesh cleanMesh = BuildCleanMesh(instObj, smrs, instMRs, instMFs, totalVertices);
                LowLodBakeResult lowLodResult = bakeLowLod
                    ? BuildLowLodBakeResult(cleanMesh, posColors, normColors, texWidth, rowsPerFrame, totalFrameCount)
                    : null;

                SaveAssets(posTex, normTex, cleanMesh, lowLodResult, clips, texWidth, texHeight, rowsPerFrame, totalFrameCount, smrs.Length, instMRs, totalVertices);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DestroyImmediate(instObj);
                DestroyImmediate(tempBakedMesh);
            }
        }

        private ClipBakeInfo[] BuildClipInfos()
        {
            ClipBakeInfo[] clips =
            {
                CreateClipInfo(idleSlot, moveSlot.clip, 0),
                CreateClipInfo(moveSlot, moveSlot.clip, 0),
                CreateClipInfo(attackSlot, moveSlot.clip, 0),
                CreateClipInfo(deathSlot, moveSlot.clip, 0)
            };

            int start = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].startFrame = start;
                start += clips[i].frameCount;
            }

            return clips;
        }

        private ClipBakeInfo CreateClipInfo(ClipBakeSlot slot, AnimationClip fallbackClip, int startFrame)
        {
            AnimationClip clip = slot.clip != null ? slot.clip : fallbackClip;
            int frameRate = slot.useCustomFrameRate ? Mathf.Max(1, slot.frameRateOverride) : Mathf.Max(1, targetFrameRate);
            int frameCount = Mathf.Max(1, Mathf.CeilToInt(clip.length * frameRate));
            return new ClipBakeInfo
            {
                label = slot.label,
                clip = clip,
                loop = slot.loop,
                frameRate = frameRate,
                frameCount = frameCount,
                startFrame = startFrame,
                usedFallback = slot.clip == null
            };
        }

        private static void WarnAboutFallbackClips(ClipBakeInfo[] clips)
        {
            foreach (ClipBakeInfo clip in clips)
            {
                if (clip.usedFallback)
                    Debug.LogWarning($"[VAT Baker Stage5] {clip.label} clip is empty, reusing Move clip '{clip.clip.name}'.");
            }
        }

        private void FindInstanceExtraRenderers(GameObject instObj, out List<MeshRenderer> instMRs, out List<MeshFilter> instMFs)
        {
            instMRs = new List<MeshRenderer>();
            instMFs = new List<MeshFilter>();

            foreach (MeshRenderer original in extraRenderers)
            {
                if (original == null)
                    continue;

                if (!original.transform.IsChildOf(targetGameObject.transform))
                {
                    Debug.LogWarning($"[VAT Baker Stage5] Extra MeshRenderer '{original.name}' is not under the target hierarchy and was skipped.");
                    continue;
                }

                string fullPath = GetHierarchyPath(original.transform, targetGameObject.transform);
                int slashIdx = fullPath.IndexOf('/');
                string relativePath = slashIdx >= 0 ? fullPath.Substring(slashIdx + 1) : string.Empty;
                Transform found = string.IsNullOrEmpty(relativePath) ? instObj.transform : instObj.transform.Find(relativePath);
                if (found == null)
                {
                    Debug.LogWarning($"[VAT Baker Stage5] Extra MeshRenderer path not found in instance: {relativePath}");
                    continue;
                }

                MeshRenderer mr = found.GetComponent<MeshRenderer>();
                MeshFilter mf = found.GetComponent<MeshFilter>();
                if (mr == null || mf == null || mf.sharedMesh == null)
                {
                    Debug.LogWarning($"[VAT Baker Stage5] Extra MeshRenderer '{relativePath}' has no valid MeshFilter/sharedMesh.");
                    continue;
                }

                instMRs.Add(mr);
                instMFs.Add(mf);
            }
        }

        private static int CountVertices(SkinnedMeshRenderer[] smrs, List<MeshFilter> meshFilters)
        {
            int total = 0;
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                if (smr.sharedMesh != null)
                    total += smr.sharedMesh.vertexCount;
            }

            foreach (MeshFilter mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                    total += mf.sharedMesh.vertexCount;
            }

            return total;
        }

        private static Texture2D CreateVatTexture(int width, int height, string name)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            return texture;
        }

        private static void BakeClipFrames(
            GameObject instObj,
            SkinnedMeshRenderer[] smrs,
            List<MeshRenderer> instMRs,
            List<MeshFilter> instMFs,
            ClipBakeInfo[] clips,
            int texWidth,
            int rowsPerFrame,
            int totalFrameCount,
            Color[] posColors,
            Color[] normColors,
            Mesh tempBakedMesh)
        {
            int completedFrames = 0;
            foreach (ClipBakeInfo clipInfo in clips)
            {
                for (int localFrame = 0; localFrame < clipInfo.frameCount; localFrame++)
                {
                    float t = GetClipSampleTime(clipInfo, localFrame);
                    int globalFrame = clipInfo.startFrame + localFrame;
                    clipInfo.clip.SampleAnimation(instObj, t);
                    BakeFrame(instObj, smrs, instMRs, instMFs, texWidth, rowsPerFrame, globalFrame, posColors, normColors, tempBakedMesh);

                    completedFrames++;
                    EditorUtility.DisplayProgressBar(
                        "Baking Stage5 Multi-Clip VAT",
                        $"{clipInfo.label} {localFrame + 1}/{clipInfo.frameCount}",
                        (float)completedFrames / totalFrameCount);
                }
            }
        }

        private static float GetClipSampleTime(ClipBakeInfo clipInfo, int localFrame)
        {
            if (!clipInfo.loop && clipInfo.frameCount > 1)
                return Mathf.Lerp(0f, clipInfo.clip.length, (float)localFrame / (clipInfo.frameCount - 1));

            return Mathf.Min((float)localFrame / clipInfo.frameRate, clipInfo.clip.length);
        }

        private static void BakeFrame(
            GameObject instObj,
            SkinnedMeshRenderer[] smrs,
            List<MeshRenderer> instMRs,
            List<MeshFilter> instMFs,
            int texWidth,
            int rowsPerFrame,
            int frame,
            Color[] posColors,
            Color[] normColors,
            Mesh tempBakedMesh)
        {
            int vertexOffset = 0;
            foreach (SkinnedMeshRenderer smr in smrs)
            {
                if (smr.sharedMesh == null)
                    continue;

                Matrix4x4 localToRootMatrix = instObj.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                smr.BakeMesh(tempBakedMesh, true);
                Vector3[] vertices = tempBakedMesh.vertices;
                Vector3[] normals = tempBakedMesh.normals;
                WriteVatVertices(vertices, normals, localToRootMatrix, vertexOffset, texWidth, rowsPerFrame, frame, posColors, normColors);
                vertexOffset += vertices.Length;
            }

            for (int i = 0; i < instMRs.Count; i++)
            {
                Mesh mesh = instMFs[i].sharedMesh;
                if (mesh == null)
                    continue;

                Matrix4x4 localToRootMatrix = instObj.transform.worldToLocalMatrix * instMRs[i].transform.localToWorldMatrix;
                WriteVatVertices(mesh.vertices, mesh.normals, localToRootMatrix, vertexOffset, texWidth, rowsPerFrame, frame, posColors, normColors);
                vertexOffset += mesh.vertexCount;
            }
        }

        private static void WriteVatVertices(
            Vector3[] vertices,
            Vector3[] normals,
            Matrix4x4 localToRootMatrix,
            int vertexOffset,
            int texWidth,
            int rowsPerFrame,
            int frame,
            Color[] posColors,
            Color[] normColors)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                int globalVertexIndex = vertexOffset + i;
                int x = globalVertexIndex % texWidth;
                int y = frame * rowsPerFrame + globalVertexIndex / texWidth;
                int pixelIndex = y * texWidth + x;

                Vector3 rootPos = localToRootMatrix.MultiplyPoint3x4(vertices[i]);
                posColors[pixelIndex] = new Color(rootPos.x, rootPos.y, rootPos.z, 1f);

                Vector3 normal = normals != null && normals.Length > i ? normals[i] : Vector3.up;
                Vector3 rootNormal = localToRootMatrix.MultiplyVector(normal).normalized;
                normColors[pixelIndex] = new Color(rootNormal.x, rootNormal.y, rootNormal.z, 1f);
            }
        }

        private static Mesh BuildCleanMesh(GameObject instObj, SkinnedMeshRenderer[] smrs, List<MeshRenderer> instMRs, List<MeshFilter> instMFs, int totalVertices)
        {
            Vector3[] cleanVertices = new Vector3[totalVertices];
            Vector3[] cleanNormals = new Vector3[totalVertices];
            Vector2[] cleanUVs = new Vector2[totalVertices];
            List<int> mergedTriangles = new List<int>();
            int vertexOffset = 0;

            foreach (SkinnedMeshRenderer smr in smrs)
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;

                Matrix4x4 rootSpaceMatrix = instObj.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                CopyMeshToCleanArrays(mesh, rootSpaceMatrix, vertexOffset, cleanVertices, cleanNormals, cleanUVs, mergedTriangles);
                vertexOffset += mesh.vertexCount;
            }

            for (int i = 0; i < instMRs.Count; i++)
            {
                Mesh mesh = instMFs[i].sharedMesh;
                if (mesh == null)
                    continue;

                Matrix4x4 rootSpaceMatrix = instObj.transform.worldToLocalMatrix * instMRs[i].transform.localToWorldMatrix;
                CopyMeshToCleanArrays(mesh, rootSpaceMatrix, vertexOffset, cleanVertices, cleanNormals, cleanUVs, mergedTriangles);
                vertexOffset += mesh.vertexCount;
            }

            Mesh cleanMesh = new Mesh
            {
                name = instObj.name.Replace("(Clone)", "") + "_CleanMesh",
                indexFormat = totalVertices > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            cleanMesh.vertices = cleanVertices;
            cleanMesh.normals = cleanNormals;
            cleanMesh.uv = cleanUVs;
            cleanMesh.subMeshCount = 1;
            cleanMesh.SetTriangles(mergedTriangles.ToArray(), 0);
            cleanMesh.RecalculateBounds();
            cleanMesh.RecalculateTangents();
            return cleanMesh;
        }

        private static void CopyMeshToCleanArrays(
            Mesh mesh,
            Matrix4x4 rootSpaceMatrix,
            int vertexOffset,
            Vector3[] cleanVertices,
            Vector3[] cleanNormals,
            Vector2[] cleanUVs,
            List<int> mergedTriangles)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;

            for (int i = 0; i < vertices.Length; i++)
            {
                cleanVertices[vertexOffset + i] = rootSpaceMatrix.MultiplyPoint3x4(vertices[i]);
                Vector3 normal = normals != null && normals.Length > i ? normals[i] : Vector3.up;
                cleanNormals[vertexOffset + i] = rootSpaceMatrix.MultiplyVector(normal).normalized;
                cleanUVs[vertexOffset + i] = uvs != null && uvs.Length > i ? uvs[i] : Vector2.zero;
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int t = 0; t < triangles.Length; t++)
                    mergedTriangles.Add(triangles[t] + vertexOffset);
            }
        }

        private void SaveAssets(
            Texture2D posTex,
            Texture2D normTex,
            Mesh cleanMesh,
            LowLodBakeResult lowLodResult,
            ClipBakeInfo[] clips,
            int texWidth,
            int texHeight,
            int rowsPerFrame,
            int totalFrameCount,
            int smrCount,
            List<MeshRenderer> instMRs,
            int totalVertices)
        {
            string folderPath = "Assets/" + saveFolderName;
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets", saveFolderName);

            string baseName = SanitizeFileName(targetGameObject.name.Replace("(Clone)", "") + "_Stage5_MultiClip");
            string posPath = folderPath + "/" + baseName + "_Pos.asset";
            string normPath = folderPath + "/" + baseName + "_Norm.asset";
            string cleanMeshPath = folderPath + "/" + SanitizeFileName(targetGameObject.name.Replace("(Clone)", "")) + "_CleanMesh.asset";
            string profilePath = folderPath + "/" + baseName + "_Profile.asset";

            CreateOrReplaceAsset(posTex, posPath);
            CreateOrReplaceAsset(normTex, normPath);
            CreateOrReplaceAsset(cleanMesh, cleanMeshPath);

            string lowLodStats = "Low LOD: disabled";
            if (lowLodResult != null)
            {
                string validSuffix = SanitizeFileName(string.IsNullOrWhiteSpace(lowLodSuffix) ? "_LowLOD" : lowLodSuffix);
                string lowPosPath = folderPath + "/" + baseName + validSuffix + "_Pos.asset";
                string lowNormPath = folderPath + "/" + baseName + validSuffix + "_Norm.asset";
                string lowMeshPath = folderPath + "/" + SanitizeFileName(targetGameObject.name.Replace("(Clone)", "")) + validSuffix + "_Mesh.asset";

                CreateOrReplaceAsset(lowLodResult.positionTexture, lowPosPath);
                CreateOrReplaceAsset(lowLodResult.normalTexture, lowNormPath);
                CreateOrReplaceAsset(lowLodResult.mesh, lowMeshPath);

                lowLodStats =
                    $"Low LOD Vertices: {lowLodResult.mesh.vertexCount}\n" +
                    $"Low LOD Triangles: {lowLodResult.mesh.triangles.Length / 3}\n" +
                    $"Low LOD Texture Width: {lowLodResult.textureWidth}\n" +
                    $"Low LOD Texture Height: {lowLodResult.textureHeight}\n" +
                    $"Low LOD Rows Per Frame: {lowLodResult.rowsPerFrame}";
            }

            VATProfile_Stage5 profile = CreateProfile(posTex, normTex, cleanMesh, lowLodResult, clips, texWidth, texHeight, rowsPerFrame, totalFrameCount);
            CreateOrReplaceAsset(profile, profilePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Stage5 VAT Bake Complete",
                $"Baking complete.\n\n" +
                $"_VATFrameCount: {totalFrameCount}\n" +
                $"_VATFrameRate: {targetFrameRate}\n" +
                $"_VATTexWidth: {texWidth}\n" +
                $"_VATTexHeight: {texHeight}\n" +
                $"_VATRowsPerFrame: {rowsPerFrame}\n\n" +
                BuildClipWindowSummary(clips) + "\n" +
                $"SMRs: {smrCount}\n" +
                $"MRs (extra): {instMRs.Count}\n" +
                $"Total Vertices: {totalVertices}\n\n" +
                $"{lowLodStats}\n\n" +
                $"Drag the generated VAT Profile onto GPUInstancingManager_Stage5.\n" +
                $"Profile: {profilePath}\n\n" +
                $"Saved to: {folderPath}",
                "OK");

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(profilePath));
        }

        private VATProfile_Stage5 CreateProfile(
            Texture2D posTex,
            Texture2D normTex,
            Mesh cleanMesh,
            LowLodBakeResult lowLodResult,
            ClipBakeInfo[] clips,
            int texWidth,
            int texHeight,
            int rowsPerFrame,
            int totalFrameCount)
        {
            VATProfile_Stage5 profile = CreateInstance<VATProfile_Stage5>();
            profile.cleanMesh = cleanMesh;
            profile.positionTexture = posTex;
            profile.normalTexture = normTex;
            profile.textureWidth = texWidth;
            profile.textureHeight = texHeight;
            profile.rowsPerFrame = rowsPerFrame;
            profile.totalFrameCount = totalFrameCount;
            profile.frameRate = targetFrameRate;
            profile.idle = ToProfileWindow(clips[0]);
            profile.move = ToProfileWindow(clips[1]);
            profile.attack = ToProfileWindow(clips[2]);
            profile.death = ToProfileWindow(clips[3]);

            if (lowLodResult != null)
            {
                profile.lowLodMesh = lowLodResult.mesh;
                profile.lowLodPositionTexture = lowLodResult.positionTexture;
                profile.lowLodNormalTexture = lowLodResult.normalTexture;
                profile.lowLodTextureWidth = lowLodResult.textureWidth;
                profile.lowLodTextureHeight = lowLodResult.textureHeight;
                profile.lowLodRowsPerFrame = lowLodResult.rowsPerFrame;
            }

            return profile;
        }

        private static VATProfile_Stage5.VATClipWindow ToProfileWindow(ClipBakeInfo clip)
        {
            return new VATProfile_Stage5.VATClipWindow
            {
                label = clip.label,
                startFrame = clip.startFrame,
                frameCount = clip.frameCount,
                frameRate = clip.frameRate,
                loop = clip.loop
            };
        }

        private static string BuildClipWindowSummary(ClipBakeInfo[] clips)
        {
            return string.Join("\n", clips.Select(c =>
                $"_{c.label}ClipStartFrame: {c.startFrame}, _{c.label}ClipFrameCount: {c.frameCount}, _{c.label}ClipFrameRate: {c.frameRate}"));
        }

        private static void CreateOrReplaceAsset(UnityEngine.Object asset, string path)
        {
            UnityEngine.Object oldAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (oldAsset != null)
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(asset, path);
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
                Debug.LogWarning("[VAT Baker Stage5] Low LOD skipped: source mesh has no triangles.");
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
            int[] oldToNew = BuildVertexClusterMapping(sourceVertices, sourceNormals, sourceUVs, sourceMesh.bounds, clusterResolution, out List<LowLodCluster> clusters);

            List<Vector3> lowVertices = new List<Vector3>();
            List<Vector3> lowNormals = new List<Vector3>();
            List<Vector2> lowUVs = new List<Vector2>();

            for (int i = 0; i < clusters.Count; i++)
            {
                LowLodCluster cluster = clusters[i];
                float invCount = 1f / Mathf.Max(cluster.count, 1);
                lowVertices.Add(cluster.positionSum * invCount);
                lowNormals.Add(cluster.normalSum.sqrMagnitude > 0.000001f ? cluster.normalSum.normalized : Vector3.up);
                lowUVs.Add(cluster.uvSum * invCount);
            }

            List<int> lowTriangles = new List<int>();
            HashSet<string> usedTriangles = new HashSet<string>();
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
                Debug.LogWarning("[VAT Baker Stage5] Low LOD skipped: reduction settings produced an empty mesh.");
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
            int lowTexWidth = Mathf.Min(lowVertexCount, MaxTextureWidth);
            int lowRowsPerFrame = Mathf.CeilToInt((float)lowVertexCount / lowTexWidth);
            int lowTexHeight = lowRowsPerFrame * frameCount;
            if (lowTexHeight > MaxTextureSize)
            {
                Debug.LogWarning($"[VAT Baker Stage5] Low LOD skipped: texture height {lowTexHeight} exceeds {MaxTextureSize}.");
                return null;
            }

            Texture2D lowPosTex = CreateVatTexture(lowTexWidth, lowTexHeight, "Stage5 Low LOD VAT Pos");
            Texture2D lowNormTex = CreateVatTexture(lowTexWidth, lowTexHeight, "Stage5 Low LOD VAT Norm");
            Color[] lowPosColors = new Color[lowTexWidth * lowTexHeight];
            Color[] lowNormColors = new Color[lowTexWidth * lowTexHeight];

            for (int f = 0; f < frameCount; f++)
            {
                for (int newIndex = 0; newIndex < lowVertexCount; newIndex++)
                {
                    int newX = newIndex % lowTexWidth;
                    int newY = f * lowRowsPerFrame + newIndex / lowTexWidth;
                    int newPixel = newY * lowTexWidth + newX;
                    lowPosColors[newPixel] = AverageClusterPositionColor(clusters[newIndex], f, fullTexWidth, fullRowsPerFrame, fullPosColors);
                    lowNormColors[newPixel] = AverageClusterNormalColor(clusters[newIndex], f, fullTexWidth, fullRowsPerFrame, fullNormColors);
                }
            }

            lowPosTex.SetPixels(lowPosColors);
            lowPosTex.Apply();
            lowNormTex.SetPixels(lowNormColors);
            lowNormTex.Apply();

            Debug.Log($"[VAT Baker Stage5] Low LOD generated: {sourceMesh.vertexCount} -> {lowVertexCount} vertices, {sourceTriangleCount} -> {lowTriangles.Count / 3} triangles.");
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
            HashSet<Vector3Int> keys = new HashSet<Vector3Int>();
            foreach (Vector3 vertex in vertices)
                keys.Add(QuantizeVertex(vertex, bounds, resolution));
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
            Dictionary<Vector3Int, int> keyToCluster = new Dictionary<Vector3Int, int>();
            int[] oldToNew = new int[sourceVertices.Length];

            for (int oldIndex = 0; oldIndex < sourceVertices.Length; oldIndex++)
            {
                Vector3Int key = QuantizeVertex(sourceVertices[oldIndex], bounds, resolution);
                if (!keyToCluster.TryGetValue(key, out int clusterIndex))
                {
                    clusterIndex = clusters.Count;
                    keyToCluster.Add(key, clusterIndex);
                    clusters.Add(new LowLodCluster { oldIndices = new List<int>() });
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

        private static Color AverageClusterPositionColor(LowLodCluster cluster, int frame, int fullTexWidth, int fullRowsPerFrame, Color[] fullPosColors)
        {
            Vector4 sum = Vector4.zero;
            foreach (int oldIndex in cluster.oldIndices)
            {
                Color color = ReadFullVatColor(oldIndex, frame, fullTexWidth, fullRowsPerFrame, fullPosColors);
                sum += new Vector4(color.r, color.g, color.b, color.a);
            }

            sum *= 1f / Mathf.Max(cluster.oldIndices.Count, 1);
            return new Color(sum.x, sum.y, sum.z, sum.w);
        }

        private static Color AverageClusterNormalColor(LowLodCluster cluster, int frame, int fullTexWidth, int fullRowsPerFrame, Color[] fullNormColors)
        {
            Vector3 sum = Vector3.zero;
            foreach (int oldIndex in cluster.oldIndices)
            {
                Color color = ReadFullVatColor(oldIndex, frame, fullTexWidth, fullRowsPerFrame, fullNormColors);
                sum += new Vector3(color.r, color.g, color.b);
            }

            Vector3 normal = sum.sqrMagnitude > 0.000001f ? sum.normalized : Vector3.up;
            return new Color(normal.x, normal.y, normal.z, 1f);
        }

        private static Color ReadFullVatColor(int oldIndex, int frame, int fullTexWidth, int fullRowsPerFrame, Color[] colors)
        {
            int oldX = oldIndex % fullTexWidth;
            int oldY = frame * fullRowsPerFrame + oldIndex / fullTexWidth;
            return colors[oldY * fullTexWidth + oldX];
        }

        private string GetHierarchyPath(Transform current, Transform root)
        {
            if (current == root)
                return current.name;

            return GetHierarchyPath(current.parent, root) + "/" + current.name;
        }

        private sealed class ClipBakeSlot
        {
            public readonly string label;
            public AnimationClip clip;
            public bool loop;
            public bool useCustomFrameRate;
            public int frameRateOverride = 30;

            public ClipBakeSlot(string label, bool loop)
            {
                this.label = label;
                this.loop = loop;
            }
        }

        private struct ClipBakeInfo
        {
            public string label;
            public AnimationClip clip;
            public bool loop;
            public int frameRate;
            public int startFrame;
            public int frameCount;
            public bool usedFallback;
        }

        private struct LowLodCluster
        {
            public Vector3 positionSum;
            public Vector3 normalSum;
            public Vector2 uvSum;
            public int count;
            public List<int> oldIndices;
        }

        private sealed class LowLodBakeResult
        {
            public Mesh mesh;
            public Texture2D positionTexture;
            public Texture2D normalTexture;
            public int textureWidth;
            public int textureHeight;
            public int rowsPerFrame;
        }
    }
}
