using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace HotaMcp;

public sealed record ToolEntry(string Name,string Group,string Safety,string Purpose);
public sealed record ToolInventory(int Count,List<ToolEntry> Tools,string Note);

/// Every tool carries its four annotations explicitly, because the protocol's defaults are
/// pessimistic — an unset hint means "writes, destroys, is not idempotent and talks to the open
/// world" — and a silent false is indistinguishable from an unset value on the wire.
[McpServerToolType]
public sealed class GameTools(IGameEndpoint endpoint)
{
    // ------------------------------------------------------------------ state: what you have

    [McpServerTool(Title="Observe your whole side",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads the entire state of your own side in one call and returns the revision that every acting tool requires. "
        +"Starts with Brief: the screen in plain words — who is in the garrison, who is visiting, whether today's building "
        +"is spent, what can be recruited, which action key does each of those — so the obvious follow-up questions need "
        +"no extra calls. "
        +"Returns: the date; your seven resources; Heroes — EVERY hero you own with id, name, map position, movement left, mana, "
        +"primary skills and army, not only the selected one; Towns — EVERY town you own with its garrison, what is built and "
        +"whether today's single building is already spent; Side — which colour you play and whose turn it is now, so a hotseat "
        +"game is never played for someone else; the active screen with its controls; and Actions, the semantic keys that are "
        +"legal on this screen right now, each with a label saying what it does and when it is used. "
        +"Does NOT reveal anything the player cannot see, does not send input and changes nothing. "
        +"For the reference rules use hota_docs; for one control's card use inspect_element.")]
    public Task<Observation> Observe(CancellationToken cancellationToken)=>endpoint.Observe(cancellationToken);

    [McpServerTool(Title="List this bridge's tools",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Lists every tool this server offers with its group and its safety class, built from the registered tools "
        +"themselves so it cannot drift from reality. Groups: state (what you have and where), look (the cards a player reads "
        +"with the right mouse button), act (the verbs that change the game), reference (the game knowledge base), diagnostic "
        +"(developer frames, not gameplay). Returns one line per tool; the full description arrives with the tool itself. "
        +"Does NOT execute anything.")]
    public ToolInventory HotaTools()
    {
        var entries=typeof(GameTools).GetMethods(BindingFlags.Public|BindingFlags.Instance)
            .Where(m=>m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m=>
            {
                string name=string.Concat(m.Name.Select((c,i)=>i>0&&char.IsUpper(c)?"_"+char.ToLowerInvariant(c):char.ToLowerInvariant(c).ToString()));
                var tool=m.GetCustomAttribute<McpServerToolAttribute>()!;
                string purpose=m.GetCustomAttribute<DescriptionAttribute>()?.Description??"";
                int stop=purpose.IndexOf(". ",StringComparison.Ordinal);
                string group=Groups.FirstOrDefault(g=>name.StartsWith(g.Prefix,StringComparison.Ordinal)).Group??"act";
                string safety=tool.ReadOnly==true?"read-only"
                    :tool.Destructive==true?"changes the game, cannot be undone"
                    :"changes something, not destructive";
                return new ToolEntry(name,group,safety,stop>0?purpose[..(stop+1)]:purpose);
            })
            .OrderBy(e=>e.Group).ThenBy(e=>e.Name).ToList();
        return new(entries.Count,entries,
            "Acting tools take operationId and the revision from the observation you acted on. Retry an uncertain "
            +"action under the SAME operationId; a new id is how a hero gets moved twice.");
    }

    [McpServerTool(Title="Capabilities and known gaps",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reports what this build of the bridge supports and what it does not, including screens that are mapped and "
        +"features still missing. Returns capability and limitation lists. Does NOT touch the game. "
        +"Call it once at the start of a session so a missing feature is read rather than guessed at.")]
    public Task<object> GameStatus(CancellationToken cancellationToken)=>endpoint.Status(cancellationToken);

    [McpServerTool(Title="Visible destinations near the hero",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Lists the recognised objects the selected hero can see, each with a stable target id. "
        +"Returns ids, object names and coordinates, plus whatever route the game last computed. "
        +"That route data is a stale cache — the game keeps a route for one destination and any action invalidates it — so a "
        +"target may read not_available while the hero can plainly reach it. Does NOT plan routes, choose targets or send input. "
        +"For a live route to one destination call inspect_target; object coverage is not exhaustive and hidden objects are "
        +"never reported, so an absent target is not proof of an empty map.")]
    public Task<NearbyTargets> NearbyTargets(CancellationToken cancellationToken)=>endpoint.Nearby(cancellationToken);

