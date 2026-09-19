namespace HotaMcp;

public sealed record MapObject(int X,int Y,int Z,int Type,string Kind);
public sealed record MapView(string Revision,int X,int Y,int Z,int Width,int Height,string[] Terrain,string[] Roads,
    string[] Blocked,List<MapObject> Objects,string Legend);
public sealed record TileInspection(int X,int Y,int Z,string? Hint,Observation Observation);
internal sealed record Viewport(int X,int Y,int Width,int Height,int MapX,int MapY,int Z);

internal sealed class MapReader(WindowsGame game,int player)
{
    private static readonly Dictionary<int,string> KnownObjects=new()
    {
        [12]="campfire",[30]="fountain_of_fortune",[32]="garden_of_revelation",[54]="creatures",
        [64]="rally_flag",[79]="resource",[98]="town",[101]="treasure_chest"
    };
    private (int Size,uint Tiles,uint Vision) Context(Observation observation)
    {
        if(observation.Screen!="adventure"||observation.Player!=player||game.I32(0x69ccf4)!=player)
            throw new InvalidOperationException("Map requires your active adventure screen");
        uint setup=game.U32(0x699538)+0x1fb70;
        if(game.U32(game.U32(0x6992b8)+0x5c)!=setup)throw new InvalidOperationException("Map layout mismatch");
        int size=game.I32(setup+0xd4);
        if(size!=game.I32(0x6783c8)||size<36||size>252)throw new InvalidOperationException("Invalid map dimensions");
        return(size,game.U32(setup+0xd0),game.U32(0x698a48));
    }
    private uint VisibleTile(Observation observation,int x,int y,int z)
    {
        var context=Context(observation);
        int levels=game.Read(game.U32(0x699538)+0x1fc48,1)[0]+1;
        if(x<0||y<0||x>=context.Size||y>=context.Size||z<0||z>=levels||levels>2)
            throw new InvalidOperationException("Map coordinates out of bounds");
        uint index=checked((uint)((z*context.Size+y)*context.Size+x));
        if((game.Read(context.Vision+index*2,1)[0]&(1<<player))==0)
            throw new InvalidOperationException("Tile is hidden from this player");
        return checked(context.Tiles+index*0x26);
    }
    public MapView Read(Observation observation,int x,int y,int z,int radius)
    {
        var context=Context(observation);
        if(radius<0||radius>12)throw new InvalidOperationException("Radius must be between 0 and 12");
        _=VisibleTile(observation,x,y,z);
        int left=Math.Max(0,x-radius),top=Math.Max(0,y-radius),right=Math.Min(context.Size-1,x+radius),bottom=Math.Min(context.Size-1,y+radius);
        var terrain=new List<string>();var roads=new List<string>();var blocked=new List<string>();var objects=new List<MapObject>();
        for(int yy=top;yy<=bottom;yy++)
        {
            string tr="",rr="",br="";
            for(int xx=left;xx<=right;xx++)
            {
                uint index=checked((uint)((z*context.Size+yy)*context.Size+xx));
                if((game.Read(context.Vision+index*2,1)[0]&(1<<player))==0){tr+="?";rr+="?";br+="?";continue;}
                uint tile=checked(context.Tiles+index*0x26);
                byte land=game.Read(tile+4,1)[0],road=game.Read(tile+8,1)[0],access=game.Read(tile+0xd,1)[0];
                if(land>11||road>3)throw new InvalidOperationException("Terrain layout not supported");
                tr+="dgsnrluwvhax"[land];rr+=(char)('0'+road);br+=(access&1)!=0?'#':'.';
                int type=BitConverter.ToInt16(game.Read(tile+0x1e,2));
                // Publish only validated visible categories at their entrance. No setup, counts, events or grail.
                if((access&16)!=0&&KnownObjects.TryGetValue(type,out var kind))objects.Add(new(xx,yy,z,type,kind));
            }
            terrain.Add(tr);roads.Add(rr);blocked.Add(br);
        }
        return new(observation.Revision,left,top,z,right-left+1,bottom-top+1,terrain.ToArray(),roads.ToArray(),blocked.ToArray(),objects,
            "? hidden; terrain d dirt,g sand,s grass,n snow,r swamp,l rough,u subterranean,w lava,v water,h rock,a highlands,x wasteland; roads 0 none,1 dirt,2 gravel,3 cobblestone; # terrain blocked,. not terrain-blocked (not a path guarantee)");
    }
    public (int X,int Y) ScreenPoint(Observation observation,int x,int y,int z)
    {
        _=VisibleTile(observation,x,y,z);
        var viewport=GetViewport();
        int sx=viewport.X+(x-viewport.MapX)*32+16,sy=viewport.Y+(y-viewport.MapY)*32+16;
        if(z!=viewport.Z||sx<viewport.X+16||sy<viewport.Y+16||sx>=viewport.X+viewport.Width-16||sy>=viewport.Y+viewport.Height-16)
            throw new InvalidOperationException("Tile is outside the current viewport; camera control required");
        return(sx,sy);
    }
    public void ValidateTarget(Observation observation,MapObject target)
    {
        uint tile=VisibleTile(observation,target.X,target.Y,target.Z);
        if(BitConverter.ToInt16(game.Read(tile+0x1e,2))!=target.Type||(game.Read(tile+0xd,1)[0]&16)==0)
            throw new InvalidOperationException("Target changed; request nearby_targets again");
    }
    public void VerifyMouse(int x,int y,int z)
    {
        uint packed=game.U32(game.U32(0x6992b8)+0xe8);
        if((packed&1023)!=x||((packed>>16)&1023)!=y||((packed>>26)&1)!=z)
            throw new InvalidOperationException("Game did not confirm target cell; no click sent");
    }
    private Viewport GetViewport()
    {
        uint adventure=game.U32(0x6992b8),dlg=game.U32(adventure+0x44),start=game.U32(dlg+0x34),end=game.U32(dlg+0x38);
        if(end<start||(end-start)%4!=0||end-start>8192)throw new InvalidOperationException("Invalid viewport layout");
        var candidates=new List<Viewport>();uint camera=game.U32(adventure+0xe4);
        for(uint pos=start;pos<end;pos+=4)
        {
            byte[] b=game.Read(game.U32(pos),0x20);
            if(BitConverter.ToUInt16(b,0x10)!=0)continue;
            if(BitConverter.ToUInt32(b,4)!=dlg||BitConverter.ToUInt32(b)!=6535716)throw new InvalidOperationException("Viewport class changed");
            candidates.Add(new(BitConverter.ToInt16(b,0x18),BitConverter.ToInt16(b,0x1a),BitConverter.ToUInt16(b,0x1c),BitConverter.ToUInt16(b,0x1e),
                (int)(camera&1023),(int)((camera>>16)&1023),(int)((camera>>26)&1)));
        }
        return candidates.Single();
    }
    public object Diagnostic(Observation observation)
    {
        if(observation.Screen!="adventure"||observation.Player!=player||observation.Hero is null)
            throw new InvalidOperationException("Map diagnostic requires own hero in adventure screen");
        uint main=game.U32(0x699538),adventure=game.U32(0x6992b8),setup=main+0x1fb70;
        if(game.U32(adventure+0x5c)!=setup)throw new InvalidOperationException("Map layout differs from reference");
        int size=game.I32(setup+0xd4);
        if(size!=game.I32(0x6783c8)||size<36||size>252)throw new InvalidOperationException("Map size mismatch");
        uint tiles=game.U32(setup+0xd0),vision=game.U32(0x698a48);
        int[] h=observation.Hero.Position;
        var visible=new List<object>();
        var mask=new List<string>();
        for(int y=Math.Max(0,h[1]-7);y<=Math.Min(size-1,h[1]+7);y++)
        {
            string row="";
            for(int x=Math.Max(0,h[0]-7);x<=Math.Min(size-1,h[0]+7);x++)
            {
                uint index=checked((uint)((h[2]*size+y)*size+x));
                bool known=(game.Read(vision+index*2,1)[0]&(1<<player))!=0;
                row+=known?".":"?";
                if(!known)continue;
                uint tile=tiles+index*0x26;
                int type=BitConverter.ToInt16(game.Read(tile+0x1e,2));
                // Invisible events and buried items must not become observable through a map reader.
                if(type is 26 or 36)type=-1;
                visible.Add(new{x,y,z=h[2],terrain=game.Read(tile+4,1)[0],road=game.Read(tile+8,1)[0],type});
            }
            mask.Add(row);
        }
        uint camera=game.U32(adventure+0xe4),mouse=game.U32(adventure+0xe8);
        uint dlg=game.U32(adventure+0x44),start=game.U32(dlg+0x34),end=game.U32(dlg+0x38);
        var viewport=new List<object>();
        if(end<start||end-start>8192)throw new InvalidOperationException("Invalid adventure UI");
        for(uint pos=start;pos<end;pos+=4)
        {
            uint item=game.U32(pos);byte[] b=game.Read(item,0x20);
            if(BitConverter.ToUInt16(b,0x10)==0)viewport.Add(new{vtable=BitConverter.ToUInt32(b),
                x=BitConverter.ToInt16(b,0x18),y=BitConverter.ToInt16(b,0x1a),width=BitConverter.ToUInt16(b,0x1c),height=BitConverter.ToUInt16(b,0x1e)});
        }
        uint finder=game.U32(0x6992d4),nodes=game.U32(finder+0x24);
        var movement=new List<object>();
        var region=Read(observation,h[0],h[1],h[2],7);
        if(game.I32(finder+0x30)==size&&game.I32(finder+0x34)==size)
        {
            foreach(var target in region.Objects)
            {
                uint index=checked((uint)((target.Z*size+target.Y)*size+target.X));
                byte[] node=game.Read(nodes+index*0x1e,0x1e);
                movement.Add(new{target,packed=BitConverter.ToUInt32(node),flags=BitConverter.ToUInt32(node,4),
                    cost=BitConverter.ToUInt16(node,0x18),cost2=BitConverter.ToUInt16(node,0x1a),raw=Convert.ToHexString(node)});
            }
        }
        return new{size,hero=h,camera,mouse,viewport,mask,visible,movement,
            available=game.I32(finder+8),maxLand=game.I32(finder+12),nodeWidth=game.I32(finder+0x30),nodeHeight=game.I32(finder+0x34)};
    }
}
