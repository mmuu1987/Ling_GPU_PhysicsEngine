using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Raycasts mouse clicks into the world and hands the hit point to the manager as a
    /// RUNTIME flow target override. Configuration assets are never touched — the
    /// override lives on the manager and is cleared by StopBattle / ResetScenario.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/Click Flow Target Setter")]
    public sealed class ClickFlowTargetSetter : MonoBehaviour
    {
        [Header("Source")]
        public MassEngineManager manager;
        public Camera raycastCamera;

        [Header("Target")]
        [Tooltip("Team receiving the order. The engine maintains flow fields for teams 0 (attacker) and 1 (defender) only.")]
        [Range(0, 1)] public int teamId;
        public LayerMask hitMask = ~0;
        [Min(0f)] public float maxRayDistance = 1000f;

        [Header("Input")]
        public int mouseButton;
        public bool startBattleOnClick = true;

        private void Reset()
        {
            manager = GetComponent<MassEngineManager>();
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
            MassEngineManager targetManager = manager != null ? manager : GetComponent<MassEngineManager>();
            if (targetManager == null)
                return;

            targetManager.SetFlowTargetOverride(teamId, point);

            if (startBattleOnClick)
                targetManager.StartBattle();
        }
    }
}
