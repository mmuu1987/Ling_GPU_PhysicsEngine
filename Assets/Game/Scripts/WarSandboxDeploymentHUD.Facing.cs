using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Formation facing controls: R / Shift+R rotate the selected block 90 degrees (footprint and facing together).</summary>
    public sealed partial class WarSandboxDeploymentHUD
    {
        private bool FacingEditable => deployment != null && deployment.IsEditing && deployment.Draft != null && deployment.Draft.Count > 0 &&
            deployment.controller != null && deployment.controller.Phase == WarSandboxBattlePhase.Setup &&
            spatialSelection && !Confirming && !plansOpen && !statsOpen && !legionOpen && !pickerOpen && !armyMenu && !templateMenu &&
            !removeArmyConfirm && !translation.Active && resizeHandle == DeploymentResizeHandle.None &&
            (WarSandboxSceneSession.Instance == null || !WarSandboxSceneSession.Instance.InputBlocked);

        private void HandleFacingKeys()
        {
            if (!Input.GetKeyDown(KeyCode.R) || !FacingEditable || WarSandboxUGUI.IsTyping) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            RotateSelected(shift ? -1 : 1);
        }

        private int FacingQuarter(int index) => WarSandboxDeploymentFacing.ResolveQuarter(deployment.Draft, index);
        private string FacingArrow(int index) => WarSandboxDeploymentFacing.Arrow(FacingQuarter(index));

        private string FacingLabel()
        {
            var draft = deployment.Draft;
            if (draft == null || draft.Count == 0) return "朝向";
            return "朝向  " + WarSandboxDeploymentFacing.Name(FacingQuarter(selected)) +
                (draft[selected].facing == WarSandboxDeploymentFacing.Auto ? "  · 自动朝敌" : "  · 手动");
        }

        /// <summary>Rotates the whole block: +1 clockwise, -1 counter-clockwise. Odd turns swap depth/width around the center.</summary>
        private void RotateSelected(int quarters)
        {
            if (!CommitFields()) { nextUiRefresh = 0; return; }
            var draft = deployment.Draft;
            if (draft == null || draft.Count == 0) return;
            var original = draft[selected];
            int quarter = ((FacingQuarter(selected) + quarters) % 4 + 4) % 4;
            var next = original;
            bool swap = (quarters & 1) != 0;
            if (swap)
            {
                var size = original.Size;
                if (!WarSandboxDeploymentResize.TryFromSize(original, original.center, new Vector2(size.z, size.x), out next, out var error))
                { inputError = "无法旋转：" + error; nextUiRefresh = 0; return; }
            }
            next.facing = quarter + 1;
            if (!deployment.TryEditFormation(draft, draft.Revision, selected, original, next, swap, out var editError))
            { inputError = "无法旋转：" + editError; nextUiRefresh = 0; return; }
            inputError = null; RefreshAfterUiChange(); nextUiRefresh = 0;
        }

        private void SetFacingAuto()
        {
            if (!CommitFields()) { nextUiRefresh = 0; return; }
            var draft = deployment.Draft;
            if (draft == null || draft.Count == 0 || draft[selected].facing == WarSandboxDeploymentFacing.Auto) return;
            var original = draft[selected]; var next = original; next.facing = WarSandboxDeploymentFacing.Auto;
            if (!deployment.TryEditFormation(draft, draft.Revision, selected, original, next, false, out var error))
            { inputError = error; nextUiRefresh = 0; return; }
            inputError = null; RefreshAfterUiChange(); nextUiRefresh = 0;
        }

        private void DrawFacingControls(WarSandboxUGUI ui, ref float fy, float fw)
        {
            ui.Label("facing-label", new Rect(0, fy, fw, 26), FacingLabel(), 14, WarSandboxUGUI.Ink, true); fy += 30;
            float bw = (fw - 12) / 3;
            ui.Button("facing-ccw", new Rect(0, fy, bw, 34), "左转90°", () => RotateSelected(-1));
            ui.Button("facing-cw", new Rect(bw + 6, fy, bw, 34), "右转90°", () => RotateSelected(1));
            ui.Button("facing-auto", new Rect(2 * (bw + 6), fy, bw, 34), "自动朝敌", SetFacingAuto, false,
                deployment.Draft[selected].facing != WarSandboxDeploymentFacing.Auto);
            fy += 40;
            ui.Label("facing-hint", new Rect(0, fy, fw, 22), "R 右转 · Shift+R 左转 · 整块连同占地一起转", 12, WarSandboxUGUI.Muted); fy += 30;
        }

        /// <summary>White bar on the front edge of a map block.</summary>
        private void DrawFacingMark(WarSandboxUGUI ui, int index, Rect rect)
        {
            const float inset = 3, bar = 3;
            if (rect.width <= 2 * inset + 2 || rect.height <= 2 * inset + 2) return;
            Rect mark;
            switch (FacingQuarter(index))
            {
                case 0: mark = new Rect(rect.x + inset, rect.y + inset, rect.width - 2 * inset, bar); break;          // +Z = map up
                case 1: mark = new Rect(rect.xMax - inset - bar, rect.y + inset, bar, rect.height - 2 * inset); break; // +X = map right
                case 2: mark = new Rect(rect.x + inset, rect.yMax - inset - bar, rect.width - 2 * inset, bar); break;
                default: mark = new Rect(rect.x + inset, rect.y + inset, bar, rect.height - 2 * inset); break;
            }
            ui.Panel("map-facing-" + index, mark, Color.white, false);
        }
    }
}
