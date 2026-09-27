namespace HotaMcp;

internal static class TeamRelations
{
    // The first byte is the number of teams; the next eight bytes map colours to teams.
    // An absent colour is 255. Without teams, other players are opponents.
    public static bool AreAllies(ReadOnlySpan<byte> header,int player,int colour)=>
        header.Length>=9&&player is >=0 and <8&&colour is >=0 and <8&&colour!=player
        &&header[0] is >0 and <=8&&header[1+player]<8&&header[1+colour]<8
        &&header[1+player]==header[1+colour];
}