    [McpServerTool(Title="Route to one destination",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Makes the game compute the route to one destination and reads the answer. "
        +"Returns movement cost, number of steps and a state — reachable_today, needs_more_days or not_available with a reason. "
        +"Does NOT move the hero. This is the live answer; the route attached to nearby_targets is a cache. "
        +"Known defect: not_available is sometimes returned for a cell the hero can in fact reach, so treat a single refusal "
        +"as weak evidence and settle it with a short move_to_tile.")]
    public Task<TargetInspection> InspectTarget(
        [Description("Target id exactly as nearby_targets reported it.")] string targetId,
        [Description("Revision from the observation these targets were read on.")] string revision,
        CancellationToken cancellationToken)=>endpoint.InspectTarget(targetId,revision,cancellationToken);

    [McpServerTool(Title="Read the map around a cell",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads the uncovered map around a cell: terrain, roads, cells blocked by terrain and the recognised visible "
        +"objects with their coordinates. Returns a grid where cells the player has not uncovered are '?'. "
        +"Does NOT send input, move anything or reveal the fog. Use it to decide where a hero can step and what lies on the way; "
        +"for the creature guarding a cell use inspect_cell.")]
    public Task<MapView> ReadMap(
        [Description("Cell x the window is centred on.")] int x,
        [Description("Cell y the window is centred on.")] int y,
        [Description("Map level: 0 surface, 1 underground.")] int z,
        [Description("Half-width of the window in cells, 1..12; larger values are capped at 12.")] int radius,
        CancellationToken cancellationToken)=>endpoint.ReadMap(x,y,z,radius,cancellationToken);

    [McpServerTool(Title="What this controller did recently",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads this controller's own recent actions and their results, newest last, each stamped with the game date. "
        +"Returns the record of what was attempted and what came of it — the answer to \"what did I do yesterday\". "
        +"Does NOT contain opponent history and shows nothing the player could not see.")]
    public Task<object> ReadJournal(
        [Description("How many recent entries to return; 0 or less means the default window.")] int limit,
        CancellationToken cancellationToken)=>endpoint.Journal(limit,cancellationToken);

    [McpServerTool(Title="Read or write your plan",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads your stored plan, or replaces it when a value is given. The plan is your own memory between turns: the "
        +"goal of the game, what changed today, what is known about the enemy, the first things to do tomorrow. "
        +"Returns the stored text. Writing REPLACES the previous plan — include what still matters. "
        +"Does NOT execute anything or change the game; read_journal holds what actually happened.")]
    public Task<object> Plan(
        [Description("New plan text, or null to read the stored one without changing it.")] string? value,
        CancellationToken cancellationToken)=>endpoint.Plan(value,cancellationToken);

    // ------------------------------------------------------------------ look: the right-button cards

    [McpServerTool(Title="Card of a map cell",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Looks at one map cell the way a player does before walking into it: the bridge holds the right mouse button "
        +"over the cell, reads the card the game draws and releases. Returns the card text — for a wandering stack, the creature "
        +"and its rough size, which is the input for \"fight or walk around\". "
        +"Does NOT enter the cell, start a fight or move the hero. The cell must be inside the visible part of the map. "
        +"The answer goes stale: wandering stacks move on the enemy's turn, so read it in the same turn you attack.")]
    public Task<CellCard> InspectCell(
        [Description("Revision from the observation this cell was seen on.")] string revision,
        [Description("Cell x.")] int x,
        [Description("Cell y.")] int y,
        [Description("Map level: 0 surface, 1 underground.")] int z,
        CancellationToken cancellationToken)=>endpoint.InspectCell(new(revision,x,y,z),cancellationToken);

