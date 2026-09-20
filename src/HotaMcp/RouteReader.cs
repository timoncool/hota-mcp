namespace HotaMcp;

public sealed record RouteView(string State,int? MovementCost,int? RemainingMovement,int? Steps,string? Detail=null);

internal sealed class RouteReader(WindowsGame game,int player)
{
    // The route is the game's own same-day path: the adapter walks the pathfinder chain the game
    // built from the cursor position and refuses to publish anything it cannot fully verify.
    // Detail is the honest reason for a refusal, so the agent never has to guess.
    public RouteView Read(Observation observation,MapObject target)
    {
        RouteView Unknown(string why)=>new("not_available",null,null,null,why);
        var hero=observation.Hero;
        if(hero is null||observation.Screen!="adventure"||game.I32(0x69ccf4)!=player)return Unknown("No active hero on the adventure map");
        uint finder=game.U32(0x6992d4),nodes=game.U32(finder+0x24),vision=game.U32(0x698a48);
        int size=game.I32(0x6783c8);
        if(size<36||size>252)return Unknown($"Unsupported map size {size}");
        int cached=game.I32(finder+8);
        if(cached!=hero.Movement)return Unknown($"The game's route cache is stale: it was built for {cached} movement points while the hero now has {hero.Movement}");
        if(game.I32(finder+0x30)!=size||game.I32(finder+0x34)!=size)return Unknown("The game's route cache grid does not match the map size");
        uint origin=(uint)(hero.Position[0]|(hero.Position[1]<<16)|(hero.Position[2]<<26));
        uint current=(uint)(target.X|(target.Y<<16)|(target.Z<<26));
        // Cells pack as x in bits 0..9, y in 16..25, z in bit 26; the remaining bits carry node
        // flags, so coordinates are compared through this mask only.
        const uint cellMask=0x0400FFFF;
        var seen=new HashSet<uint>();int steps=0,cost=0,remaining=hero.Movement;
        while(true)
        {
            int x=(int)(current&1023),y=(int)((current>>16)&1023),z=(int)((current>>26)&1);
            if(x>=size||y>=size||!seen.Add(current)||steps>512)return Unknown("The route chain is broken or too long");
            uint index=(uint)((z*size+y)*size+x);
            if((game.Read(vision+index*2,1)[0]&(1<<player))==0)return Unknown("Part of the route lies in the fog of war");
            byte[] node=game.Read(nodes+index*0x1e,0x1e);
            uint held=BitConverter.ToUInt32(node)&cellMask;
            if(BitConverter.ToUInt32(node)==0)return Unknown($"The game has not extended its route cache to ({x},{y},{z})");
            if(held!=current)return Unknown($"A route cache node does not match its cell: step {steps} at ({x},{y},{z}) carries ({held&1023},{(held>>16)&1023},{(held>>26)&1})");
            if(current==origin)
            {
                if(BitConverter.ToUInt16(node,0x18)!=0)return Unknown("The hero's own cell still has a pending step cost");
                break;
            }
            if(steps==0){cost=BitConverter.ToUInt16(node,0x18);remaining=BitConverter.ToUInt16(node,0x1c);}
            current=BitConverter.ToUInt32(node,8)&cellMask;steps++;
        }
        // Only the validated same-day layout; no invented multi-day estimates or terrain advice.
        if(cost>hero.Movement||remaining!=hero.Movement-cost)return Unknown("The route does not fit the remaining movement of this day");
        return new("reachable_today",cost,remaining,steps);
    }
}
