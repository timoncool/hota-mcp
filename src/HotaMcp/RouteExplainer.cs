namespace HotaMcp;

/// Why the game lays no path to a cell. The game's own route stops in front of whatever stands in
/// the way and says nothing about it; a player looks at the screen and sees the stack sitting in
/// the passage. This walks the explored land the way the player's eye does — eight directions,
/// around blocked cells, never through an object's entrance — and names what the cheapest way
/// has to go through. It never prices a route: movement cost stays the game's own answer.
internal sealed class RouteExplainer(WindowsGame game,int player)
{
    // Objects that stand across a way and let the hero through only after a fight or a condition.
    private static readonly HashSet<int> Gates=[33,212,215,219];
    private const int Water=8,Monster=54,Hero=34;

    private (int Size,int Level,byte[] Tiles,byte[] Vision,Dictionary<int,int> Guard)? snapshot;

    // The level is read once per call of the bridge, however many targets it explains.
    private (int Size,int Level,byte[] Tiles,byte[] Vision,Dictionary<int,int> Guard)? Snapshot(int level)
    {
        if(snapshot is {} known&&known.Level==level)return known;
        uint setup=game.U32(0x699538)+0x1fb70;
        int size=game.I32(setup+0xd4);
        if(size<36||size>252)return null;
        int cells=size*size;
        byte[] tiles=game.Read(game.U32(setup+0xd0)+(uint)(level*cells*0x26),cells*0x26);
        byte[] vision=game.Read(game.U32(0x698a48)+(uint)(level*cells*2),cells*2);
        // A wandering stack guards its own cell and every land cell around it; stepping into that
        // ring is the fight, so the ring belongs to the stack.
        var guard=new Dictionary<int,int>();
        for(int i=0;i<cells;i++)
        {
            if((vision[i*2]&(1<<player))==0||(tiles[i*0x26+0xd]&16)==0||BitConverter.ToInt16(tiles,i*0x26+0x1e)!=Monster)continue;
            int mx=i%size,my=i/size;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {
                int nx=mx+dx,ny=my+dy;
                if(nx<0||ny<0||nx>=size||ny>=size)continue;
                int n=ny*size+nx;
                if(tiles[n*0x26+4]!=Water&&((tiles[n*0x26+0xd]&1)==0||n==i))guard.TryAdd(n,i);
            }
        }
        snapshot=(size,level,tiles,vision,guard);
        return snapshot;
    }

    /// The first obstacle of the last explained route, so targets locked behind the same stack
    /// can be grouped the way a player reads a guarded pocket of the map.
    public string? LastBlocker{get;internal set;}

    public string? WhyNoPath(Observation observation,int tx,int ty,int tz)
    {
        LastBlocker=null;
        var hero=observation.Hero;
        if(hero is null||hero.Position[2]!=tz||Snapshot(tz) is not {} map)return null;
        var (size,_,tiles,vision,guard)=map;
        bool Seen(int i)=>(vision[i*2]&(1<<player))!=0;
        int Land(int i)=>tiles[i*0x26+4];
        bool Blocked(int i)=>(tiles[i*0x26+0xd]&1)!=0;
        bool Entrance(int i)=>(tiles[i*0x26+0xd]&16)!=0;
        int Type(int i)=>BitConverter.ToInt16(tiles,i*0x26+0x1e);
        int Subtype(int i)=>BitConverter.ToInt16(tiles,i*0x26+0x22);
        var own=observation.Heroes.Where(h=>h.Position[2]==tz).Select(h=>h.Position[1]*size+h.Position[0]).ToHashSet();
        int start=hero.Position[1]*size+hero.Position[0],goal=ty*size+tx;
        if(Land(start)==Water)return null;
        if(!Seen(goal))return "клетка цели скрыта туманом войны";

        string Name(int i)=>Type(i) switch
        {
            Monster=>$"{GameReference.Creature(Subtype(i))} (бродячий отряд) на ({i%size},{i/size})",
            Hero=>$"чужой герой на ({i%size},{i/size})",
            _=>$"{GameReference.MapObject(Type(i),Subtype(i))} на ({i%size},{i/size})",
        };

        // Cheapest way first by the number of obstacles crossed, then by length.
        var cost=new Dictionary<int,(int Stops,int Steps)>{[start]=(0,0)};
        var from=new Dictionary<int,int>();
        var blocker=new Dictionary<int,int>();
        var queue=new PriorityQueue<int,(int,int)>();
        queue.Enqueue(start,(0,0));
        while(queue.TryDequeue(out int at,out var c))
        {
            if(cost[at]!=c)continue;
            if(at==goal)break;
            int ax=at%size,ay=at/size;
            int? ownGuard=guard.TryGetValue(at,out int g0)?g0:null;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {
                if(dx==0&&dy==0)continue;
                int nx=ax+dx,ny=ay+dy;
                if(nx<0||ny<0||nx>=size||ny>=size)continue;
                int n=ny*size+nx;
                if(!Seen(n)||Land(n)==Water)continue;
                bool entrance=Entrance(n);
                if(Blocked(n)&&!entrance)continue;
                int stops=c.Item1;int? stopAt=null;
                if(entrance&&n!=goal)
                {
                    int type=Type(n);
                    // An entrance is where the hero stops to visit; only a fight or a gate lets
                    // him on past it.
                    if(own.Contains(n))continue;
                    if(type==Monster||type==Hero||Gates.Contains(type)){stops++;stopAt=n;}
                    else continue;
                }
                else if(guard.TryGetValue(n,out int g)&&g!=ownGuard&&n!=goal){stops++;stopAt=g;}
                var next=(stops,c.Item2+1);
                if(cost.TryGetValue(n,out var known)&&known.CompareTo(next)<=0)continue;
                cost[n]=next;from[n]=at;
                if(stopAt is int s)blocker[n]=s;else blocker.Remove(n);
                queue.Enqueue(n,next);
            }
        }
        if(!cost.ContainsKey(goal))
            return "по разведанной суше пути нет: цель за водой, за скалами или за неразведанным туманом";
        var crossed=new List<int>();
        for(int at=goal;from.TryGetValue(at,out int prev);at=prev)
            if(blocker.TryGetValue(at,out int b)&&!crossed.Contains(b))crossed.Insert(0,b);
        if(crossed.Count==0)return null;
        LastBlocker=Name(crossed[0]);
        return "путь запирает "+string.Join(", затем ",crossed.Select(Name))+" — игра не прокладывает маршрут сквозь охрану";
    }
}
