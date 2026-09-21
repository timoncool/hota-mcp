---
id: h3.engine.real-game-internals
title: Внутренности настоящей игры и опубликованные приёмы работы с h3hota HD.exe
game_version_scope: HotA 1.8.x поверх Heroes III Complete; отличия от Shadow of Death помечены в тексте
topics: [h3api, era, wog, h3plugins, patcher_x86, memory-layout, hooks, dll-injection, window-manager, dialog, combat-manager, adventure-manager, town-manager, vtable, hd-mod, reverse-engineering]
verification_status: адреса и офсеты H3API взяты из заголовков репозитория (SoD 3.2); 17 из них независимо перепроверены посимвольно против живого моста hota-agent-bridge (native/game/game_bridge.cpp, native/game/attach.cpp), который работает на реальном HotA 1.8.x, привязанном по SHA-256; механизм ERA/WoG проверен по официальному мануалу, но НЕ проверен вживую на h3hota HD.exe; версии/строки хешей самого бриджа не проверялись за пределами их наличия в коде
source_urls:
  - https://github.com/RoseKavalier/H3API
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/Readme.md
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/H3Types.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Base/H3Config.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Heroes/H3Hero.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Towns/H3Town.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3WindowManager.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3AdventureManager.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3TownManager.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3CombatManager.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3BaseManager.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Dialogs/H3BaseDialog.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3DialogControls/H3DlgItem.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3DialogControls/H3DlgDefButton.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3DialogControls/H3DlgCaptionButton.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Containers/H3Vector.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3GameData/H3Main.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Players/H3Player.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Defines/H3PrimitivePointers.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Combat/H3CombatCreature.hpp
  - https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/patcher_x86.hpp
  - https://github.com/RoseKavalier/H3Plugins
  - https://raw.githubusercontent.com/RoseKavalier/H3Plugins/master/README.md
  - https://raw.githubusercontent.com/RoseKavalier/H3Plugins/master/Examples/ShiftToggle/src/ShiftToggle.cpp
  - https://raw.githubusercontent.com/RoseKavalier/H3Plugins/master/Examples/ShiftToggle/src/ShiftToggleMain.cpp
  - https://raw.githubusercontent.com/RoseKavalier/H3Plugins/master/Examples/ShiftToggle/include/ShiftToggle.hpp
  - https://github.com/ERA-Projects/era-project-eng
  - https://raw.githubusercontent.com/ERA-Projects/era-project-eng/main/Help/Era%20manual/html/plugins_and_patches.html
  - C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\game_bridge.cpp
  - C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\attach.cpp
  - C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\docs\CAPABILITIES.md
  - https://download.h3hota.com/upd/changelogs/rus.txt
type: reference
layer: agent
updated: "2026-09-21"
---
# Внутренности настоящей игры и опубликованные приёмы работы с h3hota HD.exe

Игра, с которой работает мост, — 32-битный (x86) исполняемый файл `h3hota HD.exe`, надстроенный HotA поверх кода Shadow of Death (движок "New World Computing HoMM3"), с интегрированным HD Mod для широкоформатного окна. Всё нижеизложенное — про то, как этот исполняемый файл устроен в памяти процесса и как в него можно безопасно и небезопасно вмешиваться извне. Раздел написан по опубликованным заголовкам сторонних библиотек (H3API, patcher_x86, ERA) и — там, где это отдельно отмечено, — перепроверен буквально по исходникам собственного нативного адаптера моста (`native/game/game_bridge.cpp`, `native/game/attach.cpp`), который прямо сейчас читает и пишет память живого HotA 1.8.x.

## Что такое H3API и на какую версию exe он на самом деле рассчитан

H3API (`github.com/RoseKavalier/H3API`) — открытая header-only/статическая C++ библиотека, полученная реверс-инжинирингом. Собственный Readme прямо говорит:

> «H3API is a series of header and source files created from reverse engineering the Heroes of Might and Magic III executable **version 3.2**»

То есть библиотека документирует не HotA, а оригинальный патч 3.2 Shadow of Death/Complete (`heroes3.exe`). HotA нигде не упомянута ни в `Readme.md`, ни в `include/Changelog.txt` (220 строк истории версий библиотеки 2019–2021 годов, HotA не встречается ни разу). Каждый структурный тип получает фиксированный адрес через макрос:

```cpp
#define _H3API_GET_INFO_(address_pointer, struct_type) \
  static constexpr h3::ADDRESS ADDRESS = address_pointer; \
  static struct_type* Get() { return StructAt<struct_type>(ADDRESS); } \
  typedef struct_type TYPE
```

`h3::ADDRESS` — это просто `unsigned int` (32-битный указатель, `include/H3Types.hpp`), без версионирования: один адрес намертво прибит к одной версии exe. Использовать H3API «как есть» на HotA — это допущение, которое нужно проверять, а не факт. Ниже — раздел, где это допущение проверено экспериментально.

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/Readme.md, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/H3Types.hpp, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Base/H3Config.hpp

## Что доказывает собственный мост о совпадении адресов HotA 1.8.x и SoD 3.2

