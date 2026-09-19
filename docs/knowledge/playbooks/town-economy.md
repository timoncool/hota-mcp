---
id: playbook-town-economy
title: Город, экономика, строительство и найм
topics: [town, economy, building, recruitment]
version: 2026-09-19
sources: [../sources/bot-algorithms.md, ../../../TZ.md]
status: unavailable-tools
---

# Предлагаемый цикл после появления tools

Сначала прочитать собственный town state и доступные человеку здания, стоимость, доступный найм и pending dialogs. Затем проверить зависимости цели: ресурсы, prerequisites, текущий день, свободные слоты и назначенный город. Выбрать одну подтверждаемую операцию, отправить её с revision/operationId и сверить результат новым наблюдением.

Это адаптация для LLM, а не вывод о том, что мост уже умеет строить. VCMI `BuildAnalyzer`, `BuildingBehavior`, `BuyArmyBehavior`, `BuyArmy` и `SaveResources` подтверждают полезность разделения анализа доступного строительства, покупки и резерва ресурсов. Они не являются HotA API.

В текущем `GameTools.cs` нет `open_town`, `build`, `recruit_units`, `transfer_army`, `select_town` или экономического tool. Нельзя менять ресурсы напрямую, рассчитывать покупку по скрытой памяти или подменять её неизвестным UI click. Capability status должен сообщать этот пробел.

## Безопасность состояния

После любой покупки/стройки заново читать ресурсы и город. Не повторять operation с новым ID после timeout: сначала проверить revision/journal. Не принимать за факт, что VCMI-логика оптимальной стройки переносима на конкретный HotA мод и карту.
