# Как помочь HotA MCP

**[Русский](#русский)** · **[English](#english)**

## Русский

Сейчас больше всего помогут три вещи: играть и сообщать о проблемах, проверять игру на других языках и переводить.

### Играть и сообщать

1. Поставьте [последний выпуск](https://github.com/timoncool/hota-mcp/releases/latest), подключите MCP-клиент, дайте агенту поиграть.
2. Когда мост читает что-то неверно, отказывает в законном ходе или застревает, вызовите `debug_snapshot` на этом экране (или попросите агента). Он сохранит кадр окна игры и наблюдение моста в один и тот же момент.
3. Откройте [issue](https://github.com/timoncool/hota-mcp/issues/new/choose) и приложите:
   - издание и язык игры (например, GOG Complete, HotA 1.8.0, английская), модель и клиент;
   - пару снимков из `%LOCALAPPDATA%\HotaMcp\sessions\<сессия>\captures\` (PNG + JSON);
   - журнал сессии `%LOCALAPPDATA%\HotaMcp\sessions\<сессия>\journal.jsonl` и `%LOCALAPPDATA%\HotaMcp\errors.log`;
   - сохранение, если по нему проблема воспроизводится.

**Никогда не прикладывайте `%LOCALAPPDATA%\HotaMcp\connection.token`** — это ключ локальной службы.

### Игра на других языках

Имена существ, городов, объектов, навыков и артефактов мост берёт из таблиц установленной игры, поэтому они сами выходят на языке игры. Но в нескольких местах он сверяет русский текст игры, и на другом издании там нужна его формулировка. Если вы играете на другом языке, отчёт о том, что не читается, — уже вклад; пулреквест с текстами вашего издания — ещё лучше.

| Что ищет мост | Русский текст | Где в коде |
|---|---|---|
| Цвета игроков на флагах и окнах передачи хода | красный, синий, коричневый, зелёный, оранжевый, фиолетовый, бирюзовый, розовый | `GameReader.Colour`, `PlayerSetting` |
| Отметка посещения в строке состояния | (Посещено) / (Не посещено) | `VisitStatus.cs` |
| Вопрос о выходе | …хотите выйти… | `Bridge.ConfirmQuit`, `AwaitResult` |
| Строка повышения уровня | «<герой> теперь на уровне N» | `GameReader.LevelUpHero` |
| Вопрос о конце хода | …ещё могут ходить… | `ScreenActions` |
| Требование постройки | Требуется | `GameReader` |
| Урон в журнале боя | наносит | `Bridge` (подтверждение удара) |
| Строка состояния в бою | Атака…, …цель заклинания…, Направить… | `GameCommands`, `ScreenActions` |
| Осадная машина | Катапульта | `ScreenActions`, `ScreenBriefing` |
| Итог боя | опыт, Нападающий, Обороняющийся | `ScreenBriefing` |
| Игроки в окне сценария | Компьютер | `ScenarioReader` |
| Класс артефакта в HotA.dat | Класс: | `GameReference` |
| Имена хотсита | набираются по раскладке окна игры | `WindowsGame.KeysFor` |

### Переводы

Сводки, подписи действий и отказы моста, навык агента и справочник написаны по-русски. Переводы очень нужны:

- `skills/hota-player/SKILL.md` — как играет агент; самый ценный.
- `docs/knowledge/` — справочник, который отдаёт мост (`hota_docs`). Сохраните имена файлов и структуру заголовков, переведённый файл кладите рядом с суффиксом языка, например `00-start-here.en.md`.
- `README.md`, `README_EN.md` — добавьте `README_<ЯЗЫК>.md` и ссылку в строке языков.

Сообщения самого моста живут в исходниках C# (`ScreenBriefing.cs`, `ScreenActions.cs`, `Bridge.cs`) и пока не вынесены в ресурсы — это в планах; напишите в issue, какой язык хотите взять, и вынос сделаем вместе.

### Код

Сборка: `native\launcher\build.cmd` (Visual Studio Build Tools, x86), затем `tools\install.ps1` (установка для разработки) или `tools\build-installer.ps1` (установщик, NSIS 3). Тесты: `dotnet run --project tests/HotaMcp.TeamRules`. Правила моста: никакого перехвата мыши, никаких внутренних команд игры, одно действие за вызов, ничего сверх того, что видит игрок. Сообщение коммита — что изменилось и зачем.

## English

Three things help most right now: playing and reporting, testing other language editions of the game, and translating.

### Play and report

1. Install the [latest release](https://github.com/timoncool/hota-mcp/releases/latest), connect your MCP client, let an agent play.
2. When the bridge reads something wrong, refuses a legal move or stalls, call `debug_snapshot` on that screen (or ask the agent to). It saves a frame of the game window and the bridge's observation of the same instant.
3. Open an [issue](https://github.com/timoncool/hota-mcp/issues/new/choose) and attach:
   - the game edition and language (e.g. GOG Complete, HotA 1.8.0, English), the model and client you used;
   - the snapshot pair from `%LOCALAPPDATA%\HotaMcp\sessions\<session>\captures\` (PNG + JSON);
   - the session journal `%LOCALAPPDATA%\HotaMcp\sessions\<session>\journal.jsonl` and `%LOCALAPPDATA%\HotaMcp\errors.log`;
   - a save file, if the problem can be reproduced from it.

**Never attach `%LOCALAPPDATA%\HotaMcp\connection.token`** — it is the local service key.

### Other language editions of the game

Names of creatures, towns, objects, skills and artefacts are read from the installed game's own tables, so they come out in the game's language by themselves. A few places still match game text written in Russian, and on another edition they need that edition's wording. If you play in another language, a report of what fails is already a contribution; a pull request with the wording of your edition is even better.

| What the bridge looks for | Russian text | Where |
|---|---|---|
| Player colours on flags and hand-over windows | красный, синий, коричневый, зелёный, оранжевый, фиолетовый, бирюзовый, розовый | `GameReader.Colour`, `PlayerSetting` |
| Visited mark in the status line | (Посещено) / (Не посещено) | `VisitStatus.cs` |
| Quit question | …хотите выйти… | `Bridge.ConfirmQuit`, `AwaitResult` |
| Level-up line | «<hero> теперь на уровне N» | `GameReader.LevelUpHero` |
| End-of-turn question | …ещё могут ходить… | `ScreenActions` |
| Building requirement | Требуется | `GameReader` |
| Combat log damage | наносит | `Bridge` (combat confirmation) |
| Combat status line | Атака…, …цель заклинания…, Направить… | `GameCommands`, `ScreenActions` |
| Siege machine | Катапульта | `ScreenActions`, `ScreenBriefing` |
| Battle result | опыт, Нападающий, Обороняющийся | `ScreenBriefing` |
| Scenario players panel | Компьютер | `ScenarioReader` |
| Artefact class in HotA.dat | Класс: | `GameReference` |
| Hotseat names | typed on the game window's keyboard layout | `WindowsGame.KeysFor` |

### Translations

The bridge's briefs, action labels and refusals, the agent skill and the game reference are written in Russian. Translations are welcome:

- `skills/hota-player/SKILL.md` — how the agent plays; the most valuable one.
- `docs/knowledge/` — the game reference the bridge serves (`hota_docs`). Keep file names and headings' structure; add a translated file next to the original with a language suffix, e.g. `00-start-here.en.md`.
- `README.md`, `README_EN.md` — add `README_<LANG>.md` and a link in the language line.

The bridge's own messages live in the C# sources (`ScreenBriefing.cs`, `ScreenActions.cs`, `Bridge.cs`) and are not yet separated into resource files — that is planned; say in an issue which language you would like to take, so the extraction can be done with you.

### Code

Build: `native\launcher\build.cmd` (Visual Studio Build Tools, x86), then `tools\install.ps1` (development install) or `tools\build-installer.ps1` (installer, NSIS 3). Tests: `dotnet run --project tests/HotaMcp.TeamRules`. Rules the bridge keeps: no mouse takeover, no internal game commands, one action per call, nothing a player cannot see. Commit messages say what changed and why.
