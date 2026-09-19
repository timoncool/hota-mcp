# HotA MCP — хендофф DeepSeek, 19 сентября 2026

## Поручение и честный статус

Пользователь остановил текущего агента из-за лимита и поручил продолжение DeepSeek. Это отменяет прежнее требование пройти карту именно текущим агентом до передачи. Проект НЕ завершён; карта НЕ пройдена. Проверены первая победа, магия, строительство, найм и сохранение. Работай автономно по TZ и задачам, сохраняй прогресс и пушь в существующий приватный репозиторий. Не останавливайся после каждой мелкой правки в ожидании «продолжай».

## Пути

- Repo: `C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\outputs\hota-agent-bridge`
- GitHub private: https://github.com/timoncool/hota-mcp.git ; branch `main`.
- ТЗ: `<repo>\TZ.md`; задачи: `<repo>\docs\TASKS.md`; история: `<repo>\docs\STATE.md`. Этот хендофф новее исторических записей STATE.
- Skill: `<repo>\skills\hota-player\SKILL.md` — частичные проверенные flows, не полное покрытие.
- Справка: `<repo>\docs\knowledge\core`, `hota`, `playbooks`, `sources`; исследование MCP упаковки: `docs\knowledge\mcp-packaging-research.md`.
- Игра: `G:\HoMM 3 Complete`; EXE `h3hota HD.exe`; launcher `HD_Launcher.exe`.
- Scratch: `C:\Users\user\Documents\Codex\2026-09-19\new-chat-2\work`.
- H3API: `<scratch>\H3API`, commit `92255ab18da784a5842ecc2b8bc0ce00e19a0c56`, структуры в `include\h3api`, сводка `single_header\H3API.hpp`.
- Сервер: `src\HotaMcp`; native: `native\game`; вкладка лаунчера: `native\launcher`.
- Тесты: `tests\HotaMcp.Smoke`; atlas recipe: `tests\flows\random-setup.json`; инструкция: `docs\UI-ATLAS.md`.
- Runtime: `%LOCALAPPDATA%\HotaMcp`. `connection.token` — секрет, не печатать/коммитить.
- Последняя сессия: `C:\Users\user\AppData\Local\HotaMcp\sessions\19a912a3dddd40ad995d04f0c82a19db`.
- Scratch доказательства в gitignored `<repo>\build`: `town-disasm.txt`, `adventure-disasm.txt`, `load-dialog-diagnostic.json`, `load-browser-diagnostic.json`, `hire-brissa.json`, `recruit-water-six.json`.

## Ограничения пользователя

1. Основное наблюдение — структурированные игровые данные. Никакого OCR/скриншотов в игровом цикле. Debug framebuffer через наш MCP допустим разработчику и по запросу пользователя.
2. Никакого CUA, фокуса окна или перехвата физической мыши/клавиатуры. Все действия через наш MCP. Внутри адаптера допустимы адресные WM события/штатные обработчики; координаты вычисляет адаптер по UI/hitmap, не агент по картинке.
3. Честность: свои герои/города/армии, видимые объекты, обычные popup сведения. Не раскрывать fog-of-war, скрытые армии/книги/артефакты противника, RNG, планы AI. Не писать в ресурсы/героев/мир. UI-контекст штатной команды разрешён. Правила и тактика — отдельная справка, не автоматические подсказки в observe.
4. Python SDK не используется. Стек .NET8/C# + C++ x86. Старый ignored `.venv` может оставаться; не возрождать Python стек.
5. Встраивание в существующий HD Launcher, MCP таб, жизненный цикл сервера. Итоговый установщик поверх GOG Complete+HotA+HD; нового лаунчера не делать.
6. Итог: любой harness/API, агент/человек, соперники/союзники, несколько моделей, hotseat, передача управления, исследовать LAN. Сейчас это не полное покрытие.
7. `uncertain` → observe/journal; не повторять мутацию с новым operationId вслепую.

## Живое состояние

PID перепроверять, это снимок момента:

