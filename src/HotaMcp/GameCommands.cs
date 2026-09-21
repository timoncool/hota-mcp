namespace HotaMcp;

/// Extra evidence an action must produce before it counts as completed.
/// A revision change alone is never enough for these.
[Flags]
internal enum Confirm
{
    None = 0,
    /// The active combat stack or the round number must change, and it must still be our turn.
    CombatTurn = 1,
    /// The combat log must have grown.
    CombatLog = 2,
    /// The hero's mana must have dropped.
    ManaSpent = 4,
    /// The date must have advanced, or the game must be asking a question.
    TurnAdvanced = 8,
    /// The named scenario setup choice must now read as selected.
    SetupChoice = 16,
    /// A real party must exist: own hero and a date.
    PartyLoaded = 32,
    /// The hero must actually stand somewhere else, or have spent movement getting there.
    HeroMoved = 64,
    /// The town garrison must actually hold different stacks than before.
    GarrisonChanged = 128,
}

/// Everything a delivery needs about the game and the action being performed.
internal sealed record CommandContext(WindowsGame Game, GameReader Reader, int Player, Observation Before, string Element);

internal delegate Task Deliver(CommandContext context, CancellationToken ct);

/// One semantic action: how it reaches the game, and what proves it happened.
/// Expected lists the screens this action may legitimately land on, comma separated.
internal sealed record GameCommand(string Expected, Deliver Deliver)
{
    public bool Accepts(string screen) => Expected.Split(',').Contains(screen);

    public Confirm Confirm { get; init; } = Confirm.None;
    public int TimeoutSeconds { get; init; } = 3;
    /// Combat actions can end the battle; the result screen then terminates the wait.
    public bool BattleMayEnd { get; init; }
}

/// Ways of delivering an action through the game's own handlers.
internal static class Deliveries
{
    /// Closed native command vocabulary running on the game's UI thread.
    public static Deliver Native(int operation, int argument = 0) =>
        (context, _) => { context.Game.NativeAction(operation, context.Player, argument); return Task.CompletedTask; };

    public static Deliver Native(int operation, Func<CommandContext, int> argument) =>
        (context, _) => { context.Game.NativeAction(operation, context.Player, argument(context)); return Task.CompletedTask; };

    /// Ordinary game hotkey delivered as a window message to the game window only.
    public static Deliver Key(ushort key, ushort scan) => (context, _) => context.Game.KeyAsync(key, scan);

    public static Deliver KeyWithControl(ushort key, ushort scan) =>
        (context, _) => context.Game.KeyWithControlAsync(key, scan);

    /// Press one dialog control identified by its own id and button asset. An observation leaves
    /// out controls that carry neither text nor a button image — the army slot pictures of a town
    /// are exactly that — so when the id is not among the published elements the control is looked
    /// up in the dialog itself rather than reported as missing.
    public static Deliver Control(int id, params string[] assets) => async (context, ct) =>
    {
        var button = context.Before.Elements.FirstOrDefault(e =>
            e.Id == id && (assets.Length == 0 || assets.Contains(e.Asset)) && e.Interactive);
        if (button is not null) { await Press(context, button, ct); return; }
        var box = context.Reader.FindControlById(id)
            ?? throw new InvalidOperationException($"No control with id {id} on this screen");
        await Press(context, box.X + box.Width / 2, box.Y + box.Height / 2, ct);
    };

    /// Leaves an informational window the way the manual describes: "Return - Okay, Accept, or
    /// Yes". Esc is the game's Quit and several of these windows ignore it outright, and their own
    /// exit buttons do not always take a plain press, so Return is what actually closes them.
    /// The window's own button is still tried first where it exists.
    public static Deliver Dismiss => async (context, ct) =>
    {
        string[] assets = ["OvButn1.def", "iokay.def", "soretrn.def", "scnrback.def", "gspexit.def"];
        var exit = context.Before.Elements.LastOrDefault(e => e.Interactive && assets.Contains(e.Asset));
        if (exit is not null)
        {
            await Press(context, exit, ct);
            await Task.Delay(250, CancellationToken.None);
            try { if (context.Reader.Observe().Screen != context.Before.Screen) return; }
            catch (InvalidOperationException) { return; }
        }
        await context.Game.KeyAsync(0x0d, 0x1c);
    };

    /// Press the control the request itself names.
    public static Deliver Requested => async (context, ct) =>
        await Press(context, context.Before.Elements.Single(e => e.Key == context.Element), ct);

