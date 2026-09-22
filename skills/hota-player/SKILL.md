---
name: hota-player
description: Play the installed Heroes III Horn of the Abyss through the HotA MCP bridge — fair structured observations, semantic actions, and a built-in game reference the agent questions in plain words. Use when playing, mapping screens, or answering questions about the running game.
---

# HotA player

You are playing a real game. The bridge reads the game's state and delivers your commands to the
game's own handlers; it never picks a target, plans a route of its own, or decides a fight. Strategy
is yours. Tool names come from the connected server as the harness exposes them (a namespace may be
prepended); call `game_status` first to see what this build supports.

## Everything the bridge gives you

Twenty-three tools, in four groups. `docs/knowledge/agent/01-tools.md` holds the generated list with
the full text of every description; this is the map.

**Knowing the state**
- `game_status` — what this build supports and what it does not. Call it once at the start.
- `observe` — the whole state of your side plus the revision every action needs: date, resources,
  every hero you own, every town with its garrison and whether today's building is spent, the active
  screen, its controls and the legal actions. This is the one call you cannot skip.
- `nearby_targets` — recognised visible objects near the selected hero, by stable id.
- `inspect_target` — makes the game compute the route to one destination and returns cost, steps and
  a state with a reason. Route data in `nearby_targets` is a stale cache; this is the live answer.
- `read_map` — terrain, roads, blocked cells and visible objects around a cell, radius up to 12.
- `read_journal` — what this controller did recently. `plan` — read or write your own goals.

**Looking the way a player looks**
- `inspect_cell` — right-button card for a map cell: which creature stands there and roughly how many.
- `inspect_element` — right-button card for a control on the current screen: creature stats, skill
  and spell texts, artefact descriptions. Nothing is activated.

**Acting**
- `act` — any semantic key from `observe.Actions`: town, construction, recruitment, tavern, hero
  screen, combat, dialogs, end of turn. This is the main verb.
- `click_ui` — the same delivery for a validated control when no semantic key exists yet.
- `move_to` / `move_to_tile` — walk the selected hero to a target id or to an explicit cell.
- `attack_target` — deliberately start a fight with a visible stack; ordinary movement refuses it.
- `map_click` — make the game compute routes from a point of the map surface.
- `start_game`, `launcher_graphics` — raise the game through the existing launcher, read or choose
  its renderer.

**The reference** — `hota_docs`, `hota_docs_catalog`, `hota_docs_read`, `hota_reference`. Described
in the next section.

**Diagnostics, not gameplay** — `debug_snapshot`, `debug_capture`. They save a frame of the game
window to a file for a developer looking at coverage. They are not how the game is read.

Every acting tool takes `operationId` and `revision`. The revision comes from the observation you
acted on; a stale one is refused. The operation id is yours and must stay the same on a retry of the
same intent — repeating an `uncertain` action under a fresh id is how a hero gets moved twice.

## The loop of one turn

The same seven steps every turn. They exist because the expensive mistakes are all mistakes of
order: acting before reading, ending a day with movement left, deciding twice what was decided
yesterday.

1. **`observe`.** It opens with a briefing in words — whose turn it is, the date, every hero with
   position, movement and army, every town with what is built and whether today's building is
   spent, the forecast for tomorrow morning, and your own stored plan and last results. Read the
   briefing before anything else; most follow-up questions are already answered in it.
2. **Reconcile with the plan.** The plan comes back inside the observation. Did yesterday's active
   task happen? If it did, close it and take the next from the queue. If it did not and cannot,
   say so in the plan and replace it — an active task nobody can finish is how a game stalls.
3. **Town first.** One building per day per town, and with two towns the screen shows one of them
   — the briefing names which. On the first day of a week also recruit: growth appears that morning
   and is lost if the week turns without it. Recruit from the **fort** (`town:building:7`, or `:8` / `:9` once it is rebuilt into a citadel or castle — the town briefing names the key →
   `fort:recruit:<уровень>`), never dwelling by dwelling: the fort screen lists every tier at once
   with what each dwelling has in stock, and says which dwellings are still missing — so one screen
   answers what to hire and what to build next. In the hall, **green is what can be built now**,
   gold is what already stands, red with a cross is refused.
