---
id: hota.multiplayer-hd
title: "Hotseat, LAN и HD Mod"
game_scope: "Installed HotA 1.8.0 with HD Mod; online 1.8.1 comparison; interface functions are launcher/build dependent"
topics: [hotseat, LAN, online, simultaneous turns, HD Mod, interface]
sources: [hota.documentation, hota.faq, hdmod.community, hota.changelog]
verification_status: "official HotA multiplayer constraints verified; HD feature list partly secondary and must be probed locally"
checked_at: "2026-09-19"
---

## Факты

- Hotseat передаёт управление сторонами по очереди; экран передачи хода и активная сторона — отдельные состояния. LAN/online требует отдельного клиента/стороны и синхронизации; MCP не должен читать сведения соперника или подменять сетевой протокол.
- HD Mod добавляет масштабируемое окно, widescreen/resolution options, быстрый split stacks, map grid/quick menu и ряд hotkeys. Вторичная справка описывает F5/среднюю кнопку для Quick Menu, Alt+click для подъёма героя/города в списке и B/G для Marketplace/Thieves’ Guild; доступность проверяйте в текущем HD Launcher.
- Для 16:9 FAQ HotA рекомендует 1180×664 (также 1280×720/1285×723), чтобы интерфейс оставался читаемым; это рекомендация интерфейса, не требование игрового моста.
- Simultaneous Turns — опция HD; extended event system может прервать одновременные ходы при конфликтующих одноразовых квестах/событиях или изменении общей переменной.
- В 1.7.2 исправлялись отдельные сетевые ошибки Factory/Lightning Rod; в 1.8.1 исправлены Runes/Altar of the Runes и ещё один Lightning Rod desync. Для локальной 1.8.0 Runes/Altar sync fix не гарантирован. Сборка должна быть единой у участников.

## Советы агенту

В наблюдении явно указывайте `mode`, `active_player`, `handoff_required` и `simultaneous_turns`. Не показывайте агенту следующую сторону до штатной передачи. Для HD hotkeys возвращайте только реально доступные элементы UI; не считайте наличие HD Mod доказанным по одному пути к игре.
