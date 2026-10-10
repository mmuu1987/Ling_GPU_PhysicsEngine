using UnityEngine;
namespace MassEngine.Game
{
    public sealed partial class WarSandboxDeploymentHUD
    {
        private DeploymentResizeHandle resizeHandle;
        private WarSandboxDeploymentEntry resizePreview;
        private float resizeRadius,resizeJitter;
        private string resizeError;
        private static readonly DeploymentResizeHandle[] ResizeHandles={
            DeploymentResizeHandle.MinX|DeploymentResizeHandle.MaxZ,DeploymentResizeHandle.MaxX|DeploymentResizeHandle.MaxZ,
            DeploymentResizeHandle.MinX|DeploymentResizeHandle.MinZ,DeploymentResizeHandle.MaxX|DeploymentResizeHandle.MinZ,
            DeploymentResizeHandle.MinX,DeploymentResizeHandle.MaxX,DeploymentResizeHandle.MinZ,DeploymentResizeHandle.MaxZ};
        private static Vector2 HandlePoint(Rect r,DeploymentResizeHandle h)=>new Vector2(
            (h&DeploymentResizeHandle.MinX)!=0?r.xMin:(h&DeploymentResizeHandle.MaxX)!=0?r.xMax:r.center.x,
            (h&DeploymentResizeHandle.MaxZ)!=0?r.yMin:(h&DeploymentResizeHandle.MinZ)!=0?r.yMax:r.center.y);
        private void DrawResizeHandles(WarSandboxUGUI ui,Rect map,Vector2 world)
        {
            if(!spatialSelection||placing||deployment.Draft.Count==0)return;
            var r=MapBounds(deployment.Draft[selected].Bounds,world,map);float s=8/ui.Scale;
            foreach(var h in ResizeHandles)
            {
                var p=HandlePoint(r,h);p.x=Mathf.Clamp(p.x,map.xMin+s/2,map.xMax-s/2);p.y=Mathf.Clamp(p.y,map.yMin+s/2,map.yMax-s/2);
                ui.Panel("resize-handle-"+(int)h,new Rect(p.x-s/2,p.y-s/2,s,s),WarSandboxUGUI.Accent,false);
            }
        }
        private DeploymentResizeHandle HitResizeHandle(Vector2 point)
        {
            if(!spatialSelection||placing||deployment.Draft.Count==0)return DeploymentResizeHandle.None;
            var rect=MapBounds(deployment.Draft[selected].Bounds,translationWorld,translationMap);float hit=11/runtimeUi.Scale,best=hit*hit;var answer=DeploymentResizeHandle.None;
            if(new Rect(rect.center-rect.size*.25f,rect.size*.5f).Contains(point))return DeploymentResizeHandle.None; // retain a move target even for tiny blocks
            foreach(var h in ResizeHandles)
            {
                var p=HandlePoint(rect,h);float s=4/runtimeUi.Scale;
                p.x=Mathf.Clamp(p.x,translationMap.xMin+s,translationMap.xMax-s);p.y=Mathf.Clamp(p.y,translationMap.yMin+s,translationMap.yMax-s);
                float dist=(point-p).sqrMagnitude;if(dist<best){best=dist;answer=h;}
            }
            return answer;
        }
        private void ResizeFieldsPreview(WarSandboxDeploymentEntry e,bool preview)
        {
            if(runtimeUi==null)return;
            var r=MapBounds(preview?e.Bounds:(deployment.Draft!=null&&selected<deployment.Draft.Count?deployment.Draft[selected].Bounds:new Rect()),translationWorld,translationMap);
            float s=8/runtimeUi.Scale;
            foreach(var h in ResizeHandles){var hp=HandlePoint(r,h);hp.x=Mathf.Clamp(hp.x,translationMap.xMin+s/2,translationMap.xMax-s/2);hp.y=Mathf.Clamp(hp.y,translationMap.yMin+s/2,translationMap.yMax-s/2);runtimeUi.PreviewRect("resize-handle-"+(int)h,new Rect(hp.x-s/2,hp.y-s/2,s,s));}
            runtimeUi.PreviewField("input-x",preview?Format(e.center.x):x,preview);
            runtimeUi.PreviewField("input-z",preview?Format(e.center.z):z,preview);
            runtimeUi.PreviewField("input-density",preview?Format(e.density):density,preview);
            runtimeUi.PreviewField("input-aspect",preview?Format(e.aspect):aspect,preview);
            runtimeUi.PreviewField("input-depth",preview?Format(e.Size.x):depth,preview);
            runtimeUi.PreviewField("input-width",preview?Format(e.Size.z):width,preview);
            runtimeUi.PreviewLabel("resize-values",preview?
                "预览（未提交）\nD(X) "+e.Size.x.ToString("0.##")+" · W(Z) "+e.Size.z.ToString("0.##")+"\n密度 "+e.density.ToString("0.###")+" · 比例 "+e.aspect.ToString("0.###")+
                (resizeError==null?"\n松手校验；Shift锁比例":"\n"+resizeError):"边/角调整尺寸 · Shift锁比例\n松手校验，人数和体型不变\n编辑尺寸后切为自动占地");
        }
    }
}
