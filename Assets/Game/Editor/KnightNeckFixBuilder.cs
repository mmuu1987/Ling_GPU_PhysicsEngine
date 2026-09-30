using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MassEngine.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class KnightNeckFixBuilder
    {
        const string Root="Assets/Game/CharacterPilotPlayable";
        const string Dir=Root+"/NeckFix03";
        const string Log="Logs/AgentKnightNeck";
        [Serializable] class Measurement { public string action; public int frames,verticesPerFrame; public float oldMaximumError,newMaximumError; }
        [Serializable] class Report { public string correction="Unit-scale production bake, then one uniform scale/translation of the complete Full VAT and clean mesh. No head offsets or added neck geometry."; public List<Measurement> rows=new List<Measurement>(); public int fullVertices,lowVertices,totalFrames; public bool humanAcceptance=false; }
        public static void Prepare()
        {
            if(AssetDatabase.IsValidFolder(Dir)||Directory.Exists(Dir))throw new BuildFailedException("Fresh NeckFix03 required.");
            AssetDatabase.CreateFolder(Root,"NeckFix03");
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prepared/KayKitKnightPrepared.prefab");
            var instance=Object.Instantiate(original);instance.name="KnightCanonical03";
            var old=AssetDatabase.LoadAssetAtPath<VATProfile>(Root+"/KnightPaletteSafeVAT.asset");
            var clips=new[]{"Idle","Move","Attack","Death"}.Select(n=>AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Prepared/"+n+".anim")).ToArray();
            VATProfile full;
            try
            {
                Transform pivot=instance.transform.Find("ScalePivot");
                Vector3 scale=pivot.localScale,offset=pivot.localPosition;
                if(scale.x<=0||Mathf.Abs(scale.x-scale.y)>1e-6||Mathf.Abs(scale.x-scale.z)>1e-6)throw new BuildFailedException("This fixed recipe requires positive uniform scale.");
                pivot.localScale=Vector3.one;pivot.localPosition=Vector3.zero;
                using(var bake=VatBaker.Bake(new VatBakeRequest{model=instance,idle=clips[0],move=clips[1],attack=clips[2],death=clips[3],frameRate=30,bakeLowLod=false,extraRenderers=instance.GetComponentsInChildren<MeshRenderer>(true)}))
                {
                    var profile=bake.Profile;
                    var pixels=profile.positionTexture.GetPixels();
                    for(int frame=0;frame<profile.totalFrameCount;frame++)
                        for(int v=0;v<profile.cleanMesh.vertexCount;v++)
                        {
                            int index=frame*profile.rowsPerFrame*profile.textureWidth+v;Color p=pixels[index];
                            pixels[index]=new Color(p.r*scale.x+offset.x,p.g*scale.x+offset.y,p.b*scale.x+offset.z,p.a);
                        }
                    profile.positionTexture.SetPixels(pixels);profile.positionTexture.Apply(false,false);
                    var mesh=profile.cleanMesh;var bounds=mesh.bounds;
                    mesh.vertices=mesh.vertices.Select(v=>v*scale.x+offset).ToArray();
                    mesh.bounds=new Bounds(bounds.center*scale.x+offset,bounds.size*scale.x);
                    // Uniform positive scale preserves normals and UVs. Low is built only AFTER final geometry normalization.
                    full=bake.SaveNew(Dir+"/KnightCanonicalFull.asset");
                }
            }
            finally{Object.DestroyImmediate(instance);}
            Report report=ValidateEveryFrame(original,clips,old,full);
            var corrected=KnightAtlasLodBuilder.Create(full,Dir+"/KnightNeckSafeVAT.asset",1400,Log+"/atlas-lod.txt");
            var scenario=AssetDatabase.LoadAssetAtPath<ScenarioConfig>(Root+"/Settings/TrialScenario.asset");
            var unit=scenario.unitTypes[0];
            var before=Object.Instantiate(unit);before.renderConfig=Object.Instantiate(unit.renderConfig);
            try
            {
                var validation=UnitTypeBinder.Bind(unit,corrected);
                if(!validation.IsValid)throw new BuildFailedException(string.Join("\n",validation.Errors));
                Save(unit.renderConfig);
                Capture(unit,corrected);
                CaptureComparison(before,old,unit,corrected);
            }
            finally{Object.DestroyImmediate(before.renderConfig);Object.DestroyImmediate(before);}
            var catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Settings/TrialCatalog.asset");
            catalog.templates[0].revision=3;Save(catalog);
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene=EditorSceneManager.OpenScene(Root+"/ModelTrialMenu.unity",OpenSceneMode.Single);
                Object.FindFirstObjectByType<WarSandboxModelTrialSmoke>().templateRevision=3;
                EditorSceneManager.SaveScene(scene);
            }
            finally{if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
            report.fullVertices=corrected.cleanMesh.vertexCount;report.lowVertices=corrected.lowLodMesh.vertexCount;report.totalFrames=corrected.totalFrameCount;
            File.WriteAllText(Log+"/fix-report.json",JsonUtility.ToJson(report,true));
            Debug.Log("KNIGHT_NECK_FIX_READY");
        }
        static Report ValidateEveryFrame(GameObject prefab,AnimationClip[] clips,VATProfile old,VATProfile fixedProfile)
        {
            var report=new Report();var windows=new[]{fixedProfile.idle,fixedProfile.move,fixedProfile.attack,fixedProfile.death};
            Color[] oldPixels=old.positionTexture.GetPixels(),newPixels=fixedProfile.positionTexture.GetPixels();
            for(int action=0;action<4;action++)
            {
                var go=Object.Instantiate(prefab);
                try
                {
                    foreach(var a in go.GetComponentsInChildren<Animator>(true))a.enabled=false;
                    var measurement=new Measurement{action=clips[action].name,frames=windows[action].frameCount,verticesPerFrame=fixedProfile.cleanMesh.vertexCount};
                    for(int frame=0;frame<windows[action].frameCount;frame++)
                    {
                        float time=action==3?clips[action].length*frame/(windows[action].frameCount-1):Mathf.Min(frame/30f,clips[action].length);
                        clips[action].SampleAnimation(go,time);
                        var positions=new List<Vector3>();
                        foreach(var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))positions.AddRange(KnightNeckProbe.LinearVertices(go.transform,skin));
                        foreach(var rigid in go.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var m=go.transform.worldToLocalMatrix*rigid.transform.localToWorldMatrix;
                            positions.AddRange(rigid.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v=>m.MultiplyPoint3x4(v)));
                        }
                        if(positions.Count!=fixedProfile.cleanMesh.vertexCount)throw new BuildFailedException("Source vertex order/count changed.");
                        int global=windows[action].startFrame+frame;
                        for(int v=0;v<positions.Count;v++)
                        {
                            Color a=oldPixels[global*old.rowsPerFrame*old.textureWidth+v],b=newPixels[global*fixedProfile.rowsPerFrame*fixedProfile.textureWidth+v];
                            measurement.oldMaximumError=Mathf.Max(measurement.oldMaximumError,Vector3.Distance(positions[v],new Vector3(a.r,a.g,a.b)));
                            measurement.newMaximumError=Mathf.Max(measurement.newMaximumError,Vector3.Distance(positions[v],new Vector3(b.r,b.g,b.b)));
                        }
                    }
                    report.rows.Add(measurement);
                    if(measurement.newMaximumError>.0025f)throw new BuildFailedException("Independent linear-skin reference mismatch: "+JsonUtility.ToJson(measurement));
                }
                finally{Object.DestroyImmediate(go);}
            }
            return report;
        }
        static void Capture(UnitTypeConfig unit,VATProfile profile)
        {
            string output=Log+"/appearance";Directory.CreateDirectory(output);
            var runtime=ResolvedUnitTypeRuntime.Resolve(unit,1f);var checks=new List<string>();
            using(var capture=new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(profile),384))
                for(int lod=0;lod<3;lod++)foreach(AgentState state in new[]{AgentState.Idle,AgentState.Move,AgentState.Attack,AgentState.Dead})
                {
                    var clip=VatAppearanceRegression.GetClip(profile,state);var images=new List<Color32[]>();int sample=0;
                    foreach(int frame in VatAppearanceRegression.SampleLocalFrames(clip))
                    {
                        var pixels=capture.Capture(runtime,lod,state,(frame+.25f)/clip.frameRate);
                        if(!VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels),out string error))throw new BuildFailedException(error);
                        images.Add(pixels);WriteImage(output+"/lod"+lod+"-"+state+"-"+sample++ +".png",capture.Size,pixels);
                    }
                    if(!VatAppearanceRegressionImageChecks.HasMotion(images,out int changed))throw new BuildFailedException("Frozen "+state);
                    checks.Add(lod+"/"+state+": changed pixels="+changed);
                }
            File.WriteAllText(output+"/checks.txt",string.Join("\n",checks));
        }
        static void CaptureComparison(UnitTypeConfig before,VATProfile old,UnitTypeConfig after,VATProfile current)
        {
            var bounds=VatAppearanceRegression.CombinedBounds(old);bounds.Encapsulate(VatAppearanceRegression.CombinedBounds(current));
            using(var capture=new VatAppearanceRegressionCapture(bounds,512))
                foreach(var state in new[]{AgentState.Idle,AgentState.Attack,AgentState.Dead})
                {
                    var clip=VatAppearanceRegression.GetClip(current,state);int frame=state==AgentState.Dead?clip.frameCount-1:clip.frameCount/3;
                    WriteImage(Log+"/before-"+state+".png",512,capture.Capture(ResolvedUnitTypeRuntime.Resolve(before,1),0,state,(frame+.25f)/30));
                    WriteImage(Log+"/after-"+state+".png",512,capture.Capture(ResolvedUnitTypeRuntime.Resolve(after,1),0,state,(frame+.25f)/30));
                }
        }
        static void WriteImage(string path,int size,Color32[] pixels)
        {
            var t=new Texture2D(size,size,TextureFormat.RGBA32,false);
            try{t.SetPixels32(pixels);t.Apply(false,false);File.WriteAllBytes(path,t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}
        }
        static void Save(Object o){EditorUtility.SetDirty(o);AssetDatabase.SaveAssetIfDirty(o);}
        public static void Build()
        {
            var scenario=AssetDatabase.LoadAssetAtPath<ScenarioConfig>(Root+"/Settings/TrialScenario.asset");
            var unit=scenario.unitTypes[0];
            if(AssetDatabase.GetAssetPath(unit.renderConfig.vatProfile)!=Dir+"/KnightNeckSafeVAT.asset")throw new BuildFailedException("Wrong final profile.");
            var validation=UnitTypeBinder.ValidateBinding(unit);if(!validation.IsValid)throw new BuildFailedException(string.Join("\n",validation.Errors));
            var catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Settings/TrialCatalog.asset");
            if(catalog.templates[0].revision!=3||scenario.unitTypes.Any(u=>u.combatConfig.projectileRange!=0))throw new BuildFailedException("Wrong revision/melee parameters.");
            string output="Builds/CharacterPilot-Knight-20260930-03";if(Directory.Exists(output))throw new BuildFailedException("Never overwrite a package.");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Root+"/ModelTrialMenu.unity",Root+"/ModelTrialBattlefield.unity"},locationPathName=output+"/KnightPilot.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(result.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Build failed.");
            foreach(string name in new[]{"Start-Knight.cmd","开始验收.md","README.txt"})File.Copy("Builds/CharacterPilot-Knight-20260930-02/"+name,output+"/"+name);
            Debug.Log("KNIGHT_NECK_BUILD_READY");
        }
    }
}
