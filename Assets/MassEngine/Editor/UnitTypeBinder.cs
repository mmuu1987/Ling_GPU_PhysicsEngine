using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Editor
{
    /// <summary>
    /// M5.2 兵种绑定核心：把 VAT profile 接到兵种渲染配置，并校验整条绑定链。
    ///
    /// 设计要点：LOD 落位规则只在这里表达一次（<see cref="ResolveLodMeshes"/>），
    /// 与 <c>ResolvedUnitTypeRuntime.Resolve</c> 的 near/mid/far 配对保持一致；
    /// 测试用运行时反查本核心，任何一边漂移都会红 —— 避免"向导算一套、运行时用另一套"。
    ///
    /// 窗口与批处理共用本核心，不各写一份接线逻辑。
    /// </summary>
    public static class UnitTypeBinder
    {
        /// <summary>
        /// 按 profile 的 LOD 存在情况决定三个渲染槽位各指向哪个网格。
        /// 与 <c>ResolvedUnitTypeRuntime.Resolve</c> 一致：
        /// near 恒为 cleanMesh；mid 优先 midLod、缺失退 lowLod、再退 cleanMesh；
        /// far 优先 lowLod、缺失退 midLod、再退 cleanMesh。
        /// </summary>
        public static void ResolveLodMeshes(VatProfileData profile, out Mesh near, out Mesh mid, out Mesh far)
        {
            near = profile.cleanMesh;
            mid = profile.hasMidLod
                ? profile.midLodMesh
                : (profile.hasLowLod ? profile.lowLodMesh : profile.cleanMesh);
            far = profile.hasLowLod
                ? profile.lowLodMesh
                : (profile.hasMidLod ? profile.midLodMesh : profile.cleanMesh);
        }

        /// <summary>
        /// 读取 profile。走 <c>VatProfileReader</c> 的 duck-typing，
        /// 所以 <c>RenderConfig.vatProfile</c> 是弱类型 <c>ScriptableObject</c> 也能读。
        /// </summary>
        public static bool TryReadProfile(ScriptableObject profileAsset, out VatProfileData data, out string error)
        {
            return VatProfileReader.TryRead(profileAsset, out data, out error);
        }

        /// <summary>
        /// 把 profile 与它的三级 LOD 网格写进渲染配置。
        /// 三个槽位总是整体覆盖：profile 没有 lowLod 时 far 会落到 cleanMesh，
        /// 不能留着上一次绑定的陈旧引用。
        /// </summary>
        public static void ApplyProfile(RenderConfig render, ScriptableObject profileAsset, VatProfileData data)
        {
            if (render == null)
                throw new ArgumentNullException(nameof(render));
            if (profileAsset == null)
                throw new ArgumentNullException(nameof(profileAsset));

            ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);
            render.vatProfile = profileAsset;
            render.nearMesh = near;
            render.midMesh = mid;
            render.farMesh = far;
        }

        /// <summary>
        /// 绑定并把绑定后的结果校验一遍。写操作只碰 <c>config.renderConfig</c> 的三个网格槽位与
        /// profile 引用，不创建也不删除任何资产。
        /// </summary>
        public static ValidationResult Bind(UnitTypeConfig config, ScriptableObject profileAsset)
        {
            if (config == null)
                throw new ArgumentException("请指定兵种配置。", nameof(config));
            if (config.renderConfig == null)
                throw new ArgumentException("该兵种没有 RenderConfig，无法绑定 VAT profile。", nameof(config));
            if (!TryReadProfile(profileAsset, out VatProfileData data, out string error))
                throw new ArgumentException("VAT profile 不可读：" + error, nameof(profileAsset));

            ApplyProfile(config.renderConfig, profileAsset, data);
            return ValidateBinding(config);
        }

        /// <summary>
        /// 校验一个兵种的完整绑定链：既有兵种契约（teamId / 兵力 / 类名）、
        /// profile 可读性与契约、三个网格槽位与 profile 的配对、动画速率区间。
        /// 纯读，不改任何资产。
        /// </summary>
        public static ValidationResult ValidateBinding(UnitTypeConfig config)
        {
            var result = new ValidationResult();
            if (config == null)
            {
                result.AddError("兵种配置为空。");
                return result;
            }

            // 复用既有兵种契约校验，避免两套口径。
            ValidationResult baseline = ConfigValidator.Validate(config);
            foreach (string warning in baseline.Warnings)
                result.AddWarning(warning);
            foreach (string error in baseline.Errors)
                result.AddError(error);

            RenderConfig render = config.renderConfig;
            if (render == null)
            {
                result.AddWarning("没有 RenderConfig：该兵种会模拟但不渲染。");
                return result;
            }
            if (render.vatProfile == null)
            {
                result.AddError("RenderConfig 没有绑定 VAT profile，该兵种没有动画。");
                return result;
            }

            if (!TryReadProfile(render.vatProfile, out VatProfileData data, out string readError))
            {
                result.AddError("VAT profile 不可读：" + readError);
                return result;
            }

            // 现役 VATProfile 才有完整契约校验；其他形状的 profile 只做上面的可读性检查。
            if (render.vatProfile is VATProfile concrete &&
                !VatProfileValidation.TryValidate(concrete, out string contractError))
                result.AddError("VAT profile 契约不合法：" + contractError);

            ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);
            CheckSlot(result, "nearMesh", render.nearMesh, near);
            CheckSlot(result, "midMesh", render.midMesh, mid);
            CheckSlot(result, "farMesh", render.farMesh, far);

            CheckAnimationRates(result, config.animationConfig);
            return result;
        }

        /// <summary>
        /// 网格槽位必须与 profile 烘制时用的网格一致 —— 顶点顺序不同就会采样到错位的顶点，
        /// 画面上是乱掉的模型而不是报错，所以这里按错误处理，让向导必须修掉。
        /// 槽位留空是合法的：运行时会用 profile 的网格兜底。
        /// </summary>
        private static void CheckSlot(ValidationResult result, string slotName, Mesh authored, Mesh expected)
        {
            if (authored == null || expected == null || authored == expected)
                return;

            result.AddError("RenderConfig." + slotName + "（" + authored.name + "）与 VAT profile 烘制时用的网格（" +
                expected.name + "）不是同一个；顶点顺序不同会采样错位，画面会花。请清空该槽位或改指 profile 的网格。");
        }

        /// <summary>
        /// 动画速率区间：语义上 min 不能大于 max；Range 特性只在 Inspector 里拦，
        /// 代码路径（向导、批处理、方案导入）都得自己查一遍。
        /// </summary>
        private static void CheckAnimationRates(ValidationResult result, AnimationConfig animation)
        {
            if (animation == null)
                return;

            if (animation.moveAnimationSpeedMin > animation.moveAnimationSpeedMax)
                result.AddError("动画速率区间倒置：moveAnimationSpeedMin（" +
                    animation.moveAnimationSpeedMin + "）大于 moveAnimationSpeedMax（" +
                    animation.moveAnimationSpeedMax + "）。");

            if (animation.moveAnimationSpeedMin < 0.1f || animation.moveAnimationSpeedMin > 1.5f)
                result.AddWarning("moveAnimationSpeedMin 超出 0.1～1.5 的常规区间（当前 " +
                    animation.moveAnimationSpeedMin + "）。");
            if (animation.moveAnimationSpeedMax < 0.5f || animation.moveAnimationSpeedMax > 2f)
                result.AddWarning("moveAnimationSpeedMax 超出 0.5～2 的常规区间（当前 " +
                    animation.moveAnimationSpeedMax + "）。");
        }

        /// <summary>给窗口用的一句话摘要：这个 profile 会把三个槽位接成什么。</summary>
        public static string Describe(RenderConfig render)
        {
            if (render == null || render.vatProfile == null)
                return "未绑定 VAT profile。";
            if (!TryReadProfile(render.vatProfile, out VatProfileData data, out string error))
                return "VAT profile 不可读：" + error;

            ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);
            return string.Format(
                "{0}：{1} 顶点 / {2} 帧 @ {3}fps / 纹理 {4}×{5}（每帧 {6} 行）；LOD near {7} / mid {8} / far {9}",
                render.vatProfile.name, data.cleanMesh != null ? data.cleanMesh.vertexCount : 0,
                data.totalFrameCount, data.frameRate, data.textureWidth, data.textureHeight, data.rowsPerFrame,
                Name(near), Name(mid), Name(far));
        }

        private static string Name(Mesh mesh)
        {
            return mesh != null ? mesh.name : "（空）";
        }

        // ------------------------------------------------------------------
        // 建整套兵种：模板 -> 子配置 -> 登记进战役清单
        // ------------------------------------------------------------------

        /// <summary>
        /// 新建一个兵种并接好整套子配置，最后登记进 <paramref name="scenario"/> 的兵种清单。
        ///
        /// 子配置的独占/共享策略沿用现役约定（见 <see cref="UnitTypeCreationRequest"/>）：
        /// 出生与战斗各自独占，其余按模板共享 —— 与仓库里 6 个内置兵种的实际结构一致，
        /// 不会因为"每个兵种都生成一套独占配置"而静默改变现有兵种之间的耦合。
        ///
        /// 失败时回滚本次新建的全部资产，磁盘上不留半成品（与 <see cref="VatBakeResult"/> 同口径）。
        /// </summary>
        public static UnitTypeConfig CreateUnitType(UnitTypeCreationRequest request, out string error)
        {
            error = null;
            if (!TryValidateRequest(request, out error))
                return null;

            string directory = request.directory.Replace('\\', '/').TrimEnd('/');
            string baseName = SanitizeName(request.unitTypeName);
            if (string.IsNullOrEmpty(baseName))
            {
                error = "兵种名至少要包含一个字母或数字。";
                return null;
            }

            var created = new List<UnityEngine.Object>();
            try
            {
                UnitTypeConfig unit = UnityEngine.Object.Instantiate(request.template);
                created.Add(unit);
                unit.name = baseName;
                unit.unitTypeName = request.unitTypeName.Trim();
                unit.teamId = request.teamId;

                // 出生与战斗按现役约定独占：这两个是"这个兵种有多少人、打多疼"，
                // 共享会让改一个兵种的人数连带改掉另一个。
                SpawnConfig spawn = request.template.spawnConfig != null
                    ? UnityEngine.Object.Instantiate(request.template.spawnConfig)
                    : ScriptableObject.CreateInstance<SpawnConfig>();
                created.Add(spawn);
                spawn.name = baseName + "_Spawn";
                unit.spawnConfig = spawn;

                CombatConfig combat = request.template.combatConfig != null
                    ? UnityEngine.Object.Instantiate(request.template.combatConfig)
                    : ScriptableObject.CreateInstance<CombatConfig>();
                created.Add(combat);
                combat.name = baseName + "_Combat";
                unit.combatConfig = combat;

                RenderConfig render = null;
                if (request.exclusiveRender)
                {
                    // 换模型就必须独占渲染配置：共享会让同一军团的另一个兵种跟着换模型。
                    render = request.template.renderConfig != null
                        ? UnityEngine.Object.Instantiate(request.template.renderConfig)
                        : ScriptableObject.CreateInstance<RenderConfig>();
                    created.Add(render);
                    render.name = baseName + "_Render";
                    unit.renderConfig = render;
                }

                // 先接 profile 再落盘：绑定失败就不该写出任何资产。
                if (request.profile != null)
                {
                    if (render == null)
                    {
                        // 共享的 RenderConfig 属于模板兵种：在这里改 profile 会连带把模板兵种也换掉模型。
                        error = "要绑定 VAT profile 必须勾选「独占 RenderConfig」——" +
                            "否则会改到模板兵种共用的那份渲染配置。";
                        Rollback(created);
                        return null;
                    }
                    if (!TryReadProfile(request.profile, out VatProfileData data, out string readError))
                    {
                        error = "VAT profile 不可读：" + readError;
                        Rollback(created);
                        return null;
                    }
                    ApplyProfile(render, request.profile, data);
                }

                AssetDatabase.CreateAsset(unit, UniquePath(directory, baseName + ".asset"));
                AssetDatabase.CreateAsset(spawn, UniquePath(directory, baseName + "_Spawn.asset"));
                AssetDatabase.CreateAsset(combat, UniquePath(directory, baseName + "_Combat.asset"));
                if (render != null)
                    AssetDatabase.CreateAsset(render, UniquePath(directory, baseName + "_Render.asset"));

                // 登记进战役清单：不登记的话运行时根本看不到这个兵种（不扫文件夹）。
                // 这是最后一步，且登记前先把之前的清单存下来，失败时连清单一起还原，
                // 免得留下指向已删资产的空引用。
                UnitTypeConfig[] previousRoster = request.scenario.unitTypes;
                AppendToScenario(request.scenario, unit);

                try
                {
                    AssetDatabase.SaveAssets();
                }
                catch
                {
                    request.scenario.unitTypes = previousRoster;
                    EditorUtility.SetDirty(request.scenario);
                    throw;
                }
                return unit;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Rollback(created);
                return null;
            }
        }

        private static bool TryValidateRequest(UnitTypeCreationRequest request, out string error)
        {
            error = null;
            if (request == null)
            {
                error = "缺少创建参数。";
                return false;
            }
            if (request.scenario == null)
            {
                error = "请指定战役配置：兵种必须登记进战役清单才会被运行时加载。";
                return false;
            }
            if (request.template == null)
            {
                error = "请选择数值模板（用来继承移动/群体/动画等参数）。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.unitTypeName))
            {
                error = "请填写兵种名。";
                return false;
            }
            if (request.teamId < 0 || request.teamId > ConfigValidator.MaxTeamId)
            {
                error = "军团编号须在 0～" + ConfigValidator.MaxTeamId + " 之间。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(request.directory) ||
                !request.directory.Replace('\\', '/').StartsWith("Assets", StringComparison.Ordinal))
            {
                error = "输出目录必须在 Assets 下。";
                return false;
            }
            string directory = request.directory.Replace('\\', '/').TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(directory))
            {
                error = "输出目录不存在：" + directory;
                return false;
            }
            if (!AssetDatabase.Contains(request.template))
            {
                error = "模板必须是已保存的资产（需要从它复制子配置）。";
                return false;
            }
            return true;
        }

        private static void AppendToScenario(ScenarioConfig scenario, UnitTypeConfig unit)
        {
            UnitTypeConfig[] current = scenario.unitTypes ?? Array.Empty<UnitTypeConfig>();
            var next = new UnitTypeConfig[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[current.Length] = unit;

            Undo.RecordObject(scenario, "Add Bound Unit Type");
            scenario.unitTypes = next;
            EditorUtility.SetDirty(scenario);
        }

        /// <summary>
        /// 回滚：删掉本次已落盘的每一个资产（主资产与各自独立的子配置资产），
        /// 再释放还没落盘的临时对象。只删本次创建的，不碰任何既有资产。
        /// </summary>
        private static void Rollback(List<UnityEngine.Object> created)
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object asset = created[i];
                if (asset == null)
                    continue;
                if (EditorUtility.IsPersistent(asset))
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    if (!string.IsNullOrEmpty(path))
                        AssetDatabase.DeleteAsset(path);
                }
                else
                    UnityEngine.Object.DestroyImmediate(asset);
            }
            created.Clear();
        }

        private static string UniquePath(string directory, string fileName)
        {
            return AssetDatabase.GenerateUniqueAssetPath(directory + "/" + fileName);
        }

        /// <summary>把兵种名压成安全的资产文件名。</summary>
        internal static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var chars = new List<char>(value.Length);
            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    chars.Add(c);
                else if (chars.Count == 0 || chars[chars.Count - 1] != '_')
                    chars.Add('_');
            }
            return new string(chars.ToArray()).Trim('_');
        }
    }

    /// <summary>新建兵种的输入。<see cref="exclusiveRender"/> 见字段说明。</summary>
    public sealed class UnitTypeCreationRequest
    {
        /// <summary>兵种必须登记进这个战役清单才会被运行时加载（运行时按清单取兵种，不扫文件夹）。</summary>
        public ScenarioConfig scenario;

        /// <summary>数值模板：移动/群体/动画/渲染等按它继承。</summary>
        public UnitTypeConfig template;

        public string unitTypeName;
        public int teamId;

        /// <summary>输出目录（Assets 下的已存在文件夹）。</summary>
        public string directory = "Assets/Game/Settings";

        /// <summary>要绑定的 VAT profile；null 表示只建兵种不绑模型。</summary>
        public ScriptableObject profile;

        /// <summary>
        /// 是否给这个兵种一套独占的 RenderConfig。默认 true：
        /// 绑新模型时必须独占，否则会和同一军团共享渲染配置的另一个兵种一起换模型。
        /// 只想加一个用同一套模型的兵种时置 false，沿用模板的共享渲染配置。
        /// </summary>
        public bool exclusiveRender = true;
    }
}
