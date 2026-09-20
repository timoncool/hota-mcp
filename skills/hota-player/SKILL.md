---
name: hota-player
description: Play the installed Heroes III HotA through the HotA MCP bridge using fair structured observations and semantic actions. Use only capabilities actually exposed by the connected server; this development build does not yet support complete play.
---

# HotA player

Use the connected server's tool names as exposed by the harness; a harness may prepend a namespace. Discover tools and call `game_status` before acting. The current build remains incomplete: movement, battle, full setup and recovery are not finished, and the town exit path is unstable.

## Observe and act

1. Call `observe`. Read `Screen`, `Date`, own resources/hero, `Elements`, `Setup` and `Actions`. An unsupported screen is missing adapter coverage, not permission to guess an input.
2. Select a semantic key from `Actions`. Call `act(operationId, revision, action)` with a unique operation ID and the current observation revision.
3. Read the outcome and observe again. For a retry of the same request retain exactly the same operation ID and arguments. An `uncertain` result must not be retried under a new ID: inspect the state/journal first. A transition may produce another dialog instead of the expected map.
4. Use `plan` for concise goals and `read_journal` for prior results. The bridge does not choose strategy or inject rule explanations.

Normal play uses structured data only. Do not call `debug_capture` or `debug_snapshot` in the gameplay loop. Those are optional developer diagnostics and explicit user-requested illustrations; screenshots/OCR are not prerequisites for understanding or operating the game. Rule from the user (2026-09-20): frames are a temporary crutch while a screen's adapter is being built — cheap orientation during method development only. The playing agent (including combat) must operate by semantics: observation, actions, control bounds, map tiles. If a screen cannot be understood without a frame, that is a coverage gap and a task for a new tool, not a reason to look with eyes.

`move_to` outcomes: `completed` with a position/inventory change is real movement. `uncertain` with no position change, no movement spending and no date change means the target is not reachable from here — record it and pick another target instead of retrying with a new operation ID. Objects that hand out a bonus (campfire, garden of revelation, fountain) open a notice whose effect line is in the dialog text (`+1 Знания`); a repeat visit may answer that the bonus is granted only once, which is the game's own state, not a bridge error.

## Buttons and dialogs (verified 2026-09-20)

Every dialog button (options, browsers, questions) is an ordinary dialog control. Activate it with an **addressed window mouse event inside the control's own reported bounds** — the adapter computes the point from the UI structures, the agent never marks up a picture. This is the mechanism that works for `somain.def`108, `scnrlod.def`186, `scnrsav.def`186, `gspexit.def`/`scnrback.def`188 and the question buttons `iokay.def`30722/30725, `icancel.def`30726.

Do not use vtable-substitution delivery for these buttons: for HD Mod custom dialogs it closes the dialog without running the action (main menu button) or makes the game crash during map initialisation after a "load" (load browser button). Do not use Enter/Escape for question dialogs either: Enter on the main-menu question closed it without doing anything. Details and history: `docs/knowledge/playbooks/save-load-cycle.md`.

Verified live: load of `AUTOSAVE.GM1` from the main-menu browser produced the save's own party (Лабета 139 at 66,37, day 1, gold 10000) with the game left alive; the market build deducted 5 wood and 500 gold and set `builtToday`; leaving the town returned to adventure without terminating the game.

## Verified partial flows

