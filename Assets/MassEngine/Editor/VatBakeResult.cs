using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Editor
{
    public sealed class VatBakeResult : IDisposable
    {
        private readonly List<Object> owned = new List<Object>();
        private bool disposed;
        private bool saved;

        public VATProfile Profile { get; }

        internal VatBakeResult()
        {
            Profile = Own(ScriptableObject.CreateInstance<VATProfile>());
        }

        internal T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        public VATProfile SaveNew(string assetPath)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(VatBakeResult));
            if (saved || EditorUtility.IsPersistent(Profile))
                throw new InvalidOperationException("此烘焙结果已保存，不能再次作为新主资产保存。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("播放或即将进入播放模式时不能保存 VAT。");
            ValidateNewPath(assetPath);
            if (!VatProfileValidation.TryValidate(Profile, out string error))
                throw new ArgumentException(error);

            Object[] parts = {
                Profile.cleanMesh, Profile.positionTexture, Profile.normalTexture,
                Profile.midLodMesh, Profile.midLodPositionTexture, Profile.midLodNormalTexture,
                Profile.lowLodMesh, Profile.lowLodPositionTexture, Profile.lowLodNormalTexture
            };
            foreach (Object part in parts)
                if (part != null && (!owned.Contains(part) || EditorUtility.IsPersistent(part)))
                    throw new ArgumentException("Profile 引用了不属于本次烘焙的资源，不能移动外部资产作为子资产。");

            try
            {
                AssetDatabase.CreateAsset(Profile, assetPath);
                if (AssetDatabase.GetAssetPath(Profile) != assetPath)
                    throw new IOException("VAT 主资产创建失败。");
                var added = new HashSet<Object>();
                foreach (Object part in parts)
                    if (part != null && added.Add(part))
                    {
                        AssetDatabase.AddObjectToAsset(part, Profile);
                        if (AssetDatabase.GetAssetPath(part) != assetPath)
                            throw new IOException("VAT 子资产创建失败。");
                    }
                EditorUtility.SetDirty(Profile);
                AssetDatabase.SaveAssetIfDirty(Profile);
                if (!File.Exists(assetPath) || AssetDatabase.LoadAssetAtPath<VATProfile>(assetPath) != Profile)
                    throw new IOException("VAT 资产保存失败。");
                saved = true;
                return Profile;
            }
            catch
            {
                // 仅当该路径的主对象确实是本次结果时回滚，不删除其他调用者创建的资产。
                if (Profile != null && AssetDatabase.LoadMainAssetAtPath(assetPath) == Profile)
                    AssetDatabase.DeleteAsset(assetPath);
                Dispose();
                throw;
            }
        }

        /// <summary>校验输出路径：必须是 Assets 下尚不存在的 .asset，避免覆盖既有 profile。</summary>
        public static void ValidateNewPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || assetPath.IndexOf('\\') >= 0)
                throw new ArgumentException("输出必须是现有 Assets 文件夹中的新 .asset 路径（使用 / 分隔）。", nameof(assetPath));
            string[] segments = assetPath.Split('/');
            foreach (string segment in segments)
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".." ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith(" ") || segment.EndsWith("."))
                    throw new ArgumentException("输出路径包含无效名称或路径跳转。", nameof(assetPath));
            if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(assetPath)) ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/')))
                throw new ArgumentException("输出名称无效或 Assets 文件夹不存在。", nameof(assetPath));
            if (File.Exists(assetPath) || Directory.Exists(assetPath) || File.Exists(assetPath + ".meta") ||
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) || AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                throw new ArgumentException("输出路径已存在；VAT 烘焙只能创建新资产，不能覆盖。", nameof(assetPath));
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null && !EditorUtility.IsPersistent(owned[i]))
                    Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }
    }
}
