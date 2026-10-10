// Stress200KBuilder.cs  –  Editor-only builder for a 200k stress test scene.
// Creates a 1024×1024 scene derived from TerrainScale15, NO terrain asset
// (uses legacy flat plane sized to simulationWorldSize).

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class Stress200KBuilder
    {
        public const string Root = "Assets/Game/Experiments/Stress200K";
        public const string Battle = Root + "/Battle200K.unity";
        public const string Menu = Root + "/LaunchMenu.unity";
        public const string Logs = "Logs/Stress200K";

        const float WorldSize = 1024f;
        const int FlowResolution = 256;
        const float CellSize = 4f;

        static void Require(bool b, string e) { if (!b) throw new Exception(e); }

        static T Copy<T>(T obj, string name) where T : Object
        {
            var a = Object.Instantiate(obj);
            a.name = name;
            AssetDatabase.CreateAsset(a, Root + "/" + name + ".asset");
            return a;
        }

        [MenuItem("MassEngine/Stress Test/Build 200K Scene")]
        public static void Build()
        {
            Require(!Directory.Exists(Root), "Refuse overwrite: " + Root + " already exists.");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Logs);
            AssetDatabase.Refresh();

            // --- Copy base scene from TerrainScale15 ---
            string source = "Assets/Game/Content/Battlefields/ForestTerrain/ForestHills.unity";
            Require(AssetDatabase.CopyAsset(source, Battle), "Failed to copy scene from " + source);
            var scene = EditorSceneManager.OpenScene(Battle, OpenSceneMode.Single);

            var m = Object.FindFirstObjectByType<MassEngineManager>();
            var d = Object.FindFirstObjectByType<WarSandboxRuntimeDeployment>();
            Require(m != null && d != null, "Missing manager or deployment in copied scene.");
            Require(m.systemConfig != null, "Manager missing systemConfig");

            // --- Clone and modify system config (simulation + flow) ---
            var sysConfig = Copy(m.systemConfig, "SystemConfig200K");
            // Clone simulation config and resize to 1024×1024
            var simSrc = sysConfig.simulationConfig ?? m.systemConfig.simulationConfig;
            var simConfig = Copy(simSrc, "Simulation200K");
            simConfig.simulationWorldSize = new Vector2(WorldSize, WorldSize);
            simConfig.boundaryPadding = 4f;
            simConfig.cellSize = CellSize;
            simConfig.maxAgentsPerCell = 96;
            EditorUtility.SetDirty(simConfig);
            // Clone flow config and resize
            var flowSrc = sysConfig.runtimeFlowConfig ?? m.systemConfig.runtimeFlowConfig;
            var flowConfig = Copy(flowSrc, "Flow200K");
            flowConfig.flowFieldResolution = FlowResolution;
            flowConfig.flowFieldCellSize = WorldSize / FlowResolution;
            EditorUtility.SetDirty(flowConfig);
            // Wire them into the system config
            sysConfig.simulationConfig = simConfig;
            sysConfig.runtimeFlowConfig = flowConfig;
            EditorUtility.SetDirty(sysConfig);

            // --- Clone scenario, roster, catalog ---
            var scenario = Copy(m.scenarioConfig, "Scenario200K");
            var policy = Copy(d.rosterPolicy, "RosterPolicy200K");
            var catalog = Copy(d.battlefieldCatalog, "Catalog200K");

            // --- Clone unit templates with tight jitter ---
            var map = new System.Collections.Generic.Dictionary<UnitTypeConfig, UnitTypeConfig>();
            foreach (var original in policy.templates)
            {
                var unit = Copy(original, "Template" + map.Count);
                unit.spawnConfig = Copy(original.spawnConfig, "Spawn" + map.Count);
                unit.spawnConfig.formationJitterFraction = 0.02f;
                map[original] = unit;
                EditorUtility.SetDirty(unit);
                EditorUtility.SetDirty(unit.spawnConfig);
            }
            scenario.unitTypes = scenario.unitTypes.Select(t => map[t]).ToArray();
            policy.templates = policy.templates.Select(t => map[t]).ToArray();
            foreach (var t in catalog.templates)
                if (t.config != null && map.TryGetValue(t.config, out var replacement))
                    t.config = replacement;

            policy.maximumUnits = 200000;
            policy.explanation = "200k 压力测试：默认2048人。紧密阵型0.7密度，双方各10万人。\n" +
                                 "1024×1024 平坦地形，空间哈希 cellSize=4m，流场256×256。";

            foreach (var e in catalog.entries)
            {
                e.id = "stress200k";
                e.scenePath = Battle;
                e.displayName = "200K 压力测试";
                e.description = "1024×1024 平坦 · 紧密 0.7 · 双方各10万\n仅用于性能瓶颈分析。";
                e.briefing = "200,000 单位压力测试。\n1024×1024 平坦地形。\n此为高负载试验。";
            }
            catalog.defaultEntryId = "stress200k";

            // --- Apply configs to manager ---
            m.terrainSurfaceAsset = null;
            m.systemConfig = sysConfig;
            m.scenarioConfig = scenario;
            d.rosterPolicy = policy;
            d.battlefieldCatalog = catalog;

            // Attach the perf probe
            if (d.GetComponent<Stress200KPerfProbe>() == null)
                d.gameObject.AddComponent<Stress200KPerfProbe>();

            foreach (var obj in new Object[] { m, d, scenario, policy, catalog, sysConfig })
                EditorUtility.SetDirty(obj);

            EditorSceneManager.SaveScene(scene);

            // --- Copy and configure launch menu ---
            Require(AssetDatabase.CopyAsset("Assets/Game/Content/Battlefields/ForestTerrain/LaunchMenu.unity", Menu),
                "Menu copy failed");
            var menuScene = EditorSceneManager.OpenScene(Menu, OpenSceneMode.Single);
            Object.FindFirstObjectByType<WarSandboxSceneSession>().catalog =
                AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root + "/Catalog200K.asset");
            EditorSceneManager.SaveScene(menuScene);

            AssetDatabase.SaveAssets();
            Debug.Log("Stress200K: Scene built at " + Root);
        }
    }
}