    [McpServerTool(Title="Card of a control on screen",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads the game's own info card for one control of the current screen: creature stats, a skill or spell "
        +"description, artefact text, hero details. The bridge holds the right mouse button, reads the card and releases it, so "
        +"the control is NOT activated — nothing is bought, moved, dismissed or spent. Returns the card text. "
        +"Left-clicking the same control would act: on an army slot or an exchange arrow a left click moves troops. "
        +"For the rules behind the card use hota_reference.")]
    public Task<ElementCard> InspectElement(
        [Description("Revision from the observation this control was read on.")] string revision,
        [Description("Element key from observe.Elements, for example \"ui:14\" or \"id:224\".")] string element,
        CancellationToken cancellationToken)=>endpoint.InspectElement(new(revision,element),cancellationToken);

    // ------------------------------------------------------------------ act: the verbs

    [McpServerTool(Title="Perform a semantic action",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),
     Description("Performs one action from observe.Actions by its key — town, construction, recruitment, tavern, hero screen, "
        +"army transfers, combat, dialogs, end of turn. This is the main verb of the bridge. "
        +"Returns the result state and a fresh observation. The game enforces costs, daily limits and legality; an action that "
        +"the game refuses comes back as refused, not as a silent no-op. "
        +"Changes the game and cannot be undone — a dismissed stack is gone, an ended turn is over. "
        +"Retry an uncertain result under the SAME operationId; a new id repeats the action and can move a hero twice. "
        +"Only keys the latest observation published are accepted; for a raw control with no semantic key use click_ui.")]
    public Task<OperationResult> Act(
        [Description("Your own id for this intent, stable across retries of the same intent.")] string operationId,
        [Description("Revision from the observation you decided on; a stale revision is refused.")] string revision,
        [Description("Action key exactly as observe.Actions published it, for example \"turn:end\" or \"army:merge:Пикси\".")] string action,
        CancellationToken cancellationToken)=>endpoint.Click(new(operationId,revision,action),cancellationToken);

    [McpServerTool(Title="Press a raw control",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),
     Description("Presses one validated control of the current screen by its element key, for screens where no semantic key "
        +"exists yet. Returns the result state and a fresh observation. "
        +"Prefer act: a semantic key says what it does and is checked against the screen. Use this only when the control you "
        +"need is in observe.Elements but not in observe.Actions. "
        +"Changes the game and cannot be undone; retry under the SAME operationId.")]
    public Task<OperationResult> ClickUi(
        [Description("Your own id for this intent, stable across retries of the same intent.")] string operationId,
        [Description("Revision from the observation you decided on; a stale revision is refused.")] string revision,
        [Description("Element key from observe.Elements.")] string element,
        CancellationToken cancellationToken)=>endpoint.Click(new(operationId,revision,element),cancellationToken);

    [McpServerTool(Title="Walk to a known target",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),
     Description("Walks the selected hero toward a visible target id using the game's own route and move handlers. The hero "
        +"walks the whole way and the answer comes back when he stops. Returns the result state and a fresh observation. "
        +"May pick up the resource, open a dialog or run into an enemy on the way. Spends movement and cannot be undone. "
        +"Refuses a cell holding a creature stack, because stepping there is a battle — that is attack_target, chosen "
        +"deliberately. For a cell with no object use move_to_tile. Retry under the SAME operationId.")]
    public Task<OperationResult> MoveTo(
        [Description("Your own id for this intent, stable across retries of the same intent.")] string operationId,
        [Description("Revision from the observation this target was read on.")] string revision,
        [Description("Target id from nearby_targets.")] string targetId,
        CancellationToken cancellationToken)=>endpoint.Move(new(operationId,revision,targetId),cancellationToken);

    [McpServerTool(Title="Walk to a map cell",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),
     Description("Walks the selected hero to an explicit cell using the game's own route planning. Use it for exploring and for "
        +"cells that carry no recognised object — revealed terrain, a town seen across the map. "
        +"Returns the result state and a fresh observation. The hero stops when the day's movement runs out; if the game cannot "
        +"plan a path nothing moves and the result stays uncertain. Spends movement and cannot be undone. "
        +"Retry under the SAME operationId.")]
    public Task<OperationResult> MoveToTile(
        [Description("Your own id for this intent, stable across retries of the same intent.")] string operationId,
        [Description("Revision from the observation you decided on.")] string revision,
        [Description("Destination cell x.")] int x,
        [Description("Destination cell y.")] int y,
        [Description("Map level: 0 surface, 1 underground.")] int z,
        CancellationToken cancellationToken)=>endpoint.MoveToTile(new(operationId,revision,x,y,z),cancellationToken);

