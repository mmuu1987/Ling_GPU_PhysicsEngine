using UnityEngine;

namespace MassEngine
{
    public struct MassGpuShaderSet
    {
        public readonly ComputeShader SpatialHashShader;
        public readonly ComputeShader RuntimeFlowShader;
        public readonly ComputeShader CombatSimulationShader;
        public readonly ComputeShader LodClassificationShader;

        public readonly int ClearGrid;
        public readonly int BuildSpatialHash;
        public readonly int ClearRuntimeFlowResources;
        public readonly int BuildRuntimeTargetDensity;
        public readonly int SelectRuntimeFlowTargets;
        public readonly int GenerateRuntimeFlowField;
        public readonly int ClearDensityMap;
        public readonly int BuildDensityMap;
        public readonly int ClearPendingDamage;
        public readonly int SimulateCombatAndAccumulateDamage;
        public readonly int ClassifyVisibleAgentsForUnitType;

        public bool IsValid
        {
            get
            {
                return SpatialHashShader != null &&
                       RuntimeFlowShader != null &&
                       CombatSimulationShader != null &&
                       LodClassificationShader != null;
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
            if (LodClassificationShader == null) missing.Append("LodClassification ");
            return missing.ToString().TrimEnd();
        }

        private MassGpuShaderSet(
            ComputeShader spatialHashShader,
            ComputeShader runtimeFlowShader,
            ComputeShader combatSimulationShader,
            ComputeShader lodClassificationShader)
        {
            SpatialHashShader = spatialHashShader;
            RuntimeFlowShader = runtimeFlowShader;
            CombatSimulationShader = combatSimulationShader;
            LodClassificationShader = lodClassificationShader;

            ClearGrid = FindKernelOrInvalid(spatialHashShader, "ClearGrid");
            BuildSpatialHash = FindKernelOrInvalid(spatialHashShader, "BuildSpatialHash");
            ClearRuntimeFlowResources = FindKernelOrInvalid(runtimeFlowShader, "ClearRuntimeFlowResources");
            BuildRuntimeTargetDensity = FindKernelOrInvalid(runtimeFlowShader, "BuildRuntimeTargetDensity");
            SelectRuntimeFlowTargets = FindKernelOrInvalid(runtimeFlowShader, "SelectRuntimeFlowTargets");
            GenerateRuntimeFlowField = FindKernelOrInvalid(runtimeFlowShader, "GenerateRuntimeFlowField");
            ClearDensityMap = FindKernelOrInvalid(combatSimulationShader, "ClearDensityMap");
            BuildDensityMap = FindKernelOrInvalid(combatSimulationShader, "BuildDensityMap");
            ClearPendingDamage = FindKernelOrInvalid(combatSimulationShader, "ClearPendingDamage");
            SimulateCombatAndAccumulateDamage = FindKernelOrInvalid(combatSimulationShader, "SimulateCombatAndAccumulateDamage");
            ClassifyVisibleAgentsForUnitType = FindKernelOrInvalid(lodClassificationShader, "ClassifyVisibleAgentsForUnitType");
        }

        public static MassGpuShaderSet Find(
            ComputeShader spatialHashShader,
            ComputeShader runtimeFlowShader,
            ComputeShader combatSimulationShader,
            ComputeShader lodClassificationShader)
        {
            return new MassGpuShaderSet(spatialHashShader, runtimeFlowShader, combatSimulationShader, lodClassificationShader);
        }

        public void SetFloat(int id, float value)
        {
            SetFloatIfPresent(SpatialHashShader, id, value);
            SetFloatIfPresent(RuntimeFlowShader, id, value);
            SetFloatIfPresent(CombatSimulationShader, id, value);
            SetFloatIfPresent(LodClassificationShader, id, value);
        }

        public void SetInt(int id, int value)
        {
            SetIntIfPresent(SpatialHashShader, id, value);
            SetIntIfPresent(RuntimeFlowShader, id, value);
            SetIntIfPresent(CombatSimulationShader, id, value);
            SetIntIfPresent(LodClassificationShader, id, value);
        }

        public void SetInts(int id, int x, int y)
        {
            SetIntsIfPresent(SpatialHashShader, id, x, y);
            SetIntsIfPresent(RuntimeFlowShader, id, x, y);
            SetIntsIfPresent(CombatSimulationShader, id, x, y);
            SetIntsIfPresent(LodClassificationShader, id, x, y);
        }

        public void SetVector(int id, Vector4 value)
        {
            SetVectorIfPresent(SpatialHashShader, id, value);
            SetVectorIfPresent(RuntimeFlowShader, id, value);
            SetVectorIfPresent(CombatSimulationShader, id, value);
            SetVectorIfPresent(LodClassificationShader, id, value);
        }

        public void SetVectorArray(int id, Vector4[] values)
        {
            if (values == null || values.Length == 0)
                return;

            SetVectorArrayIfPresent(SpatialHashShader, id, values);
            SetVectorArrayIfPresent(RuntimeFlowShader, id, values);
            SetVectorArrayIfPresent(CombatSimulationShader, id, values);
            SetVectorArrayIfPresent(LodClassificationShader, id, values);
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
