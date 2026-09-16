using System;
using System.Collections.Generic;
using MassEngine.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>
    /// M5.2 兵种绑定核心的 EditMode 测试。
    ///
    /// 重点是最后那条反查：向导的 LOD 落位规则必须与运行时
    /// <c>ResolvedUnitTypeRuntime.Resolve</c> 完全一致 —— 否则会出现
    /// "向导接了一套、运行时用另一套"，画面花了却查不出原因。
    /// </summary>
    public sealed class UnitTypeBinderTests
    {
        private const string TempRoot = "Assets/UnitTypeBinderTestTemp";

        private readonly List<Object> created = new List<Object>();
        private readonly List<string> tempFolders = new List<string>();
        private ScenarioConfig scenario;
        private UnitTypeConfig template;

        [SetUp]
        public void SetUp()
        {
            // 每个测试一份干净的清单 + 模板，避免测试之间互相污染。
            scenario = NewScenario();
            template = NewTemplate();
            scenario.unitTypes = new[] { template };
        }

        [TearDown]
        public void TearDown()
        {
            // 先删临时目录：模板与新兵种都是落盘资产，DestroyImmediate 不允许销毁持久化对象。
            foreach (string folder in tempFolders)
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
            tempFolders.Clear();

            // 根目录也要清掉，否则会留下一个空的 UnitTypeBinderTestTemp 与它的 .meta。
            if (AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.DeleteAsset(TempRoot);

            foreach (Object asset in created)
            {
                if (asset == null)
                    continue;
                if (EditorUtility.IsPersistent(asset))
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    if (!string.IsNullOrEmpty(path))
                        AssetDatabase.DeleteAsset(path);
                }
                else
                    Object.DestroyImmediate(asset);
            }
            created.Clear();
        }

        // ------------------------------------------------------------------
        // LOD 落位：三级 / 两级 / 单级
        // ------------------------------------------------------------------

        [Test]
        public void ResolveLodMeshesUsesLowLodForBothMidAndFarWhenMidIsMissing()
        {
            // 现役 Male profile 就是 full + low 两级（没有 mid）。
            VatProfileData data = Profile(clean: "Clean", mid: null, low: "Low").Data;
            UnitTypeBinder.ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);

            Assert.AreEqual("Clean", near.name);
            Assert.AreEqual("Low", mid.name, "缺 mid 时 mid 槽位应退到 lowLod。");
            Assert.AreEqual("Low", far.name);
        }

        [Test]
        public void ResolveLodMeshesUsesThreeTiersWhenAllPresent()
        {
            // 现役 Female profile 是三级齐全。
            VatProfileData data = Profile(clean: "Clean", mid: "Mid", low: "Low").Data;
            UnitTypeBinder.ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);

            Assert.AreEqual("Clean", near.name);
            Assert.AreEqual("Mid", mid.name);
            Assert.AreEqual("Low", far.name);
        }

        [Test]
        public void ResolveLodMeshesFallsBackToCleanMeshForSingleLod()
        {
            VatProfileData data = Profile(clean: "Clean", mid: null, low: null).Data;
            UnitTypeBinder.ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);

            Assert.AreEqual("Clean", near.name);
            Assert.AreEqual("Clean", mid.name, "只有一级时 mid 应退到 cleanMesh。");
            Assert.AreEqual("Clean", far.name, "只有一级时 far 应退到 cleanMesh。");
        }

        // ------------------------------------------------------------------
        // 接线：三个槽位必须整体覆盖
        // ------------------------------------------------------------------

        [Test]
        public void ApplyProfileOverwritesEverySlotSoStaleReferencesCannotSurvive()
        {
            // 先绑一个三级 profile，再绑一个两级 profile：
            // far 若不被覆盖就会留着上一份的 lowLod，指向错误的顶点顺序。
            RenderConfig render = NewRenderConfig();
            VatProfileData threeTier = Profile(clean: "CleanA", mid: "MidA", low: "LowA").Data;
            UnitTypeBinder.ApplyProfile(render, NewProfileAsset("ThreeTier"), threeTier);
            Assert.AreEqual("MidA", render.midMesh.name);

            VatProfileData twoTier = Profile(clean: "CleanB", mid: null, low: "LowB").Data;
            UnitTypeBinder.ApplyProfile(render, NewProfileAsset("TwoTier"), twoTier);

            Assert.AreEqual("CleanB", render.nearMesh.name);
            Assert.AreEqual("LowB", render.midMesh.name, "mid 必须被改写，不能留着上一份的 MidA。");
            Assert.AreEqual("LowB", render.farMesh.name, "far 必须被改写，不能留着上一份的 LowA。");
        }

        [Test]
        public void BindRejectsUnpersistedProfileBeforeItCanBlankOutTheRenderConfig()
        {
            // Bind 是向导「绑定选中兵种」走的路径。它直接改既有资产，
            // 未落盘的 profile 写进去之后，磁盘上是空引用 —— 而且会连带把
            // 三个网格槽位一起清空，把一份本来完好的兵种改坏。
            UnitTypeConfig config = BoundUnitType(out RenderConfig render, out _);
            Mesh meshBefore = render.nearMesh;

            var loose = ScriptableObject.CreateInstance<VATProfile>();
            created.Add(loose);
            Assert.IsFalse(AssetDatabase.Contains(loose), "测试前提：这个 profile 未落盘。");

            Assert.Throws<ArgumentException>(() => UnitTypeBinder.Bind(config, loose),
                "未落盘的 profile 必须在写坏 RenderConfig 之前就被拒绝。");
            Assert.AreSame(meshBefore, render.nearMesh, "被拒绝时不应改动任何槽位。");
        }

        // ------------------------------------------------------------------
        // 校验：错配必须报错，正确绑定必须通过
        // ------------------------------------------------------------------

        [Test]
        public void ValidateBindingAcceptsCorrectlyBoundUnitType()
        {
            UnitTypeConfig config = BoundUnitType(out _, out _);
            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsTrue(result.IsValid, "正确绑定不应报错：" + string.Join(" / ", result.Errors));
        }

        [Test]
        public void ValidateBindingRejectsMeshThatDiffersFromTheProfileMesh()
        {
            // 顶点顺序不同的网格会采样到错位顶点，画面上是花掉的模型而不是异常，
            // 所以必须在这里拦下来。
            UnitTypeConfig config = BoundUnitType(out RenderConfig render, out _);
            render.farMesh = NewMesh("SomeOtherMesh");

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsFalse(result.IsValid, "farMesh 与 profile 网格不一致必须报错。");
            StringAssert.Contains("farMesh", string.Join(" / ", result.Errors));
        }

        [Test]
        public void ValidateBindingAcceptsEmptyMeshSlots()
        {
            // 槽位留空是合法的：运行时会用 profile 的网格兜底。
            UnitTypeConfig config = BoundUnitType(out RenderConfig render, out _);
            render.nearMesh = null;
            render.midMesh = null;
            render.farMesh = null;

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsTrue(result.IsValid, "空槽位应交给运行时兜底，不算错误：" + string.Join(" / ", result.Errors));
        }

        [Test]
        public void ValidateBindingRejectsMissingProfile()
        {
            UnitTypeConfig config = BoundUnitType(out RenderConfig render, out _);
            render.vatProfile = null;

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("profile", string.Join(" / ", result.Errors));
        }

        [Test]
        public void ValidateBindingRejectsEmptyMaterialSoTheWholeLodTierCannotSilentlyVanish()
        {
            // 运行时 MassGpuRenderDispatcher.DrawLod 按 LOD 档位取材质，任一档为空就整档 return，
            // 只警告一次 —— 画面上是"某个距离上的单位凭空消失"，不是报错。
            // 早先校验只查网格槽位，材质全空的兵种会被判为完全合法。
            UnitTypeConfig config = BoundUnitType(out RenderConfig render, out _);
            render.midMaterial = null;

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsFalse(result.IsValid, "材质为空必须报错。");
            StringAssert.Contains("midMaterial", string.Join(" / ", result.Errors));
        }

        [Test]
        public void EmptyMaterialsReachTheRuntimeAsTiersThatDrawNothing()
        {
            // 反查：校验放行 == 运行时真的画不出来。把"校验漏判"与"画面后果"钉在一起，
            // 免得以后有人删掉材质检查而测试仍然全绿。
            UnitTypeConfig config = BoundUnitType(out RenderConfig render, out _);
            render.nearMaterial = null;
            render.midMaterial = null;
            render.farMaterial = null;

            ResolvedUnitTypeRuntime runtime = ResolvedUnitTypeRuntime.Resolve(config, 1.5f);
            Assert.IsNull(runtime.GetMaterial(0), "运行时近景材质应为空。");
            Assert.IsNull(runtime.GetMaterial(1), "运行时中景材质应为空。");
            Assert.IsNull(runtime.GetMaterial(2), "运行时远景材质应为空。");

            // 网格仍由 profile 兜底，所以"消失"不会伴随任何网格相关的报错 —— 这正是它难查的原因。
            Assert.IsNotNull(runtime.GetMesh(0), "网格仍由 profile 兜底，问题只表现为不绘制。");

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);
            Assert.IsFalse(result.IsValid, "运行时画不出来的配置必须被校验拦下。");
            string errors = string.Join(" / ", result.Errors);
            StringAssert.Contains("nearMaterial", errors);
            StringAssert.Contains("midMaterial", errors);
            StringAssert.Contains("farMaterial", errors);
        }

        [Test]
        public void ValidateBindingRejectsInvertedAnimationRateRange()
        {
            // Range 特性只在 Inspector 里拦，代码路径（向导、批处理、方案导入）必须自己查。
            UnitTypeConfig config = BoundUnitType(out _, out AnimationConfig animation);
            animation.moveAnimationSpeedMin = 1.4f;
            animation.moveAnimationSpeedMax = 0.9f;

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsFalse(result.IsValid, "速率区间倒置必须报错。");
            StringAssert.Contains("倒置", string.Join(" / ", result.Errors));
        }

        [Test]
        public void ValidateBindingStillReportsBaselineUnitTypeErrors()
        {
            // 绑定校验不能把既有兵种契约校验盖掉：teamId 越界仍须报错。
            UnitTypeConfig config = BoundUnitType(out _, out _);
            config.teamId = ConfigValidator.MaxTeamId + 1;

            ValidationResult result = UnitTypeBinder.ValidateBinding(config);

            Assert.IsFalse(result.IsValid, "teamId 越界必须仍然报错。");
            StringAssert.Contains("teamId", string.Join(" / ", result.Errors));
        }

        // ------------------------------------------------------------------
        // 误报检查：现役 6 个内置兵种必须全部通过校验
        // ------------------------------------------------------------------

        [Test]
        public void ValidateBindingReportsNoErrorsForEveryShippedUnitType()
        {
            // 校验器的价值取决于它不会误报：如果连仓库里现役的兵种都判不合法，
            // 用户就会学会忽略它，真正的问题反而被淹没。
            // 这条同时锁住"现役资产结构 = 向导认可的绑定形态"。
            const string scenarioPath = "Assets/Game/Settings/ScenarioConfig.asset";
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(scenarioPath);
            Assert.IsNotNull(scenario, "测试前提：现役 ScenarioConfig 必须存在。");
            Assert.IsNotEmpty(scenario.unitTypes, "测试前提：现役清单不能为空。");

            foreach (UnitTypeConfig unit in scenario.unitTypes)
            {
                Assert.IsNotNull(unit, "现役清单里不应有空引用。");
                ValidationResult result = UnitTypeBinder.ValidateBinding(unit);
                Assert.IsTrue(result.IsValid,
                    "现役兵种「" + unit.unitTypeName + "」不应被判为不合法，实际报错：" +
                    string.Join(" / ", result.Errors));
            }
        }

        [Test]
        public void ValidateBindingReportsNoErrorsForEveryShippedRenderConfigMeshPairing()
        {
            // 上一条走的是完整绑定校验；这条单独盯住最容易误报的一环 ——
            // 三个网格槽位与 profile 烘制网格的配对。现役 Female profile 是三级 LOD、
            // Male 是两级，两者的落位规则不同，都要判为合法。
            int checkedProfiles = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UnitTypeConfig", new[] { "Assets/Game/Settings" }))
            {
                var unit = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (unit == null || unit.renderConfig == null || unit.renderConfig.vatProfile == null)
                    continue;

                ValidationResult result = UnitTypeBinder.ValidateBinding(unit);
                foreach (string error in result.Errors)
                    Assert.IsFalse(error.Contains("不是同一个"),
                        "兵种「" + unit.unitTypeName + "」的网格槽位被误判为错配：" + error);
                checkedProfiles++;
            }

            Assert.Greater(checkedProfiles, 0, "测试前提：至少要检查到一个绑定了 profile 的现役兵种。");
        }

        [Test]
        public void ShippedRosterSharesOneRenderConfigPerArmyWhichIsWhyBindingAffectsBothUnitTypes()
        {
            // 这条记录的是现役结构事实：同一军团的近战+远程共用一份 RenderConfig（共用一个模型）。
            // 所以"给某个兵种绑 profile"实际会改到共享资产、同时影响另一个兵种 ——
            // 向导据此显示影响面警告。若哪天结构变了，这条会红，提示去改向导的提示文案。
            const string scenarioPath = "Assets/Game/Settings/ScenarioConfig.asset";
            var scenario = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(scenarioPath);
            Assert.IsNotNull(scenario, "测试前提：现役 ScenarioConfig 必须存在。");

            var byRender = new Dictionary<RenderConfig, int>();
            foreach (UnitTypeConfig unit in scenario.unitTypes)
            {
                if (unit == null || unit.renderConfig == null)
                    continue;
                byRender.TryGetValue(unit.renderConfig, out int count);
                byRender[unit.renderConfig] = count + 1;
            }

            Assert.Greater(byRender.Count, 0, "测试前提：现役兵种应当都配了 RenderConfig。");
            bool anyShared = false;
            foreach (KeyValuePair<RenderConfig, int> pair in byRender)
                if (pair.Value > 1)
                    anyShared = true;

            Assert.IsTrue(anyShared,
                "现役结构里应当存在被多个兵种共用的 RenderConfig；若已改成每个兵种独占，" +
                "请同步移除向导里的共用警告。");
        }

        // ------------------------------------------------------------------
        // 反查：向导的落位规则 == 运行时的落位规则
        // ------------------------------------------------------------------
        [Test]
        public void BinderSlotsMatchWhatTheRuntimeActuallyResolves()
        {
            // 这条是本文件的核心：向导算出的 near/mid/far 必须与
            // ResolvedUnitTypeRuntime.Resolve 实际采用的网格逐一相同。
            // 两边任何一处漂移（比如运行时改了 mid 的回退顺序）都会在这里红。
            foreach (bool mid in new[] { true, false })
            {
                foreach (bool low in new[] { true, false })
                {
                    UnitTypeConfig config = BoundUnitType(out RenderConfig render,
                        out _, clean: "Clean", mid: mid ? "Mid" : null, low: low ? "Low" : null);

                    Assert.IsTrue(UnitTypeBinder.TryReadProfile(render.vatProfile, out VatProfileData data, out string readError),
                        readError);
                    UnitTypeBinder.ResolveLodMeshes(data, out Mesh binderNear, out Mesh binderMid, out Mesh binderFar);

                    ResolvedUnitTypeRuntime runtime = ResolvedUnitTypeRuntime.Resolve(config, 1.5f);

                    string label = "mid=" + mid + " low=" + low + "：";
                    Assert.AreEqual(runtime.nearMesh, binderNear, label + "near 落位应与运行时一致。");
                    Assert.AreEqual(runtime.midMesh, binderMid, label + "mid 落位应与运行时一致。");
                    Assert.AreEqual(runtime.farMesh, binderFar, label + "far 落位应与运行时一致。");
                }
            }
        }

        // ------------------------------------------------------------------
        // 建整套兵种：资产落盘、登记清单、失败回滚
        // ------------------------------------------------------------------

        [Test]
        public void CreateUnitTypeWritesAssetsAndRegistersIntoScenarioRoster()
        {
            // 兵种不登记进清单运行时根本看不到（运行时按清单取兵种，不扫文件夹），
            // 所以"建了资产但没登记"必须被这条测出来。
            UnitTypeConfig created = CreateIntoTempFolder("New Spearman", 2, out ScenarioConfig scenario, out string folder);

            Assert.IsNotNull(created);
            Assert.AreEqual("New Spearman", created.unitTypeName);
            Assert.AreEqual(2, created.teamId);
            Assert.IsTrue(AssetDatabase.Contains(created), "兵种资产必须落盘。");
            Assert.IsTrue(AssetDatabase.Contains(created.spawnConfig), "SpawnConfig 必须落盘。");
            Assert.IsTrue(AssetDatabase.Contains(created.combatConfig), "CombatConfig 必须落盘。");

            Assert.Contains(created, scenario.unitTypes, "新兵种必须登记进战役清单。");
            Assert.IsTrue(AssetDatabase.GetAssetPath(created).StartsWith(folder), "资产应落在指定目录。");
        }

        [Test]
        public void CreateUnitTypeKeepsSpawnAndCombatExclusiveButSharesTemplateSubConfigs()
        {
            // 现役约定：出生/战斗各自独占，移动/群体/动画按模板共享。
            // 若把共享项也复制一份，同一军团的两个兵种会静默解耦，改一个不再影响另一个。
            UnitTypeConfig created = CreateIntoTempFolder("Clone", 1, out _, out _);
            Assert.IsNotNull(created);

            Assert.AreNotSame(template.spawnConfig, created.spawnConfig, "SpawnConfig 必须独占。");
            Assert.AreNotSame(template.combatConfig, created.combatConfig, "CombatConfig 必须独占。");
            Assert.AreSame(template.movementConfig, created.movementConfig, "MovementConfig 按约定共享。");
            Assert.AreSame(template.flockingConfig, created.flockingConfig, "FlockingConfig 按约定共享。");
            Assert.AreSame(template.animationConfig, created.animationConfig, "AnimationConfig 按约定共享。");
        }

        [Test]
        public void CreateUnitTypeRollsBackEveryAssetWhenProfileCannotBeRead()
        {
            // 失败必须整体回滚：磁盘上留半成品比直接报错更难查。
            //
            // profile 必须是已落盘资产才会走到"读不出来"这一步 ——
            // 未落盘的对象会被 TryValidateRequest 的 Contains 守卫提前拒掉，
            // 那样这条测试就测不到回滚了（夹具必须走在真实路径上）。
            string profileFolder = NewTempFolder();
            var unreadable = ScriptableObject.CreateInstance<ScriptableObject>();
            created.Add(unreadable);
            AssetDatabase.CreateAsset(unreadable, profileFolder + "/Unreadable.asset");
            Assert.IsTrue(AssetDatabase.Contains(unreadable), "测试前提：不可读的 profile 也必须已落盘。");

            string folder = NewTempFolder();
            int before = CountAssets(folder);

            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = NewScenario(),
                template = template,
                unitTypeName = "Doomed",
                teamId = 0,
                directory = folder,
                profile = unreadable
            }, out string error);

            Assert.IsNull(result, "profile 不可读时必须失败。");
            Assert.IsNotNull(error);
            Assert.AreEqual(before, CountAssets(folder), "失败后不应留下任何新资产。");
        }

        [Test]
        public void CreateUnitTypeRejectsUnpersistedProfileInsteadOfWritingAnEmptyReference()
        {
            // 与"模板必须已落盘"同源：AssetDatabase.CreateAsset 按调用当时的引用状态序列化，
            // 未落盘的 profile 在磁盘上会被写成 vatProfile: {fileID: 0}，
            // 并且它带过去的三个网格槽位也一起变空 —— 内存与 Inspector 都正常，
            // 域重载后才暴露成"兵种没有动画"。
            UnitTypeConfig config = BoundUnitType(out _, out _);
            ScriptableObject transientProfile = config.renderConfig.vatProfile;

            // 夹具里的 profile 是落盘的；这里另造一个确定未落盘的。
            var loose = ScriptableObject.CreateInstance<VATProfile>();
            created.Add(loose);
            Assert.IsFalse(AssetDatabase.Contains(loose), "测试前提：这个 profile 未落盘。");
            Assert.IsTrue(AssetDatabase.Contains(transientProfile), "测试前提：夹具的 profile 已落盘。");

            string folder = NewTempFolder();
            int assetsBefore = CountAssets(folder);
            ScenarioConfig freshScenario = NewScenario();
            int rosterBefore = freshScenario.unitTypes.Length;

            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = freshScenario,
                template = template,
                unitTypeName = "EmptyRef",
                teamId = 1,
                directory = folder,
                profile = loose
            }, out string error);

            Assert.IsNull(result, "未落盘的 profile 必须被拒绝。");
            StringAssert.Contains("已保存", error);
            Assert.AreEqual(assetsBefore, CountAssets(folder), "失败后不应留下任何新资产。");
            Assert.AreEqual(rosterBefore, freshScenario.unitTypes.Length, "失败时不应改动战役清单。");
        }

        [Test]
        public void CreateUnitTypeRefusesToBindProfileIntoASharedRenderConfig()
        {
            // 共享的 RenderConfig 属于模板兵种：往里写 profile 会把模板兵种也换掉模型。
            ScenarioConfig scenario = NewScenario();
            UnitTypeConfig before = scenario.unitTypes.Length > 0 ? scenario.unitTypes[0] : null;
            int rosterBefore = scenario.unitTypes.Length;

            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = scenario,
                template = template,
                unitTypeName = "Shared",
                teamId = 0,
                directory = NewTempFolder(),
                profile = Profile("C", null, null).Asset,
                exclusiveRender = false
            }, out string error);

            Assert.IsNull(result, "共享 RenderConfig 时绑 profile 必须被拒绝。");
            StringAssert.Contains("独占", error);
            Assert.AreEqual(rosterBefore, scenario.unitTypes.Length, "失败时不应改动战役清单。");
            Assert.AreSame(before, scenario.unitTypes.Length > 0 ? scenario.unitTypes[0] : null);
        }

        [Test]
        public void CreateUnitTypeFallsBackToAFreshExclusiveRenderConfigWhenTemplateHasNone()
        {
            // 模板没有 RenderConfig、但要求独占并绑 profile 时，应当造一份全新的独占 RenderConfig，
            // 把 profile 接进去，而不是失败 —— 也不该去碰模板（模板根本没有可碰的渲染配置）。
            //
            // 注意模板必须已落盘：未落盘的模板会被 AssetDatabase.Contains 那道守卫提前拒掉，
            // 那样这条测试就测不到 RenderConfig 回退分支了（早先正是踩了这个坑）。
            string folder = NewTempFolder();
            var bare = ScriptableObject.CreateInstance<UnitTypeConfig>();
            bare.unitTypeName = "NoRender";
            bare.teamId = 0;
            AssetDatabase.CreateAsset(bare, folder + "/Bare.asset");
            created.Add(bare);
            Assert.IsTrue(AssetDatabase.Contains(bare), "测试前提：模板必须已落盘。");

            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = scenario,
                template = bare,
                unitTypeName = "FreshRender",
                teamId = 1,
                directory = folder,
                profile = Profile("C", null, null).Asset
            }, out string error);

            Assert.IsNotNull(result, "模板没有 RenderConfig 时应回退造一份独占的，而不是失败：" + error);
            Assert.IsNotNull(result.renderConfig, "应当回退生成 RenderConfig。");
            Assert.AreNotSame(bare.renderConfig, result.renderConfig, "回退生成的必须是新对象。");
            Assert.IsNotNull(result.renderConfig.vatProfile, "profile 应当已接进回退生成的 RenderConfig。");
            Assert.IsTrue(AssetDatabase.Contains(result.renderConfig), "回退生成的 RenderConfig 必须落盘。");
            Assert.Contains(result, scenario.unitTypes, "新建的兵种必须登记进清单。");

            // 回退路径没有可继承的材质来源，所以三个材质槽位必然是空的 —— 这份兵种此刻画不出来。
            // 记录这个事实，并确认校验会如实报出来（向导会把它显示给用户），
            // 而不是让用户以为"建好了就能上战场"。
            Assert.IsNull(result.renderConfig.nearMaterial, "测试前提：回退生成的 RenderConfig 没有材质来源。");
            ValidationResult validation = UnitTypeBinder.ValidateBinding(result);
            Assert.IsFalse(validation.IsValid, "材质未填的兵种必须被判为不合法。");
            StringAssert.Contains("Material", string.Join(" / ", validation.Errors));
        }

        [Test]
        public void CreateUnitTypeRejectsUnpersistedTemplateInsteadOfCreatingOrphans()
        {
            // 模板未落盘时必须拒绝：创建流程要从模板复制子配置，
            // 未落盘的模板复制出来的东西也落不了盘，只会留下一堆孤儿资产。
            string folder = NewTempFolder();
            var transient = ScriptableObject.CreateInstance<UnitTypeConfig>();
            transient.unitTypeName = "Transient";
            transient.teamId = 0;
            created.Add(transient);

            int rosterBefore = scenario.unitTypes.Length;
            int assetsBefore = CountAssets(folder);

            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = scenario,
                template = transient,
                unitTypeName = "Orphan",
                teamId = 0,
                directory = folder,
                profile = Profile("C", null, null).Asset
            }, out string error);

            Assert.IsNull(result, "未落盘的模板必须被拒绝。");
            Assert.IsNotNull(error);
            Assert.AreEqual(rosterBefore, scenario.unitTypes.Length, "失败时不应改动战役清单。");
            Assert.AreEqual(assetsBefore, CountAssets(folder), "失败后不应留下任何新资产。");
        }

        [Test]
        public void CreatedUnitTypeWritesSubConfigReferencesToDiskNotFileIdZero()
        {
            // 直接读磁盘上的 YAML 原文，而不是 LoadAssetAtPath —— 后者可能返回内存里的同一个对象，
            // 那样即使磁盘上写的是 fileID: 0 也照样"通过"，测不出真正的问题。
            //
            // 这条针对的失效模式：CreateAsset 按调用当时的引用状态序列化。若主资产先于被引用的
            // 子配置落盘，子配置引用会被写成 fileID: 0；此后没人再标脏主资产，SaveAssets 也不会补写。
            // 结果磁盘上是"引用全空"的兵种 + 几个孤儿子配置，而内存与 Inspector 看起来都正常，
            // 下次域重载后才暴露：ConfigValidator 报 SpawnConfig is null，兵种既不生成也不渲染。
            UnitTypeConfig created = CreateIntoTempFolder("OnDisk", 3, out _, out string folder);
            Assert.IsNotNull(created);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string path = AssetDatabase.GetAssetPath(created);
            string yaml = System.IO.File.ReadAllText(path);

            AssertReferenceWritten(yaml, "spawnConfig");
            AssertReferenceWritten(yaml, "combatConfig");
            AssertReferenceWritten(yaml, "renderConfig");

            // 子配置本身也要真的落在同一个目录里，而不是只写了个引用。
            string spawnPath = AssetDatabase.GetAssetPath(created.spawnConfig);
            Assert.IsTrue(spawnPath.StartsWith(folder), "spawnConfig 应当落在同一个输出目录里：" + spawnPath);

            // 光看主资产不够：RenderConfig 自己那份 profile 引用同样必须落盘。
            // 未落盘的 profile 会写成 fileID: 0，并连带把三个网格槽位一起清空 ——
            // 内存与 Inspector 都正常，域重载后才变成"兵种没有动画"。
            string renderPath = AssetDatabase.GetAssetPath(created.renderConfig);
            string renderYaml = System.IO.File.ReadAllText(renderPath);
            AssertReferenceWritten(renderYaml, "vatProfile");
            AssertReferenceWritten(renderYaml, "nearMesh");
            AssertReferenceWritten(renderYaml, "midMesh");
            AssertReferenceWritten(renderYaml, "farMesh");
        }

        /// <summary>断言 YAML 里某个字段写的是真实引用（fileID 非 0），而不是空引用。</summary>
        private static void AssertReferenceWritten(string yaml, string fieldName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                yaml, "^\\s*" + fieldName + ":\\s*(\\{.*?\\})\\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "磁盘上的兵种资产里找不到字段 " + fieldName + "。");

            string value = match.Groups[1].Value;
            Assert.IsFalse(value.Contains("fileID: 0"),
                fieldName + " 在磁盘上被写成了空引用（" + value + "）——" +
                "子配置引用没有落盘，兵种重新载入后会既不生成也不渲染。");
        }

        [Test]
        public void CreatedUnitTypeReloadsFromDiskWithAllSubConfigReferencesIntact()
        {
            UnitTypeConfig created = CreateIntoTempFolder("Reloaded", 3, out _, out string folder);
            Assert.IsNotNull(created);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string path = AssetDatabase.GetAssetPath(created);
            var reloaded = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(path);
            Assert.IsNotNull(reloaded, "兵种资产应当能从磁盘读回。");

            Assert.IsNotNull(reloaded.spawnConfig, "重新载入后 spawnConfig 为空。");
            Assert.IsNotNull(reloaded.combatConfig, "重新载入后 combatConfig 为空。");
            Assert.IsNotNull(reloaded.renderConfig, "重新载入后 renderConfig 为空。");

            Assert.IsTrue(AssetDatabase.Contains(reloaded.spawnConfig), "spawnConfig 必须指向已落盘资产。");
            Assert.IsTrue(AssetDatabase.Contains(reloaded.combatConfig), "combatConfig 必须指向已落盘资产。");
            Assert.IsTrue(AssetDatabase.Contains(reloaded.renderConfig), "renderConfig 必须指向已落盘资产。");
            Assert.IsTrue(AssetDatabase.GetAssetPath(reloaded.spawnConfig).StartsWith(folder),
                "spawnConfig 应当落在同一个输出目录里。");

            ValidationResult result = UnitTypeBinder.ValidateBinding(reloaded);
            Assert.IsTrue(result.IsValid, "重新载入的兵种应当通过校验：" + string.Join(" / ", result.Errors));
        }

        [Test]
        public void CreateUnitTypeRejectsNameThatSanitizesToNothing()
        {
            int rosterBefore = scenario.unitTypes.Length;
            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = scenario,
                template = template,
                unitTypeName = "///",
                teamId = 0,
                directory = NewTempFolder()
            }, out string error);

            Assert.IsNull(result);
            Assert.IsNotNull(error);
            Assert.AreEqual(rosterBefore, scenario.unitTypes.Length, "失败时不应改动战役清单。");
        }

        [Test]
        public void CreateUnitTypeRejectsMissingScenarioBecauseUnitWouldNeverLoad()
        {
            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = null,
                template = template,
                unitTypeName = "Orphan",
                teamId = 0,
                directory = NewTempFolder()
            }, out string error);

            Assert.IsNull(result, "没有战役清单就不能建兵种。");
            StringAssert.Contains("战役", error);
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private struct ProfileFixture
        {
            internal VatProfileData Data;
            internal VATProfile Asset;
        }

        /// <summary>
        /// 造一份 profile 数据。顶点数等布局字段填成合法值，让 VatProfileReader 能读出来。
        /// 网格按名字区分，便于断言落位。
        /// </summary>
        private ProfileFixture Profile(string clean, string mid, string low)
        {
            var profile = ScriptableObject.CreateInstance<VATProfile>();
            created.Add(profile);

            Mesh cleanMesh = NewMesh(clean);
            Mesh midMesh = mid != null ? NewMesh(mid) : null;
            Mesh lowMesh = low != null ? NewMesh(low) : null;

            profile.cleanMesh = cleanMesh;
            profile.positionTexture = NewTexture("Pos");
            profile.normalTexture = NewTexture("Norm");
            profile.textureWidth = 4;
            profile.textureHeight = 4;
            profile.rowsPerFrame = 1;
            profile.totalFrameCount = 4;
            profile.frameRate = 30;
            profile.idle = NewWindow("Idle", 0, 1, true);
            profile.move = NewWindow("Move", 1, 1, true);
            profile.attack = NewWindow("Attack", 2, 1, true);
            profile.death = NewWindow("Death", 3, 1, false);

            if (midMesh != null)
            {
                profile.midLodMesh = midMesh;
                profile.midLodPositionTexture = NewTexture("MidPos");
                profile.midLodNormalTexture = NewTexture("MidNorm");
                profile.midLodTextureWidth = 4;
                profile.midLodTextureHeight = 4;
                profile.midLodRowsPerFrame = 1;
            }
            if (lowMesh != null)
            {
                profile.lowLodMesh = lowMesh;
                profile.lowLodPositionTexture = NewTexture("LowPos");
                profile.lowLodNormalTexture = NewTexture("LowNorm");
                profile.lowLodTextureWidth = 4;
                profile.lowLodTextureHeight = 4;
                profile.lowLodRowsPerFrame = 1;
            }

            Assert.IsTrue(VatProfileReader.TryRead(profile, out VatProfileData data, out string error), error);
            Persist(profile, cleanMesh, midMesh, lowMesh);
            return new ProfileFixture { Data = data, Asset = profile };
        }

        /// <summary>
        /// 把 profile 连同它的网格/纹理落盘成"主资产 + 子资产"，与 <c>VatBakeResult.SaveNew</c> 的产物同构。
        ///
        /// 必须落盘：创建流程会把 profile 引用写进新的 RenderConfig，
        /// 而 <c>AssetDatabase.CreateAsset</c> 按调用当时的引用状态序列化 ——
        /// 未落盘的 profile 在磁盘上会被写成 <c>vatProfile: {fileID: 0}</c>，
        /// 连三个网格槽位一起变空。所以"未落盘 profile"不是合法输入，夹具也不能用它。
        /// </summary>
        private void Persist(VATProfile profile, Mesh clean, Mesh mid, Mesh low)
        {
            string folder = NewTempFolder();
            AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
            AddSubAsset(profile, clean);
            AddSubAsset(profile, profile.positionTexture);
            AddSubAsset(profile, profile.normalTexture);
            AddSubAsset(profile, mid);
            AddSubAsset(profile, profile.midLodPositionTexture);
            AddSubAsset(profile, profile.midLodNormalTexture);
            AddSubAsset(profile, low);
            AddSubAsset(profile, profile.lowLodPositionTexture);
            AddSubAsset(profile, profile.lowLodNormalTexture);
            AssetDatabase.SaveAssets();
        }

        private static void AddSubAsset(VATProfile owner, Object part)
        {
            if (part != null && !EditorUtility.IsPersistent(part))
                AssetDatabase.AddObjectToAsset(part, owner);
        }

        private VATProfile NewProfileAsset(string name)
        {
            var profile = ScriptableObject.CreateInstance<VATProfile>();
            profile.name = name;
            created.Add(profile);
            return profile;
        }

        /// <summary>一个已正确绑定 profile 的兵种；三个网格槽位与 profile 一致。</summary>
        private UnitTypeConfig BoundUnitType(out RenderConfig render, out AnimationConfig animation,
            string clean = "Clean", string mid = null, string low = null)
        {
            ProfileFixture fixture = Profile(clean, mid, low);

            render = NewRenderConfig();
            UnitTypeBinder.ApplyProfile(render, fixture.Asset, fixture.Data);

            animation = ScriptableObject.CreateInstance<AnimationConfig>();
            created.Add(animation);

            var spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            spawn.unitCount = 1000;
            created.Add(spawn);

            var config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            config.unitTypeName = "Test Unit";
            config.teamId = 0;
            config.spawnConfig = spawn;
            config.renderConfig = render;
            config.animationConfig = animation;
            created.Add(config);
            return config;
        }

        /// <summary>
        /// 一份"结构完整"的渲染配置：三个材质槽位都填上现役材质。
        ///
        /// 材质必须给：运行时 <c>MassGpuRenderDispatcher.DrawLod</c> 遇到空材质会整档跳过绘制，
        /// 所以空材质是"某个距离上的单位凭空消失"的真缺陷，不是可选字段。
        /// 用现役材质而不是临时 new Material，是为了让夹具与仓库里 3 份 RenderConfig 的真实形状一致。
        /// </summary>
        private RenderConfig NewRenderConfig()
        {
            var render = ScriptableObject.CreateInstance<RenderConfig>();
            created.Add(render);
            ApplyShippedMaterials(render);
            return render;
        }

        /// <summary>按现役约定填满三个材质槽位（近景用受光材质，中/远景用无阴影材质）。</summary>
        private static void ApplyShippedMaterials(RenderConfig render)
        {
            render.nearMaterial = ShippedMaterial("LitInstancedAgent.mat");
            render.midMaterial = ShippedMaterial("FarInstancedAgent.mat");
            render.farMaterial = ShippedMaterial("FarInstancedAgent.mat");
        }

        private static Material ShippedMaterial(string fileName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/MassEngine/VatRender/Materials/" + fileName);
            Assert.IsNotNull(material, "测试前提：现役材质必须存在 —— " + fileName);
            return material;
        }

        // ------------------------------------------------------------------
        // 建整套兵种用的夹具
        // ------------------------------------------------------------------

        private ScenarioConfig NewScenario()
        {
            var asset = ScriptableObject.CreateInstance<ScenarioConfig>();
            asset.unitTypes = new UnitTypeConfig[0];
            created.Add(asset);
            return asset;
        }

        /// <summary>数值模板：六个子配置齐全，且都已落盘（创建流程要从它复制子配置）。</summary>
        private UnitTypeConfig NewTemplate()
        {
            string folder = NewTempFolder();
            var spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            spawn.unitCount = 1000;
            AssetDatabase.CreateAsset(spawn, folder + "/TemplateSpawn.asset");
            created.Add(spawn);

            var combat = ScriptableObject.CreateInstance<CombatConfig>();
            AssetDatabase.CreateAsset(combat, folder + "/TemplateCombat.asset");
            created.Add(combat);

            var movement = ScriptableObject.CreateInstance<MovementConfig>();
            AssetDatabase.CreateAsset(movement, folder + "/TemplateMovement.asset");
            created.Add(movement);

            var flocking = ScriptableObject.CreateInstance<FlockingConfig>();
            AssetDatabase.CreateAsset(flocking, folder + "/TemplateFlocking.asset");
            created.Add(flocking);

            var animation = ScriptableObject.CreateInstance<AnimationConfig>();
            AssetDatabase.CreateAsset(animation, folder + "/TemplateAnimation.asset");
            created.Add(animation);

            var render = ScriptableObject.CreateInstance<RenderConfig>();
            ApplyShippedMaterials(render);
            AssetDatabase.CreateAsset(render, folder + "/TemplateRender.asset");
            created.Add(render);

            var unit = ScriptableObject.CreateInstance<UnitTypeConfig>();
            unit.unitTypeName = "Template";
            unit.teamId = 0;
            unit.spawnConfig = spawn;
            unit.combatConfig = combat;
            unit.movementConfig = movement;
            unit.flockingConfig = flocking;
            unit.animationConfig = animation;
            unit.renderConfig = render;
            AssetDatabase.CreateAsset(unit, folder + "/TemplateUnit.asset");
            created.Add(unit);
            return unit;
        }

        private UnitTypeConfig CreateIntoTempFolder(string name, int teamId, out ScenarioConfig outScenario,
            out string folder)
        {
            folder = NewTempFolder();
            outScenario = scenario;
            UnitTypeConfig result = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            {
                scenario = scenario,
                template = template,
                unitTypeName = name,
                teamId = teamId,
                directory = folder,
                profile = Profile("Clean", null, null).Asset
            }, out string error);

            Assert.IsNotNull(result, "创建应成功：" + error);
            return result;
        }

        /// <summary>建一个测试用临时目录（Assets 下，测完删掉）。</summary>
        private string NewTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.CreateFolder("Assets", "UnitTypeBinderTestTemp");

            string folder = AssetDatabase.GenerateUniqueAssetPath(TempRoot + "/Case");
            AssetDatabase.CreateFolder(TempRoot, System.IO.Path.GetFileName(folder));
            string full = TempRoot + "/" + System.IO.Path.GetFileName(folder);
            tempFolders.Add(full);
            return full;
        }

        private static int CountAssets(string folder)
        {
            return AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length;
        }

        private Mesh NewMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            created.Add(mesh);
            return mesh;
        }

        private Texture2D NewTexture(string name)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBAHalf, false, true) { name = name };
            created.Add(texture);
            return texture;
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
    }
}
