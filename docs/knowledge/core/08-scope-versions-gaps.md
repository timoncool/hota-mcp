---
id: h3.core.scope-versions-gaps
title: Границы справки и проверка версии
game_version_scope: Complete baseline plus explicit HotA boundary
topics: [scope, compatibility, verification, uncertainty, adapter]
verification_status: verified_scope; not_exhaustive
source_urls:
  - https://h3hota.com/en/download
  - https://h3hota.com/en/documentation
  - https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/297000/manuals/BONUS_Heroes_of_Might_and_Magic_III_HDEdition_OldManual1999_EN.pdf
---

Эта база — рабочая начальная справка для retrieval, а не исчерпывающий rulebook. Базовая линия — Heroes III Complete с официальными дополнениями; HotA — отдельный слой поверх игры. HD Mod меняет прежде всего запуск и интерфейс, но конкретную сборку нужно фиксировать отдельно.

Перед применением числового правила адаптер должен знать редакцию, версию HotA/HD, карту и режим. Поля с точными стоимостями, формулами, шансами, лимитами или изменяемыми балансными значениями имеют статус `unverified`, если они не подтверждены локальным запуском и источником.

Проверено наличие локальных материалов в `G:\HoMM 3 Complete`: `Heroes3_Manual.pdf`, `README.TXT`, `_HD3_Data\Common\HELPTXT.ENG`, а также HotA RMG templates. Они пригодны как локальные reference-артефакты; полная расшифровка PDF в эту базу не копируется.

Остаются пробелы: полные таблицы зданий/цен и роста, все вторичные навыки и заклинания, точные формулы боя, HotA-дельты по версиям, сценарные исключения, сетевой/одновременный ход, генератор карт и полный словарь интерфейсных состояний. Эти темы должны пополняться отдельными version-scoped записями.
