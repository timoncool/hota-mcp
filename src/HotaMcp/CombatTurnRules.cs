namespace HotaMcp;

internal static class CombatTurnRules
{
    public static bool OwnActor(int player,int firstOwner,int secondOwner,int activeSide)=>
        player is >=0 and <8&&activeSide is 0 or 1
        &&(activeSide==0?firstOwner==player:secondOwner==player);
}