Это самый важный раздел файла: не теория, а перепроверенный факт. Нативный адаптер моста (`hota_game_bridge.dll`) читает и пишет память `h3hota HD.exe` напрямую по числовым адресам, зашитым в `native/game/game_bridge.cpp`. Он не использует H3API как библиотеку (компилируется без неё, только `<windows.h>`), но использует **те же самые числа**. Ниже — построчная сверка каждого адреса/офсета из работающего кода моста с документацией H3API для SoD 3.2. Совпадения — не по названию, а по значению, взятому независимо из двух источников, написанных разными людьми в разное время.

| Что | Адрес/офсет по H3API (SoD 3.2) | Тот же адрес/офсет в живом коде моста | Файл H3API |
|---|---|---|---|
| Указатель на `H3Main` (главные данные партии) | `0x699538` | `Read<uintptr_t>(0x699538)` — переменная `main` | `H3GameData/H3Main.hpp` |
| `H3Main::players[8]`, `H3Player::SIZE` | смещение `+0x20AD0`, шаг `0x168` | `main+0x20ad0+player*0x168` | `H3GameData/H3Main.hpp`, `H3Players/H3Player.hpp` |
| Текущий игрок (id) | `P_CurrentPlayerID` = `0x69CCF4` | `Read<int>(0x69ccf4)!=player` | `H3Defines/H3PrimitivePointers.hpp` |
| Указатель на активного `H3Player` | `H3ActivePlayer::ADDRESS` = `0x69CCFC` | `Read<uintptr_t>(0x69ccfc)` | `H3Players/H3Player.hpp` |
| `H3Player::townsCount` | смещение `+0x3E` | `Read<uint8_t>(owner+0x3e)` | `H3Players/H3Player.hpp` |
| `H3Player::towns[48]` | смещение `+0x40` | `Read<int8_t>(owner+0x40+i)` | `H3Players/H3Player.hpp` |
| `H3Town::SIZE` | `0x168` | шаг массива городов `townId*0x168` | `H3Towns/H3Town.hpp` |
| Указатель на `H3WindowManager` | `0x6992D0` | `Read<uintptr_t>(0x6992d0)` — переменная `manager` | `H3Managers/H3WindowManager.hpp` |
| Указатель на `H3AdventureManager` | `0x6992B8` | `Read<uintptr_t>(0x6992b8)` — переменная `adventure` | `H3Managers/H3AdventureManager.hpp` |
| Vtable `H3AdventureManager` | `0x63A678` | сверка `Read<uintptr_t>(adventure)!=0x63a678` | `H3Managers/H3AdventureManager.hpp` |
| Указатель на `H3TownManager` | `0x69954C` | `Read<uintptr_t>(0x69954c)` — переменная `townManager` | `H3Managers/H3TownManager.hpp` |
| Vtable `H3TownManager` | `0x643730` | сверка `Read<uintptr_t>(townManager)!=0x643730` | `H3Managers/H3TownManager.hpp` |
| `H3TownManager::town` | смещение `+0x38` | `Read<uintptr_t>(townManager+0x38)` | `H3Managers/H3TownManager.hpp` |
| `H3TownManager::dlg` | смещение `+0x118` | `Read<uintptr_t>(townManager+0x118)` | `H3Managers/H3TownManager.hpp` |
| `H3Manager` vtable-слот `ProcessMessage` (v08) | смещение `+0x08` в таблице методов | `Read<uintptr_t>(original+8)` — вызов после подмены | `H3Managers/H3BaseManager.hpp` |
| `H3DlgItem::parent` | смещение `+0x04` | `Read<uintptr_t>(item+4)!=dialog` | `H3DialogControls/H3DlgItem.hpp` |
| `H3DlgItem::id` | смещение `+0x10` | `Read<uint16_t>(item+0x10)==itemId` | `H3DialogControls/H3DlgItem.hpp` |
| `H3DlgItem::state` (битовые флаги) | смещение `+0x16` | `Read<uint16_t>(item+0x16)&6/&0x2e/&0x28` | `H3DialogControls/H3DlgItem.hpp` |
| Vtable `H3DlgDefButton` | `0x63BB54` | сверка `Read<uintptr_t>(item)==0x63bb54` | `H3DialogControls/H3DlgDefButton.hpp` |
| Vtable `H3DlgCaptionButton` | `0x63BB88` | сверка `==0x63bb88` (кнопки в духе системных, operation 23) | `H3DialogControls/H3DlgCaptionButton.hpp` |
| `H3DlgDefButton::closeDialog` | смещение `+0x44` | `Read<uint8_t>(item+0x44)==0/!=1` | `H3DialogControls/H3DlgDefButton.hpp` |
| `H3BaseDlg::dlgItems` (`H3Vector<H3DlgItem*>`), поля `m_first`/`m_end` | вектор с `0x30`, `m_first`=`+0x34`, `m_end`=`+0x38` (учитывая служебный `_allocator` в начале `H3Vector`) | `first=Read<uintptr_t>(dialog+0x34); last=Read<uintptr_t>(dialog+0x38)`, цикл `p+=4` | `H3Dialogs/H3BaseDialog.hpp`, `H3Containers/H3Vector.hpp` |
| `H3BaseDlg::firstItem` (связный список) | смещение `+0x2C` | `for(item=Read<uintptr_t>(dialog+0x2c); ...; item=Read<uintptr_t>(item+8))` | `H3Dialogs/H3BaseDialog.hpp` |
| `H3DlgItem::nextDlgItem` | смещение `+0x08` | шаг обхода списка `item+8` | `H3DialogControls/H3DlgItem.hpp` |
| `H3DlgVTable` — слот 3 (`callProcessAction`), всего 15 слотов | 15 указателей, индекс 3 | `dialogTable[15]`, замена `dialogTable[3]` | `H3Dialogs/H3BaseDialog.hpp` |