    /// Press a control found by its exact reported rectangle (the game's own slot widgets).
    public static Deliver Slot(int x, int y, int width, int height, string missing) => async (context, ct) =>
    {
        var point = context.Reader.FindControl(x, y, width, height)
            ?? throw new InvalidOperationException(missing);
        await Press(context, point.X, point.Y, ct);
    };

    public static Task Press(CommandContext context, UiElement element, CancellationToken ct) =>
        Press(context, element.X + element.Width / 2, element.Y + element.Height / 2, ct);

    public static Task Press(CommandContext context, int x, int y, CancellationToken ct) =>
        context.Game.MouseAsync(x, y, context.Before.Width, context.Before.Height, true, CancellationToken.None);

    /// Two plain clicks on two garrison-row widgets, the way the manual describes the gesture.
    public static Deliver TwoSlots(int firstY, int secondY) => async (context, ct) =>
    {
        var first = context.Reader.FindControl(241, firstY, 58, 64)
            ?? throw new InvalidOperationException("Town garrison row control not found in the town dialog");
        var second = context.Reader.FindControl(241, secondY, 58, 64)
            ?? throw new InvalidOperationException("Town garrison row control not found in the town dialog");
        await Press(context, first.X, first.Y, ct);
        await Task.Delay(700, CancellationToken.None);
        await Press(context, second.X, second.Y, ct);
    };

    /// Click a combat hex at the coordinates the combat manager reports for it.
    public static Deliver CombatHex(Func<CommandContext, int> hex) => async (context, ct) =>
    {
        var point = new CombatReader(context.Game, context.Player).Point(hex(context));
        await Press(context, point.X, point.Y, ct);
    };
}

/// Maps every published action key and every clickable UI element to its command.
/// Adding an action means adding one row here; nothing else in the bridge changes.
internal static class GameCommands
{
    /// Direction suffix to the key the game listens for. The orthogonals are the arrow keys the
    /// manual names; the diagonals are the corners of the numeric keypad, which with the numeric
    /// lock off arrive as Home, Page Up, End and Page Down.
    private static (ushort Key, ushort Scan) StepKey(string actionKey) => actionKey[(actionKey.LastIndexOf(':') + 1)..] switch
    {
        "north" => ((ushort)0x26, (ushort)0x48),
        "south" => ((ushort)0x28, (ushort)0x50),
        "west" => ((ushort)0x25, (ushort)0x4b),
        "east" => ((ushort)0x27, (ushort)0x4d),
        "northwest" => ((ushort)0x24, (ushort)0x47),
        "northeast" => ((ushort)0x21, (ushort)0x49),
        "southwest" => ((ushort)0x23, (ushort)0x4f),
        "southeast" => ((ushort)0x22, (ushort)0x51),
        _ => throw new InvalidOperationException("Unknown direction"),
    };

    /// Where answering a modal question can legitimately land.
    private const string AfterMessage="adventure,message,combat,battle_result,town,hero_screen,exchange";

    /// Anything a town building can open.
    private const string AnyTownScreen="town,town_hall,building_confirmation,recruitment,tavern,"
        +"marketplace,hero_screen,message,spellbook,split_stack,exchange,thieves_guild,level_up,"
        +"mage_guild,town_fort";

    private static int Suffix(string key, int part) => int.Parse(key.Split(':')[part]);

