"""Builds the project page (docs/index.html in Russian, docs/en.html in English), sitemap, robots and llms.txt.

GitHub Pages serves main:/docs. Run after changing the texts below: python build/gen-site.py
"""
import html
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
SITE = "https://timoncool.github.io/hota-mcp/"
REPO = "https://github.com/timoncool/hota-mcp"
RELEASE = REPO + "/releases/latest"
VERSION = "1.0.1"
SIZE_MB = 32
UPDATED = "2026-09-29"
PAGES = {"ru": "", "en": "en.html"}
STARS = "https://img.shields.io/github/stars/timoncool/hota-mcp?label=%E2%98%85&amp;color=3f3f46&amp;style=flat"

T = {
    "ru": {
        "title": "HotA MCP — ИИ-агент играет в Героев III: Horn of the Abyss как живой игрок",
        "description": "MCP-сервер для Heroes of Might and Magic III: Horn of the Abyss. Claude, DeepSeek или любой MCP-агент играет целую партию через настоящий интерфейс игры: меню, карта, города, бои, хотсит. Без OCR и скриншотов, бесплатно, Windows.",
        "og_alt": "ИИ-агент играет в Героев III через HotA MCP",
        "langs_label": "Язык",
        "download": "Скачать",
        "stars_alt": "Звёзды на GitHub",
        "h1": "ИИ-агент играет в Героев III как живой игрок",
        "lead": "HotA MCP подключает Claude, DeepSeek или любой MCP-агент к вашей Heroes III: Horn of the Abyss. Тот же экран, те же кнопки — партия целиком, от главного меню до экрана итогов.",
        "cta_download": "Скачать для Windows",
        "cta_star": "Поставить звезду",
        "size": "МБ",
        "note": "Windows 10/11 x64 и ваша Heroes III Complete (GOG) с HotA 1.8.1 и HD Mod. Установщик без прав администратора, .NET ставить не нужно.",
        "hero_alt": "DeepSeek Flash через HotA MCP открыл сундук и выбирает между золотом и опытом",
        "why_h": "Зачем",
        "why": "Герои — стратегия с длинным горизонтом: экономика, разведка, бои, карта, которую берут неделями. Одним удачным ходом её не выиграть, поэтому из неё выходит честный бенчмарк стратегического мышления агентов — в одиночку, против человека или в команде с ним.",
        "features_h": "Что умеет",
        "features": [
            ("Играет как человек", "Читает то, что на экране, и жмёт кнопки самой игры оконными событиями. Без перехвата мыши, computer-use, OCR и внутренних команд игры."),
            ("Вся партия", "Главное меню, настройка сценария и случайной карты, хотсит, сохранение и загрузка, карта приключений, города, найм, обмен, бои и осады, повышения, экран итогов."),
            ("Всё словами", "Каждый observe начинается со сводки хода. Герои, города, отряды и объекты — по именам; маршруты, стоимость и отметки «посещено» — от самой игры."),
            ("Честные границы", "Скрытая карта, чужой ход, устаревший взгляд — отказ с кодом причины. Агент видит ровно то, что видит игрок, и ничего сверх."),
            ("Встроенный справочник", "Правила, формулы, карточки существ и артефактов из таблиц установленной игры. Поиск офлайн — агенту не нужно полагаться на память."),
            ("Хотсит и команды", "Мост передаёт цвет хода и участников сценария, свой цвет агент выбирает сам. Играйте против агента, с ним в команде или столкните двух агентов."),
            ("Стоимость партии", "Доллары, токены и вызовы по сторонам и дням игры из телеметрии Claude Code — видно, во что обходится стратегия каждой модели."),
            ("Итог подтверждает игра", "Одно действие — один вызов по ревизии наблюдения: устаревший взгляд отклоняется, повтор безопасен. Итог подтверждает сама игра — сменой экрана, ресурсов, журнала боя."),
        ],
        "look_h": "Партия глазами агента",
        "look_sub": "Агент получает экран структурой: сводку хода, ресурсы, героев, отряды, доступные действия и их подписи. Решает он сам — мост только показывает и нажимает.",
        "look_points": [
            "<b>Одно действие — один вызов.</b> Каждое действие ссылается на ревизию наблюдения: устаревший взгляд отклоняется, повтор с тем же operationId безопасен.",
            "<b>Результат доказывается игрой:</b> сменой экрана, ресурсов, журнала боя, даты. Нет доказательства — ответ «не уверен», а не выдумка.",
            "<b>Маршруты от самой игры:</b> путь, стоимость хода и охрана клетки — ровно то, что показал бы курсор.",
            "<b>Бой словами:</b> отряды, клетки, досягаемость, ответный удар, заклинания и автобой.",
        ],
        "look_alt": "Claude играет в HotA: окно города и ход рассуждений агента",
        "tools_h": "Инструменты",
        "tools_sub": "31 инструмент; полные описания — в <a href=\"" + REPO + "/blob/main/docs/knowledge/agent/01-tools.md\">справочнике агента</a>.",
        "tools_head": ("Инструмент", "Для чего"),
        "tools": [
            ("observe", "Всё состояние своей стороны и действия, доступные на этом экране"),
            ("act", "Любое смысловое действие из observe: город, найм, бой, диалог, конец хода"),
            ("nearby_targets, inspect_target, read_map", "Что вокруг, маршрут и стоимость от самой игры, разведанная карта"),
            ("move_to, move_to_tile, attack_target", "Ходьба и бой — нарочно разные инструменты"),
            ("inspect_cell, inspect_element, inspect_tile", "Карточки правой кнопки и строка состояния, которые читает игрок"),
            ("wait_for_turn, ally_log", "Хотсит: ждать своего хода или окна, адресованного вам; что сделал союзник"),
            ("plan, mark, read_journal", "Память агента между ходами"),
            ("hota_docs, hota_reference", "Справочник по игре"),
            ("debug_snapshot", "Кадр и наблюдение одной парой — чтобы сообщить, что мост читает не так"),
        ],
        "start_h": "Как начать",
        "steps": [
            ("Установить", "Скачайте установщик и запустите, закрыв HD Launcher. Игру он найдёт сам и положит на рабочий стол ярлык «HotA MCP»."),
            ("Подключить агента", "Claude Code, Claude Desktop, Codex или любой MCP-клиент — строки ниже."),
            ("Играть", "Скажите агенту «давай поиграем в героев». Он поднимет службу, лаунчер и игру сам — или запустите их ярлыком."),
        ],
        "connect_cc": "Claude Code:",
        "connect_cd": "Claude Desktop (claude_desktop_config.json):",
        "connect_note": "Путь — если ставили в папку по умолчанию; иначе укажите свою. Правила игры сервер отдаёт клиенту при подключении, полный порядок — навык <a href=\"" + REPO + "/blob/main/skills/hota-player/SKILL.md\">hota-player</a>.",
        "user": "&lt;вы&gt;",
        "faq_h": "Вопросы",
        "faq": [
            ("Что нужно для игры?", "Windows 10/11 x64 и ваша легальная копия Heroes of Might and Magic III Complete (GOG) с Horn of the Abyss 1.8.1 и HD Mod — при подключении мост проверяет, что всё нужное ему на месте в этой сборке. Файлов игры в проекте нет."),
            ("Какие агенты подходят?", "Любой MCP-клиент через stdio или HTTP: Claude Code, Claude Desktop, Codex и другие. Проверено с Claude и DeepSeek. Автобой идёт до 3 минут — поднимите таймаут вызова инструмента в клиенте (у Codex — tool_timeout_sec = 200)."),
                                    ("Это бесплатно? Куда уходят данные?", "Проект бесплатный, лицензия MIT. Служба работает на вашем компьютере и слушает только 127.0.0.1; телеметрия стоимости партии, если включить, тоже уходит только в локальный мост."),
            ("Работает ли с игрой не на русском?", "Мост проверен на русской HotA 1.8.1: около двух десятков фраз игры мост сверяет по тексту. Если ваша игра на другом языке — расскажите, что не читается, это одна из главных задач следующих версий."),
        ],
        "help_h": "Нужна помощь",
        "help_sub": "Мост, навык агента и справочник пока говорят по-русски. Поиграйте с агентом и сообщите, где он застревает; переведите навык и справочник на свой язык — модели играют лучше, когда справочник говорит на языке игры, которую они видят.",
        "help_links": [("Как сообщить о проблеме", REPO + "/blob/main/CONTRIBUTING.md"), ("Issues", REPO + "/issues"), ("Как размечать новые экраны", REPO + "/blob/main/docs/DEVELOPING.md")],
        "author_h": "Кто сделал",
        "author_sub": "Nerual Dreming — художник, основатель ArtGeneration.me и сообщества Нейро-Картель, автор ACE-Step Studio, YuE2 Studio и MCP-серверов для Telegram и Civitai.",
        "donate": "Поддержать проект",
        "footer_rights": "Heroes of Might and Magic III и Horn of the Abyss принадлежат их правообладателям. Проект независимый, фанатский и исследовательский; он работает с вашей собственной копией игры. Код — MIT.",
        "changelog": "Что изменилось",
        "issues": "Сообщить о проблеме",
        "updated": "Обновлено",
        "close": "Закрыть",
        "faq_q": "Вопросы",
    },
    "en": {
        "title": "HotA MCP — an AI agent plays Heroes III: Horn of the Abyss like a human player",
        "description": "An MCP server for Heroes of Might and Magic III: Horn of the Abyss. Claude, DeepSeek or any MCP agent plays a whole game through the game's real interface: menus, map, towns, battles, hotseat. No OCR, no screenshots, free, Windows.",
        "og_alt": "An AI agent plays Heroes III through HotA MCP",
        "langs_label": "Language",
        "download": "Download",
        "stars_alt": "GitHub stars",
        "h1": "An AI agent plays Heroes III like a human player",
        "lead": "HotA MCP connects Claude, DeepSeek or any MCP agent to your Heroes III: Horn of the Abyss. The same screen, the same buttons — a whole game, from the main menu to the score screen.",
        "cta_download": "Download for Windows",
        "cta_star": "Star on GitHub",
        "size": "MB",
        "note": "Windows 10/11 x64 and your own Heroes III Complete (GOG) with HotA 1.8.1 and HD Mod. The installer needs no administrator rights and no .NET.",
        "hero_alt": "DeepSeek Flash opened a treasure chest through HotA MCP and chooses between gold and experience",
        "why_h": "Why",
        "why": "Heroes is a long-horizon strategy game: economy, scouting, battles, a map taken over weeks. One lucky move does not win it, which makes it an honest benchmark of an agent's strategic thinking — alone, against a human or on a human's team.",
        "features_h": "What it does",
        "features": [
            ("Plays like a human", "Reads what is on the screen and presses the game's own buttons with window events. No mouse hijacking, computer-use, OCR or internal game commands."),
            ("The whole game", "Main menu, scenario and random map setup, hotseat, save and load, the adventure map, towns, recruiting, trading, battles and sieges, level-ups, the score screen."),
            ("Everything in words", "Every observe opens with a turn summary. Heroes, towns, troops and objects by name; routes, costs and “visited” marks come from the game itself."),
            ("Honest limits", "Hidden map, another side's turn, a stale view — refused with a reason code. The agent sees exactly what a player sees and nothing more."),
            ("Built-in reference", "Rules, formulas, creature and artifact cards from the installed game's own tables. Offline search, so the agent need not rely on memory."),
            ("Hotseat and teams", "The bridge passes the colour to move and the scenario's participants; the agent picks its own colour. Play against an agent, with one on your team, or pit two agents against each other."),
            ("Cost of a game", "Dollars, tokens and calls per side and game day from Claude Code telemetry — see what each model's strategy costs."),
            ("The game confirms it", "One action per call, tied to the observation revision: a stale view is refused, a retry is safe. The game itself confirms the outcome — a new screen, resources, battle log."),
        ],
        "look_h": "The game through the agent's eyes",
        "look_sub": "The agent gets the screen as structure: a turn summary, resources, heroes, troops, the actions available and their labels. It makes the decisions — the bridge only shows and presses.",
        "look_points": [
            "<b>One action per call.</b> Every action refers to the observation revision it was decided on: a stale view is refused, a retry with the same operationId is safe.",
            "<b>The game proves the result:</b> a new screen, resources, battle log or date. No proof means an “uncertain” answer, not a guess.",
            "<b>Routes from the game itself:</b> path, movement cost and the guard of a cell — exactly what the cursor would show.",
            "<b>Battles in words:</b> stacks, hexes, reach, retaliation, spells and auto-combat.",
        ],
        "look_alt": "Claude plays HotA: a town screen and the agent's reasoning",
        "tools_h": "Tools",
        "tools_sub": "31 tools; full descriptions are in the <a href=\"" + REPO + "/blob/main/docs/knowledge/agent/01-tools.md\">agent reference</a> (in Russian).",
        "tools_head": ("Tool", "What for"),
        "tools": [
            ("observe", "Your side's whole state and the actions available on this screen"),
            ("act", "Any action named by observe: town, recruiting, battle, dialog, end of turn"),
            ("nearby_targets, inspect_target, read_map", "What is around, route and cost from the game itself, the explored map"),
            ("move_to, move_to_tile, attack_target", "Walking and fighting — deliberately separate tools"),
            ("inspect_cell, inspect_element, inspect_tile", "Right-click cards and the status line a player reads"),
            ("wait_for_turn, ally_log", "Hotseat: wait for your turn or a window addressed to you; what an ally did"),
            ("plan, mark, read_journal", "The agent's memory between turns"),
            ("hota_docs, hota_reference", "Game reference"),
            ("debug_snapshot", "A frame and an observation as one pair — to report what the bridge reads wrong"),
        ],
        "start_h": "Get started",
        "steps": [
            ("Install", "Download the installer and run it with HD Launcher closed. It finds the game itself and puts a “HotA MCP” shortcut on the desktop."),
            ("Connect an agent", "Claude Code, Claude Desktop, Codex or any MCP client — lines below."),
            ("Play", "Tell the agent “let's play Heroes”. It brings up the service, the launcher and the game itself — or start them with the shortcut."),
        ],
        "connect_cc": "Claude Code:",
        "connect_cd": "Claude Desktop (claude_desktop_config.json):",
        "connect_note": "The path assumes the default folder; use yours if you changed it. The server hands its rules to the client on connect; the full game procedure is the <a href=\"" + REPO + "/blob/main/skills/hota-player/SKILL.md\">hota-player</a> skill.",
        "user": "&lt;you&gt;",
        "faq_h": "Questions",
        "faq": [
            ("What do I need?", "Windows 10/11 x64 and your own legal copy of Heroes of Might and Magic III Complete (GOG) with Horn of the Abyss 1.8.1 and HD Mod — on attach the bridge checks that everything it needs is in place in this build. The project contains no game files."),
            ("Which agents work?", "Any MCP client over stdio or HTTP: Claude Code, Claude Desktop, Codex and others. Tested with Claude and DeepSeek. Auto-combat takes up to 3 minutes — raise the client's tool call timeout (Codex: tool_timeout_sec = 200)."),
                                    ("Is it free? Where does my data go?", "The project is free, MIT-licensed. The service runs on your computer and listens on 127.0.0.1 only; game cost telemetry, if you turn it on, also goes only to the local bridge."),
            ("Does it work with a non-Russian game?", "The bridge is verified on the Russian HotA 1.8.1: the bridge matches about twenty game phrases by text. If your game is in another language, tell us what it fails to read — that is one of the main goals of the next versions."),
        ],
        "help_h": "Help wanted",
        "help_sub": "The bridge, the agent skill and the reference speak Russian for now. Let an agent play and report where it gets stuck; translate the skill and the reference into your language — models play better when the reference speaks the language of the game they see.",
        "help_links": [("How to report a problem", REPO + "/blob/main/CONTRIBUTING.md"), ("Issues", REPO + "/issues"), ("How to map new screens", REPO + "/blob/main/docs/DEVELOPING.md")],
        "author_h": "Who made it",
        "author_sub": "Nerual Dreming — artist, founder of ArtGeneration.me and the Neuro-Cartel community, author of ACE-Step Studio, YuE2 Studio and MCP servers for Telegram and Civitai.",
        "donate": "Support the project",
        "footer_rights": "Heroes of Might and Magic III and Horn of the Abyss belong to their rights holders. This is an independent fan and research project; it works with your own copy of the game. Code — MIT.",
        "changelog": "What changed",
        "issues": "Report a problem",
        "updated": "Updated",
        "close": "Close",
        "faq_q": "Questions",
    },
}

