using System;
using System.Globalization;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Read-only preview contract, not a new simulation radius policy.
    /// Current shipped types use DefaultSwordUnit. Unknown/custom modules are deliberately
    /// not instantiated from the UI and must not be presented as verified physical data.</summary>
    public readonly struct UnitPreviewRadius
    {
        public readonly bool Available;
        public readonly float BaseRadius;
        public readonly string Source;
        public float Diameter => BaseRadius * 2;
        private UnitPreviewRadius(bool available, float radius, string source)
        { Available = available; BaseRadius = radius; Source = source; }
        public static UnitPreviewRadius Read(UnitTypeConfig config)
        {
            if (config == null) return new UnitPreviewRadius(false, 0, "未配置兵种");
            string type = config.unitTypeClassName;
            if (!string.IsNullOrWhiteSpace(type) && type != typeof(DefaultSwordUnit).FullName && type != typeof(DefaultSwordUnit).AssemblyQualifiedName)
                return new UnitPreviewRadius(false, 0, "自定义模块：尚未验证半径口径");
            // Use the actual production builder (including its missing-config default and
            // minimum clamp); never mutate the asset or duplicate its default value here.
            float radius = new DefaultSwordUnit(config).BuildGpuSettings().agentRadius;
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0)
                return new UnitPreviewRadius(false, 0, "半径无效：不绘制推测圆环");
            return new UnitPreviewRadius(true, radius, config.flockingConfig != null
                ? "FlockingConfig.agentRadius（模板配置，经 GPU 构建钳制）" : "UnitTypeGpuSettings 默认值（缺少群体配置）");
        }
        public UnitPreviewRadius WithValue(float value, string source)
        {
            if (!Available) return this;
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            return new UnitPreviewRadius(true, value, source);
        }
        // AgentDataCommon.hlsl / QueryCombatNeighborhood: contact body radius only.
        // Ordinary separation/static obstacles/projectiles have DIFFERENT rules.
        public float ContactRadius(Vector3 scale) => Available
            ? BaseRadius * Mathf.Max(.01f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))) : 0;
        public string Summary => Available
            ? "物理基准半径 " + BaseRadius.ToString("R", CultureInfo.InvariantCulture) + " m   ·   直径 " + Diameter.ToString("R", CultureInfo.InvariantCulture) + " m"
            : "物理半径：暂不可用";
        public const string Legend = "深金色圈：1× 接触半径 · 非硬碰撞边界 / 攻击范围";
    }
}

