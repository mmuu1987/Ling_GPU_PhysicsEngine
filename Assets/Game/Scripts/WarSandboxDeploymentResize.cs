using System;
using UnityEngine;
namespace MassEngine.Game
{
    [Flags] public enum DeploymentResizeHandle { None=0, MinX=1, MaxX=2, MinZ=4, MaxZ=8 }
    /// <summary>W=world Z; D=world X. Auto footprint is the sole authority after an explicit shape edit.</summary>
    public static class WarSandboxDeploymentResize
    {
        public const float Tolerance=.001f;
        public static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        public static bool TryFromSize(WarSandboxDeploymentEntry source, Vector3 center, Vector2 depthWidth,
            out WarSandboxDeploymentEntry result, out string error)
        {
            result=source;error=null;
            if(source.count<=0 || !Finite(center.x)||!Finite(center.y)||!Finite(center.z)||
                !Finite(depthWidth.x)||!Finite(depthWidth.y)||depthWidth.x<=0||depthWidth.y<=0)
            {error="人数、中心和尺寸必须为有效正数（中心可为负）。";return false;}
            double area=(double)depthWidth.x*depthWidth.y;float density=(float)(source.count/area),aspect=depthWidth.y/depthWidth.x;
            float floor=WarSandboxDeploymentEntry.MinimumDensity(source.template);
            if(density<.05f-1e-6f&&density>=floor*(1-1e-4f)&&aspect>=.1f-1e-6f&&aspect<=10f+1e-6f)
            {
                // Too sparse for the auto footprint (0.05 /m2 floor) but required by a large body: keep it explicit,
                // exactly as FitDensityToBody does. density/aspect fields stay as they were (still in range).
                result.center=center;result.manualSize=new Vector3(depthWidth.x,source.manualSize.y,depthWidth.y);return true;
            }
            if(density<.05f-1e-6f||density>SpawnConfig.PackingLimitPerSquareMeter+1e-6f||aspect<.1f-1e-6f||aspect>10f+1e-6f)
            {error="尺寸超出密度0.05–1.5或宽深比0.1–10的可表示范围。";return false;}
            result.center=center;result.density=Mathf.Clamp(density,.05f,SpawnConfig.PackingLimitPerSquareMeter);result.aspect=Mathf.Clamp(aspect,.1f,10);result.manualSize=Vector3.zero;
            if(Mathf.Abs(result.Size.x-depthWidth.x)>Tolerance || Mathf.Abs(result.Size.z-depthWidth.y)>Tolerance)
            {error="尺寸与自动占地转换不一致，未提交。";return false;}return true;
        }
        public static bool Fits(WarSandboxDeploymentEntry entry,float radius,float jitter,out string error)
        {
            error=null;var size=entry.Size;
            if(entry.count<=0||!Finite(radius)||radius<=0||!Finite(jitter)||!Finite(size.x)||!Finite(size.z)||size.x<=0||size.z<=0)
            {error="无法验证实际半径或阵型尺寸。";return false;}
            if(!LatticeFits(size,entry.count,radius,jitter)){error=NoRoom(-1,MaxLatticeCount(size,entry.count,radius,jitter),entry.count);return false;}return true;
        }
        /// <summary>Same rows/columns as DefaultSpawnModule; jitter taken off conservatively.</summary>
        public static bool LatticeFits(Vector3 size,int count,float radius,float jitter)
        {
            if(count<=1)return true;
            float a=Mathf.Max(.01f,size.z/Mathf.Max(.01f,size.x));int cols=Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count*a)),1,count),rows=Mathf.CeilToInt(count/(float)cols);
            float f=1-2*Mathf.Clamp(jitter,0,.08f);
            return !((rows>1 && size.x/(rows-1)*f+.0001f<2*radius)||(cols>1 && size.z/(cols-1)*f+.0001f<2*radius));
        }
        /// <summary>2026-10-17 (no unit cap): largest count up to <paramref name="count"/> whose lattice fits this footprint.</summary>
        public static int MaxLatticeCount(Vector3 size,int count,float radius,float jitter)
        {
            int lo=1,hi=Mathf.Max(1,count);
            while(lo<hi){int mid=lo+(hi-lo+1)/2;if(LatticeFits(size,mid,radius,jitter))lo=mid;else hi=mid-1;}
            while(lo>1&&!LatticeFits(size,lo,radius,jitter))lo--;
            return LatticeFits(size,lo,radius,jitter)?lo:0;
        }
        /// <summary>The one "not enough room" message: how many fit, never a fixed unit cap.</summary>
        public static string NoRoom(int index,int fit,int count)=>
            (index>=0?"编成 "+(index+1)+"：":"")+"格子不够：这块占地最多放 "+fit+" 个（当前 "+count+" 个）。请扩大占地或减少人数；不会自动减人数或缩模型。";
        /// <summary>Smallest uniform scale (1 = none) of the footprint after which the real lattice clears 2r once jitter
        /// is taken off. Uniform scaling keeps the aspect, so rows/cols do not change. At most ~6.5% per side.</summary>
        public static float LatticeGrowth(WarSandboxDeploymentEntry entry,float radius,float jitter)
        {
            var size=entry.Size;
            if(entry.count<=1||!Finite(radius)||radius<=0||!Finite(jitter)||!Finite(size.x)||!Finite(size.z)||size.x<=0||size.z<=0)return 1f;
            float a=Mathf.Max(.01f,size.z/Mathf.Max(.01f,size.x));int cols=Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(entry.count*a)),1,entry.count),rows=Mathf.CeilToInt(entry.count/(float)cols);
            float f=1-2*Mathf.Clamp(jitter,0,.08f),k=1f;
            if(rows>1)k=Mathf.Max(k,2*radius*(rows-1)/(f*size.x));
            if(cols>1)k=Mathf.Max(k,2*radius*(cols-1)/(f*size.z));
            return k>1f?k*1.0005f:1f;
        }
        public static bool TryResize(WarSandboxDeploymentEntry original,DeploymentResizeHandle handle,Vector2 delta,bool locked,
            float radius,float jitter,out WarSandboxDeploymentEntry result,out string error)
        {
            result=original;error=null;
            int x=(handle&DeploymentResizeHandle.MaxX)!=0?1:(handle&DeploymentResizeHandle.MinX)!=0?-1:0;
            int z=(handle&DeploymentResizeHandle.MaxZ)!=0?1:(handle&DeploymentResizeHandle.MinZ)!=0?-1:0;
            if((x==0&&z==0)||!Finite(delta.x)||!Finite(delta.y)){error="无效尺寸手柄或坐标。";return false;}
            var size=original.Size;float d=size.x,w=size.z;
            if(!Finite(d)||!Finite(w)||d<=0||w<=0||original.count<=0){error="原占地无效。";return false;}
            float nd=x==0?d:d+x*delta.x,nw=z==0?w:w+z*delta.y;bool crossed=nd<=0||nw<=0;
            double minArea=original.count/(double)SpawnConfig.PackingLimitPerSquareMeter,maxArea=original.count/(double)WarSandboxDeploymentEntry.MinimumDensity(original.template);
            if(locked)
            {
                float ratio=w/d;if(ratio<.1f||ratio>10){error="旧尺寸比例超出自动范围，不能锁比例转换。";return false;}
                float sx=nd/d,sz=nw/w;float factor=x==0?sz:z==0?sx:Mathf.Abs(sx-1)>=Mathf.Abs(sz-1)?sx:sz;
                factor=Mathf.Clamp(factor,(float)Math.Sqrt(minArea/(d*(double)w)),(float)Math.Sqrt(maxArea/(d*(double)w)));nd=d*factor;nw=w*factor;
            }
            else if(x==0 || z==0)
            {
                float lo=x!=0?Mathf.Max((float)(minArea/w),w/10):Mathf.Max((float)(minArea/d),d*.1f);
                float hi=x!=0?Mathf.Min((float)(maxArea/w),w/.1f):Mathf.Min((float)(maxArea/d),d*10);
                if(lo>hi){error="固定边与参数范围不相容，请用角手柄调整。";return false;}
                if(x!=0)nd=Mathf.Clamp(nd,lo,hi);else nw=Mathf.Clamp(nw,lo,hi);
            }
            else
            {
                nd=Mathf.Max(.001f,nd);nw=Mathf.Max(.001f,nw);float ratio=Mathf.Clamp(nw/nd,.1f,10);
                double area=Math.Max(minArea,Math.Min(maxArea,(double)nd*nw));nd=(float)Math.Sqrt(area/ratio);nw=nd*ratio;
            }
            // Crossing the opposite edge stops at a locally admissible minimum along this captured shape path,
            // rather than reflecting/inverting. Ordinary too-tight requests remain red and are rejected.
            if(crossed && Fits(original,radius,jitter,out _))
            {
                var target=new Vector2(nd,nw);float bad=0,good=1;bool found=false;
                for(int i=0;i<=64;i++)
                {
                    float t=i/64f;var s=Vector2.Lerp(target,new Vector2(d,w),t);
                    if(TryFromSize(original,original.center,s,out var probe,out _)&&Fits(probe,radius,jitter,out _)){good=t;found=true;break;}bad=t;
                }
                if(found){for(int i=0;i<18;i++){float mid=(bad+good)*.5f;var s=Vector2.Lerp(target,new Vector2(d,w),mid);if(TryFromSize(original,original.center,s,out var p,out _)&&Fits(p,radius,jitter,out _))good=mid;else bad=mid;}var v=Vector2.Lerp(target,new Vector2(d,w),good);nd=v.x;nw=v.y;}
            }
            // Opposite edge fixed; under Shift an edge's opposite midpoint is fixed. Corners keep the opposite corner.
            var center=original.center+new Vector3(x*(nd-d)*.5f,0,z*(nw-w)*.5f);
            return TryFromSize(original,center,new Vector2(nd,nw),out result,out error);
        }
    }
}