4. **Main hero.** Spend his movement on the goal of the current state, not on what happens to be
   near. Before a fight: `inspect_tile` on the target (the game's own hint line says what it is,
   what it gives and whether this hero has been there), then `hota_reference` on both creatures,
   then decide.
5. **Collector.** Free objects, mines, mills once a week, and the fog. `read_map` sees everything
   already discovered, including what lies outside the hero's sight — `nearby_targets` only covers
   what he can see now. Before sending him anywhere, `inspect_tile` the target: the game's hint
   ends in «(Посещено)» for a shrine, a warehouse or a weekly object already taken, and a walk
   there is a wasted day. `nearby_targets` does not carry that mark.
6. **Spend what is left.** Movement does not carry over. A hero with movement and nothing to do is
   a hero who should be walking toward tomorrow's target.
7. **Close the day.** Rewrite the plan — state, roles, active task with its deadline, queue, one
   compressed line of what was done — then `turn:end`. The game asks for confirmation while
   movement remains; that question is a reminder, not an error.

## What to do with what breaks

Every turn turns up something the bridge gets wrong, and the turn is the only place it can be
found. Treat each one the same way:

- **A wrong answer** — the bridge said something the screen contradicts. Trust the screen, find why
  the reading is wrong, fix the reading. Two towns showed the first town's garrison for both; the
  fix was to read which town the screen is showing.
- **A missing capability** — something a player does that has no action key. Map it with
  `debug_snapshot`: one call saves the frame and the matching observation together, so what is on
  the screen and what the bridge reports are the same instant and can be compared line by line.
  Use it for mapping, not `debug_capture` — a picture alone does not say which control is which.
  Then `probe-screen` lists every control with its id, state, rectangle and picture, and comparing
  a probe before and after a press says exactly which control answered.
- **A missing name** — an id or a number where the game has a word. The game's own tables carry the
  names: creatures, buildings, map objects, resources. A number the player never sees should never
  reach the agent.
- **Then write it down.** A fix that is not recorded is found again next week. Rules of the game go
  to the knowledge base; how to work the bridge goes here; what happened this game goes to the plan.

## The reference: ask it, do not guess

The bridge carries a knowledge base of the game and answers direct questions. Use it whenever you
are unsure of a rule, a number, or whether something is worth doing. It is a separate, explicit
interface — nothing from it leaks into observations.

- `hota_docs(query)` — ask in plain words: «как считается урон», «какая охрана у утопии драконов»,
  «в каком порядке строить здания», «что брать при повышении уровня». Returns the passage that
  answers, with its file and heading.
  - `detail="titles"` — file and heading only, for orienting cheaply.
  - `detail="snippet"` — the answering passage. Default; this is what you normally want.
  - `detail="full"` — whole sections. Costs several times more; ask for it only when you truly need
    the entire section in one call.
- `hota_docs_read(path, heading, offset, maxChars)` — the whole section a hit named. Long text comes
  in windows and the answer tells you where the next window starts; nothing is cut silently.
- `hota_docs_catalog()` — every document; with a `path`, that document's headings. Use it when no
  search hit looks right.
- `hota_reference(name, kind)` — the rule card for a named thing (навык, заклинание, существо,
  артефакт, здание, объект карты), read from the rule tables of the **installed** game, so it matches
  the exact version being played.

What lives there: formulas (damage, terrain cost, growth, payback), bank and guard tables, level-up
probabilities, secondary skills and magic, town economy, what HotA changed, game loops and decision
priorities, the screen registry, and playbooks for save/load, town, turn and battle. Every file ends
with a `## Пробелы` section saying what it does **not** know — read it before assuming silence means
"no such rule".

If the reference has no answer, say so. Do not invent a number.

## Observe and act

1. `observe` — screen, date, your resources and hero, `Elements`, `Setup`, and the `Actions`
   available on this screen. An unsupported screen is a coverage gap, not licence to guess an input.
2. `act(operationId, revision, action)` with a key from `Actions`, a unique operation ID, and the
   revision from that observation. A stale revision is refused.
3. Read the outcome, then observe again. **Retry with exactly the same operation ID.** An `uncertain`
   result must never be retried under a new ID — observe first and decide again.
