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
    const int n=128;var pixels=new Color32[n*n];float low=float.PositiveInfinity,high=float.NegativeInfinity;
    for(int z=0;z<surface.Depth;z++)for(int x=0;x<surface.Width;x++){float h=surface.HeightAtVertex(x,z);low=Mathf.Min(low,h);high=Mathf.Max(high,h);}
    for(int z=0;z<n;z++)for(int x=0;x<n;x++){
     var p=surface.Origin+new Vector2((x+.5f)*surface.Size.x/n,(z+.5f)*surface.Size.y/n);
     surface.TrySample(p,out var sample);bool walkable=navigation.IsWalkable(p);
     var c=walkable?Color.Lerp(new Color(.32f,.49f,.24f),new Color(.78f,.75f,.45f),Mathf.InverseLerp(low,high,sample.Position.y)):new Color(.24f,.29f,.29f);
     if(walkable&&Mathf.Repeat(sample.Position.y,5)<.45f)c*=.87f;
     pixels[z*n+x]=c;
    }
    terrainMapTexture=new Texture2D(n,n,TextureFormat.RGBA32,false){name="Terrain deployment height and exclusion",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
    terrainMapTexture.SetPixels32(pixels);terrainMapTexture.Apply(false,false);
   }
   ui.PictureFit("deployment-terrain-map",map,terrainMapTexture);
  }
 }
}