EXTRA = {
    "ru": {
        "brand_sub": "ИИ-агент играет в Героев III",
        "stats": [("31", "инструмент"), ("8", "сторон в хотсите"), ("0", "OCR и скриншотов"), ("1", "установщик")],
        "tiles": [
            ("ti-swords", "Вся партия", ""),
            ("ti-message-2", "Всё словами", ""),
            ("ti-eye-off", "Честные границы", ""),
            ("ti-hand-click", "Играет как человек", ""),
            ("ti-book-2", "Встроенный справочник", ""),
            ("ti-users-group", "Хотсит и команды", ""),
            ("ti-coins", "Стоимость партии", ""),
            ("ti-circle-check", "Итог подтверждает игра", ""),
        ],
        "log_head": "Журнал Claude · месяц 1, неделя 2, день 4",
        "log": [
            ("move_to", "Кальт дошёл сам и открыл сундук: 2000 золота или 1800 опыта. Сборщику — золото."),
            ("act", "Брисса прошла весь путь до конца запаса хода. Игра спрашивает про остаток у Кальта — подтверждаю конец дня."),
            ("inspect_cell", "Осмотр клетки даёт ровно то, что видит игрок: (70,24) — «Толпа (20-49) Королевские Грифоны»."),
            ("hota_reference", "Считаю по таблицам игры: моя армия ≈ 6677 боевой ценности, грифоны от 20 штук — уже 7280 и выше, плюс скорость 9 и безлимитный ответный удар. Не лезу."),
            ("move_to", "Веду Кальта в город — 9950 золота лежит без дела, а армия слабая."),
        ],
        "town_alt": "Экран города в партии Claude: панорама, гарнизон и строка ресурсов",
        "viewer_hint": "Колесо или щипок — зум · перетаскивание — сдвиг · двойной клик — приблизить или сбросить · Esc — закрыть",
    },
    "en": {
        "brand_sub": "An AI agent plays Heroes III",
        "stats": [("31", "tools"), ("8", "hotseat sides"), ("0", "OCR or screenshots"), ("1", "installer")],
        "tiles": [
            ("ti-swords", "The whole game", ""),
            ("ti-message-2", "Everything in words", ""),
            ("ti-eye-off", "Honest limits", ""),
            ("ti-hand-click", "Plays like a human", ""),
            ("ti-book-2", "Built-in reference", ""),
            ("ti-users-group", "Hotseat and teams", ""),
            ("ti-coins", "Cost of a game", ""),
            ("ti-circle-check", "The game confirms it", ""),
        ],
        "log_head": "Claude's log · month 1, week 2, day 4",
        "log": [
            ("move_to", "Kalt got there by himself and opened a chest: 2000 gold or 1800 experience. For a gatherer — the gold."),
            ("act", "Brissa walked the whole way to the end of her movement. The game asks about Kalt's remaining points — I confirm the end of the day."),
            ("inspect_cell", "Inspecting a cell gives exactly what a player sees: (70,24) — “A horde (20-49) of Royal Griffins”."),
            ("hota_reference", "By the game's own tables: my army is ≈ 6677 fight value, griffins from 20 up are 7280 and more, plus speed 9 and unlimited retaliation. Not going in."),
            ("move_to", "Taking Kalt to town — 9950 gold is lying idle and the army is weak."),
        ],
        "town_alt": "A town screen from Claude's game: panorama, garrison and the resource bar",
        "viewer_hint": "Wheel or pinch — zoom · drag — pan · double click — zoom in or reset · Esc — close",
    },
}
for code in T:
    T[code].update(EXTRA[code])

