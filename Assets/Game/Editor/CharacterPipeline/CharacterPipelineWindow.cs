using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Thin editor entry; all checks/baking use the same core as batch reproduction.</summary>
    public sealed class CharacterPipelineWindow : EditorWindow
    {
        private CharacterRecipe recipe;
        private SerializedObject serializedRecipe;
        private Vector2 scroll;
        private string message="Choose an existing prepared-model recipe, or create a recipe asset.";
        private CharacterPipelineReport lastReport;
        [MenuItem("MassEngine/Character pipeline/Open workflow")]
        public static void Open() => GetWindow<CharacterPipelineWindow>("角色接入流程");
        [MenuItem("MassEngine/Character pipeline/Select Knight example")]
        private static void SelectExample()
        {
            Open();var w=GetWindow<CharacterPipelineWindow>();w.recipe=AssetDatabase.LoadAssetAtPath<CharacterRecipe>(CharacterPipeline.Root+"/Recipes/Knight.asset");Selection.activeObject=w.recipe;
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("已准备好的Generic模型 → 尺寸/附件/预算检查 → 烘焙与全帧几何验证 → LOD/对照图 → 新的独立兵种/试点。不会覆盖旧资源。动画路径必须已匹配；不做自动重定向。",MessageType.Info);
            var next=(CharacterRecipe)EditorGUILayout.ObjectField("配方",recipe,typeof(CharacterRecipe),false);
            if(next!=recipe){recipe=next;serializedRecipe=null;lastReport=null;}
            if(GUILayout.Button("新建空白配方资产"))
            {
                string path=EditorUtility.SaveFilePanelInProject("新建角色配方","CharacterRecipe","asset","选择未使用的资产路径");
                if(!string.IsNullOrEmpty(path))
                {
                    if(File.Exists(path) || File.Exists(path+".meta")){message="路径已存在；请选择新名字。";return;}
                    recipe=CreateInstance<CharacterRecipe>();AssetDatabase.CreateAsset(recipe,path);serializedRecipe=null;Selection.activeObject=recipe;
                }
            }
            if(recipe!=null)
            {
                if(serializedRecipe==null || serializedRecipe.targetObject!=recipe)serializedRecipe=new SerializedObject(recipe);
                scroll=EditorGUILayout.BeginScrollView(scroll);serializedRecipe.Update();
                var p=serializedRecipe.GetIterator();bool enter=true;
                while(p.NextVisible(enter)){enter=false;if(p.name=="m_Script")continue;EditorGUILayout.PropertyField(p,true);}
                serializedRecipe.ApplyModifiedProperties();EditorGUILayout.EndScrollView();
                EditorGUILayout.LabelField("新产物",CharacterPipeline.Output(recipe));
                using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if(GUILayout.Button("1 · 仅检查（不生成资产）"))Attempt(()=>lastReport=CharacterPipeline.Preflight(recipe));
                    if(GUILayout.Button("2 · 烘焙、验证并生成新版本"))
                        if(EditorUtility.DisplayDialog("生成新版本","会串行烘焙并生成GPU检查图。自动门禁失败将回滚本次新资产；旧资产不会重绑。若创建独立试点，需要切换场景。","继续","取消"))
                            Attempt(()=>{AssetDatabase.SaveAssetIfDirty(recipe);lastReport=CharacterPipeline.Run(recipe);});
                }
            }
            EditorGUILayout.HelpBox(message,MessageType.None);
            if(lastReport!=null)
            {
                EditorGUILayout.LabelField("几何 / 半径",lastReport.targetBodyHeight+" m height / "+lastReport.agentRadius+" m radius (independent)");
                EditorGUILayout.LabelField("Full / Low / frames",lastReport.fullVertices+" / "+lastReport.lowVertices+" / "+lastReport.totalFrames);
                EditorGUILayout.LabelField("位置误差",lastReport.maxPositionError+" m; gate "+lastReport.allowedPositionError+" m");
                if(!string.IsNullOrEmpty(lastReport.evidence) && GUILayout.Button("3 · 打开检查图与人工清单"))EditorUtility.RevealInFinder(Path.GetFullPath(lastReport.evidence));
                if(lastReport.automatedPassed && GUILayout.Button("选中新生成的独立兵种"))Selection.activeObject=AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(lastReport.output+"/Unit.asset");
                EditorGUILayout.HelpBox("人工检查不可跳过：头颈/肩部、手与挂件、脚落地、四动作与LOD色块。参考图位置来自独立蒙皮计算，并共用VAT shader/UV/法线；不是另一套原材质验收。大型体型还需另做碰撞、攻击距离和镜头专项。",MessageType.Warning);
            }
        }
        private void Attempt(Action action)
        {
            try{action();message=lastReport.status+"\n"+string.Join("\n",lastReport.warnings);}
            catch(Exception ex){lastReport=null;message=ex.Message;Debug.LogException(ex);}
        }
    }
}
