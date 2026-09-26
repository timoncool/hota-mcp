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
        +"Returns ids, object names and coordinates, and the game's own route to each: reachable_today, needs_more_days or "
        +"not_available. Read only, it presses nothing: when the game's route table is stale the routes say so, and "
        +"pointing at a cell with inspect_path rebuilds it. Where the game "
        +"lays no path, the reason names what shuts the way — a wandering stack with its cell, a garrison, a border gate, "
        +"another hero — or says there is no explored land way; LockedBehind groups the targets one fight opens. "
        +"A mine carries the colour of its flag (yours, an ally's — leave it, an enemy's or nobody's — take it). "
        +"Does NOT choose targets or move the hero. Object coverage is not exhaustive and hidden objects are never reported, "
        +"so an absent target is not proof of an empty map.")]
    public Task<NearbyTargets> NearbyTargets(CancellationToken cancellationToken)=>endpoint.Nearby(cancellationToken);

    [McpServerTool(Title="Route to one destination",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Makes the game compute the route to one destination and reads the answer. "
        +"Returns movement cost, number of steps and a state — reachable_today, needs_more_days or not_available with a reason. "
        +"Does NOT move the hero. Where the game lays no path, the reason names what shuts the way.")]
    public Task<TargetInspection> InspectTarget(
        [Description("Target id exactly as nearby_targets reported it.")] string targetId,
        [Description("Revision from the observation these targets were read on.")] string revision,
        CancellationToken cancellationToken)=>endpoint.InspectTarget(targetId,revision,cancellationToken);

    [McpServerTool(Title="Route to any cell",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Makes the game plan the selected hero's route to any explored cell, the way pointing at it does, and reads "
        +"the game's own answer. Returns movement cost, steps and a state — reachable_today, needs_more_days or not_available "
        +"with the reason (no path at all, fog on the way). Use it to learn whether an area can be walked to at all and in how "
        +"many days, before sending a hero there. Does NOT move the hero.")]
    public Task<RouteView> InspectPath(
        [Description("Cell x.")] int x,
        [Description("Cell y.")] int y,
        [Description("Map level: 0 surface, 1 underground.")] int z,
        [Description("Revision from the latest observation.")] string revision,
        CancellationToken cancellationToken)=>endpoint.InspectPath(x,y,z,revision,cancellationToken);

    [McpServerTool(Title="The whole level at a glance",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads one whole map level the way the minimap shows it, one character per cell, from the game's own map: "
        +"'?' where the player has not been, water, rock, land, the flags over towns and mines (yours, an ally's, an enemy's, "
        +"nobody's) and the heroes a player sees, plus a list of the towns and heroes with coordinates and the number of "
        +"unexplored cells. Use it to see where the unexplored land and the enemy towns are before choosing a direction. "
        +"Does NOT send input or reveal the fog.")]
    public Task<MiniMapView> ReadMinimap(
        [Description("Map level: 0 surface, 1 underground.")] int z,
        CancellationToken cancellationToken)=>endpoint.ReadMiniMap(z,cancellationToken);

    [McpServerTool(Title="The game's minimap picture as text",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Takes the minimap exactly as the game paints it on the adventure screen and turns each cell's colour into "
        +"a character: unexplored black, water, dark rock and forest, land, and the players' flag colours. It shows the level "
        +"the adventure view shows now (view:level switches it). Use it as the picture a player glances at; for exact objects "
        +"use read_minimap or read_map. Does NOT send input.")]
    public Task<MinimapPicture> MinimapCapture(CancellationToken cancellationToken)=>endpoint.MinimapCapture(cancellationToken);

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
        [Description("How many recent entries to return; 0 or less means the default window.")] int limit=0,
        CancellationToken cancellationToken=default)=>endpoint.Journal(limit,cancellationToken);

    [McpServerTool(Title="What the ally did on his turns",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads the log of your ally's turns, newest last, each line stamped with the game date and his colour: where "
        +"his heroes stopped, which objects they reached, picked up or beat, fights, armies, levels, skills, artefacts, what "
        +"his towns built and hired, and his resource change per turn. The bridge records it while he moves, reading only "
        +"his side, the way an allied player follows him. The first lines of your brief already sum up his last turn; this "
        +"tool gives the whole record. Does NOT show opponents and changes nothing.")]
    public Task<object> AllyLog(
        [Description("How many recent lines to return; 0 or less means the default window of 60.")] int limit=0,
        CancellationToken cancellationToken=default)=>endpoint.AllyLog(limit,cancellationToken);

    [McpServerTool(Title="Pin a note to a map cell",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Pins your own note to a map cell, or removes it when the note is empty. Use it for what the map does not "
        +"remember for you: a stack too strong for now, a passage, a guarded pocket worth coming back to, where an enemy "
        +"hero was last seen. Notes are handed back in every adventure observation and beside the target in nearby_targets. "
        +"Returns all notes. Does NOT change the game.")]
    public Task<object> Mark(
        [Description("Cell x.")] int x,
        [Description("Cell y.")] int y,
        [Description("Map level: 0 surface, 1 underground.")] int z,
        [Description("The note, up to 300 characters; empty or null removes the note from the cell.")] string? note=null,
        CancellationToken cancellationToken=default)=>endpoint.Mark(x,y,z,note,cancellationToken);

    [McpServerTool(Title="Read or write your plan",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads your stored plan, or replaces it when a value is given. This is the controller's memory between "
        +"turns and it is handed back inside every observation, so it is read whether or not it is asked for. "
        +"Returns the stored text. Writing REPLACES the whole plan, so carry forward what still matters. "
        +"Shape that survives a long game: GOAL — one line, unchanged for the whole game; ACTIVE TASK — exactly one, with "
        +"the game day it must be done by, so stalling is visible; QUEUE — the tasks after it, in order; DONE — one "
        +"compressed line per day, not a retelling; BANS — what the player forbade; NOT TAKEN NEARBY — objects seen and "
        +"left, so they are not rediscovered every turn. Keep decisions and their reason, not a narration of events; what "
        +"happened is in read_journal. Rewrite it at the end of every turn, and whenever the active task turns out to be "
        +"impossible — an active task nobody can finish is how a game stalls. "
        +"Does NOT execute anything or change the game.")]
    public Task<object> Plan(
        [Description("New plan text, or null to read the stored one without changing it.")] string? value=null,
        CancellationToken cancellationToken=default)=>endpoint.Plan(value,cancellationToken);

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

    [McpServerTool(Title="Hover a map cell",ReadOnly=false,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Moves the mouse over one map cell the way a player does and returns the line the game writes at the "
        +"bottom of the screen. That line is the cheapest and richest thing on the adventure map: it names the object, what "
        +"it gives, what it costs, whether the bonus is once per hero, and — for the hero selected right now — whether he "
        +"has already been there, as «(Посещено)» or «(Не посещено)». Example: «Беседка (+2000 опыта один раз для каждого "
        +"героя за 1000 золота) (Не посещено)». "
        +"The visited state belongs to the SELECTED hero, so switch heroes and ask again to read it for the other one. "
        +"Nothing is entered, no fight starts, the hero does not move. "
        +"Use it before spending a day walking to a bonus object. For the creature guarding a cell use inspect_cell, which "
        +"holds the right button and reads the fuller card.")]
    public Task<TileInspection> InspectTile(
        [Description("Cell x.")] int x,
        [Description("Cell y.")] int y,
        [Description("Map level: 0 surface, 1 underground.")] int z,
        [Description("Revision from the observation this cell was seen on.")] string revision,
        CancellationToken cancellationToken)=>endpoint.InspectTile(x,y,z,revision,cancellationToken);

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
        [Description("Exact renderer label from a previous read, or null to only read.")] string? renderer=null,
        CancellationToken cancellationToken=default)=>endpoint.Graphics(renderer,cancellationToken);

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
        [Description("How many hits to return; 0 or less means 3.")] int limit=0,
        [Description("How much text per hit: \"snippet\" (default) the answering passage, \"titles\" only file and heading for cheap orientation, \"full\" whole sections at several times the cost. Prefer hota_docs_read over asking for full.")] string? detail=null,
        CancellationToken cancellationToken=default)=>endpoint.Docs(new(query,limit<=0?3:limit,detail),cancellationToken);

    [McpServerTool(Title="What the reference contains",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Lists the contents of the knowledge base. Without a path it names every document and how many sections each "
        +"holds; with a path it lists that document's headings. "
        +"Returns the list only, never the text — read a section with hota_docs_read. "
        +"Use it when no search hit looks right, or to see what subjects exist before asking.")]
    public Task<DocsCatalog> HotaDocsCatalog(
        [Description("Document path exactly as a catalog entry or a search hit named it, or null to list everything.")] string? path=null,
        CancellationToken cancellationToken=default)=>endpoint.DocsCatalog(path,cancellationToken);

    [McpServerTool(Title="Read a reference section",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Reads one document, or one heading inside it, exactly as the catalog or a search hit named it. "
        +"Returns the text in windows: maxChars caps the window and the answer says where the next window starts, so nothing is "
        +"cut silently. Use it after a search instead of asking hota_docs for full detail on every hit.")]
    public Task<DocText> HotaDocsRead(
        [Description("Document path as the catalog or a search hit gave it.")] string path,
        [Description("Heading inside that document, or null for the whole file.")] string? heading=null,
        [Description("Where to continue from; 0 starts at the beginning, later values come from the previous answer.")] int offset=0,
        [Description("Window size in characters; 0 or less means 6000.")] int maxChars=0,
        CancellationToken cancellationToken=default)=>endpoint.DocsRead(path,heading,offset,maxChars,cancellationToken);

    [McpServerTool(Title="Rule card of a named thing",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),
     Description("Looks up the rule card of one named thing — a secondary skill, a spell, a creature, an artefact, a building, a "
        +"map object or a faction — read from the rule tables of the INSTALLED game, so it matches the exact version being "
        +"played, including what each mastery level does and what a spell costs. "
        +"Returns the card fields as the game stores them. Names follow the language the game is installed in: a Russian install "
        +"answers «Архангел», not \"Archangel\". Creatures of the three HotA factions are not in these tables at all — ask "
        +"hota_docs for those. Says nothing about the current party; that is observe.")]
    public Task<ReferenceAnswer> HotaReference(
        [Description("The name as the game spells it.")] string name,
        [Description("Narrows the search: навык, заклинание, существо, артефакт, здание, объект карты. Null searches every kind.")] string? kind=null,
        [Description("How many cards to return; 0 or less means 3.")] int limit=0,
        CancellationToken cancellationToken=default)=>endpoint.Reference(new(name,kind,limit<=0?3:limit),cancellationToken);

    // ------------------------------------------------------------------ diagnostic: not gameplay

    [McpServerTool(Title="Diagnostic frame plus state",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Developer diagnostic for mapping screens: returns the game's current frame as an image together with everything "
        +"the bridge knows at that instant — the observation with every control, text, picture and action, or, on a screen the "
        +"bridge has not mapped yet, the raw controls of the top window (id, class, rectangle, state, picture, frame, text) and "
        +"the reason there is no observation. Works on every screen. Saves the PNG and the JSON on the host and returns their "
        +"paths; the image itself is returned only when includeImage is true, because images are expensive and have no place "
        +"in the play loop. Not part of playing: the play loop reads observe.")]
    public async Task<ModelContextProtocol.Protocol.CallToolResult> DebugSnapshot(
        [Description("Return the frame as an image in the answer as well. For a developer mapping a screen; default false.")] bool includeImage=false,
        CancellationToken cancellationToken=default)
    {
        var snapshot=await endpoint.Snapshot(cancellationToken);
        string state=System.Text.Json.JsonSerializer.Serialize(snapshot,new System.Text.Json.JsonSerializerOptions{WriteIndented=false});
        var content=new List<ModelContextProtocol.Protocol.ContentBlock>{new ModelContextProtocol.Protocol.TextContentBlock{Text=state}};
        if(includeImage)
            content.Add(ModelContextProtocol.Protocol.ImageContentBlock.FromBytes(
                await File.ReadAllBytesAsync(snapshot.Capture.Path,cancellationToken),"image/png"));
        return new(){Content=content};
    }

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
        ("hota_tools","state"),("game_status","state"),("observe","state"),("nearby_targets","state"),("inspect_target","state"),("inspect_path","state"),
        ("read_map","state"),("read_journal","state"),("ally_log","state"),("plan","state"),("mark","state"),
        ("inspect_cell","look"),("inspect_element","look"),("inspect_tile","look"),
        ("act","act"),("click_ui","act"),("move_to","act"),("move_to_tile","act"),
        ("attack_target","act"),("map_click","act"),("start_game","act"),("launcher_graphics","act"),
        ("hota_docs","reference"),("hota_reference","reference"),
        ("debug_snapshot","diagnostic"),("debug_capture","diagnostic"),
    ];
}
