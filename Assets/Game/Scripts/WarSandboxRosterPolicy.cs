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
        [TextArea]public string explanation;
        public bool TryValidateDefinition(out string error)
        {
            error=null;if(templates==null||templates.Length==0||maximumUnits<1||maximumRadius<=0){error="战场角色清单配置无效。";return false;}
            if(templates.Distinct().Count()!=templates.Length){error="战场角色清单重复。";return false;}
            foreach(var t in templates){var v=ConfigValidator.Validate(t);if(!v.IsValid){error="角色清单："+string.Join("\n",v.Errors);return false;}if(t.flockingConfig.agentRadius>maximumRadius+.0001f){error="角色尺寸超出此战场："+t.unitTypeName;return false;}}
            return true;
        }
        public bool TryValidate(WarSandboxDeploymentDraft draft,SimulationConfig simulation,out string error)
        {
            if(!TryValidateDefinition(out error))return false;long total=0;
            for(int i=0;i<draft.Count;i++)
            {
                var e=draft[i];if(!templates.Contains(e.template)){error="此战场不提供该角色；大型单位请使用大型适配场。";return false;}
                total+=e.count;float radius=e.template.flockingConfig.agentRadius;
                if(restrictLargeToTeamZero && radius>1 && e.teamId!=0){error="本大型适配场仅允许大兽在攻方，守方使用适配骑士；未验证大兽互斗。";return false;}if(simulation==null||simulation.cellSize+1e-4f<2*radius){error="此战场网格不覆盖该角色占地。";return false;}
                float area=Mathf.Max(.001f,e.Size.x*e.Size.z);float packing=1/(4*radius*radius);
                if(e.count/area>packing+.0001f){error="编成 "+(i+1)+"：角色占地过大，请降低人数或扩大阵型。";return false;}
            }
            if(total>maximumUnits){error="此角色整合预览最多 "+maximumUnits+" 个单位；未作大规模性能验收。";return false;}
            error=null;return true;
        }
        public WarSandboxDeploymentEntry Select(WarSandboxDeploymentEntry prior,UnitTypeConfig template)
        {
            if(!templates.Contains(template))throw new InvalidOperationException("Template not allowed by this battlefield roster.");
            var next=useTemplateSpawnDefaults?WarSandboxDeploymentEntry.From(template):prior;next.template=template;next.teamId=prior.teamId;next.center=prior.center;return next;
        }
    }
}
