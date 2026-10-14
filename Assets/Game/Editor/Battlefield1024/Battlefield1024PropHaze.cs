using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Scenery haze for the 1024 maps. Scene fog is off; ground and backdrop haze in their own shader by world
    /// radius. Props now use "MassEngine/Battlefield Props 1024" with the ground material's haze values, so distant
    /// mountains / trees fade like the terrain behind them. The menu re-points already generated P*.mat in place
    /// (property names match URP/Lit, so texture, tint, cutoff and cull carry over) without regenerating maps.
    /// </summary>
    public static class Battlefield1024PropHaze
    {
        public const string ShaderName = "MassEngine/Battlefield Props 1024";
        static readonly Regex PropMaterial = new Regex(@"^P\d+\.mat$");

        public static Shader FindShader()
        {
            var shader = Shader.Find(ShaderName);
            Battlefield1024Generator.Require(shader != null, "缺少着色器 " + ShaderName + "（Maps1024/Shared/BattlefieldProps1024.shader）");
            return shader;
        }

        public static void SetHaze(Material m, Vector4 color, float start, float end)
        {
            m.SetVector("_HazeColor", color); m.SetFloat("_HazeStart", start); m.SetFloat("_HazeEnd", end);
        }

        [MenuItem(Battlefield1024Generator.MenuRoot + "刷新景物雾效（不重新生成）", false, 20)]
        public static void RefreshAll()
        {
            var shader = FindShader();
            var log = new List<string>();
            foreach (string dir in AssetDatabase.GetSubFolders(Battlefield1024Generator.Root))
            {
                var ground = AssetDatabase.LoadAssetAtPath<Material>(dir + "/Materials/Ground.mat");
                if (ground == null || !ground.HasProperty("_HazeStart")) { log.Add(dir + "：没有带雾参数的 Ground.mat，跳过"); continue; }
                Vector4 color = ground.GetVector("_HazeColor"); float start = ground.GetFloat("_HazeStart"), end = ground.GetFloat("_HazeEnd");
                int changed = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { dir + "/Materials" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!PropMaterial.IsMatch(Path.GetFileName(path))) continue;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) continue;
                    int queue = m.renderQueue;
                    if (m.shader != shader) m.shader = shader;
                    m.renderQueue = queue;
                    SetHaze(m, color, start, end);
                    m.enableInstancing = true;
                    EditorUtility.SetDirty(m);
                    changed++;
                }
                log.Add(dir + "：" + changed + " 个景物材质 → " + ShaderName + "，雾 " + start + "–" + end + " m，颜色 " + color);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Battlefield1024Generator.LogDir);
            File.WriteAllText(Path.Combine(Battlefield1024Generator.LogDir, "prop-haze.txt"), string.Join("\n", log) + "\n");
            Debug.Log("[战场 1024] 刷新景物雾效\n" + string.Join("\n", log));
        }
    }
}
