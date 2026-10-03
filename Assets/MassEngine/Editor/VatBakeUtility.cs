using System;
using UnityEngine;

namespace MassEngine.Editor
{
    public static class VatBakeUtility
    {
        internal const int MaxTextureSize = 16384;
        internal const int MaxFrameCount = 16384;
        internal const int MaxVertexCount = 1000000;
        internal const int MaxIndexCount = 6000000;
        internal const long MaxWorkingBytes = 512L * 1024 * 1024;

        public static Vector3Int CalculateLayout(int vertexCount, int frameCount, int maxWidth = 4096)
        {
            if (vertexCount <= 0 || frameCount <= 0 || maxWidth <= 0 || maxWidth > MaxTextureSize)
                throw new ArgumentException("顶点数、帧数必须为正，最大纹理宽度须在 1～16384 之间。");

            int width = Math.Min(vertexCount, maxWidth);
            long rows = ((long)vertexCount + width - 1) / width;
            long height = rows * frameCount;
            if (height > MaxTextureSize || (long)width * height * 64 > MaxWorkingBytes)
                throw new ArgumentException("VAT 布局超过 16384 尺寸或 512 MiB 烘焙工作内存预算，请降低顶点数或帧数。");
            return new Vector3Int(width, (int)height, (int)rows);
        }

        internal static void CheckMemory(Vector3Int full, Vector3Int low, int vertices, int indices)
        {
            // 两组 Color 缓冲及 RGBAHalf 纹理的 CPU/GPU 副本，另预留网格与聚类工作区。
            long bytes = ((long)full.x * full.y + (long)low.x * low.y) * 64
                + (long)vertices * 256 + (long)indices * 8;
            if (bytes > MaxWorkingBytes)
                throw new ArgumentException("Full/Low LOD 合计超过 512 MiB 烘焙工作内存预算。");
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        internal static void CheckPosition(Vector3 value)
        {
            if (!IsFinite(value) || Mathf.Abs(value.x) > 65504f || Mathf.Abs(value.y) > 65504f || Mathf.Abs(value.z) > 65504f)
                throw new ArgumentException("模型采样位置包含非有限值或超出 RGBAHalf 范围（±65504）。");
        }

        internal static Vector3 Normalize(Vector3 value)
        {
            if (!IsFinite(value))
                throw new ArgumentException("模型法线包含非有限值。");
            float largest = Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
            return largest > 0f ? (value / largest).normalized : Vector3.up;
        }

        internal static Bounds ExpandForHalfPrecision(Bounds bounds)
        {
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            Vector3 margin = new Vector3(
                Mathf.Max(Mathf.Abs(minimum.x), Mathf.Abs(maximum.x)),
                Mathf.Max(Mathf.Abs(minimum.y), Mathf.Abs(maximum.y)),
                Mathf.Max(Mathf.Abs(minimum.z), Mathf.Abs(maximum.z))) * 0.001f + Vector3.one * 0.001f;
            bounds.SetMinMax(minimum - margin, maximum + margin);
            return bounds;
        }

        internal static Texture2D CreateTexture(VatBakeResult owner, Vector3Int layout, Color[] pixels, string name)
        {
            Texture2D texture = owner.Own(new Texture2D(layout.x, layout.y, TextureFormat.RGBAHalf, false, true));
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