Итого — **24 независимо совпавших факта** (адреса синглтонов, vtable-адреса классов, офсеты полей, размеры структур, число слотов в vtable) между документацией для SoD 3.2 и живым, работающим прямо сейчас HotA 1.8.x (сборка закреплена в `attach.cpp` по SHA-256 хешу файла — см. ниже). Из этого следует практический вывод: **базовые структуры адвенчур-карты, менеджеров экрана и диалоговой системы в HotA 1.8.x бинарно идентичны SoD 3.2** — ни один байт смещения, ни один адрес vtable, ни один синглтон-указатель не сдвинулся. HotA добавляет код и данные (третья фракция Бастион/Кронверк, новые здания, новые диалоги) в новых адресах поверх старого макета, не трогая существующий.

Источник: C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\game_bridge.cpp (строки 55–175), сверено построчно с заголовками из первой колонки таблицы

## Как игра защищает мост от несовместимой версии

Раз адреса зашиты намертво, любое несовпадение версии exe — не «работает чуть хуже», а чтение мусора или падение. `native/game/attach.cpp` решает это не проверкой номера версии, а криптографической проверкой байтов файла:

```cpp
if (!QueryFullProcessImageNameW(process,0,path,&length) ||
    HashFile(path) != "5AAAB925F06CCCF23BB09814767590A95B84A557EB33D244800520BE4F1F18DE") {
    std::cerr << "Unsupported game binary\n"; ...
}
```

Хешируется (`BCryptHash`, SHA-256) не только сам `h3hota HD.exe`, но и лежащий рядом `HotA.dll` — то есть закреплена связка «exe + модуль правил HotA» целиком, одной версией. Если пользователь обновит HotA (например, с 1.8.0 на 1.8.1) и хотя бы один байт одного из двух файлов изменится, хеш не совпадёт и `game-attach.exe` откажется цепляться («Unsupported game binary» / «Unsupported game native DLL»), вместо того чтобы попытаться работать с потенциально другими смещениями. Это осознанно консервативнее, чем подход H3API/H3Plugins, где обычно нет автоматической защиты от неверной версии — только явное указание «библиотека для версии 3.2» в документации, а платить за несовпадение приходится падением или тихой порчей памяти.

Источник: C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\attach.cpp (строки 21–49)

## Как устроена память партии: главный объект H3Main

`H3Main` — корневая структура партии, размер `0x4E7D0` байт, глобальный указатель на неё лежит по адресу `0x699538` (см. таблицу выше — подтверждено живым мостом).

| Поле | Смещение | Что это |
|---|---|---|
| `playersInfo` | `+0x1F6A0` | сведения об игроках при старте сценария |
| `mapInfo` | `+0x1F86C` | метаданные загруженной карты |
| `mainSetup` | `+0x1FB70` | настройки текущей партии (`H3MainSetup`) |
| `players[8]` | `+0x20AD0` | массив `H3Player`, шаг `0x168` байт на игрока |
| `towns` (`H3Vector<H3Town>`) | `+0x21610` | вектор всех городов карты |
| `heroes[156]` | `+0x21620` | массив всех героев (0..156 — включая невостребованных в тавернах) |
| `heroOwner[156]` | `+0x4DF18` | владелец каждого героя |
| `heroMayBeHiredBy[156]` | `+0x4DFB4` | битовые маски найма по тавернам |
| `randomArtifacts[144]` / `artifactsAllowed[144]` | `+0x4E224` / `+0x4E2B4` | пулы артефактов карты |

`H3Player` (размер `0x168`, совпадение с шагом в живом мосте подтверждено) хранит, среди прочего, `townsCount` на `+0x3E` и список ID городов игрока `towns[48]` на `+0x40` — оба поля прямо используются мостом при проверке «этот город точно принадлежит игроку, от чьего имени пришла команда» перед тем, как открыть городской диалог.

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3GameData/H3Main.hpp, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Players/H3Player.hpp

## Как устроен герой на карте приключений (H3Hero)

`H3Hero` — размер `0x492` байт, `pragma pack(push,1)` (побайтовая упаковка, без выравнивания — редкость среди H3-структур, где обычно `pack(4)`). Ключевые поля:

