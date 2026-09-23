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
    /// Gold must have been spent: the purchase went through whatever screen it lands on.
    GoldSpent = 256,
    /// The screen itself must be a different one: closing a window, not a popup inside it.
    ScreenLeft = 512,
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
    /// Kept only because the screen adapters still reference the type; no command uses it any
    /// more. Calling a screen's own command with a parameter bypassed the interface and crashed
    /// the game, so every action presses the button a player presses.
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
    /// The НАЧАТЬ button of the scenario screen, found by its own caption rather than by a code:
    /// pressing it is the only path that commits the map and the player slots before the game
    /// starts.
    public static Deliver StartScenario => async (context, ct) =>
    {
        // НАЧАТЬ is control 186, drawn from scnrbeg.def, beside ВЫЙТИ at 188. It carries no text of
        // its own, so it is addressed by id; the caption lives in the picture.
        var box = context.Reader.FindControlById(186)
            ?? throw new InvalidOperationException(
                "Кнопка НАЧАТЬ (контрол 186) на экране не найдена: панель выбора сценария не "
                +"открыта. Открой её и убедись, что карта выбрана — иначе игра начнётся без города "
                +"и героя и будет проиграна сразу.");
        await Press(context, box.X + box.Width / 2, box.Y + box.Height / 2, ct);
    };

    /// Some keys carry the control number in the key itself; the id is worked out from the
    /// command rather than written down twice.
    public static Deliver Control(Func<CommandContext,int> id) => async (context, ct) =>
    {
        int wanted = id(context);
        var box = context.Reader.FindControlById(wanted)
            ?? throw new InvalidOperationException($"Кнопка {wanted} на экране не найдена");
        await Press(context, box.X + box.Width / 2, box.Y + box.Height / 2, ct);
    };

    public static Deliver Control(int id, params string[] assets) => async (context, ct) =>
    {
        var button = context.Before.Elements.FirstOrDefault(e =>
            e.Id == id && (assets.Length == 0 || assets.Contains(e.Asset, StringComparer.OrdinalIgnoreCase)) && e.Interactive);
        // A named picture is a stronger identity than a number: ids shift between screens and
        // builds, the picture does not. When the id misses but the picture is on screen, that is
        // the button.
        button ??= assets.Length == 0 ? null : context.Before.Elements.FirstOrDefault(e =>
            e.Asset is not null && assets.Contains(e.Asset, StringComparer.OrdinalIgnoreCase) && e.Interactive);
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

    /// The game works out what a click means from where the cursor already stands, not from the
    /// coordinates the click carries: a battlefield target ignored a click sent from elsewhere, and
    /// so do the picture grids of the setup screen. So every press walks the cursor onto the spot
    /// first and only then presses, the way a hand does.
    public static async Task Press(CommandContext context, int x, int y, CancellationToken ct)
    {
        if (context.Before.Screen == "popup_choice")
        {
            await context.Game.MouseRealAsync(x, y, context.Before.Width, context.Before.Height, ct);
            return;
        }
        await context.Game.MouseAsync(x, y, context.Before.Width, context.Before.Height, false, CancellationToken.None);
        // 120 ms was not always enough: the OK of a resource message opened by a step (windmill,
        // wood warehouse) ignored the press twice, while the same point after 200 ms closed it.
        await Task.Delay(200, CancellationToken.None);
        await context.Game.MouseAsync(x, y, context.Before.Width, context.Before.Height, true, CancellationToken.None);
    }

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

    /// Aiming a melee attack. The game reads the direction of the blow from WHERE inside the
    /// defender's cell the cursor is: a press on the dead centre is not an attack from anywhere,
    /// and the order is dropped without a word. Pressing on the side that faces the attacker is
    /// what a player does without thinking, and it is what makes the stack walk up and strike.
    public static Deliver CombatAttack(Func<CommandContext, CombatStack> target, Func<CommandContext, int?>? from = null) => async (context, ct) =>
    {
        var reader = new CombatReader(context.Game, context.Player);
        var combat = context.Before.Combat ?? throw new InvalidOperationException("Бой не прочитан");
        var active = combat.Stacks.FirstOrDefault(s => s.Id == combat.ActiveStack)
            ?? throw new InvalidOperationException("Активный отряд не определён");
        var victim = target(context);
        var around = victim.Around().ToList();
        // Which side a melee blow comes from is decided by the part of the defender's hex the
        // cursor stands on. Pointing at the middle left that choice to chance, and a side the
        // attacker cannot reach meant no blow at all. So the cursor is set on the edge of the
        // defender's hex that faces a neighbouring hex the attacker can actually reach, the one
        // nearest to him. A shooter's shot goes wherever inside the hex the cursor is.
        var reachable = new HashSet<int>(combat.ReachableHexes);
        // A melee blow needs a free hex next to the defender that the attacker can step onto, or
        // the attacker already standing there. Without one the press lands on nothing, so it is
        // refused before anything is sent.
        bool touching = active.Hexes.Any(around.Contains);
        if (!active.Shooter && !touching && !around.Any(reachable.Contains))
            throw new ActionRefused(ActionRefused.MeleeUnreachable,
                $"{active.Name} в этот ход не дотягиваются до «{victim.Name}»: рядом с целью нет доступной клетки. "
                + "Подойди ближе (combat:move:<клетка>), подожди (combat:wait) или встань в защиту (combat:defend). Ничего не отправлено.");
        // Already standing next to the defender: the blow comes from where the attacker is.
        int? chosen = from?.Invoke(context);
        if (chosen is int wanted && !around.Contains(wanted))
            throw new ActionRefused(ActionRefused.HexNotAdjacent,$"Клетка {wanted} не соседняя с целью — оттуда не ударить");
        if (chosen is int side0 && !reachable.Contains(side0) && !active.Hexes.Contains(side0))
            throw new ActionRefused(ActionRefused.HexUnreachable,$"На клетку {side0} в этот ход не встать — оттуда не ударить. Ничего не отправлено.");
        int? approach = chosen
            ?? active.Hexes.Where(around.Contains).Cast<int?>().FirstOrDefault()
            ?? around.Where(reachable.Contains)
                .OrderBy(h => HexDistance(h, active.Hex)).Cast<int?>().FirstOrDefault();
        int targetHex = approach is int near ? Facing(victim, near) : victim.Hex;
        var to = reader.Center(targetHex);
        if (approach is int side)
        {
            // The game reads the side of a blow from the sector of the defender's hex under the
            // cursor. A third of the way from its middle toward the chosen neighbour stays inside
            // the defender's hex and in that neighbour's sector.
            var edge = reader.Center(side);
            to = (to.X + (edge.X - to.X) / 3, to.Y + (edge.Y - to.Y) / 3);
        }
        // The game works out what a click means from where the cursor already is: it decides
        // «attack this stack» while the mouse travels over the defender, and a click that arrives
        // without that journey is discarded. So the cursor is moved first, exactly as a hand does.
        await context.Game.MouseAsync(to.X, to.Y, context.Before.Width, context.Before.Height, false, ct);
        await Task.Delay(200, CancellationToken.None);
        // Hovering a stack opens the expansion's stats panel, and by the right edge of the field
        // it covers the stack itself: a click there lands on the panel and is lost. The aim then
        // moves to a part of the same hex the panel leaves free, keeping to the side of the blow.
        bool Covered((int X, int Y) p) => context.Reader.Overlays().Any(o => p.X >= o.X && p.X < o.X + o.W && p.Y >= o.Y && p.Y < o.Y + o.H);
        if (Covered(to))
        {
            var centre = reader.Center(targetHex);
            var toward = approach is int a ? reader.Center(a) : centre;
            var free = new List<(int X, int Y)>();
            for (int ox = -16; ox <= 16; ox += 4)
                for (int oy = -16; oy <= 16; oy += 4)
                    if (Math.Abs(oy) + Math.Abs(ox) * 0.55 <= 18) free.Add((centre.X + ox, centre.Y + oy));
            var overlays = context.Reader.Overlays();
            var pick = free.Where(p => !overlays.Any(o => p.X >= o.X && p.X < o.X + o.W && p.Y >= o.Y && p.Y < o.Y + o.H))
                .OrderByDescending(p => (p.X - centre.X) * (toward.X - centre.X) + (p.Y - centre.Y) * (toward.Y - centre.Y))
                .Cast<(int X, int Y)?>().FirstOrDefault()
                ?? throw new InvalidOperationException("Цель целиком закрыта панелью характеристик — щелчок по ней не дойдёт");
            to = pick;
            await context.Game.MouseAsync(to.X, to.Y, context.Before.Width, context.Before.Height, false, ct);
            await Task.Delay(200, CancellationToken.None);
        }
        await Press(context, to.X, to.Y, ct);
    };

    /// The six hexes around one battlefield hex: seventeen to a row, and even rows sit half a
    /// hex to the right of odd ones — read off the hex boxes the game keeps: hex 133 (odd row)
    /// has its lower neighbours 149 and 150, hex 151 (even row) its upper ones 134 and 135.
    public static IEnumerable<int> HexNeighbours(int hex)
    {
        int row = hex / 17;
        int[] around = row % 2 == 0 ? [-1, 1, -17, -16, 17, 18] : [-1, 1, -18, -17, 16, 17];
        foreach (int d in around)
        {
            int n = hex + d;
            if (n < 0 || n >= 187) continue;
            if (d is -1 or 1 && n / 17 != row) continue;
            yield return n;
        }
    }

    /// The hex of a stack that touches a given neighbour — for a two-hex creature, the half the
    /// blow from there actually lands on.
    public static int Facing(CombatStack stack, int side) =>
        stack.Hexes.Where(h => HexNeighbours(h).Contains(side)).DefaultIfEmpty(stack.Hex).First();

    /// Where a neighbouring hex lies seen from a defender, in the words a player would use.
    public static string SideName(int defender, int side)
    {
        int d = side - defender, row = defender / 17;
        return d switch
        {
            -1 => "слева",
            1 => "справа",
            _ when side / 17 < row && (d == (row % 2 == 0 ? -17 : -18)) => "сверху-слева",
            _ when side / 17 < row => "сверху-справа",
            _ when side / 17 > row && (d == (row % 2 == 0 ? 17 : 16)) => "снизу-слева",
            _ => "снизу-справа",
        };
    }

    /// Rough distance between two hexes, enough to prefer the nearer side of a defender.
    private static int HexDistance(int a, int b) => Math.Abs(a / 17 - b / 17) + Math.Abs(a % 17 - b % 17);
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
    // A dwelling out on the map answers «yes, hire» with its recruitment window, and a level-up
    // can follow a reward, so both are ordinary places for a message to lead to.
    private const string AfterMessage="adventure,message,combat,battle_result,town,hero_screen,exchange,recruitment,level_up,mage_guild,tavern,marketplace,main_menu,system_options,save_game,load_game,scenario_selection,game_type,high_score_name,high_scores";

    /// Anything a town building can open.
    private const string AnyTownScreen="town,town_hall,building_confirmation,recruitment,tavern,"
        +"marketplace,hero_screen,message,spellbook,split_stack,exchange,thieves_guild,level_up,"
        +"mage_guild,town_fort";

    private static int Suffix(string key, int part) => int.Parse(key.Split(':')[part]);

    public static GameCommand ForAction(AvailableAction action, Observation before) => action.Key switch
    {
        // Main menu and scenario setup.
        "menu:new" => new("game_type", Deliveries.Control(101)),
        "menu:highscores" => new("high_scores", Deliveries.Control(103,"mmenuhs.def")),
        "menu:credits" => new("credits", Deliveries.Control(104,"mmenucr.def")),
        // The game asks «Вы действительно хотите выйти?» first.
        "menu:quit" => new("main_menu,message", Deliveries.Control(105,"mmenuqt.def")),
        "menu:load" => new("game_type", Deliveries.Control(102)),
        "menu:back" => new("main_menu", Deliveries.Control(104)),
        // Single player leads to the scenario list after "new game" and to the browser after
        // "load game"; the reader names the browser by the button it carries.
        "menu:multiplayer" => new("game_type,scenario_selection,multiplayer", Deliveries.Control(102,"gtmulti.def")),
        "multiplayer:hotseat" => new("popup_choice,scenario_selection,message,multiplayer", Deliveries.Control(102,"muBhot.def")),
        "multiplayer:cancel" => new("game_type", Deliveries.Control(124,"muBcanc.def")),
        "hotseat:accept" => new("scenario_selection,message,popup_choice", Deliveries.Control(519,"mubchck.def")),
        "hotseat:cancel" => new("multiplayer", Deliveries.Control(520,"muBcanc.def")),
        "menu:campaign" => new("game_type,scenario_selection", Deliveries.Control(101,"gtcampn.def")),
        "menu:tutorial" => new("game_type,scenario_selection", Deliveries.Control(103,"gttutor.def")),
        "menu:single" => new("scenario_selection,load_game,save_game", Deliveries.Control(100)),
        // These two carry no caption at all — the meaning is in the picture, so the picture is the
        // name: TPTav01 hires the chosen hero, TPTav02 opens the Thieves Guild beside it.
        "tavern:hire" => new("town,tavern", Deliveries.Control(12,"TPTav01.def")) { Confirm = Confirm.GarrisonChanged },
        "tavern:thieves" => new("thieves_guild", Deliveries.Control(11,"TPTav02.def")),
        "tavern:pick:слева" => new("tavern", Deliveries.Control(c => 5)),
        "tavern:pick:справа" => new("tavern", Deliveries.Control(c => 6)),
        _ when action.Key.StartsWith("tavern:select:",StringComparison.Ordinal)
            && int.TryParse(action.Key["tavern:select:".Length..],out int who) && who is 1 or 2
            => new("tavern",Deliveries.Control(4+who)),
        "scenario:back" => new("main_menu", Deliveries.Control(188,"scnrback.def")),
        // These three switch the panel. They were sent as the screen's own internal command with a
        // parameter, and that call crashed the game outright — the same bypass that started a
        // scenario with no town and no hero. The buttons are ordinary controls with captions, so
        // they are pressed like a player presses them.
        _ when action.Key.StartsWith("scenario:filter:",StringComparison.Ordinal)
            && int.TryParse(action.Key["scenario:filter:".Length..],out int filter)
            => new("scenario_selection",Deliveries.Control(filter)),
        _ when action.Key.StartsWith("scenario:map:",StringComparison.Ordinal)
            => new("scenario_selection",SelectScenario){TimeoutSeconds=30},
        // The popups of the setup screen: the grids of starting towns and heroes, the drop-down
        // lists and the team agreements. Every one of them is left by pressing what it holds.
        _ when action.Key.StartsWith("выбор:город:",StringComparison.Ordinal)
            => new("scenario_selection,popup_choice",PickTown),
        _ when action.Key.StartsWith("выбор:герой:",StringComparison.Ordinal)
            => new("scenario_selection,popup_choice",PickHero),
        _ when action.Key.StartsWith("выбрать:",StringComparison.Ordinal)
            => new("scenario_selection,popup_choice",PickRow),
        _ when action.Key.StartsWith("команда:",StringComparison.Ordinal)
            => new("popup_choice",PickTeamSlot),
        _ when action.Key.StartsWith("market:give:",StringComparison.Ordinal)
            => new("marketplace", Deliveries.Control(c => 28 + MarketResource(c.Element))),
        _ when action.Key.StartsWith("market:get:",StringComparison.Ordinal)
            => new("marketplace", Deliveries.Control(c => 63 + MarketResource(c.Element))),
        "market:max" => new("marketplace", Deliveries.Control(7, "Ircbtns.def")),
        // The amount is what is received — the number under the right picture, control 12 — because
        // the side given moves in steps of the rate (7 sulphur per crystal).
        _ when action.Key.StartsWith("market:amount:") => new("marketplace", SliderTo("market:amount:", 6, 12)) { TimeoutSeconds = 30 },
        _ when action.Key.StartsWith("split:amount:") => new("split_army", SliderTo("split:amount:", 6, 5)) { TimeoutSeconds = 30 },
        "market:trade" => new("marketplace", Deliveries.Control(5, "TPMrkB.def")) { Confirm = Confirm.None },
        "market:close" => new("town", Deliveries.Control(30722, "iOk6432.def")),
        "popup:подтвердить" => new("scenario_selection,popup_choice",Deliveries.Control(1,"CAMPCHK.def")),
        "popup:отменить" => new("scenario_selection,popup_choice",Deliveries.Control(2,"CAMPCAN.def")),
        // The random map settings share the «setup:» prefix with the player rows; they are told
        // apart by being one of the known generator controls, and must be matched first.
        _ when ScenarioReader.Controls.Any(x => ScenarioReader.Key(x) == action.Key) => new("scenario_selection",
            Deliveries.Control(c => ScenarioReader.Controls.Single(x => ScenarioReader.Key(x) == c.Element).Id))
            { Confirm = Confirm.SetupChoice },
        _ when action.Key.StartsWith("setup:",StringComparison.Ordinal)
            && action.Key.Count(c=>c==':')==3
            && action.Key.Split(':')[3] is not ("назад" or "вперёд" or "выбрать")
            => new("scenario_selection",SetupByName){TimeoutSeconds=30},
        _ when action.Key.StartsWith("setup:",StringComparison.Ordinal)
            && action.Key.EndsWith(":выбрать",StringComparison.Ordinal)
            => new("popup_choice",OpenSetupGrid),
        _ when action.Key.StartsWith("setup:",StringComparison.Ordinal)
            && action.Key.Count(c=>c==':')>=2
            => new("scenario_selection",PlayerSetup),
        "rmg:underground" => new("scenario_selection", Deliveries.Control(285,"RanUndr.def")),
        "rmg:template" => new("scenario_selection,popup_choice", Deliveries.Control(7001)),
        "rmg:teams" => new("scenario_selection,popup_choice", Deliveries.Control(7005,"gspbut2.def")),
        "rmg:road:dirt" => new("scenario_selection", Deliveries.Control(7007)),
        "rmg:road:gravel" => new("scenario_selection", Deliveries.Control(7008)),
        "rmg:road:cobble" => new("scenario_selection", Deliveries.Control(7009)),
        "rmg:generated" => new("scenario_selection,popup_choice", Deliveries.Control(335,"RanShow.def")),
        "scenario:more" => new("scenario_selection,popup_choice", Deliveries.Control(6999,"gspbut2.def")),
        "scenario:pvp" => new("scenario_selection,popup_choice", Deliveries.Control(7089,"gspbut2.def")),
        "scenario:maps" => new("scenario_selection", Deliveries.Control(128)),
        "scenario:players" => new("scenario_selection", Deliveries.Control(129)),
        "scenario:random" => new("scenario_selection", Deliveries.Control(130)),
        _ when action.Key.StartsWith("scenario:difficulty:",StringComparison.Ordinal)
            && int.TryParse(action.Key["scenario:difficulty:".Length..],out int level) && level is >=1 and <=5
            => new("scenario_selection",Deliveries.Control(106+level)),
        // Starting a scenario goes through the button, not through the screen's own command code.
        // The command started the map while the setup panel had never been committed, and the game
        // began with no town and no hero — an instant defeat. The button does what a player's press
        // does: it fixes the chosen map and the player slots first.
        "scenario:start" => new("adventure", Deliveries.StartScenario) { TimeoutSeconds = 10 },


        // Modal questions: ordinary presses on the dialog's own buttons. Answering one can start a
        // battle, open a town or a hero screen, or chain into the next message, so the landing is
        // not always the map.
        "message:accept" => new(AfterMessage, Deliveries.Control(30722, "iokay.def")),
        "turn:end:anyway" => new("adventure", Deliveries.Control(30725,"iokay.def")) { Confirm = Confirm.TurnAdvanced, TimeoutSeconds = 20 },
        "turn:end:cancel" => new("adventure", Deliveries.Control(30726,"icancel.def")),
        _ when action.Key.StartsWith("reward:take:",StringComparison.Ordinal)
            => new("message",RewardChoice),
        "message:confirm" => new(AfterMessage, Deliveries.Control(30725, "iokay.def")),
        "message:decline" => new(AfterMessage, Deliveries.Control(30726, "icancel.def")),

        // Adventure map. Keys are the ones the manual lists under Section IV, Keyboard Shortcuts.
        "turn:end" => new("adventure", Deliveries.Key(0x45, 0x12)) { Confirm = Confirm.TurnAdvanced },
        "hero:select" => new("adventure", SelectOwnHero),
        _ when action.Key.StartsWith("hero:sheet:") => new("hero_screen,adventure", OpenHeroSheet),
        _ when action.Key.StartsWith("hero:pick:") => new("adventure", SelectMapHero){ Confirm = Confirm.None },
        // "M - Moves current hero" along the planned path.
        "hero:move" => new("adventure", Deliveries.Key(0x4d, 0x32)),
        // "Arrow Keys - Moves current hero": one step in a direction, no route planning involved.
        // The diagonals are the numeric keypad, the way the game has always taken them.
        _ when action.Key.StartsWith("hero:step:") => new("adventure,message,town,hero_screen,combat,battle_result",
            Deliveries.Key(StepKey(action.Key).Key, StepKey(action.Key).Scan)) { Confirm = Confirm.HeroMoved },
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
        "game:main_menu" => new("main_menu,message", Deliveries.Control(108, "somain.def")),
        _ when action.Key.StartsWith("save:name:") => new("save_game", SaveName),
        "save:confirm" => new("message", Deliveries.Control(186, "scnrsav.def")),
        "load:confirm" => new("adventure", Deliveries.Control(186, "scnrlod.def")) { Confirm = Confirm.PartyLoaded },
        "load:back" => new("main_menu", Deliveries.Control(188, "scnrback.def", "gspexit.def")),
        _ when action.Key.StartsWith("load:select:") || action.Key.StartsWith("load:open:") =>
            new("load_game", SelectSaveRow),

        // Town.
        "town:construction" => new("town_hall", ClickBuilding(c => Highest(c, 13, 12, 11, 10))),
        "town:close" => new("adventure", Deliveries.Key(0x1b, 0x01)),
        "construction:close" => new("town", Deliveries.Control(30722,"iokay.def","TPMage1.def")),
        "building:cancel" => new("town_hall", Deliveries.Control(30721,"icancel.def")),
        "building:buy" => new("town", Deliveries.Control(30722,"iBUY30.def")),
        _ when action.Key.StartsWith("building:inspect:") =>
            new("building_confirmation", Deliveries.Control(c => 600 + Suffix(c.Element, 2))),
        _ when action.Key.StartsWith("town:open:") => new("town", OpenTown),
        _ when action.Key.StartsWith("town:switch:") => new("town", SwitchTown),
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
        "hero:close" => new("adventure,town", Deliveries.Key(0x1b, 0x01)) { Confirm = Confirm.ScreenLeft },
        _ when action.Key.StartsWith("hero:wear:") => new("hero_screen", WearArtifact),
        "hero:backpack:влево" => new("hero_screen", Deliveries.Control(77, "hsbtns3.def")),
        "hero:backpack:вправо" => new("hero_screen", Deliveries.Control(78, "hsbtns5.def")),
        "hero:backpack" => new("backpack", Deliveries.Control(8000, "bckpck.def")),
        // The kingdom overview has no Esc: it closes on its own exit button, bottom right.
        "kingdom:close" => new("adventure,town", Deliveries.Dismiss),
        // Every one of these windows closes on its own button; Esc is the fallback when the
        // window does not publish one.
        "screen:close" => new("adventure,town,combat,hero_screen", Deliveries.Dismiss),
        "scores:scenarios" => new("high_scores", Deliveries.Control(1002, "HiScSta.def")),
        "scores:campaigns" => new("high_scores", Deliveries.Control(1001, "HiScCam.def")),
        "scores:exit" => new("main_menu", Deliveries.Control(30722, "HiScExt.def")) { Confirm = Confirm.ScreenLeft, TimeoutSeconds = 10 },
        "score:accept" => new("high_scores,main_menu", Deliveries.Control(503, "mubchck.def")) { Confirm = Confirm.ScreenLeft, TimeoutSeconds = 10 },
        "split:cancel" => new("town,hero_screen,exchange", Deliveries.Key(0x1b, 0x01)),
        // The exchange window closes with the game's ordinary Esc, like the other hero screens.
        // It opens both from a meeting on the map and from the town screen.
        "exchange:done" => new("adventure,town", Deliveries.Key(0x1b, 0x01)),
        "exchange:backpack:слева" => new("backpack", Deliveries.Control(8000, "bckpck.def")),
        "exchange:backpack:справа" => new("backpack", Deliveries.Control(8001, "bckpck.def")),
        // The expansion's backpack window has no button and ignores Esc: a transparent control over
        // the whole screen catches a press outside the panel and closes it.
        "backpack:close" => new("exchange,hero_screen", async (context, ct) =>
        {
            var panel = context.Before.Elements.Where(e => e.Id == 0 && e.Width < context.Before.Width)
                .OrderByDescending(e => e.Width * e.Height).FirstOrDefault()
                ?? throw new InvalidOperationException("Панель рюкзака на экране не найдена");
            int x = panel.X > 40 ? panel.X / 2 : panel.X + panel.Width + (context.Before.Width - panel.X - panel.Width) / 2;
            await Deliveries.Press(context, x, panel.Y + panel.Height / 2, ct);
        }) { Confirm = Confirm.ScreenLeft },
        _ when action.Key.StartsWith("exchange:artifact:") => new("exchange", GiveArtifact),
        // Reading only: the comparison is printed in the action's own label, so performing it
        // must not touch the screen. Pressing nothing is the honest delivery.
        "exchange:compare" => new("exchange",static (_,_)=>Task.CompletedTask),
        "exchange:specialty:left" => new("exchange,message",Deliveries.Control(105)),
        "exchange:specialty:right" => new("exchange,message",Deliveries.Control(106)),
        "exchange:army:right" => new("exchange",Deliveries.Control(400,"SwCMR.def")){Confirm=Confirm.GarrisonChanged},
        "exchange:army:left" => new("exchange",Deliveries.Control(402,"SwCML.def")){Confirm=Confirm.GarrisonChanged},
        "exchange:army:swap" => new("exchange",Deliveries.Control(401,"SwXCh.def")){Confirm=Confirm.GarrisonChanged},
        "exchange:artifacts:right" => new("exchange",Deliveries.Control(450,"SwAMR_M.def")),
        "exchange:artifacts:left" => new("exchange",Deliveries.Control(452,"SwAML_M.def")),
        _ when action.Key.StartsWith("exchange:give:",StringComparison.Ordinal)
            => new("exchange",ExchangeStack(true)){Confirm=Confirm.GarrisonChanged},
        _ when action.Key.StartsWith("exchange:take:",StringComparison.Ordinal)
            => new("exchange",ExchangeStack(false)){Confirm=Confirm.GarrisonChanged},

        // Tavern and recruitment.
        "tavern:close" => new("town", Deliveries.Key(0x1b, 0x01)),
        "recruit:max" => new("recruitment", Deliveries.Key(0x4d, 0x32)),
        // A purchase lands on whichever screen opened the recruitment window — the town, or the
        // fort when every tier was hired from there — so the proof is the gold, not the screen.
        "recruit:buy" => new("town,town_fort,adventure", Deliveries.Key(0x0d, 0x1c)) { Confirm = Confirm.GoldSpent },
        "recruit:cancel" => new("town", Deliveries.Key(0x1b, 0x01)),

        // Combat.
        "combat:spellbook" => new("spellbook", Deliveries.Key(0x43, 0x2e)),
        "spellbook:close" or "spell:cancel" => new("combat", Deliveries.Key(0x1b, 0x01)),
        _ when action.Key.StartsWith("spellbook:select:") => new("combat", SelectSpell),
        _ when action.Key.StartsWith("spell:target:") => new("combat",
            Deliveries.CombatHex(c => AttackStack(c, c.Element[13..]).Hex))
            { Confirm = Confirm.CombatLog | Confirm.ManaSpent, BattleMayEnd = true },
        "combat:wait" => new("combat", Deliveries.Key(0x57, 0x11))
            { Confirm = Confirm.CombatTurn | Confirm.CombatLog, TimeoutSeconds = 10, BattleMayEnd = true },
        "combat:defend" => new("combat", Deliveries.Key(0x44, 0x20))
            { Confirm = Confirm.CombatTurn | Confirm.CombatLog, TimeoutSeconds = 10, BattleMayEnd = true },
        "combat:retreat" => new("combat", Deliveries.Control(2002)),
        "combat:auto" => new("combat", Deliveries.Control(2004)),
        // Pressing a stack's cell opens the creature card the player sees, with upgrade and
        // dismiss on it; the cells themselves are addressed by ArmyCell.
        // The game asks to confirm the price before it upgrades; that question is the landing.
        "army:upgrade" => new("creature_card,message",Deliveries.Control(300)),
        "army:dismiss" => new("creature_card",Deliveries.Control(30723)),
        "army:close" => new("creature_card",Deliveries.Control(30722)),
        "split:confirm" => new("town,hero_screen,exchange",Deliveries.Control(30722)),
        "split:decline" => new("town,hero_screen,exchange",Deliveries.Control(30721)),
        // Stacks are addressed the way the agent thinks about them — by creature name — and the
        // adapter finds the row and the slot. The numeric form stays legal for the rare case where
        // the same creature stands in two slots of one row.
        _ when action.Key.StartsWith("army:open:",StringComparison.Ordinal)
            => new("town",OpenArmyStack),
        _ when action.Key.StartsWith("army:give:",StringComparison.Ordinal)
            => new("town",MoveStack(true,false)){Confirm=Confirm.GarrisonChanged},
        _ when action.Key.StartsWith("army:take:",StringComparison.Ordinal)
            => new("town",MoveStack(false,false)){Confirm=Confirm.GarrisonChanged},
        _ when action.Key.StartsWith("army:split-give:",StringComparison.Ordinal)
            => new("split_army",MoveStack(true,false,true)),
        _ when action.Key.StartsWith("army:split-take:",StringComparison.Ordinal)
            => new("split_army",MoveStack(false,false,true)),
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
        _ when action.Key.StartsWith("combat:catapult:") => new("combat", async (context, ct) =>
        {
            string part = context.Element["combat:catapult:".Length..];
            int hex = ScreenActions.CatapultTargets.First(t => t.Part == part).Hex;
            var centre = new CombatReader(context.Game, context.Player).Center(hex);
            // A player points first: the status line then reads «Атака: <сегмент> (… прочность цели: n/m)».
            // Without that line the segment is already down and a press would do nothing.
            await context.Game.MouseAsync(centre.X, centre.Y, context.Before.Width, context.Before.Height, false, ct);
            await Task.Delay(250, CancellationToken.None);
            string status = context.Reader.Observe().Elements.FirstOrDefault(e => e.Id == 2005)?.Text?.Trim() ?? "";
            if (!status.StartsWith("Атака", StringComparison.Ordinal))
                throw new ActionRefused(ActionRefused.SegmentNotTarget,$"По «{part}» катапульте не выстрелить: строка состояния «{status}», а не «Атака: …» — сегмент разрушен или не цель. Выбери другой. Ничего не отправлено.");
            await Deliveries.Press(context, centre.X, centre.Y, ct);
        }) { Confirm = Confirm.CombatTurn, TimeoutSeconds = 10, BattleMayEnd = true },
        _ when action.Key.StartsWith("combat:move:") => new("combat",
            Deliveries.CombatHex(c => Suffix(c.Element, 2)))
            { Confirm = Confirm.CombatTurn, TimeoutSeconds = 10, BattleMayEnd = true },
        _ when action.Key.StartsWith("combat:attack:") => new("combat",
            Deliveries.CombatAttack(c => AttackStack(c, AttackTarget(c.Element)), c => AttackSide(c.Element)))
            { Confirm = Confirm.CombatTurn | Confirm.CombatLog, TimeoutSeconds = 10, BattleMayEnd = true },
        // After a fight the game may go straight on to a level-up or a message, not the map.
        "battle:accept" => new("adventure,level_up,message", Deliveries.Key(0x0d, 0x1c)),

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
            ("adventure", 10, "iam009.def") => new("system_options", Deliveries.Control(10,"iam009.def")),
            ("system_options", 30722, "soretrn.def") => new("adventure", Deliveries.Control(30722,"soretrn.def")),
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

    /// combat:attack:<stack> or combat:attack:<stack>:from:<hex> — the second names the hex the
    /// blow is struck from.
    private static string AttackTarget(string key)
    {
        string rest = key["combat:attack:".Length..];
        int from = rest.IndexOf(":from:", StringComparison.Ordinal);
        return from < 0 ? rest : rest[..from];
    }

    private static int? AttackSide(string key)
    {
        int from = key.IndexOf(":from:", StringComparison.Ordinal);
        return from < 0 ? null : int.Parse(key[(from + 6)..]);
    }

    private static CombatStack AttackStack(CommandContext context, string stackId) =>
        context.Before.Combat!.Stacks.Single(s => s.Id == stackId);

    private static Deliver ClickBuilding(int building) => ClickBuilding(_ => building);

    /// A hall or a fort is rebuilt in place: once the town hall stands, the game clears the bit of
    /// the village hall it replaced, so the building to press is the highest stage present.
    private static int Highest(CommandContext context, params int[] stages)
    {
        var town = context.Before.Towns.FirstOrDefault(t => t.Id == context.Before.OpenTown)
            ?? throw new InvalidOperationException("Открытый город не прочитан");
        return stages.Cast<int?>().FirstOrDefault(b => town.Buildings.Contains(b!.Value))
            ?? throw new InvalidOperationException("В городе нет ни одной ступени этого здания");
    }

    private static Deliver ClickBuilding(Func<CommandContext, int> building) => async (context, ct) =>
    {
        var point = new TownReader(context.Game, context.Player).BuildingPoint(building(context));
        await Deliveries.Press(context, point.X, point.Y, ct);
    };

    /// Own town entry: the town portrait in the adventure sidebar is pressed the same way a
    /// player presses it. The press is repeated once because the sidebar can still be animating.
    private static readonly Deliver OpenTown = async (context, ct) =>
    {
        string name = context.Element["town:open:".Length..];
        var owned = GameReader.SidebarTowns(context.Game, context.Player);
        int slot = Array.FindIndex(owned, id => context.Before.Towns.FirstOrDefault(t => t.Id == id)?.Name == name);
        if (slot < 0) throw new InvalidOperationException($"Города {name} нет в списке твоих городов");
        if (slot >= 5) throw new InvalidOperationException($"Город {name} ниже видимой части списка; прокрути список городов");
        var portrait = context.Before.Elements.FirstOrDefault(e => e.Id == 32 + slot && e.Asset == "itpa.def")
            ?? throw new InvalidOperationException($"Место города {name} в списке справа не найдено");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await Deliveries.Press(context, portrait, ct);
            await Task.Delay(150, CancellationToken.None);
            if (context.Reader.Observe().Screen == "town") break;
        }
    };

    /// Another own town from inside the town screen: its icon in the town list on the right.
    private static readonly Deliver SwitchTown = async (context, ct) =>
    {
        string name = context.Element["town:switch:".Length..];
        var owned = GameReader.SidebarTowns(context.Game, context.Player);
        int slot = Array.FindIndex(owned, id => context.Before.Towns.FirstOrDefault(t => t.Id == id)?.Name == name);
        if (slot < 0) throw new InvalidOperationException($"Города {name} нет в списке твоих городов");
        if (slot >= 5) throw new InvalidOperationException($"Город {name} ниже видимой части списка");
        var box = context.Reader.FindControlById(155 + slot)
            ?? throw new InvalidOperationException($"Значок города {name} в списке справа не найден");
        await Deliveries.Press(context, box.X + box.Width / 2, box.Y + box.Height / 2, ct);
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
        if (entry.Folder != folder) throw new InvalidOperationException("Save row is not selectable in the visible window");
        if (entry.Width > 0 && entry.Height > 0)
        {
            await Deliveries.Press(context, entry.X + entry.Width / 2, entry.Y + entry.Height / 2, ct);
            return;
        }
        // A row below the view is reached the way a player reaches it: the arrow keys move the
        // selection one row at a time and the list scrolls after it. Opening a folder needs a
        // press on its row, so folders are not walked to.
        if (folder) throw new InvalidOperationException("Папка ниже видимой части списка; открыть её можно только нажатием по строке");
        int current = context.Before.Saves!.SelectedIndex;
        if (current < 0) throw new InvalidOperationException("Не видно, какая строка выделена сейчас");
        int steps = index - current;
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            await context.Game.KeyAsync(steps > 0 ? (ushort)0x28 : (ushort)0x26, steps > 0 ? (ushort)0x50 : (ushort)0x48);
            await Task.Delay(40, CancellationToken.None);
        }
        var now = context.Reader.Observe().Saves?.SelectedIndex;
        if (now != index) throw new InvalidOperationException($"Выделение встало на строку {now}, а не на {index}");
    };

    private static readonly Deliver SelectLevelSkill = async (context, ct) =>
    {
        string skill = context.Element["level:choose:".Length..];
        var icon = context.Before.Elements.FirstOrDefault(e => e.Id is 2010 or 2011 && ScreenActions.LevelSkill(context.Before.Elements, e) == skill)
            ?? throw new InvalidOperationException($"Навыка «{skill}» среди предложенных нет");
        // A press right after the dialog opens can be lost; the choice counts only once the game
        // draws its frame over the picture.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Deliveries.Press(context, icon, ct);
            await Task.Delay(250, CancellationToken.None);
            if (ScreenActions.LevelSkillChosen(context.Reader.Observe().Elements, icon)) return;
        }
        throw new InvalidOperationException($"Игра не отметила выбор «{skill}» после трёх нажатий");
    };

    /// One press on a hero portrait selects that hero; a press on the hero already selected opens
    /// his screen. Both cases are covered by pressing, looking, and pressing once more.
    /// The portrait of the named hero in the list on the right of the map.
    private static UiElement HeroPortrait(CommandContext context)
    {
        string name = context.Element[(context.Element.IndexOf(':', 5) + 1)..];
        var list = GameReader.SidebarHeroes(context.Game, context.Player);
        for (int slot = 0; slot < 5; slot++)
        {
            if (list[slot] < 0) continue;
            var hero = context.Before.Heroes.FirstOrDefault(h => h.Id == list[slot]);
            if (hero?.Name != name) continue;
            return context.Before.Elements.FirstOrDefault(e => e.Id == 15 + slot && e.Interactive)
                ?? throw new InvalidOperationException($"Портрет героя {name} сейчас не на панели");
        }
        throw new InvalidOperationException($"Героя {name} нет в списке героев на карте");
    }

    private static readonly Deliver SelectMapHero = async (context, ct) =>
        await Deliveries.Press(context, HeroPortrait(context), ct);

    private static readonly Deliver OpenHeroSheet = async (context, ct) =>
    {
        var portrait = HeroPortrait(context);
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
    private static Deliver MoveStack(bool toGarrison,bool requireMerge,bool split=false) => async (context, ct) =>
    {
        string wanted = context.Element[(context.Element.IndexOf(':') + 1)..];
        wanted = wanted[(wanted.IndexOf(':') + 1)..];
        var town = context.Before.Towns.FirstOrDefault(t => t.Id == context.Before.OpenTown)
            ?? context.Before.Towns.FirstOrDefault()
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
        // A split always goes to a free cell: pressed onto a twin, Shift would only merge.
        int target = moving >= 0 && !split ? Array.FindIndex(toTypes, type => type == moving) : -1;
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
        if (split)
            await context.Game.ShiftClickRealAsync(destination.X, destination.Y, context.Before.Width, context.Before.Height, ct);
        else
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

    /// Moving a stack between two heroes who met on the map. The gesture is the one every army
    /// row in this game uses — press the stack, press the cell it should land in — and the cells
    /// here are the pictures: 13 plus the slot on the left, 20 plus the slot on the right.
    private static Deliver ExchangeStack(bool give) => async (context, ct) =>
    {
        string wanted = context.Element[(context.Element.IndexOf(':') + 1)..];
        wanted = wanted[(wanted.IndexOf(':') + 1)..];
        int fromBase = give ? 13 : 20, toBase = give ? 20 : 13;
        int fromCount = give ? 65 : 72, toCount = give ? 72 : 65;
        int source = -1, target = -1, sourceType = -1;
        for (int slot = 0; slot < 7 && source < 0; slot++)
        {
            var image = context.Before.Elements.FirstOrDefault(e => e.Id == fromBase + slot && e.Frame > 0);
            var number = context.Before.Elements.FirstOrDefault(e => e.Id == fromCount + slot && !string.IsNullOrWhiteSpace(e.Text));
            if (image is null || number is null) continue;
            if (!string.Equals(GameReference.Creature(image.Frame - 2), wanted, StringComparison.OrdinalIgnoreCase)) continue;
            source = slot; sourceType = image.Frame - 2;
        }
        if (source < 0) throw new InvalidOperationException($"Отряда «{wanted}» в этом ряду нет");
        // A cell holding the same creature merges the two stacks; an empty one asks how to divide.
        for (int slot = 0; slot < 7 && target < 0; slot++)
        {
            var image = context.Before.Elements.FirstOrDefault(e => e.Id == toBase + slot && e.Frame > 0);
            if (image is not null && image.Frame - 2 == sourceType) target = slot;
        }
        if (target < 0)
            for (int slot = 0; slot < 7 && target < 0; slot++)
            {
                var number = context.Before.Elements.FirstOrDefault(e => e.Id == toCount + slot);
                if (number is null || string.IsNullOrWhiteSpace(number.Text)) target = slot;
            }
        if (target < 0) throw new InvalidOperationException("У второго героя нет ни свободной клетки, ни такого же отряда");
        var from = context.Reader.FindControlById(fromBase + source)
            ?? throw new InvalidOperationException($"Клетка {source} не найдена");
        var to = context.Reader.FindControlById(toBase + target)
            ?? throw new InvalidOperationException($"Клетка назначения {target} не найдена");
        await Deliveries.Press(context, from.X + from.Width / 2, from.Y + from.Height / 2, ct);
        await Task.Delay(250, CancellationToken.None);
        await Deliveries.Press(context, to.X + to.Width / 2, to.Y + to.Height / 2, ct);
    };

    /// The save name field takes key presses, not typed characters: the old name is erased with
    /// Backspace and the digits are pressed one by one, then the field is read back.
    private static readonly Deliver SaveName = async (context, ct) =>
    {
        string name = context.Element["save:name:".Length..];
        name = name.ToLowerInvariant();
        if (name.Length is 0 or > 32 || !name.All(c => KeyOf(c).Key != 0))
            throw new InvalidOperationException("Имя файла — до 32 знаков: буквы, цифры, дефис");
        var field = context.Before.Elements.FirstOrDefault(e => e.Id == 160)
            ?? throw new InvalidOperationException("Поля имени файла на экране нет");
        await Deliveries.Press(context, field, ct);
        await Task.Delay(150, CancellationToken.None);
        for (int i = 0; i < (field.Text?.Length ?? 0) + 2; i++)
        {
            await context.Game.KeyAsync(0x08, 0x0e);
            await Task.Delay(40, CancellationToken.None);
        }
        foreach (char c in name)
        {
            var (key, scan) = KeyOf(c);
            await context.Game.KeyAsync(key, scan);
            await Task.Delay(60, CancellationToken.None);
        }
        await Task.Delay(150, CancellationToken.None);
        string? now = context.Reader.Observe().Elements.FirstOrDefault(e => e.Id == 160)?.Text;
        if (!string.Equals(now, name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"В поле имени «{now}», а не «{name}»: игра набирает буквы по своей раскладке — напиши имя буквами этой раскладки; не сохраняй");
    };

    /// Virtual key and scan code of the key that carries a letter. The game turns a key into a
    /// letter through its own keyboard layout, so a latin and a cyrillic letter on the same key
    /// map to the same press; the field is read back to see which one came out.
    private static (ushort Key, ushort Scan) KeyOf(char c)
    {
        if (c == '-') return (0xbd, 0x0c);
        if (char.IsAsciiDigit(c)) return (c, (ushort)(c == '0' ? 0x0b : c - '0' + 1));
        const string latin = "qwertyuiopasdfghjklzxcvbnm";
        const string cyrillic = "йцукенгшщзфывапролдячсмитьхъжэбюё";
        (ushort, ushort)[] latinKeys = [('Q',0x10),('W',0x11),('E',0x12),('R',0x13),('T',0x14),('Y',0x15),('U',0x16),('I',0x17),('O',0x18),('P',0x19),
            ('A',0x1e),('S',0x1f),('D',0x20),('F',0x21),('G',0x22),('H',0x23),('J',0x24),('K',0x25),('L',0x26),
            ('Z',0x2c),('X',0x2d),('C',0x2e),('V',0x2f),('B',0x30),('N',0x31),('M',0x32)];
        (ushort, ushort)[] cyrillicKeys = [('Q',0x10),('W',0x11),('E',0x12),('R',0x13),('T',0x14),('Y',0x15),('U',0x16),('I',0x17),('O',0x18),('P',0x19),
            ('A',0x1e),('S',0x1f),('D',0x20),('F',0x21),('G',0x22),('H',0x23),('J',0x24),('K',0x25),('L',0x26),
            ('Z',0x2c),('X',0x2d),('C',0x2e),('V',0x2f),('B',0x30),('N',0x31),('M',0x32),
            (0xdb,0x1a),(0xdd,0x1b),(0xba,0x27),(0xde,0x28),(0xbc,0x33),(0xbe,0x34),(0xc0,0x29)];
        char lower = char.ToLowerInvariant(c);
        int i = latin.IndexOf(lower);
        if (i >= 0) return latinKeys[i];
        i = cyrillic.IndexOf(lower);
        return i >= 0 ? cyrillicKeys[i] : ((ushort)0, (ushort)0);
    }

    /// Choosing between the two offers of a reward dialog. The value under a picture is its name
    /// here, because that is what the player reads; the press lands on the picture above it.
    private static readonly Deliver RewardChoice = async (context, ct) =>
    {
        // The key names the reward, and the action that published it put the rewards in the order
        // the dialog's own text names them: the first belongs to the left picture.
        string wanted = context.Element["reward:take:".Length..];
        var action = context.Before.Actions.FirstOrDefault(a => a.Key == context.Element)
            ?? throw new InvalidOperationException($"Варианта «{wanted}» в диалоге нет");
        var numbers = context.Before.Elements
            .Where(e => e.Text is not null && int.TryParse(e.Text.Trim(), out _))
            .OrderBy(e => e.X).ToList();
        int side = context.Before.Actions.Where(a => a.Key.StartsWith("reward:take:", StringComparison.Ordinal))
            .ToList().IndexOf(action);
        if (side < 0 || side >= numbers.Count) throw new InvalidOperationException("Вариант не сопоставлен картинке");
        var label = numbers[side];
        var picture = context.Before.Elements
            .Where(e => e.Text is null && Math.Abs(e.X - label.X) < 20 && e.Y < label.Y)
            .OrderByDescending(e => e.Y).FirstOrDefault()
            ?? throw new InvalidOperationException("Картинка варианта не найдена");
        await Deliveries.Press(context, picture.X + picture.Width / 2, picture.Y + picture.Height / 2, ct);
    };

    /// Choosing a scenario by name. The list answers the arrow keys the way it answers a player's:
    /// one press moves the selection by one row and the view follows. The bridge walks the
    /// difference between where the selection is and where the named map lies — one decided
    /// intent, carried out mechanically, the same as pressing a stack twice to move it.
    /// Presses the cell of the starting-town grid that carries the named faction. The grid lays
    /// the factions out in the order the game numbers them, so the name alone finds the cell.
    private static readonly Deliver PickTown = async (context, ct) =>
    {
        string want = context.Element["выбор:город:".Length..];
        var cell = want.Equals("случайный", StringComparison.OrdinalIgnoreCase)
            ? context.Before.Elements.FirstOrDefault(e => e.Id == 999 && e.Interactive)
            : context.Before.Elements.FirstOrDefault(e => e.Id is >= 1000 and <= 1011 && e.Interactive
                && GameReference.Faction(e.Id - 1000).Equals(want, StringComparison.OrdinalIgnoreCase));
        if (cell is null) throw new InvalidOperationException(
            $"В открытой сетке нет города «{want}». Открой её кнопкой города нужного ряда и посмотри, что предложено.");
        await Deliveries.Press(context, cell, ct);
    };

    /// Presses the cell of the starting-hero grid that carries the named hero. The grid holds the
    /// roster of the town already chosen for that row, in the order the game numbers heroes.
    private static readonly Deliver PickHero = async (context, ct) =>
    {
        string want = context.Element["выбор:герой:".Length..];
        UiElement? cell = null;
        if (want.Equals("случайный", StringComparison.OrdinalIgnoreCase))
            cell = context.Before.Elements.FirstOrDefault(e => e.Id == 2999 && e.Interactive);
        else
        {
            var town = context.Before.Elements.FirstOrDefault(e => e.Id is >= 1000 and <= 1011 && e.Frame % 2 == 1);
            if (town is null) throw new InvalidOperationException(
                "Сначала выбери городе ряда: пока город случайный, игра не показывает его героев.");
            cell = context.Before.Elements.FirstOrDefault(e => e.Id is >= 3000 and < 3016 && e.Interactive
                && GameReference.Hero((town.Id - 1000) * 16 + e.Id - 3000).Equals(want, StringComparison.OrdinalIgnoreCase));
        }
        if (cell is null) throw new InvalidOperationException(
            $"В открытой сетке нет героя «{want}»; посмотри, кого она предлагает.");
        await Deliveries.Press(context, cell, ct);
    };

    /// Presses the row of an open drop-down list by the words written on it.
    private static readonly Deliver PickRow = async (context, ct) =>
    {
        string want = context.Element["выбрать:".Length..];
        var row = context.Before.Elements.FirstOrDefault(e => e.Interactive
            && e.Text is not null && e.Text.Trim().Equals(want, StringComparison.OrdinalIgnoreCase));
        if (row is null) throw new InvalidOperationException($"В открытом списке нет строки «{want}»");
        await Deliveries.Press(context, row, ct);
    };

    /// Presses one box of the team agreements dialog.
    private static readonly Deliver PickTeamSlot = async (context, ct) =>
    {
        var parts = context.Element.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[1], out int team) || !int.TryParse(parts[2], out int place))
            throw new InvalidOperationException("Место в команде называется так: команда:<номер>:<место>");
        int id = (team == 1 ? 100 : 110) + place - 1;
        var box = context.Before.Elements.FirstOrDefault(e => e.Id == id && e.Interactive)
            ?? throw new InvalidOperationException($"В окне команд нет места {place} у команды {team}");
        await Deliveries.Press(context, box, ct);
    };

    private static int MarketResource(string key)
    {
        string[] res = ["дерево", "ртуть", "руда", "сера", "кристаллы", "самоцветы", "золото"];
        int i = Array.IndexOf(res, key[(key.LastIndexOf(':') + 1)..]);
        if (i < 0) throw new InvalidOperationException($"Ресурса «{key[(key.LastIndexOf(':') + 1)..]}» на рынке нет");
        return i;
    }

    /// Sets an amount the way a player does: the arrows at the two ends of a slider move it one
    /// unit at a time, and a number on the screen says where it stands. The market and the split
    /// window both work this way; they differ only in which number follows the slider.
    private static Deliver SliderTo(string prefix, int slider, int count) => async (context, ct) =>
    {
        int wanted = int.Parse(context.Element[prefix.Length..]);
        var bar = context.Reader.FindControlById(slider)
            ?? throw new InvalidOperationException("Ползунка количества на экране нет");
        // The screen is redrawn while the slider moves; a read that lands mid-redraw is repeated.
        int Read()
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    string text = context.Reader.Observe().Elements.FirstOrDefault(e => e.Id == count)?.Text ?? "";
                    return int.TryParse(new string(text.Where(char.IsDigit).ToArray()), out int v)
                        ? v : throw new InvalidOperationException("Число у ползунка не прочитано");
                }
                catch (InvalidOperationException e) when (attempt < 8 && e.Message.StartsWith("State changing", StringComparison.Ordinal))
                {
                    Thread.Sleep(80);
                }
            }
        }
        int now = Read();
        while (now != wanted)
        {
            bool up = now < wanted;
            int x = up ? bar.X + bar.Width - 8 : bar.X + 8;
            await Deliveries.Press(context, x, bar.Y + bar.Height / 2, ct);
            // The number under the slider is redrawn a moment after the press.
            int next = Read();
            for (int wait = 0; wait < 6 && next == now; wait++) { await Task.Delay(100, CancellationToken.None); next = Read(); }
            if (next == now)
                throw new InvalidOperationException($"Ползунок встал на {now}, до {wanted} не дойти: "
                    + (up ? "больше не позволяет запас" : "меньше поставить нельзя"));
            // One step can be larger than one unit; stepping past the wanted number means it cannot be
            // set exactly, and walking back would only rock the slider to and fro.
            if (up ? next > wanted : next < wanted)
                throw new InvalidOperationException($"Ползунок идёт шагами: после {now} сразу {next}, ровно {wanted} не поставить. Выбери число из этого ряда.");
            now = next;
        }
    };

    /// Hands one artefact to the other hero the way a player does: press it to lift it onto the
    /// cursor, then press a cell of the other hero that the game lights up for it, or a free cell
    /// of his backpack when none lights up.
    private static readonly Deliver GiveArtifact = async (context, ct) =>
    {
        string name = context.Element["exchange:artifact:".Length..];
        string? from = null;
        int at = name.LastIndexOf('@');
        if (at > 0) { from = name[(at + 1)..]; name = name[..at]; }
        var items = context.Before.Elements;
        (int Id, bool Left)? source = null;
        foreach (var (doll, pack, left) in new[] { (27, 89, true), (46, 94, false) })
        {
            if (from is not null && from != (left ? "слева" : "справа")) continue;
            var hit = ExchangeArtifacts.Worn(items, doll).Where(a => a.Name == name).Select(a => a.Id)
                .Concat(ExchangeArtifacts.Pack(items, pack).Where(a => a.Name == name).Select(a => a.Id)).Cast<int?>().FirstOrDefault();
            if (hit is int id) { source = (id, left); break; }
        }
        if (source is null) throw new InvalidOperationException($"Артефакта «{name}» у героев в окне нет");
        await Deliveries.Press(context, items.First(e => e.Id == source.Value.Id), ct);
        await Task.Delay(250, CancellationToken.None);
        var lifted = context.Reader.Observe().Elements;
        int doll2 = source.Value.Left ? 46 : 27, pack2 = source.Value.Left ? 94 : 89;
        var target = lifted.Where(e => e.Id >= doll2 && e.Id < doll2 + 19 && e.Frame == ExchangeArtifacts.Highlight).OrderBy(e => e.Id).FirstOrDefault();
        if (target is null)
        {
            int free = Enumerable.Range(pack2, 5).FirstOrDefault(id => lifted.All(e => e.Id != id), -1);
            if (free < 0) throw new InvalidOperationException("Артефакт взят на курсор, но у соседа нет ни подходящего слота, ни свободной видимой клетки рюкзака");
            var box = context.Reader.FindControlById(free) ?? throw new InvalidOperationException("Клетка рюкзака не найдена");
            await Deliveries.Press(context, box.X + box.Width / 2, box.Y + box.Height / 2, ct);
            return;
        }
        await Deliveries.Press(context, target, ct);
    };

    /// Puts on an artefact from the backpack the way a player does on the hero screen: press it
    /// to lift it, then press the worn cell the game lights up for it. Whatever was worn there
    /// goes to the backpack in its place.
    private static readonly Deliver WearArtifact = async (context, ct) =>
    {
        string name = context.Element["hero:wear:".Length..];
        var item = context.Before.Elements.FirstOrDefault(e => e.Id is >= 40 and <= 44 && e.Width == 44
                && GameReference.Artifact(e.Frame) == name)
            ?? throw new InvalidOperationException($"«{name}» в видимой части рюкзака нет");
        await Deliveries.Press(context, item, ct);
        await Task.Delay(250, CancellationToken.None);
        var lifted = context.Reader.Observe().Elements;
        var target = lifted.FirstOrDefault(e => e.Id is >= 2 and <= 20 && e.Frame == ExchangeArtifacts.Highlight)
            ?? throw new InvalidOperationException($"«{name}» поднят, но игра не подсветила ни одного слота, куда его надеть");
        bool occupied = context.Before.Elements.Any(e => e.Id == target.Id);
        await Deliveries.Press(context, target, ct);
        if (!occupied) return;
        // The slot was taken: the game swaps, and what was worn there is now on the cursor. It
        // goes into the first free backpack cell, so the hand is empty again.
        await Task.Delay(250, CancellationToken.None);
        var after = context.Reader.Observe().Elements;
        // While an artefact is on the cursor the game lights the slots it fits; with no slot lit the
        // hand is empty — the game has already put the removed artefact into the backpack itself.
        if (after.All(e => e.Frame != ExchangeArtifacts.Highlight)) return;
        int free = Enumerable.Range(40, 5).FirstOrDefault(id => after.All(e => e.Id != id), -1);
        if (free < 0) throw new InvalidOperationException("Надето, но снятый артефакт остался на курсоре: в видимой части рюкзака нет свободной клетки");
        var box = context.Reader.FindControlById(free) ?? throw new InvalidOperationException("Клетка рюкзака не найдена");
        await Deliveries.Press(context, box.X + box.Width / 2, box.Y + box.Height / 2, ct);
    };

    private static readonly Deliver SelectScenario = async (context, ct) =>
    {
        string wanted = context.Element["scenario:map:".Length..];
        var list = context.Before.Setup?.Fields.FirstOrDefault(f => f.Key == "map")?.Choices
            ?? throw new InvalidOperationException("Список сценариев не прочитан: открой панель «Доступные сценарии»");
        int target = list.FindIndex(c => c.Action == context.Element);
        int current = list.FindIndex(c => c.Selected);
        if (target < 0) throw new InvalidOperationException($"Сценария «{wanted}» в списке нет");
        if (current < 0) throw new InvalidOperationException("Не видно, какой сценарий выбран сейчас");
        int steps = target - current;
        // Walking hundreds of rows one key at a time is not how a player finds a map: he narrows
        // the list by size first and then picks from what is left. Refusing the long walk keeps
        // that gesture honest instead of grinding through the whole library.
        if (Math.Abs(steps) > 40)
            throw new InvalidOperationException(
                $"До «{wanted}» {Math.Abs(steps)} строк списка. Сузь список фильтром размера "
                +"(scenario:filter:...) и выбирай из оставшихся, как это делает игрок.");
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            await context.Game.KeyAsync(steps > 0 ? (ushort)0x28 : (ushort)0x26,
                steps > 0 ? (ushort)0x50 : (ushort)0x48);
            await Task.Delay(60, CancellationToken.None);
        }
    };

    /// One control of the players panel. The key names the colour, the column and the direction,
    /// and the row is the colour's place in the game's own order.
    /// Opens the whole grid of starting towns, or of the heroes of the town already chosen, by
    /// pressing the picture that stands between the two arrows of that row. Stepping the arrows
    /// one press at a time is what this replaces.
    /// Sets one of a player's starting choices by its name. The grid of pictures ignores a click
    /// that no hand made, so the value is stepped with the row's own arrow and checked after every
    /// step against what the game itself says about the picture — the card the right button opens.
    /// The agent names a town or a hero; the walking is the adapter's business.
    private static readonly Deliver SetupByName = async (context, ct) =>
    {
        string[] colours=["красный","синий","коричневый","зелёный","оранжевый","фиолетовый","бирюзовый","розовый"];
        var parts=context.Element.Split(':');
        int slot=Array.IndexOf(colours,parts[1]);
        if(slot<0)throw new InvalidOperationException($"Цвет «{parts[1]}» не опознан");
        var (forward,column)=parts[2] switch
        {
            "город" => (223+slot,176),
            "герой" => (239+slot,252),
            "бонус" => (255+slot,328),
            _ => throw new InvalidOperationException($"Столбец «{parts[2]}» не опознан")
        };
        string want=parts[3];
        var arrow=context.Reader.FindControlById(forward)
            ?? throw new InvalidOperationException($"У цвета «{parts[1]}» нельзя менять «{parts[2]}»: карта задала это жёстко");
        int pictureY=arrow.Y-3+16,pictureX=column+24;
        var seen=new List<string>();
        for(int step=0;step<14;step++)
        {
            string now=await NameUnderPointer(context,pictureX,pictureY,ct);
            if(now.Contains(want,StringComparison.OrdinalIgnoreCase))return;
            if(seen.Contains(now))break;
            seen.Add(now);
            await Deliveries.Press(context,arrow.X+arrow.Width/2,arrow.Y+arrow.Height/2,ct);
            await Task.Delay(250,CancellationToken.None);
        }
        throw new InvalidOperationException(
            $"«{want}» не предлагается в столбце «{parts[2]}» у цвета «{parts[1]}». Игра предлагает: {string.Join(", ",seen)}");
    };

    /// What the game calls the picture at this point: it is asked the way a player asks, by holding
    /// the right button, and the answer is the line the card puts under its own heading.
    private static async Task<string> NameUnderPointer(CommandContext context,int x,int y,CancellationToken ct)
    {
        await context.Game.RightMouseDownAsync(x,y,context.Before.Width,context.Before.Height,ct);
        try
        {
            await Task.Delay(300,CancellationToken.None);
            var texts=context.Reader.ReadCard().Texts;
            if(texts.Length==0)return "";
            // The last line is the card's heading in braces. Normally the line before it is the
            // value; when that line is a whole sentence the heading is the value itself, which is
            // how a random choice is written: «{Случайный бонус}».
            string heading=texts[^1].Trim('{','}',' ');
            string value=texts.Length>1?texts[^2]:"";
            return value.Length is >0 and <40?value:heading;
        }
        finally{await context.Game.RightMouseUpAsync();}
    }

    private static readonly Deliver OpenSetupGrid = async (context, ct) =>
    {
        string[] colours=["красный","синий","коричневый","зелёный","оранжевый","фиолетовый","бирюзовый","розовый"];
        var parts=context.Element.Split(':');
        int slot=Array.IndexOf(colours,parts[1]);
        if(slot<0)throw new InvalidOperationException($"Цвет «{parts[1]}» не опознан");
        int arrow=parts[2] switch
        {
            "город" => 215+slot,
            "герой" => 231+slot,
            _ => throw new InvalidOperationException($"Открыть можно выбор города или героя, а не «{parts[2]}»")
        };
        int column=parts[2]=="город"?176:252;
        var row=context.Reader.FindControlById(arrow)
            ?? throw new InvalidOperationException($"Ряд цвета «{parts[1]}» на панели участников не найден");
        var picture=context.Before.Elements.FirstOrDefault(e=>Math.Abs(e.X-column)<6&&Math.Abs(e.Y-row.Y)<10)
            ?? throw new InvalidOperationException("Картинка выбора в этом ряду не найдена");
        await Deliveries.Press(context,picture,ct);
    };

    private static readonly Deliver PlayerSetup = async (context, ct) =>
    {
        string[] colours=["красный","синий","коричневый","зелёный","оранжевый","фиолетовый","бирюзовый","розовый"];
        var parts=context.Element.Split(':');
        int slot=Array.IndexOf(colours,parts[1]);
        if(slot<0)throw new InvalidOperationException($"Цвет «{parts[1]}» не опознан");
        int id=parts[2] switch
        {
            // The flag at the start of the row seats a person at this colour; the button beside the
            // name is the handicap, not the switch it was once taken for.
            "кто" => 263+slot,
            "фора" => 207+slot,
            "город" => (parts[3]=="назад"?215:223)+slot,
            "герой" => (parts[3]=="назад"?231:239)+slot,
            "бонус" => (parts[3]=="назад"?247:255)+slot,
            _ => throw new InvalidOperationException($"Столбец «{parts[2]}» не опознан")
        };
        var box=context.Reader.FindControlById(id)
            ?? throw new InvalidOperationException($"Контрол {id} на панели участников не найден");
        await Deliveries.Press(context,box.X+box.Width/2,box.Y+box.Height/2,ct);
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
