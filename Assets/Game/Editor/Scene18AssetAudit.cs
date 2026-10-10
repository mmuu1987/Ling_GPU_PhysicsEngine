using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor {
 [InitializeOnLoad] public static class Scene18AssetAudit {
  public const string Logs="Logs/Scene18-20261004";
  static readonly string[] Roots={"Assets/ThirdParty/Supercyan Free Forest Sample","Assets/ThirdParty/Polytope Studio","Assets/ThirdParty/Pure Poly","Assets/ThirdParty/TriForge Assets"};
  static Scene18AssetAudit(){EditorApplication.update+=Tick;}
  static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||AssetDatabase.IsAssetImportWorkerProcess())return;string req=Logs+"/request.txt";if(!File.Exists(req))return;string action=File.ReadAllText(req).Trim();if(action!="Inventory"&&typeof(Scene18AssetAudit).Assembly.GetType("MassEngine.Game.Editor.Scene18Builder")==null)return;File.Delete(req);try{if(action=="Inventory")Inventory();else {var type=typeof(Scene18AssetAudit).Assembly.GetType("MassEngine.Game.Editor.Scene18Builder");if(type==null||!new[]{"Prepare","Build","Capture"}.Contains(action))throw new Exception("Unknown scene18 action");type.GetMethod(action).Invoke(null,null);}File.WriteAllText(Logs+"/"+action+"-complete.json","{\"passed\":true}");}catch(Exception e){File.WriteAllText(Logs+"/"+action+"-error.txt",e.ToString());Debug.LogException(e);}}
  [Serializable] public class MeshRow{public string renderer,asset,mesh;public long triangles;public string[] materials,shaders;}
  [Serializable] public class PrefabRow{public string path;public long allLodTriangles;public int lodGroups;public MeshRow[] meshes;}
  [Serializable] public class TextureRow{public string path;public int sourceWidth,sourceHeight,width,height,maxSize;public string compression;}
  [Serializable] public class Report{public string unity,pipeline;public string[] scenes,dirtyScenes;public PrefabRow[] prefabs;public TextureRow[] textures;}
  public static long Triangles(Mesh mesh){long t=0;if(mesh==null)return 0;for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)==MeshTopology.Triangles)t+=(long)mesh.GetIndexCount(i)/3;return t;}
  public static void Inventory(){Directory.CreateDirectory(Logs);var prefabs=new List<PrefabRow>();var textures=new List<TextureRow>();
   foreach(string root in Roots){if(!AssetDatabase.IsValidFolder(root))continue;foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{root})){var path=AssetDatabase.GUIDToAssetPath(guid);var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(go==null)continue;var rows=new List<MeshRow>();foreach(var f in go.GetComponentsInChildren<MeshFilter>(true)){var rr=f.GetComponent<Renderer>();if(rr==null||f.sharedMesh==null)continue;rows.Add(new MeshRow{renderer=f.name,asset=AssetDatabase.GetAssetPath(f.sharedMesh),mesh=f.sharedMesh.name,triangles=Triangles(f.sharedMesh),materials=rr.sharedMaterials.Select(m=>m==null?"null":AssetDatabase.GetAssetPath(m)).ToArray(),shaders=rr.sharedMaterials.Select(m=>m==null?"null":m.shader.name).ToArray()});}prefabs.Add(new PrefabRow{path=path,allLodTriangles=rows.Sum(x=>x.triangles),lodGroups=go.GetComponentsInChildren<LODGroup>(true).Length,meshes=rows.ToArray()});}
    foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{root})){string path=AssetDatabase.GUIDToAssetPath(guid);var importer=AssetImporter.GetAtPath(path) as TextureImporter;var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(importer==null||t==null)continue;importer.GetSourceTextureWidthAndHeight(out int w,out int h);textures.Add(new TextureRow{path=path,sourceWidth=w,sourceHeight=h,width=t.width,height=t.height,maxSize=importer.maxTextureSize,compression=importer.textureCompression.ToString()});}}
   var scenes=new List<string>();var dirty=new List<string>();for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);scenes.Add(s.path);if(s.isDirty)dirty.Add(s.path);}
   var report=new Report{unity=Application.unityVersion,pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline==null?"Built-in":UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name,scenes=scenes.ToArray(),dirtyScenes=dirty.ToArray(),prefabs=prefabs.ToArray(),textures=textures.ToArray()};File.WriteAllText(Logs+"/asset-audit.json",JsonUtility.ToJson(report,true));Debug.Log("SCENE18_ASSET_AUDIT_COMPLETE "+prefabs.Count+" prefabs");
  }
 }
}
