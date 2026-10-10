using System;
using UnityEngine;
namespace MassEngine.Game
{
    public enum MemberSelectionScope { WholeArmy, Pending, Local, Empty, Unavailable }
    /// <summary>Selection identity is NOT an order group. No command is issued by this class.</summary>
    public sealed class MemberSelectionState
    {
        public readonly struct Ticket : IEquatable<Ticket>
        {
            public readonly long Epoch, Request; public readonly int Team;
            public Ticket(long epoch,int team,long request){Epoch=epoch;Team=team;Request=request;}
            public bool Equals(Ticket other)=>Epoch==other.Epoch&&Team==other.Team&&Request==other.Request;
        }
        public long Epoch {get;} public int Team {get;private set;} public long Request {get;private set;}
        public int Version {get;private set;} public int Alive {get;private set;}
        public MemberSelectionScope Scope {get;private set;}=MemberSelectionScope.WholeArmy;
        public string Error {get;private set;} public int Count=>members.Length;
        private int[] members=Array.Empty<int>();
        public Ticket Current=>new Ticket(Epoch,Team,Request);
        public MemberSelectionState(long epoch,int team){Epoch=epoch;Team=team;}
        public Ticket Begin(){checked{Request++;Version++;}members=Array.Empty<int>();Scope=MemberSelectionScope.Pending;Error=null;return Current;}
        public void Clear(int team){checked{Request++;Version++;}Team=team;Alive=0;members=Array.Empty<int>();Scope=MemberSelectionScope.WholeArmy;Error=null;}
        public void Invalidate(string reason){Begin();Scope=MemberSelectionScope.Unavailable;Error=reason;}
        public bool Apply(Ticket ticket,int[] selected,int alive,bool whole=false)
        {
            if(!Current.Equals(ticket)||Scope==MemberSelectionScope.Unavailable)return false;
            if(selected==null||alive<selected.Length||alive<0)throw new ArgumentException("Invalid selection result");
            for(int i=0;i<selected.Length;i++)if(selected[i]<0||(i>0&&selected[i]<=selected[i-1]))throw new ArgumentException("Stable entity indices must be unique and ordered");
            bool changed=members.Length!=selected.Length;
            if(!changed)for(int i=0;i<members.Length;i++)if(members[i]!=selected[i]){changed=true;break;}
            if(changed)checked{Version++;}members=(int[])selected.Clone();Alive=alive;
            Scope=whole?MemberSelectionScope.WholeArmy:members.Length==0?MemberSelectionScope.Empty:MemberSelectionScope.Local;Error=null;return true;
        }
        public bool TrySnapshot(out int[] snapshot,out int version)
        {version=Version;snapshot=Scope==MemberSelectionScope.Local?(int[])members.Clone():Array.Empty<int>();return Scope==MemberSelectionScope.Local;}
    }
    public readonly struct MemberSelectionProjection
    {
        public readonly Matrix4x4 View, ViewProjection; public readonly Rect Viewport, Box;
        public readonly float Near, Far;
        public MemberSelectionProjection(Camera camera,Vector2 a,Vector2 b)
        {View=camera.worldToCameraMatrix;ViewProjection=camera.projectionMatrix*View;Viewport=camera.pixelRect;Near=camera.nearClipPlane;Far=camera.farClipPlane;Box=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));}
        public bool Contains(Vector3 groundCentre)
        {
            if(Viewport.width<=0||Viewport.height<=0||Box.width<=0||Box.height<=0)return false;
            Vector4 world=new Vector4(groundCentre.x,groundCentre.y,groundCentre.z,1),clip=ViewProjection*world;
            float depth=-(View*world).z;
            if(depth<Near||depth>Far||clip.w<=0||float.IsNaN(clip.w))return false;
            Vector2 uv=new Vector2(clip.x,clip.y)/clip.w*.5f+Vector2.one*.5f;
            Vector2 p=Viewport.position+Vector2.Scale(uv,Viewport.size);
            return uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1&&p.x>=Box.xMin&&p.x<=Box.xMax&&p.y>=Box.yMin&&p.y<=Box.yMax;
        }
    }
}
