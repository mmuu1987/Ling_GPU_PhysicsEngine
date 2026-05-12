using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    internal readonly struct Stage7ScenarioGizmoUnit
    {
        public readonly bool IsValid;
        public readonly int TeamId;
        public readonly string RoleName;
        public readonly string UnitName;
        public readonly int UnitCount;
        public readonly Color Color;
        public readonly Vector3 SpawnCenter;
        public readonly Vector3 SpawnSize;
        public readonly MovementConfig Movement;

        public Stage7ScenarioGizmoUnit(UnitTypeConfig config, string roleName, Color color)
        {
            IsValid = config != null;
            TeamId = config != null ? config.teamId : 0;
            RoleName = string.IsNullOrWhiteSpace(roleName) ? "Unit" : roleName;
            UnitName = config != null && !string.IsNullOrWhiteSpace(config.unitTypeName) ? config.unitTypeName : RoleName;
            UnitCount = config != null && config.spawnConfig != null ? Mathf.Max(0, config.spawnConfig.unitCount) : 0;
            Color = color;
            SpawnCenter = config != null && config.spawnConfig != null ? config.spawnConfig.spawnCenter : Vector3.zero;
            Vector3 spawnSize = config != null && config.spawnConfig != null ? config.spawnConfig.spawnSize : Vector3.zero;
            SpawnSize = new Vector3(Mathf.Max(0.01f, spawnSize.x), Mathf.Max(0f, spawnSize.y), Mathf.Max(0.01f, spawnSize.z));
            Movement = config != null ? config.movementConfig : null;
        }
    }

    internal readonly struct Stage7ScenarioGizmoFlowField
    {
        public readonly bool IsValid;
        public readonly string Label;
        public readonly Vector2 Origin;
        public readonly Vector2 WorldSize;
        public readonly float CellSize;
        public readonly int ResolutionX;
        public readonly int ResolutionZ;
        public readonly Color Color;

        public Stage7ScenarioGizmoFlowField(string label, Vector2 origin, int resolution, float cellSize, Color color)
        {
            float safeCellSize = Mathf.Max(0.01f, cellSize);
            int safeResolution = Mathf.Max(1, resolution);
            IsValid = true;
            Label = string.IsNullOrWhiteSpace(label) ? "Flow Field" : label;
            Origin = origin;
            CellSize = safeCellSize;
            ResolutionX = safeResolution;
            ResolutionZ = safeResolution;
            WorldSize = new Vector2(safeResolution * safeCellSize, safeResolution * safeCellSize);
            Color = color;
        }

        public Vector3 Center => new Vector3(Origin.x + WorldSize.x * 0.5f, 0f, Origin.y + WorldSize.y * 0.5f);
        public Vector3 Size => new Vector3(WorldSize.x, 0f, WorldSize.y);
    }

    internal readonly struct Stage7ScenarioGizmoSimulation
    {
        public readonly bool IsValid;
        public readonly Vector2 WorldSize;
        public readonly float CellSize;
        public readonly Color Color;

        public Stage7ScenarioGizmoSimulation(Vector2 worldSize, float cellSize, Color color)
        {
            IsValid = true;
            WorldSize = new Vector2(Mathf.Max(0.01f, worldSize.x), Mathf.Max(0.01f, worldSize.y));
            CellSize = Mathf.Max(0.01f, cellSize);
            Color = color;
        }

        public Vector2 Origin => new Vector2(-WorldSize.x * 0.5f, -WorldSize.y * 0.5f);
        public int ResolutionX => Mathf.Max(1, Mathf.CeilToInt(WorldSize.x / CellSize));
        public int ResolutionZ => Mathf.Max(1, Mathf.CeilToInt(WorldSize.y / CellSize));
        public Vector3 Center => Vector3.zero;
        public Vector3 Size => new Vector3(WorldSize.x, 0f, WorldSize.y);
    }
}