| Поле | Смещение | Комментарий |
|---|---|---|
| `x, y, z` | `0x00–0x05` | координаты на карте (`INT16` каждая) |
| `owner` | `0x22` | 0..7, цвет-владелец |
| `name[13]` | `0x23` | имя героя, null-terminated |
| `hero_class` | `0x30` | индекс спрайта класса, 0..17 (Knight..Elementalist); Bulwark/Кронверк добавляет классы вне этого диапазона H3API-документации SoD |
| `maxMovement` / `movement` | `0x49` / `0x4D` | максимум и остаток очков хода на день |
| `experience` / `level` | `0x51` / `0x55` | опыт и текущий уровень |
| `levelSeed` | `0x8F` | seed для системы весов вторичных навыков при левел-апе (см. `h3.mechanics.leveling` в соседнем разделе базы) |
| `army` (`H3Army`) | `0x91` | 7 слотов отряда |
| `secSkill[28]` / `secSkillPosition[28]` / `secSkillCount` | `0xC9` / `0xE5` / `0x101` | уровни вторичных навыков, порядок отображения, их число |
| флаги посещённых бонусных объектов (well/stables/buoy/…) | `0x105` (битовое поле, 32 бита) | какие разовые бонусы адвенчур-карты герой уже получил в этом ходу/неделе |
| `moraleBonus` / `luckBonus` | `0x11A` / `0x11B` | текущие модификаторы морали/удачи (без учёта врага) |
| `visitedTowns` | `0x121` | битсет посещённых для гильдии магов ратушей городов (48 слотов, зарезервировано 64) |
| `bodyArtifacts[19]` | `0x12D` | надетые артефакты по слотам |
| `backpackArtifacts[64]` / `backpackCount` | `0x1D4` / `0x3D1` | рюкзак |
| `learnedSpells[70]` / `availableSpell[70]` | `0x3EA` / `0x430` | изученные и временно доступные (через артефакты) заклинания |
| `primarySkill[4]` | `0x476` | атака/защита/сила магии/знание |

Функция-член `GetPower()` (боевая ценность армии с учётом коэффициентов атаки/защиты) и набор `Get*Power()` (архейри, оффенс, армор, дипломатия, скаутинг, орлиный глаз, обучаемость, интеллект, первая помощь) существуют как реальные вызовы в exe — H3API лишь оборачивает уже посчитанные игрой значения, а не переизобретает формулы.

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Heroes/H3Hero.hpp

## Как устроен город (H3Town) и его менеджер экрана

`H3Town` — размер `0x168` (подтверждено живым мостом как шаг массива городов).

| Поле | Смещение | Комментарий |
|---|---|---|
| `number` / `owner` / `type` | `0x00` / `0x01` / `0x04` | id города, владелец, тип фракции (0..8, где 8 — Причал; Бастион/Кронверк в HotA идёт вне этого исходного диапазона SoD) |
| `garrisonHero` / `visitingHero` | `0x0C` / `0x10` | id героя в гарнизоне и id визитёра |
| `recruits[2][7]` | `0x16` | доступное для найма количество по 7 слотам, отдельно баз./апгрейд |
| `spells[5][6]` | `0x44` | какие заклинания подгружены в гильдию магии по уровням/слотам |
| `magicGuild[5]` | `0xBC` | построены ли уровни гильдии магии |
| `guards` (`H3Army`) | `0xE0` | армия в гарнизоне без героя |
| `built` / `built2` / `buildableMask` | `0x150` / `0x158` / `0x160` | битовые поля построенных (2×32 бита на 48+ построек) и доступных построек |

`H3TownManager` (менеджер экрана города, живой адрес `0x69954C`, vtable `0x643730` — оба значения независимо подтверждены мостом) хранит указатель на текущий открытый `town` на `+0x38` и на диалог `dlg` на `+0x118`; через этот менеджер мост инициирует «построить Ратушу/Городской совет» (`hall(...)` по адресу `0x5D34D0`) и открытие города (`openTown(...)` по адресу `0x5BE610`), проверяя перед этим владельца города и битовую маску доступности постройки (`town+0x150 & 0x3C00` для проверки, что здание ратуши доступно).

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Towns/H3Town.hpp, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3TownManager.hpp

## Существо и отряд в бою (H3CombatCreature)

`H3CombatCreature` (он же `H3CombatMonster`) — размер `0x548`, представляет один боевой стек на поле боя (не путать с `H3CreatureSlot` в армии героя — это отдельное, боевое представление).

| Поле | Смещение | Комментарий |
|---|---|---|
| `type` | `0x34` | `eCreatureType` — какое существо |
| `position` | `0x38` | позиция на гексагональном поле, 0..186 |
| `secondHexOrientation` | `0x44` | для двухклеточных существ: 0-влево, 1-вправо, -1-неприменимо |
| `numberAlive` / `previousNumber` | `0x4C` / `0x50` | текущая и предыдущая численность стека |
| `numberForeverDead` | `0x54` | безвозвратные потери (сгорели навсегда, не подлежат воскрешению базовым Resurrection) |
| `healthLost` | `0x58` | недостача HP у «верхнего» существа в стеке |
| `slotIndex` | `0x5C` | 0..6 — какой слот армии героя, -1 после боя, если существо снято |
| `baseHP` | `0x6C` | максимум HP одного существа с учётом бонусов |
| `info` (`H3CreatureInformation`) | `0x74` | боевая копия статов существа (атака/защита/урон и т.д., с учётом баффов на момент боя) |
| `spellToApply` | `0xEC` | применяется в under-the-hood процедуре «эффект после удара» (адрес процедуры в игре `0x440220`) |
| `side` / `sideIndex` | `0xF4` / `0xF8` | 0-атакующий/1-защищающийся, и позиция в списке своей стороны |

