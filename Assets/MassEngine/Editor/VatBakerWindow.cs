using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Editor
{
    /// <summary>
    /// VAT 烘焙窗口：模型 + 四段 AnimationClip -> VATProfile 资产。
    /// 这里只是 <see cref="VatBaker"/> 的表单外壳，采样与布局全部走核心，避免窗口与批处理各写一份。
    /// </summary>
    public sealed class VatBakerWindow : EditorWindow
    {
        private const string DefaultFolder = "Assets/VAT_Data";

        [SerializeField] private GameObject model;
        [SerializeField] private AnimationClip idle;
        [SerializeField] private AnimationClip move;
        [SerializeField] private AnimationClip attack;
        [SerializeField] private AnimationClip death;
        [SerializeField] private int frameRate = 30;
        [SerializeField] private bool bakeLowLod = true;
        [SerializeField] private float lowLodRatio = .25f;
        [SerializeField] private int lowLodMaxVertices = 1200;
        [SerializeField] private string folder = DefaultFolder;
        [SerializeField] private string assetName = string.Empty;
        [SerializeField] private List<MeshRenderer> extraRenderers = new List<MeshRenderer>();
        [SerializeField] private bool showExtras;

        private string feedback;
        private bool feedbackIsError;
        private Vector2 scroll;

        [MenuItem("MassEngine/VAT Baker")]
        public static void Open() => GetWindow<VatBakerWindow>("VAT 烘焙");

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("VAT 烘焙（模型 + 四段动画 -> VATProfile）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "逐帧采样蒙皮后的顶点位置/法线写入 RGBAHalf 纹理，供 VatRender 间接实例绘制采样。\n" +
                "四段动画全部必填；输出只能新建资产，不覆盖已有 profile。", MessageType.Info);

            EditorGUILayout.Space();
            model = (GameObject)EditorGUILayout.ObjectField("模型（Prefab / 场景对象）", model, typeof(GameObject), true);
            frameRate = Mathf.Clamp(EditorGUILayout.IntField("采样帧率", frameRate), 1, 240);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("四段动画", EditorStyles.boldLabel);
            idle = (AnimationClip)EditorGUILayout.ObjectField("Idle（循环）", idle, typeof(AnimationClip), false);
            move = (AnimationClip)EditorGUILayout.ObjectField("Move（循环）", move, typeof(AnimationClip), false);
            attack = (AnimationClip)EditorGUILayout.ObjectField("Attack（循环）", attack, typeof(AnimationClip), false);
            death = (AnimationClip)EditorGUILayout.ObjectField("Death（单次）", death, typeof(AnimationClip), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Low LOD", EditorStyles.boldLabel);
            bakeLowLod = EditorGUILayout.Toggle("烘焙 Low LOD", bakeLowLod);
            using (new EditorGUI.DisabledScope(!bakeLowLod))
            {
                lowLodRatio = EditorGUILayout.Slider("顶点保留比例", lowLodRatio, .02f, 1f);
                lowLodMaxVertices = Mathf.Max(0, EditorGUILayout.IntField("顶点上限（0 = 不限）", lowLodMaxVertices));
            }
            EditorGUILayout.HelpBox("Mid LOD 有意留空：运行时对 mid/low 缺失有回退，单级 LOD 可正常绘制。", MessageType.None);

            showExtras = EditorGUILayout.Foldout(showExtras, "附加 MeshRenderer（武器等非蒙皮部件）");
            if (showExtras)
            {
                EditorGUI.indentLevel++;
                int size = Mathf.Max(0, EditorGUILayout.IntField("数量", extraRenderers.Count));
                while (size > extraRenderers.Count)
                    extraRenderers.Add(null);
                while (size < extraRenderers.Count)
                    extraRenderers.RemoveAt(extraRenderers.Count - 1);
                for (int i = 0; i < extraRenderers.Count; i++)
                    extraRenderers[i] = (MeshRenderer)EditorGUILayout.ObjectField(
                        "元素 " + i, extraRenderers[i], typeof(MeshRenderer), true);
                EditorGUI.indentLevel--;
                EditorGUILayout.HelpBox("必须是模型自身层级内的 MeshRenderer，且带 MeshFilter 的共享网格。", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.TextField("目录", folder);
                if (GUILayout.Button("选择", GUILayout.Width(60)))
                {
                    string picked = EditorUtility.OpenFolderPanel("选择输出目录", folder, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                        folder = ToAssetPath(picked);
                }
            }
            assetName = EditorGUILayout.TextField("资产名", assetName);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("烘焙到资产", GUILayout.Height(36)))
                    Bake();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("播放模式期间不能烘焙。", MessageType.Warning);

            if (!string.IsNullOrEmpty(feedback))
                EditorGUILayout.HelpBox(feedback, feedbackIsError ? MessageType.Error : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void Bake()
        {
            feedback = null;
            string path = BuildAssetPath(out string pathError);
            if (pathError != null)
            {
                feedback = pathError;
                feedbackIsError = true;
                return;
            }

            var request = new VatBakeRequest
            {
                model = model,
                idle = idle,
                move = move,
                attack = attack,
                death = death,
                frameRate = frameRate,
                bakeLowLod = bakeLowLod,
                lowLodRatio = lowLodRatio,
                lowLodMaxVertices = lowLodMaxVertices,
                extraRenderers = extraRenderers.ToArray(),
                progress = (stage, value) => EditorUtility.DisplayProgressBar("VAT 烘焙", stage, value)
            };

            try
            {
                VatBakeResult result = VatBaker.Bake(request);
                try
                {
                    VATProfile profile = result.SaveNew(path);
                    AssetDatabase.Refresh();
                    feedback = Describe(profile, path);
                    feedbackIsError = false;
                    EditorGUIUtility.PingObject(profile);
                }
                finally
                {
                    // 保存成功后子资产已持久化，Dispose 只会清掉未被接管的临时对象。
                    result.Dispose();
                }
            }
            catch (Exception exception)
            {
                feedback = exception.Message;
                feedbackIsError = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private string BuildAssetPath(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(assetName))
            {
                error = "请填写资产名。";
                return null;
            }
            string trimmed = folder.TrimEnd('/');
            // 必须是真正的 Assets 目录，不能是 "AssetsFoo" 这种同前缀目录
            // （否则会被 SaveNew 的 "Assets/" 校验挡回来，用户只看到一句泛泛的报错）。
            if (trimmed != "Assets" && !trimmed.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "输出目录必须在 Assets 下。";
                return null;
            }
            return trimmed + "/" + assetName.Trim() + ".asset";
        }

        private static string ToAssetPath(string absolute)
        {
            string normalized = absolute.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            return normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalized.Substring(dataPath.Length)
                : absolute;
        }

        private static string Describe(VATProfile profile, string path)
        {
            string lod = profile.HasLowLod
                ? string.Format("Low LOD {0} 顶点 / {1}x{2} / 每帧 {3} 行",
                    profile.lowLodMesh.vertexCount, profile.lowLodTextureWidth, profile.lowLodTextureHeight, profile.lowLodRowsPerFrame)
                : "Low LOD 未生成";
            return string.Format(
                "烘焙完成：{0} 顶点，{1} 帧 @ {2} fps，纹理 {3}x{4}（每帧 {5} 行）；{6}\n资产：{7}",
                profile.cleanMesh.vertexCount, profile.totalFrameCount, profile.frameRate,
                profile.textureWidth, profile.textureHeight, profile.rowsPerFrame, lod, path);
        }
    }
}
