using UnityEngine;

namespace MassEngine
{
    public struct VatClipData
    {
        public int startFrame;
        public int frameCount;
        public int frameRate;

        public float Duration
        {
            get { return Mathf.Max(1, frameCount) / (float)Mathf.Max(1, frameRate); }
        }
    }

    public struct VatProfileData
    {
        public Mesh cleanMesh;
        public Texture positionTexture;
        public Texture normalTexture;
        public Mesh midLodMesh;
        public Texture midLodPositionTexture;
        public Texture midLodNormalTexture;
        public int midLodTextureWidth;
        public int midLodTextureHeight;
        public int midLodRowsPerFrame;
        public Mesh lowLodMesh;
        public Texture lowLodPositionTexture;
        public Texture lowLodNormalTexture;
        public int lowLodTextureWidth;
        public int lowLodTextureHeight;
        public int lowLodRowsPerFrame;
        public int textureWidth;
        public int textureHeight;
        public int rowsPerFrame;
        public int totalFrameCount;
        public int frameRate;
        public VatClipData idle;
        public VatClipData move;
        public VatClipData attack;
        public VatClipData death;
        public bool hasMidLod;
        public bool hasLowLod;
    }

    /// <summary>
    /// Reads a VAT profile ScriptableObject (duck-typed via reflection so any baker's
    /// profile type works) into a plain struct. Runs once per unit type at
    /// initialization — never on the render path, and never writes anything back.
    /// </summary>
    public static class VatProfileReader
    {
        public static bool TryRead(ScriptableObject profile, out VatProfileData data, out string error)
        {
            data = default;
            error = string.Empty;
            if (profile == null)
            {
                error = "Profile is null.";
                return false;
            }

            System.Type type = profile.GetType();
            data.cleanMesh = ReadField<Mesh>(profile, type, "cleanMesh");
            data.positionTexture = ReadField<Texture>(profile, type, "positionTexture");
            data.normalTexture = ReadField<Texture>(profile, type, "normalTexture");
            data.midLodMesh = ReadField<Mesh>(profile, type, "midLodMesh");
            data.midLodPositionTexture = ReadField<Texture>(profile, type, "midLodPositionTexture");
            data.midLodNormalTexture = ReadField<Texture>(profile, type, "midLodNormalTexture");
            data.midLodTextureWidth = ReadField<int>(profile, type, "midLodTextureWidth");
            data.midLodTextureHeight = ReadField<int>(profile, type, "midLodTextureHeight");
            data.midLodRowsPerFrame = ReadField<int>(profile, type, "midLodRowsPerFrame");
            data.lowLodMesh = ReadField<Mesh>(profile, type, "lowLodMesh");
            data.lowLodPositionTexture = ReadField<Texture>(profile, type, "lowLodPositionTexture");
            data.lowLodNormalTexture = ReadField<Texture>(profile, type, "lowLodNormalTexture");
            data.lowLodTextureWidth = ReadField<int>(profile, type, "lowLodTextureWidth");
            data.lowLodTextureHeight = ReadField<int>(profile, type, "lowLodTextureHeight");
            data.lowLodRowsPerFrame = ReadField<int>(profile, type, "lowLodRowsPerFrame");
            data.textureWidth = ReadField<int>(profile, type, "textureWidth");
            data.textureHeight = ReadField<int>(profile, type, "textureHeight");
            data.rowsPerFrame = ReadField<int>(profile, type, "rowsPerFrame");
            data.totalFrameCount = ReadField<int>(profile, type, "totalFrameCount");
            data.frameRate = ReadField<int>(profile, type, "frameRate");
            data.idle = ReadClip(profile, type, "idle");
            data.move = ReadClip(profile, type, "move");
            data.attack = ReadClip(profile, type, "attack");
            data.death = ReadClip(profile, type, "death");
            data.hasLowLod = data.lowLodMesh != null &&
                             data.lowLodPositionTexture != null &&
                             data.lowLodNormalTexture != null &&
                             data.lowLodTextureWidth > 0 &&
                             data.lowLodTextureHeight > 0 &&
                             data.lowLodRowsPerFrame > 0;
            data.hasMidLod = data.midLodMesh != null &&
                             data.midLodPositionTexture != null &&
                             data.midLodNormalTexture != null &&
                             data.midLodTextureWidth > 0 &&
                             data.midLodTextureHeight > 0 &&
                             data.midLodRowsPerFrame > 0;

            if (data.cleanMesh == null)
            {
                error = "Profile is missing cleanMesh.";
                return false;
            }

            if (data.positionTexture == null || data.normalTexture == null)
            {
                error = "Profile is missing full LOD VAT position or normal texture.";
                return false;
            }

            if (data.textureWidth <= 0 || data.textureHeight <= 0 || data.rowsPerFrame <= 0 || data.totalFrameCount <= 0 || data.frameRate <= 0)
            {
                error = "Profile has invalid full LOD VAT layout values.";
                return false;
            }

            return true;
        }

        private static T ReadField<T>(object target, System.Type targetType, string fieldName)
        {
            System.Reflection.FieldInfo field = targetType.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
                return default;

            object value = field.GetValue(target);
            return value is T typed ? typed : default;
        }

        private static VatClipData ReadClip(object target, System.Type targetType, string fieldName)
        {
            System.Reflection.FieldInfo field = targetType.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
                return default;

            object clip = field.GetValue(target);
            if (clip == null)
                return default;

            System.Type clipType = clip.GetType();
            return new VatClipData
            {
                startFrame = ReadField<int>(clip, clipType, "startFrame"),
                frameCount = Mathf.Max(1, ReadField<int>(clip, clipType, "frameCount")),
                frameRate = Mathf.Max(1, ReadField<int>(clip, clipType, "frameRate"))
            };
        }
    }
}
