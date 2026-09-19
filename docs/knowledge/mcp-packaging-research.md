---
id: mcp-packaging-research
title: Offline MCP packaging for Heroes III and HotA knowledge
topics: [mcp, resources, resource-templates, tools, prompts, packaging, offline]
version: 2026-09-19
sources:
  - https://modelcontextprotocol.io/specification/2025-11-25/server/resources
  - https://modelcontextprotocol.io/specification/2025-11-25/server/tools
  - https://modelcontextprotocol.io/specification/2025-11-25/server/prompts
  - https://modelcontextprotocol.io/specification/2025-11-25/basic
  - https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.2.0
  - https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerResourceAttribute.cs
  - https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerPromptAttribute.cs
  - https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerToolAttribute.cs
  - https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/v2.2.0/src/ModelContextProtocol/McpServerBuilderExtensions.cs
  - https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/resources/resources.md
  - https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/prompts/prompts.md
  - https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tools/tools.md
status: researched
---

# Проверка

Проверено 2026-09-19. Проект действительно использует `ModelContextProtocol.AspNetCore` 2.2.0; NuGet lock-файл разрешает `ModelContextProtocol`, `ModelContextProtocol.Core` и ASP.NET Core пакет на 2.2.0. Текущие утверждения ниже разделяют обязательный wire protocol от практической рекомендации для HotA bridge.

## Что требует MCP

Resources — application-driven context. Сервер объявляет capability `resources` и может включать `subscribe`/`listChanged`; наличие ресурса не требует автоматической вставки его содержимого в каждый `observe`. Клиент запрашивает:

- `resources/list` с опциональным opaque `cursor`, ответом `resources` и `nextCursor`;
- `resources/templates/list` с `resourceTemplates` и `nextCursor`;
- `resources/read` с URI, ответом `contents` (text или blob).

В ресурсе протокол предусматривает `uri`, `name`, опциональные `title`, `description`, `mimeType`, `size` и `_meta` согласно текущей schema. Ресурсный URI может использовать собственную схему. `resourceTemplates` используют URI template; спецификация оставляет реализацию параметров и autocomplete серверу/клиенту.

Tools — callable functions. Сервер объявляет capability `tools`; `tools/list` также использует пагинацию. Tool имеет имя, описание, `inputSchema`, а при необходимости `outputSchema`; результат может содержать `content`, `structuredContent`, `isError` и `_meta`. Ошибки внутри выполнения рекомендуется возвращать как tool result с `isError`, а protocol errors оставлять для ошибок самого MCP запроса.

Prompts — reusable templates, запрашиваемые клиентом через `prompts/list` и `prompts/get`; список prompts также пагинируется. Prompt возвращает сообщения с ролью и content; это явный шаблон запроса, а не скрытая системная память и не замена `SKILL.md` хоста.

Все cursors непрозрачны: клиент не должен вычислять следующий cursor. Сервер обязан ограничивать размер страниц, но спецификация не задаёт число элементов или токенов модели. Поэтому лимиты контекста и дозирование чтения — прикладная политика проекта.

## Проверенные API C# SDK 2.2.0

Тег `v2.2.0` у официального `modelcontextprotocol/csharp-sdk` указывает commit `6fa3825`. В исходниках этого тега подтверждены следующие атрибуты и регистрация:

- `[McpServerResourceType]` на типе и `[McpServerResource(UriTemplate = ..., Name = ..., MimeType = ...)]` на методе. Фиксированный URI попадает в resources list; URI с параметрами — в resource templates list. Метод может вернуть `string`, `ResourceContents`, `TextResourceContents`, `IEnumerable<...>` или `ReadResourceResult` по правилам SDK.
- `[McpServerPromptType]` и `[McpServerPrompt]`; prompt с аргументами документируется `System.ComponentModel.DescriptionAttribute`, метод может возвращать `string`, `PromptMessage`, `IEnumerable<PromptMessage>`, `ChatMessage` или коллекцию `ChatMessage`.
- `[McpServerToolType]` и `[McpServerTool]`; параметры метода десериализуются из JSON и описываются `[Description]`. `CancellationToken`, `IServiceProvider`, `McpServer`, progress и DI-параметры не входят в пользовательскую input schema, когда SDK распознаёт их как специальные.
- Регистрация в официальных примерах: `.WithResources<T>()`, `.WithPrompts<T>()`, `.WithTools<T>()` после `AddMcpServer()`. Точные названия и примеры приведены в SDK docs resources/prompts/tools.
- Resource-метод может вернуть `TextResourceContents`; binary API использует `BlobResourceContents.FromBytes`. Для нашего Markdown нужен текстовый ресурс с `MimeType = "text/markdown"`.
- Tool можно пометить `UseStructuredContent = true`; в атрибуте 2.2.0 также есть `ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`, `Title`, `OutputSchemaType`. Это metadata для discovery/клиента, а не автоматическая бизнес-валидация: исходник SDK прямо требует валидировать аргументы внутри tool.

