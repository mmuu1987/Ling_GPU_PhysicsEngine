using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MassEngine.Editor
{
    public sealed class VatBakeRequest
    {
        public GameObject model;
        public AnimationClip idle, move, attack, death;
        public int frameRate = 30;
        public bool bakeLowLod = true;
        public float lowLodRatio = .25f;
        public int lowLodMaxVertices = 1200;
        public MeshRenderer[] extraRenderers = Array.Empty<MeshRenderer>();

        /// <summary>可选进度回调：(阶段描述, 0~1 进度)。批处理与窗口共用同一份核心。</summary>
        public Action<string, float> progress;
    }

    public static class VatBaker
    {
        public static VatBakeResult Bake(VatBakeRequest request)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("播放或即将进入播放模式时不能烘焙 VAT。");
            if (request == null || request.model == null)
                throw new ArgumentException("请指定待烘焙模型。", nameof(request));
            if (request.idle == null || request.move == null || request.attack == null || request.death == null)
                throw new ArgumentException("Idle、Move、Attack、Death 四段动画全部必填。", nameof(request));
            if (request.frameRate <= 0 || request.frameRate > 240)
                throw new ArgumentException("采样帧率须在 1～240 之间。", nameof(request));
            if (request.bakeLowLod && (!VatBakeUtility.IsFinite(request.lowLodRatio) || request.lowLodRatio <= 0f ||
                request.lowLodRatio > 1f || request.lowLodMaxVertices < 0 || (request.lowLodMaxVertices > 0 && request.lowLodMaxVertices < 3)))
                throw new ArgumentException("Low LOD 比例须在 (0, 1]；顶点上限须为 0（不限）或至少 3。", nameof(request));
            CheckTransform(request.model.transform);

            AnimationClip[] clips = { request.idle, request.move, request.attack, request.death };
            VATProfile.VATClipWindow[] windows = BuildWindows(clips, request.frameRate, out int frames);
            List<Source> sources = CollectSources(request, out int vertices, out int indices);
            Vector3Int layout = VatBakeUtility.CalculateLayout(vertices, frames);
            VatBakeUtility.CheckMemory(layout, Vector3Int.zero, vertices, indices);

            VatBakeResult result = new VatBakeResult();
            Scene preview = default;
            GameObject staging = null;
            Mesh temporaryMesh = null;
            try
            {
                preview = EditorSceneManager.NewPreviewScene();
                staging = new GameObject("VAT Bake Preview") { hideFlags = HideFlags.HideAndDontSave };
                staging.SetActive(false);
                SceneManager.MoveGameObjectToScene(staging, preview);
                temporaryMesh = new Mesh { name = "VAT Sampling Mesh", hideFlags = HideFlags.HideAndDontSave };
                var positions = new Color[layout.x * layout.y];
                var normals = new Color[positions.Length];
                Mesh clean = result.Own(new Mesh
                {
                    name = request.model.name + "_CleanMesh",
                    indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                });
                BuildTopology(sources, clean, vertices, indices);
                var framePositions = new Vector3[vertices];
                var frameNormals = new Vector3[vertices];
                var bakedPositions = new List<Vector3>();
                var bakedNormals = new List<Vector3>();
                Bounds animatedBounds = default;
                bool hasBounds = false;
                bool hasBindPose = false;

                for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                {
                    GameObject instance = null;
                    var materials = new List<Material>();
                    try
                    {
                        // 每段重建同一基准姿态，连未动画的 blendshape/组件通道也不会串到下一段。
                        // 父节点保持禁用：采样无需激活场景脚本、Animator 自动更新或 OnEnable。
                        instance = Object.Instantiate(request.model, staging.transform, false);
                        instance.hideFlags = HideFlags.HideAndDontSave;
                        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                        instance.transform.localScale = Vector3.one;
                        IsolateMaterials(instance, materials);
                        Renderer[] renderers = ResolveRenderers(instance.transform, sources);
                        Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
                        if (!hasBindPose)
                        {
                            // 刚实例化、还没 SampleAnimation，此刻层级就是绑定姿态。
                            // cleanMesh 存绑定姿态而不是 Idle 首帧：Low LOD 的顶点聚类以 cleanMesh
                            // 的几何范围做归一化，基准必须只取决于模型本身。若拿首帧当基准，
                            // 改一段 Idle 动画就会连带改掉所有远处 LOD 的网格拓扑。
                            SampleFrame(instance.transform, sources, renderers, temporaryMesh,
                                bakedPositions, bakedNormals, framePositions, frameNormals, bindPose: true);
                            clean.vertices = framePositions;
                            clean.normals = frameNormals;
                            clean.RecalculateBounds();
                            clean.RecalculateTangents();
                            hasBindPose = true;
                        }
                        VATProfile.VATClipWindow window = windows[clipIndex];
                        for (int frame = 0; frame < window.frameCount; frame++)
                        {
                            float time = clipIndex == 3 && window.frameCount > 1
                                ? clips[clipIndex].length * frame / (window.frameCount - 1)
                                : Mathf.Min(frame / (float)request.frameRate, clips[clipIndex].length);
                            clips[clipIndex].SampleAnimation(instance, time);
                            foreach (Transform transform in transforms)
                                CheckTransform(transform);
                            SampleFrame(instance.transform, sources, renderers, temporaryMesh,
                                bakedPositions, bakedNormals, framePositions, frameNormals);
                            int startPixel = (window.startFrame + frame) * layout.z * layout.x;
                            for (int v = 0; v < vertices; v++)
                            {
                                Vector3 p = framePositions[v];
                                Vector3 n = frameNormals[v];
                                positions[startPixel + v] = new Color(p.x, p.y, p.z, 1f);
                                normals[startPixel + v] = new Color(n.x, n.y, n.z, 1f);
                                if (!hasBounds)
                                {
                                    animatedBounds = new Bounds(p, Vector3.zero);
                                    hasBounds = true;
                                }
                                else
                                    animatedBounds.Encapsulate(p);
                            }
                            request.progress?.Invoke(
                                string.Format("采样 {0} {1}/{2}", window.label, frame + 1, window.frameCount),
                                (window.startFrame + frame + 1f) / frames);
                        }
                    }
                    finally
                    {
                        if (instance != null)
                            Object.DestroyImmediate(instance);
                        foreach (Material material in materials)
                            if (material != null)
                                Object.DestroyImmediate(material);
                    }
                }

                VATProfile profile = result.Profile;
                profile.name = request.model.name + "_VAT";
                profile.cleanMesh = clean;
                profile.textureWidth = layout.x;
                profile.textureHeight = layout.y;
                profile.rowsPerFrame = layout.z;
                profile.totalFrameCount = frames;
                profile.frameRate = request.frameRate;
                profile.idle = windows[0];
                profile.move = windows[1];
                profile.attack = windows[2];
                profile.death = windows[3];
                if (request.bakeLowLod)
                {
                    request.progress?.Invoke("生成 Low LOD", .9f);
                    VatLodReducer.Bake(result, positions, normals, layout, request.lowLodRatio, request.lowLodMaxVertices, indices);
                }
                request.progress?.Invoke("写入纹理", .95f);
                clean.bounds = VatBakeUtility.ExpandForHalfPrecision(animatedBounds);
                profile.positionTexture = VatBakeUtility.CreateTexture(result, layout, positions, "Full_Positions");
                profile.normalTexture = VatBakeUtility.CreateTexture(result, layout, normals, "Full_Normals");
                if (!VatProfileValidation.TryValidate(profile, out string error))
                    throw new ArgumentException(error);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
            finally
            {
                if (temporaryMesh != null)
                    Object.DestroyImmediate(temporaryMesh);
                if (staging != null)
                    Object.DestroyImmediate(staging);
                if (preview.IsValid())
                    EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static VATProfile.VATClipWindow[] BuildWindows(AnimationClip[] clips, int rate, out int total)
        {
            string[] labels = { "Idle", "Move", "Attack", "Death" };
            var windows = new VATProfile.VATClipWindow[clips.Length];
            total = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                float length = clips[i].length;
                // 必须用 float 运算：clip.length 是 float，转 double 会放大表示误差。
                // 例：0.5333s 的 clip 在 float 下 length*30 恰好舍入到 16，double 下是 16.0000008，
                // 取上整就变成 17 —— 既有资产正是按 float 口径烘的（141+16+16+15 = 188 帧）。
                int count = Mathf.Max(1, Mathf.CeilToInt(length * rate));
                if (!VatBakeUtility.IsFinite(length) || length < 0f || count > VatBakeUtility.MaxFrameCount - total)
                    throw new ArgumentException("动画长度非法或四段合计超过 16384 帧。");
                windows[i] = new VATProfile.VATClipWindow
                {
                    label = labels[i], startFrame = total, frameCount = count, frameRate = rate, loop = i != 3
                };
                total += count;
            }
            return windows;
        }

        private sealed class Source
        {
            internal Renderer renderer;
            internal Mesh mesh;
            internal int[] path;
            internal int count;
            internal int offset;
            internal Vector3[] positions;
            internal Vector3[] normals;
        }

        /// <summary>
        /// 判断节点在模型内部是否处于激活状态。<c>activeInHierarchy</c> 在这里不可用：
        /// 作为资产加载的 prefab 根节点没有父场景，整棵树永远是"未激活"，
        /// 那样会把最常见的输入（prefab 资产）误判成没有渲染器。这里只看模型自身这一段的 activeSelf。
        /// </summary>
        private static bool IsActiveInModel(Transform transform, Transform root)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                    return false;
                if (current == root)
                    break;
            }
            return true;
        }

        private static List<Source> CollectSources(VatBakeRequest request, out int vertexCount, out int indexCount)
        {
            var renderers = new List<Renderer>();
            foreach (SkinnedMeshRenderer renderer in request.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.enabled && IsActiveInModel(renderer.transform, request.model.transform))
                    renderers.Add(renderer);
            var extras = new HashSet<MeshRenderer>();
            foreach (MeshRenderer renderer in request.extraRenderers ?? Array.Empty<MeshRenderer>())
            {
                if (renderer == null || !renderer.transform.IsChildOf(request.model.transform))
                    throw new ArgumentException("附件必须是模型自身或子层级中的 MeshRenderer，不能是空引用或外部对象。");
                if (renderer.enabled && IsActiveInModel(renderer.transform, request.model.transform) && extras.Add(renderer))
                    renderers.Add(renderer);
            }
            if (renderers.Count == 0)
                throw new ArgumentException("模型没有启用且激活的蒙皮渲染器，也没有有效的显式附件。");
            var sources = new List<Source>();
            vertexCount = 0;
            indexCount = 0;
            foreach (Renderer renderer in renderers)
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = skinned != null ? skinned.sharedMesh : (filter != null ? filter.sharedMesh : null);
                if (mesh == null || mesh.vertexCount == 0)
                    throw new ArgumentException(renderer.name + " 缺少网格或网格为空。");
                if (mesh.vertexCount > VatBakeUtility.MaxVertexCount - vertexCount)
                    throw new ArgumentException("总顶点数超过 100 万限制。");
                long meshIndices = 0;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                        throw new ArgumentException(renderer.name + " 只支持三角形网格。");
                    meshIndices += mesh.GetIndexCount(sub);
                }
                if (meshIndices == 0 || meshIndices > VatBakeUtility.MaxIndexCount - indexCount)
                    throw new ArgumentException("网格没有三角形或总索引数超过 600 万限制。");
                if (skinned != null)
                {
                    foreach (Transform bone in skinned.bones)
                        if (bone == null || !bone.IsChildOf(request.model.transform))
                            throw new ArgumentException(renderer.name + " 的骨骼缺失或位于模型层级外。");
                    if (skinned.rootBone != null && !skinned.rootBone.IsChildOf(request.model.transform))
                        throw new ArgumentException(renderer.name + " 的根骨骼位于模型层级外。");
                }
                var path = new List<int>();
                for (Transform current = renderer.transform; current != request.model.transform; current = current.parent)
                    path.Add(current.GetSiblingIndex());
                path.Reverse();
                sources.Add(new Source
                {
                    renderer = renderer, mesh = mesh, path = path.ToArray(), count = mesh.vertexCount, offset = vertexCount,
                    positions = mesh.vertices, normals = mesh.normals
                });
                vertexCount += mesh.vertexCount;
                indexCount += (int)meshIndices;
            }
            return sources;
        }

        private static Renderer[] ResolveRenderers(Transform root, List<Source> sources)
        {
            var renderers = new Renderer[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                Transform current = root;
                foreach (int index in sources[i].path)
                    current = current.GetChild(index);
                renderers[i] = sources[i].renderer is SkinnedMeshRenderer
                    ? (Renderer)current.GetComponent<SkinnedMeshRenderer>() : current.GetComponent<MeshRenderer>();
                if (renderers[i] == null)
                    throw new ArgumentException("克隆模型的渲染器层级与源模型不一致。");
            }
            return renderers;
        }

        private static void BuildTopology(List<Source> sources, Mesh clean, int vertices, int indices)
        {
            var uv = new Vector2[vertices];
            var triangles = new List<int>(indices);
            foreach (Source source in sources)
            {
                Vector2[] sourceUv = source.mesh.uv;
                for (int v = 0; v < source.count; v++)
                {
                    Vector2 value = sourceUv.Length > v ? sourceUv[v] : Vector2.zero;
                    if (!VatBakeUtility.IsFinite(value.x) || !VatBakeUtility.IsFinite(value.y))
                        throw new ArgumentException(source.renderer.name + " 的 UV 包含非有限值。");
                    uv[source.offset + v] = value;
                }
                for (int sub = 0; sub < source.mesh.subMeshCount; sub++)
                {
                    int[] sourceTriangles = source.mesh.GetTriangles(sub);
                    foreach (int index in sourceTriangles)
                    {
                        if (index < 0 || index >= source.count)
                            throw new ArgumentException("网格包含越界索引。");
                        triangles.Add(source.offset + index);
                    }
                }
            }
            clean.vertices = new Vector3[vertices];
            clean.uv = uv;
            clean.SetTriangles(triangles, 0, false);
        }

        private static void SampleFrame(Transform root, List<Source> sources, Renderer[] renderers, Mesh temporary,
            List<Vector3> bakedPositions, List<Vector3> bakedNormals, Vector3[] positions, Vector3[] normals,
            bool bindPose = false)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                Source source = sources[i];
                Renderer renderer = renderers[i];
                IList<Vector3> meshPositions;
                IList<Vector3> meshNormals;
                if (renderer is SkinnedMeshRenderer skinned && !bindPose)
                {
                    if (skinned.sharedMesh != source.mesh)
                        throw new ArgumentException("动画不能替换蒙皮网格；VAT 要求顶点顺序固定。");
                    // BakeMesh(false) 保持渲染器局部坐标，缩放只在下面的 localToRoot 中应用一次。
                    temporary.Clear();
                    skinned.BakeMesh(temporary, false);
                    if (temporary.vertexCount != source.count)
                        throw new ArgumentException("蒙皮采样改变了顶点数，不能保持 VAT 顶点映射。");
                    temporary.GetVertices(bakedPositions);
                    temporary.GetNormals(bakedNormals);
                    if (bakedNormals.Count != source.count)
                    {
                        temporary.RecalculateNormals();
                        temporary.GetNormals(bakedNormals);
                    }
                    meshPositions = bakedPositions;
                    meshNormals = bakedNormals;
                }
                else
                {
                    // 绑定姿态直接取网格原始顶点，与 Stage5 既有资产的口径一致。
                    if (!bindPose)
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh != source.mesh)
                            throw new ArgumentException("动画不能替换附件网格；VAT 要求顶点顺序固定。");
                    }
                    meshPositions = source.positions;
                    meshNormals = source.normals;
                }
                Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int element = 0; element < 16; element++)
                    if (!VatBakeUtility.IsFinite(matrix[element]))
                        throw new ArgumentException("模型变换矩阵包含非有限值。");
                float determinant = matrix.determinant;
                if (!VatBakeUtility.IsFinite(determinant) || Mathf.Abs(determinant) < 1e-12f)
                    throw new ArgumentException("模型存在不可逆缩放，不能正确转换法线。");
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int v = 0; v < source.count; v++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(meshPositions[v]);
                    VatBakeUtility.CheckPosition(p);
                    Vector3 n = meshNormals.Count > v ? meshNormals[v] : Vector3.up;
                    positions[source.offset + v] = p;
                    normals[source.offset + v] = VatBakeUtility.Normalize(normalMatrix.MultiplyVector(n));
                }
            }
        }

        private static void CheckTransform(Transform transform)
        {
            Quaternion rotation = transform.localRotation;
            Vector3 scale = transform.localScale;
            if (!VatBakeUtility.IsFinite(transform.localPosition) || !VatBakeUtility.IsFinite(scale) ||
                scale.x == 0f || scale.y == 0f || scale.z == 0f || !VatBakeUtility.IsFinite(rotation.x) ||
                !VatBakeUtility.IsFinite(rotation.y) || !VatBakeUtility.IsFinite(rotation.z) || !VatBakeUtility.IsFinite(rotation.w))
                throw new ArgumentException(transform.name + " 的位置、旋转或缩放无效。");
        }

        private static void IsolateMaterials(GameObject instance, List<Material> owned)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material original = materials[i];
                    if (original == null)
                        continue;
                    if (!copies.TryGetValue(original, out Material copy))
                    {
                        copy = new Material(original) { hideFlags = HideFlags.HideAndDontSave };
                        owned.Add(copy);
                        copies.Add(original, copy);
                    }
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
        }
    }
}
