---
id: hota.gaps
title: "Пробелы и неопределённости HotA knowledge"
game_scope: "HotA MCP project; installed 1.8.0, online 1.8.1; audit date 2026-09-19"
topics: [gaps, verification, local build, HD Mod]
sources: [hota.sources, TZ, local.complete.readme]
verification_status: "open items explicitly recorded"
checked_at: "2026-09-19"
type: reference
layer: official
updated: "2026-09-20"
---
# Пробелы и неопределённости HotA knowledge


## Не установлено

- Установленная версия теперь подтверждена UI ID3001 как HotA 1.8.0. Сохранены хеши `h3hota.exe` `5AAAB925F06CCCF23BB09814767590A95B84A557EB33D244800520BE4F1F18DE` и `HotA.dll` `0A1DAA1D8F29870B5CB72EBBA54A88C43A366473530B908BD23FAFC7968223A7`.
- Нельзя гарантировать наличие HD Mod, его плагины, разрешение, хоткеи или Online Lobby без чтения текущего launcher state.
- Онлайн-документация описывает много деталей списками; значения баланса, доступность героев и spell research могут меняться между версиями и шаблонами.
- Локальные RMG templates — пользовательская коллекция; они не равны встроенному набору текущей HotA. Правила 1.8.1-only нельзя переносить в установленную 1.8.0.
- Полный справочник всех числовых статов существ, героев и объектов намеренно не продублирован; для точного числа используйте официальную страницу соответствующей версии и проверяйте карту.

## Что проверить адаптером

`build_id`, режим hotseat/LAN, активную сторону, уровень воды, имя шаблона, разрешённые фракции, наличие HD Mod/Quick Menu, активный spell research и видимость объекта. При отсутствии подтверждения возвращайте `unknown`, а не значение из последней онлайн-версии.
