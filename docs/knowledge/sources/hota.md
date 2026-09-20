---
id: hota.sources
title: "HotA knowledge source registry"
game_scope: "Heroes III Complete/SoD + installed HotA 1.8.0; online HotA 1.8.1; checked 2026-09-19"
topics: [sources, provenance, verification]
sources: [hota.download, hota.documentation, hota.changelog, hota.faq, hdmod.community, local.complete.readme, local.hota.templates, local.installed.build, TZ]
verification_status: "registry checked 2026-09-19"
checked_at: "2026-09-19"
type: explanation
layer: agent
updated: "2026-09-20"
---
# HotA knowledge source registry


| ID | Источник | Роль | Область | Статус |
|---|---|---|---|---|
| `hota.download` | [Official HotA download](https://h3hota.com/en/download) | текущая версия, архив, установка | HotA 1.8.1 online | первичный, проверен 2026-09-19 |
| `hota.documentation` | [Official HotA documentation](https://h3hota.com/en/documentation) | города, ростеры, объекты, RMG, редактор, механики | содержимое HotA; детали по версии | первичный, проверен 2026-09-19 |
| `hota.changelog` | [Official English changelog](https://download.h3hota.com/upd/changelogs/eng.txt) | изменения 1.8.1, 1.8.0, 1.7.3, 1.7.2 | version-specific | первичный, проверен 2026-09-19 |
| `hota.faq` | [Official HotA FAQ](https://h3hota.com/en/faq) | статус проекта, три города, официальные каналы | HotA general | первичный, проверен 2026-09-19 |
| `hdmod.community` | [HD Mod extended functionality tips](https://heroes3wog.net/homm3-hd-mod-extended-functionality-tips/) | hotkeys и Quick Menu | HD Mod; secondary | вторичный, явно помечен |
| `local.complete.readme` | `G:\HoMM 3 Complete\README.TXT` | базовая Complete, старые hotseat/TCP-IP сведения | Complete 4.0 readme, локальный read-only | локальный исторический, не HotA |
| `local.hota.templates` | `G:\HoMM 3 Complete\HotA_RMGTemplates\1deaL\rmg.txt`, `_HD3_Data\Templates\*\rmg.txt` | реальный формат и пользовательские RMG templates | локальная коллекция | локальный read-only; версия не доказана |
| `local.installed.build` | UI ID3001 после перезапуска + EXE/DLL SHA-256 | установленная сборка | HotA 1.8.0 | локальный runtime, подтверждено 2026-09-19 |
| `TZ` | `outputs/hota-agent-bridge/TZ.md`, разделы 2, 3, 21 | проектные границы: HotA+HD, честность, MCP retrieval | текущий проект | внутренний контракт |

## Правила цитирования

Официальные страницы имеют приоритет. Wiki/порталы используются только для вторичных UI-подсказок и помечаются как secondary. Дата проверки — 2026-09-19. «Последняя онлайн» не подменяет установленную сборку; для неё требуется локальный build ID/hash.
