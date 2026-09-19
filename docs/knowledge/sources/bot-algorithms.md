---
id: bot-algorithms
title: Open AI algorithms relevant to Heroes III play cycles
topics: [vcmi, nullkiller2, battleai, source-review, limitations]
version: 2026-09-19
sources:
  - https://vcmi.eu/developers/AI/
  - https://github.com/vcmi/vcmi/blob/develop/docs/developers/Code_Structure.md
  - https://github.com/vcmi/vcmi/tree/develop/AI/Nullkiller2
  - https://github.com/vcmi/vcmi/tree/develop/AI/BattleAI
status: researched
---

# Что действительно подтверждено открытым кодом

VCMI — отдельный open-source движок Heroes III. Его AI не является подключаемым AI для HotA: Nullkiller2 и BattleAI работают внутри VCMI и вызывают серверные callbacks VCMI. Их код полезен как опубликованный образец циклов принятия решений, но не доказывает совместимость с `h3hota HD.exe`, HotA или HD Mod.

## Adventure AI: Nullkiller2

Документация VCMI описывает `Gateway` как точку событий сервера и `CCallback` как интерфейс чтения состояния и запросов действий. На `yourTurn` gateway запускает `makeTurn` в AI-задаче. Основной цикл проходит несколько pass-ов: сброс несерилизуемого состояния, обновление анализаторов/pathfinder, сбор и приоритизация goals, декомпозиция и выполнение лучшей цели.

Подтверждённые части:

- анализаторы строят сведения, а не обязаны принимать стратегические решения: HeroAnalyser различает main/scout, BuildAnalyzer готовит доступные стройки и ресурсы, DangerHitMapAnalyser оценивает угрозу, Pathfinder строит маршруты;
- ObjectClusterizer группирует объекты по блокирующим объектам, DeepDecomposer превращает абстрактную цель в составной план (например, сначала получить ключ/лодку);
- goals/tasks/behaviors разделяют желание, план и конкретное действие. Документация перечисляет `BuildThis`, `BuyArmy`, `ExecuteHeroChain`, `RecruitHero`, `SaveResources`, `CaptureObjectsBehavior`, `BuildingBehavior`, `BuyArmyBehavior`, `StartupBehavior`, `DefenceBehavior` и другие;
- часть состояния обновляется лениво раз в день, часть — на каждом pass. Это источник идеи для адаптера: не пересчитывать дорогую карту после каждого неизменившегося наблюдения.

Это описание алгоритма VCMI, а не обещание функций моста. В HotA-адаптации агент может использовать похожие фазы как внутренний план, но должен получать только разрешённые наблюдения и вызывать реально объявленные MCP tools.

## Combat AI: BattleAI

`activeStack` вызывается для хода активного отряда. `AttackPossibility` представляет один вариант атаки и его оценку урона/эффектов; `PotentialTargets` собирает варианты. `BattleExchangeVariant` упрощённо моделирует фиксированную последовательность обменов с учётом порядка ходов, wait и retaliation. `BattleExchangeEvaluator` выбирает лучший обмен. `BattleEvaluator` добавляет заклинания и движение к недостижимым целям. Затем BattleAI отправляет команды через VCMI callback.

Документация также указывает `HypotheticBattle`: копия/обёртка изменяемых состояний для оценки действия без изменения реального состояния. Это хороший принцип для будущего боевого планировщика, но в текущем HotA bridge боевые команды отсутствуют.

## Что нельзя переносить без доказательства

- VCMI имеет сервер, клиент, свои callbacks, сетевую синхронизацию и TBB-потоки; HotA/HD Mod этого контракта не предоставляют.
- Названия `Nullkiller`, `BattleAI`, `CCallback`, `HypotheticBattle` не являются tools проекта.
- VCMI maphack/difficulty или полная внутренняя карта не являются допустимым наблюдением HotA-агента.
- VCMI algorithmic quality claims не означают, что LLM должен повторять скрытые оценки, pathfinder или стратегические советы в `observe`.

## Статус для HotaMcp

Исходники `src/HotaMcp/GameTools.cs` подтверждают только `nearby_targets`, `inspect_target`, `game_status`, `observe`, `click_ui`, `read_journal`, `plan`. Сейчас `click_ui` ограничен открытием системных опций и возвратом на adventure. Нет доказанного tools для движения, строительства, найма, боя или завершения хода. Эти пробелы должны оставаться явными в capability discovery.