- `start_game` asks the existing host launcher to launch HotA. Poll `game_status` for attachment. Starting the launcher itself and automatic intro skipping are not implemented yet.
- Main menu: `menu:new` → `menu:single` → scenario selection. `scenario:maps`, `scenario:random`, `scenario:players` switch setup panels; only the maps/random transitions have been live-tested so far.
- In random setup, read `Setup.Fields`: actual values, choices, selected/enabled state. Invoke only available `setup:*` actions. Size 72→36→72 and monsters strong→normal→strong have been verified. Other exposed choices require further acceptance testing. Full teams/templates/difficulty/player configuration is not complete.
- `scenario:start` starts the current configuration. Read again: an initial map observation may be followed by a scripted message.
- For `Screen=message`, read the complete text in `Elements`. A single-button notice exposes `message:accept`. A supported yes/no dialog exposes `message:confirm` and `message:decline`; choose based on its question and the intended action. Never automatically accept every dialog.
- `turn:end` can produce a warning that heroes still have movement. Confirm only when ending the turn is intended. After the AI turns, a new scripted message may appear. Verify the date change and then handle that message. A day 1→2 transition with income and autosave has been observed.
- Own town opening, construction inspection and buying a market were verified with ordinary resource deductions. Town exit subsequently terminated the game; do not treat this as a reliable production flow.

## Adventure and battle: partial verified coverage

Use `nearby_targets` then `move_to` with the current revision and target ID. Resource collection can end adjacent to the original object: verify inventory change and disappearance, not only hero position equality. Gold and wood collection were live-tested.

On `Screen=combat`, read `Combat` and only invoke listed `combat:wait`, `combat:defend`, `combat:move:HEX`, `combat:attack:STACK_ID`. Hex IDs are game cells, not screen pixels. These actions have live evidence; shooting-specific controls and comprehensive magic coverage are unfinished. Observe again after each result: animations may still be settling even after active stack changes. Do not infer that a completed transition guarantees the next action is ready. Never repeat uncertain actions under a new ID.

## Fairness

Use only the assigned player's observations and server-provided target IDs. `nearby_targets` and `inspect_target` are partial coverage, not a complete map. Missing targets or route data do not prove absence or inaccessibility. Do not use process memory, raw addresses, files, saves, screenshots of another turn, or another player's credentials to obtain hidden state.

Game reference materials are being prepared separately. Until search/resources are actually listed by the connected MCP server, do not invent their names or claim access to bundled references.

Combat.Log contains the last24 entries of the actual game log with zero-based Index; LogCount is the full current battle count. These are not hover hints. Action evidence records entries added since dispatch; verify both log and state. Indices reset for a new battle; durable battle IDs and full-history paging remain unfinished.

Verified magic path: combat:spellbook opens the current book; read actual labels and mana costs. spellbook:select:ID selects a currently exposed icon. In target mode choose spell:target:STACK_ID; the game checks target validity. Stone Skin on own water elementals was verified (mana30→26, log and defence change). Current target-mode recognition is Russian-text-specific; pages, schools and spell descriptions are unfinished. Do not assume all visible stacks are valid spell targets.

On battle_result read the result text and losses, then battle:accept. One victory and return to adventure were verified; the complete map is not finished.

Updated town evidence: town:open uses the actual sidebar portrait, currently limited to one owned town. Repeated open/close and a market purchase survived. town:tavern reads selected hero and price; tavern:hire was verified for Brissa. town:recruit:LEVEL opens a built dwelling using the game's building hit map; recruit:max selects maximum, recruit:buy confirms displayed cost. Six water elementals bought for1800. Do not confuse the remaining-stock text with selected quantity.

Saving: `game:save` from system_options opens save_game; `save:confirm` presses its game button. Read and accept the resulting message. The browser remembers its last folder: opened in-game it showed `Games\Превосходство в воздухе` with `..`, `111`, `111ы`, so read the selected row in `Saves` before confirming — confirming writes into the selected name and `111.GM1`/`111ы.GM1` must not be overwritten. Name editing/reading is still unfinished.

Loading: `menu:load` → `menu:single` from the main menu opens the load browser; `load:open:INDEX` enters a folder row, `load:select:INDEX` selects a file row, `load:confirm` presses ЗАГРУЗИТЬ. Then verify the loaded party by date/resources/hero, never by "the screen changed" — an instant load of the current state looks like nothing happened. The in-game `game:load` question still returns to the party without a browser; the working cycle is the main-menu one.

