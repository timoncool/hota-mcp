---
id: playbook-turn-end
title: Конец хода, stale state и подтверждение результата
topics: [end-turn, revision, idempotency, journal]
version: 2026-09-19
sources: [../../../TZ.md, ../../../src/HotaMcp/GameTools.cs]
status: draft-research
type: how-to
layer: agent
updated: "2026-09-20"
---

# Цикл завершения

Агент не нажимает конец хода, пока не проверил pending dialogs, доступные действия героев/городов и цель хода. В текущем bridge отдельного `end_turn` нет, поэтому завершение хода остаётся недоступным capability.

Для будущего action tool: передать ожидаемую revision и уникальный operationId; дождаться подтверждённого результата; при timeout повторно наблюдать ту же операцию, не отправляя новый ID; после смены active player создать новое наблюдение и отдельный контекст памяти. Нельзя смешивать две стороны hotseat.

`read_journal` показывает недавние результаты контроллера, но не является полной памятью партии и не раскрывает opponent history. `plan` лишь читает/сохраняет план. `click_ui` требует revision и повторяет только ту же операцию; по описанию сейчас поддерживает лишь system options/adventure.
