using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>Replace an empty editor fixture only with the real formal player render; retain both pieces of evidence.</summary>
    public static class RobotExpressiveOverviewFix
    {
        public const string Path02=RobotExpressiveAdmissionBuilder.Root+"/Previews/robot-expressive-player.png";
        public static void AttachActualPlayer08()
        {
            const string receipt="Logs/AgentRobotFormal20261002/robot-seed-01/receipt.json",source="Logs/AgentRobotFormal20261002/robot-seed-01/report/setup.png";
            Require(File.Exists(receipt)&&File.ReadAllText(receipt).Contains("\"passed\": true"),"Formal player smoke must succeed");Require(!File.Exists(Path02)&&File.Exists(source),"Fresh actual overview required");
            var t=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                Require(t.LoadImage(File.ReadAllBytes(source))&&t.width==1280&&t.height==720,"Actual player PNG dimensions");var pixels=t.GetPixels32();int orange=0;
                for(int y=180;y<600;y++)for(int x=330;x<950;x++){var c=pixels[y*1280+x];if(c.r>80&&c.g>40&&c.r>c.b*1.35f&&c.g>c.b*1.1f)orange++;}
                Require(orange>50,"World view has no rendered robot foreground; do not publish a blank GUI fixture");
                File.Copy(source,Path02);AssetDatabase.ImportAsset(Path02,ImportAssetOptions.ForceSynchronousImport);var i=(TextureImporter)AssetImporter.GetAtPath(Path02);i.mipmapEnabled=false;i.maxTextureSize=1024;i.npotScale=TextureImporterNPOTScale.None;i.textureCompression=TextureImporterCompression.Uncompressed;i.SaveAndReimport();
                var catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(RobotExpressiveAdmissionBuilder.CatalogPath);catalog.entries.Single(e=>e.id==RobotExpressiveAdmissionBuilder.BattlefieldId).preview=AssetDatabase.LoadAssetAtPath<Texture2D>(Path02);CharacterPipeline.Save(catalog);
                string output=Environment.GetEnvironmentVariable("ROBOT_FORMAL_OUTPUT");Require(!string.IsNullOrWhiteSpace(output),"Protected runner required");
                using(var f=new FileStream(System.IO.Path.Combine(output,"actual-overview-receipt.json"),FileMode.CreateNew))using(var w=new StreamWriter(f,new UTF8Encoding(false)))w.Write("{\"passed\":true,\"source\":\""+source+"\",\"actualPlayerWorld\":true,\"robotWorldForegroundPixels\":"+orange+",\"rejectedEmptyEditorWorldFixture\":true}");
                Debug.Log("ROBOT_FORMAL_ACTUAL_OVERVIEW_READY");
            }
            finally{UnityEngine.Object.DestroyImmediate(t);}
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
