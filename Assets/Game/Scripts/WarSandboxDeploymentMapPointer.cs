using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace MassEngine.Game
{
    /// <summary>Owns exactly one left pointer. No PointerClick handler: release cannot also place a formation.</summary>
    public sealed class WarSandboxDeploymentMapPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<PointerEventData> Down, Move, Up;
        public Action Cancel;
        private int? pointer;
        public RectTransform Rect => (RectTransform)transform;
        public Vector2 Normalized(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, e.position, e.pressEventCamera, out var p);
            var r=Rect.rect; return new Vector2((p.x-r.xMin)/r.width, 1-(p.y-r.yMin)/r.height);
        }
        public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold=false; }
        public void OnPointerDown(PointerEventData e)
        {
            if(e.button!=PointerEventData.InputButton.Left || pointer.HasValue)return;
            pointer=e.pointerId; Down?.Invoke(e);
        }
        public void OnBeginDrag(PointerEventData e) { OnDrag(e); }
        public void OnDrag(PointerEventData e) { if(pointer==e.pointerId)Move?.Invoke(e); }
        public void OnPointerUp(PointerEventData e)
        {
            if(pointer!=e.pointerId || e.button!=PointerEventData.InputButton.Left)return;
            pointer=null; Up?.Invoke(e);
        }
        public void OnEndDrag(PointerEventData e) { if(pointer==e.pointerId){pointer=null;Cancel?.Invoke();} }
        private void OnDisable() { pointer=null; Cancel?.Invoke(); }
    }
}
