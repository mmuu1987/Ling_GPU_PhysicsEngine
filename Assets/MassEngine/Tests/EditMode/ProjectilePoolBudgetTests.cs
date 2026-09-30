#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using MassEngine.Projectiles;
namespace MassEngine.Tests
{
    public sealed class ProjectilePoolBudgetTests
    {
        [TestCase(20000, .8f, 3f, 100000)]
        [TestCase(20000, .8f, 5f, 160000)]
        [TestCase(400000, .01f, 999f, 262144)]
        public void BudgetComesFromLifetimeCadenceAndHasAHardCeiling(int count,float interval,float lifetime,int expected)
        {
            var scenario=ScriptableObject.CreateInstance<ScenarioConfig>(); var unit=ScriptableObject.CreateInstance<UnitTypeConfig>();
            var spawn=ScriptableObject.CreateInstance<SpawnConfig>(); var combat=ScriptableObject.CreateInstance<CombatConfig>();
            try
            {
                unit.spawnConfig=spawn;unit.combatConfig=combat;spawn.unitCount=count;
                combat.projectileRange=20;combat.projectileSpeed=12;combat.attackInterval=interval;combat.projectileMaxLifetime=lifetime;
                scenario.unitTypes=new[]{unit}; Assert.AreEqual(expected,ProjectilePoolBudget.Calculate(scenario,count));
                Assert.AreEqual(0,ProjectilePoolBudget.Calculate(scenario,0));
                combat.projectileRange=0;Assert.AreEqual(Mathf.Max(1,count/4),ProjectilePoolBudget.Calculate(scenario,count));
            }
            finally { Object.DestroyImmediate(scenario);Object.DestroyImmediate(unit);Object.DestroyImmediate(spawn);Object.DestroyImmediate(combat); }
        }
    }
}
#endif
