using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Editor
{
    /// <summary>
    /// 把全分辨率 VAT 采样降成 Low LOD：对 cleanMesh 做顶点聚类得到低模，
    /// 再把逐帧的位置/法线纹理按簇取平均，得到与低模顶点一一对应的低分辨率纹理。
    /// 算法与归档版 Stage6 窗口一致（顶点聚类 + 簇内平均），此处改为事务化产出：
    /// 所有新建对象都挂到 <see cref="VatBakeResult"/> 上，由它统一释放或随主资产保存。
    /// </summary>
    public static class VatLodReducer
    {
        private const int MaxClusterResolution = 256;

        /// <summary>
        /// 生成 Low LOD 并写回 <see cref="VatBakeResult.Profile"/> 的 lowLod* 槽位。
        /// Mid LOD 有意留空：现役 <c>ResolvedUnitTypeRuntime</c> 对 mid/low 缺失有回退，单级 LOD 可跑，
        /// 现有 Male profile 同样是 full + low 两级。
        /// </summary>
        /// <param name="result">本次烘焙的持有者，新建的网格与纹理由它接管。</param>
        /// <param name="positions">全分辨率位置纹理像素（布局见 <paramref name="layout"/>）。</param>
        /// <param name="normals">全分辨率法线纹理像素。</param>
        /// <param name="layout">全分辨率布局：x = 纹理宽，y = 纹理高，z = 每帧行数。</param>
        /// <param name="ratio">低模顶点保留比例，取值 (0, 1]。</param>
        /// <param name="maxVertices">低模顶点上限；0 表示不限。</param>
        /// <param name="indices">全分辨率索引总数，仅用于烘焙内存预算核算。</param>
        public static void Bake(VatBakeResult result, Color[] positions, Color[] normals, Vector3Int layout,
            float ratio, int maxVertices, int indices)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (positions == null || normals == null || positions.Length != normals.Length)
                throw new ArgumentException("位置与法线纹理像素必须同时提供且长度一致。", nameof(positions));
            if (layout.x <= 0 || layout.y <= 0 || layout.z <= 0 || (long)layout.x * layout.y != positions.Length)
                throw new ArgumentException("全分辨率布局与像素数组长度不一致。", nameof(layout));
            if (!VatBakeUtility.IsFinite(ratio) || ratio <= 0f || ratio > 1f)
                throw new ArgumentException("Low LOD 比例须在 (0, 1]。", nameof(ratio));
            if (maxVertices < 0)
                throw new ArgumentException("Low LOD 顶点上限不能为负。", nameof(maxVertices));

            Mesh clean = result.Profile.cleanMesh;
            if (clean == null || clean.vertexCount == 0)
                throw new ArgumentException("Low LOD 需要已生成的全分辨率 cleanMesh。", nameof(result));

            int frameCount = layout.y / layout.z;
            if (frameCount <= 0)
                throw new ArgumentException("全分辨率布局推不出帧数。", nameof(layout));

            Vector3[] sourceVertices = clean.vertices;
            Vector3[] sourceNormals = clean.normals;
            Vector2[] sourceUVs = clean.uv;
            int[] sourceTriangles = clean.triangles;
            if (sourceTriangles.Length == 0)
                throw new ArgumentException("cleanMesh 没有三角形，无法生成 Low LOD。");

            int targetVertexCount = Mathf.Max(4, Mathf.RoundToInt(sourceVertices.Length * ratio));
            if (maxVertices > 0)
                targetVertexCount = Mathf.Min(targetVertexCount, maxVertices);
            targetVertexCount = Mathf.Min(targetVertexCount, sourceVertices.Length);

            // 聚类以 cleanMesh.bounds 为归一化基准，而包围盒在调用点会被动画行程撑大。
            // 这里显式按顶点重算一次"几何包围盒"，让减面结果只取决于网格本身，
            // 不依赖调用顺序，也不会因为动画行程（例如死亡姿态躺倒）改变减面密度。
            Bounds bounds = GeometryBounds(sourceVertices);
            int resolution = FindClusterResolution(sourceVertices, bounds, targetVertexCount);
            int[] oldToNew = BuildVertexClusterMapping(sourceVertices, sourceNormals, sourceUVs, bounds,
                resolution, out List<Cluster> clusters);

            var lowVertices = new List<Vector3>(clusters.Count);
            var lowNormals = new List<Vector3>(clusters.Count);
            var lowUvs = new List<Vector2>(clusters.Count);
            foreach (Cluster cluster in clusters)
            {
                float inverse = 1f / Mathf.Max(cluster.count, 1);
                lowVertices.Add(cluster.positionSum * inverse);
                lowNormals.Add(cluster.normalSum.sqrMagnitude > 1e-6f ? cluster.normalSum.normalized : Vector3.up);
                lowUvs.Add(cluster.uvSum * inverse);
            }

            List<int> lowTriangles = BuildReducedTriangles(sourceTriangles, oldToNew);
            if (lowVertices.Count == 0 || lowTriangles.Count == 0)
                throw new ArgumentException("Low LOD 减面后网格为空，请提高顶点保留比例或顶点上限。");

            var lowMesh = result.Own(new Mesh
            {
                name = clean.name + "_LowLOD",
                indexFormat = lowVertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            });
            lowMesh.SetVertices(lowVertices);
            lowMesh.SetNormals(lowNormals);
            lowMesh.SetUVs(0, lowUvs);
            lowMesh.SetTriangles(lowTriangles, 0, false);
            lowMesh.RecalculateBounds();
            lowMesh.RecalculateTangents();

            Vector3Int lowLayout = VatBakeUtility.CalculateLayout(lowMesh.vertexCount, frameCount);
            VatBakeUtility.CheckMemory(layout, lowLayout, lowMesh.vertexCount, indices);

            var lowPositions = new Color[lowLayout.x * lowLayout.y];
            var lowNormalsPixels = new Color[lowPositions.Length];
            AverageFrames(clusters, layout, frameCount, positions, normals, lowLayout, lowPositions, lowNormalsPixels);

            VATProfile profile = result.Profile;
            profile.lowLodMesh = lowMesh;
            profile.lowLodPositionTexture = VatBakeUtility.CreateTexture(result, lowLayout, lowPositions, clean.name + "_LowLOD_Positions");
            profile.lowLodNormalTexture = VatBakeUtility.CreateTexture(result, lowLayout, lowNormalsPixels, clean.name + "_LowLOD_Normals");
            profile.lowLodTextureWidth = lowLayout.x;
            profile.lowLodTextureHeight = lowLayout.y;
            profile.lowLodRowsPerFrame = lowLayout.z;
        }

        /// <summary>按顶点重算几何包围盒，避免依赖调用点对 <c>Mesh.bounds</c> 的改写。</summary>
        private static Bounds GeometryBounds(Vector3[] vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
                bounds.Encapsulate(vertices[i]);
            return bounds;
        }

        /// <summary>按帧把全分辨率纹理按簇平均写入低分辨率纹理。</summary>
        private static void AverageFrames(List<Cluster> clusters, Vector3Int full, int frameCount,
            Color[] positions, Color[] normals, Vector3Int low, Color[] lowPositions, Color[] lowNormals)
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                for (int clusterIndex = 0; clusterIndex < clusters.Count; clusterIndex++)
                {
                    List<int> members = clusters[clusterIndex].oldIndices;
                    var positionSum = new Vector3(0f, 0f, 0f);
                    var normalSum = new Vector3(0f, 0f, 0f);
                    foreach (int oldIndex in members)
                    {
                        int pixel = (frame * full.z + oldIndex / full.x) * full.x + oldIndex % full.x;
                        Color p = positions[pixel];
                        positionSum += new Vector3(p.r, p.g, p.b);
                        Color n = normals[pixel];
                        normalSum += new Vector3(n.r, n.g, n.b);
                    }

                    float inverse = 1f / Mathf.Max(members.Count, 1);
                    Vector3 average = positionSum * inverse;
                    Vector3 normal = normalSum.sqrMagnitude > 1e-6f ? normalSum.normalized : Vector3.up;
                    int lowPixel = (frame * low.z + clusterIndex / low.x) * low.x + clusterIndex % low.x;
                    lowPositions[lowPixel] = new Color(average.x, average.y, average.z, 1f);
                    lowNormals[lowPixel] = new Color(normal.x, normal.y, normal.z, 1f);
                }
            }
        }

        /// <summary>重映射三角形：丢掉塌缩成边或点的面，并按无序顶点集去重。</summary>
        private static List<int> BuildReducedTriangles(int[] sourceTriangles, int[] oldToNew)
        {
            var reduced = new List<int>(sourceTriangles.Length);
            var used = new HashSet<long>();
            for (int triangle = 0; triangle < sourceTriangles.Length; triangle += 3)
            {
                int a = oldToNew[sourceTriangles[triangle]];
                int b = oldToNew[sourceTriangles[triangle + 1]];
                int c = oldToNew[sourceTriangles[triangle + 2]];
                if (a == b || b == c || c == a)
                    continue;
                if (!used.Add(TriangleKey(a, b, c)))
                    continue;
                reduced.Add(a);
                reduced.Add(b);
                reduced.Add(c);
            }
            return reduced;
        }

        /// <summary>顶点数上限 100 万，每个索引 20 位，三个索引可无损装进 long。</summary>
        private static long TriangleKey(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return ((long)a << 40) | ((long)b << 20) | (uint)c;
        }

        private static int FindClusterResolution(Vector3[] vertices, Bounds bounds, int targetVertexCount)
        {
            int bestResolution = 1;
            int bestCount = CountClusters(vertices, bounds, bestResolution);
            for (int resolution = 2; resolution <= MaxClusterResolution; resolution++)
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
            foreach (Vector3 vertex in vertices)
                keys.Add(QuantizeVertex(vertex, bounds, resolution));
            return keys.Count;
        }

        private static int[] BuildVertexClusterMapping(Vector3[] vertices, Vector3[] normals, Vector2[] uvs,
            Bounds bounds, int resolution, out List<Cluster> clusters)
        {
            clusters = new List<Cluster>();
            var keyToCluster = new Dictionary<Vector3Int, int>();
            var oldToNew = new int[vertices.Length];
            for (int oldIndex = 0; oldIndex < vertices.Length; oldIndex++)
            {
                Vector3Int key = QuantizeVertex(vertices[oldIndex], bounds, resolution);
                if (!keyToCluster.TryGetValue(key, out int clusterIndex))
                {
                    clusterIndex = clusters.Count;
                    keyToCluster.Add(key, clusterIndex);
                    clusters.Add(new Cluster { oldIndices = new List<int>() });
                }

                Cluster cluster = clusters[clusterIndex];
                cluster.positionSum += vertices[oldIndex];
                cluster.normalSum += normals != null && normals.Length > oldIndex ? normals[oldIndex] : Vector3.up;
                cluster.uvSum += uvs != null && uvs.Length > oldIndex ? uvs[oldIndex] : Vector2.zero;
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
            Vector3 normalized = new Vector3(
                (vertex.x - bounds.min.x) / Mathf.Max(size.x, 1e-4f),
                (vertex.y - bounds.min.y) / Mathf.Max(size.y, 1e-4f),
                (vertex.z - bounds.min.z) / Mathf.Max(size.z, 1e-4f));
            int maxCell = Mathf.Max(0, resolution - 1);
            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(normalized.x * resolution), 0, maxCell),
                Mathf.Clamp(Mathf.FloorToInt(normalized.y * resolution), 0, maxCell),
                Mathf.Clamp(Mathf.FloorToInt(normalized.z * resolution), 0, maxCell));
        }

        private struct Cluster
        {
            internal Vector3 positionSum;
            internal Vector3 normalSum;
            internal Vector2 uvSum;
            internal int count;
            internal List<int> oldIndices;
        }
    }
}
