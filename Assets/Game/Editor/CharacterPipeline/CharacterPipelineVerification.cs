using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MassEngine.Editor;
using Object=UnityEngine.Object;
using static MassEngine.Game.Editor.CharacterGeometry;

namespace MassEngine.Game.Editor
{
    /// <summary>Bounded batch reproduction using exactly the same core as the editor window. Not an NUnit suite.</summary>
    public static class CharacterPipelineVerification
    {
        const string Prior="Assets/Game/CharacterPilotPlayable";
        const string RecipePath=CharacterPipeline.Root+"/Recipes/Knight.asset";
        const string Log="Logs/AgentCharacterPipeline";
        [Serializable] class Checks {public bool passed;public List<string> checks=new List<string>();public string output,evidence;public float versus03MaximumPositionDifference;public bool humanAcceptance=false;}
        private static void Pass(Checks c,string s){c.checks.Add(s);Debug.Log("CHARACTER_PIPELINE_CHECK "+s);}
        private static void Reject(Checks c,string name,Action action,string expected)
        {
            bool rejected=false;
            try{action();}catch(InvalidOperationException ex){rejected=ex.Message.Contains(expected);if(!rejected)throw;}
            Require(rejected,"Expected rejection did not occur: "+name);Pass(c,name);
        }
        public static void Prepare04()
        {
            Directory.CreateDirectory(Log);
            Require(!Directory.Exists(CharacterPipeline.Root+"/Generated/Knight04"),"Fresh Knight04 required.");
            CharacterPipeline.EnsureFolder(CharacterPipeline.Root+"/Recipes");
            var r=AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
            if(r==null)
            {
                r=ScriptableObject.CreateInstance<CharacterRecipe>();r.name="Knight";r.outputName="Knight04";
                r.model=AssetDatabase.LoadAssetAtPath<GameObject>(Prior+"/Prepared/KayKitKnightPrepared.prefab");r.sizeRootPath="ScalePivot";
                r.attachmentPaths=r.model.GetComponentsInChildren<MeshRenderer>(true).Select(m=>AnimationUtility.CalculateTransformPath(m.transform,r.model.transform)).ToArray();
                r.idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(Prior+"/Prepared/Idle.anim");r.move=AssetDatabase.LoadAssetAtPath<AnimationClip>(Prior+"/Prepared/Move.anim");
                r.attack=AssetDatabase.LoadAssetAtPath<AnimationClip>(Prior+"/Prepared/Attack.anim");r.death=AssetDatabase.LoadAssetAtPath<AnimationClip>(Prior+"/Prepared/Death.anim");
                r.unitTemplate=AssetDatabase.LoadAssetAtPath<ScenarioConfig>(Prior+"/Settings/TrialScenario.asset").unitTypes[0];r.agentRadius=r.unitTemplate.flockingConfig.agentRadius;
                r.trialMenu=AssetDatabase.LoadAssetAtPath<SceneAsset>(Prior+"/ModelTrialMenu.unity");r.trialBattlefield=AssetDatabase.LoadAssetAtPath<SceneAsset>(Prior+"/ModelTrialBattlefield.unity");
                r.trialCatalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Prior+"/Settings/TrialCatalog.asset");r.createTrial=true;
                r.battlefieldId="character-pipeline-knight";r.templateId="pipeline-knight";AssetDatabase.CreateAsset(r,RecipePath);
            }
            var c=new Checks();CharacterPipeline.Preflight(r);Pass(c,"Current Knight recipe preflight");
            var test=Object.Instantiate(r);var inactive=new GameObject("Inactive validation fixtures");inactive.SetActive(false);AnimationClip clip=null;
            try
            {
                test.targetBodyHeight=3.6f;var larger=CharacterPipeline.Preflight(test);Require(Mathf.Abs(larger.bodyScaleFactor-2)<.001f && larger.agentRadius==r.agentRadius,"Size and radius were coupled.");Pass(c,"3.6m target preflight keeps independent radius (not a large-creature runtime test)");test.targetBodyHeight=r.targetBodyHeight;
                test.move=null;Reject(c,"Missing Move rejected",()=>CharacterPipeline.Preflight(test),"Four nonempty");test.move=r.move;
                test.attachmentPaths=Array.Empty<string>();Reject(c,"Unlisted weapon attachment rejected",()=>CharacterPipeline.Preflight(test),"not explicitly declared");test.attachmentPaths=r.attachmentPaths;
                test.outputName="../Unsafe";Reject(c,"Path traversal rejected",()=>CharacterPipeline.Preflight(test),"Output name");test.outputName="Knight04";
                test.fullVertexBudget=8;Reject(c,"Vertex budget rejected before bake",()=>CharacterPipeline.Preflight(test),"Full vertex budget");test.fullVertexBudget=r.fullVertexBudget;
                test.maxTextureMiB=1;Reject(c,"Texture budget rejected before bake",()=>CharacterPipeline.Preflight(test),"texture budget");test.maxTextureMiB=r.maxTextureMiB;
                test.frameRate=61;Reject(c,"Out-of-range frame rate rejected",()=>CharacterPipeline.Preflight(test),"Frame rate");test.frameRate=r.frameRate;
                clip=new AnimationClip();clip.SetCurve("NotPresent",typeof(Transform),"m_LocalPosition.x",AnimationCurve.Linear(0,0,1,1));test.move=clip;
                Reject(c,"Unmapped animation binding rejected",()=>CharacterPipeline.Preflight(test),"Unmapped");Object.DestroyImmediate(clip);clip=null;test.move=r.move;
                clip=new AnimationClip();string bone=AnimationUtility.GetCurveBindings(r.move)[0].path;clip.SetCurve(bone,typeof(Transform),"m_LocalScale.x",AnimationCurve.Linear(0,1,1,2));test.move=clip;
                Reject(c,"Animated scale rejected",()=>CharacterPipeline.Preflight(test),"Animated scale");Object.DestroyImmediate(clip);clip=null;test.move=r.move;
                var model=Instance(r.model,inactive);var pivot=SizeRoot(model,r.sizeRootPath);Vector3 original=pivot.localScale;
                pivot.localScale=new Vector3(1,2,1);Reject(c,"Nonuniform hierarchy scale rejected",()=>CheckHierarchy(model,r.sizeRootPath),"Nonuniform");
                pivot.localScale=-Vector3.one;Reject(c,"Negative hierarchy scale rejected",()=>CheckHierarchy(model,r.sizeRootPath),"negative");pivot.localScale=original;
                Reject(c,"Nested/nonexistent size root rejected",()=>CheckHierarchy(model,"not-a-direct-child"),"DIRECT child");
                var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];var mesh=Object.Instantiate(skin.sharedMesh);skin.sharedMesh=mesh;
                mesh.AddBlendShapeFrame("Unsupported",100,new Vector3[mesh.vertexCount],null,null);
                Reject(c,"Blendshape rejected rather than silently misvalidated",()=>CheckHierarchy(model,r.sizeRootPath),"Blendshapes");Object.DestroyImmediate(mesh);Object.DestroyImmediate(model);
                test.outputName="RollbackProbe04";test.createTrial=false;
                Reject(c,"Forced failure after persisted Full rolls back owned output",()=>CharacterPipeline.RunChecked(test,stage=>{if(stage=="full-saved")throw new InvalidOperationException("Injected rollback check");}),"Injected rollback check");
                Require(!Directory.Exists(CharacterPipeline.Output(test)) && !File.Exists(CharacterPipeline.Output(test)+".meta"),"Partial output survived rollback.");
                Pass(c,"No asset folder/meta left by forced rollback");
            }
            finally{if(clip!=null)Object.DestroyImmediate(clip);Object.DestroyImmediate(test);Object.DestroyImmediate(inactive);}
            var result=CharacterPipeline.Run(r);c.output=result.output;c.evidence=result.evidence;
            Pass(c,"Recipe core completed 114-frame geometry, GPU capture and independent trial creation");
            VerifyOutput(result,c);
        }
        public static void VerifyExisting04()
        {
            var report=JsonUtility.FromJson<CharacterPipelineReport>(File.ReadAllText(CharacterPipeline.Root+"/Generated/Knight04/PipelineReport.json"));
            Require(report.automatedPassed,"No completed recipe-core output to resume verifying.");
            // Preserve the first failed harness run. Its independently logged preflight/rollback checks are evidence,
            // not a claim that the bad literal-count assertion passed. This method does not modify generated assets.
            var c=new Checks{output=report.output,evidence=report.evidence};
            foreach(var line in File.ReadAllLines(Log+"/prepare-04.log"))
                if(line.StartsWith("CHARACTER_PIPELINE_CHECK "))c.checks.Add(line.Substring("CHARACTER_PIPELINE_CHECK ".Length));
            Require(c.checks.Contains("No asset folder/meta left by forced rollback"),"Missing original rollback evidence.");
            Pass(c,"First harness assertion corrected: Low is bounded by budget, not a literal1337 vertex count; old failed log retained");
            VerifyOutput(report,c);
        }
        private static void VerifyOutput(CharacterPipelineReport result,Checks c)
        {
            // Scene switches can unload Editor wrappers: reload all persistent references.
            var r=AssetDatabase.LoadAssetAtPath<CharacterRecipe>(RecipePath);
            Reject(c,"Existing successful output is never overwritten",()=>CharacterPipeline.Preflight(r),"Output already exists");
            var final=(VATProfile)AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(result.output+"/Unit.asset").renderConfig.vatProfile;
            var old=AssetDatabase.LoadAssetAtPath<VATProfile>(Prior+"/NeckFix03/KnightNeckSafeVAT.asset");
            Require(final.cleanMesh.vertexCount==6666 && final.lowLodMesh.vertexCount>=8 && final.lowLodMesh.vertexCount<=r.lowVertexBudget && final.totalFrameCount==114,"Unexpected Knight reproduction metrics.");
            Require(final.cleanMesh.triangles.SequenceEqual(old.cleanMesh.triangles) && final.cleanMesh.uv.SequenceEqual(old.cleanMesh.uv),"Full topology/UV drift.");
            var a=old.positionTexture.GetPixels();var b=final.positionTexture.GetPixels();Require(a.Length==b.Length,"Layout changed.");
            for(int f=0;f<final.totalFrameCount;f++)for(int v=0;v<6666;v++){int i=f*final.rowsPerFrame*final.textureWidth+v;c.versus03MaximumPositionDifference=Mathf.Max(c.versus03MaximumPositionDifference,Vector3.Distance(new Vector3(a[i].r,a[i].g,a[i].b),new Vector3(b[i].r,b[i].g,b[i].b)));}
            Require(c.versus03MaximumPositionDifference<=.0025f,"Reproduction differs from accepted03 beyond quantization gate.");Pass(c,"04 topology/UV and all-frame positions reproduce03 within tolerance");
            var damaged=Object.Instantiate(final);damaged.positionTexture=Object.Instantiate(final.positionTexture);damaged.cleanMesh=Object.Instantiate(final.cleanMesh);damaged.cleanMesh.bounds=new Bounds(final.cleanMesh.bounds.center,final.cleanMesh.bounds.size+Vector3.one);var pixels=damaged.positionTexture.GetPixels();pixels[0].r+=.1f;damaged.positionTexture.SetPixels(pixels);damaged.positionTexture.Apply(false,false);
            var inactive=new GameObject("Inactive corruption fixture");inactive.SetActive(false);
            try
            {
                var bounds=BoundsOf(Positions(r.model,r,true));float scale=r.targetBodyHeight/bounds.size.y;Vector3 ground=r.groundBindFeet?new Vector3(0,-bounds.min.y*scale,0):Vector3.zero;
                Reject(c,"100mm position corruption trips independent geometry gate",()=>{var t=CharacterPipeline.VerifyAllFrames(r,damaged,inactive,scale,ground,new CharacterPipelineReport());Object.DestroyImmediate(t);},"Independent geometry gate");
            }
            finally{Object.DestroyImmediate(damaged.positionTexture);Object.DestroyImmediate(damaged.cleanMesh);Object.DestroyImmediate(damaged);Object.DestroyImmediate(inactive);}
            c.passed=true;File.WriteAllText(Log+"/verification-04.json",JsonUtility.ToJson(c,true));File.WriteAllText(Log+"/recipe-report-04.json",JsonUtility.ToJson(result,true));
            Debug.Log("CHARACTER_PIPELINE_REPRODUCTION_READY");
        }
        public static void Build04() => CharacterPipelineTrial.Build(CharacterPipeline.Root+"/Generated/Knight04","Builds/CharacterPipeline-Knight-20260930-04");
    }
}