T["ru"].update({
    "ideas_h": "Идеи и принципы",
    "ideas_sub": "Всё началось с идеи матчей между разными нейросетями. Цель выросла шире: программный игрок, который занимает место живого — против агента, против человека, в союзе с ним, по просьбе «поиграй за меня» или «развивайся до Капитолия», в полностью автономной партии.",
    "ideas": [
        ("Настоящая игра", "Агент играет в установленную у вас HotA, а не в копию или симулятор. Человек смотрит, участвует и в любой момент забирает управление."),
        ("Как живой игрок", "Действия идут через штатные обработчики интерфейса. Героя не переносят записью координат, покупку не делают правкой ресурсов, исход боя не пишут в память."),
        ("Без картинок", "Игра читается структурой, без скриншотов и OCR: так партия остаётся точной и недорогой."),
        ("Честная граница", "Агент знает ровно то, что узнал бы человек за этой стороной. Туман, чужие армии и планы компьютера закрыты; неизвестное — явный статус, а не выдумка."),
        ("Мост не советчик", "Он передаёт состояние и выполняет решения. Цели, маршрут, бой и свой цвет выбирает агент; справка — отдельный инструмент и в ответы не подмешивается."),
        ("Команды по смыслу", "Агент называет цель — город, сундук, шахту, отряд — а не пиксели. Геометрия и нажатия — забота моста."),
        ("Надёжные операции", "Каждое действие привязано к ревизии наблюдения: повтор не купит войска дважды, при неизвестном итоге сначала сверка, а не слепой повтор."),
        ("Экономия", "Короткая сводка и подробности по запросу; ожидание анимаций и чужих ходов не тратит вызовы модели. Стоимость партии считается по фактическим данным."),
        ("Любая модель", "Мост не привязан к поставщику: Claude, DeepSeek или любой другой агент с MCP."),
        ("Весь цикл без подготовки", "От закрытой игры до экрана итогов: запуск, меню, сценарий или случайная карта, партия, сохранение и загрузка — без ручной подготовки."),
    ],
})
T["en"].update({
    "ideas_h": "Ideas and principles",
    "ideas_sub": "It started as an idea for matches between different AI models. The goal grew wider: a programmatic player that takes a human's seat — against an agent, against a human, as an ally, on “play for me” or “build up to the Capitol”, or in a fully autonomous game.",
    "ideas": [
        ("The real game", "The agent plays your installed HotA, not a copy or a simulator. A human watches, joins in and can take control at any moment."),
        ("Like a human player", "Actions go through the interface's own handlers. A hero is not moved by writing coordinates, a purchase is not a resource edit, a battle result is not written to memory."),
        ("No pictures", "The game is read as structure, without screenshots or OCR, which keeps a game exact and cheap."),
        ("An honest boundary", "The agent knows exactly what a human on that side would. Fog, foreign armies and the computer's plans stay closed; the unknown is an explicit status, not a guess."),
        ("The bridge is no advisor", "It passes the state and carries out decisions. Goals, routes, battles and its own colour are the agent's call; the reference is a separate tool and never mixed into answers."),
        ("Commands by meaning", "The agent names a target — a town, a chest, a mine, a stack — not pixels. Geometry and clicks are the bridge's job."),
        ("Reliable operations", "Every action is tied to an observation revision: a retry never buys troops twice, and an unknown outcome is checked first, never blindly repeated."),
        ("Economy", "A short summary with details on request; waiting for animations and other turns costs no model calls. The cost of a game is measured from real data."),
        ("Any model", "The bridge is not tied to a provider: Claude, DeepSeek or any other agent with MCP."),
        ("The whole cycle, no setup", "From a closed game to the score screen: launch, menus, a scenario or a random map, the game, save and load — with no manual preparation."),
    ],
})

