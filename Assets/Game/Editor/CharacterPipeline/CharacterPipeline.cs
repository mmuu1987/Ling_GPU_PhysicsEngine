using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using MassEngine.Editor;
using Object = UnityEngine.Object;
using static MassEngine.Game.Editor.CharacterGeometry;

namespace MassEngine.Game.Editor
{
    [Serializable] public sealed class CharacterPipelineReport
    {
        public string status, output, evidence, sourceModel, sourceGuid, sourceDependencyHash;
        public string unityVersion, createdUtc;
        public float sourceBodyHeight, targetBodyHeight, bodyScaleFactor, agentRadius, maxPositionError, allowedPositionError;
        public int fullVertices, lowVertices, totalFrames, gpuSnapshots, sourceReferenceSnapshots;
        public long positionComparisons, textureBytes;
        public bool automatedPassed, humanAcceptance;
        public string sourcePreviewMethod = "Independent bone/bindpose/weight positions rendered through the same VAT GPU shader; topology/UV/normals shared. NOT a separate native SkinnedMeshRenderer/material acceptance.";
        public List<CharacterFrameCheck> actions = new List<CharacterFrameCheck>();
        public List<string> warnings = new List<string>();
    }
    [Serializable] public sealed class CharacterFrameCheck { public string action; public int frames; public float maximumPositionError; }

    public static class CharacterPipeline
    {
        public const string Root = "Assets/Game/CharacterPipeline";
        public static string Output(CharacterRecipe r) => Root + "/Generated/" + r.outputName;