- Game `45500`, launcher `47720`, MCP `43516`.
- Сервер скрыто запущен: `<repo>\build\session-load-enter\HotaMcp.exe --launcher-pid 47720`.
- HTTP `http://127.0.0.1:18773`; stdio proxy того же EXE с `--stdio`.
- Native v29 закреплён в игре, Ready property `HotAMcp.GameBridge.v1`.
- Игра жива, `adventure`, 800×600, 32bit windowed. Не менять рендер без причины.
- Карта «Превосходство в воздухе»,72×72, день2/неделя1/месяц1.
- Выбран `137 Брисса`,66,37,0; мана30; движение1630;18 пикси(type118),3 воздушных(type112),4 водных(type115).
- Второй свой герой `139 Лабета`: последняя проверка67,43,0; движение409; мана26;2 водных. Перед действием выбрать/перечитать.
- Ресурсы `[14,10,10,4,4,4,7000]` в порядке дерево/ртуть/руда/сера/кристаллы/самоцветы/золото.
- Город `5 Уоззар`, Conflux/type8. Рынок построен сегодня. Гарнизон6 водных; запас найма20/6/0/0/0/0/0 неулучшенных.
- visitingHero в первом adventure чтении был -1, после открытия options137. Не считать поле town+10 стабильным вне контекста без проверки.

### Сохранение — беречь

`G:\HoMM 3 Complete\Games\Превосходство в воздухе\111ы.GM1`,119077 байт,19.09.2026 21:55:29 MSK.

Штатное сообщение «111ы удалось сохранить» и файл подтверждены. Содержит бой/рынок/найм. Имя получилось из111 + ошибочного S в активном поле (русскаяы). Старый111.GM1 не перезаписан. Загрузка этого файла НЕ проверена. AUTOSAVE.GM1 и старый111.GM1 более ранние; BATTLE.GM1 перед боем.

## Что проверено

- Launcher таб запускает/останавливает MCP; start_game через Play; ожидание игры/lifecycle, frontend меню без игрового контекста.
- Новая игра, часть random настроек, старт, сюжетные сообщения, конец дня и autosave.
- Сбор800 золота и9 дерева: ресурсы, исчезновение объекта, расход движения.
- Бой против24 нимф+23 океанид: wait/defend/move/melee, индексированный игровой log.
- Книга → Каменная Кожа на водных: мана30→26, защита10→21, новая запись игрового лога. Победа188 опыта; результат принят → adventure. Потери21 пикси,4 воздушных,3 водных; не выдавать за хорошую тактику.
- Город через sidebar portrait, повторные вход/выход без завершения процесса; рынок5 дерева/500 золота; Брисса2500;6 водных1800.
- Сохранение options S → button186/scnrsav.def → сообщение.
- Перед передачей build smoke:0errors/0warnings; официальный MCP client `--capture` PASS,13 tools, adventure/hero137, PNG800×600 metadata only, revision unchanged. Это НЕ тест полной загрузки/карты.

## Свежая незаконченная работа: загрузка

Последние изменения: TownView.GarrisonTypes/Counts, GarrisonHero/VisitingHero, Recruitable; экспериментальный `game:load`.

В options `game:load` отправляет L/scan0x26. Игра показывает вопрос «Вы уверены… несохранённые игры будут потеряны?». Два опыта подтверждения вернули adventure БЕЗ load browser: сначала native29, затем Enter через MCP. Причина неизвестна, загрузки не было. Вторая попытка после наблюдения вернувшейся карты, не слепой повтор pending.

В текущем коде message:accept/confirm используют Enter; decline Escape. Это последняя экспериментальная замена, полный регресс сообщений после неё НЕ пройден. При сравнении смотреть последний проверенный1931c87.

Возможное направление исследования: открытие options ещё native1 напрямую вызывает обработчик из хука; возвращаемое значение/верхний manager loop могло теряться. Это гипотеза, не диагноз. Нужны нормальная доставка событий и проверка цепочек переходов.

`game:load` ожидаетload_game, классификацияload_game ещё не реализована; на вопросеuncertain. `message:confirm` считает возвратadventure успехом, что НЕ доказывает загрузку. `save:confirm` теперь ожидаетmessage вместоadventure; нужно отличить успех/overwrite/error, не считать любойmessage записью файла.

## Архитектура и подключение

GameSession lifecycle/attach; Bridge gate/revision/operationId/journal; GameReader owner/UI; TownReader/CombatReader/MapReader/ScenarioReader предметные данные; native game_bridge ограниченные команды; WindowsGame ReadProcessMemory/background messages. Произвольные адреса/запись памяти не публиковать игроку.

Tools: game_status,nearby_targets,act,observe,click_ui,read_journal,debug_capture,launcher_graphics,inspect_target,move_to,start_game,plan,debug_snapshot. inspect_target пока объект/маршрут, не карточки существ. Capability strings устарели: исправлять по фактической реализации.