FONTS = "https://fonts.googleapis.com/css2?family=Cormorant+Garamond:wght@600;700&family=PT+Sans:ital,wght@0,400;0,700;1,400&display=swap"
TABLER = "https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@3.48.0/dist/tabler-icons.min.css"
NOISE = ("url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='240' height='240'%3E%3Cfilter id='n'%3E"
         "%3CfeTurbulence type='fractalNoise' baseFrequency='.9' numOctaves='3' stitchTiles='stitch'/%3E%3CfeColorMatrix "
         "values='0 0 0 0 0.16 0 0 0 0 0.09 0 0 0 0 0.03 0 0 0 .55 0'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' "
         "filter='url(%23n)'/%3E%3C/svg%3E\")")

CSS = """
:root {
  --ink: #140c06;
  --leather: #30180c;
  --gold: #e4cc6c;
  --gold-2: #c0a848;
  --gold-3: #b49c48;
  --gold-dark: #483c24;
  --parch: #efe3c4;
  --muted: #c2b08a;
  --crimson: #9c0000;
  --crimson-2: #840000;
  --crimson-dark: #480000;
  --royal: #243c84;
  --royal-2: #18306c;
  --bevel: #f0dc8c #7a6530 #5a4a22 #cdb467;
  --noise: NOISE;
}
* { box-sizing: border-box; }
html { scroll-behavior: smooth; }
body {
  margin: 0; color: var(--parch);
  background: var(--noise), radial-gradient(120% 60% at 50% 0%, #2a170b 0%, var(--ink) 60%) fixed, var(--ink);
  font: 16px/1.65 "PT Sans", "Segoe UI", Roboto, Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
}
a { color: inherit; }
img { max-width: 100%; }
code { font: 13.5px/1.5 ui-monospace, "Cascadia Mono", Consolas, monospace; }
.wrap { max-width: 1140px; margin: 0 auto; padding: 0 20px; }
.serif { font-family: "Cormorant Garamond", Georgia, serif; }
body { font-variant-numeric: lining-nums; }
.gold-text {
  background: linear-gradient(180deg, #fff3bf 0%, #eed27a 38%, #c89f3e 62%, #f3dc8e 100%);
  -webkit-background-clip: text; background-clip: text; color: transparent;
  filter: drop-shadow(0 2px 0 rgba(0,0,0,.75)) drop-shadow(0 0 1px rgba(0,0,0,.9));
}

header {
  position: sticky; top: 0; z-index: 20;
  background: var(--noise), linear-gradient(180deg, #3a2010, #22120a);
  border-bottom: 2px solid var(--gold-3);
  box-shadow: 0 1px 0 #0a0603, 0 6px 18px rgba(0,0,0,.5);
}
.bar { display: flex; align-items: center; gap: 12px; padding-block: 8px; }
.mark { width: 38px; height: 38px; flex: none; filter: drop-shadow(0 2px 3px rgba(0,0,0,.6)); }
.name { font-family: "Cormorant Garamond", Georgia, serif; font-weight: 700; font-size: 24px; letter-spacing: .01em; white-space: nowrap; }
.langs { display: flex; gap: 14px; margin-left: auto; white-space: nowrap; }
.langs a { font-size: 13px; color: var(--muted); text-decoration: none; }
.langs a:hover { color: var(--parch); }
.langs a[aria-current="page"] { color: var(--gold); font-weight: 700; }

.btn {
  display: inline-flex; align-items: center; justify-content: center; gap: 10px; text-decoration: none;
  padding: 11px 20px; font-weight: 700; font-size: 16px; line-height: 1.2; color: #fff3cf; white-space: nowrap;
  border: 2px solid; border-color: var(--bevel); border-radius: 3px;
  background: var(--noise), linear-gradient(180deg, #b81414 0%, var(--crimson-2) 55%, #5e0000 100%);
  text-shadow: 0 1px 0 #000;
  box-shadow: 0 0 0 1px #0a0603, inset 0 1px 0 rgba(255,220,170,.35), inset 0 -2px 0 rgba(0,0,0,.35), 0 4px 10px rgba(0,0,0,.45);
  transition: filter .12s;
}
.btn:hover { filter: brightness(1.12); }
.btn:active { transform: translateY(1px); box-shadow: 0 0 0 1px #0a0603, inset 0 2px 6px rgba(0,0,0,.5); }
.btn.blue { background: var(--noise), linear-gradient(180deg, #3552a2 0%, var(--royal-2) 58%, #11234f 100%); }
.btn small { font-weight: 400; font-size: 12.5px; opacity: .85; }
.btn img { display: block; }
.btn.slim { padding: 7px 14px; font-size: 14px; }

.hero {
  position: relative; text-align: center; padding: 60px 0 36px;
  background: linear-gradient(180deg, rgba(20,12,6,.35) 0%, rgba(20,12,6,.72) 55%, var(--ink) 100%), url(site/hero-bg.webp) center 30% / cover no-repeat;
  border-bottom: 1px solid rgba(180,156,72,.25);
}
.brand { display: inline-flex; align-items: center; gap: 18px; margin: 0 0 18px; }
.brand img { width: 118px; height: auto; filter: drop-shadow(0 8px 18px rgba(0,0,0,.7)); }
.brand-name { font-family: "Cormorant Garamond", Georgia, serif; font-weight: 700; font-size: clamp(48px, 8vw, 88px); line-height: .95; text-align: left; }
.brand-sub { display: block; font-family: "PT Sans", sans-serif; font-size: 15px; letter-spacing: .18em; text-transform: uppercase; color: var(--gold); margin-top: 6px; text-shadow: 0 1px 0 #000; }
h1 { font-family: "Cormorant Garamond", Georgia, serif; font-weight: 700; font-size: clamp(30px, 4.6vw, 50px); line-height: 1.1; margin: 0 auto; max-width: 18em; color: var(--parch); text-shadow: 0 2px 0 #000, 0 0 24px rgba(0,0,0,.8); }
.lead { color: #e2d3ae; font-size: clamp(17px, 2.1vw, 19px); margin: 16px auto 0; max-width: 40em; text-shadow: 0 1px 0 #000; }
.cta { display: flex; flex-wrap: wrap; justify-content: center; gap: 12px; margin: 28px 0 10px; }
.note { color: var(--muted); font-size: 13.5px; margin: 8px auto 0; max-width: 60em; }

.res {
  display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); margin: 28px auto 0; max-width: 900px;
  background: var(--noise), linear-gradient(180deg, #2c4892 0%, var(--royal-2) 55%, #10224f 100%);
  border: 2px solid; border-color: var(--bevel);
  box-shadow: 0 0 0 1px #0a0603, inset 0 0 0 1px rgba(0,0,0,.5), 0 8px 22px rgba(0,0,0,.5);
}
.res div { padding: 10px 8px 12px; text-align: center; border-left: 1px solid rgba(10,6,3,.7); box-shadow: inset 1px 0 0 rgba(228,204,108,.25); }
.res div:first-child { border-left: 0; box-shadow: none; }
.res b { display: block; font-family: "Cormorant Garamond", Georgia, serif; font-size: 34px; line-height: 1; color: var(--gold); text-shadow: 0 2px 0 #000; }
.res span { font-size: 13px; color: #d9e0f3; text-shadow: 0 1px 0 #000; }

.frame { margin: 0; }
.frame img { display: block; width: 100%; height: auto; border: 1px solid rgba(228,204,108,.28); box-shadow: 0 18px 44px rgba(0,0,0,.55); cursor: zoom-in; }
.hero-shot { margin: 40px auto 0; max-width: 1100px; }

section { padding: 76px 0 0; }
.sec-head { text-align: center; margin: 0 auto 26px; max-width: 48em; }
h2 { font-family: "Cormorant Garamond", Georgia, serif; font-weight: 700; font-size: clamp(32px, 4.2vw, 46px); line-height: 1.05; margin: 0; }
.rule { position: relative; height: 14px; max-width: 360px; margin: 10px auto 0;
  background: linear-gradient(90deg, transparent, var(--gold-3) 22%, var(--gold) 50%, var(--gold-3) 78%, transparent) center / 100% 1px no-repeat; }
.rule::after { content: ""; position: absolute; left: 50%; top: 50%; width: 10px; height: 10px; transform: translate(-50%, -50%) rotate(45deg);
  background: linear-gradient(135deg, #fff1b8, #b49c48); box-shadow: 0 0 0 2px var(--ink), 0 0 0 3px var(--gold-3); }
.sub { color: var(--muted); margin: 14px auto 0; }
.sub a, .note a, .connect a { color: var(--gold); }

.panel {
  position: relative;
  background: var(--noise), radial-gradient(130% 100% at 25% 0%, #5a3a1c 0%, #3a2210 52%, #25130a 100%);
  border: 1px solid rgba(228,204,108,.26); border-radius: 2px;
  box-shadow: inset 0 0 34px rgba(0,0,0,.4), 0 10px 26px rgba(0,0,0,.4);
}

.bento { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; }
.tile { padding: 18px; display: flex; flex-direction: column; gap: 12px; }
.tile.wide { grid-column: span 3; }
.tile h3 { margin: 0; font-family: "Cormorant Garamond", Georgia, serif; font-size: 25px; line-height: 1.1; color: var(--gold); text-shadow: 0 2px 0 #000; }
.tile p { margin: 0; color: #dccba3; font-size: 15px; line-height: 1.55; }
.tile .pic { margin: 0; padding: 4px; background: linear-gradient(135deg, #e4cc6c, #6b5a2a 50%, #c0a848); box-shadow: 0 0 0 1px #0a0603; }
.tile .pic img { display: block; width: 100%; height: 190px; object-fit: cover; object-position: top center; border: 1px solid #0a0603; cursor: zoom-in; image-rendering: auto; }
.tile .row { display: flex; align-items: center; gap: 14px; }
.medal { flex: none; width: 50px; height: 50px; border-radius: 50%; display: grid; place-items: center; font-size: 26px; color: var(--gold);
  background: var(--noise), radial-gradient(circle at 40% 30%, #c01818, var(--crimson-2) 55%, #4a0000);
  border: 2px solid; border-color: var(--bevel); box-shadow: 0 0 0 1px #0a0603, inset 0 2px 6px rgba(0,0,0,.45), 0 3px 8px rgba(0,0,0,.5);
  text-shadow: 0 1px 0 #000; }

.agent-shot { max-width: 640px; margin: 0 auto; }
.log { padding: 0; overflow: hidden; }
.log-head { padding: 10px 16px; font-family: "Cormorant Garamond", Georgia, serif; font-size: 20px; font-weight: 700; color: var(--gold);
  background: var(--noise), linear-gradient(180deg, #a11212, var(--crimson-dark)); border-bottom: 2px solid var(--gold-3); text-shadow: 0 1px 0 #000; }
.log ol { list-style: none; margin: 0; padding: 8px 16px 14px; display: grid; gap: 0; }
.log li { padding: 12px 0; border-bottom: 1px solid rgba(180,156,72,.22); font-size: 15px; line-height: 1.55; color: #eadcb8; }
.log li:last-child { border-bottom: 0; }
.call { display: inline-block; margin: 0 8px 4px 0; padding: 1px 8px; font-size: 12.5px; color: var(--gold); background: rgba(0,0,0,.45); border: 1px solid var(--gold-dark); border-radius: 2px; }
.points { list-style: none; margin: 26px 0 0; padding: 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; }
.points li { padding: 16px 18px; color: #dccba3; font-size: 15px; line-height: 1.55; }
.points li b { color: var(--gold); }

.table { padding: 6px 18px; overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: 15px; }
th, td { text-align: left; padding: 11px 10px; border-bottom: 1px solid rgba(180,156,72,.22); vertical-align: top; }
tr:last-child td { border-bottom: 0; }
th { color: var(--gold); font-family: "Cormorant Garamond", Georgia, serif; font-size: 19px; font-weight: 700; }
td { color: #e6d6b0; }
td:first-child { white-space: nowrap; }
td code { color: var(--gold); }

.steps { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }
.steps li { padding: 22px 18px 18px; display: grid; gap: 8px; align-content: start; }
.steps b { font-family: "Cormorant Garamond", Georgia, serif; font-size: 24px; color: var(--gold); display: flex; align-items: center; gap: 12px; text-shadow: 0 2px 0 #000; }
.steps .num { width: 40px; height: 40px; flex: none; display: grid; place-items: center; border-radius: 50%; font-size: 22px;
  background: var(--noise), radial-gradient(circle at 40% 30%, #c01818, var(--crimson-2) 55%, #4a0000);
  border: 2px solid; border-color: var(--bevel); box-shadow: 0 0 0 1px #0a0603; }
.steps span { color: #dccba3; font-size: 15px; line-height: 1.55; }
.connect { margin-top: 22px; padding: 18px 20px; display: grid; gap: 8px; }
.connect p { margin: 6px 0 0; color: var(--muted); font-size: 14.5px; }
.connect p:first-child { margin-top: 0; }
pre { margin: 0; padding: 12px 14px; overflow-x: auto; color: #f3e6c4; background: rgba(0,0,0,.55); border: 1px solid var(--gold-dark); box-shadow: inset 0 2px 8px rgba(0,0,0,.6); }
#start .cta { justify-content: center; margin-top: 26px; }

.faq { padding: 4px 22px; }
.faq details { border-bottom: 1px solid rgba(180,156,72,.22); }
.faq details:last-child { border-bottom: 0; }
.faq summary { list-style: none; cursor: pointer; padding: 16px 36px 16px 0; font-family: "Cormorant Garamond", Georgia, serif; font-weight: 700; font-size: 22px; color: var(--gold); position: relative; text-shadow: 0 1px 0 #000; }
.faq summary::-webkit-details-marker { display: none; }
.faq summary::after { content: "+"; position: absolute; right: 4px; top: 12px; font-size: 26px; color: var(--gold-2); }
.faq details[open] summary::after { content: "\\2212"; }
.answer { padding: 0 0 18px; color: #e2d2ab; max-width: 52em; }
.answer p { margin: 0; }

.pills { display: flex; flex-wrap: wrap; justify-content: center; gap: 10px; }
footer { margin: 76px 0 36px; padding-top: 22px; border-top: 1px solid rgba(180,156,72,.3); color: var(--muted); font-size: 13.5px; text-align: center; }
footer p { margin: 0 0 8px; }
footer a { color: var(--gold-2); }

.viewer { position: fixed; inset: 0; z-index: 50; display: none; overflow: hidden; background: rgba(8,5,2,.94); touch-action: none; }
.viewer.open { display: block; }
.viewer img { position: absolute; left: 0; top: 0; transform-origin: 0 0; max-width: none; cursor: grab; user-select: none; -webkit-user-drag: none; box-shadow: 0 0 0 1px #0a0603, 0 0 0 5px #b49c48, 0 20px 60px rgba(0,0,0,.7); }
.viewer img.dragging { cursor: grabbing; }
.viewer img.pixel { image-rendering: pixelated; }
.viewer .close { position: absolute; top: 12px; right: 14px; z-index: 2; width: 44px; height: 44px; padding: 0; font-size: 24px; }
.viewer .hint { position: absolute; left: 50%; bottom: 14px; transform: translateX(-50%); z-index: 2; max-width: calc(100% - 32px); padding: 6px 12px; font-size: 13px; color: var(--muted); text-align: center; background: rgba(20,12,6,.8); border: 1px solid var(--gold-dark); }

@media (max-width: 960px) {
  .bento { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .tile, .tile.wide { grid-column: span 1; }
  .look { grid-template-columns: minmax(0, 1fr); }
  .points { grid-template-columns: minmax(0, 1fr); }
  .steps { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 600px) {
  .hero { padding-top: 36px; }
  .name, .langs + .btn { display: none; }
  .brand { gap: 12px; }
  .brand img { width: 78px; }
  .brand-sub { font-size: 12px; letter-spacing: .12em; }
  .res { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .res div:nth-child(3) { border-left: 0; box-shadow: none; }
  .res div:nth-child(n+3) { border-top: 1px solid rgba(10,6,3,.7); }
  .bento { grid-template-columns: minmax(0, 1fr); }
  .cta .btn { width: 100%; }
  .btn.primary-dl { flex-direction: column; gap: 2px; }
  td:first-child { white-space: normal; }
  .faq, .table, .connect { padding-inline: 14px; }
}
""".replace("NOISE", NOISE)