    [McpServerTool(Title="Attack a creature stack",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),
     Description("Deliberately starts a fight: moves the selected hero onto a visible creature stack. Ordinary movement refuses "
        +"such cells, so this tool is the only way in and the choice is explicit. "
        +"Returns the result state and a fresh observation; the battle then runs on the combat screen. "
        +"The army can be lost and nothing can be undone — read inspect_cell for the guard and hota_reference for both "
        +"creatures before calling. Retry under the SAME operationId; an uncertain result is not permission to attack again.")]
    public Task<OperationResult> AttackTarget(
        [Description("Your own id for this intent, stable across retries of the same intent.")] string operationId,
        [Description("Revision from the observation this stack was read on.")] string revision,
        [Description("Target id of the creature stack from nearby_targets.")] string targetId,
        CancellationToken cancellationToken)=>endpoint.Attack(new(operationId,revision,targetId),cancellationToken);

    [McpServerTool(Title="Click the map surface",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Clicks one point of the adventure map surface in game pixels. This is how the game is made to compute routes: "
        +"after such a click a route cache exists and nearby_targets starts reporting routes. "
        +"Returns the result state and a fresh observation. Clicking the centre of the map area lands on the hero's own tile and "
        +"orders no move. Prefer move_to and move_to_tile for actual movement; this is the low-level gesture behind them.")]
    public Task<OperationResult> MapClick(
        [Description("Game-pixel x inside the 800x600 map surface.")] int x,
        [Description("Game-pixel y inside the 800x600 map surface.")] int y,
        CancellationToken cancellationToken)=>endpoint.MapClick(new(x,y),cancellationToken);

    [McpServerTool(Title="Raise the game",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Starts HotA through the host HD Launcher's own Play action with its saved settings. "
        +"Returns already_running or launch_pending; calling it again while the game is coming up is harmless. "
        +"Does NOT activate windows or send mouse or keyboard input. Check readiness with game_status and observe.")]
    public Task<object> StartGame(CancellationToken cancellationToken)=>endpoint.Start(cancellationToken);

    [McpServerTool(Title="Launcher renderer",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads the HD Launcher's renderer options, or selects one for the next launch. Pass null to read without "
        +"changing anything. Returns the available renderer labels and the current selection. "
        +"Only change the renderer when the user asks for it — it decides how the game window is drawn. "
        +"Does NOT activate windows or send input.")]
    public Task<object> LauncherGraphics(
        [Description("Exact renderer label from a previous read, or null to only read.")] string? renderer,
        CancellationToken cancellationToken)=>endpoint.Graphics(renderer,cancellationToken);

    // ------------------------------------------------------------------ reference: the knowledge base

    [McpServerTool(Title="Ask the game reference",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Asks the built-in knowledge base a direct question and returns the passage that answers it: rules, formulas, "
        +"combat, movement, towns and economy, skills and spells, object guards, hotkeys, HotA specifics and this bridge's own "
        +"playbooks. Ask in plain words, in Russian or English — every document carries an English summary beside its Russian "
        +"body, and the 1999 manual and the HotA documentation are English originals. "
        +"Returns the hits with file, heading and the answering passage. "
        +"A question naming one thing — a creature, a spell, an artefact, a building — also returns that thing's card from the "
        +"installed game's own tables. Every document ends with a Пробелы section stating what it does not know, so silence is "
        +"never mistaken for \"no such rule\". "
        +"Says nothing about the current game; that is observe.")]
    public Task<DocsAnswer> HotaDocs(
        [Description("The question in plain words, for example \"как считается урон\" or \"dragon utopia guards\".")] string query,
        [Description("How many hits to return; 0 or less means 3.")] int limit,
        [Description("How much text per hit: \"snippet\" (default) the answering passage, \"titles\" only file and heading for cheap orientation, \"full\" whole sections at several times the cost. Prefer hota_docs_read over asking for full.")] string? detail,
        CancellationToken cancellationToken)=>endpoint.Docs(new(query,limit<=0?3:limit,detail),cancellationToken);

