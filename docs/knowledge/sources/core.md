# Core knowledge sources

Эти источники покрывают базовую справку и границы применимости. Ссылки ведут на руководства или официальные страницы проекта; пересказ в `docs/knowledge/core/` выполнен кратко и собственными словами.

| ID | Источник | Использование | Статус |
|---|---|---|---|
| `src.h3.manual.steam` | [Heroes III HD Edition old manual (PDF)](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/297000/manuals/BONUS_Heroes_of_Might_and_Magic_III_HDEdition_OldManual1999_EN.pdf) | Базовые правила календаря, карты, городов, героев, магии, боя и интерфейса; историческое руководство для Complete baseline | verified primary/manual |
| `src.h3.manual.local` | `G:\HoMM 3 Complete\Heroes3_Manual.pdf` | Локальная копия руководства, read-only проверено наличие | local artifact; содержание не перепечатано |
| `src.h3.help.local` | `G:\HoMM 3 Complete\_HD3_Data\Common\HELPTXT.ENG` | Локальные тексты подсказок и интерфейсные названия | local artifact; version-scoped |
| `src.h3.tutorial.lazy` | [Heroes 3 Tutorial Manual](https://heroes.thelazy.net/index.php/Tutorial_Manual) | Проверка базового потока карты и обучения; вторичный справочник | corroborating |
| `src.h3.manual.mirror` | [Manual mirror](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) | Поиск отдельных разделов и сверка текста руководства | secondary mirror |
| `src.h3.manual.page50` | [Town building rule, manual page 50](https://www.manualshelf.com/manual/games-pc/heroes-of-might-and-magic-iii/user-guide-english/page-50.html) | Лимит: не более одной постройки в городе за день | corroborating manual page |
| `src.h3.ab.manual` | [Armageddon’s Blade manual](https://heroes3wog.net/download/%5BHeroes%203%5D%20Armageddons%20Blade%20Manual.pdf) | Проверка: AB добавляет Conflux к восьми исходным типам городов | expansion manual mirror |
| `src.hota.documentation` | [HotA Documentation](https://h3hota.com/en/documentation) | Официальная граница и перечисление добавлений/изменений HotA | verified project source |
| `src.hota.download` | [HotA Download](https://h3hota.com/en/download) | Совместимость установки с Complete/SoD и версиями HotA | verified project source |
| `src.hota.faq` | [HotA FAQ](https://h3hota.com/en/faq) | Версионные и режимные оговорки | verified project source |

## Правило цитирования и доверия

Существенное правило в core-файле должно иметь хотя бы одну запись `source_urls`. Общие базовые правила сверены с руководством; HotA-дельты требуют ссылки на документацию HotA и маркировки области версии. Точные числа без подтверждения локальной сборки не считаются установленными.

## Точные привязки основных правил

| Правило | Привязка |
|---|---|
| Игроки ходят по очереди; начало дня начисляет доход; городские жилища производят в первый день недели | Руководство, **Gameplay**, печатная стр. 13; [строки 571–573](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Стандартные ресурсы: gold, wood, ore, crystal, gems, mercury, sulfur | Руководство, **Resource Mines and Loose Resources**, стр. 17; [строки 718–720](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Запас движения зависит от самого медленного существа | **Movement Allowance**, стр. 15; [строки 652–669](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Местность и дороги меняют стоимость; Pathfinding уменьшает штраф | **Terrain and Roads**, стр. 16; [строки 678–688](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Посадка/высадка забирает остаток движения дня | **Boats**, стр. 16; [строки 690–692](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| У героя максимум семь стеков, в стеке один тип существа | **Heroes**, стр. 14; **Combat**, стр. 40; [строки 630–631](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| В бою до семи стеков на сторону; герой не атакует напрямую | **Combat**, стр. 40; [строки 1576–1580](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Один active-action на стек за раунд; порядок по скорости | **Troop Actions**, стр. 42; [строки 1655–1657](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Стрельба: ограниченные выстрелы, нельзя стрелять при соседнем враге | **Perform a Ranged Attack**, стр. 42; [строки 1662–1666](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Wait и defend; defend даёт 20% защиты до конца раунда | **Wait / Defend**, стр. 43; [строки 1681–1690](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |
| Завершение боя: retreat, surrender или полное уничтожение | **Ending Combat**, стр. 43; [строки 539–541](https://manualzz.com/doc/28894522/ubisoft-heroes-of-might-and-magic-iii-video-game-user-manual) |
| Один город строит не более одной постройки в день | **Towns**, стр. 50; [копия страницы 50](https://www.manualshelf.com/manual/games-pc/heroes-of-might-and-magic-iii/user-guide-english/page-50.html) |
| Внешние жилища пополняются раз в неделю | **Adventure Locations**, стр. 18; [строки 739–740](https://manualmachine.com/gamespc/heroesofmightandmagiciii/1119554-user-manual/) |

## Локальная проверка

На 2026-09-19 read-only проверено наличие `Heroes3_Manual.pdf`, `README.TXT` и `HELPTXT.ENG` в установленной `G:\HoMM 3 Complete`. Локальные файлы не изменялись.