    public static GameCommand ForAction(AvailableAction action, Observation before) => action.Key switch
    {
        // Main menu and scenario setup.
        "menu:new" => new("game_type", Deliveries.Native(20, 101)),
        "menu:load" => new("game_type", Deliveries.Native(20, 102)),
        "menu:back" => new("main_menu", Deliveries.Native(21, 104)),
        // Single player leads to the scenario list after "new game" and to the browser after
        // "load game"; the reader names the browser by the button it carries.
        "menu:single" => new("scenario_selection,load_game,save_game", Deliveries.Native(21, 100)),
        "scenario:back" => new("main_menu", Deliveries.Native(22)),
        "scenario:maps" => new("scenario_selection", Deliveries.Native(23, 128)),
        "scenario:players" => new("scenario_selection", Deliveries.Native(23, 129)),
        "scenario:random" => new("scenario_selection", Deliveries.Native(23, 130)),
        "scenario:start" => new("adventure", Deliveries.Native(25)) { TimeoutSeconds = 10 },
        _ when action.Key.StartsWith("setup:") => new("scenario_selection",
            Deliveries.Native(24, c => ScenarioReader.Controls.Single(x => ScenarioReader.Key(x) == c.Element).Id))
            { Confirm = Confirm.SetupChoice },

        // Modal questions: ordinary presses on the dialog's own buttons. Answering one can start a
        // battle, open a town or a hero screen, or chain into the next message, so the landing is
        // not always the map.
        "message:accept" => new(AfterMessage, Deliveries.Control(30722, "iokay.def")),
        "message:confirm" => new(AfterMessage, Deliveries.Control(30725, "iokay.def")),
        "message:decline" => new(AfterMessage, Deliveries.Control(30726, "icancel.def")),

        // Adventure map. Keys are the ones the manual lists under Section IV, Keyboard Shortcuts.
        "turn:end" => new("adventure", Deliveries.Key(0x45, 0x12)) { Confirm = Confirm.TurnAdvanced },
        "hero:select" => new("adventure", SelectOwnHero),
        _ when action.Key.StartsWith("hero:sheet:") => new("hero_screen,adventure", OpenHeroSheet),
        // "M - Moves current hero" along the planned path.
        "hero:move" => new("adventure", Deliveries.Key(0x4d, 0x32)),
        // "Arrow Keys - Moves current hero": one step in a direction, no route planning involved.
        // The diagonals are the numeric keypad, the way the game has always taken them.
        _ when action.Key.StartsWith("hero:step:") => new("adventure,message,town,hero_screen,combat,battle_result",
            Deliveries.Key(StepKey(action.Key).Key, StepKey(action.Key).Scan)) { Confirm = Confirm.HeroMoved },
        // "Ctrl + Arrow Keys - Scrolls Adventure Map".
        _ when action.Key.StartsWith("view:scroll:") => new("adventure",
            Deliveries.KeyWithControl(StepKey(action.Key).Key, StepKey(action.Key).Scan)),
        "hero:sleep" => new("adventure", Deliveries.Key(0x5a, 0x2c)),
        "hero:wake" => new("adventure", Deliveries.Key(0x57, 0x11)),
        "game:kingdom" => new("kingdom_overview", Deliveries.Key(0x4b, 0x25)),
        // Screens the manual reaches with a single letter; all of them read-only except the market.
        "game:world_view" => new("world_view", Deliveries.Key(0x56, 0x2f)),
        "game:puzzle" => new("puzzle_map", Deliveries.Key(0x50, 0x19)),
        "game:marketplace" => new("marketplace", Deliveries.Key(0x42, 0x30)),
        "game:thieves_guild" => new("thieves_guild", Deliveries.Key(0x47, 0x22)),
        "game:adventure_options" => new("adventure_options", Deliveries.Key(0x41, 0x1e)),
        "game:quest_log" => new("quest_log", Deliveries.Key(0x51, 0x10)),
        "game:scenario_info" => new("scenario_info", Deliveries.Key(0x49, 0x17)),

        // System options, save and load.
        "game:save" => new("save_game", Deliveries.Key(0x53, 0x1f)),
        "game:load" => new("message", Deliveries.Key(0x4c, 0x26)),
        "game:main_menu" => new("main_menu", Deliveries.Control(108, "somain.def")),
        "save:confirm" => new("message", Deliveries.Control(186, "scnrsav.def")),
        "load:confirm" => new("adventure", Deliveries.Control(186, "scnrlod.def")) { Confirm = Confirm.PartyLoaded },
        "load:back" => new("main_menu", Deliveries.Control(188, "scnrback.def", "gspexit.def")),
        _ when action.Key.StartsWith("load:select:") || action.Key.StartsWith("load:open:") =>
            new("load_game", SelectSaveRow),

        // Town.
        "town:construction" => new("town_hall", Deliveries.Native(4)),
        "town:close" => new("adventure", Deliveries.Key(0x1b, 0x01)),
        "construction:close" => new("town", Deliveries.Native(10)),
        "building:cancel" => new("town_hall", Deliveries.Native(6)),
        "building:buy" => new("town", Deliveries.Native(7)),
        _ when action.Key.StartsWith("building:inspect:") =>
            new("building_confirmation", Deliveries.Native(5, c => Suffix(c.Element, 2))),
        _ when action.Key.StartsWith("town:open:") => new("town", OpenTown),
        "town:tavern" => new("tavern", ClickBuilding(5)),
        // A building opens whatever screen it owns; the landing is therefore not fixed.
        _ when action.Key.StartsWith("town:building:") => new(AnyTownScreen,
            ClickBuilding(c => Suffix(c.Element, 2))),
        // The castle screen lists every tier; pressing its dwelling opens that tier's recruitment.
        _ when action.Key.StartsWith("fort:recruit:") => new("recruitment,town_fort,message",
            RecruitFromFort),
        _ when action.Key.StartsWith("town:recruit:") =>
            new("recruitment", ClickBuilding(c => 30 + Suffix(c.Element, 2))),
        // Manual (Town Garrison): highlight the hero portrait, then click the banner left of the
        // first garrison slot; the game then merges garrison and hero army under the hero.
        "town:lead" => new("town", Deliveries.TwoSlots(483, 387)) { Confirm = Confirm.GarrisonChanged },
        "town:banner" => new("town", Deliveries.Slot(241, 387, 58, 64, "Garrison banner control not found in the town dialog")),
        // Town screen: "Space - Switches visiting/garrison heroes", "Up Arrow - Previous town",
        // "Down Arrow - Next town".
        "hero:switch" => new("town", Deliveries.Key(0x20, 0x39)),
        "town:previous" => new("town", Deliveries.Key(0x26, 0x48)),
        "town:next" => new("town", Deliveries.Key(0x28, 0x50)),
        "hero:out" => new("town", Deliveries.TwoSlots(387, 483)),
        _ when action.Key.StartsWith("town:take:") => new("town", TakeGarrisonStack) { Confirm = Confirm.GarrisonChanged },

        // Level-up: the two offered skills are picture buttons, and the confirm button only
        // becomes usable once one of them is chosen.
        _ when action.Key.StartsWith("level:choose:") => new("level_up", SelectLevelSkill),
        "level:accept" => new("adventure,town,message,combat,battle_result",
            Deliveries.Control(30722, "iokay.def")),

        // Hero screens.
        // Esc returns to whichever screen opened this one, so both are legitimate landings.
        "hero:close" => new("adventure,town", Deliveries.Key(0x1b, 0x01)),
        // The kingdom overview has no Esc: it closes on its own exit button, bottom right.
        "kingdom:close" => new("adventure,town", Deliveries.Dismiss),
        // Every one of these windows closes on its own button; Esc is the fallback when the
        // window does not publish one.
        "screen:close" => new("adventure,town,combat,hero_screen", Deliveries.Dismiss),
        "split:cancel" => new("town,hero_screen,exchange", Deliveries.Key(0x1b, 0x01)),
        // The exchange window closes with the game's ordinary Esc, like the other hero screens.
        // It opens both from a meeting on the map and from the town screen.
        "exchange:done" => new("adventure,town", Deliveries.Key(0x1b, 0x01)),

        // Tavern and recruitment.
        "tavern:hire" => new("town", Deliveries.Key(0x0d, 0x1c)),
        "tavern:close" => new("town", Deliveries.Key(0x1b, 0x01)),
        "recruit:max" => new("recruitment", Deliveries.Key(0x4d, 0x32)),
        "recruit:buy" => new("town", Deliveries.Key(0x0d, 0x1c)),
        "recruit:cancel" => new("town", Deliveries.Key(0x1b, 0x01)),

        // Combat.
        "combat:spellbook" => new("spellbook", Deliveries.Key(0x43, 0x2e)),
        "spellbook:close" or "spell:cancel" => new("combat", Deliveries.Key(0x1b, 0x01)),
        _ when action.Key.StartsWith("spellbook:select:") => new("combat", SelectSpell),
        _ when action.Key.StartsWith("spell:target:") => new("combat",
            Deliveries.CombatHex(c => StackHex(c, c.Element[13..])))
            { Confirm = Confirm.CombatLog | Confirm.ManaSpent, BattleMayEnd = true },
        "combat:wait" => new("combat", Deliveries.Key(0x57, 0x11))
            { Confirm = Confirm.CombatTurn | Confirm.CombatLog, TimeoutSeconds = 10, BattleMayEnd = true },
        "combat:defend" => new("combat", Deliveries.Key(0x44, 0x20))
            { Confirm = Confirm.CombatTurn | Confirm.CombatLog, TimeoutSeconds = 10, BattleMayEnd = true },
        "combat:retreat" => new("combat", Deliveries.Control(2002)),
        "combat:auto" => new("combat", Deliveries.Control(2004)),
        // Pressing a stack's cell opens the creature card the player sees, with upgrade and
        // dismiss on it; the cells themselves are addressed by ArmyCell.
        "army:upgrade" => new("creature_card",Deliveries.Control(300)),
        "army:dismiss" => new("creature_card",Deliveries.Control(30723)),
        "army:close" => new("creature_card",Deliveries.Control(30722)),
        "split:confirm" => new("split_army",Deliveries.Control(30722)),
        "split:decline" => new("split_army",Deliveries.Control(30721)),
        // Stacks are addressed the way the agent thinks about them — by creature name — and the
        // adapter finds the row and the slot. The numeric form stays legal for the rare case where
        // the same creature stands in two slots of one row.
        _ when action.Key.StartsWith("army:open:",StringComparison.Ordinal)
            => new("town",OpenArmyStack),
        _ when action.Key.StartsWith("army:give:",StringComparison.Ordinal)
            => new("town",MoveStack(true,false)){Confirm=Confirm.GarrisonChanged},
        _ when action.Key.StartsWith("army:take:",StringComparison.Ordinal)
            => new("town",MoveStack(false,false)){Confirm=Confirm.GarrisonChanged},
        _ when action.Key.StartsWith("army:merge:",StringComparison.Ordinal)
            => new("town",MoveStack(false,true)){Confirm=Confirm.GarrisonChanged},
        _ when action.Key.StartsWith("army:join:",StringComparison.Ordinal)
            => new("town",JoinStacks){Confirm=Confirm.GarrisonChanged},
        "army:deselect" => new("town",async(context,ct)=>await ClearSelection(context,ct)),
        // Combat screen: "R - Retreat", "S - Surrender", "O - Combat Options", "T - View troop".
        "combat:surrender" => new("combat,message", Deliveries.Key(0x53, 0x1f)),
        "combat:options" => new("combat,system_options", Deliveries.Key(0x4f, 0x18)),
        // Spell book: "A - Displays adventure spells", "C - Combat spells".
        "spellbook:adventure" => new("spellbook", Deliveries.Key(0x41, 0x1e)),
        "spellbook:combat" => new("spellbook", Deliveries.Key(0x43, 0x2e)),
        _ when action.Key.StartsWith("combat:move:") => new("combat",
            Deliveries.CombatHex(c => Suffix(c.Element, 2)))
            { Confirm = Confirm.CombatTurn, TimeoutSeconds = 10, BattleMayEnd = true },
        _ when action.Key.StartsWith("combat:attack:") => new("combat",
            Deliveries.CombatHex(c => StackHex(c, c.Element[14..])))
            { Confirm = Confirm.CombatTurn | Confirm.CombatLog, TimeoutSeconds = 10, BattleMayEnd = true },
        "battle:accept" => new("adventure", Deliveries.Key(0x0d, 0x1c)),

        _ => throw new InvalidOperationException("Action not implemented"),
    };

