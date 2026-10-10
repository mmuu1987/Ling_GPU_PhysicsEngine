using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace MassEngine.Game.Editor {
 // Applies to the actual scenes being built, not just EditorBuildSettings or a specific builder.
 public sealed class ActorMaterialBuildGuard : IProcessSceneWithReport {
  public int callbackOrder=>-900;
  public void OnProcessScene(Scene scene,BuildReport report){if(report==null)return;RequireValid(scene);Directory.CreateDirectory("Logs/ActorMaterialGuard");File.AppendAllText("Logs/ActorMaterialGuard/passed-scenes.log",DateTime.UtcNow.ToString("o")+" | "+report.summary.guid+" | "+scene.path+" | units="+Units(scene).Count+"\n");}
  public static void RequireValid(Scene scene){var errors=Inspect(scene);if(errors.Count>0)throw new BuildFailedException("ACTOR_ASSET_GUARD: "+scene.path+"\n"+string.Join("\n",errors));}
  public static List<UnitTypeConfig> Units(Scene scene){var set=new HashSet<UnitTypeConfig>();foreach(var root in scene.GetRootGameObjects()){
   foreach(var manager in root.GetComponentsInChildren<MassEngineManager>(true))if(manager.scenarioConfig!=null&&manager.scenarioConfig.unitTypes!=null)foreach(var u in manager.scenarioConfig.unitTypes)if(u!=null)set.Add(u);
   foreach(var session in root.GetComponentsInChildren<WarSandboxSceneSession>(true))AddCatalog(session.catalog,set);
   foreach(var deployment in root.GetComponentsInChildren<WarSandboxRuntimeDeployment>(true)){AddCatalog(deployment.battlefieldCatalog,set);if(deployment.rosterPolicy!=null&&deployment.rosterPolicy.templates!=null)foreach(var u in deployment.rosterPolicy.templates)if(u!=null)set.Add(u);}
  }return set.OrderBy(u=>AssetDatabase.GetAssetPath(u)).ToList();}
  static void AddCatalog(WarSandboxBattlefieldCatalog c,HashSet<UnitTypeConfig> set){if(c!=null&&c.templates!=null)foreach(var t in c.templates)if(t!=null&&t.config!=null)set.Add(t.config);}
  public static List<string> Inspect(Scene scene){var errors=new List<string>();var seen=new HashSet<Material>();foreach(var u in Units(scene))InspectUnit(u,errors,seen);return errors;}
  public static void InspectUnit(UnitTypeConfig unit,List<string> errors,HashSet<Material> seen=null){
   string owner=unit==null?"<missing unit>":AssetDatabase.GetAssetPath(unit)+" ["+unit.unitTypeName+"]";
   if(unit==null||unit.renderConfig==null){errors.Add(owner+": missing render config");return;}
   var resolved=ResolvedUnitTypeRuntime.Resolve(unit,1.5f);
   for(int lod=0;lod<3;lod++){string context=owner+" LOD"+lod;var mat=resolved.GetMaterial(lod);if(resolved.GetMesh(lod)==null)errors.Add(context+": missing effective mesh");if(mat==null){errors.Add(context+": missing material");continue;}if(mat.shader==null)errors.Add(context+": missing shader");if(seen==null||seen.Add(mat))InspectMaterial(mat,context,errors);
    if(unit.renderConfig.vatProfile!=null){var block=resolved.GetBlock(lod);if(block==null||block.GetTexture("_VATPosTex")==null||block.GetTexture("_VATNormTex")==null)errors.Add(context+": missing effective VAT position/normal texture");}
   }
  }
  public static void InspectMaterial(Material material,string context,List<string> errors){
   string path=AssetDatabase.GetAssetPath(material);if(string.IsNullOrEmpty(path)||!File.Exists(path))return;
   // Check GUIDs actually serialized as assigned. An intentional null slot for a solid-color
   // material is legal, but an assigned texture that disappeared is never silently accepted.
   string yaml=File.ReadAllText(path);var assigned=new Dictionary<string,string>();
   foreach(Match m in Regex.Matches(yaml,@"- ([A-Za-z0-9_]+):\s*\r?\n\s+m_Texture:\s*\{[^}\r\n]*guid:\s*([a-fA-F0-9]{32})"))assigned[m.Groups[1].Value]=m.Groups[2].Value;
   var slots=new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");if(slots==null)return;
   for(int i=0;i<slots.arraySize;i++){var pair=slots.GetArrayElementAtIndex(i);string property=pair.FindPropertyRelative("first").stringValue;var tex=pair.FindPropertyRelative("second.m_Texture");if(assigned.TryGetValue(property,out var guid)&&tex.objectReferenceValue==null){string target=AssetDatabase.GUIDToAssetPath(guid);errors.Add(context+": "+path+" "+property+" has unresolved assigned texture GUID="+guid+" path="+(string.IsNullOrEmpty(target)?"<missing>":target));}}
  }
 }
}