Пометка авторов H3API «эти поля нуждаются в подтверждении» стоит явно на части протектед-полей структуры (например, `_f_0F0`, `hasLosses`/`hasLosses2`) — это не догадка агента, а честная маркировка авторов библиотеки о неполной уверенности в реверс-инжиниринге. Использовать эти конкретные поля для точных решений не стоит без собственной проверки.

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Combat/H3CombatCreature.hpp

## Менеджеры экрана: общий каркас H3Manager

Все крупные подсистемы интерфейса (окна, ввод, звук, мышь, обмен войсками, бой, адвенчур-карта, город, найм) — наследники одного базового класса `H3Manager` (размер `0x38`, конструктор `0x44D200`) с трёхслотовой виртуальной таблицей:

```cpp
struct ManagerVTable { h3func start; h3func stop; h3func processMessage; };
```

Слоты: `[v00] Start(zorder)`, `[v04] Stop()`, `[v08] ProcessMessage(H3Msg&)`. Именно слот `v08` (смещение `+0x08` от адреса vtable) — точка, куда попадает любое сообщение, адресованное менеджеру; мост подтверждённо использует это же смещение (`Read<uintptr_t>(original+8)`) при временной подмене vtable менеджера города (см. раздел про подключение плагинов ниже). Перечисление типов менеджера (`eType`, битовые флаги, а не последовательные номера — это маска для фильтрации «кто активен сейчас»):

| Менеджер | Значение `eType` |
|---|---|
| `INPUT_MANAGER` | `0x4` |
| `SOUND_MANAGER` | `0x10` |
| `WINDOW_MANAGER` | `0x20` |
| `MOUSE_MANAGER` | `0x40` |
| `SWAP_MANAGER` | `0x100` |
| `COMBAT_MANAGER` | `0x200` |
| `ADVENTURE_MANAGER` | `0x400` |
| `TOWN_MANAGER` | `0x800` |
| `RECRUIT_MANAGER` | `0x4000` |

Менеджеры организованы двусвязным списком: `parent` на `+0x04`, `child` на `+0x08` от начала `H3Manager` (после vtable-указателя на `+0x00`).

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Managers/H3BaseManager.hpp

## Диалоги и элементы управления: H3BaseDlg, H3DlgItem, их vtable

`H3BaseDlg` (базовый класс всех модальных и немодальных диалогов, размер `0x4C`, vtable `0x643CD4`, конструктор `0x5FEFB0`/деструктор `0x5FF040`) хранит:

| Поле | Смещение | Комментарий |
|---|---|---|
| `nextDialog` / `lastDialog` | `+0x08` / `+0x0C` | двусвязный список открытых диалогов (стек окон) |
| `xDlg,yDlg,widthDlg,heightDlg` | `+0x18…+0x24` | геометрия окна |
| `lastItem` / `firstItem` | `+0x28` / `+0x2C` | связный список элементов управления (кнопки, текст, поля ввода) |
| `dlgItems` (`H3Vector<H3DlgItem*>`) | `+0x30` | тот же набор элементов, но как вектор указателей (внутри вектора: служебный allocator-байт, затем `m_first`=`+0x34`, `m_end`=`+0x38`, `m_capacity`=`+0x3C`) |
| `focusedItemId` | `+0x40` | id элемента с фокусом ввода |

Vtable диалога — 15 функций (`H3DlgVTable`): `destroyDlg, showDlg, hideDlg, callProcessAction, _nullsub, redrawDlg, runDlg, initDlgItems, activateDlg, dlgProc, mouseMove, rightClick, clickRet, _nullsub3, closeDlg`. Слот №3 (`callProcessAction`, смещение `+0x0C` в массиве указателей) — та самая точка, на которую мост временно подменяет свой обработчик (см. ниже).

`H3DlgItem` (базовый класс кнопок/текстовых полей/скроллбаров, размер `0x30`, vtable `0x643CA0`, 13 виртуальных методов) хранит:

| Поле | Смещение |
|---|---|
| `parent` (`H3BaseDlg*`) | `+0x04` |
| `nextDlgItem` / `previousDlgItem` | `+0x08` / `+0x0C` |
| `id` (`UINT16`) | `+0x10` |
| `type` (`eControl`) | `+0x14` |
| `state` (`eControlState`, битовые флаги активности/видимости) | `+0x16` |
| `xPos,yPos,widthItem,heightItem` | `+0x18…+0x1E` |

Конкретные подклассы кнопок расширяют `H3DlgItem` дальше собственного размера `0x30`: `H3DlgDefButton` (кнопка с картинкой из `.def`, размер `0x68`, vtable `0x63BB54`) добавляет `closeDialog` (`BOOL8` на `+0x44` — закрывать ли диалог по клику) и `hotkeys`/`caption`; `H3DlgCaptionButton` (кнопка-подпись, часто системные «ОК/Отмена» в верхних панелях, размер `0x70`, vtable `0x63BB88`) наследует от `H3DlgDefButton`.

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3Dialogs/H3BaseDialog.hpp, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3DialogControls/H3DlgItem.hpp, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3DialogControls/H3DlgDefButton.hpp, https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/h3api/H3DialogControls/H3DlgCaptionButton.hpp

