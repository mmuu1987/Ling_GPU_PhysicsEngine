using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxModelTrialBuilderTests
    {
        private string folder;
        private readonly List<Object> transient = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__M54BuilderReview_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(folder);
            foreach (Object value in transient)
                if (value != null && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
            transient.Clear();
        }

        [Test]
        public void CreateMaterialRejectsAnExistingAssetWithoutOverwritingItsBytes()
        {
            Material source = SourceMaterial();
            Texture2D atlas = PersistedAtlas();
            var existing = new Material(source) { name = "UserEditedMaterial" };
            string path = folder + "/Reserved.mat";
            AssetDatabase.CreateAsset(existing, path);
            byte[] before = File.ReadAllBytes(path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Exception failure = null;
            try { transient.Add(CreateMaterial(source, atlas, path)); }
            catch (TargetInvocationException exception) { failure = exception.InnerException; }
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "Creating a trial must not overwrite an existing material.");
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(failure, Is.TypeOf<BuildFailedException>());
        }

        [Test]
        public void CreateMaterialPersistsANewCopyWithoutChangingItsSource()
        {
            Material source = SourceMaterial();
            Texture2D atlas = PersistedAtlas();
            string before = EditorJsonUtility.ToJson(source);
            string path = folder + "/New.mat";
            Material result = CreateMaterial(source, atlas, path);
            Assert.That(EditorUtility.IsPersistent(result), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(result), Is.EqualTo(path));
            Assert.That(result.GetTexture("_BaseMap"), Is.EqualTo(atlas));
            Assert.That(result.GetColor("_BaseColor"), Is.EqualTo(Color.white));
            Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before));
        }

        [Test]
        public void FreshPlayerOutputIncludesTrackedChecklistAndWindowedLauncher()
        {
            string output = folder + "/FreshPlayer";
            Assert.That(Directory.Exists(output), Is.False);
            var method = typeof(WarSandboxModelTrialBuilder).GetMethod("WritePlayerSupportFiles", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { output });
            string checklist = File.ReadAllText(output + "/开始验收.md");
            Assert.That(checklist, Does.Contain("**编号**：`M54_Manual`"));
            Assert.That(checklist, Does.Contain("**名称**"));
            string launcher = File.ReadAllText(output + "/Start-Trial.cmd");
            Assert.That(launcher, Does.Contain("\"%~dp0ModelTrial.exe\""));
            Assert.That(launcher, Does.Contain("-screen-fullscreen 0"));
            Assert.That(File.ReadAllBytes(output + "/Start-Trial.cmd")[0], Is.EqualTo((byte)'@'), "CMD must not start with a UTF-8 BOM.");
        }

        private Texture2D PersistedAtlas()
        {
            // Production passes the imported PNG asset, not Unity's transient whiteTexture.
            var atlas = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            atlas.SetPixel(0, 0, Color.green);
            atlas.Apply();
            AssetDatabase.CreateAsset(atlas, folder + "/Atlas.asset");
            return atlas;
        }

        private Material SourceMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            source.SetColor("_BaseColor", Color.red);
            transient.Add(source);
            return source;
        }

        private static Material CreateMaterial(Material source, Texture atlas, string path)
        {
            var method = typeof(WarSandboxModelTrialBuilder).GetMethod("CreateMaterial", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Material)method.Invoke(null, new object[] { source, atlas, path });
        }
    }
}
