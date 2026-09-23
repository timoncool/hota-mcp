namespace HotaMcp;

/// Which colour this service plays. A hotseat game has several people at one keyboard and the
/// bridge must act for exactly one of them, so the colour is set, not assumed: `--player` on the
/// command line, otherwise `Player=` in settings.ini of the state directory, otherwise red.
internal static class PlayerSetting
{
    private static readonly string[][] Names=
    [
        ["0","красный","red"],["1","синий","blue"],["2","коричневый","tan"],["3","зелёный","зеленый","green"],
        ["4","оранжевый","orange"],["5","фиолетовый","purple"],["6","бирюзовый","teal"],["7","розовый","pink"],
    ];

    public static int Parse(string value)
    {
        string wanted=value.Trim().ToLowerInvariant();
        for(int i=0;i<Names.Length;i++)if(Names[i].Contains(wanted))return i;
        throw new InvalidOperationException($"Unknown player colour «{value}»: use 0-7 or a colour name (красный, синий, …)");
    }

    public static int Read(string directory,string? commandLine)
    {
        if(commandLine is not null)return Parse(commandLine);
        string file=Path.Combine(directory,"settings.ini");
        if(!File.Exists(file))return 0;
        var line=File.ReadAllLines(file).Select(l=>l.Trim()).FirstOrDefault(l=>l.StartsWith("Player=",StringComparison.OrdinalIgnoreCase));
        return line is null?0:Parse(line["Player=".Length..]);
    }
}
