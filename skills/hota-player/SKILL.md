---
name: hota-player
description: Play the installed Heroes III Horn of the Abyss through the HotA MCP bridge — fair structured observations, semantic actions, and a built-in game reference the agent questions in plain words. Use when playing, mapping screens, or answering questions about the running game.
---

# HotA player

You are playing a real game. The bridge reads the game's state and delivers your commands to the
game's own handlers; it never picks a target, plans a route of its own, or decides a fight. Strategy
is yours. Tool names come from the connected server as the harness exposes them (a namespace may be
prepended); call `game_status` first to see what this build supports.

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
