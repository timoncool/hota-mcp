using System.ComponentModel;
using ModelContextProtocol.Server;

namespace HotaMcp;

[McpServerToolType]
public sealed class GameTools(IGameEndpoint endpoint)
{
    [McpServerTool,Description("Click a point on the adventure map surface (game pixel coordinates). This is the game's own way to make it compute routes: after such a click the route cache exists and nearby_targets starts reporting reachable routes. Use the centre of the map area to reach the hero's own tile without ordering a move.")]
    public Task<OperationResult> MapClick(int x,int y,CancellationToken cancellationToken)=>endpoint.MapClick(new(x,y),cancellationToken);

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
    [McpServerTool,Description("Read the known map around a cell: terrain, roads, cells blocked by terrain, and the recognized visible objects with their coordinates. Cells the player has not uncovered come back as '?'. Use it to decide where a hero can step and what is worth walking to. Radius is capped at 12. No input is sent to the game.")]
    public Task<MapView> ReadMap(int x,int y,int z,int radius,CancellationToken cancellationToken)=>endpoint.ReadMap(x,y,z,radius,cancellationToken);

    [McpServerTool,Description("Read recognized visible destinations near the selected hero. Reads game memory and sends no input at all. Route data attached to each target is whatever the game last computed, and the game keeps a route for a single destination which any action invalidates — so a target can read not_available while the hero can plainly reach it. To get a real route, call inspect_target for the one destination you are considering. Object coverage is not exhaustive and hidden objects are never reported.")]
    public Task<NearbyTargets> NearbyTargets(CancellationToken cancellationToken)=>endpoint.Nearby(cancellationToken);

    [McpServerTool,Description("Read a visible target and available game route data directly from memory by target ID. Does not send keyboard or mouse input, move the hero, or add reference knowledge. Unavailable route data is not evidence that the target is unreachable.")]
    public Task<TargetInspection> InspectTarget(string targetId,string revision,CancellationToken cancellationToken)=>endpoint.InspectTarget(targetId,revision,cancellationToken);

    [McpServerTool,Description("Read supported capabilities and current development limitations. No game action.")]
    public Task<object> GameStatus(CancellationToken cancellationToken)=>endpoint.Status(cancellationToken);

    [McpServerTool,Description("Ask the game reference a direct question and get the passage that answers it: rules, formulas, combat, movement, towns and economy, skills and spells, object guards, hotkeys, and this bridge's own playbooks. Ask in plain words (\"как считается урон\", \"какая охрана у утопии драконов\"). Ranking is BM25 over sections; a misheard or mistyped query falls back to closest titles by spelling. Ask in Russian or English: the knowledge files carry an English summary beside the Russian body, and the game manual and the HotA documentation are English originals. A question that names one thing — a creature, a spell, an artefact, a building — is answered with that thing’s card from the installed game’s own tables; those card names follow the language the game is installed in. detail controls cost: \"snippet\" (default) returns only the answering passage, \"titles\" returns file and heading alone for orientation, \"full\" returns whole sections and costs several times more. Read the whole section with hota_docs_read instead of asking for full detail on every hit.")]
    public Task<DocsAnswer> HotaDocs(string query,int limit,string? detail,CancellationToken cancellationToken)
        =>endpoint.Docs(new(query,limit<=0?3:limit,detail),cancellationToken);

    [McpServerTool,Description("List what the reference contains. Without a path it names every document and how many sections each holds; with a path it lists that document's headings so you can read one section instead of the whole file. Use it when no search hit looks right, or to see what subjects exist before asking.")]
    public Task<DocsCatalog> HotaDocsCatalog(string? path,CancellationToken cancellationToken)=>endpoint.DocsCatalog(path,cancellationToken);

    [McpServerTool,Description("Look up the game's own rule card for a named thing: a secondary skill, a spell, a creature, an artefact, a building, a map object or a faction. The data is read from the rule tables of the installed game, so it matches the exact HotA version being played, including what each mastery level of a skill does and what a spell costs. Pass kind to narrow the search (навык, заклинание, существо, артефакт, здание, объект карты). This is reference only and says nothing about the current party; use observe for that.")]
    public Task<ReferenceAnswer> HotaReference(string name,string? kind,int limit,CancellationToken cancellationToken)
        =>endpoint.Reference(new(name,kind,limit<=0?3:limit),cancellationToken);

    [McpServerTool,Description("Read one document, or one heading inside it, exactly as the catalog or a search hit named it. Use after a search to read the whole section instead of guessing from a passage. Long text is returned in windows: maxChars caps the window (default 6000) and offset continues where the previous window ended, so nothing is silently cut.")]
    public Task<DocText> HotaDocsRead(string path,string? heading,int offset,int maxChars,CancellationToken cancellationToken)
        =>endpoint.DocsRead(path,heading,offset,maxChars,cancellationToken);

    [McpServerTool,Description("Observe your assigned player's own hero/resources and supported active UI. Returns a revision required for actions. Unknown screens and wrong-player contexts are denied.")]
    public Task<Observation> Observe(CancellationToken cancellationToken)=>endpoint.Observe(cancellationToken);

    [McpServerTool,Description("Click a validated UI action from the latest observation. Accepts a key from observation Actions or validated system-option buttons. Includes own towns, construction cards and purchase/cancel. Game enforces costs and daily construction limits. Use the SAME operationId for retries; never retry an uncertain action with a new ID.")]
    public Task<OperationResult> ClickUi(string operationId,string revision,string element,CancellationToken cancellationToken)
        =>endpoint.Click(new(operationId,revision,element),cancellationToken);

    [McpServerTool,Description("Execute an available semantic action key from observe.Actions, with the current revision. Construction spends normal game resources. Keep operationId unchanged for retries; uncertain means observe before making another decision.")]
    public Task<OperationResult> Act(string operationId,string revision,string action,CancellationToken cancellationToken)
        =>endpoint.Click(new(operationId,revision,action),cancellationToken);

    [McpServerTool,Description("Look at a map cell the way a player does before walking into it: the bridge holds the right mouse button over the cell and reads the card the game shows, then releases. For a wandering stack that card names the creature and its rough size, which is what you need before deciding to fight. Nothing is entered, no fight starts, the hero does not move. The cell must be within the visible part of the map.")]
    public Task<CellCard> InspectCell(string revision,int x,int y,int z,CancellationToken cancellationToken)
        =>endpoint.InspectCell(new(revision,x,y,z),cancellationToken);

    [McpServerTool,Description("Read the game's own info card for one control from the latest observation: creature stats, skill or spell description, artefact text, hero details. The bridge holds the right mouse button the way a player does, reads the card and releases it, so the control is not activated and nothing is bought, moved or spent. Pass the element key from observe.Elements.")]
    public Task<ElementCard> InspectElement(string revision,string element,CancellationToken cancellationToken)
        =>endpoint.InspectElement(new(revision,element),cancellationToken);

    [McpServerTool,Description("Read this controller's recent action results and plan updates. Contains no opponent history.")]
    public Task<object> ReadJournal(int limit,CancellationToken cancellationToken)=>endpoint.Journal(limit,cancellationToken);

    [McpServerTool,Description("Read or save your concise strategy, goals and next steps. Pass null to read. A plan does not execute actions or change the game.")]
    public Task<object> Plan(string? value,CancellationToken cancellationToken)=>endpoint.Plan(value,cancellationToken);
}