SCRIPT = """
const viewer = document.getElementById('viewer');
const view = document.getElementById('viewer-image');
let scale = 1, fit = 1, x = 0, y = 0, dragging = null, moved = false;
const pointers = new Map();
let pinch = null;
function paint() {
  view.style.transform = 'translate(' + x + 'px,' + y + 'px) scale(' + scale + ')';
  view.classList.toggle('pixel', scale > 1.6);
}
function fitImage() {
  const w = view.naturalWidth, h = view.naturalHeight;
  fit = Math.min((innerWidth - 40) / w, (innerHeight - 90) / h, 1);
  scale = fit; x = (innerWidth - w * fit) / 2; y = (innerHeight - h * fit) / 2 - 20; paint();
}
function zoomAt(cx, cy, next) {
  next = Math.min(fit * 10, Math.max(fit, next));
  x = cx - (cx - x) * next / scale; y = cy - (cy - y) * next / scale; scale = next;
  if (scale === fit) fitImage(); else paint();
}
function openViewer(image) {
  view.alt = image.alt;
  view.onload = fitImage;
  view.src = image.currentSrc || image.src;
  viewer.classList.add('open'); viewer.setAttribute('aria-hidden', 'false');
  document.body.style.overflow = 'hidden';
}
function closeViewer() {
  viewer.classList.remove('open'); viewer.setAttribute('aria-hidden', 'true');
  document.body.style.overflow = ''; pointers.clear(); pinch = null;
}
document.addEventListener('click', (event) => {
  const image = event.target.closest('.frame img, .tile .pic img');
  if (image) openViewer(image);
});
viewer.addEventListener('click', (event) => {
  if (event.target.closest('.close')) return closeViewer();
  if (event.target === viewer && !moved) closeViewer();
});
viewer.addEventListener('wheel', (event) => {
  event.preventDefault();
  zoomAt(event.clientX, event.clientY, scale * Math.exp(-event.deltaY * 0.0015));
}, { passive: false });
view.addEventListener('dblclick', (event) => {
  if (scale > fit * 1.05) fitImage(); else zoomAt(event.clientX, event.clientY, fit * 3);
});
view.addEventListener('pointerdown', (event) => {
  view.setPointerCapture(event.pointerId);
  pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
  moved = false;
  if (pointers.size === 2) {
    const [a, b] = [...pointers.values()];
    pinch = { d: Math.hypot(a.x - b.x, a.y - b.y), s: scale };
  } else {
    dragging = { x: event.clientX, y: event.clientY };
    view.classList.add('dragging');
  }
});
view.addEventListener('pointermove', (event) => {
  if (!pointers.has(event.pointerId)) return;
  pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
  if (pinch && pointers.size === 2) {
    const [a, b] = [...pointers.values()];
    zoomAt((a.x + b.x) / 2, (a.y + b.y) / 2, pinch.s * Math.hypot(a.x - b.x, a.y - b.y) / pinch.d);
    moved = true;
  } else if (dragging) {
    x += event.clientX - dragging.x; y += event.clientY - dragging.y;
    dragging = { x: event.clientX, y: event.clientY }; moved = true; paint();
  }
});
function release(event) {
  pointers.delete(event.pointerId);
  if (pointers.size < 2) pinch = null;
  if (pointers.size === 0) { dragging = null; view.classList.remove('dragging'); }
}
view.addEventListener('pointerup', release);
view.addEventListener('pointercancel', release);
addEventListener('resize', () => { if (viewer.classList.contains('open')) fitImage(); });
document.addEventListener('keydown', (event) => {
  if (!viewer.classList.contains('open')) return;
  if (event.key === 'Escape') closeViewer();
  if (event.key === '+' || event.key === '=') zoomAt(innerWidth / 2, innerHeight / 2, scale * 1.25);
  if (event.key === '-') zoomAt(innerWidth / 2, innerHeight / 2, scale / 1.25);
  if (event.key === '0') fitImage();
});
// the download buttons name the latest release as GitHub has it, so a new release needs no new page
fetch('https://api.github.com/repos/timoncool/hota-mcp/releases/latest')
  .then((response) => (response.ok ? response.json() : Promise.reject(new Error('GitHub answered ' + response.status))))
  .then((release) => {
    const installer = release.assets.find((asset) => /^HotaMcp-Setup-.+\\.exe$/.test(asset.name));
    if (!installer) throw new Error('the latest release has no installer');
    const label = release.tag_name + ' · ' + Math.round(installer.size / 1048576) + ' SIZE';
    document.querySelectorAll('[data-release]').forEach((element) => { element.textContent = label; });
  })
  .catch((error) => console.warn('Latest release not read; the page shows the one it was built with:', error));
"""


