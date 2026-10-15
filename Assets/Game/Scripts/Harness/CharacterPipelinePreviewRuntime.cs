using System;
using System.IO;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Only generated preview menus use this component. No gameplay defaults or personal main-game settings are changed.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class CharacterPipelinePreviewRuntime : MonoBehaviour
    {
        public string planDirectory = "CharacterPipelinePlans";
        public string previewName = "CharacterPreview";
        private static CharacterPipelinePreviewRuntime instance;
        private WarSandboxRuntimeDeployment last;
        private bool automated;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() => instance=null;
        private void Awake()
        {
            if(instance!=null && instance!=this){Destroy(gameObject);return;}
            instance=this;DontDestroyOnLoad(gameObject);
            QualitySettings.vSyncCount=0;Application.targetFrameRate=30;
            automated=Array.IndexOf(Environment.GetCommandLineArgs(),"--model-trial-smoke")>=0;
            Debug.Log("CHARACTER_PILOT_FRAME_CAP target=30 vSync=0; isolated pipeline preview, not performance evidence.");
        }
        private void Update()
        {
            if(automated)return;
            var session=WarSandboxSceneSession.Instance;var controller=session!=null?session.Controller:null;
            var deployment=controller!=null?controller.GetComponent<WarSandboxRuntimeDeployment>():null;
            if(deployment==null){last=null;return;}
            if(deployment==last)return;
            // Serialized preview configuration is not allowed to escape persistentDataPath.
            string safe=Path.GetFileName(planDirectory);
            if(string.IsNullOrWhiteSpace(safe) || safe=="." || safe==".." || safe!=planDirectory)safe="CharacterPipelinePlans";
            deployment.PlanStore=new WarSandboxLocalPlanStore(Path.Combine(Application.persistentDataPath,safe));last=deployment;
        }
        private void OnDestroy(){if(instance==this)instance=null;}
    }
}
