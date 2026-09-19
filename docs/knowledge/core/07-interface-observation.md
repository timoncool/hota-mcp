---
id: h3.core.interface-observation
title: Игровой интерфейс и доступные наблюдения
game_version_scope: Heroes III Complete with HD/HotA interface differences
topics: [adventure-screen, town-screen, combat-screen, dialogs, minimap, shortcuts]
verification_status: verified_concepts; exact-layout-versioned
source_urls:
  - https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/297000/manuals/BONUS_Heroes_of_Might_and_Magic_III_HDEdition_OldManual1999_EN.pdf
  - https://heroes.thelazy.net/index.php/Tutorial_Manual
  - https://h3hota.com/en/faq
---

Основные экраны — карта приключений, город, бой, книга заклинаний и модальные диалоги. Руководство перечисляет на HUD дату, ресурсную панель, город, героя, шахту, свободный ресурс и панель статуса (раздел **Adventure Map View**, стр. 14). В городе видны доход, доступные существа, гарнизон и армия посещающего героя (стр. 15); в бою — поле, стеки и кнопки действий.

Интерфейс HD Mod/HotA может менять масштаб, панели, горячие клавиши и компоновку. Поэтому MCP должен читать семантическое состояние и доступность действия, а не полагаться на координаты экрана. Тексты и позиции элементов version-scoped.

Диалог, повышение уровня, встреча или передача хода блокируют прежнюю последовательность команд. Наблюдение должно содержать `screen`, `modal`, `revision`, сторону и список доступных действий. Если состояние не подтверждено, вернуть `unknown/unavailable`.

Не выдавать игроку скрытые поля памяти, внутренние адреса, данные других сторон или недоступные человеку подсказки. Состояние UI — это разрешённое представление, а не полный дамп игры.