4. `plan` holds your goals, `read_journal` your prior results.

Confirmation is by evidence, not by the picture changing: movement by position and movement points
spent, a stack transfer by the garrison composition, end of turn by the date, a combat action by the
battle log growing, a spell by mana spent.

Play by semantics. `debug_snapshot` and `debug_capture` are developer diagnostics for building a
screen's adapter or for an illustration the user asked for — not part of the play loop. If a screen
cannot be understood without a frame, that is a missing tool, not a reason to look with eyes.

## Looking before acting

- `inspect_cell(x, y, z)` — the bridge holds the right mouse button over a map cell and reads the
  card the game shows: for a wandering stack, the creature and its rough size. This is the input for
  "fight or walk around". It goes stale — wandering stacks move every day.
- `inspect_element(element)` — the same for a control on the current screen: creature stats, a skill
  or spell description, artefact text. The control is not activated; nothing is bought or spent.
- Left click on a skill, spell or stack opens a popup that stays and is read by an ordinary
  `observe`. Careful: left click on an army slot or an exchange arrow **performs the action**. For
  information, prefer the right button.

## Before the first move of a turn

`observe` carries the whole kingdom, not just the selected hero: `Heroes` lists every hero you own
with position, movement, mana and army; `Towns` lists every town with its garrison, what is built
and whether the daily build is spent. Read that first — cycling `hero:select` to discover what you
own wastes turns and misses heroes standing still somewhere.

Then read `plan` and `read_journal`. The plan is where the previous turn left its intent; without it
a new turn starts blind and repeats yesterday's thinking. Write the plan back at the end of every
turn: the goal of the game, what changed today, what is known about the enemy, and the first two or
three things to do tomorrow. Keep it short enough to read in one glance.

## The shape of a day

1. **Roster.** Who do I have, where, with how much movement, and what is idle. Troops sitting in a
   garrison are an army you are not using.
2. **Town.** One building per day per town — decide which, and buy it early so the day is not lost.
   Creature growth arrives on the first day of a week, so that is the day to recruit; on other days
   the dwellings answer "Доступно 0" and that is normal, not a failure.
3. **Main hero.** Spend movement on what grows strength: guarded objects worth the loss, unguarded
   bonuses on the way, terrain that opens new map.
4. **Secondary hero.** A hero with a token army is not useless — he collects resources, flags mines,
   visits obelisks and the one-time bonus objects, and carries reinforcements to the front so the
   main hero never walks home.
5. **End the day** only when both heroes have spent what they usefully can. The game asks for
   confirmation while movement remains; that question is a reminder, not an error.

## Artefacts and chests

- A chest is a choice by role: the main hero takes **experience**, a collector takes gold.
- Every artefact picked up is sorted at once. Right-click its cell (`inspect_element`) and read the
  game's own card: what helps the hero or his army stays with the main hero; what works from a
  place — pieces of the Statue of Legion raise a town's growth only when their carrier ends the
  day in that town — goes to the hero who sits there.
- In the exchange window the briefing lists both heroes' artefacts by slot and the visible
  backpack; `exchange:artifact:<name>` hands one over, `exchange:backpack:<side>` opens the whole
  backpack (the row at the bottom shows only five cells).

## Objects worth a detour

Permanent, one visit per hero: Garden of Revelation (+1 Knowledge), Learning Stone, Mercenary Camp,
Marletto Tower, Star Axis, Tree of Knowledge. Weekly: Stables (+movement for the week), Windmill,
Water Wheel, Magic Spring. For the day only: Fountain of Fortune, Rally Flag, Oasis, Watering Hole.
A collector hero should be routed through these rather than walking empty.

## Before committing to a fight

The decision is made before the first hex is clicked:

1. `inspect_cell` on the stack — the game names the creature and its rough size. It goes stale:
   wandering stacks move on the enemy's turn, so inspect in the same turn you attack.
2. `hota_reference` on both your creature and theirs. The cards carry the modifiers that decide the
   fight and are invisible on the battlefield — mutual double damage between water and fire
   elementals, immunities, flying, and above all **speed**, which says who strikes first.
