namespace HotaMcp;

internal static class OpponentTurnObservation
{
    // Preserve the observe response shape without reading or revealing the other side's screen.
    public static Observation Waiting(int player)=>new("",player,[],[],null,"waiting",0,0,[])
    {
        Brief=["Ожидание своего хода. Следи за переходом через wait_for_turn."]
    };

    public static OperationResult AfterHandoff(OperationResult result,int player)=>new(result.Status,
        "Ход передан другому игроку; его экран скрыт.",Waiting(player));
}
