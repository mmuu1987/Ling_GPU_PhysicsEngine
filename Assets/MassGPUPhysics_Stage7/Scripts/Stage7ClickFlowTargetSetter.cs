using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [DisallowMultipleComponent]
    [AddComponentMenu("MassGPUPhysics/Stage7/Click Flow Target Setter")]
    public sealed class Stage7ClickFlowTargetSetter : MonoBehaviour
    {
        [Header("Source")]
        public MassGpuSystemManager_Stage7 manager;
        public Camera raycastCamera;

        [Header("Target")]
        public int teamId;
        public LayerMask hitMask = ~0;
        [Min(0f)] public float maxRayDistance = 1000f;

        [Header("Input")]
        public int mouseButton;
        public bool startBattleOnClick = true;

        private void Reset()
        {
            manager = GetComponent<MassGpuSystemManager_Stage7>();
            raycastCamera = Camera.main;
        }

        private void Update()
        {
            if (!Input.GetMouseButtonDown(mouseButton))
                return;

            Camera cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;
            if (cameraToUse == null)
                return;

            Ray ray = cameraToUse.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(0f, maxRayDistance), hitMask, QueryTriggerInteraction.Ignore))
                return;

            SetTargetPoint(hit.point);
        }

        public void SetTargetPoint(Vector3 point)
        {
            MassGpuSystemManager_Stage7 targetManager = manager != null ? manager : GetComponent<MassGpuSystemManager_Stage7>();
            if (targetManager == null)
                return;

            if (targetManager.UnitTypes == null)
                targetManager.Initialize();

            UnitTypeConfig config = targetManager.UnitTypes != null ? targetManager.UnitTypes.FindFirstConfigForTeam(teamId) : null;
            MovementConfig movement = config != null ? config.movementConfig : null;
            if (movement == null)
                return;

            movement.useConfiguredFlowTarget = true;
            movement.flowTargetMode = FlowFieldTargetMode.Point;
            movement.targetPoint = point;

            if (startBattleOnClick)
                targetManager.StartBattle();
        }
    }
}