def url(lang):
    return SITE + PAGES[lang]


def esc(text):
    return html.escape(text, quote=True)


def json_ld(lang, t):
    faq = [{"@type": "Question", "name": q, "acceptedAnswer": {"@type": "Answer", "text": a}} for q, a in t["faq"]]
    graph = [
        {
            "@type": "SoftwareApplication",
            "@id": SITE + "#app",
            "name": "HotA MCP",
            "url": url(lang),
            "inLanguage": lang,
            "description": t["description"],
            "applicationCategory": "DeveloperApplication",
            "applicationSubCategory": "MCP server for AI agents",
            "operatingSystem": "Windows 10, Windows 11",
            "softwareVersion": VERSION,
            "fileSize": f"{SIZE_MB} MB",
            "downloadUrl": RELEASE,
            "isAccessibleForFree": True,
            "license": "https://opensource.org/licenses/MIT",
            "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
            "image": SITE + f"og-{lang}.jpg",
            "logo": SITE + "icon-512.png",
            "screenshot": SITE + "screenshots/deepseek.png",
            "codeRepository": REPO,
            "sameAs": [REPO],
            "about": {"@type": "VideoGame", "name": "Heroes of Might and Magic III: Horn of the Abyss"},
            "author": {"@id": "https://github.com/timoncool#person"},
            "dateModified": UPDATED,
        },
        {
            "@type": "Person",
            "@id": "https://github.com/timoncool#person",
            "name": "Nerual Dreming",
            "url": "https://github.com/timoncool",
            "sameAs": ["https://github.com/timoncool", "https://t.me/nerual_dreming", "https://artgeneration.me"],
        },
        {"@type": "FAQPage", "inLanguage": lang, "mainEntity": faq},
    ]
    return json.dumps({"@context": "https://schema.org", "@graph": graph}, ensure_ascii=False, separators=(",", ":"))


