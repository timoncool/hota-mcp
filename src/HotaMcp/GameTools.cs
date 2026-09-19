using System.ComponentModel;
using ModelContextProtocol.Server;

namespace HotaMcp;

[McpServerToolType]
public sealed class GameTools(IGameEndpoint endpoint)
{
    [McpServerTool,Description("Read supported capabilities and current development limitations. No game action.")]
    public Task<object> GameStatus(CancellationToken cancellationToken)=>endpoint.Status(cancellationToken);

    [McpServerTool,Description("Observe your assigned player's own hero/resources and supported active UI. Returns a revision required for actions. Unknown screens and wrong-player contexts are denied.")]
    public Task<Observation> Observe(CancellationToken cancellationToken)=>endpoint.Observe(cancellationToken);

    [McpServerTool,Description("Click a validated UI action from the latest observation. Currently only open system options and return to adventure are implemented. Use the SAME operationId for retries; never retry an uncertain action with a new ID.")]
    public Task<OperationResult> ClickUi(string operationId,string revision,string element,CancellationToken cancellationToken)
        =>endpoint.Click(new(operationId,revision,element),cancellationToken);

    [McpServerTool,Description("Read this controller's recent action results and plan updates. Contains no opponent history.")]
    public Task<object> ReadJournal(int limit,CancellationToken cancellationToken)=>endpoint.Journal(limit,cancellationToken);

    [McpServerTool,Description("Read or save your concise strategy, goals and next steps. Pass null to read. A plan does not execute actions or change the game.")]
    public Task<object> Plan(string? value,CancellationToken cancellationToken)=>endpoint.Plan(value,cancellationToken);
}
