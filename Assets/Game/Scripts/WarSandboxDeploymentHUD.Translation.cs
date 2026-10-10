using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace MassEngine.Game
{
    public sealed partial class WarSandboxDeploymentHUD
    {
        private readonly WarSandboxDeploymentTranslation translation = new WarSandboxDeploymentTranslation();
        private WarSandboxDeploymentMapPointer mapPointer;
        private Rect translationMap;
        private Vector2 translationWorld, translationLayout;
        private RectTransform translationGhost;
        private bool translationPlacement, translationSpatial, translationNarrow;
        private float translationScale;
        private Vector2 translationScreen;
        private bool MapGestureAllowed => isActiveAndEnabled && deployment != null && deployment.IsEditing && deployment.Draft != null &&
            deployment.controller != null && deployment.controller.Phase==WarSandboxBattlePhase.Setup && !Confirming && !plansOpen && !statsOpen &&
            !legionOpen && !pickerOpen && !armyMenu && !templateMenu && !removeArmyConfirm && tightDialog==0 &&
            (WarSandboxSceneSession.Instance==null || !WarSandboxSceneSession.Instance.InputBlocked);
        private void CancelTranslation(string reason=null)
        {
            bool had=translation.Active || translationGhost!=null;
            if(resizeHandle!=DeploymentResizeHandle.None)ResizeFieldsPreview(default,false);
            resizeHandle=DeploymentResizeHandle.None;resizeError=null;
            translation.Cancel(); translationPlacement=false;
            if(translationGhost!=null) { if(Application.isPlaying)Destroy(translationGhost.gameObject);else DestroyImmediate(translationGhost.gameObject);translationGhost=null; }
            if(had) {nextUiRefresh=0;if(reason!=null)inputError=reason;}
        }
        private void OnApplicationFocus(bool focused) { if(!focused)CancelTranslation("拖动已取消：窗口失去焦点。"); }
        private bool CheckTranslation()
        {
            if(!translation.Active)return false;
            if(Input.GetKeyDown(KeyCode.Escape)){CancelTranslation("拖动已取消。");return true;}
            if(!MapGestureAllowed || runtimeUi==null || new Vector2(runtimeUi.Width,runtimeUi.Height)!=translationLayout || runtimeUi.Scale!=translationScale ||
                new Vector2(Screen.width,Screen.height)!=translationScreen || spatialSelection!=translationSpatial || narrowMap!=translationNarrow || placing!=translationPlacement ||
                !translation.IsCurrent(deployment.Draft,selected,deployment.WorldSize,translationMap) || HasPendingInputs())
                CancelTranslation("拖动已取消：页面、布局、选择或草稿已变化。");
            return false;
        }
        private Vector2 MapPoint(PointerEventData e)
        {
            var n=mapPointer.Normalized(e);return new Vector2(translationMap.x+n.x*translationMap.width,translationMap.y+n.y*translationMap.height);
        }
        private void BindTranslation(WarSandboxUGUI ui,Rect map,Vector2 world)
        {
            translationMap=map;translationWorld=world;
            mapPointer=ui.DeploymentPointerArea("map-pointer",map);
            mapPointer.Down=TranslationDown;mapPointer.Move=TranslationMove;mapPointer.Up=TranslationUp;
            mapPointer.Cancel=()=>CancelTranslation();
        }
        private void TranslationDown(PointerEventData e)
        {
            CancelTranslation();
            if(!MapGestureAllowed)return;
            // Explicit-update policy: never parse/overwrite unfinished text as a side effect of a gesture.
            if(HasPendingInputs()){inputError="输入尚未更新：请先更新数值，再拖动或点击放置；原输入已保留。";nextUiRefresh=0;return;}
            var draft=deployment.Draft;var point=MapPoint(e);int index=-1;
            resizeHandle=HitResizeHandle(point);
            if(resizeHandle!=DeploymentResizeHandle.None)
            {
                index=selected;resizePreview=draft[index];
                if(!deployment.TryGetResizeRadius(resizePreview,out resizeRadius,out var reason)){CancelTranslation();inputError=reason;nextUiRefresh=0;return;}
                resizeJitter=resizePreview.template.spawnConfig.formationJitterFraction;
            }
            if(index>=0) { }
            else if(placing && draft.Count>0)index=selected;
            else for(int i=draft.Count-1;i>=0;i--)if(MapBounds(draft[i].Bounds,translationWorld,translationMap).Contains(point)){index=i;break;}
            if(index<0){spatialSelection=false;nextUiRefresh=0;return;}
            selected=index;ReadFields();EventSystem.current?.SetSelectedGameObject(null);
            // Do not expand the side panel while the pointer is held: it changes the map projection.
            translationPlacement=placing;
            translationLayout=new Vector2(runtimeUi.Width,runtimeUi.Height);
            translationScale=runtimeUi.Scale;translationScreen=new Vector2(Screen.width,Screen.height);translationSpatial=spatialSelection;translationNarrow=narrowMap;
            translation.Begin(draft,index,translationWorld,translationMap,point,e.position);
        }
        private void TranslationMove(PointerEventData e)
        {
            if(!translation.Active)return;
            if(CheckTranslation())return;
            if(!translation.Active)return;
            if(!translation.Move(deployment.Draft,selected,deployment.WorldSize,translationMap,MapPoint(e),e.position))
            {CancelTranslation("拖动已取消：已离开地图内容区域。");inputError="拖动已取消：已离开地图内容区域。";nextUiRefresh=0;return;}
            if(translationPlacement && translation.Dragging){CancelTranslation("点击放置模式不接受拖拽；请短点击目标点。");return;}
            if(!translation.Dragging)return;
            if(translationGhost==null)
            {
                var go=new GameObject("deployment-translation-preview",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Outline));
                translationGhost=(RectTransform)go.transform;translationGhost.SetParent(mapPointer.transform,false);
                translationGhost.anchorMin=translationGhost.anchorMax=translationGhost.pivot=new Vector2(0,1);
                go.GetComponent<Image>().color=new Color(.2f,.85f,.85f,.35f);go.GetComponent<Image>().raycastTarget=false;
                go.GetComponent<Outline>().effectColor=WarSandboxUGUI.Accent;
            }
            var entry=translation.Original;entry.center=translation.PreviewCenter;
            if(resizeHandle!=DeploymentResizeHandle.None)
            {
                var delta=translation.PreviewCenter-translation.Original.center;
                bool converted=WarSandboxDeploymentResize.TryResize(translation.Original,resizeHandle,new Vector2(delta.x,delta.z),Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),resizeRadius,resizeJitter,out resizePreview,out resizeError);
                if(converted)WarSandboxDeploymentResize.Fits(resizePreview,resizeRadius,resizeJitter,out resizeError);
                entry=resizePreview;ResizeFieldsPreview(entry,true);
                translationGhost.GetComponent<Image>().color=resizeError==null?new Color(.2f,.85f,.85f,.35f):new Color(1,.2f,.15f,.45f);
            }
            var r=MapBounds(entry.Bounds,translationWorld,translationMap);
            translationGhost.anchoredPosition=new Vector2(r.x-translationMap.x,-(r.y-translationMap.y));translationGhost.sizeDelta=r.size;
        }
        private void TranslationUp(PointerEventData e)
        {
            if(!translation.Active)return;
            TranslationMove(e);if(!translation.Active)return;
            if(!translation.Dragging && !translationPlacement)
            {CancelTranslation();spatialSelection=true;RefreshAfterUiChange();nextUiRefresh=0;return;}
            if(resizeHandle!=DeploymentResizeHandle.None)
            {
                string reason=resizeError;bool committed=reason==null && deployment.TryResizeDraft(translation.Draft,translation.Revision,translation.Index,translation.Original,resizePreview,out reason);
                CancelTranslation();if(committed){spatialSelection=true;RefreshAfterUiChange();}else inputError="未提交尺寸："+reason;
                nextUiRefresh=0;return;
            }
            var center=translation.PreviewCenter;
            if(translationPlacement){var p=WarSandboxMinimapProjection.MapToWorld(MapPoint(e),translationWorld,translationMap);center=new Vector3(p.x,translation.Original.center.y,p.z);}
            bool ok=deployment.TryTranslateDraft(translation.Draft,translation.Revision,translation.Index,translation.Original,center,out string error);
            CancelTranslation();
            if(ok){placing=false;spatialSelection=true;RefreshAfterUiChange();}
            else inputError="未提交位置："+error;
            nextUiRefresh=0;
        }
    }
}

