namespace HotaMcp;

public sealed record BuildCheck(string Name,bool Passed,string Detail);
public sealed record BuildFingerprint(string Status,BuildCheck[] Checks)
{
    public bool Usable=>Status=="compatible";
    public string Summary=>Usable
        ?"Every structure this adapter uses was found in place"
        :"Structures this adapter needs were not found: "
            +string.Join("; ",Checks.Where(c=>!c.Passed).Select(c=>c.Name+" — "+c.Detail));
}

/// <summary>
/// Decides whether the running game is one this adapter can drive.
///
/// File hashes answer the wrong question: HotA and HD Mod update themselves, so the hashes move on
/// every release while the structures the adapter reads usually do not. What matters is whether those
/// structures are where the adapter expects them, so that is what is checked.
///
/// Probes read memory only. Every command additionally revalidates its own target structure before
/// dispatch, so a wrong verdict here still cannot turn into a blind write.
/// </summary>
internal static class BuildProfile
{
    /// Dialog classes the screen reader can name. The active dialog must be one of them, otherwise
    /// the whole reading layer is addressing something else.
    private static readonly uint[] ScreenVtables=
    [
        0x640c5c,0x643990,0x63d46c,0x641ddc,0x63d528,0x63db40,0x63ff60,0x63e6d8,0x641cbc,
        0x63a5e4,0x642478,0x64373c,0x6437b0,0x643954,0x643c24,0x63eae8,0x642438,
    ];

    public static BuildFingerprint Probe(WindowsGame game)
    {
        var checks=new List<BuildCheck>();

        Check(checks,"window manager",()=>
        {
            uint manager=game.U32(0x6992d0);
            if(manager<0x10000)throw new InvalidOperationException("manager pointer is not a pointer");
            uint dialog=game.U32(manager+0x54);
            // The score screen at the end of a game has no window at all; that is a screen of the
            // game, not the intro video that plays before one.
            if(dialog<0x10000&&GameReader.GameEnded(game))return "no active dialog: the score screen of a finished game";
            if(dialog<0x10000)throw new InvalidOperationException("no active dialog");
            uint vtable=game.U32(dialog);
            // A dialog class the screen reader has no name for is an unsupported screen, not a
            // different build: the layout is still this one. Only a class that cannot be a class
            // at all says the manager is being read wrongly.
            // Dialogs the expansion adds live in HotA.dll, which is loaded well above the base
            // game image, so their class pointer is far outside it and moves between runs. Such a
            // class is checked by what it points at — a table whose first entry is code — instead
            // of by the address it happens to have.
            if(vtable<0x400000||vtable>0x700000&&game.U32(vtable)<0x400000)
                throw new InvalidOperationException($"active dialog class 0x{vtable:x} is not a class pointer");
            return ScreenVtables.Contains(vtable)
                ?$"active dialog class 0x{vtable:x}"
                :$"active dialog class 0x{vtable:x} is a screen this adapter does not name yet";
        });

        Check(checks,"surface geometry",()=>
        {
            uint surface=game.U32(game.U32(0x6992d0)+0x40);
            int width=game.I32(surface+0x24),height=game.I32(surface+0x28);
            if(width<640||width>8192||height<480||height>8192)
                throw new InvalidOperationException($"reported {width}x{height}");
            return $"{width}x{height}";
        });

        Check(checks,"hero array addressing",()=>
        {
            // The adapter derives the hero array offset from this instruction sequence rather than
            // hardcoding it, so the sequence itself is the contract.
            byte[] code=game.Read(0x4317e1,19);
            if(!code.AsSpan(0,13).SequenceEqual(Convert.FromHexString("8BC2C1E00603C28D04C08D8441"))
                ||code[17]!=0x5d||code[18]!=0xc2)
                throw new InvalidOperationException("instruction sequence differs");
            return $"record stride 0x492, offset 0x{BitConverter.ToUInt32(code,13):x}";
        });

        Check(checks,"town entry addressing",()=>
        {
            if(BitConverter.ToUInt16(game.Read(0x4081bd,2))!=0x828b)
                throw new InvalidOperationException("adventure town handler differs");
            return $"town array offset 0x{BitConverter.ToUInt32(game.Read(0x4081bf,4)):x}";
        });

        Check(checks,"manager list",()=>
        {
            uint executive=game.U32(0x699550);
            if(executive<0x10000)throw new InvalidOperationException("executive pointer is not a pointer");
            uint current=game.U32(executive);
            int count=0;
            var seen=new HashSet<uint>();
            while(current>=0x10000&&seen.Add(current)&&count<32){count++;current=game.U32(current+4);}
            if(count==0)throw new InvalidOperationException("manager list is empty");
            return $"{count} managers";
        });

        return new(checks.All(c=>c.Passed)?"compatible":"incompatible",checks.ToArray());
    }

    private static void Check(List<BuildCheck> checks,string name,Func<string> probe)
    {
        try{checks.Add(new(name,true,probe()));}
        catch(Exception e)when(e is InvalidOperationException or OverflowException or ArgumentException)
        {checks.Add(new(name,false,e.Message));}
    }
}
