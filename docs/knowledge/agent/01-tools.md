---
id: h3.agent.tools
title: Инструменты моста (сгенерировано из живого MCP)
game_version_scope: HotA 1.8.x поверх Heroes III Complete
topics: [python, build, gen-tools-doc, tools, list, resources, execute, available]
verification_status: собрано из открытых источников; внутриигровым замером не проверялось
type: how-to
layer: agent
updated: "2026-09-21"
---
# Инструменты моста (сгенерировано из живого MCP)

Файл собран командой `python build/gen-tools-doc.py`: список берётся прямо у работающего моста (MCP `tools/list`, `resources/list`), поэтому всегда соответствует текущей сборке.

## Инструменты

| Инструмент | Что делает |
| --- | --- |
| `act` | Execute an available semantic action key from observe.Actions, with the current revision. Construction spends normal game resources. Keep operationId unchanged for retries; uncertain means observe before making another decision. |
| `attack_target` | Move the selected own hero onto a visible creature stack and start the battle deliberately. Use only after observing the situation; the ordinary move_to now refuses creature cells, because stepping there fights. The game decides the outcome; uncertain is not permission to retry under another operation ID. |
| `click_ui` | Click a validated UI action from the latest observation. Accepts a key from observation Actions or validated system-option buttons. Includes own towns, construction cards and purchase/cancel. Game enforces costs and daily construction limits. Use the SAME operationId for retries; never retry an uncertain action with a new ID. |
| `debug_capture` | Explicit developer diagnostic only: save the game's current rendered framebuffer as a local PNG without window activation, cursor movement, keyboard input or desktop capture. Returns file metadata only, never inline image data. Do not use in normal gameplay loops; observe provides economical structured game state. |
| `debug_snapshot` | Explicit diagnostic: in one call read fair structured observation, save the game framebuffer PNG and matching JSON, and verify the observed revision is unchanged across capture. Returns observation and local file metadata, never inline images. Does not freeze animations. Use for UI mapping/debugging, not normal gameplay; no focus or input changes. |
| `game_status` | Read supported capabilities and current development limitations. No game action. |
| `hota_docs` | Ask the project's own documentation and get the matching text back: game rules, playbooks, hotkeys and controls, combat, towns and economy, save/load cycle, capability map and the running lessons log. Use this whenever you are unsure what to do next, how a game function is operated, or whether a capability exists. Returns the best matching sections with their file, heading, score and text; empty hits mean the documentation does not cover the question yet. |
| `hota_docs_catalog` | List every document this bridge can answer from, with the headings inside each document. Use it when no search hit looks right, or to see what knowledge exists before asking. |
| `hota_docs_read` | Read one document, or one heading inside it, exactly as the catalog or a search hit named it. Use after a search to read the full section instead of guessing from a snippet. |
| `inspect_target` | Read a visible target and available game route data directly from memory by target ID. Does not send keyboard or mouse input, move the hero, or add reference knowledge. Unavailable route data is not evidence that the target is unreachable. |
| `launcher_graphics` | Read HD Launcher renderer options, or select one exact returned renderer label for the next game launch. Pass null to read. Only change graphics when the user asks; does not activate windows or use mouse/keyboard input. |
| `map_click` | Click a point on the adventure map surface (game pixel coordinates). This is the game's own way to make it compute routes: after such a click the route cache exists and nearby_targets starts reporting reachable routes. Use the centre of the map area to reach the hero's own tile without ordering a move. |
| `move_to` | Move the selected own hero toward a currently visible target ID from nearby_targets using the ordinary game route and move handlers. May collect the resource, open a dialog or encounter enemies. Experimental incomplete adapter: uncertain is not permission to retry under another operation ID. No coordinates or hidden data required. |
| `move_to_tile` | Move the selected own hero to an explicit map cell (x,y,z) using the game's own route planning and move command. Use for exploration and for reaching cells without a known object, for example revealed terrain or a town seen on the map. The game plans the path itself; if it cannot, the result stays uncertain and nothing moves. Hero stops when daily movement runs out. |
| `nearby_targets` | Read recognized visible destinations by target ID and available game route data. Reads game memory without UI input. No hidden objects, terrain-rule explanations or strategic recommendations. Object coverage and route support are incomplete. |
| `observe` | Observe your assigned player's own hero/resources and supported active UI. Returns a revision required for actions. Unknown screens and wrong-player contexts are denied. |
| `plan` | Read or save your concise strategy, goals and next steps. Pass null to read. A plan does not execute actions or change the game. |
| `read_journal` | Read this controller's recent action results and plan updates. Contains no opponent history. |
| `start_game` | Start HotA using the existing host HD Launcher's Play action and saved settings. Does not activate windows or send mouse/keyboard input. Returns already_running or launch_pending on repeated requests; use game_status and observe to check readiness. |

## Ресурсы

- `hota://docs/index` — Hota MCP documentation index (application/json)
- `hota://docs/{path}` — Hota MCP documentation file (text/markdown)

## Игровые действия

Действия внутри экранов игры не перечислены здесь: они приходят в `observe` в списке `actions` с русским описанием и зависят от текущего экрана (карта, город, бой, диалог). Клик по действию — `click_ui` с ключом из этого списка и текущей `revision`.