        /// <summary>Header sniff to reject HTTP-200 error pages before import; Unity remains responsible for full FBX parsing.</summary>
        public static bool IsFbxPayload(byte[] bytes, string contentType = "")
        {
            if (bytes == null || bytes.Length < 1024 || bytes.Length > 32 * 1024 * 1024) return false;
            if ((contentType ?? "").IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string prefix = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            return prefix.StartsWith("Kaydara FBX Binary", StringComparison.Ordinal) ||
                (prefix.TrimStart().StartsWith("; FBX", StringComparison.Ordinal) && prefix.Contains("FBXHeaderExtension"));
        }

        public static CharacterPipelineReport Preflight(CharacterRecipe r)
        {
            Require(r!=null,"Choose a recipe.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Stop Play Mode before preparing assets.");
            Require(Regex.IsMatch(r.outputName ?? "", "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"),"Output name must be 1–64 ASCII letters/digits/_/-, no paths.");
            string output=Output(r);
            Require(!Directory.Exists(output) && !File.Exists(output) && !File.Exists(output+".meta") && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(output)),"Output already exists; choose a NEW version: " + output);
            Require(r.model!=null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(r.model)),"Use a prepared prefab/model asset, not a scene object.");
            CheckHierarchy(r.model,r.sizeRootPath,r.blendshapeAttachmentPaths);var attachments=Attachments(r.model,r);CheckClips(r);
            Require(r.frameRate>=1 && r.frameRate<=60,"Frame rate must be 1–60 for this bounded tool.");
            Require(Finite(r.targetBodyHeight) && r.targetBodyHeight>=.05f && r.targetBodyHeight<=50,"Target body height must be .05–50 metres.");
            Require(Finite(r.agentRadius) && r.agentRadius>=.01f && r.agentRadius<=25,"Specify a finite independent agent radius (.01–25m).");
            Require(Finite(r.maximumPositionError) && r.maximumPositionError>=.00001f && r.maximumPositionError<=.1f,"Position error gate must be .00001–.1 metres.");
            Require(r.fullVertexBudget>=8 && r.fullVertexBudget<=50000 && r.maxTextureMiB>=1 && r.maxTextureMiB<=128,"Invalid bounded vertex/texture budget.");
            Require(r.unitTemplate!=null && r.unitTemplate.renderConfig!=null && r.unitTemplate.flockingConfig!=null,"A valid existing unit template with render/flocking config is required.");
            var templateValidation=UnitTypeBinder.ValidateBinding(r.unitTemplate);
            Require(templateValidation.IsValid,string.Join("\n",templateValidation.Errors));
            var skins=r.model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(skins.Length>0,"At least one skinned body mesh is required; rigid-only props use a different workflow.");
            int vertices=skins.Sum(s=>s.sharedMesh.vertexCount)+attachments.Sum(a=>a.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            Require(vertices<=r.fullVertexBudget,"Full vertex budget exceeded.");
            int frames=r.Clips.Sum(c=>Mathf.Max(1,Mathf.CeilToInt(c.length*r.frameRate)));
            Require(frames<=2048,"Recipe frame budget exceeded (2048).");
            var layout=VatBakeUtility.CalculateLayout(vertices,frames);
            Require(Enum.IsDefined(typeof(CharacterLowLodPolicy),r.lowPolicy),"Unknown Low LOD policy.");
            long bytes=(long)layout.x*layout.y*16;
            if(r.lowPolicy!=CharacterLowLodPolicy.FullOnly)
            {
                Require(r.lowVertexBudget>=8 && r.lowVertexBudget<vertices && r.lowVertexBudget<=16384,"Low budget must be >=8 and smaller than Full (max 16384).");
                bytes+=(long)Mathf.NextPowerOfTwo(r.lowVertexBudget)*frames*16;
            }
            Require(bytes<=r.maxTextureMiB*1048576L,"Full + conservative Low texture budget exceeded.");
            Require(r.paletteColumns>=1 && r.paletteColumns<=32 && r.paletteRows>=1 && r.paletteRows<=32,"Palette grid must be 1–32 on each axis.");
            var body=BoundsOf(Positions(r.model,r,true));Require(body.size.y>.0001f,"Zero body bind height.");
            if(r.createTrial) CharacterPipelineTrial.CheckTemplates(r);
            var report=new CharacterPipelineReport{status="preflight-passed",output=output,sourceModel=AssetDatabase.GetAssetPath(r.model),sourceBodyHeight=body.size.y,
                targetBodyHeight=r.targetBodyHeight,bodyScaleFactor=r.targetBodyHeight/body.size.y,agentRadius=r.agentRadius,fullVertices=vertices,totalFrames=frames,
                allowedPositionError=r.maximumPositionError,textureBytes=bytes,unityVersion=Application.unityVersion,createdUtc=DateTime.UtcNow.ToString("O")};
            report.sourceGuid=AssetDatabase.AssetPathToGUID(report.sourceModel);report.sourceDependencyHash=AssetDatabase.GetAssetDependencyHash(report.sourceModel).ToString();
            report.warnings.Add("Body height is measured on skinned bind geometry, excluding declared rigid attachments. Forward must already be +Z. Grounding is bind-pose only.");
            report.warnings.Add("Visual size does not automatically tune collision, formation, attack reach, camera or large-creature behaviour. Agent radius is copied explicitly into a private config.");
            report.warnings.Add("Human checklist: neck/shoulders, hand-to-weapon contact, feet/ground, all actions, palette/LOD continuity. Automated checks are not human acceptance.");
            if(r.lowPolicy==CharacterLowLodPolicy.PaletteGrid)report.warnings.Add("PaletteGrid is an explicit authoring contract, not a universal texture simplifier. Inspect colour boundaries manually.");
            return report;
        }
        public static CharacterPipelineReport Run(CharacterRecipe r) => RunChecked(r,null);
        internal static CharacterPipelineReport RunChecked(CharacterRecipe r, Action<string> checkpoint)
        {
            var report=Preflight(r);string output=report.output;
            var inputObjects=new Object[]{r.model,r.idle,r.move,r.attack,r.death,r.unitTemplate,r.trialMenu,r.trialBattlefield,r.trialCatalog};
            var inputs=inputObjects.Where(o=>o!=null).Select(AssetDatabase.GetAssetPath).Where(p=>!string.IsNullOrEmpty(p)).Distinct().ToDictionary(p=>p,p=>AssetDatabase.GetAssetDependencyHash(p).ToString());
            EnsureFolder(Root+"/Generated");
            string folderGuid=AssetDatabase.CreateFolder(Root+"/Generated",r.outputName);
            Require(!string.IsNullOrEmpty(folderGuid),"Could not reserve fresh output folder.");
            string evidence="Logs/CharacterPipeline/"+r.outputName+"-"+Guid.NewGuid().ToString("N").Substring(0,8);Directory.CreateDirectory(evidence);report.evidence=evidence;
            GameObject staging=null;VATProfile reference=null;Texture2D referencePositions=null;
            try
            {
                checkpoint?.Invoke("reserved");
                staging=new GameObject("Character Pipeline Inactive Staging"){hideFlags=HideFlags.HideAndDontSave};staging.SetActive(false);
                var source=Instance(r.model,staging);
                var bounds=BoundsOf(Positions(source,r,true));
                float factor=r.targetBodyHeight/bounds.size.y;
                Vector3 ground=r.groundBindFeet?new Vector3(0,-bounds.min.y*factor,0):Vector3.zero;
                var pivot=SizeRoot(source,r.sizeRootPath);float originalScale=pivot==null?1:pivot.localScale.x;
                Vector3 originalOffset=pivot==null?Vector3.zero:pivot.localPosition;
                if(pivot!=null){pivot.localScale=Vector3.one;pivot.localPosition=Vector3.zero;}
                source.name=r.outputName;
                VATProfile full;
                using(var bake=VatBaker.Bake(new VatBakeRequest{model=source,idle=r.idle,move=r.move,attack=r.attack,death=r.death,frameRate=r.frameRate,bakeLowLod=false,extraRenderers=Attachments(source,r)}))
                {
                    Normalize(bake.Profile,originalScale*factor,originalOffset*factor+ground);
                    referencePositions=VerifyAllFrames(r,bake.Profile,staging,factor,ground,report);
                    full=bake.SaveNew(output+"/Full.asset");
                }
                Object.DestroyImmediate(source);
                checkpoint?.Invoke("full-saved");
                VATProfile final=full;
                if(r.lowPolicy==CharacterLowLodPolicy.PaletteGrid)
                    final=CharacterPaletteLod.Create(full,output+"/FinalVAT.asset",r.lowVertexBudget,r.paletteColumns,r.paletteRows,evidence+"/lod.txt");
                else if(r.lowPolicy==CharacterLowLodPolicy.StandardClustering)
                    final=VatLodReducer.CreateFarVariant(full,output+"/FinalVAT.asset",r.lowVertexBudget);
                report.lowVertices=final.HasLowLod?final.lowLodMesh.vertexCount:0;
                report.textureBytes=(long)final.textureWidth*final.textureHeight*16+(final.HasLowLod?(long)final.lowLodTextureWidth*final.lowLodTextureHeight*16:0);
                Require(report.textureBytes<=r.maxTextureMiB*1048576L,"Actual texture budget exceeded.");
                var unit=CloneUnit(r.unitTemplate,output,"Unit");unit.flockingConfig.agentRadius=r.agentRadius;Save(unit.flockingConfig);
                var validation=UnitTypeBinder.Bind(unit,final);Require(validation.IsValid,string.Join("\n",validation.Errors));Save(unit.renderConfig);
                reference=Object.Instantiate(full);reference.name="IndependentSourcePositionReference";reference.positionTexture=referencePositions;
                Capture(r,unit,final,reference,report);
                checkpoint?.Invoke("captured");
                var savedRecipe=Object.Instantiate(r);savedRecipe.name="RecipeSnapshot";AssetDatabase.CreateAsset(savedRecipe,output+"/RecipeSnapshot.asset");
                if(r.createTrial)CharacterPipelineTrial.Create(r,unit,output);
                foreach(var input in inputs)Require(AssetDatabase.GetAssetDependencyHash(input.Key).ToString()==input.Value,"Input changed while preparing; refusing publication: "+input.Key);
                report.status="ready-for-human-review";report.automatedPassed=true;
                File.WriteAllText(evidence+"/report.json",JsonUtility.ToJson(report,true));
                File.WriteAllText(output+"/PipelineReport.json",JsonUtility.ToJson(report,true));
                File.WriteAllText(evidence+"/HumanReview.md","# Pending human review\n\n[ ] Head/neck and shoulders\n[ ] Hands/sword/shield contact\n[ ] Bind foot grounding and animated motion\n[ ] Idle/Move/Attack/Death\n[ ] Palette and near/mid/far forced images\n[ ] Real camera LOD transitions (not proven by forced capture)\n\nHuman acceptance is false until a person records their review.\n");
                AssetDatabase.ImportAsset(output+"/PipelineReport.json");
                Debug.Log("CHARACTER_PIPELINE_READY "+output+" evidence="+evidence);
                return report;
            }
            catch(Exception ex)
            {
                report.status="failed-rolled-back";report.automatedPassed=false;
                // Delete only the fresh folder that this invocation reserved. Never remove an input or another run's output.
                bool owned=AssetDatabase.AssetPathToGUID(output)==folderGuid;
                if(owned)Require(AssetDatabase.DeleteAsset(output),"Owned output rollback failed: "+output);
                File.WriteAllText(evidence+"/failure.json",JsonUtility.ToJson(report,true));File.WriteAllText(evidence+"/failure.txt",ex.ToString()+"\nownedFolderRemoved="+owned);
                throw;
            }
            finally
            {
                if(reference!=null)Object.DestroyImmediate(reference);
                if(referencePositions!=null)Object.DestroyImmediate(referencePositions);
                if(staging!=null)Object.DestroyImmediate(staging);
            }
        }
        internal static void Normalize(VATProfile p,float scale,Vector3 offset)
        {
            var data=p.positionTexture.GetPixels();
            for(int f=0;f<p.totalFrameCount;f++)for(int v=0;v<p.cleanMesh.vertexCount;v++)
            {
                int i=f*p.rowsPerFrame*p.textureWidth+v;var c=data[i];var n=new Vector3(c.r,c.g,c.b)*scale+offset;
                Require(Finite(n) && Mathf.Max(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z))<65504,"Output exceeds finite Half range.");data[i]=new Color(n.x,n.y,n.z,c.a);
            }
            p.positionTexture.SetPixels(data);p.positionTexture.Apply(false,false);
            var old=p.cleanMesh.bounds;p.cleanMesh.vertices=p.cleanMesh.vertices.Select(v=>v*scale+offset).ToArray();
            p.cleanMesh.bounds=new Bounds(old.center*scale+offset,old.size*scale+Vector3.one*.004f);
            Require(VatProfileValidation.TryValidate(p,out string error),error);
        }
        internal static Texture2D VerifyAllFrames(CharacterRecipe r,VATProfile p,GameObject staging,float factor,Vector3 ground,CharacterPipelineReport report)
        {
            var actual=p.positionTexture.GetPixels();var normals=p.normalTexture.GetPixels();var reference=new Color[actual.Length];
            var windows=new[]{p.idle,p.move,p.attack,p.death};
            for(int action=0;action<4;action++)
            {
                var go=Instance(r.model,staging);
                try
                {
                    var window=windows[action];var check=new CharacterFrameCheck{action=window.label,frames=window.frameCount};
                    for(int frame=0;frame<window.frameCount;frame++)
                    {
                        float time=action==3 && window.frameCount>1?r.Clips[action].length*frame/(window.frameCount-1):Mathf.Min(frame/(float)r.frameRate,r.Clips[action].length);
                        r.Clips[action].SampleAnimation(go,time);
                        var expected=Positions(go,r);Require(expected.Length==p.cleanMesh.vertexCount,"Source vertex order/count drift.");
                        for(int v=0;v<expected.Length;v++)
                        {
                            int i=(window.startFrame+frame)*p.rowsPerFrame*p.textureWidth+v;Vector3 e=expected[v]*factor+ground;
                            Color a=actual[i],n=normals[i];Vector3 position=new Vector3(a.r,a.g,a.b);
                            Require(Finite(position) && Finite(new Vector3(n.r,n.g,n.b)),"Nonfinite VAT sample.");
                            Require(p.cleanMesh.bounds.Contains(position),"Animated position outside published bounds.");
                            check.maximumPositionError=Mathf.Max(check.maximumPositionError,Vector3.Distance(e,position));reference[i]=new Color(e.x,e.y,e.z,1);
                        }
                        report.positionComparisons+=expected.Length;
                    }
                    report.actions.Add(check);report.maxPositionError=Mathf.Max(report.maxPositionError,check.maximumPositionError);
                    Require(check.maximumPositionError<=r.maximumPositionError,"Independent geometry gate failed: "+check.action+" error="+check.maximumPositionError);
                }
                finally{Object.DestroyImmediate(go);}
            }
            var t=new Texture2D(p.textureWidth,p.textureHeight,TextureFormat.RGBAFloat,false,true){name="Independent linear skin positions",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            t.SetPixels(reference);t.Apply(false,false);return t;
        }
        private static void Capture(CharacterRecipe r,UnitTypeConfig unit,VATProfile final,VATProfile reference,CharacterPipelineReport report)
        {
            var refUnit=Object.Instantiate(unit);refUnit.renderConfig=Object.Instantiate(unit.renderConfig);refUnit.renderConfig.vatProfile=reference;
            refUnit.renderConfig.nearMesh=reference.cleanMesh;
            try
            {
                var runtime=ResolvedUnitTypeRuntime.Resolve(unit,1);var expected=ResolvedUnitTypeRuntime.Resolve(refUnit,1);
                using(var capture=new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(final),384))
                    foreach(var state in new[]{AgentState.Idle,AgentState.Move,AgentState.Attack,AgentState.Dead})
                    {
                        var clip=VatAppearanceRegression.GetClip(final,state);var frames=VatAppearanceRegression.SampleLocalFrames(clip).ToArray();
                        for(int lod=0;lod<3;lod++)
                        {
                            var images=new List<Color32[]>();
                            for(int sample=0;sample<frames.Length;sample++)
                            {
                                float time=(frames[sample]+.25f)/clip.frameRate;
                                var pixels=capture.Capture(runtime,lod,state,time);CheckImage(pixels);images.Add(pixels);
                                WriteImage(report.evidence+"/lod"+lod+"-"+state+"-"+sample+".png",capture.Size,pixels);report.gpuSnapshots++;
                                if(lod==0){var source=capture.Capture(expected,0,state,time);CheckImage(source);WriteImage(report.evidence+"/source-reference-"+state+"-"+sample+".png",capture.Size,source);report.sourceReferenceSnapshots++;}
                            }
                            Require(VatAppearanceRegressionImageChecks.HasMotion(images,out int changed),"No detectable motion in "+state+" LOD"+lod);
                        }
                    }
            }
            finally{Object.DestroyImmediate(refUnit.renderConfig);Object.DestroyImmediate(refUnit);}
        }
        private static void CheckImage(Color32[] pixels) => Require(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels),out string error),error);
        private static void WriteImage(string path,int size,Color32[] pixels)
        {var t=new Texture2D(size,size,TextureFormat.RGBA32,false);try{t.SetPixels32(pixels);t.Apply(false,false);File.WriteAllBytes(path,t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}}
        internal static UnitTypeConfig CloneUnit(UnitTypeConfig source,string folder,string name)
        {
            var u=Clone(source,folder+"/"+name+".asset");
            u.spawnConfig=Clone(source.spawnConfig,folder+"/"+name+"_Spawn.asset");u.renderConfig=Clone(source.renderConfig,folder+"/"+name+"_Render.asset");
            u.movementConfig=Clone(source.movementConfig,folder+"/"+name+"_Movement.asset");u.flockingConfig=Clone(source.flockingConfig,folder+"/"+name+"_Flocking.asset");
            u.animationConfig=Clone(source.animationConfig,folder+"/"+name+"_Animation.asset");u.combatConfig=Clone(source.combatConfig,folder+"/"+name+"_Combat.asset");Save(u);return u;
        }
        internal static T Clone<T>(T source,string path) where T:ScriptableObject
        {if(source==null)return null;VatBakeResult.ValidateNewPath(path);var c=Object.Instantiate(source);c.name=Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(c,path);return c;}
        internal static void Save(Object o){EditorUtility.SetDirty(o);AssetDatabase.SaveAssetIfDirty(o);}
        internal static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))),"Cannot create folder: "+path);}
    }
}