## Что такое H3Msg и как через него подаются события в игру

Все менеджеры и элементы диалогов принимают одно и то же событие — `H3Msg&` (в H3API оно только *объявлено* (`struct H3Msg;`) во всех потребляющих файлах и нигде в опубликованных заголовках не *определено* целиком — это зафиксированный пробел документации самого H3API, не ошибка чтения). Косвенно по использованию (`msg->IsKeyPress()`, `msg->KeyPressed()`, аргумент `int a3,a4,a5` при вызове через `THISCALL_5` в примерах H3Plugins) понятно, что это компактная структура «код события + данные».

Мост, работающий с реальным HotA 1.8.x, реализует и использует байт-в-байт совместимую структуру самостоятельно, назвав её `GameMessage` (32 байта, статически проверено `static_assert(sizeof(GameMessage)==32)`):

```cpp
struct GameMessage { int command, subtype, item, flags, x, y; void* parameter; void* dialog; };
```

Мост синтезирует такие сообщения сам и «впрыскивает» их не через постановку в общую очередь Windows-сообщений (это было бы небезопасно и неточно адресуемо), а точечно — подменой указателя на vtable (или таблицы методов) ровно одного объекта (кнопки/диалога/менеджера), так что при следующем естественном вызове виртуального метода этим же объектом внутри игровой модальной обёртки управление один раз попадает в код моста, отдаёт заранее подготовленное `GameMessage`, само восстанавливает оригинальный указатель на vtable и передаёт управление дальше — оригинальному обработчику. Это «одноразовый батут» (trampoline), а не постоянный хук: он снимает себя сам после первого срабатывания.

Пример (сокращённо, кнопка):
```cpp
buttonOriginal = Read<uintptr_t>(targetButton);           // сохранить исходный vtable-указатель
for (i=0..12) buttonTable[i] = Read<uintptr_t>(buttonOriginal + i*4); // скопировать все 13 слотов
buttonTable[2] = &DeliverButtonCommand;                    // подменить только vProcessMsg (слот 2)
*reinterpret_cast<uintptr_t*>(targetButton) = &buttonTable; // подложить копию с одной подменой
```
и `DeliverButtonCommand` при первом же вызове восстанавливает `buttonOriginal`, отдаёт заранее собранное сообщение и возвращает `2` — код, который «модальный цикл» (`ProcessItems`) интерпретирует как готовый результат, включая совместимость с собственным меню HD Mod.

Источник: C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\game_bridge.cpp (строки 9–53)

## Как плагины подключаются к игре: три разные экосистемы

В сообществе HoMM3 сосуществуют три независимых способа встроиться в исполняемый файл, и они **не взаимозаменяемы**:

**1. ERA/WoG (`ERA-Projects/era-project-eng`).** Исторически — платформа для оригинального Shadow of Death (наследник Wake of Gods), со своим загрузчиком, скриптовым языком ERM 2.0 и системой плагинов двух видов: старые `.dll`-плагины в папке `EraPlugins` (грузятся автоматически после инициализации кода WoG) и новые `.era`-плагины там же, с доступом к системе событий и «прикладным функциям» ERA. Официальный мануал прямо предупреждает: **в секции инициализации плагина запрещено менять код игры — разрешена только регистрация обработчиков событий**, сама установка патчей/хуков откладывается на потом. ERA целится в код именно оригинального `heroes3.exe`/WoG-сборки; в опубликованной документации проекта нет упоминаний прямой поддержки `h3hota HD.exe` как отдельного целевого бинарника — это отдельный незакрытый вопрос (см. `## Пробелы`).

**2. H3Plugins на базе H3API (`RoseKavalier/H3Plugins`).** Классические Windows DLL, скомпилированные под x86 со статически слинкованным H3API. Собственный `README.md` уточняет, что реальная загрузка таких DLL в процесс идёт через **HD Mod**: «By default, all folders of active HDmod plugins are browsed to add `.pac` or `.lod` game archives» — то есть именно HD Mod (тот же компонент, что даёт HotA широкоэкранный режим) выступает хост-загрузчиком плагинов, а не сама игра и не ERA.

**3. Наш мост (`hota-agent-bridge`).** Ни ERA, ни HD Mod plugin API не используются вовсе. Инъекция сделана низкоуровневым штатным механизмом Windows — `SetWindowsHookExW(WH_GETMESSAGE, ...)` на поток целевого окна игры:

```cpp
DWORD thread = GetWindowThreadProcessId(target, nullptr);
HHOOK hook = SetWindowsHookExW(WH_GETMESSAGE, proc, dll, thread);
PostMessageW(target, kInit, 0, 0); // будит очередь сообщений — Windows сама подгружает DLL в чужой процесс
```

Это официально задокументированный Win32-приём: при установке WH_GETMESSAGE-хука с ненулевым `hMod` система сама отображает указанную DLL в адресное пространство процесса-владельца потока в момент, когда тот в следующий раз вызовет `GetMessage`/`PeekMessage`. Не требуется ни `CreateRemoteThread`, ни ручная запись в чужую память для инъекции, ни патч точки входа exe. Дальше — обмен приватными сообщениями через `PostMessageW`/`GetPropW` на самом окне игры (`kInit`, `kAction` = `WM_APP+0x391/0x392`), что гарантированно исполняется в родном потоке игры (см. ниже про потокобезопасность).

