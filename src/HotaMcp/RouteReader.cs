namespace HotaMcp;

public sealed record RouteView(string State,int? MovementCost,int? RemainingMovement,int? Steps,string? Detail=null)
{
    /// Where the hero stands at the end of today's movement along this route — the last green arrow
    /// the game draws — when the whole route takes longer than today.
    public int[]? StopsToday {get;init;}
    /// Full days of movement the route takes at the hero's full daily movement, today counted.
    public int? Days {get;init;}
}

internal sealed class RouteReader(WindowsGame game,int player)
{
    // The route is the game's own path, the one it draws for the player when the cursor points at a
    // cell. The adapter walks that chain from the target back to the hero and refuses to publish
    // anything it cannot fully verify; Detail carries the honest reason for a refusal.
    //
    // Node layout, measured on the running game (stride 0x1e):
    //   +0x00 byte x, +0x02 byte y    the cell this node describes; +0x01 and +0x03 carry flags
    //   +0x08 byte x, +0x0a byte y    the previous cell on the path; equal to its own cell at the hero
    //   +0x18 u16                     movement spent to reach this cell
    //   +0x1c u16                     movement left on arrival
    // The cache covers the whole map and is rebuilt by the game whenever the hero's movement
    // changes, so no cursor work is needed to make a route exist.
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
        int[]? today=null;
        while(true)
        {
            if(x<0||y<0||x>=size||y>=size)return Unknown("The route leaves the map");
            // The route table is one level wide — the hero's — while the fog plane holds both levels.
            int cell=y*size+x,index=(level*size+y)*size+x;
            if(!seen.Add(cell)||steps>512)return Unknown("The route chain is broken or too long");
            if((game.Read(vision+(uint)index*2,1)[0]&(1<<player))==0)
                return Unknown("Part of the route lies in the fog of war");
            byte[] node=game.Read(nodes+(uint)cell*0x1e,0x1e);
            int nodeX=node[0],nodeY=node[2];
            if(node.All(b=>b==0))
                return steps==0&&Planned(finder,x,y,level,hero) is RouteView planned?planned
                    :Unknown($"The game found no path to ({x},{y},{level})");
            if(nodeX!=x||nodeY!=y)
                return Unknown($"A route cache node does not match its cell: step {steps} at ({x},{y},{level}) carries ({nodeX},{nodeY})");
            int fromX=node[8],fromY=node[10];
            if(steps==0){cost=BitConverter.ToUInt16(node,0x18);remaining=BitConverter.ToUInt16(node,0x1c);}
            // Walking back from the target, the first cell the hero reaches within today's points
            // is where the green arrows end.
            if(today is null&&BitConverter.ToUInt16(node,0x18)<=hero.Movement)today=[x,y,level];
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
        if(cost>hero.Movement)return Later(cost,steps,hero) with{StopsToday=today};
        if(remaining!=hero.Movement-cost)
            return Unknown($"The game's own arithmetic does not close: cost {cost}, left {remaining}, had {hero.Movement}");
        return new("reachable_today",cost,remaining,steps);
    }

    /// A route longer than today: its cost and the days of movement it takes, today counted.
    private static RouteView Later(int cost,int? steps,HeroView hero)=>
        new("needs_more_days",cost,null,steps){Days=hero.MaxMovement>0?1+(int)Math.Ceiling((cost-hero.Movement)/(double)hero.MaxMovement):null};

    /// Underground the game fills no whole-map table: the route to the destination it has just
    /// planned lives only in the short list at +0x3C..+0x40, whose nodes carry the level as bit
    /// 10 of y. The destination's own node there gives the cost and what is left.
    private RouteView? Planned(uint finder,int x,int y,int level,HeroView hero)
    {
        // The list keeps the nodes of whatever was searched last; only the destination planned
        // right now is this route.
        if(!hero.PlannedDestination.SequenceEqual(new[]{x,y,level}))return null;
        uint start=game.U32(finder+0x3c),end=game.U32(finder+0x40);
        if(start==0||end<=start||(end-start)%0x1e!=0||end-start>0x1e*256)return null;
        byte[] list=game.Read(start,(int)(end-start));
        for(int i=0;i<list.Length;i+=0x1e)
        {
            int nx=BitConverter.ToUInt16(list,i),ny=BitConverter.ToUInt16(list,i+2);
            if(nx!=x||(ny&0x3ff)!=y||(ny>>10&1)!=level)continue;
            int cost=BitConverter.ToUInt16(list,i+0x18),left=BitConverter.ToUInt16(list,i+0x1c);
            if(cost>hero.Movement)return Later(cost,null,hero);
            if(left!=hero.Movement-cost)return null;
            return new("reachable_today",cost,left,null);
        }
        return null;
    }
}
