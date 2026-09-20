using System.ComponentModel;
using ModelContextProtocol.Server;

namespace HotaMcp;

[McpServerToolType]
public sealed class GameTools(IGameEndpoint endpoint)
{
    [McpServerTool,Description("Move the selected own hero onto a visible creature stack and start the battle deliberately. Use only after observing the situation; the ordinary move_to now refuses creature cells, because stepping there fights. The game decides the outcome; uncertain is not permission to retry under another operation ID.")]
    public Task<OperationResult> AttackTarget(string operationId,string revision,string targetId,CancellationToken cancellationToken)=>endpoint.Attack(new(operationId,revision,targetId),cancellationToken);

    [McpServerTool,Description("Move the selected own hero toward a currently visible target ID from nearby_targets using the ordinary game route and move handlers. May collect the resource, open a dialog or encounter enemies. Experimental incomplete adapter: uncertain is not permission to retry under another operation ID. No coordinates or hidden data required.")]
    public Task<OperationResult> MoveTo(string operationId,string revision,string targetId,CancellationToken cancellationToken)=>endpoint.Move(new(operationId,revision,targetId),cancellationToken);
    [McpServerTool,Description("Move the selected own hero to an explicit map cell (x,y,z) using the game's own route planning and move command. Use for exploration and for reaching cells without a known object, for example revealed terrain or a town seen on the map. The game plans the path itself; if it cannot, the result stays uncertain and nothing moves. Hero stops when daily movement runs out.")]
    public Task<OperationResult> MoveToTile(string operationId,string revision,int x,int y,int z,CancellationToken cancellationToken)=>endpoint.MoveToTile(new(operationId,revision,x,y,z),cancellationToken);
    [McpServerTool,Description("Explicit diagnostic: in one call read fair structured observation, save the game framebuffer PNG and matching JSON, and verify the observed revision is unchanged across capture. Returns observation and local file metadata, never inline images. Does not freeze animations. Use for UI mapping/debugging, not normal gameplay; no focus or input changes.")]
    public Task<DebugSnapshot> DebugSnapshot(CancellationToken cancellationToken)=>endpoint.Snapshot(cancellationToken);
    [McpServerTool,Description("Start HotA using the existing host HD Launcher's Play action and saved settings. Does not activate windows or send mouse/keyboard input. Returns already_running or launch_pending on repeated requests; use game_status and observe to check readiness.")]
    public Task<object> StartGame(CancellationToken cancellationToken)=>endpoint.Start(cancellationToken);
    [McpServerTool,Description("Read HD Launcher renderer options, or select one exact returned renderer label for the next game launch. Pass null to read. Only change graphics when the user asks; does not activate windows or use mouse/keyboard input.")]
    public Task<object> LauncherGraphics(string? renderer,CancellationToken cancellationToken)=>endpoint.Graphics(renderer,cancellationToken);
    [McpServerTool,Description("Explicit developer diagnostic only: save the game's current rendered framebuffer as a local PNG without window activation, cursor movement, keyboard input or desktop capture. Returns file metadata only, never inline image data. Do not use in normal gameplay loops; observe provides economical structured game state.")]
    public Task<CaptureResult> DebugCapture(CancellationToken cancellationToken)=>endpoint.Capture(cancellationToken);
    [McpServerTool,Description("Read recognized visible destinations by target ID and available game route data. Reads game memory without UI input. No hidden objects, terrain-rule explanations or strategic recommendations. Object coverage and route support are incomplete.")]
    public Task<NearbyTargets> NearbyTargets(CancellationToken cancellationToken)=>endpoint.Nearby(cancellationToken);

    [McpServerTool,Description("Read a visible target and available game route data directly from memory by target ID. Does not send keyboard or mouse input, move the hero, or add reference knowledge. Unavailable route data is not evidence that the target is unreachable.")]
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken cancellationToken)=>endpoint.InspectTarget(targetId,revision,cancellationToken);

    [McpServerTool,Description("Read supported capabilities and current development limitations. No game action.")]
    public Task<object> GameStatus(CancellationToken cancellationToken)=>endpoint.Status(cancellationToken);

    [McpServerTool,Description("Ask the project's own documentation and get the matching text back: game rules, playbooks, hotkeys and controls, combat, towns and economy, save/load cycle, capability map and the running lessons log. Use this whenever you are unsure what to do next, how a game function is operated, or whether a capability exists. Returns the best matching sections with their file, heading, score and text; empty hits mean the documentation does not cover the question yet.")]
    public Task<DocsAnswer> HotaDocs(string query,int limit,CancellationToken cancellationToken)=>endpoint.Docs(new(query,limit<=0?3:limit),cancellationToken);

    [McpServerTool,Description("List every document this bridge can answer from, with the headings inside each document. Use it when no search hit looks right, or to see what knowledge exists before asking.")]
    public Task<DocsCatalog> HotaDocsCatalog(CancellationToken cancellationToken)=>endpoint.DocsCatalog(cancellationToken);

    [McpServerTool,Description("Read one document, or one heading inside it, exactly as the catalog or a search hit named it. Use after a search to read the full section instead of guessing from a snippet.")]
    public Task<DocText> HotaDocsRead(string path,string? heading,CancellationToken cancellationToken)=>endpoint.DocsRead(path,heading,cancellationToken);

    [McpServerTool,Description("Observe your assigned player's own hero/resources and supported active UI. Returns a revision required for actions. Unknown screens and wrong-player contexts are denied.")]
    public Task<Observation> Observe(CancellationToken cancellationToken)=>endpoint.Observe(cancellationToken);

    [McpServerTool,Description("Click a validated UI action from the latest observation. Accepts a key from observation Actions or validated system-option buttons. Includes own towns, construction cards and purchase/cancel. Game enforces costs and daily construction limits. Use the SAME operationId for retries; never retry an uncertain action with a new ID.")]
    public Task<OperationResult> ClickUi(string operationId,string revision,string element,CancellationToken cancellationToken)
        =>endpoint.Click(new(operationId,revision,element),cancellationToken);

    [McpServerTool,Description("Execute an available semantic action key from observe.Actions, with the current revision. Construction spends normal game resources. Keep operationId unchanged for retries; uncertain means observe before making another decision.")]
    public Task<OperationResult> Act(string operationId,string revision,string action,CancellationToken cancellationToken)
        =>endpoint.Click(new(operationId,revision,action),cancellationToken);

    [McpServerTool,Description("Read this controller's recent action results and plan updates. Contains no opponent history.")]
    public Task<object> ReadJournal(int limit,CancellationToken cancellationToken)=>endpoint.Journal(limit,cancellationToken);

    [McpServerTool,Description("Read or save your concise strategy, goals and next steps. Pass null to read. A plan does not execute actions or change the game.")]
    public Task<object> Plan(string? value,CancellationToken cancellationToken)=>endpoint.Plan(value,cancellationToken);
}
