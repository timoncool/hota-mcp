---
id: hota.rmg-templates
title: "Случайные карты и шаблоны HotA"
game_scope: "Installed HotA 1.8.0; online 1.8.1 comparison; HotA RMG"
topics: [RMG, templates, water, zones, objects, multiplayer templates]
sources: [hota.documentation, hota.changelog, local.hota.templates]
verification_status: "official RMG rules plus local template files verified; local templates are not proof of installed game version"
checked_at: "2026-09-19"
---

## Факты

- В официальном RMG вода — явный режим. На безводных картах убираются морские артефакты, Summon/Scuttle Boat, Water Walk и Navigation; набор стартовых героев также меняется (например Beatrice/Kinkeria/Derek/Biarma против Sylvia/Voy/Elmore/Haugir). Выбор уровня воды сразу влияет на доступных стартовых героев.
- Зоны без города могут стать Cove на sand/swamp, Factory на wasteland и Bulwark на snow. Доступность фракций и объектов задают свойства карты и шаблон.
- HotA исправляет генерацию проходов, дорог, стражей, заблокированных объектов, размеров зон и морских элементов. Исправление пустых водных зон, custom settings объектов из шаблонов и лимит до 10 000 событий/Pandora на случайных картах — `1.8.1-only`; не считать их гарантированными в установленной 1.8.0.
- Локальный `G:\HoMM 3 Complete\HotA_RMGTemplates\1deaL\rmg.txt` и каталог `_HD3_Data\Templates` показывают расширенный формат шаблонов, поля Cove и правила зон/terrain. Это локальные файлы пользователя, доступные read-only; они не устанавливают версию HotA.

## Советы агенту

Запрашивайте у MCP имя шаблона, уровень воды, список разрешённых городов/героев и версию карты до планирования старта. Не переносите ограничения `1deaL` или другого шаблона на «случайную карту вообще». Если шаблон не распознан, показывайте настройки как неизвестные.
