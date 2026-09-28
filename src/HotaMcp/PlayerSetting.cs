namespace HotaMcp;

/// Colour names as a controller or a script says them: 0-7, Russian or English. Which colour a
/// request acts for is decided per request by GameSession from the game itself.
internal static class PlayerSetting
{
    public const int EverySide=-1;

    private static readonly string[][] Names=
    [
        ["0","красный","red"],["1","синий","blue"],["2","коричневый","tan"],["3","зелёный","зеленый","green"],
        ["4","оранжевый","orange"],["5","фиолетовый","purple"],["6","бирюзовый","teal"],["7","розовый","pink"],
    ];

    public static int Parse(string value)
    {
        string wanted=value.Trim().ToLowerInvariant();
        if(wanted is "все" or "all")return EverySide;
        for(int i=0;i<Names.Length;i++)if(Names[i].Contains(wanted))return i;
        throw new InvalidOperationException($"Unknown player colour «{value}»: use 0-7, a colour name (красный, синий, …) or все");
    }
}
