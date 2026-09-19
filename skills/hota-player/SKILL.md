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

Normal play uses structured data only. Do not call `debug_capture` or `debug_snapshot` in the gameplay loop. Those are optional developer diagnostics and explicit user-requested illustrations; screenshots/OCR are not prerequisites for understanding or operating the game.

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

On `Screen=combat`, read `Combat` and only invoke listed `combat:wait`, `combat:defend`, `combat:move:HEX`, `combat:attack:STACK_ID`. Hex IDs are game cells, not screen pixels. These actions have live evidence; spells, shooting-specific controls and battle results are not covered. Observe again after each result: animations may still be settling even after active stack changes. Do not infer that a completed transition guarantees the next action is ready. Never repeat uncertain actions under a new ID.

## Fairness

Use only the assigned player's observations and server-provided target IDs. `nearby_targets` and `inspect_target` are partial coverage, not a complete map. Missing targets or route data do not prove absence or inaccessibility. Do not use process memory, raw addresses, files, saves, screenshots of another turn, or another player's credentials to obtain hidden state.

Game reference materials are being prepared separately. Until search/resources are actually listed by the connected MCP server, do not invent their names or claim access to bundled references.

Combat.Log contains the last24 entries of the actual game log with zero-based Index; LogCount is the full current battle count. These are not hover hints. Action evidence records entries added since dispatch; verify both log and state. Indices reset for a new battle; durable battle IDs and full-history paging remain unfinished.
