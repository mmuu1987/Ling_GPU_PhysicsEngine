using System;
namespace MassEngine.Game
{
    /// <summary>Frozen player intent. A zero/pending/error selection NEVER means the whole army.</summary>
    public sealed class PlayerCommandScope
    {
        public MemberSelectionScope Scope {get;} public int Team {get;} public long Epoch {get;} public long Request {get;} public int Version {get;}
        private readonly int[] members;
        public int Count=>members.Length;
        public PlayerCommandScope(MemberSelectionScope scope,int team,long epoch,long request,int version,int[] ids)
        {Scope=scope;Team=team;Epoch=epoch;Request=request;Version=version;members=ids==null?Array.Empty<int>():(int[])ids.Clone();}
        public int[] CopyMembers()=>(int[])members.Clone();
        public bool SameIntent(PlayerCommandScope other)=>other!=null&&Scope==other.Scope&&Team==other.Team&&Epoch==other.Epoch&&Request==other.Request&&Version==other.Version;
        public bool Allows(ArmyOrderType kind,bool append,out string error)
        {
            error=null;
            if(Team<0){error="请先选择军团";return false;}
            if(kind!=ArmyOrderType.Move&&kind!=ArmyOrderType.Hold&&kind!=ArmyOrderType.Attack&&kind!=ArmyOrderType.Retreat){error="不支持的命令";return false;}
            if(Scope!=MemberSelectionScope.WholeArmy&&Scope!=MemberSelectionScope.Local){error=Scope==MemberSelectionScope.Empty?"零选中：请重新框选，或明确选择整军团":Scope==MemberSelectionScope.Pending?"选区尚未确认，请稍候":"选区不可用，请重新框选";return false;}
            if(Scope==MemberSelectionScope.Local&&(Count==0||Count>4096)){error=Count==0?"局部选区为空":"局部命令每次最多4096成员；未截断或回退整军团";return false;}
            if(Scope==MemberSelectionScope.Local&&append){error="局部移动暂不支持追加航点；请使用单目标移动";return false;}
            return true;
        }
    }
}
