using UnityEngine;

namespace MassEngine
{
    public struct MassGpuShaderSet
    {
        public readonly ComputeShader SpatialHashShader;
        public readonly ComputeShader RuntimeFlowShader;
        public readonly ComputeShader CombatSimulationShader;
        public readonly ComputeShader LodClassificationShader;
        public readonly ComputeShader ProjectileShader;

        public readonly int ClearGrid;
        public readonly int BuildSpatialHash;
        // One set of flow kernels for every team; the team being rebuilt travels in the
        // flowTeamId uniform rather than in the kernel name.
        public readonly int ClearRuntimeFlowResources;
        public readonly int BuildRuntimeFlowTargetDensity;
        public readonly int SelectRuntimeFlowTargets;
        public readonly int GenerateRuntimeFlowField;
        public readonly int ClearDensityMap;
        public readonly int BuildDensityMap;
        public readonly int BuildEngagementSlotOccupancy;
        public readonly int ClearPendingDamage;
        public readonly int SimulateCombatAndAccumulateDamage;
        public readonly int ClearLaunchRequests;
        public readonly int ClassifyVisibleAgentsForUnitType;
        public readonly int SimulateProjectiles;
        public readonly int ClearProjectiles;
        /// <summary>Render-only kernel: a shader without it still simulates, it just draws no trails.</summary>
        public readonly int CollectActiveProjectiles;

        public bool IsValid
        {
            get
            {
                return SpatialHashShader != null &&
                       RuntimeFlowShader != null &&
                       CombatSimulationShader != null &&
                       LodClassificationShader != null &&
                       ProjectileShader != null &&
                       ClearLaunchRequests >= 0 &&
                       SimulateProjectiles >= 0 &&
                       ClearProjectiles >= 0;
            }
        }

        /// <summary>Human-readable list of missing shaders, empty string when valid.</summary>
        public string DescribeMissing()
        {
            if (IsValid)
                return string.Empty;

            System.Text.StringBuilder missing = new System.Text.StringBuilder();
            if (SpatialHashShader == null) missing.Append("SpatialHash ");
            if (RuntimeFlowShader == null) missing.Append("RuntimeFlow ");
            if (CombatSimulationShader == null) missing.Append("CombatSimulation ");
            else if (ClearLaunchRequests < 0) missing.Append("CombatSimulation/ClearLaunchRequests ");
            if (LodClassificationShader == null) missing.Append("LodClassification ");
            if (ProjectileShader == null)
                missing.Append("Projectile ");
            else
            {
                if (SimulateProjectiles < 0) missing.Append("Projectile/SimulateProjectiles ");
                if (ClearProjectiles < 0) missing.Append("Projectile/ClearProjectiles ");
            }
            return missing.ToString().TrimEnd();
        }

        private MassGpuShaderSet(
            ComputeShader spatialHashShader,
            ComputeShader runtimeFlowShader,
            ComputeShader combatSimulationShader,
            ComputeShader lodClassificationShader,
            ComputeShader projectileShader)
        {
            SpatialHashShader = spatialHashShader;
            RuntimeFlowShader = runtimeFlowShader;
            CombatSimulationShader = combatSimulationShader;
            LodClassificationShader = lodClassificationShader;

            ClearGrid = FindKernelOrInvalid(spatialHashShader, "ClearGrid");
            BuildSpatialHash = FindKernelOrInvalid(spatialHashShader, "BuildSpatialHash");
            ClearRuntimeFlowResources = FindKernelOrInvalid(runtimeFlowShader, "ClearRuntimeFlowResources");
            BuildRuntimeFlowTargetDensity = FindKernelOrInvalid(runtimeFlowShader, "BuildRuntimeFlowTargetDensity");
            SelectRuntimeFlowTargets = FindKernelOrInvalid(runtimeFlowShader, "SelectRuntimeFlowTargets");
            GenerateRuntimeFlowField = FindKernelOrInvalid(runtimeFlowShader, "GenerateRuntimeFlowField");
            ClearDensityMap = FindKernelOrInvalid(combatSimulationShader, "ClearDensityMap");
            BuildDensityMap = FindKernelOrInvalid(combatSimulationShader, "BuildDensityMap");
            BuildEngagementSlotOccupancy = FindKernelOrInvalid(combatSimulationShader, "BuildEngagementSlotOccupancy");
            ClearPendingDamage = FindKernelOrInvalid(combatSimulationShader, "ClearPendingDamage");
            SimulateCombatAndAccumulateDamage = FindKernelOrInvalid(combatSimulationShader, "SimulateCombatAndAccumulateDamage");
            ClearLaunchRequests = FindKernelOrInvalid(combatSimulationShader, "ClearLaunchRequests");
            ClassifyVisibleAgentsForUnitType = FindKernelOrInvalid(lodClassificationShader, "ClassifyVisibleAgentsForUnitType");

            ProjectileShader = projectileShader;
            SimulateProjectiles = FindKernelOrInvalid(projectileShader, "SimulateProjectiles");
            ClearProjectiles = FindKernelOrInvalid(projectileShader, "ClearProjectiles");
            // Deliberately absent from IsValid: projectile visuals are optional, and a
            // missing collect kernel must not block the whole simulation pipeline.
            CollectActiveProjectiles = FindKernelOrInvalid(projectileShader, "CollectActiveProjectiles");
        }