    /// Raw UI elements the adapter publishes but that carry no semantic action key.
    public static GameCommand ForElement(Observation before, string elementKey)
    {
        var item = before.Elements.SingleOrDefault(e => e.Key == elementKey);
        bool freeform = before.Screen is "message" or "exchange";
        if (item is null || (!item.Interactive && !freeform))
            throw new InvalidOperationException("Action unavailable");
        return (before.Screen, item.Id, item.Asset) switch
        {
            ("adventure", 10, "iam009.def") => new("system_options", Deliveries.Native(1)),
            ("system_options", 30722, "soretrn.def") => new("adventure", Deliveries.Native(2)),
            ("system_options", 102, "soload.def") => new("message", Deliveries.Key(0x4c, 0x26)),
            ("system_options", 106, "sosave.def") => new("save_game", Deliveries.Key(0x53, 0x1f)),
            ("save_game", 188, "gspexit.def") => new("system_options", Deliveries.Requested),
            ("load_game", 188, "scnrback.def") => new("main_menu", Deliveries.Requested),
            ("message", 30722, "iokay.def") => new("adventure", Deliveries.Control(30722, "iokay.def")),
            ("message", 30725, "iokay.def") => new("adventure", Deliveries.Control(30725, "iokay.def")),
            ("message", 30726, "icancel.def") => new("adventure", Deliveries.Control(30726, "icancel.def")),
            // Any other control of a message or exchange dialog: the reward choice inside a
            // treasure chest, an army slot or an exchange arrow. The dialog owns the outcome.
            _ when freeform => new(before.Screen, Deliveries.Requested),
            _ => throw new InvalidOperationException("Use an available semantic action"),
        };
    }