Исходник SDK 2.2.0 не подтверждает отдельный встроенный полнотекстовый search API по ресурсам. Поэтому поиск знаний должен быть отдельным read-only tool с компактным результатом, а чтение документа — resource template или отдельным tool.

## Минимальная офлайн поставка

Рекомендация для проекта: поставлять Markdown из `docs/knowledge/core`, `docs/knowledge/playbooks` и `docs/knowledge/sources` рядом с self-contained publish. Сервер при старте строит индекс только из разрешённого каталога, проверяет frontmatter (`id`, `title`, `topics`, `version`, `sources`, `status`) и не читает произвольные пути клиента.

Для SDK/MSBuild нужно явно включить файлы в проектную выдачу. Исследовательская схема для `src/HotaMcp/HotaMcp.csproj` (код пока не добавлять): `Content Include="..\\..\\docs\\knowledge\\**\\*.md"`, `CopyToOutputDirectory=PreserveNewest`, `CopyToPublishDirectory=PreserveNewest`. При publish self-contained `.NET` документы должны лежать в предсказуемом подкаталоге, например `knowledge/`, а URI должны быть стабильными (`hota://knowledge/{id}`), без абсолютных Windows-путей.

Рекомендуемый минимальный интерфейс:

1. `resources/list`: только компактные карточки документов и metadata, с pagination.
2. `resources/templates/list`: `hota://knowledge/{id}` и, при необходимости, `hota://knowledge/topic/{topic}`.
3. `resources/read`: читать один документ или секцию по ID; сервер применяет лимит символов/байтов и возвращает явный `truncated` в metadata прикладного результата.
4. Read-only `knowledge_search(query, topics?, version?, cursor?)`: возвращает ID, title, короткие совпадения и source/status, без полного текста.
5. Read-only `knowledge_read(id, section?, max_chars?)`: дозированно выдаёт Markdown. Агент сам решает, когда продолжить чтение.

Search/read — рекомендации проекта, не новые обязательные MCP methods. Если поиск ещё не реализован, capability discovery должен это показать; нельзя выдавать resource list за полнотекстовый поиск.

## Версии, источники и проверки

Frontmatter каждого документа хранит версию правил/сборки, дату проверки, первичные источники и status (`verified`, `corroborating`, `unverified`, `stale`). Для HotA и базового Heroes III отдельные source IDs обязательны. При неизвестной сборке сервер возвращает metadata и `status: stale/uncertain`, а не совет или silently-updated rule.

Практическая рекомендация: включить SHA-256 каждого поставленного Markdown в индекс сборки и проверять его при старте. Это не требование MCP, а контроль целостности дистрибутива. Миграция версии знаний должна менять version/status явно; старый документ можно оставить читаемым для replay, но не выдавать как актуальный без проверки.

## Resources, prompts и SKILL.md

Resource — справочный документ, который клиент или агент читает явно. Prompt — вызываемый шаблон диалога с аргументами и сообщениями. Harness-specific `SKILL.md` — инструкция конкретного запуска/хоста агента; она не является MCP protocol resource и не должна маскироваться под правила игры. Внутри дистрибутива можно поставлять отдельный prompt «как читать справку», но он не заменяет skill и не должен автоматически добавляться к каждому наблюдению.

`observe` возвращает только динамическое состояние и capability/uncertainty, доступные назначенной стороне. Knowledge resource может быть вызван отдельно; его чтение не даёт права раскрывать скрытое состояние карты или соперника.

## Продолжение игрового цикла

После чтения справки агент самостоятельно продолжает выполнение цели: новая развилка означает необходимость нового решения и свежего observation, а не остановку всей работы. Остановка допустима только после выполнения цели, недоступного capability, устойчивого uncertain/stale состояния или необходимости решения пользователя. Пакет знаний не должен навязывать стратегию или добавлять советы в `observe`.

## Пробелы и ограничения

Официальная спецификация не задаёт локальную файловую упаковку, Markdown frontmatter, индекс поиска, токен-бюджет модели, размер секций, stale policy или агентский loop. Это проектные рекомендации, которые должны быть протестированы на выбранном MCP-клиенте. SDK 2.2.0 предоставляет building blocks и attribute registration, но не реализует готовый offline knowledge search/index.
