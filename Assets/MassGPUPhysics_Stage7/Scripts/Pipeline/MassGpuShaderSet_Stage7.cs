using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public struct MassGpuShaderSet_Stage7
    {
        public readonly ComputeShader SpatialHashShader;
        public readonly ComputeShader RuntimeFlowShader;
        public readonly ComputeShader CombatSimulationShader;
        public readonly ComputeShader LodClassificationShader;

        public readonly int ClearGrid;
        public readonly int BuildSpatialHash;
        public readonly int ClearRuntimeAttackerFlowResources;
        public readonly int BuildRuntimeAttackerTargetDensity;
        public readonly int SelectRuntimeAttackerFlowTargets;
        public readonly int GenerateRuntimeAttackerFlowField;
        public readonly int ClearRuntimeDefenderFlowResources;
        public readonly int BuildRuntimeDefenderTargetDensity;
        public readonly int SelectRuntimeDefenderFlowTargets;
        public readonly int GenerateRuntimeDefenderFlowField;
        public readonly int ClearDensityMap;
        public readonly int BuildDensityMap;
        public readonly int ClearPendingDamage;
        public readonly int SimulateCombatAndAccumulateDamage;
        public readonly int ClassifyVisibleAgentsByTeam;

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

        private MassGpuShaderSet_Stage7(
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
            ClearRuntimeAttackerFlowResources = FindKernelOrInvalid(runtimeFlowShader, "ClearRuntimeAttackerFlowResources");
            BuildRuntimeAttackerTargetDensity = FindKernelOrInvalid(runtimeFlowShader, "BuildRuntimeAttackerTargetDensity");
            SelectRuntimeAttackerFlowTargets = FindKernelOrInvalid(runtimeFlowShader, "SelectRuntimeAttackerFlowTargets");
            GenerateRuntimeAttackerFlowField = FindKernelOrInvalid(runtimeFlowShader, "GenerateRuntimeAttackerFlowField");
            ClearRuntimeDefenderFlowResources = FindKernelOrInvalid(runtimeFlowShader, "ClearRuntimeDefenderFlowResources");
            BuildRuntimeDefenderTargetDensity = FindKernelOrInvalid(runtimeFlowShader, "BuildRuntimeDefenderTargetDensity");
            SelectRuntimeDefenderFlowTargets = FindKernelOrInvalid(runtimeFlowShader, "SelectRuntimeDefenderFlowTargets");
            GenerateRuntimeDefenderFlowField = FindKernelOrInvalid(runtimeFlowShader, "GenerateRuntimeDefenderFlowField");
            ClearDensityMap = FindKernelOrInvalid(combatSimulationShader, "ClearDensityMap");
            BuildDensityMap = FindKernelOrInvalid(combatSimulationShader, "BuildDensityMap");
            ClearPendingDamage = FindKernelOrInvalid(combatSimulationShader, "ClearPendingDamage");
            SimulateCombatAndAccumulateDamage = FindKernelOrInvalid(combatSimulationShader, "SimulateCombatAndAccumulateDamage");
            ClassifyVisibleAgentsByTeam = FindKernelOrInvalid(lodClassificationShader, "ClassifyVisibleAgentsByTeam");
        }

        public static MassGpuShaderSet_Stage7 Find(
            ComputeShader spatialHashShader,
            ComputeShader runtimeFlowShader,
            ComputeShader combatSimulationShader,
            ComputeShader lodClassificationShader)
        {
            return new MassGpuShaderSet_Stage7(spatialHashShader, runtimeFlowShader, combatSimulationShader, lodClassificationShader);
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
