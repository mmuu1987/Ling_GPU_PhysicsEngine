using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxDeploymentHUD {
  private Texture2D terrainMapTexture;
  private TerrainNavigationGrid terrainMapNavigation;
  private void ReleaseTerrainMap(){if(terrainMapTexture!=null)Destroy(terrainMapTexture);terrainMapTexture=null;terrainMapNavigation=null;}
  private void DrawTerrainMap(WarSandboxUGUI ui,Rect map){
   var manager=deployment.controller.manager;
   if(!manager.TryGetTerrainContext(out var surface,out var navigation,out _)||surface==null||navigation==null){ReleaseTerrainMap();return;}
   if(terrainMapTexture==null||terrainMapNavigation!=navigation){
    ReleaseTerrainMap();terrainMapNavigation=navigation;
    const int n=128;var pixels=new Color32[n*n];
    for(int z=0;z<n;z++)for(int x=0;x<n;x++){
     var p=surface.Origin+new Vector2((x+.5f)*surface.Size.x/n,(z+.5f)*surface.Size.y/n);
     bool walkable=navigation.IsWalkable(p);
     // 2026-10-10 user feedback: plain green background; exclusion stays visible for placement.
     var c=walkable?new Color(.42f,.60f,.31f):new Color(.24f,.29f,.29f);
     pixels[z*n+x]=c;
    }
    terrainMapTexture=new Texture2D(n,n,TextureFormat.RGBA32,false){name="Terrain deployment exclusion",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
    terrainMapTexture.SetPixels32(pixels);terrainMapTexture.Apply(false,false);
   }
   ui.PictureFit("deployment-terrain-map",map,terrainMapTexture);
  }
 }
}