3. `hota_docs` for the numbers you cannot see: army strength with the hero multiplier, how a
   neutral pack splits into stacks, what the size words mean.
4. Only then decide. A fight you win by trading your specialty stack for a guard is usually a fight
   worth postponing until the army is bigger or a spell is available.

## The turn

`observe` → `nearby_targets` → `inspect_target` on what looks worthwhile (the game plans the route
and returns cost and steps: `reachable_today`, `needs_more_days`, `not_available` with a reason) →
`inspect_cell` on anything guarded → decide → `move_to` or `move_to_tile`. The hero walks the whole
way; the answer comes back when it stops. Handle dialogs from `observe.Actions`. Close the day with
`turn:end`; the game may ask for confirmation while heroes still have movement.

`move_to` refuses a cell holding a creature stack — stepping there is a battle, and that is
`attack_target`, chosen deliberately.

Known defect: the route planner sometimes answers `not_available` for a cell the hero can in fact
reach. Treat a single "no path" as weak evidence, not proof; a short `move_to_tile` toward the
target settles it.

## Screens

The registry of every screen the bridge knows, how to open it and how to leave it, is
`docs/knowledge/agent/01-screens.md` — reachable as `hota_docs("реестр экранов")`. Verified:
adventure, hero sheet, town and all its buildings, town hall, fort, mage guild, tavern, recruitment,
marketplace, kingdom overview, world view, puzzle map, thieves guild, scenario info, adventure
options, system options, combat, spellbook, battle result, level up, exchange, split stack, message,
main menu, scenario setup, save and load browsers.

Not yet mapped: trading at the marketplace, learning a spell in the mage guild, reading tavern
candidates, structured content of world view / puzzle / thieves guild, town siege, surrender.

## Verified specifics

- **Town.** `town:open` uses the sidebar portrait. Recruiting is easiest from the Fort screen, where
  all tiers appear with dwelling, growth and remaining stock at once: `fort:recruit:<tier>`, then
  `recruit:max` and `recruit:buy`. The town hall marks every building «уже построено», «можно
  построить», «построить нельзя» — the same states the player sees. `town:lead`, `hero:out`,
  `hero:switch`, `town:take:<slot>` move army between hero and garrison; success is the garrison
  changing, not the click landing.
  Part of a stack: `army:split-give:<creature>` (hero → garrison) or `army:split-take:<creature>`
  (garrison → hero) opens the split window; `split:amount:<n>` sets how many go to the new cell,
  `split:confirm` applies. A plain `army:give`/`army:take` onto a free cell moves the whole stack.
  Market: `market:give:<resource>` → `market:get:<resource>` → `market:amount:<n>` (or `market:max`)
  → `market:trade`. Fort labels carry the price per creature and for the whole stock.
- **Combat.** `Combat.Log` carries the last 24 real log entries with zero-based `Index`; `LogCount`
  is the full count. Indices reset each battle. `combat:spellbook` opens the current book — read the
  actual labels and mana costs; `spellbook:select:<id>` picks an exposed icon; in target mode choose
  `spell:target:<stack>` and let the game check validity. Not every visible stack is a legal target.
- **Saving.** `game:save` from system options opens the save browser; it remembers its last folder,
  so read the selected row in `Saves` before `save:confirm` — confirming writes into the selected
  name and will overwrite it.
- **Loading.** From the main menu: `menu:load` → `menu:single` → `load:open:<folder>` →
  `load:select:<row>` → `load:confirm`. Verify the loaded party by date, hero and resources — never
  by "the screen changed", because loading the current state looks like nothing happened.
- **Dialog buttons** are ordinary dialog controls, activated by an addressed window mouse event
  inside the control's own reported bounds, computed by the adapter from UI structures. Do not use
  vtable substitution for them (it closes HD Mod dialogs without running the action, and crashed map
  initialisation after a load), and do not use Enter/Escape on question dialogs.

## Fairness

Use only your own player's observations and the server's target IDs. `nearby_targets` and
`inspect_target` are partial coverage: a missing target or missing route data does not prove the
thing is absent or unreachable. Never reach for process memory, raw addresses, save files, another
turn's frames, or another player's state to learn what the player cannot see.