def head_section(title, sub=""):
    extra = f'\n      <p class="sub">{sub}</p>' if sub else ""
    return f'<div class="sec-head"><h2 class="gold-text">{title}</h2><div class="rule"></div>{extra}</div>'


def page(lang):
    t = T[lang]
    exe = r"C:\Users\{}\AppData\Local\Programs\HotaMcp\app\HotaMcp.exe".format(t["user"])
    exe_json = exe.replace("\\", "\\\\")
    release_label = f"v{VERSION} · {SIZE_MB} {t['size']}"
    alternates = "\n".join(f'<link rel="alternate" hreflang="{code}" href="{url(code)}" />' for code in PAGES)
    current = ' aria-current="page"'
    langs = "\n".join(
        f'      <a href="./{PAGES[code]}" hreflang="{code}"{current if code == lang else ""}>{name}</a>'
        for code, name in (("ru", "Русский"), ("en", "English"))
    )
    stats = "".join(f"<div><b>{n}</b><span>{label}</span></div>" for n, label in t["stats"])
    texts = {title: text for title, text in t["features"]}
    tiles = []
    for index, (kind, title, alt) in enumerate(t["tiles"]):
        wide = ""
        if kind.startswith("img:"):
            name = kind[4:]
            body = f'<figure class="pic"><img src="site/{name}.webp" alt="{esc(alt)}" loading="lazy" /></figure><h3>{title}</h3><p>{texts[title]}</p>'
        else:
            body = f'<div class="row"><span class="medal"><i class="ti {kind}" aria-hidden="true"></i></span><h3>{title}</h3></div><p>{texts[title]}</p>'
        tiles.append(f'      <article class="tile panel{wide}">{body}</article>')
    tiles = "\n".join(tiles)
    ideas = "\n".join(f'      <li class="panel"><b>{h}.</b> {p}</li>' for h, p in t["ideas"])
    tools = "\n".join(
        "          <tr><td>{}</td><td>{}</td></tr>".format(", ".join(f"<code>{n.strip()}</code>" for n in names.split(",")), what)
        for names, what in t["tools"]
    )
    steps = "\n".join(f'      <li class="panel"><b><span class="num">{i}</span>{h}</b><span>{p}</span></li>' for i, (h, p) in enumerate(t["steps"], 1))
    faq = "\n".join(f'      <details><summary>{q}</summary><div class="answer"><p>{a}</p></div></details>' for q, a in t["faq"])
    help_links = "\n".join(f'      <a class="btn blue slim" href="{href}">{name}</a>' for name, href in t["help_links"])
    stars = f'<img class="stars" src="{STARS}" alt="{t["stars_alt"]}" height="20" />'
    return f"""<!doctype html>
<html lang="{lang}">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>{esc(t['title'])}</title>
<meta name="description" content="{esc(t['description'])}" />
<meta name="theme-color" content="#30180c" />
<link rel="canonical" href="{url(lang)}" />
{alternates}
<link rel="alternate" hreflang="x-default" href="{url('en')}" />
<link rel="icon" href="favicon.ico" sizes="16x16 32x32 48x48" />
<link rel="icon" type="image/png" sizes="192x192" href="icon-192.png" />
<link rel="apple-touch-icon" href="apple-touch-icon.png" />
<link rel="manifest" href="site.webmanifest" />
<meta property="og:type" content="website" />
<meta property="og:site_name" content="HotA MCP" />
<meta property="og:title" content="{esc(t['title'])}" />
<meta property="og:description" content="{esc(t['description'])}" />
<meta property="og:url" content="{url(lang)}" />
<meta property="og:locale" content="{'ru_RU' if lang == 'ru' else 'en_US'}" />
<meta property="og:locale:alternate" content="{'en_US' if lang == 'ru' else 'ru_RU'}" />
<meta property="og:image" content="{SITE}og-{lang}.jpg" />
<meta property="og:image:width" content="1200" />
<meta property="og:image:height" content="630" />
<meta property="og:image:alt" content="{esc(t['og_alt'])}" />
<meta name="twitter:card" content="summary_large_image" />
<meta name="twitter:title" content="{esc(t['title'])}" />
<meta name="twitter:description" content="{esc(t['description'])}" />
<meta name="twitter:image" content="{SITE}og-{lang}.jpg" />
<link rel="preconnect" href="https://fonts.googleapis.com" />
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
<link rel="stylesheet" href="{FONTS}" />
<link rel="stylesheet" href="{TABLER}" />
<link rel="preload" as="image" href="site/hero-bg.webp" />
<script type="application/ld+json">{json_ld(lang, t)}</script>
<style>{CSS}</style>
</head>
<body>
<header>
  <div class="wrap bar">
    <img class="mark" src="icon-192.png" alt="" width="38" height="38" />
    <div class="name gold-text">HotA MCP</div>
    <nav class="langs" aria-label="{t['langs_label']}">
{langs}
    </nav>
    <a class="btn blue slim" href="{REPO}">GitHub{stars}</a>
    <a class="btn slim" href="{RELEASE}">{t['download']}</a>
  </div>
</header>

<section class="hero">
  <div class="wrap">
    <div class="brand"><img src="icon-512.png" alt="HotA MCP" width="118" height="118" /><div class="brand-name"><span class="gold-text">HotA MCP</span></div></div>
    <h1>{t['h1']}</h1>
    <p class="lead">{t['lead']}</p>
    <div class="cta">
      <a class="btn primary-dl" href="{RELEASE}">{t['cta_download']}<small data-release>{release_label}</small></a>
      <a class="btn blue" href="{REPO}">{t['cta_star']}{stars}</a>
    </div>
    <p class="note">{t['note']}</p>
    <div class="res">{stats}</div>
  </div>
</section>

<main class="wrap">
  <figure class="frame hero-shot"><img src="screenshots/deepseek.png" alt="{esc(t['hero_alt'])}" width="1800" height="783" /></figure>

  <section id="why">
    {head_section(t['why_h'], t['why'])}
  </section>

  <section id="ideas">
    {head_section(t['ideas_h'], t['ideas_sub'])}
    <ul class="points">
{ideas}
    </ul>
  </section>

  <section id="features">
    {head_section(t['features_h'])}
    <div class="bento">
{tiles}
    </div>
  </section>

  <section id="agent">
    {head_section(t['look_h'], t['look_sub'])}
    <figure class="frame agent-shot"><img src="screenshots/hero.png" alt="{esc(t['look_alt'])}" loading="lazy" width="815" height="1252" /></figure>
  </section>

  <section id="tools">
    {head_section(t['tools_h'], t['tools_sub'])}
    <div class="panel table"><table>
        <thead><tr><th>{t['tools_head'][0]}</th><th>{t['tools_head'][1]}</th></tr></thead>
        <tbody>
{tools}
        </tbody>
    </table></div>
  </section>

  <section id="start">
    {head_section(t['start_h'])}
    <ol class="steps">
{steps}
    </ol>
    <div class="panel connect">
      <p>{t['connect_cc']}</p>
      <pre><code>claude mcp add hota -- "{exe}" --stdio</code></pre>
      <p>{t['connect_cd']}</p>
      <pre><code>{{
  "mcpServers": {{
    "hota": {{
      "command": "{exe_json}",
      "args": ["--stdio"]
    }}
  }}
}}</code></pre>
      <p>{t['connect_note']}</p>
    </div>
    <div class="cta"><a class="btn primary-dl" href="{RELEASE}">{t['cta_download']}<small data-release>{release_label}</small></a></div>
  </section>

  <section id="faq">
    {head_section(t['faq_h'])}
    <div class="panel faq">
{faq}
    </div>
  </section>

  <section id="help">
    {head_section(t['help_h'], t['help_sub'])}
    <div class="pills">
{help_links}
    </div>
  </section>

  <section id="author">
    {head_section(t['author_h'], t['author_sub'])}
    <div class="pills">
      <a class="btn blue slim" href="https://github.com/timoncool">GitHub · @timoncool</a>
      <a class="btn blue slim" href="https://t.me/nerual_dreming">Telegram · @nerual_dreming</a>
      <a class="btn blue slim" href="https://t.me/neuroport">Telegram · @neuroport</a>
      <a class="btn blue slim" href="https://artgeneration.me">ArtGeneration.me</a>
      <a class="btn slim" href="https://github.com/timoncool/ACE-Step-Studio/blob/master/DONATE.md">{t['donate']}</a>
    </div>
  </section>

  <footer>
    <p>{t['footer_rights']}</p>
    <p><a href="{REPO}/blob/main/CHANGELOG.md">{t['changelog']}</a> · <a href="{REPO}/issues">{t['issues']}</a> · {t['updated']} <time datetime="{UPDATED}">{UPDATED}</time></p>
  </footer>
</main>

<div class="viewer" id="viewer" aria-hidden="true" role="dialog">
  <button type="button" class="btn slim close" aria-label="{t['close']}">×</button>
  <img id="viewer-image" alt="" draggable="false" />
  <div class="hint">{t['viewer_hint']}</div>
</div>
<script>{SCRIPT.replace('SIZE', t['size'])}</script>
</body>
</html>
"""


