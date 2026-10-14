using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// 兵种平衡 (2026-10-14), user request "全部兵种的移动速度都翻一倍": doubles MovementConfig.maxSpeed of every
    /// player-facing main-catalog template (对照 / 密集方阵 variants excluded) and raises the move-animation
    /// playback so legs keep up: moveReferenceSpeed := max(effective old reference, old maxSpeed),
    /// moveAnimationSpeedMax := max(old, 2). Each touched .asset is copied to outputs/Chal/bak/speed2 first; runs once.
    /// </summary>
    public static class BalanceSpeedApply
    {
        const string Bak = "outputs/Chal/bak/speed2";
        const string Done = Bak + "/DONE.txt";

        public static bool IsPlayer(WarSandboxUnitTemplateEntry t) =>
            t != null && t.config != null && !t.hiddenFromSelection &&
            !t.config.unitTypeName.Contains("对照") && !t.config.unitTypeName.Contains("密集方阵");

        [MenuItem("Tools/挑战模式/兵种移动速度×2（写资源，先备份，只执行一次）")]
        public static void Run()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Write("ERROR playing"); return; }
            if (File.Exists(Done)) { Write("REFUSED already applied: " + File.ReadAllText(Done)); return; }
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
            var moves = new Dictionary<MovementConfig, string>();
            var anims = new Dictionary<AnimationConfig, float>(); // anim -> old maxSpeed of its units (max)
            foreach (var t in catalog.templates)
            {
                if (!IsPlayer(t)) continue;
                var u = t.config;
                if (u.movementConfig == null) { log.AppendLine("WARN no movement " + t.templateId); continue; }
                moves[u.movementConfig] = t.templateId;
                if (u.animationConfig != null)
                    anims[u.animationConfig] = Mathf.Max(anims.TryGetValue(u.animationConfig, out var o) ? o : 0f, u.movementConfig.maxSpeed);
            }
            Directory.CreateDirectory(Bak);
            var paths = new HashSet<string>();
            foreach (var m in moves.Keys) paths.Add(AssetDatabase.GetAssetPath(m));
            foreach (var a in anims.Keys) paths.Add(AssetDatabase.GetAssetPath(a));
            foreach (var p in paths)
            {
                string dst = Path.Combine(Bak, p.Replace('/', '~') + ".bak");
                File.Copy(p, dst, true);
                log.AppendLine("BAK " + p + " -> " + dst);
            }
            foreach (var kv in anims)
            {
                var a = kv.Key; float oldMax = kv.Value;
                float oldRef = a.moveReferenceSpeed, oldAnim = a.moveAnimationSpeedMax;
                float eff = oldRef > 0f ? oldRef : oldMax;
                a.moveReferenceSpeed = Mathf.Max(eff, oldMax);
                a.moveAnimationSpeedMax = Mathf.Max(oldAnim, 2f);
                EditorUtility.SetDirty(a);
                log.AppendLine("ANIM " + AssetDatabase.GetAssetPath(a) + " ref " + oldRef + "->" + a.moveReferenceSpeed + " max " + oldAnim + "->" + a.moveAnimationSpeedMax);
            }
            foreach (var kv in moves)
            {
                var m = kv.Key; float old = m.maxSpeed;
                m.maxSpeed = old * 2f;
                EditorUtility.SetDirty(m);
                log.AppendLine("MOVE " + AssetDatabase.GetAssetPath(m) + " (" + kv.Value + ") maxSpeed " + old + "->" + m.maxSpeed);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Done, System.DateTime.Now.ToString("s") + " moves=" + moves.Count + " anims=" + anims.Count + "\n");
            log.AppendLine("DONE moves=" + moves.Count + " anims=" + anims.Count);
            Write(log.ToString());
        }

        static void Write(string s)
        {
            Directory.CreateDirectory("Logs/Chal");
            File.WriteAllText("Logs/Chal/speed2.txt", s, new UTF8Encoding(false));
            Debug.Log("[BalanceSpeedApply] Logs/Chal/speed2.txt");
        }
    }
}
