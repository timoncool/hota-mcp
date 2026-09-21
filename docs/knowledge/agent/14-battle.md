---
id: playbook-battle-cycle
title: Подготовка боя и цикл боевого решения
topics: [battle, tactics, simulation, uncertainty]
version: 2026-09-19
sources: [../sources/bot-algorithms.md, ../../../TZ.md]
status: verified-partial
type: how-to
layer: agent
updated: "2026-09-21"
---

# Подготовка

При появлении боя агент должен остановить маршрут и получить согласованное battle observation: стороны, видимые отряды, active stack, поле/препятствия, доступные действия и pending dialog. Нельзя продолжать старый план героя через бой.

## Решение

Обобщаемый LLM-цикл: перечислить легальные действия активного отряда; для каждого оценить видимый непосредственный результат; выбрать действие с учётом цели партии; отправить одну команду; получить новую revision. При неизвестном результате сначала наблюдать, затем решать. Смерть/победа/новый экран завершают battle loop.

VCMI BattleAI даёт более конкретный опубликованный алгоритм: AttackPossibility → PotentialTargets → BattleExchangeVariant, затем BattleExchangeEvaluator; BattleEvaluator добавляет заклинания и движение. HypotheticBattle моделирует варианты без изменения настоящего состояния. Это документированный исходный алгоритм VCMI, а не обещание HotA bridge.

Бой выполняется действиями из `observe.Actions`: `combat:move:<клетка>`, `combat:attack:<отряд>`, `combat:wait`, `combat:defend`, `combat:retreat`, `combat:surrender`, `combat:spellbook`, `combat:auto`; заклинание — `spellbook:select:<id>` и затем `spell:target:<отряд>`. Атака с карты — отдельный инструмент `attack_target`. Боевое действие считается выполненным, только когда вырос боевой журнал и сменился активный отряд или раунд; заклинание дополнительно требует расхода маны. Непроверенными остаются: осада, сдача, полное покрытие школ магии.