    [McpServerTool(Title="What the reference contains",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Lists the contents of the knowledge base. Without a path it names every document and how many sections each "
        +"holds; with a path it lists that document's headings. "
        +"Returns the list only, never the text — read a section with hota_docs_read. "
        +"Use it when no search hit looks right, or to see what subjects exist before asking.")]
    public Task<DocsCatalog> HotaDocsCatalog(
        [Description("Document path exactly as a catalog entry or a search hit named it, or null to list everything.")] string? path,
        CancellationToken cancellationToken)=>endpoint.DocsCatalog(path,cancellationToken);

    [McpServerTool(Title="Read a reference section",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads one document, or one heading inside it, exactly as the catalog or a search hit named it. "
        +"Returns the text in windows: maxChars caps the window and the answer says where the next window starts, so nothing is "
        +"cut silently. Use it after a search instead of asking hota_docs for full detail on every hit.")]
    public Task<DocText> HotaDocsRead(
        [Description("Document path as the catalog or a search hit gave it.")] string path,
        [Description("Heading inside that document, or null for the whole file.")] string? heading,
        [Description("Where to continue from; 0 starts at the beginning, later values come from the previous answer.")] int offset,
        [Description("Window size in characters; 0 or less means 6000.")] int maxChars,
        CancellationToken cancellationToken)=>endpoint.DocsRead(path,heading,offset,maxChars,cancellationToken);

    [McpServerTool(Title="Rule card of a named thing",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Looks up the rule card of one named thing — a secondary skill, a spell, a creature, an artefact, a building, a "
        +"map object or a faction — read from the rule tables of the INSTALLED game, so it matches the exact version being "
        +"played, including what each mastery level does and what a spell costs. "
        +"Returns the card fields as the game stores them. Names follow the language the game is installed in: a Russian install "
        +"answers «Архангел», not \"Archangel\". Creatures of the three HotA factions are not in these tables at all — ask "
        +"hota_docs for those. Says nothing about the current party; that is observe.")]
    public Task<ReferenceAnswer> HotaReference(
        [Description("The name as the game spells it.")] string name,
        [Description("Narrows the search: навык, заклинание, существо, артефакт, здание, объект карты. Null searches every kind.")] string? kind,
        [Description("How many cards to return; 0 or less means 3.")] int limit,
        CancellationToken cancellationToken)=>endpoint.Reference(new(name,kind,limit<=0?3:limit),cancellationToken);

    // ------------------------------------------------------------------ diagnostic: not gameplay

    [McpServerTool(Title="Diagnostic frame plus state",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Developer diagnostic: saves the game's framebuffer as a local PNG together with the matching observation JSON "
        +"and checks that the revision did not change across the capture. "
        +"Returns the observation and the file paths, never inline image data. Writes files on the host. "
        +"This is for mapping a screen that has no adapter yet, or for an illustration the user asked for — not part of playing. "
        +"The play loop reads observe.")]
    public Task<DebugSnapshot> DebugSnapshot(CancellationToken cancellationToken)=>endpoint.Snapshot(cancellationToken);

    [McpServerTool(Title="Diagnostic frame",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Developer diagnostic: saves the game's current frame as a local PNG without activating the window, moving the "
        +"cursor, sending input or capturing the desktop. "
        +"Returns the file path and size, never inline image data. Writes a file on the host. "
        +"Not part of playing: observe is the cheap, structured way to read the game.")]
    public Task<CaptureResult> DebugCapture(CancellationToken cancellationToken)=>endpoint.Capture(cancellationToken);

    /// The inventory is read from the registered tools themselves, so it cannot drift away from
    /// what the server actually offers the way a hand-written list does.
    private static readonly (string Prefix,string Group)[] Groups=
    [
        ("hota_tools","state"),("game_status","state"),("observe","state"),("nearby_targets","state"),("inspect_target","state"),
        ("read_map","state"),("read_journal","state"),("plan","state"),
        ("inspect_cell","look"),("inspect_element","look"),
        ("act","act"),("click_ui","act"),("move_to","act"),("move_to_tile","act"),
        ("attack_target","act"),("map_click","act"),("start_game","act"),("launcher_graphics","act"),
        ("hota_docs","reference"),("hota_reference","reference"),
        ("debug_snapshot","diagnostic"),("debug_capture","diagnostic"),
    ];
}
