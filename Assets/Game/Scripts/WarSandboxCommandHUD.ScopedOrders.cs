using System;
using UnityEngine;
namespace MassEngine.Game
{
    public sealed partial class WarSandboxCommandHUD
    {
        private bool playerScopeEnabled;
        private PlayerCommandScope moveIntent;
        private ComputeBuffer moveBattleIdentity,receiptBattleIdentity;
        private LocalOrderReceipt lastPlayerReceipt;
        private LocalOrderChannel receiptChannel;
        private bool resumeAfterCommit,commitNotified;
        private int receiptPlaybackRevision;
        private string receiptFeedback;private float receiptFeedbackSuppressedUntil;
        public LocalOrderReceipt LastPlayerReceipt=>lastPlayerReceipt;
        public bool PlayerScopeEnabled=>playerScopeEnabled;
        private void UpdateScopedOrdersP8()
        {
            ResolveReferences();
            if(!SelectionBattleP7||controller.manager==null||controller.manager.Buffers==null||!controller.manager.Buffers.IsAllocated)
            {
                if(playerScopeEnabled){CloseSelectionPreview();moveIntent=null;lastPlayerReceipt=null;receiptChannel=null;}
                return;
            }
            // Ordinary battles use P8; an explicitly requested P7 developer preview retains its input blockade for regression tests.
            if(!selectionPreviewEnabled){selectionPreviewEnabled=true;playerScopeEnabled=true;}
            if(awaitingMoveTarget&&moveIntent!=null&&controller.manager.Buffers?.agentBuffer!=moveBattleIdentity)
            {awaitingMoveTarget=false;moveIntent=null;ScopedFeedbackP8(false,"本局已改变，请重新选择目标");}
            ObserveLocalReceiptP8();
        }
        private bool CaptureScopeP8(ArmyOrderType kind,bool append,out PlayerCommandScope scope,out string error)
        {
            scope=null;error=null;ResolveReferences();
            if(controller==null||controller.SelectedArmy==null){error="请先选择军团";return false;}
            if(PendingDecision26||helpOpen||WarSandboxUGUI.IsTyping||WarSandboxDeploymentHUD.BlocksInput(controller)||!WarSandboxSceneSession.AllowsBattleCommands(controller)||IsTerminalPhase(controller.Phase))
            {error="当前界面或战斗状态不允许下令";return false;}
            if(selectionDragging){error="请先完成或取消框选";return false;}
            if(selectionPreviewEnabled&&!playerScopeEnabled){error="P7开发预览仅验证选择，不接受玩家命令";return false;}
            if(memberSelection!=null&&(!memberSelection.IsCurrent||memberSelection.State.Team!=controller.selectedTeam))
            {
                if(!memberSelection.IsCurrent){error="本局选区已失效，请重新框选";return false;}
                memberSelection.Clear(controller.selectedTeam);
            }
            if(selectionError!=null){error="选区不可用："+selectionError;return false;}
            if(memberSelection==null)scope=new PlayerCommandScope(MemberSelectionScope.WholeArmy,controller.selectedTeam,0,0,0,null);
            else
            {
                var state=memberSelection.State;int[] ids=Array.Empty<int>();int version=state.Version;
                if(state.Scope==MemberSelectionScope.Local&&!memberSelection.TrySnapshot(out ids,out version)){error="选区尚未确认，请稍候";return false;}
                scope=new PlayerCommandScope(state.Scope,state.Team,state.Epoch,state.Request,version,ids);
            }
            return scope.Allows(kind,append,out error);
        }
        private void ScopedFeedbackP8(bool accepted,string message)
        {
            receiptFeedbackSuppressedUntil=accepted?0:Time.unscaledTime+4;
            string text=message+(accepted?"":"\n未下达，旧命令保留");SetFeedback(text);Feedback25?.Result(accepted,text,null,"局部指挥");nextUiRefresh=0;
        }
        private bool CaptureMoveIntentP8()
        {
            if(!CaptureScopeP8(ArmyOrderType.Move,false,out var scope,out string error)){ScopedFeedbackP8(false,error);return false;}
            moveIntent=scope;moveBattleIdentity=controller.manager?.Buffers?.agentBuffer;return true;
        }
        // Returns handled=true for a local command OR an explicit rejection. Only a validated WholeArmy falls through to legacy API.
        private bool RoutePlayerOrderP8(ArmyOrderType kind,Vector3 target,bool append,out bool accepted)
        {
            accepted=false;
            if(!CaptureScopeP8(kind,append,out var scope,out string error)){ScopedFeedbackP8(false,error);return true;}
            if(kind==ArmyOrderType.Move&&awaitingMoveTarget&&moveIntent!=null&&(!moveIntent.SameIntent(scope)||moveBattleIdentity!=controller.manager?.Buffers?.agentBuffer))
            {awaitingMoveTarget=false;moveIntent=null;ScopedFeedbackP8(false,"选点期间选区/本局已改变，请重新按移动");return true;}
            if(scope.Scope==MemberSelectionScope.WholeArmy)return false;
            if(!SelectionBattleP7){ScopedFeedbackP8(false,"局部命令仅用于当前战斗");return true;}
            if(kind==ArmyOrderType.Retreat)target=controller.SelectedArmy.spawnCenter;
            if((kind==ArmyOrderType.Move||kind==ArmyOrderType.Retreat)&&(!LocalOrderPlan.Finite(new Vector2(target.x,target.z))||float.IsNaN(target.y)||float.IsInfinity(target.y)))
            {ScopedFeedbackP8(false,"目标坐标无效");return true;}
            var manager=controller.manager;
            if(!manager.TryEnableLocalOrderPrototype(out error)){ScopedFeedbackP8(false,"局部导航不可用："+error);return true;}
            LocalOrderKind localKind=kind==ArmyOrderType.Attack?LocalOrderKind.Attack:kind==ArmyOrderType.Retreat?LocalOrderKind.Retreat:kind==ArmyOrderType.Hold?LocalOrderKind.Hold:LocalOrderKind.Move;
            var receipt=manager.LocalOrders.Submit(manager.LocalOrders.Epoch,scope.Team,scope.Version,scope.CopyMembers(),localKind,new Vector2(target.x,target.z));
            if(receipt.Status==LocalOrderStatus.Rejected){ScopedFeedbackP8(false,receipt.Error);return true;}
            lastPlayerReceipt=receipt;receiptChannel=manager.LocalOrders;receiptBattleIdentity=manager.Buffers.agentBuffer;receiptPlaybackRevision=controller.PlaybackRevision;
            resumeAfterCommit=controller.Phase==WarSandboxBattlePhase.Paused;commitNotified=false;receiptFeedback=null;accepted=true;awaitingMoveTarget=false;moveIntent=null;
            ScopedFeedbackP8(true,"军团 "+scope.Team+" · 局部 "+scope.Count+" 人 · "+FormatOrder(kind)+"\n正在校验/规划，尚未提交；旧命令继续");return true;
        }
        private void ObserveLocalReceiptP8()
        {
            var r=lastPlayerReceipt;if(r==null)return;
            if(controller==null||controller.manager==null||controller.manager.Buffers?.agentBuffer!=receiptBattleIdentity||controller.manager.LocalOrders!=receiptChannel||!SelectionBattleP7)
            {lastPlayerReceipt=null;resumeAfterCommit=false;return;}
            if(r.Status==LocalOrderStatus.Invalidated){lastPlayerReceipt=null;resumeAfterCommit=false;return;}
            bool committed=r.Status==LocalOrderStatus.Committed||r.Status==LocalOrderStatus.GpuExecuting;
            if(committed&&!commitNotified){controller.NotifyLocalOrderCommitted();commitNotified=true;}
            if(committed&&resumeAfterCommit)
            {
                if(controller.PlaybackRevision!=receiptPlaybackRevision||controller.Phase!=WarSandboxBattlePhase.Paused)resumeAfterCommit=false;
                else if(WarSandboxSceneSession.AllowsBattleCommands(controller)&&!PendingDecision26&&!helpOpen)
                {controller.StartOrResumeBattle();resumeAfterCommit=false;}
            }
            if(r.Status==LocalOrderStatus.AwaitingSnapshot)return;
            string message="军团 "+r.Team+" · 局部 #"+r.Sequence+" · "+LocalOrderNameP8(r.Kind)+"\n";
            bool ok=committed;
            if(r.Status==LocalOrderStatus.Rejected)message+="拒绝："+r.Error;
            else if(r.Status==LocalOrderStatus.Invalidated)message+="本次命令已被覆盖/本局已失效";
            else message+=(r.Status==LocalOrderStatus.GpuExecuting?"GPU已执行 "+r.GpuExecutingMembers+" / "+r.SubmittedMembers:"已提交 "+r.SubmittedMembers+" 人，等待GPU确认")+"；请求 "+r.MemberSnapshot.Count+" 人"+(r.NavigationError==null?"":"；导航刷新失败，保留旧路径："+r.NavigationError);
            if(message==receiptFeedback||Time.unscaledTime<receiptFeedbackSuppressedUntil)return;receiptFeedback=message;ScopedFeedbackP8(ok,message);
        }
        private string LocalOrderNameP8(LocalOrderKind kind)=>kind==LocalOrderKind.Attack?"攻击":kind==LocalOrderKind.Hold?"待命":kind==LocalOrderKind.Retreat?"撤退":"移动";
        private bool GroundMoveClickP8(Ray ray,bool append)
        {
            if(!controller.TryRaycastGround(ray,Mathf.Max(1f,maxRayDistance),groundMask,out var point,out string error)){ScopedFeedbackP8(false,error);return false;}
            return IssueMoveTo(point,append);
        }
        private void MinimapCommandP8(Vector3 target,int button,bool shift)
        {
            var action=WarSandboxMinimapProjection.ResolvePointerAction(button,awaitingMoveTarget,shift);
            if(action==WarSandboxMinimapAction.FocusCamera)
            {
                if(!controller.TryResolveGroundPoint(target,out target,out string error)){SetFeedback(error);return;}
                cameraFocusMode=CameraFocusMode.None;cameraManager?.CenterTacticalPoint(target);
            }
            else if(action!=WarSandboxMinimapAction.None)IssueMoveTo(target,action==WarSandboxMinimapAction.QueueMoveSelectedArmy);
        }
        private void PlayerOrderHotkeyP8(KeyCode key)
        {switch(key){case KeyCode.A:IssueAttack();break;case KeyCode.M:BeginMoveOrder();break;case KeyCode.H:IssueHold();break;case KeyCode.R:IssueRetreat();break;}}
        private void ClearPlayerSelectionP8()
        {CancelMoveTarget();moveIntent=null;ReleaseSelectionCaptureP7();memberSelection?.Dispose();memberSelection=null;selectionError=null;nextUiRefresh=0;}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool TestPlayerOrderP8(ArmyOrderType kind,Vector3 target,bool append=false)
        {
            if(kind==ArmyOrderType.Move)return IssueMoveTo(target,append);
            var previous=lastPlayerReceipt;if(kind==ArmyOrderType.Attack)IssueAttack();else if(kind==ArmyOrderType.Hold)IssueHold();else if(kind==ArmyOrderType.Retreat)IssueRetreat();
            return lastPlayerReceipt!=previous&&lastPlayerReceipt.Status!=LocalOrderStatus.Rejected;
        }
        public bool TestGroundMoveP8(Vector3 target,bool append=false)=>GroundMoveClickP8(new Ray(target+Vector3.up*500,Vector3.down),append);
        public void TestMinimapP8(Vector3 target,int button,bool shift=false)=>MinimapCommandP8(target,button,shift);
        public void TestBeginMoveP8()=>BeginMoveOrder();
        public void TestHotkeyP8(KeyCode key)=>PlayerOrderHotkeyP8(key);
        public void TestWholeArmyScopeP8()=>ClearPlayerSelectionP8();
#endif
    }
}
