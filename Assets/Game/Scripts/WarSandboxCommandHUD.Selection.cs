using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassEngine.Game
{
    public sealed partial class WarSandboxCommandHUD
    {
        // P7 developer preview remains isolated; P8 enables player selection through the unified order router.
        private bool selectionPreviewEnabled,selectionDragging,selectionSyntheticInput;
        private Vector2 selectionDown,selectionPointer,selectionScreen;
        private Rect selectionViewport; private Camera selectionCamera;
        private GpuMemberSelection memberSelection;
        private string selectionError;
        private static readonly HashSet<WarSandboxCommandHUD> selectionCaptures=new HashSet<WarSandboxCommandHUD>();
        public bool SelectionPreviewActive=>selectionPreviewEnabled;
        public GpuMemberSelection SelectionPreview=>memberSelection;
        public static bool IsSelectionCameraCaptured(Camera camera)
        {foreach(var h in selectionCaptures)if(h!=null&&h.selectionDragging&&h.selectionCamera==camera)return true;return false;}
        public bool BeginSelectionPreviewForDevelopment(out string error)
        {
            error=null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResolveReferences();
            if(controller==null||controller.manager==null||controller.SelectedArmy==null||!SelectionBattleP7){error="P7 preview requires a running or paused initialized battle";return false;}
            try{CloseSelectionPreview();memberSelection=new GpuMemberSelection(controller.manager,controller.selectedTeam);selectionPreviewEnabled=true;awaitingMoveTarget=false;nextUiRefresh=0;return true;}
            catch(Exception ex){error=ex.Message;CloseSelectionPreview();return false;}
#else
            error="P7 preview is development-only until P8 input scope is unified";return false;
#endif
        }
        public void CloseSelectionPreview()
        {ReleaseSelectionCaptureP7();memberSelection?.Dispose();memberSelection=null;selectionPreviewEnabled=false;playerScopeEnabled=false;selectionError=null;selectionSyntheticInput=false;nextUiRefresh=0;}
        private bool SelectionBattleP7=>controller!=null&&(controller.Phase==WarSandboxBattlePhase.Running||controller.Phase==WarSandboxBattlePhase.Paused);
        private bool SelectionBlockedP7=>!SelectionBattleP7||controller.SelectedArmy==null||PendingDecision26||helpOpen||WarSandboxUGUI.IsTyping||WarSandboxDeploymentHUD.BlocksInput(controller)||(WarSandboxSceneSession.Instance!=null&&WarSandboxSceneSession.Instance.InputBlocked);
        private void ReleaseSelectionCaptureP7(){selectionDragging=false;selectionCaptures.Remove(this);}
        private void CancelSelectionP7(bool clearScope=true)
        {
            ReleaseSelectionCaptureP7();
            if(playerScopeEnabled&&!clearScope){if(memberSelection!=null&&memberSelection.State.Scope==MemberSelectionScope.Pending)memberSelection.Invalidate("输入上下文改变，请重新框选");}
            else if(memberSelection!=null&&memberSelection.State.Scope!=MemberSelectionScope.WholeArmy)memberSelection.Clear(controller!=null?controller.selectedTeam:memberSelection.State.Team);
            nextUiRefresh=0;
        }
        private bool RejectSelectionOrderP7()
        {
            if(!selectionPreviewEnabled||playerScopeEnabled)return false;
            awaitingMoveTarget=false;SetFeedback("P7 选择验证中：玩家命令入口尚未接通\n按钮／热键／小地图下令均禁用，旧命令保留");nextUiRefresh=0;return true;
        }
        private void UpdateSelectionPreviewP7()
        {
            if(!selectionPreviewEnabled)return;ResolveReferences();
            if(!SelectionBattleP7){ReleaseSelectionCaptureP7();memberSelection?.Dispose();memberSelection=null;return;}
            if(memberSelection!=null&&!memberSelection.IsCurrent){ReleaseSelectionCaptureP7();memberSelection.Dispose();memberSelection=null;}
            if(memberSelection==null&&!playerScopeEnabled)
            {
                if(selectionError!=null)return;
                try{memberSelection=new GpuMemberSelection(controller.manager,controller.selectedTeam);}
                catch(Exception ex){selectionError=ex.Message;SetFeedback("P7选区不可用："+ex.Message+"；命令入口仍禁用，请关闭开发预览后重试");return;}
            }
            if(memberSelection!=null&&memberSelection.State.Team!=controller.selectedTeam){ReleaseSelectionCaptureP7();memberSelection.Clear(controller.selectedTeam);}
            if(SelectionBlockedP7){CancelSelectionP7(!playerScopeEnabled);memberSelection?.Tick();return;}
            memberSelection?.Tick();
            if(selectionSyntheticInput)return;
            var camera=commandCamera!=null?commandCamera:Camera.main;
            bool navigation=Input.GetMouseButton(1)||Input.GetMouseButton(2)||Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt)||Input.GetKey(KeyCode.AltGr)||Input.mouseScrollDelta.sqrMagnitude>0;
            ProcessSelectionPointerP7(Input.mousePosition,Input.GetMouseButtonDown(0),Input.GetMouseButton(0),Input.GetMouseButtonUp(0),IsMouseOverInterface(),navigation,Input.GetKeyDown(KeyCode.Escape),Application.isFocused,camera);
        }
        private void ProcessSelectionPointerP7(Vector2 point,bool down,bool held,bool up,bool overUI,bool cameraNavigation,bool escape,bool focused,Camera camera)
        {
            if(!selectionPreviewEnabled)return;
            // Modal/UI > deployment > target picking > selection. Camera gestures never share a capture.
            if(escape&&awaitingMoveTarget){CancelMoveTarget();moveIntent=null;ReleaseSelectionCaptureP7();return;}
            if(SelectionBlockedP7||!focused){CancelSelectionP7(!playerScopeEnabled);return;}
            if(escape){CancelSelectionP7();moveIntent=null;return;}
            if(camera==null||overUI||cameraNavigation||awaitingMoveTarget){ReleaseSelectionCaptureP7();return;}
            Vector2 screen=new Vector2(Screen.width,Screen.height);
            bool inside=new Rect(Vector2.zero,screen).Contains(point)&&camera.pixelRect.Contains(point);
            if(!inside){ReleaseSelectionCaptureP7();return;}
            if(selectionDragging&&(selectionCamera!=camera||selectionScreen!=screen||selectionViewport!=camera.pixelRect)){ReleaseSelectionCaptureP7();return;}
            if(down)
            {
                if(memberSelection==null)
                {
                    if(selectionError!=null)return;
                    try{memberSelection=new GpuMemberSelection(controller.manager,controller.selectedTeam);}
                    catch(Exception ex){selectionError=ex.Message;SetFeedback("选区不可用："+ex.Message+"；请明确切换整军团或重开后重试");return;}
                }
                selectionDragging=true;selectionDown=selectionPointer=point;selectionCamera=camera;selectionScreen=screen;selectionViewport=camera.pixelRect;
                selectionCaptures.Add(this);cameraFocusMode=CameraFocusMode.None;
                var motion=cameraManager!=null?cameraManager.GetComponent<BattlefieldCameraComfort24>():null;motion?.CancelMotion();
            }
            if(!selectionDragging)return;selectionPointer=point;
            if(up)
            {
                var a=selectionDown;ReleaseSelectionCaptureP7();
                if((point-a).sqrMagnitude>=36f){memberSelection.Request(new MemberSelectionProjection(camera,a,point));nextUiRefresh=0;}
            }
            else if(!held&&!down)ReleaseSelectionCaptureP7();
        }
        private string SelectionStatusP7()
        {
            if(memberSelection==null)return selectionError!=null?"选区不可用："+selectionError:playerScopeEnabled?"当前军团 "+controller.selectedTeam+" · 整军团范围 · 存活 "+controller.GetAliveUnitCount(controller.selectedTeam):"P7 开发预览 · 等待战斗";
            if(controller==null||controller.SelectedArmy==null)return "请先选择军团 · 未启用局部命令";
            var s=memberSelection.State;string team="当前军团 "+s.Team;
            if(s.Scope==MemberSelectionScope.Pending)return team+" · 选区确认中（禁用命令）";
            if(s.Scope==MemberSelectionScope.Unavailable)return team+" · 选区不可用："+s.Error;
            if(s.Scope==MemberSelectionScope.WholeArmy)return team+" · 整军团范围 · "+(memberSelection.Busy?"数量确认中":("存活 "+s.Alive+"（GPU采样）"));
            string command="沿用军团命令";var local=controller.manager.LocalOrders;
            if(local!=null&&s.TrySnapshot(out var ids,out _)&&ids.Length>0)
            {
                int first=local.OrderAt(ids[0]).sequence;bool mixed=false;
                foreach(int i in ids)if(local.OrderAt(i).sequence!=first){mixed=true;break;}
                command=mixed?"混合命令":first==0?"沿用军团命令":"局部历史命令 #"+first;
            }
            return team+" · 已选 "+s.Count+" / 存活 "+s.Alive+(s.Scope==MemberSelectionScope.Empty?" · 零选中（不回退整军团）":" · "+command);
        }
        private void DrawSelectionPreviewP7(WarSandboxUGUI ui,float width)
        {
            if(!selectionPreviewEnabled)return;
            float w=Mathf.Min(480,width-32),x=(width-w)*.5f;
            if(playerScopeEnabled)ui.Button("whole-army-scope",new Rect(x+w-102,88,102,26),"整军团范围",ClearPlayerSelectionP8);
            ui.Panel("p7-preview",new Rect(x,118,w,80),WarSandboxUGUI.Surface);
            ui.Label("p7-title",new Rect(x+8,120,w-16,26),(playerScopeEnabled?"左键拖框：当前军团存活成员 · 投影选取（可穿遮挡）":"P7 开发验证 · 左键拖框 · 投影选取（可穿遮挡）"),13,WarSandboxUGUI.Accent);
            ui.Label("p7-state",new Rect(x+8,146,w-16,44),SelectionStatusP7()+(playerScopeEnabled?"\n局部仅支持单目标；Esc取消选点/清选区，旧命令保留":"\n玩家下令入口禁用；Esc清选区，旧命令保留"),12,WarSandboxUGUI.Ink);
        }
        private void DrawSelectionRectangleP7()
        {
            if(!selectionDragging)return;
            float x=Mathf.Min(selectionDown.x,selectionPointer.x),y=Screen.height-Mathf.Max(selectionDown.y,selectionPointer.y),w=Mathf.Abs(selectionDown.x-selectionPointer.x),h=Mathf.Abs(selectionDown.y-selectionPointer.y);
            var old=GUI.color;GUI.color=new Color(.15f,1,.85f,.15f);GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);GUI.color=new Color(.15f,1,.85f,.9f);
            GUI.DrawTexture(new Rect(x,y,w,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x,y+h,w,1),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x,y,1,h),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(x+w,y,1,h),Texture2D.whiteTexture);GUI.color=old;
        }
        private void LateUpdate(){if(selectionPreviewEnabled&&!SelectionBlockedP7&&memberSelection!=null&&memberSelection.State.Team==controller.selectedTeam)memberSelection.Draw(commandCamera!=null?commandCamera:Camera.main);}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void TestSelectionPointerP7(Vector2 point,bool down,bool held,bool up,bool overUI=false,bool cameraNavigation=false,bool escape=false,bool focused=true)
        {selectionSyntheticInput=true;ProcessSelectionPointerP7(point,down,held,up,overUI,cameraNavigation,escape,focused,commandCamera!=null?commandCamera:Camera.main);}
        public string TestSelectionStatusP7()=>SelectionStatusP7();
#endif
    }
}