    private static int StackHex(CommandContext context, string stackId) =>
        context.Before.Combat!.Stacks.Single(s => s.Id == stackId).Hex;

    private static Deliver ClickBuilding(int building) => ClickBuilding(_ => building);

    private static Deliver ClickBuilding(Func<CommandContext, int> building) => async (context, ct) =>
    {
        var point = new TownReader(context.Game, context.Player).BuildingPoint(building(context));
        await Deliveries.Press(context, point.X, point.Y, ct);
    };

    /// Own town entry: the town portrait in the adventure sidebar is pressed the same way a
    /// player presses it. The press is repeated once because the sidebar can still be animating.
    private static readonly Deliver OpenTown = async (context, ct) =>
    {
        int townId = Suffix(context.Element, 2);
        if (context.Before.Towns.Count != 1 || context.Before.Towns[0].Id != townId)
            throw new InvalidOperationException("Town sidebar selection currently verified for one owned town only");
        var portrait = context.Before.Elements.Single(e => e.Id == 32 && e.Asset == "itpa.def");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await Deliveries.Press(context, portrait, ct);
            await Task.Delay(150, CancellationToken.None);
            if (context.Reader.Observe().Screen == "town") break;
        }
    };

    /// "H - Selects next hero": the game's own way to move between the player's heroes on the map
    /// and to pick one up again after a screen dropped the selection. With a single hero it simply
    /// selects that one.
    private static readonly Deliver SelectOwnHero = async (context, _) =>
    {
        int? before = context.Before.Hero?.Id;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await context.Game.KeyAsync(0x48, 0x23);
            await Task.Delay(300, CancellationToken.None);
            var hero = context.Reader.Observe().Hero;
            if (hero is not null && hero.Id != before) break;
            if (hero is not null && before is null) break;
        }
    };

    /// A save file or folder row is an ordinary control of the browser dialog; the game keeps
    /// its own list state, so the row is pressed inside the bounds it reports.
    private static readonly Deliver SelectSaveRow = async (context, ct) =>
    {
        int index = Suffix(context.Element, 2);
        var entry = context.Before.Saves?.Entries.SingleOrDefault(e => e.Index == index)
            ?? throw new InvalidOperationException("Unknown save entry; observe the browser again");
        bool folder = context.Element.StartsWith("load:open:");
        if (entry.Folder != folder || entry.Width < 1 || entry.Height < 1)
            throw new InvalidOperationException("Save row is not selectable in the visible window");
        await Deliveries.Press(context, entry.X + entry.Width / 2, entry.Y + entry.Height / 2, ct);
    };

    private static readonly Deliver SelectLevelSkill = async (context, ct) =>
    {
        int id = Suffix(context.Element, 2);
        await Deliveries.Press(context, context.Before.Elements.Single(e => e.Id == id), ct);
    };

    /// One press on a hero portrait selects that hero; a press on the hero already selected opens
    /// his screen. Both cases are covered by pressing, looking, and pressing once more.
    private static readonly Deliver OpenHeroSheet = async (context, ct) =>
    {
        var portrait = context.Before.Elements.First(e => e.Id == 15 + Suffix(context.Element, 2));
        await Deliveries.Press(context, portrait, ct);
        await Task.Delay(400, CancellationToken.None);
        try { if (context.Reader.Observe().Screen == "hero_screen") return; }
        catch (InvalidOperationException) { return; }
        await Deliveries.Press(context, portrait, ct);
    };

    /// The two army rows of the town screen are a fixed grid: seven 58x64 cells starting at x=305
    /// with a 62 pixel step, the garrison row at y=387 and the visiting hero's at y=483 — the same
    /// coordinates the town's own portrait and banner controls use. Addressing a cell by control id
    /// was wrong: ids are not handed out per display slot, so a press could land two cells away.
    private static (int X,int Y) ArmyCell(CommandContext context,bool garrison,int slot)
    {
        if (slot is < 0 or > 6) throw new InvalidOperationException("Слот вне диапазона 0..6");
        return context.Reader.FindControl(305 + 62 * slot, garrison ? 387 : 483, 58, 64)
            ?? throw new InvalidOperationException(
                $"Клетка {slot} {(garrison ? "верхнего" : "нижнего")} ряда не найдена на экране города");
    }

    /// Leaves the screen with nothing picked up. A stack stays selected until it is put somewhere,
    /// so a gesture that starts while an unrelated stack is held would move that stack instead of
    /// the wanted one. Pressing the held cell a second time opens its card, and closing the card
    /// releases it — that is the player's own way out, and it is verified rather than assumed.
    private static async Task ClearSelection(CommandContext context,CancellationToken ct)
    {
        var held=context.Reader.SelectedArmyCell();
        if(held is not {} cell)return;
        var (x,y)=ArmyCell(context,cell.Garrison,cell.Slot);
        await Deliveries.Press(context,x,y,ct);
        await Task.Delay(400,CancellationToken.None);
        if(context.Reader.Observe().Screen=="creature_card")
        {
            var close=context.Reader.FindControlById(30722);
            if(close is not null)
                await Deliveries.Press(context,close.X+close.Width/2,close.Y+close.Height/2,ct);
            await Task.Delay(400,CancellationToken.None);
        }
        if(context.Reader.SelectedArmyCell() is not null)
            throw new InvalidOperationException(
                "На экране остался выделенный отряд, и его не удалось снять. "
                +"Сними его вручную действием army:deselect и повтори.");
    }

    /// A stack answers the way a hero portrait does: the first press selects it, the second opens
    /// its card. The pictures sit at 101 plus the slot — 108 and up are only the count labels — and
    /// they carry neither text nor a button image, so they are found in the dialog itself.
    private static readonly Deliver OpenArmyStack = async (context, ct) =>
    {
        // The key names the row and the slot: h3 is the visiting hero's fourth stack, g0 the
        // garrison's first. The two rows are different controls, so both have to be addressable.
        string where = context.Element.Split(':')[^1];
        bool garrison = where.StartsWith("g", StringComparison.OrdinalIgnoreCase);
        await ClearSelection(context, ct);
        var (x, y) = ArmyCell(context, garrison, int.Parse(where[1..]));
        await Deliveries.Press(context, x, y, ct);
        await Task.Delay(400, CancellationToken.None);
        try { if (context.Reader.Observe().Screen == "creature_card") return; }
        catch (InvalidOperationException) { return; }
        await Deliveries.Press(context, x, y, ct);
    };

    /// Moving a stack is two plain presses, the way a player does it: press the stack, then press
    /// the slot it should land in. The garrison pictures are at 101 plus the slot, the visiting
    /// hero's at 126 plus the slot.
    ///
    /// Where it lands decides what happens: a slot holding the same creature merges the two stacks
    /// silently, an empty slot makes the game ask how to divide. Merging is nearly always the
    /// intent, so a matching slot is preferred; `requireMerge` refuses the move outright when there
    /// is nothing to merge with, so "объединить" never turns into a split dialog by accident.
    private static Deliver MoveStack(bool toGarrison,bool requireMerge) => async (context, ct) =>
    {
        string wanted = context.Element[(context.Element.IndexOf(':') + 1)..];
        wanted = wanted[(wanted.IndexOf(':') + 1)..];
        var town = context.Before.Towns.FirstOrDefault()
            ?? throw new InvalidOperationException("Экран города не прочитан");
        var visiting = context.Before.Heroes.FirstOrDefault(h => h.Id == town.VisitingHero);
        var keeper = context.Before.Heroes.FirstOrDefault(h => h.Id == town.GarrisonHero);
        int[] upperTypes = keeper?.ArmyTypes ?? town.GarrisonTypes;
        int[] upperCounts = keeper?.ArmyCounts ?? town.GarrisonCounts;
        int[] fromTypes = (toGarrison ? visiting?.ArmyTypes : upperTypes) ?? [];
        int[] fromCounts = (toGarrison ? visiting?.ArmyCounts : upperCounts) ?? [];
        int[] toTypes = (toGarrison ? upperTypes : visiting?.ArmyTypes) ?? [];
        int slot = ResolveStack(wanted, fromTypes, fromCounts,
            toGarrison ? "в армии героя" : "в гарнизоне");
        int moving = slot < fromTypes.Length ? fromTypes[slot] : -1;
        int target = moving >= 0 ? Array.FindIndex(toTypes, type => type == moving) : -1;
        if (target < 0 && requireMerge)
            throw new InvalidOperationException(
                $"Объединять не с чем: {(toGarrison ? "в гарнизоне" : "у героя")} нет отряда «{GameReference.Creature(moving)}». " +
                $"Перенести отдельным отрядом — army:{(toGarrison ? "give" : "take")}:{wanted}");
        int countBase = toGarrison ? 108 : 133;
        if (target < 0)
            for (int candidate = 0; candidate < 7 && target < 0; candidate++)
            {
                var label = context.Before.Elements.FirstOrDefault(e => e.Id == countBase + candidate);
                if (label is null || string.IsNullOrWhiteSpace(label.Text)) target = candidate;
            }
        if (target < 0) throw new InvalidOperationException(
            "Свободных клеток нет и сливать не с чем — семь слотов заняты другими существами");
        await ClearSelection(context, ct);
        var source = ArmyCell(context, !toGarrison, slot);
        var destination = ArmyCell(context, toGarrison, target);
        await Deliveries.Press(context, source.X, source.Y, ct);
        await Task.Delay(250, CancellationToken.None);
        var held = context.Reader.SelectedArmyCell();
        if (held is null || held.Value.Garrison == toGarrison || held.Value.Slot != slot)
            throw new InvalidOperationException(
                $"Нажатие по клетке {slot} не взяло отряд: рамка выделения не появилась там, где ожидалась. "
                +"Ничего не перенесено, состояние не изменилось.");
        await Deliveries.Press(context, destination.X, destination.Y, ct);
    };

    /// A stack is named either by creature — the way the agent asks for it — or by slot number for
    /// the case where the same creature stands twice in one row.
    private static int ResolveStack(string wanted,int[] types,int[] counts,string where)
    {
        if (int.TryParse(wanted, out int index))
        {
            if (index is < 0 or > 6) throw new InvalidOperationException("Слот вне диапазона 0..6");
            return index;
        }
        int hash = wanted.IndexOf('#');
        if (hash > 0 && int.TryParse(wanted[(hash + 1)..], out int pinned) && pinned is >= 0 and < 7)
            return pinned;
        for (int slot = 0; slot < types.Length; slot++)
            if (slot < counts.Length && counts[slot] > 0
                && string.Equals(GameReference.Creature(types[slot]), wanted, StringComparison.OrdinalIgnoreCase))
                return slot;
        string present = string.Join(", ", types.Zip(counts)
            .Where(s => s.First >= 0 && s.Second > 0)
            .Select(s => $"{GameReference.Creature(s.First)} x{s.Second}"));
        throw new InvalidOperationException(
            $"Отряда «{wanted}» {where} нет. Есть: {(present.Length > 0 ? present : "пусто")}");
    }

    /// Two stacks of one creature in the same row are joined by the same two presses as a move
    /// across rows: press one, press the other. The key spells the row and both slots, because the
    /// name alone does not tell them apart.
    private static readonly Deliver JoinStacks = async (context, ct) =>
    {
        string where = context.Element["army:join:".Length..];
        int plus = where.IndexOf('+');
        bool garrison = where[0] is 'g' or 'G';
        int first = int.Parse(where[1..plus]), second = int.Parse(where[(plus + 1)..]);
        await ClearSelection(context, ct);
        var source = ArmyCell(context, garrison, first);
        var target = ArmyCell(context, garrison, second);
        await Deliveries.Press(context, source.X, source.Y, ct);
        await Task.Delay(250, CancellationToken.None);
        var held = context.Reader.SelectedArmyCell();
        if (held is null || held.Value.Garrison != garrison || held.Value.Slot != first)
            throw new InvalidOperationException(
                $"Нажатие по клетке {first} не взяло отряд: рамка выделения не появилась там, где ожидалась. "
                +"Ничего не слито, состояние не изменилось.");
        await Deliveries.Press(context, target.X, target.Y, ct);
    };

    private static readonly Deliver RecruitFromFort = async (context, ct) =>
    {
        int tier = Suffix(context.Element, 2);
        await Deliveries.Press(context, context.Before.Elements.First(e => e.Id == 1 + tier), ct);
    };

    private static readonly Deliver SelectSpell = async (context, ct) =>
    {
        int id = Suffix(context.Element, 2);
        await Deliveries.Press(context, context.Before.Elements.Single(e => e.Id == id), ct);
    };

    /// Town garrison slot: the slot widget receives the same press the player's own click raises,
    /// and the game hands the stack to the visiting hero.
    private static readonly Deliver TakeGarrisonStack = async (context, ct) =>
    {
        int slot = int.Parse(context.Element["town:take:".Length..]);
        if (slot is < 0 or > 6) throw new InvalidOperationException("Garrison slot outside supported range");
        var point = context.Reader.FindControl(305 + 62 * slot, 387, 58, 64)
            ?? throw new InvalidOperationException("Garrison slot control not found in the town dialog");
        await Deliveries.Press(context, point.X, point.Y, ct);
    };
}
