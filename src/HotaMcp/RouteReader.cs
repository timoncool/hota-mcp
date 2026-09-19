namespace HotaMcp;

public sealed record RouteView(string State,int? MovementCost,int? RemainingMovement,int? Steps);

internal sealed class RouteReader(WindowsGame game,int player)
{
    public RouteView Read(Observation observation,MapObject target)
    {
        RouteView Unknown()=>new("not_available",null,null,null);
        var hero=observation.Hero;
        if(hero is null||observation.Screen!="adventure"||game.I32(0x69ccf4)!=player)return Unknown();
        uint finder=game.U32(0x6992d4),nodes=game.U32(finder+0x24),vision=game.U32(0x698a48);
        int size=game.I32(0x6783c8);
        if(size<36||size>252||game.I32(finder+8)!=hero.Movement||game.I32(finder+0x30)!=size||game.I32(finder+0x34)!=size)return Unknown();
        uint origin=(uint)(hero.Position[0]|(hero.Position[1]<<16)|(hero.Position[2]<<26));
        uint current=(uint)(target.X|(target.Y<<16)|(target.Z<<26));
        var seen=new HashSet<uint>();int steps=0,cost=0,remaining=hero.Movement;
        while(true)
        {
            int x=(int)(current&1023),y=(int)((current>>16)&1023),z=(int)((current>>26)&1);
            if(x>=size||y>=size||!seen.Add(current)||steps>512)return Unknown();
            uint index=(uint)((z*size+y)*size+x);
            if((game.Read(vision+index*2,1)[0]&(1<<player))==0)return Unknown();
            byte[] node=game.Read(nodes+index*0x1e,0x1e);
            if(BitConverter.ToUInt32(node)!=current)return Unknown();
            if(current==origin)
            {
                if(BitConverter.ToUInt16(node,0x18)!=0)return Unknown();
                break;
            }
            if(steps==0){cost=BitConverter.ToUInt16(node,0x18);remaining=BitConverter.ToUInt16(node,0x1c);}
            current=BitConverter.ToUInt32(node,8);steps++;
        }
        // Only the validated same-day layout; no invented multi-day estimates or terrain advice.
        if(cost>hero.Movement||remaining!=hero.Movement-cost)return Unknown();
        return new("reachable_today",cost,remaining,steps);
    }
}