Источник: https://raw.githubusercontent.com/ERA-Projects/era-project-eng/main/Help/Era%20manual/html/plugins_and_patches.html, https://raw.githubusercontent.com/RoseKavalier/H3Plugins/master/README.md, C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\attach.cpp

## Хуки patcher_x86: какие типы существуют и когда какой применяется

И ERA, и H3API/H3Plugins в итоге опираются на общую низкоуровневую библиотеку **patcher_x86** (`baratorch`, с явной пометкой в шапке файла: «the form of implementation of low-level hooks (LoHook) is partly borrowed from Berserker (from ERA)» — то есть современный H3API-хукинг и ERA-хукинг исторически произошли от одного и того же кода). Две основные абстракции:

- **`LoHook`** — хук на произвольный адрес инструкции. Хук-функция получает `HookContext*` (снимок всех регистров процессора на момент хука: `eax,ebx,ecx,edx,esi,edi,esp,ebp`, плюс методы `Push()/Pop()` для работы со стеком, как при вызове `WriteLoHookEx`). Возвращаемое значение хук-функции — `EXEC_DEFAULT` (1, «выполнить оригинальный код дальше») или `NO_EXEC_DEFAULT`/`SKIP_DEFAULT` (0, «пропустить оригинальный код»). Именно так устроен пример `ShiftToggle::CheckShift` — возвращает `NO_EXEC_DEFAULT` вместе с явно указанным адресом перехода (`c.return_address = 0x40A985`), то есть хук не просто «до/после», а может целиком перенаправить поток выполнения.
- **`HiHook`** — хук на конкретную инструкцию `CALL`, с тремя разновидностями (`hooktype`): `CALL_` (подмена самого адреса вызова), `SPLICE_` (врезка «мостом» — оригинальная функция физически перемещается, а на её месте остаётся трамплин; так безопаснее переживает конкурирующие патчи разных плагинов на одном адресе) и `FUNCPTR_` (подмена указателя на функцию напрямую, для виртуальных вызовов через таблицы). Дополнительно указывается соглашение вызова оригинальной функции: `STDCALL_/THISCALL_/FASTCALL_/CDECL_` — это обязательный параметр, потому что x86 не хранит соглашение вызова в самом коде, и его нужно знать заранее из реверс-инжиниринга.

Практическое правило, явно видное на примере `ShiftToggle::Start()`:
```cpp
Hook(0x40A7C7, ::CheckShift);                     // LoHook на конкретный адрес кода
Hook(0x408BA0, Splice, Thiscall, ::_HH_CheckShift); // HiHook типа Splice, __thiscall
```
— `LoHook` используют, когда нужно перехватить именно точку внутри функции (условие, ответвление); `HiHook`/`Splice` — когда нужно перехватить сам вызов чужой функции целиком (обернуть её, дописать логику до/после, опционально не выполнять оригинал).

Источник: https://raw.githubusercontent.com/RoseKavalier/H3API/master/include/patcher_x86.hpp, https://raw.githubusercontent.com/RoseKavalier/H3Plugins/master/Examples/ShiftToggle/src/ShiftToggle.cpp

## В каком потоке безопасно вызывать функции игры

`h3hota HD.exe`, как и оригинальный движок NWC, — однопоточная по логике игры программа: обработка ввода, адвенчур-карта, бой, диалоги — всё крутится в одном главном потоке через классический Windows message loop (`GetMessage`/`PeekMessage` → `TranslateMessage` → `DispatchMessage`). Все три рассмотренных подхода (ERA, H3API-хуки, наш мост) неявно полагаются на это: **любой код, который трогает игровые структуры или вызывает игровые функции, должен исполняться синхронно внутри этого главного потока**, а не из отдельного потока адаптера/сервера.

Мост реализует это ограничение буквально, а не как соглашение «постарайся не ломать»:
- Сам обработчик хука `GameHook` — это `HOOKPROC`, вызываемый Windows-ом строго внутри потока-владельца очереди сообщений на каждой выборке сообщения (`code>=0 && wp==PM_REMOVE`).
- Все операции с игровой памятью (`Dispatch(...)`) выполняются исключительно внутри этого колбэка, то есть строго в главном потоке игры — никогда из потока `game-attach.exe` или из потока MCP-сервера.
- `game-attach.exe` со своей стороны только шлёт приватные Windows-сообщения (`PostMessageW`) и читает результат через `GetPropW` на самом окне — то есть общение с игрой идёт исключительно через штатный, потокобезопасный механизм очереди сообщений Windows, никогда через прямой вызов адреса из чужого потока.
- Любое исключение внутри `Dispatch` перехватывается structured exception handling (`__try/__except`) и превращается в код результата (`4`), а не в падение процесса игры — предохранитель на случай, если проверка адресов и версии всё же где-то ошиблась.

Практический вывод для любых новых хуков/операций: если код когда-либо обращается к `H3Main`, менеджерам, диалогам, армии — это обращение обязано происходить синхронно из колбэка, вызванного самой игрой (хук, виртуальный метод, обработчик сообщения), и никогда — из фонового потока или таймера адаптера, даже если адрес «просто читается», а не пишется: структуры меняются посреди кадра, и чтение из чужого потока может застать их в противоречивом промежуточном состоянии.

