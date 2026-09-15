using UnityEngine;

namespace MassEngine.Editor
{
    public static class VatProfileValidation
    {
        public static bool TryValidate(VATProfile profile, out string error)
        {
            error = string.Empty;
            if (profile == null)
                return Fail("VAT Profile 为空。", out error);
            if (profile.totalFrameCount <= 0 || profile.totalFrameCount > VatBakeUtility.MaxFrameCount || profile.frameRate <= 0)
                return Fail("总帧数或帧率无效。", out error);

            VATProfile.VATClipWindow[] windows = { profile.idle, profile.move, profile.attack, profile.death };
            string[] labels = { "Idle", "Move", "Attack", "Death" };
            for (int i = 0; i < windows.Length; i++)
            {
                VATProfile.VATClipWindow window = windows[i];
                long end = (long)window.startFrame + window.frameCount;
                if (window.startFrame < 0 || window.frameCount <= 0 || window.frameRate <= 0 || end > profile.totalFrameCount)
                    return Fail(labels[i] + " 片段起始帧、帧数或帧率无效，或片段越界。", out error);
                for (int j = 0; j < i; j++)
                    if (window.startFrame < (long)windows[j].startFrame + windows[j].frameCount && windows[j].startFrame < end)
                        return Fail(labels[i] + " 与 " + labels[j] + " 的帧窗口重叠。", out error);
            }

            long pixels = 0;
            if (!ValidateLod("Full", profile.cleanMesh, profile.positionTexture, profile.normalTexture,
                profile.textureWidth, profile.textureHeight, profile.rowsPerFrame, profile.totalFrameCount, false, ref pixels, out error))
                return false;
            if (!ValidateLod("Mid", profile.midLodMesh, profile.midLodPositionTexture, profile.midLodNormalTexture,
                profile.midLodTextureWidth, profile.midLodTextureHeight, profile.midLodRowsPerFrame, profile.totalFrameCount, true, ref pixels, out error))
                return false;
            if (!ValidateLod("Low", profile.lowLodMesh, profile.lowLodPositionTexture, profile.lowLodNormalTexture,
                profile.lowLodTextureWidth, profile.lowLodTextureHeight, profile.lowLodRowsPerFrame, profile.totalFrameCount, true, ref pixels, out error))
                return false;
            return pixels * 64 <= VatBakeUtility.MaxWorkingBytes || Fail("VAT 纹理合计超过烘焙内存预算。", out error);
        }

        private static bool ValidateLod(string label, Mesh mesh, Texture2D positions, Texture2D normals,
            int width, int height, int rows, int frames, bool optional, ref long pixels, out string error)
        {
            error = string.Empty;
            bool absent = mesh == null && positions == null && normals == null && width == 0 && height == 0 && rows == 0;
            if (optional && absent)
                return true;
            if (mesh == null || positions == null || normals == null)
                return Fail(label + " LOD 的网格、位置纹理和法线纹理必须完整配对。", out error);
            if (mesh.vertexCount <= 0 || width <= 0 || height <= 0 || rows <= 0 ||
                width > VatBakeUtility.MaxTextureSize || height > VatBakeUtility.MaxTextureSize)
                return Fail(label + " LOD 的顶点数或纹理布局无效。", out error);
            if (positions == normals || positions.width != width || positions.height != height || normals.width != width || normals.height != height)
                return Fail(label + " LOD 的位置/法线纹理尺寸与声明不一致，或错误复用了同一张纹理。", out error);
            if ((long)width * rows < mesh.vertexCount || (long)rows * frames > height)
                return Fail(label + " LOD 的纹理容量不足以容纳全部顶点和帧。", out error);
            if (!VatBakeUtility.IsFinite(mesh.bounds.center) || !VatBakeUtility.IsFinite(mesh.bounds.extents))
                return Fail(label + " LOD 的包围盒包含非有限值。", out error);
            pixels += (long)width * height;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