def sitemap():
    links = "\n".join(f'    <xhtml:link rel="alternate" hreflang="{code}" href="{url(code)}"/>' for code in PAGES)
    links += f'\n    <xhtml:link rel="alternate" hreflang="x-default" href="{url("en")}"/>'
    entries = "\n".join(f"  <url>\n    <loc>{url(code)}</loc>\n    <lastmod>{UPDATED}</lastmod>\n{links}\n  </url>" for code in PAGES)
    return f'<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n{entries}\n</urlset>\n'


MANIFEST = json.dumps({
    "name": "HotA MCP",
    "short_name": "HotA MCP",
    "start_url": "./",
    "display": "browser",
    "background_color": "#140c06",
    "theme_color": "#30180c",
    "icons": [
        {"src": "icon-192.png", "sizes": "192x192", "type": "image/png"},
        {"src": "icon-512.png", "sizes": "512x512", "type": "image/png"},
    ],
}, indent=2) + "\n"


LLMS = f"""# HotA MCP

> An MCP server that lets an AI agent play Heroes of Might and Magic III: Horn of the Abyss (HotA 1.8.1 + HD Mod, Windows) like a human player. It reads the game's screens as structure and presses the game's own buttons with window events — no OCR, screenshots, mouse hijacking or internal game commands. A whole game: main menu, scenario setup, hotseat, save/load, adventure map, towns, recruiting, battles, sieges, level-ups, the score screen.

For an agent given this repository: the user installs the latest release (it needs their own copy of the game), you connect to the server over stdio, read the rules it sends on connect and the skill below, and start with `start_game` or `observe`. One action per call; every action takes the `revision` of the observation it was decided on and an `operationId`.

## Connect

- stdio: `HotaMcp.exe --stdio` from the install folder (default `%LOCALAPPDATA%\\Programs\\HotaMcp\\app\\HotaMcp.exe`)
- Claude Code: `claude mcp add hota -- "%LOCALAPPDATA%\\Programs\\HotaMcp\\app\\HotaMcp.exe" --stdio`
- Auto-combat can take up to 3 minutes: raise the client's tool call timeout (Codex: `tool_timeout_sec = 200`)
- Hotseat: every `observe` says whose turn it is («Ходит <цвет>») and lists the scenario's participants; the agent picks its own colour and waits with `wait_for_turn` and that colour

## Docs

- [Agent skill](https://raw.githubusercontent.com/timoncool/hota-mcp/main/skills/hota-player/SKILL.md): how to play through the bridge, step by step (Russian)
- [Tools](https://github.com/timoncool/hota-mcp/blob/main/docs/knowledge/agent/01-tools.md): all 31 tools
- [README (Russian)](https://github.com/timoncool/hota-mcp#readme), [README (English)](https://github.com/timoncool/hota-mcp/blob/main/README_EN.md)
- [Latest release]({RELEASE}): the installer
- [Project page]({SITE})

## Optional

- [Developer guide](https://github.com/timoncool/hota-mcp/blob/main/docs/DEVELOPING.md): code map and how to map a new game screen
- [CHANGELOG](https://github.com/timoncool/hota-mcp/blob/main/CHANGELOG.md)
"""


def write(name, text):
    (DOCS / name).write_text(text, encoding="utf-8", newline="\n")


write("index.html", page("ru"))
write("en.html", page("en"))
write("sitemap.xml", sitemap())
write("robots.txt", f"User-agent: *\nAllow: /\n\nSitemap: {SITE}sitemap.xml\n")
write("llms.txt", LLMS)
write("site.webmanifest", MANIFEST)
write(".nojekyll", "")
print("site written to", DOCS)
