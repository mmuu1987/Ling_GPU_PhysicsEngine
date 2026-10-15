using System;
using System.Linq;
using UnityEngine;
namespace MassEngine.Game
{
    /// <summary>Optional battlefield-local roster library and authoring safety; null policy preserves legacy Scenario-only menus.</summary>
    [CreateAssetMenu(menuName="MassEngine/Battlefield Roster Policy")]
    public sealed class WarSandboxRosterPolicy : ScriptableObject
    {
        public UnitTypeConfig[] templates=Array.Empty<UnitTypeConfig>();
        public int maximumUnits=256;
        public float maximumRadius=.8f;
        public bool useTemplateSpawnDefaults=true;
        public bool restrictLargeToTeamZero;
        /// <summary>C4 (2026-10-12): cap on large bodies (radius above half a spatial cell, the GPU
        /// large-body rule) in one deployment. 0 keeps the old rule: such bodies are rejected.</summary>
        public int maximumLargeUnits;
        [TextArea]public string explanation;
        /// <summary>Same test as AgentDataCommon.hlsl IsLargeBodyRadius.</summary>
        public static bool IsLargeBody(float radius,float cellSize)=>radius>cellSize*.5f;
        /// <summary>Largest body the GPU large path covers (Kaiju B 2026-10-15): the body scan
        /// (LARGE_BODY_SCAN_MAX_CELL_RADIUS 5: r + half a cell &lt; 5 cells), two such bodies in the pair scan
        /// (LARGE_PAIR_SCAN_MAX_CELL_RADIUS 9: r1 + r2 &lt; 9 cells) and the stamp (6 cells) all stay in range.</summary>
        public static float MaximumLargeBodyRadius(float cellSize)=>cellSize*4.5f-.01f;
        public bool TryValidateDefinition(out string error)
        {
            error=null;if(templates==null||templates.Length==0||maximumUnits<1||maximumRadius<=0||maximumLargeUnits<0){error="战场角色清单配置无效。";return false;}
            if(templates.Distinct().Count()!=templates.Length){error="战场角色清单重复。";return false;}
            foreach(var t in templates){var v=ConfigValidator.Validate(t);if(!v.IsValid){error="角色清单："+string.Join("\n",v.Errors);return false;}if(t.flockingConfig.agentRadius>maximumRadius+.0001f){error="角色尺寸超出此战场："+t.unitTypeName;return false;}}
            return true;
        }
        /// <summary>spatial=false skips the per-formation packing (footprint) check; caps and roster rules always apply.</summary>
        public bool TryValidate(WarSandboxDeploymentDraft draft,SimulationConfig simulation,out string error,bool spatial=true)
        {
            if(!TryValidateDefinition(out error))return false;long total=0,large=0;
            for(int i=0;i<draft.Count;i++)
            {
                var e=draft[i];if(!templates.Contains(e.template)){error="此战场不提供该角色；大型单位请使用大型适配场。";return false;}
                total+=e.count;float radius=e.template.flockingConfig.agentRadius;
                if(restrictLargeToTeamZero && radius>1 && e.teamId!=0){error="本大型适配场仅允许大兽在攻方，守方使用适配骑士；未验证大兽互斗。";return false;}if(simulation==null||float.IsNaN(simulation.cellSize)){error="此战场网格不覆盖该角色占地。";return false;}
                if(IsLargeBody(radius,simulation.cellSize))
                {
                    // C4: large bodies use the GPU large path (footprint stamping) instead of fitting one cell.
                    if(maximumLargeUnits<=0){error="此战场网格不覆盖该角色占地。";return false;}
                    if(radius>MaximumLargeBodyRadius(simulation.cellSize)){error="编成 "+(i+1)+"：角色半径 "+radius.ToString("0.##")+" 米超出大型单位支持范围（最大 "+MaximumLargeBodyRadius(simulation.cellSize).ToString("0.##")+" 米）。";return false;}
                    large+=e.count;
                }
                float area=Mathf.Max(.001f,e.Size.x*e.Size.z);float packing=1/(4*radius*radius);
                if(spatial&&e.count/area>packing+.0001f){error="编成 "+(i+1)+"：角色占地过大，请降低人数或扩大阵型。";return false;}
            }
            if(large>maximumLargeUnits){error="此战场最多 "+maximumLargeUnits+" 个大型单位（当前 "+large+"）。";return false;}
            if(total>maximumUnits){error="此角色整合预览最多 "+maximumUnits+" 个单位；未作大规模性能验收。";return false;}
            error=null;return true;
        }
        public WarSandboxDeploymentEntry Select(WarSandboxDeploymentEntry prior,UnitTypeConfig template)
        {
            if(!templates.Contains(template))throw new InvalidOperationException("Template not allowed by this battlefield roster.");
            var next=useTemplateSpawnDefaults?WarSandboxDeploymentEntry.From(template):prior;
            if(!useTemplateSpawnDefaults&&WarSandboxDeploymentEntry.IsLargeTemplate(template)!=WarSandboxDeploymentEntry.IsLargeTemplate(prior.template))
            {
                // C4: swapping between a crowd role and a large body restarts from that class's default block.
                next.count=WarSandboxDeploymentEntry.DefaultCount(template);next.density=template.spawnConfig.formationDensity;
                next.aspect=template.spawnConfig.formationAspect;next.manualSize=Vector3.zero;
            }
            next.template=template;next.teamId=prior.teamId;next.center=prior.center;next.FitDensityToBody();return next;
        }
    }
}
