using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Editor
{
    /// <summary>
    /// M5.3 重烘对拍：用现役烘焙器重烘内置兵种，把产物与仓库里的既有 profile 逐字段对比。
    /// 只读既有资产、只写报告目录，不覆盖任何现有 profile。
    /// 入口：<c>-executeMethod MassEngine.Editor.VatRebakeComparison.Run</c>。
    /// </summary>
    public static class VatRebakeComparison
    {
        private const string DefaultPrefab = "Assets/ThirdParty/RPG Tiny Hero Duo/Prefab/MaleCharacterPBR.prefab";
        private const string DefaultReference = "Assets/Art/VAT/Characters/MaleCharacter_Stage5_MultiClip_Profile.asset";
        private const string ClipFolder = "Assets/ThirdParty/RPG Tiny Hero Duo/Animation/SwordAndShield";
        private const string InPlaceFolder = ClipFolder + "/InPlace";

        private const string ArgPrefab = "--vat-rebake-prefab=";
        private const string ArgReference = "--vat-rebake-reference=";
        private const string ArgOutput = "--vat-rebake-output=";
        private const string ArgExtras = "--vat-rebake-extras=";
        private const string ArgLowRatio = "--vat-rebake-low-ratio=";
        private const string ArgLowMax = "--vat-rebake-low-max=";
        private const string ArgKeep = "--vat-rebake-keep";

        /// <summary>
        /// 既有 Male profile 的 cleanMesh 是 4112 顶点：2041（活动蒙皮）+ 2071（五个活动 MeshRenderer 附件）。
        /// 附件要显式传入，烘焙器不会自动收录非蒙皮部件——这正是原烘焙的输入组合。
        /// </summary>
        private const string DefaultExtras = "Hair01,Head01_Male,Shield08,Eye01,Mouth01";

        public static void Run()
        {
            try
            {
                Execute();
            }
            catch (Exception exception)
            {
                Debug.LogError("[VAT 重烘对拍] 失败：" + exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Execute()
        {
            string prefabPath = Argument(ArgPrefab) ?? DefaultPrefab;
            string referencePath = Argument(ArgReference) ?? DefaultReference;
            string outputDirectory = Argument(ArgOutput) ?? "Logs/M51Baseline/rebake";
            bool keepAsset = HasFlag(ArgKeep);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException("找不到模型 prefab：" + prefabPath);
            var reference = AssetDatabase.LoadAssetAtPath<VATProfile>(referencePath);
            if (reference == null)
                throw new FileNotFoundException("找不到参考 profile：" + referencePath);

            ResolveClips(reference, out AnimationClip idle, out AnimationClip move,
                out AnimationClip attack, out AnimationClip death);
            MeshRenderer[] extras = ResolveExtras(prefab, Argument(ArgExtras) ?? DefaultExtras);

            var request = new VatBakeRequest
            {
                model = prefab,
                idle = idle,
                move = move,
                attack = attack,
                death = death,
                frameRate = reference.frameRate,
                bakeLowLod = true,
                lowLodRatio = Number(ArgLowRatio, .25f),
                lowLodMaxVertices = (int)Number(ArgLowMax, 1200f),
                extraRenderers = extras
            };

            string report = Path.Combine(outputDirectory, "rebake-comparison.md");
            string tempAsset = "Assets/Art/VAT/Characters/_RebakeComparison.asset";
            var log = new StringBuilder();
            var failures = new List<string>();

            Debug.Log("[VAT 重烘对拍] 开始烘焙：" + prefabPath);
            using (VatBakeResult result = VatBaker.Bake(request))
            {
                VATProfile baked = result.Profile;
                Compare(reference, baked, log, failures);

                if (keepAsset)
                {
                    if (File.Exists(tempAsset))
                        AssetDatabase.DeleteAsset(tempAsset);
                    result.SaveNew(tempAsset);
                    log.AppendLine();
                    log.AppendLine("重烘产物已保留：" + tempAsset);
                }
            }

            Directory.CreateDirectory(outputDirectory);
            string header = failures.Count == 0
                ? "# VAT 重烘对拍：全部通过"
                : "# VAT 重烘对拍：" + failures.Count + " 项不一致";
            // 结论行只陈述本次真正判定过的内容：Low LOD 顶点数是派生量、不判定，
            // 所以这里只说"结构一致"，不再写死 994/1004 那对数字（换参数后它们会变）。
            string conclusion = failures.Count == 0
                ? "结论：新烘焙器产出的布局、片段窗口与 LOD 结构与既有资产逐项一致。\n"
                  + "Low LOD 顶点数是派生量，只记录不判定；本次实测值与差异说明见上表。\n"
                : "结论：以下字段与既有资产不一致 ——\n"
                  + string.Join("\n", failures.ConvertAll(f => "- " + f)) + "\n";
            File.WriteAllText(report, header + "\n\n"
                + "模型：" + prefabPath + "\n\n"
                + "参考 profile：" + referencePath + "\n\n"
                + "```text\n" + log + "```\n\n"
                + conclusion);

            Debug.Log("[VAT 重烘对拍] 报告：" + report);
            Debug.Log(failures.Count == 0
                ? "[VAT 重烘对拍] 通过：结构与既有资产一致。"
                : "[VAT 重烘对拍] 有 " + failures.Count + " 项不一致。");

            if (Application.isBatchMode)
                EditorApplication.Exit(failures.Count == 0 ? 0 : 2);
        }

        private static void Compare(VATProfile reference, VATProfile baked, StringBuilder log, List<string> failures)
        {
            // 硬契约：布局与片段窗口。这些字段决定 shader 怎么按 currentState 取帧，
            // 一旦偏移就会取错帧，属于必须逐位一致的部分。
            log.AppendLine("字段                        参考            重烘");
            log.AppendLine("--------------------------  --------------  --------------");
            Row(log, failures, "cleanMesh 顶点数", reference.cleanMesh.vertexCount, baked.cleanMesh.vertexCount);
            Row(log, failures, "textureWidth", reference.textureWidth, baked.textureWidth);
            Row(log, failures, "textureHeight", reference.textureHeight, baked.textureHeight);
            Row(log, failures, "rowsPerFrame", reference.rowsPerFrame, baked.rowsPerFrame);
            Row(log, failures, "totalFrameCount", reference.totalFrameCount, baked.totalFrameCount);
            Row(log, failures, "frameRate", reference.frameRate, baked.frameRate);
            log.AppendLine();
            Row(log, failures, "idle.startFrame", reference.idle.startFrame, baked.idle.startFrame);
            Row(log, failures, "idle.frameCount", reference.idle.frameCount, baked.idle.frameCount);
            Row(log, failures, "move.startFrame", reference.move.startFrame, baked.move.startFrame);
            Row(log, failures, "move.frameCount", reference.move.frameCount, baked.move.frameCount);
            Row(log, failures, "attack.startFrame", reference.attack.startFrame, baked.attack.startFrame);
            Row(log, failures, "attack.frameCount", reference.attack.frameCount, baked.attack.frameCount);
            Row(log, failures, "death.startFrame", reference.death.startFrame, baked.death.startFrame);
            Row(log, failures, "death.frameCount", reference.death.frameCount, baked.death.frameCount);
            Row(log, failures, "death.loop", reference.death.loop, baked.death.loop);
            log.AppendLine();
            // Low LOD 的结构也必须一致（存在性、帧布局、纹理高度），但顶点数只记录不判定，原因见下。
            Row(log, failures, "midLod 存在", reference.HasMidLod, baked.HasMidLod);
            Row(log, failures, "lowLod 存在", reference.HasLowLod, baked.HasLowLod);
            Row(log, failures, "lowLodTextureHeight", reference.lowLodTextureHeight, baked.lowLodTextureHeight);
            Row(log, failures, "lowLodRowsPerFrame", reference.lowLodRowsPerFrame, baked.lowLodRowsPerFrame);

            log.AppendLine();
            log.AppendLine("以下为派生量，只记录不判定：");
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  lowLod 顶点数               {0,-14}  {1,-14} 差值 {2}",
                reference.lowLodMesh.vertexCount, baked.lowLodMesh.vertexCount,
                baked.lowLodMesh.vertexCount - reference.lowLodMesh.vertexCount));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  lowLodTextureWidth          {0,-14}  {1,-14}",
                reference.lowLodTextureWidth, baked.lowLodTextureWidth));
            log.AppendLine();
            // 差异根因（已在 M5.1 查实）：Low LOD 的顶点聚类以 cleanMesh 的几何范围为归一化基准，
            // 而两份 cleanMesh 的"附件部分"取自不同姿态 ——
            //   Stage5 先 BakeClipFrames 再 BuildCleanMesh，于是挂在骨骼下的附件
            //   （Hair01/Head01_Male/Shield08/Eye01/Mouth01，共 2071 顶点）被冻结在死亡动画末帧姿态；
            //   本烘焙器在采样前捕获绑定姿态，附件与蒙皮网格同基准。
            // 蒙皮部分（Body05 1847 + Cloak02 194 = 2041 顶点）两边逐顶点完全一致，可证算法本身相同。
            // 绑定姿态是更正确的选择：它只取决于模型，不随动画内容漂移。
            //
            // 这段说明只在"顶点数确实不同"时才有意义 —— 换了减面参数（如 --vat-rebake-low-ratio）
            // 之后数量差异来自参数而非姿态，硬写结论会让正文与上面的表自相矛盾。
            if (baked.lowLodMesh.vertexCount != reference.lowLodMesh.vertexCount)
            {
                log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "lowLod 顶点数差异：参考 {0} / 重烘 {1}（差值 {2}）。",
                    reference.lowLodMesh.vertexCount, baked.lowLodMesh.vertexCount,
                    baked.lowLodMesh.vertexCount - reference.lowLodMesh.vertexCount));
                log.AppendLine("  若本次用了默认减面参数（比例 0.25 / 上限 1200），根因是两份 cleanMesh 的附件部分取自不同姿态：");
                log.AppendLine("    参考（Stage5）：附件在死亡动画末帧姿态下被并入 cleanMesh，几何范围偏大 → 分辨率 25 → 994 簇。");
                log.AppendLine("    重烘（本工具）：附件在绑定姿态下并入，与蒙皮网格同基准 → 分辨率 25 → 1004 簇。");
                log.AppendLine("    蒙皮部分 2041 顶点逐顶点一致，说明聚类算法相同，差异仅来自基准姿态。");
                log.AppendLine("    绑定姿态不随动画内容漂移，故本工具采用绑定姿态，该差异按已知差异记录。");
                log.AppendLine("  若本次改过减面参数，数量差异主要来自参数本身，不能套用上面的姿态解释。");
            }

            if (!VatProfileValidation.TryValidate(baked, out string error))
                failures.Add("重烘产物未通过 profile 校验：" + error);

            CheckTextureContainsMotion(baked, log, failures);
        }

        /// <summary>
        /// 解码纹素，确认烘出来的确实是"逐帧动画"而不是"每一帧都等于绑定姿态"。
        ///
        /// 为什么必须有这一步：上面所有判定读的都是尺寸类字段（顶点数、布局整数、窗口整数），
        /// 它们全部来自 CalculateLayout / BuildWindows，与采样结果无关；cleanMesh 更是在任何
        /// SampleAnimation 之前就捕获好的。也就是说采样整体失效时，产物依然尺寸正确、校验全过、
        /// 与既有资产逐字段一致 —— 整条验收链对"纹理会动吗"是完全盲的。
        /// 这里按 shader 契约取样若干顶点的整条时间轴，帧间无位移即判失败。
        /// </summary>
        private static void CheckTextureContainsMotion(VATProfile baked, StringBuilder log, List<string> failures)
        {
            log.AppendLine();
            log.AppendLine("纹素抽检（确认帧间确有形变，尺寸类字段查不出这一项）：");

            if (!(baked.positionTexture is Texture2D readable))
            {
                failures.Add("重烘位置纹理不是可读 Texture2D，无法抽检纹素。");
                return;
            }

            int width = readable.width;
            int rowsPerFrame = Mathf.Max(1, baked.rowsPerFrame);
            Color[] pixels = readable.GetPixels();

            Vector3 Sample(int vertex, int frame)
            {
                int x = vertex % width;
                int y = frame * rowsPerFrame + vertex / width;
                if (y < 0 || y >= readable.height)
                    return Vector3.zero;
                Color c = pixels[y * width + x];
                return new Vector3(c.r, c.g, c.b);
            }

            // 在整个 idle 窗口上取若干个顶点，量它们在时间轴上的最大位移。
            int vertexCount = baked.cleanMesh.vertexCount;
            int probes = Mathf.Min(64, vertexCount);
            int idleStart = baked.idle.startFrame;
            int idleFrames = Mathf.Max(1, baked.idle.frameCount);

            int moving = 0;
            float widest = 0f;
            for (int i = 0; i < probes; i++)
            {
                int vertex = probes > 0 ? i * vertexCount / probes : 0;
                Vector3 first = Sample(vertex, idleStart);
                float span = 0f;
                for (int frame = 1; frame < idleFrames; frame++)
                    span = Mathf.Max(span, (Sample(vertex, idleStart + frame) - first).magnitude);
                widest = Mathf.Max(widest, span);
                if (span > 1e-4f)
                    moving++;
            }

            log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  抽检 {0} 个顶点：{1} 个在 idle 窗口内有位移，最大位移 {2:F5}", probes, moving, widest));

            if (moving == 0)
                failures.Add("重烘位置纹理在 idle 窗口内没有任何顶点移动 —— 逐帧采样没有生效，" +
                    "产出的是一份每帧都等于绑定姿态的纹理（尺寸类字段查不出这种失效）。");
            else if (moving < probes / 2)
                failures.Add("重烘位置纹理只有 " + moving + "/" + probes +
                    " 个抽检顶点在动，逐帧采样可能只对少数骨骼生效。");
        }

        private static void Row<T>(StringBuilder log, List<string> failures, string label, T reference, T baked)
        {
            bool same = EqualityComparer<T>.Default.Equals(reference, baked);
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-26}  {1,-14}  {2,-14} {3}",
                label, reference, baked, same ? "" : "  <== 不一致"));
            if (!same)
                failures.Add(label + "：参考 " + reference + "，重烘 " + baked);
        }

        /// <summary>按名字在模型层级里找出附件 MeshRenderer，顺序固定以便对拍可复现。</summary>
        private static MeshRenderer[] ResolveExtras(GameObject prefab, string names)
        {
            if (string.IsNullOrWhiteSpace(names))
                return Array.Empty<MeshRenderer>();

            var byName = new Dictionary<string, MeshRenderer>();
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (!byName.ContainsKey(renderer.name))
                    byName.Add(renderer.name, renderer);

            var resolved = new List<MeshRenderer>();
            foreach (string raw in names.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0)
                    continue;
                if (!byName.TryGetValue(name, out MeshRenderer renderer))
                    throw new FileNotFoundException("模型里找不到附件 MeshRenderer：" + name);
                resolved.Add(renderer);
            }

            Debug.Log("[VAT 重烘对拍] 附件：" + string.Join(", ", resolved.ConvertAll(r => r.name)));
            return resolved.ToArray();
        }

        /// <summary>四段 clip 由参考 profile 的帧数反推时长后，在素材目录里挑最接近的一条。</summary>
        private static void ResolveClips(VATProfile reference, out AnimationClip idle, out AnimationClip move,
            out AnimationClip attack, out AnimationClip death)
        {
            idle = FindClip("Idle_Normal_SwordAndShield", reference.idle);
            move = FindClip("MoveFWD_Battle_InPlace_SwordAndShield", reference.move);
            attack = FindClip("Attack01_SwordAndShiled", reference.attack);
            death = FindClip("Die01_SwordAndShield", reference.death);

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[VAT 重烘对拍] 选用 clip：idle={0}({1:F3}s) move={2}({3:F3}s) attack={4}({5:F3}s) death={6}({7:F3}s)",
                idle.name, idle.length, move.name, move.length, attack.name, attack.length, death.name, death.length));
        }

        private static AnimationClip FindClip(string name, VATProfile.VATClipWindow window)
        {
            string[] candidates =
            {
                ClipFolder + "/" + name + ".fbx",
                InPlaceFolder + "/" + name + ".fbx"
            };
            foreach (string path in candidates)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null)
                    return clip;
            }

            // 路径没命中就按名字在素材目录里扫一遍，避免因大小写或后缀差异中断。
            string wanted = name.ToLowerInvariant();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).ToLowerInvariant() == wanted)
                    return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }

            throw new FileNotFoundException("找不到动画 clip：" + name
                + "（参考片段需要约 " + window.frameCount / (float)window.frameRate + " 秒）");
        }

        private static string Argument(string prefix)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(prefix, StringComparison.Ordinal))
                    return argument.Substring(prefix.Length).Trim('"');
            return null;
        }

        private static float Number(string prefix, float fallback)
        {
            string raw = Argument(prefix);
            return raw != null && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        private static bool HasFlag(string flag)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, flag, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
