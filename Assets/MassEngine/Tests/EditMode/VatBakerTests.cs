using System;
using System.Collections.Generic;
using MassEngine.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Tests
{
    /// <summary>
    /// M5.1 VAT 烘焙工具的 EditMode 契约测试：布局推导、四段必填、LOD 配对校验、
    /// 烘焙往返（含"采样是否真的逐帧生效"）与资产保存的路径规则。
    /// </summary>
    public sealed class VatBakerTests
    {
        private const string TempFolder = "Assets/VatBakerTestTemp";
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (UnityEngine.Object asset in created)
                if (asset != null)
                    UnityEngine.Object.DestroyImmediate(asset);
            created.Clear();
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        // ------------------------------------------------------------------
        // 布局推导：必须与 shader 的 vY = frame * rowsPerFrame + vertexID / textureWidth 一致
        // ------------------------------------------------------------------

        [Test]
        public void LayoutPacksVerticesRowMajorPerFrame()
        {
            Vector3Int layout = VatBakeUtility.CalculateLayout(1000, 188);
            Assert.AreEqual(1000, layout.x, "顶点数不足上限时纹理宽度应等于顶点数。");
            Assert.AreEqual(1, layout.z, "1000 顶点只占 1 行。");
            Assert.AreEqual(188, layout.y, "高度 = 每帧行数 × 帧数。");
        }

        [Test]
        public void LayoutCapsWidthAndGrowsRowsPerFrame()
        {
            // 与现役 Male profile 同形：8192 顶点被 4096 宽切成每帧 2 行。
            Vector3Int layout = VatBakeUtility.CalculateLayout(8192, 188);
            Assert.AreEqual(4096, layout.x);
            Assert.AreEqual(2, layout.z, "8192 / 4096 = 每帧 2 行。");
            Assert.AreEqual(376, layout.y, "188 帧 × 2 行 = 376，与现有 Male profile 一致。");
        }

        [Test]
        public void LayoutRoundsPartialRowUp()
        {
            Vector3Int layout = VatBakeUtility.CalculateLayout(4097, 3);
            Assert.AreEqual(4096, layout.x);
            Assert.AreEqual(2, layout.z, "多出的 1 个顶点要占一整行。");
            Assert.AreEqual(6, layout.y);
        }

        [Test]
        public void LayoutRejectsOversizedOrInvalidInput()
        {
            Assert.Throws<ArgumentException>(() => VatBakeUtility.CalculateLayout(0, 10));
            Assert.Throws<ArgumentException>(() => VatBakeUtility.CalculateLayout(10, 0));
            Assert.Throws<ArgumentException>(() => VatBakeUtility.CalculateLayout(10, 10, 0));
            Assert.Throws<ArgumentException>(() => VatBakeUtility.CalculateLayout(10, 10, 20000));
            Assert.Throws<ArgumentException>(() => VatBakeUtility.CalculateLayout(100000, 700), "高度越过 16384 上限。");
            Assert.Throws<ArgumentException>(() => VatBakeUtility.CalculateLayout(4096, 16384), "超出 512 MiB 工作内存预算。");
        }

        // ------------------------------------------------------------------
        // 四段必填与参数边界：必须在采样前拒绝，不能烘出半成品
        // ------------------------------------------------------------------

        [Test]
        public void BakeRejectsMissingClipsBeforeSampling()
        {
            GameObject model = SpawnModel(out _);
            AnimationClip idle = NewClip("Idle", .5f);
            AnimationClip move = NewClip("Move", .2f);
            AnimationClip attack = NewClip("Attack", .3f);
            AnimationClip death = NewClip("Death", .4f);

            VatBakeRequest noIdle = NewRequest(model, idle, move, attack, death);
            noIdle.idle = null;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(noIdle), "缺 Idle 必须拒绝。");

            VatBakeRequest noMove = NewRequest(model, idle, move, attack, death);
            noMove.move = null;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(noMove), "缺 Move 必须拒绝。");

            VatBakeRequest noAttack = NewRequest(model, idle, move, attack, death);
            noAttack.attack = null;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(noAttack), "缺 Attack 必须拒绝。");

            VatBakeRequest noDeath = NewRequest(model, idle, move, attack, death);
            noDeath.death = null;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(noDeath), "缺 Death 必须拒绝。");
        }

        [Test]
        public void BakeRejectsInvalidParameters()
        {
            GameObject model = SpawnModel(out _);
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(null));

            VatBakeRequest nullModel = NewRequest(model, NewClip("i", .5f), NewClip("m", .2f), NewClip("a", .3f), NewClip("d", .4f));
            nullModel.model = null;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(nullModel));

            VatBakeRequest badRate = NewRequest(model, NewClip("i", .5f), NewClip("m", .2f), NewClip("a", .3f), NewClip("d", .4f));
            badRate.frameRate = 0;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(badRate));

            VatBakeRequest badRatio = NewRequest(model, NewClip("i", .5f), NewClip("m", .2f), NewClip("a", .3f), NewClip("d", .4f));
            badRatio.bakeLowLod = true; // 比例只在开启 Low LOD 时才校验，关掉的话这条会因错误的原因通过。
            badRatio.lowLodRatio = 0f;
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(badRatio));

            VatBakeRequest outsideExtra = NewRequest(model, NewClip("i", .5f), NewClip("m", .2f), NewClip("a", .3f), NewClip("d", .4f));
            var stranger = new GameObject("Stranger");
            spawned.Add(stranger);
            outsideExtra.extraRenderers = new[] { stranger.AddComponent<MeshRenderer>() };
            Assert.Throws<ArgumentException>(() => VatBaker.Bake(outsideExtra), "层级外的附件必须拒绝。");
        }

        // ------------------------------------------------------------------
        // 烘焙往返：布局、窗口、采样内容与 profile 字段
        // ------------------------------------------------------------------

        [Test]
        public void BakeProducesProfileMatchingRequestedLayout()
        {
            GameObject model = SpawnModel(out MeshRenderer quad);
            AnimationClip idle = NewClip("Idle", .5f);
            AnimationClip move = NewClip("Move", .2f);
            AnimationClip attack = NewClip("Attack", .3f);
            AnimationClip death = NewClip("Death", .4f);
            VatBakeRequest request = NewRequest(model, idle, move, attack, death);
            request.extraRenderers = new[] { quad };

            // 期望帧数从 clip 实际长度推导：float 长度乘帧率后取上整，不能硬编码整数。
            int idleFrames = ExpectedFrames(idle);
            int moveFrames = ExpectedFrames(move);
            int attackFrames = ExpectedFrames(attack);
            int deathFrames = ExpectedFrames(death);
            int totalFrames = idleFrames + moveFrames + attackFrames + deathFrames;

            using (VatBakeResult result = VatBaker.Bake(request))
            {
                VATProfile profile = result.Profile;
                Assert.AreEqual(30, profile.frameRate);
                Assert.AreEqual(4, profile.cleanMesh.vertexCount, "cleanMesh 顶点数应等于参与烘焙的网格顶点数。");
                Assert.AreEqual(totalFrames, profile.totalFrameCount, "总帧数应为四段之和。");
                Assert.AreEqual(4, profile.textureWidth);
                Assert.AreEqual(1, profile.rowsPerFrame);
                Assert.AreEqual(totalFrames, profile.textureHeight);

                // 窗口必须首尾相接、无空洞：这是 shader 按 currentState 选段的唯一依据。
                AssertWindow(profile.idle, "Idle", 0, idleFrames, true);
                AssertWindow(profile.move, "Move", idleFrames, moveFrames, true);
                AssertWindow(profile.attack, "Attack", idleFrames + moveFrames, attackFrames, true);
                AssertWindow(profile.death, "Death", idleFrames + moveFrames + attackFrames, deathFrames, false);

                Assert.AreEqual(profile.textureWidth, profile.positionTexture.width);
                Assert.AreEqual(profile.textureHeight, profile.positionTexture.height);
                Assert.AreEqual(TextureFormat.RGBAHalf, profile.positionTexture.format);
                Assert.AreEqual(FilterMode.Point, profile.positionTexture.filterMode, "VAT 采样必须用 Point，插值会串帧。");
                Assert.AreEqual(TextureWrapMode.Clamp, profile.positionTexture.wrapMode);
                Assert.IsFalse(profile.HasLowLod, "显式关闭 Low LOD 时不应产出 low 资产。");

                Assert.IsTrue(VatProfileValidation.TryValidate(profile, out string error), error);
            }
        }

        [Test]
        public void BakeSamplesAnimationPerFrameInsteadOfBindPose()
        {
            // 这条测试盯的是"父节点保持禁用还能不能采样"：若 SampleAnimation 在未激活层级上无效，
            // 所有帧的位置都会退化成绑定姿态，这里立刻会红。
            GameObject model = SpawnModel(out MeshRenderer quad);
            VatBakeRequest request = AnimatedRequest(model, quad);

            using (VatBakeResult result = VatBaker.Bake(request))
            {
                VATProfile profile = result.Profile;
                Color[] pixels = profile.positionTexture.GetPixels();
                int width = profile.textureWidth;
                int rows = profile.rowsPerFrame;

                Vector3 first = PixelAt(pixels, width, rows, 0, 0);
                Vector3 second = PixelAt(pixels, width, rows, 1, 0);
                Vector3 last = PixelAt(pixels, width, rows, 14, 0);

                Assert.Greater(second.x, first.x + 1e-3f, "相邻帧的采样位置必须不同：动画没有被应用。");
                Assert.Greater(last.x, second.x, "Idle 段的 X 位移应随帧推进单调增加。");
                Assert.AreEqual(1f, pixels[0].a, "位置纹理的 alpha 固定写 1。");

                // cleanMesh 存绑定姿态，逐帧位移由纹理提供；两者不能是同一份数据。
                Vector3 meshFirst = profile.cleanMesh.vertices[0];
                Assert.AreEqual(first.x, meshFirst.x, 1e-3f, "本 fixture 的绑定姿态与 Idle 首帧恰好重合。");
                Assert.Less(meshFirst.x, last.x - 1e-3f, "cleanMesh 不应被后续帧覆写。");
            }
        }

        [Test]
        public void CleanMeshStoresBindPoseRatherThanIdleFirstFrame()
        {
            // Low LOD 的顶点聚类以 cleanMesh 的几何范围做归一化。基准若取自 Idle 首帧，
            // 改一段 Idle 动画就会连带改掉远处 LOD 的网格拓扑。这条锁死"基准只取决于模型本身"。
            GameObject model = SpawnModel(out MeshRenderer quad);
            AnimationClip idle = NewClip("IdleOffset", .5f, animateX: true, startX: 2f);
            VatBakeRequest request = NewRequest(model, idle,
                NewClip("Move", .2f), NewClip("Attack", .3f), NewClip("Death", .4f));
            request.extraRenderers = new[] { quad };

            using (VatBakeResult result = VatBaker.Bake(request))
            {
                VATProfile profile = result.Profile;
                Vector3 frame0 = PixelAt(profile.positionTexture.GetPixels(),
                    profile.textureWidth, profile.rowsPerFrame, 0, 0);

                Assert.AreEqual(2f, frame0.x, 1e-3f, "测试前提：Idle 首帧应偏离绑定姿态。");
                Assert.AreEqual(0f, profile.cleanMesh.vertices[0].x, 1e-3f,
                    "cleanMesh 必须存绑定姿态，而不是 Idle 首帧。");
            }
        }

        [Test]
        public void BakeGeneratesLowLodPairedWithFullResolution()
        {
            GameObject model = SpawnModel(out MeshRenderer quad);
            VatBakeRequest request = AnimatedRequest(model, quad);
            request.bakeLowLod = true;
            request.lowLodRatio = 1f;
            request.lowLodMaxVertices = 0;

            using (VatBakeResult result = VatBaker.Bake(request))
            {
                VATProfile profile = result.Profile;
                Assert.IsTrue(profile.HasLowLod, "开启 Low LOD 后三个槽位与布局字段必须齐全。");
                Assert.LessOrEqual(profile.lowLodMesh.vertexCount, profile.cleanMesh.vertexCount);
                Assert.AreEqual(profile.lowLodTextureWidth, profile.lowLodPositionTexture.width);
                Assert.AreEqual(profile.lowLodTextureHeight, profile.lowLodPositionTexture.height);
                Assert.AreEqual(profile.lowLodRowsPerFrame * profile.totalFrameCount, profile.lowLodTextureHeight,
                    "Low LOD 高度同样 = 每帧行数 × 总帧数。");
                Assert.AreNotSame(profile.positionTexture, profile.lowLodPositionTexture, "两级 LOD 不能复用同一张纹理。");
                Assert.IsTrue(VatProfileValidation.TryValidate(profile, out string error), error);
            }
        }

        [Test]
        public void LowLodReducerRejectsCapThatCollapsesMesh()
        {
            // 顶点上限压得比网格能承受的还低时，减面会塌成空网格；必须报错而不是产出坏 profile。
            // 取 3 而不是 2：2 会在请求校验阶段就被挡下，走不到减面器，那样测试会因错误的原因通过。
            GameObject model = SpawnModel(out MeshRenderer quad);
            VatBakeRequest request = AnimatedRequest(model, quad);
            request.bakeLowLod = true;
            request.lowLodRatio = 1f;
            request.lowLodMaxVertices = 3;

            var error = Assert.Throws<ArgumentException>(() => VatBaker.Bake(request));
            StringAssert.Contains("减面", error.Message, "应当是减面塌陷导致的失败，而不是前置校验。");
        }

        // ------------------------------------------------------------------
        // profile 校验：配对、重叠与容量
        // ------------------------------------------------------------------

        [Test]
        public void ValidationAcceptsProfileWithoutMidLod()
        {
            // 现有 Male profile 就是 full + low 两级，mid 缺失是设计内行为，不能报错。
            VATProfile profile = NewValidProfile();
            Assert.IsFalse(profile.HasMidLod);
            Assert.IsTrue(VatProfileValidation.TryValidate(profile, out string error), error);
        }

        [Test]
        public void ValidationRejectsOverlappingClipWindows()
        {
            VATProfile profile = NewValidProfile();
            profile.attack = NewWindow("Attack", 10, 5, true);
            Assert.IsFalse(VatProfileValidation.TryValidate(profile, out string error));
            StringAssert.Contains("重叠", error);
        }

        [Test]
        public void ValidationRejectsWindowsBeyondTotalFrameCount()
        {
            VATProfile profile = NewValidProfile();
            profile.death = NewWindow("Death", 38, 10, false);
            Assert.IsFalse(VatProfileValidation.TryValidate(profile, out string error));
            StringAssert.Contains("Death", error);
        }

        [Test]
        public void ValidationRejectsUnpairedOrReusedLodTextures()
        {
            VATProfile unpaired = NewValidProfile();
            unpaired.lowLodPositionTexture = NewTexture(4, 42, "LowPos");
            unpaired.lowLodNormalTexture = NewTexture(4, 42, "LowNorm");
            unpaired.lowLodTextureWidth = 4;
            unpaired.lowLodTextureHeight = 42;
            unpaired.lowLodRowsPerFrame = 1;
            unpaired.lowLodMesh = null;
            Assert.IsFalse(VatProfileValidation.TryValidate(unpaired, out string unpairedError), "缺网格但留着纹理必须报错。");
            StringAssert.Contains("配对", unpairedError);

            VATProfile reused = NewValidProfile();
            reused.normalTexture = reused.positionTexture;
            Assert.IsFalse(VatProfileValidation.TryValidate(reused, out string reusedError));
            StringAssert.Contains("复用", reusedError);
        }

        [Test]
        public void ValidationRejectsTextureTooSmallForVertices()
        {
            VATProfile profile = NewValidProfile();
            profile.textureWidth = 2;
            profile.rowsPerFrame = 1;
            profile.textureHeight = 42;
            profile.positionTexture = NewTexture(2, 42, "TinyPos");
            profile.normalTexture = NewTexture(2, 42, "TinyNorm");
            Assert.IsFalse(VatProfileValidation.TryValidate(profile, out string error));
            StringAssert.Contains("容量", error);
        }

        [Test]
        public void BakeAcceptsPrefabAssetAsModel()
        {
            // 回归：prefab 作为资产加载时整棵树 activeInHierarchy == false，
            // 早先的渲染器筛选会因此判定"没有蒙皮渲染器"，把 M5 最主要的输入挡在门外。
            const string prefabPath = "Assets/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "测试前提：源模型 prefab 必须存在。");
            Assert.IsFalse(prefab.activeInHierarchy, "测试前提：资产形态的 prefab 不在任何活动场景里。");

            VatBakeRequest request = NewRequest(prefab,
                NewClip("Idle", .1f), NewClip("Move", .1f), NewClip("Attack", .1f), NewClip("Death", .1f));
            request.bakeLowLod = false;
            int expectedFrames = ExpectedFrames(request.idle) * 4;

            using (VatBakeResult result = VatBaker.Bake(request))
            {
                Assert.Greater(result.Profile.cleanMesh.vertexCount, 0, "应当能从 prefab 资产采出顶点。");
                Assert.AreEqual(expectedFrames, result.Profile.totalFrameCount);
            }
        }

        // ------------------------------------------------------------------
        // 资产保存：只新建、不覆盖
        // ------------------------------------------------------------------

        [Test]
        public void SaveNewWritesReloadableAssetAndRefusesSecondSave()
        {
            EnsureTempFolder();
            GameObject model = SpawnModel(out MeshRenderer quad);
            VatBakeRequest request = AnimatedRequest(model, quad);
            int expectedFrames = ExpectedFrames(request.idle) + ExpectedFrames(request.move)
                + ExpectedFrames(request.attack) + ExpectedFrames(request.death);

            string path = TempFolder + "/RoundTrip.asset";
            using (VatBakeResult result = VatBaker.Bake(request))
            {
                VATProfile saved = result.SaveNew(path);
                Assert.IsNotNull(saved);
                Assert.IsTrue(EditorUtility.IsPersistent(saved), "保存后主资产必须是持久化对象。");
                Assert.Throws<InvalidOperationException>(() => result.SaveNew(TempFolder + "/Again.asset"),
                    "同一个烘焙结果不能重复保存。");

                VATProfile reloaded = AssetDatabase.LoadAssetAtPath<VATProfile>(path);
                Assert.AreSame(saved, reloaded, "资产应能按路径原样载回。");
                Assert.AreEqual(expectedFrames, reloaded.totalFrameCount);
                Assert.AreEqual(4, reloaded.cleanMesh.vertexCount);
                Assert.IsNotNull(reloaded.positionTexture, "位置纹理应作为子资产一并保存。");
                Assert.IsTrue(VatProfileValidation.TryValidate(reloaded, out string error), error);
            }
        }

        [Test]
        public void SaveNewRejectsPathsOutsideAssetsOrAlreadyTaken()
        {
            Assert.Throws<ArgumentException>(() => VatBakeResult.ValidateNewPath("Temp/Outside.asset"));
            Assert.Throws<ArgumentException>(() => VatBakeResult.ValidateNewPath("Assets/VatBakerTestTemp/../Escape.asset"));
            Assert.Throws<ArgumentException>(() => VatBakeResult.ValidateNewPath("Assets/VatBakerTestTemp/NoExtension"));
            Assert.Throws<ArgumentException>(() => VatBakeResult.ValidateNewPath("Assets/MissingFolderHere/New.asset"));
            Assert.Throws<ArgumentException>(() => VatBakeResult.ValidateNewPath(
                "Assets/VAT_Data/MaleCharacter_Stage5_MultiClip_Profile.asset"), "已存在的 profile 必须拒绝覆盖。");
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static void AssertWindow(VATProfile.VATClipWindow window, string label, int start, int count, bool loop)
        {
            Assert.AreEqual(label, window.label);
            Assert.AreEqual(start, window.startFrame, label + " 起始帧");
            Assert.AreEqual(count, window.frameCount, label + " 帧数");
            Assert.AreEqual(30, window.frameRate, label + " 帧率");
            Assert.AreEqual(loop, window.loop, label + " 循环标记");
        }

        /// <summary>
        /// 复刻 <c>VatBaker.BuildWindows</c> 的帧数公式，用于独立推导期望值。
        /// 必须用 float 乘法：既有资产是按 float 口径烘的，转 double 会把
        /// 0.5333s 这类长度的 16 帧算成 17 帧。
        /// </summary>
        private static int ExpectedFrames(AnimationClip clip, int rate = 30)
        {
            return Mathf.Max(1, Mathf.CeilToInt(clip.length * rate));
        }

        [Test]
        public void FrameCountUsesFloatArithmeticToMatchExistingAssets()
        {
            // 回归：源素材里 0.5333s 的 clip，float 下 length*30 落在 16.0，double 下是 16.0000008。
            // 用 double 取上整会多出 1 帧，逐段累积后整份 profile 的帧窗口全部错位。
            // 这条必须用真实 FBX：合成 clip 的长度落不到这个临界点上，复现不出差异。
            const string clipPath = "Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/Attack01_SwordAndShiled.fbx";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip, "测试前提：源动画 clip 必须存在。");

            float asFloat = clip.length * 30f;
            double asDouble = (double)clip.length * 30d;
            Assert.LessOrEqual(asFloat, 16f, "测试前提：float 乘积应落在 16 或略低。");
            Assert.Greater(asDouble, 16d, "测试前提：double 乘积应超过 16，差异才成立。");
            Assert.AreEqual(16, Mathf.CeilToInt(asFloat), "float 口径取 16 帧。");
            Assert.AreEqual(17, (int)Math.Ceiling(asDouble), "double 口径会错取 17 帧。");

            GameObject model = SpawnModel(out MeshRenderer quad);
            VatBakeRequest request = NewRequest(model, clip, clip, clip, clip);
            request.extraRenderers = new[] { quad };

            using (VatBakeResult result = VatBaker.Bake(request))
            {
                Assert.AreEqual(16, result.Profile.idle.frameCount, "应按 float 口径取 16 帧，而不是 double 的 17。");
                Assert.AreEqual(64, result.Profile.totalFrameCount, "四段各 16 帧 = 64；按 double 口径会变成 68。");
            }
        }

        private static Vector3 PixelAt(Color[] pixels, int width, int rowsPerFrame, int frame, int vertex)
        {
            int index = (frame * rowsPerFrame + vertex / width) * width + vertex % width;
            Color color = pixels[index];
            return new Vector3(color.r, color.g, color.b);
        }

        private static VATProfile.VATClipWindow NewWindow(string label, int start, int count, bool loop)
        {
            return new VATProfile.VATClipWindow
            {
                label = label,
                startFrame = start,
                frameCount = count,
                frameRate = 30,
                loop = loop
            };
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "VatBakerTestTemp");
        }

        /// <summary>四段都带 X 位移动画的请求，用于验证逐帧采样与 LOD 生成。</summary>
        private VatBakeRequest AnimatedRequest(GameObject model, MeshRenderer quad)
        {
            VatBakeRequest request = NewRequest(model,
                NewClip("Idle", .5f, animateX: true), NewClip("Move", .2f, animateX: true),
                NewClip("Attack", .3f, animateX: true), NewClip("Death", .4f, animateX: true));
            request.extraRenderers = new[] { quad };
            return request;
        }

        private VatBakeRequest NewRequest(GameObject model, AnimationClip idle, AnimationClip move,
            AnimationClip attack, AnimationClip death)
        {
            return new VatBakeRequest
            {
                model = model,
                idle = idle,
                move = move,
                attack = attack,
                death = death,
                frameRate = 30,
                bakeLowLod = false
            };
        }

        /// <summary>
        /// 造一个带子网格的模型。子节点由 clip 曲线驱动（曲线路径写死 "Quad"），
        /// 所以这里不需要预置任何姿态 —— 绑定姿态就是原点。
        /// </summary>
        private GameObject SpawnModel(out MeshRenderer quad)
        {
            var root = new GameObject("VatTestModel");
            spawned.Add(root);
            var child = new GameObject("Quad");
            child.transform.SetParent(root.transform, false);
            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = NewQuadMesh();
            quad = child.AddComponent<MeshRenderer>();
            return root;
        }

        private Mesh NewQuadMesh()
        {
            var mesh = new Mesh { name = "VatTestQuad" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(0f, 1f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            created.Add(mesh);
            return mesh;
        }

        /// <summary>
        /// 造一段时长确定的 clip。<c>new AnimationClip()</c> 没有曲线时 length 恒为 0，
        /// 帧数推导会全塌成 1，所以这里始终写一条曲线来撑出时长；<paramref name="animateX"/>
        /// 决定它是真位移还是常量曲线，<paramref name="startX"/> 用来把首帧推离绑定姿态。
        /// </summary>
        private AnimationClip NewClip(string name, float length, bool animateX = false, float startX = 0f)
        {
            var clip = new AnimationClip { name = name, legacy = false };
            float end = animateX ? 5f : startX;
            var curve = new AnimationCurve(new Keyframe(0f, startX), new Keyframe(length, end));
            clip.SetCurve("Quad", typeof(Transform), "localPosition.x", curve);
            Assert.AreEqual(length, clip.length, 1e-3f, name + " 的时长没有建立起来，测试前提不成立。");
            created.Add(clip);
            return clip;
        }

        private VATProfile NewValidProfile()
        {
            var profile = ScriptableObject.CreateInstance<VATProfile>();
            created.Add(profile);

            var mesh = new Mesh { name = "ValidProfileMesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            created.Add(mesh);

            profile.cleanMesh = mesh;
            profile.textureWidth = 4;
            profile.textureHeight = 42;
            profile.rowsPerFrame = 1;
            profile.totalFrameCount = 42;
            profile.frameRate = 30;
            profile.positionTexture = NewTexture(4, 42, "ValidPos");
            profile.normalTexture = NewTexture(4, 42, "ValidNorm");
            profile.idle = NewWindow("Idle", 0, 15, true);
            profile.move = NewWindow("Move", 15, 6, true);
            profile.attack = NewWindow("Attack", 21, 9, true);
            profile.death = NewWindow("Death", 30, 12, false);
            return profile;
        }

        private Texture2D NewTexture(int width, int height, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true) { name = name };
            created.Add(texture);
            return texture;
        }
    }
}
