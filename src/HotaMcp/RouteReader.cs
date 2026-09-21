namespace HotaMcp;

public sealed record RouteView(string State,int? MovementCost,int? RemainingMovement,int? Steps,string? Detail=null);

internal sealed class RouteReader(WindowsGame game,int player)
{
    // The route is the game's own path, the one it draws for the player when the cursor points at a
    // cell. The adapter walks that chain from the target back to the hero and refuses to publish
    // anything it cannot fully verify; Detail carries the honest reason for a refusal.
    //
    // Node layout, measured on the running game (stride 0x1e):
    //   +0x00 u16 x, +0x02 u16 y      the cell this node describes
    //   +0x08 u16 x, +0x0a u16 y      the previous cell on the path; equal to its own cell at the hero
    //   +0x18 u16                     movement spent to reach this cell
    //   +0x1c u16                     movement left on arrival
    public RouteView Read(Observation observation,MapObject target)
    {
        RouteView Unknown(string why)=>new("not_available",null,null,null,why);
        var hero=observation.Hero;
        if(hero is null||observation.Screen!="adventure"||game.I32(0x69ccf4)!=player)
            return Unknown("No active hero on the adventure map");
        uint finder=game.U32(0x6992d4),nodes=game.U32(finder+0x24),vision=game.U32(0x698a48);
        int size=game.I32(0x6783c8);
        if(size<36||size>252)return Unknown($"Unsupported map size {size}");
        int cached=game.I32(finder+8);
        if(cached!=hero.Movement)
            return Unknown($"The game's route cache is stale: it was built for {cached} movement points while the hero now has {hero.Movement}");
        if(game.I32(finder+0x30)!=size||game.I32(finder+0x34)!=size)
            return Unknown("The game's route cache grid does not match the map size");
        if(target.Z!=hero.Position[2])
            return Unknown("Target is on another map level; the game plans such routes only through the connecting object");

        int level=target.Z,x=target.X,y=target.Y;
        var seen=new HashSet<int>();
        int steps=0,cost=0,remaining=hero.Movement;
        while(true)
        {
            if(x<0||y<0||x>=size||y>=size)return Unknown("The route leaves the map");
            int index=(level*size+y)*size+x;
            if(!seen.Add(index)||steps>512)return Unknown("The route chain is broken or too long");
            if((game.Read(vision+(uint)index*2,1)[0]&(1<<player))==0)
                return Unknown("Part of the route lies in the fog of war");
            byte[] node=game.Read(nodes+(uint)index*0x1e,0x1e);
            int nodeX=BitConverter.ToUInt16(node,0),nodeY=BitConverter.ToUInt16(node,2);
            if(nodeX==0&&nodeY==0&&BitConverter.ToUInt16(node,8)==0&&BitConverter.ToUInt16(node,10)==0)
                return Unknown($"The game has not extended its route cache to ({x},{y},{level})");
            if(nodeX!=x||nodeY!=y)
                return Unknown($"A route cache node does not match its cell: step {steps} at ({x},{y},{level}) carries ({nodeX},{nodeY})");
            int fromX=BitConverter.ToUInt16(node,8),fromY=BitConverter.ToUInt16(node,10);
            if(steps==0){cost=BitConverter.ToUInt16(node,0x18);remaining=BitConverter.ToUInt16(node,0x1c);}
            // The hero's own cell is its own predecessor and costs nothing to stand on.
            if(fromX==x&&fromY==y)
            {
                if(x!=hero.Position[0]||y!=hero.Position[1])
                    return Unknown($"The route ends at ({x},{y}) instead of the hero's cell");
                if(BitConverter.ToUInt16(node,0x18)!=0)
                    return Unknown("The hero's own cell still has a pending step cost");
                break;
            }
            x=fromX;y=fromY;steps++;
        }
        if(steps==0)return Unknown("Target is the hero's own cell");
        // Only what the game itself worked out. A path longer than today's movement is the dashed
        // continuation the player also sees; it is reported as such, never as an invented estimate.
        if(cost>hero.Movement)return new("needs_more_days",cost,null,steps);
        if(remaining!=hero.Movement-cost)
            return Unknown($"The game's own arithmetic does not close: cost {cost}, left {remaining}, had {hero.Movement}");
        return new("reachable_today",cost,remaining,steps);
    }
}
