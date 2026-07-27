using UnityEngine;

namespace MassEngine
{
    [CreateAssetMenu(menuName = "MassEngine/Scenario Config")]
    public sealed class ScenarioConfig : ScriptableObject
    {
        public UnitTypeConfig[] unitTypes = new UnitTypeConfig[0];
    }
}
