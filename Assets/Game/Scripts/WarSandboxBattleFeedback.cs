using UnityEngine;

namespace MassEngine.Game
{
    [DisallowMultipleComponent]
    public sealed class WarSandboxBattleFeedback : MonoBehaviour
    {
        public string Message { get; private set; }
        public float MessageUntil { get; private set; }
        private WarSandboxBattleController controller;
        private void OnEnable()
        {
            controller = GetComponent<WarSandboxBattleController>();
            if (controller != null) controller.FeedbackRequested += OnFeedback;
        }
        private void OnDisable() { if (controller != null) controller.FeedbackRequested -= OnFeedback; }
        private void OnFeedback(WarSandboxSoundCue cue)
        {
            if (!Application.isPlaying) return;
            WarSandboxAudio.Ensure().Play(cue);
            if (cue == WarSandboxSoundCue.Start) Message = "战斗开始";
            else if (cue == WarSandboxSoundCue.ControlPoint)
                Message = controller.IsControlPointContested ? "据点争夺中 · 占领暂停" : controller.ControlPointOwnerTeamId < 0
                    ? "据点已回到中立" : WarSandboxBattleController.DefaultArmyName(controller.ControlPointOwnerTeamId) + "开始占领据点";
            else return;
            MessageUntil = Time.unscaledTime + 2.5f;
        }
    }
}