Текущая конфигурация harness:
```json
{"mcpServers":{"hota":{"command":"C:\\Users\\user\\Documents\\Codex\\2026-09-19\\new-chat-2\\outputs\\hota-agent-bridge\\build\\session-load-enter\\HotaMcp.exe","args":["--stdio"]}}}
```
Это dev путь. Proxy требует живойHTTP/token; полный bootstrap не реализован.

HTTP для разработки, тот же backend MCP:
```powershell
$a=@{Authorization='Bearer '+[IO.File]::ReadAllText((Join-Path $env:LOCALAPPDATA 'HotaMcp\connection.token')).Trim()}
$o=Invoke-RestMethod http://127.0.0.1:18773/bridge/observe -Method Post -Headers $a -ContentType application/json -Body '{}'
# Выбрать action из свежего $o.actions; это пример, не выполнять автоматически:
$body=@{operationId=[guid]::NewGuid().ToString('N');revision=$o.revision;element='town:open:5'}|ConvertTo-Json
$r=Invoke-RestMethod http://127.0.0.1:18773/bridge/click -Method Post -Headers $a -ContentType application/json -Body $body
```
Другие endpoints: `/bridge/nearby`, `/bridge/move` сtargetId, `/bridge/debug-snapshot`.

### Сборка и смена dev сервера

.NET8 SDK8.0.404, official MCP SDK2.2.0. Native x86 MSVC: `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars32.bat`.
```powershell
# cwd repo
dotnet build tests/HotaMcp.Smoke -v quiet
dotnet publish src/HotaMcp -c Release -r win-x64 --self-contained true -o build/session-NEXT -v quiet
```
Не перезаписывать работающий EXE. Остановка только MCP через pipe (PID лаунчера перепроверить):
```powershell
$p=[IO.Pipes.NamedPipeClientStream]::new('.','hota-mcp-control-47720',[IO.Pipes.PipeDirection]::InOut)
$p.Connect(1000)
$b=[Text.Encoding]::UTF8.GetBytes('stop'); $p.Write($b,0,$b.Length)
$buf=[byte[]]::new(512); $null=$p.Read($buf,0,512); $p.Dispose()
Start-Process (Join-Path $pwd 'build\session-NEXT\HotaMcp.exe') -ArgumentList '--launcher-pid','47720' -WindowStyle Hidden
```
После готовности перечитатьtoken новым вызовом: старыйtoken даёт401. Не повторять мутацию после неясного ответа.

`native/game/build.cmd` →build/v29; csproj копируетv29. Launcher native build/v4 содержит старую serverсборку: финальная синхронизация ещё нужна. Сборка DLL не обновляет уже загруженный модуль; не менять hooks при pending modal.

Read-only developer diagnostic:
```powershell
$lines=@(& build/session-load-enter/HotaMcp.exe --diagnostic)
$d=$lines[0]|ConvertFrom-Json
```
Нужно `@(...)`: одна строка иначе индексируется какchar. Адреса diagnostic не отдавать игроку. Не применять --hover/--center в обход MCP.

Smoke без изменения партии:
```powershell
dotnet run --project tests/HotaMcp.Smoke --no-build -- "$pwd" --state-dir "$env:LOCALAPPDATA\HotaMcp" --capture
```
--menus/--atlas запускать в подходящем меню, не поверх партии.

## Приоритеты

1. Проверить живое состояние. Беречь111ы. Исправить loadflow/цепочки результатов, реально загрузить и сверить7000gold/market/2героев/6waterгарнизона.
2. Список/выбор собственных героев, город/гарнизон/передача армии, количество найма, надёжные postconditions покупок.
3. Штатные inspect карточки мобов/заклинаний/вражеского героя, только доступные человеку сведения. Сейчас не готовы.
4. Надёжная доставка событий и подтверждение завершения, устранение ложныхcompleted/uncertain и опасных directmodal paths.
5. Укрепить армию, пройти полную карту, пополнять skill/atlas доказанными переходами.
6. Проверить черновики knowledge, встроить offline resources/search/read в MCP/publish. Не инъектировать тактику в observe.
7. Полный старт/настройки/сохранение/recovery; hotseat изоляция; bootstrap; актуальный launcher package/installer; матрица фракций и приёмка DeepSeek.

GM режим был только исследовательским интересом: HotA events/попапы/награды/битвы возможная основа; dynamic world spawn/нейтралы не проверены. Не смешивать с честным режимом и не менять текущую партию какGM.