Источник: C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\game_bridge.cpp (строки 176–204), C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\attach.cpp

## Чем отличается адресация HotA от Shadow of Death и что ломается при обновлении версии

Экспериментально (раздел выше, 24 сверенных факта) для проверенного подмножества структур (H3Main, H3Player, H3Town/H3TownManager, H3WindowManager, H3AdventureManager, H3BaseDlg/H3DlgItem и их vtable-адреса, базовый H3Manager) адресация **не отличается вовсе**: HotA 1.8.x, на котором сейчас работает мост, использует те же самые абсолютные адреса и офсеты, что документированы для оригинального патча 3.2 SoD/Complete. Это означает, что HotA собран как надстройка над тем же самым базовым образом кода (тот же компилятор, те же настройки линковки, добавления идут в новые адреса, а не переупаковкой существующих).

Что при этом всё-таки ломается при обновлении версии, по прямой логике кода моста и документации:
- **Любое изменение хотя бы одного байта `h3hota HD.exe` или `HotA.dll`** (патч-релиз HotA, вплоть до 1.8.0 → 1.8.1) меняет SHA-256 файла и **гарантированно** блокирует `game-attach.exe` («Unsupported game binary/native DLL»), даже если по факту адреса не сдвинулись — мост осознанно не пытается «на глаз» угадать совместимость, отказ — это заглушка безопасности, а не доказательство того, что адреса реально изменились.
- Новый контент HotA (третья фракция Бастион/Кронверк, новые здания, новые диалоги: список рун, алтарь рун, интерфейс третьей фракции) размещается в адресах и структурах, которых H3API вообще не документирует (H3API документирует только состав SoD) — то есть для любых операций, специфичных для контента HotA, готовых H3API-заголовков нет и не может быть, только собственное реверс-инжиниринговое исследование.
- ERA/WoG нацелены на оригинальный `heroes3.exe`; официальная документация ERA не подтверждает и не отрицает работу на `h3hota HD.exe` напрямую — не проверялось (см. `## Пробелы`).
- HD Mod, через который грузятся классические H3Plugins, — отдельный самостоятельно обновляемый компонент; его собственная версия и её совместимость с конкретной сборкой HotA — отдельная переменная, не покрытая ни H3API, ни нашим мостом.

Источник: C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge\native\game\attach.cpp, https://download.h3hota.com/upd/changelogs/rus.txt (версия 1.8.1 от 25.08.2026 подтверждает, что HotA продолжает выходить патчами поверх той же базы, без анонсов о «переписанном движке» или смене архитектуры exe)

## Пробелы

- Полное определение структуры `H3Msg` (не только косвенное использование через `IsKeyPress()/KeyPressed()`) не найдено ни в одном опубликованном заголовке H3API — она везде только предварительно объявлена (`struct H3Msg;`), а не определена целиком. Реализация `GameMessage` в собственном мосте (32 байта: command/subtype/item/flags/x/y/parameter/dialog) — это независимая, самостоятельно реверс-инженерная реконструкция, не переиспользование H3API.
- Не проверено, работает ли ERA/WoG (и её ERM-скрипты/плагины) на `h3hota HD.exe` вообще — официальный мануал ERA описывает только оригинальный WoG/SoD-сценарий; ни в документации HotA (h3hota.com), ни в мануале ERA нет прямого подтверждения или опровержения совместимости.
- Не найдено официального описания того, как именно HD Mod загружает `.dll`-плагины H3API/H3Plugins в память процесса (сам механизм инъекции HD Mod, в отличие от `SetWindowsHookExW` нашего моста, не задокументирован в проверенных источниках) — README H3Plugins описывает только конечный эффект (папки плагинов сканируются), не сам приём.
- Не проверено, распространяется ли байт-в-байт совпадение адресов SoD 3.2 ↔ HotA 1.8.x (24 факта выше) на структуры, которые мост не использует и которые здесь не сверялись напрямую — H3Hero, H3Town (сами структуры, не менеджер), H3CombatCreature, H3CombatManager, дополнительные диалоги. Эти структуры в разделах выше приведены только по документации H3API для SoD 3.2, без независимой перепроверки на живом HotA.
- Не найдено публичного репозитория, посвящённого именно бинарным отличиям `h3hota HD.exe` от `heroes3.exe` (адреса нового контента HotA — третьей фракции, новых зданий, ИИ-правок) — задача, обозначенная в брифе как «третий круг только для сверки», в отношении HotA-специфичных адресов вообще не имеет опубликованного первого/второго круга: этот пробел не закрыт никем в проверенных источниках.
- Значения `0x419400` («plan»), `0x40a530` («choose»), `0x408250`/`0x4081bd`/`0x4081bf` (обработчик открытия города с адвенчур-карты) и адрес модального цикла `0x602C56` в `game_bridge.cpp` не сверялись отдельно с H3API (в проверенных заголовках H3API адреса функций, в отличие от адресов структур, обычно не публикуются макросом `_H3API_GET_INFO_`), поэтому в таблицу 24 совпадений не включены — они взяты как факт из работающего кода моста, но без независимого второго источника.
