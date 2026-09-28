---
id: hota.version-scope
title: "HotA версии и границы совместимости"
game_scope: "Installed HotA 1.8.1 (latest, 25.08.2026); Complete/SoD base; checked 2026-09-29"
topics: [versions, changelog, compatibility, Complete]
sources: [hota.download, hota.changelog, local.complete.readme, local.installed.build]
verification_status: "installed 1.8.1 verified 2026-09-29 by HotA_Setup.ini, HotA.dll date and live creature tables"
checked_at: "2026-09-29"
type: reference
layer: official
updated: "2026-09-29"
---
# HotA версии и границы совместимости


## Факты

- Установлена последняя версия HotA 1.8.1 (25.08.2026), официальным установщиком поверх 1.8.0. Подтверждение 29.09.2026: HotA_Setup.ini «Main Version=1.8.1», HotA.dll от 25.08.2026 и таблица существ запущенной игры (AI Value Боевого Мамонта 1672 — значение 1.8.1).
- HotA ставится поверх Shadow of Death или Complete. Ubisoft «HD Edition» и чистые Restoration of Erathia/Armageddon’s Blade не являются поддерживаемой основой.
- 1.8.0 добавила Кронверк (Bulwark) и расширенную систему событий.
- 1.8.1 поверх 1.8.0: Ice Formations и связанные шахты, лимит событий/Ящиков Пандоры на RMG до 10 000, лимит Ocean Bottles/Signs 255 и combined Pandora/local events 20 000, исправления Рун и онлайн-синхронизации, специализация Биармы (Стрельба → Первая помощь), правки специализаций Далтона и Эргона, Боевой Мамонт (боевая ценность 1601 → 1672), Ярмарка дороже (2000 золота + 10 дерева), новые шаблоны RMG Conquest и Sapphire, пять новых карт. Всё это действует в установленной игре.
- HD Mod — 5.8 R20 (21.09.2026, последняя на 29.09.2026), обновляется встроенным апдейтером лаунчера.
- Мост не привязан к хешам файлов: при подключении он проверяет, что структуры игры, на которые опирается, на своих местах.

## Советы агенту

Правила 1.8.1 — текущие. Числа существ, артефактов и заклинаний берите из `hota_reference`: он читает таблицы самой запущенной игры и поэтому всегда совпадает с установленной версией. Если подключена другая копия игры, версию смотрите в её главном меню.