        public static MassGpuShaderSet Find(
            ComputeShader spatialHashShader,
            ComputeShader runtimeFlowShader,
            ComputeShader combatSimulationShader,
            ComputeShader lodClassificationShader,
            ComputeShader projectileShader)
        {
            return new MassGpuShaderSet(spatialHashShader, runtimeFlowShader, combatSimulationShader, lodClassificationShader, projectileShader);
        }

        public void SetFloat(int id, float value)
        {
            SetFloatIfPresent(SpatialHashShader, id, value);
            SetFloatIfPresent(RuntimeFlowShader, id, value);
            SetFloatIfPresent(CombatSimulationShader, id, value);
            SetFloatIfPresent(LodClassificationShader, id, value);
            SetFloatIfPresent(ProjectileShader, id, value);
        }

        public void SetInt(int id, int value)
        {
            SetIntIfPresent(SpatialHashShader, id, value);
            SetIntIfPresent(RuntimeFlowShader, id, value);
            SetIntIfPresent(CombatSimulationShader, id, value);
            SetIntIfPresent(LodClassificationShader, id, value);
            SetIntIfPresent(ProjectileShader, id, value);
        }

        public void SetInts(int id, int x, int y)
        {
            SetIntsIfPresent(SpatialHashShader, id, x, y);
            SetIntsIfPresent(RuntimeFlowShader, id, x, y);
            SetIntsIfPresent(CombatSimulationShader, id, x, y);
            SetIntsIfPresent(LodClassificationShader, id, x, y);
            SetIntsIfPresent(ProjectileShader, id, x, y);
        }

        public void SetVector(int id, Vector4 value)
        {
            SetVectorIfPresent(SpatialHashShader, id, value);
            SetVectorIfPresent(RuntimeFlowShader, id, value);
            SetVectorIfPresent(CombatSimulationShader, id, value);
            SetVectorIfPresent(LodClassificationShader, id, value);
            SetVectorIfPresent(ProjectileShader, id, value);
        }

        public void SetVectorArray(int id, Vector4[] values)
        {
            if (values == null || values.Length == 0)
                return;

            SetVectorArrayIfPresent(SpatialHashShader, id, values);
            SetVectorArrayIfPresent(RuntimeFlowShader, id, values);
            SetVectorArrayIfPresent(CombatSimulationShader, id, values);
            SetVectorArrayIfPresent(LodClassificationShader, id, values);
            SetVectorArrayIfPresent(ProjectileShader, id, values);
        }

        private static int FindKernelOrInvalid(ComputeShader shader, string kernelName)
        {
            if (shader == null)
                return -1;

            return shader.HasKernel(kernelName) ? shader.FindKernel(kernelName) : -1;
        }

        private static void SetFloatIfPresent(ComputeShader shader, int id, float value)
        {
            if (shader != null)
                shader.SetFloat(id, value);
        }

        private static void SetIntIfPresent(ComputeShader shader, int id, int value)
        {
            if (shader != null)
                shader.SetInt(id, value);
        }

        private static void SetIntsIfPresent(ComputeShader shader, int id, int x, int y)
        {
            if (shader != null)
                shader.SetInts(id, x, y);
        }

        private static void SetVectorIfPresent(ComputeShader shader, int id, Vector4 value)
        {
            if (shader != null)
                shader.SetVector(id, value);
        }

        private static void SetVectorArrayIfPresent(ComputeShader shader, int id, Vector4[] values)
        {
            if (shader != null)
                shader.SetVectorArray(id, values);
        }
    }
}
