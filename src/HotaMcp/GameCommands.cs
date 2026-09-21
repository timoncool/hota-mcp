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

    /// Press one dialog control identified by its own id and button asset.
    public static Deliver Control(int id, params string[] assets) => async (context, ct) =>
    {
        var button = context.Before.Elements.Single(e =>
            e.Id == id && (assets.Length == 0 || assets.Contains(e.Asset)) && e.Interactive);
        await Press(context, button, ct);
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
    private static int Suffix(string key, int part) => int.Parse(key.Split(':')[part]);

    public static GameCommand ForAction(AvailableAction action, Observation before) => action.Key switch
    {
        // Main menu and scenario setup.
        "menu:new" => new("game_type", Deliveries.Native(20, 101)),
        "menu:load" => new("game_type", Deliveries.Native(20, 102)),
        "menu:back" => new("main_menu", Deliveries.Native(21, 104)),
        "menu:single" => new("scenario_selection", Deliveries.Native(21, 100)),
        "scenario:back" => new("main_menu", Deliveries.Native(22)),
        "scenario:maps" => new("scenario_selection", Deliveries.Native(23, 128)),
        "scenario:players" => new("scenario_selection", Deliveries.Native(23, 129)),
        "scenario:random" => new("scenario_selection", Deliveries.Native(23, 130)),
        "scenario:start" => new("adventure", Deliveries.Native(25)) { TimeoutSeconds = 10 },
        _ when action.Key.StartsWith("setup:") => new("scenario_selection",
            Deliveries.Native(24, c => ScenarioReader.Controls.Single(x => ScenarioReader.Key(x) == c.Element).Id))
            { Confirm = Confirm.SetupChoice },

        // Modal questions: ordinary presses on the dialog's own buttons.
        "message:accept" => new("adventure", Deliveries.Control(30722, "iokay.def")),
        "message:confirm" => new("adventure", Deliveries.Control(30725, "iokay.def")),
        "message:decline" => new("adventure", Deliveries.Control(30726, "icancel.def")),

        // Adventure map.
        "turn:end" => new("adventure", Deliveries.Key(0x45, 0x12)) { Confirm = Confirm.TurnAdvanced },
        "hero:select" => new("adventure", SelectOwnHero),
        // Manual, Section IV: "M - Moves current hero" along the planned path.
        "hero:move" => new("adventure", Deliveries.Key(0x4d, 0x32)),

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
        _ when action.Key.StartsWith("town:recruit:") =>
            new("recruitment", ClickBuilding(c => 30 + Suffix(c.Element, 2))),
        // Manual (Town Garrison): highlight the hero portrait, then click the banner left of the
        // first garrison slot; the game then merges garrison and hero army under the hero.
        "town:lead" => new("town", Deliveries.TwoSlots(483, 387)),
        "town:banner" => new("town", Deliveries.Slot(241, 387, 58, 64, "Garrison banner control not found in the town dialog")),
        // Manual, Section IV, town screen: "Space - Switches visiting/garrison heroes".
        "hero:switch" => new("town", Deliveries.Key(0x20, 0x39)),
        "hero:out" => new("town", Deliveries.TwoSlots(387, 483)),
        _ when action.Key.StartsWith("town:take:") => new("town", TakeGarrisonStack),

        // Hero screens.
        // Esc returns to whichever screen opened this one, so both are legitimate landings.
        "hero:close" => new("adventure,town", Deliveries.Key(0x1b, 0x01)),
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

    /// Own hero selection: the ordinary hotkey H cycles the player's heroes and centres the view.
    private static readonly Deliver SelectOwnHero = async (context, _) =>
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await context.Game.KeyAsync(0x48, 0x23);
            await Task.Delay(300, CancellationToken.None);
            if (context.Reader.Observe().Hero is not null) break;
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
