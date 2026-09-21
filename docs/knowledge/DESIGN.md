---
id: h3.DESIGN
title: Как устроен корпус знаний и почему именно так (дизайн, сентябрь 2026)
game_version_scope: HotA 1.8.x поверх Heroes III Complete
topics: [llms, link, relations, mintlify, blog, scaling-llms-txt, taxis, tutorial]
verification_status: источник: исследование практик 2026 + решения этого репозитория
type: explanation
layer: agent
updated: "2026-09-20"
---
# Как устроен корпус знаний и почему именно так (дизайн, сентябрь 2026)

Документ отвечает на вопрос «на каком софте, в каком формате и по какой иерархии собирается корпус, который агент читает через мост». Ниже — что делают в 2026 году и что из этого применено здесь.

## Что говорят практики (проверено 20.09.2026)

1. **llms.txt v2 (август 2026) — иерархия файлов, а не один список.** Спека: один H1, блок-цитата с сутью, дальше секции ссылок с однострочными описаниями. В v2 добавили link relations для обнаружения. Mintlify переписали свой генератор в «иерархию файлов»: корневой файл ссылается на дочерние индексы веток, а не перечисляет всё сам; на 200 задачах агенты выполнили их на 52% быстрее при 45% меньшем расходе токенов (источник: mintlify.com/blog/scaling-llms-txt).
2. **Diátaxis как таксономия для агентов.** Фреймворк из четырёх типов документов — tutorial, how-to, reference, explanation — снова вышел в топ HN (август 2026): агенты ошибаются там, где документация структурно сломана. Практика: у каждого документа явный тип (источник: byteiota.com/diataxis-…, HN 2 авг 2026).
3. **Схема метаданных и версионирование.** Agent Knowledge Base Specification: документ — запись с метаданными (id, тип, источник, дата), чанкинг по типу контента (семантический для прозы, иерархический для структурных документов), регламент обновления (источник: geodocs.dev/ai-agents/agent-knowledge-base-spec).
4. **«Вычитающий» контекст побеждает.** Отчёт Anthropic 2026 (Agentic Coding) и бенчмарки Sourcegraph: 5 тыс. токенов адресной выдачи обходят 100 тыс. токенов пересказа; структурный поиск поднимает точность. Команды с поддерживаемыми файлами контекста делают задачи на 55% быстрее и на 40% меньше ошибок (источник: heyuan110.com/posts/ai/2026-06-16-context-engineering-2026).
5. **Docs-as-code и проверки.** Стек: markdown + индекс + llms.txt + проверки документации в CI, плюс «документ должен быть разбиваем на чанки и собран вокруг вопроса, который зададут» (источник: literally.dev/resources/docs-that-perform-in-ai-search, kunalganglani.com/blog/agent-readable-documentation-toolchain).
6. **Ресурсы MCP, а не только инструменты.** MCP моделирует читаемые данные как ресурсы (URI), действия — как инструменты; документацию отдают ресурсами и поиском (источник: aiagents.codeguides.io/…/exposing-resources-and-prompts-not-just-tools).

## Что применено здесь

| Практика | Как сделано в репозитории |
| --- | --- |
| Иерархический индекс | `docs/knowledge/llms.txt` — корень: H1, цитата-суть, ссылки на индексы веток с описанием; `llms-full.txt` — весь корпус одним файлом |
| Индекс ветки | `docs/knowledge/<ветка>/README.md` — таблица «файл, тип, заголовок, о чём»; генерируется, руками не правится |
| Таксономия Diátaxis | у каждого документа поле `type:` (how-to у сценариев, reference у справок и правил, explanation у разборов) |
| Метаданные | YAML-заголовок (`id`, `title`, `type`, `layer`, `updated`, `topics`, `verification_status`, `source_urls`) либо блок-цитата с теми же полями у новых документов |
| Мелкие документы по темам | одна тема — один файл; крупные справочники (мануал, справка, документация HotA) публикуются целыми, но поиск режет их по заголовкам на разделы |
| Адресная выдача | `hota_docs` возвращает 2–8 разделов с оценкой и снипетом, `hota_docs_read` дочитывает файл или конкретный заголовок; нормализация по длине не даёт большим справкам перебивать точные короткие разделы |
| Ресурсы MCP | `hota://docs/index` (каталог) и `hota://docs/{path}` (документ), рядом инструменты поиска |
| Docs-as-code | `build/gen-docs-nav.py` собирает индексы и llms.txt, `build/gen-tools-doc.py` — список инструментов из живого MCP, `build/check-docs.py` — линтер (H1, метаданные, ссылки, размер), `build/fix-docs.py` — нормализация метаданных |
| Границы корпуса | только официальная документация игры и дополнения плюс материалы для агента; никаких секретов, профессиональных механик и тонкостей — за деталями агент идёт в интернет сам |

## Регламент обновления
1. Правка документов — обычный коммит в `docs/knowledge/**` (это часть репозитория, не внешняя система).
2. После правок: `python build/gen-docs-nav.py` (индексы и llms.txt) и `python build/check-docs.py` (линтер).
3. Изменение поверхности моста: `python build/gen-tools-doc.py` обновляет список инструментов.
4. Мост перечитывает корпус сам (кэш 10 секунд); пересборка нужна только при изменении кода `DocsIndex`.

## Источники
- llms.txt: spec v2 и грамматики — llmsx.org/reference/spec (проверено 30.08.2026); Mintlify, Scaling llms.txt.
- Diátaxis для агентов — byteiota.com/diataxis-the-documentation-framework-ai-agents-need/.
- Agent Knowledge Base Specification — geodocs.dev/ai-agents/agent-knowledge-base-spec.
- Context engineering 2026 (отчёт Anthropic, бенчмарки Sourcegraph) — heyuan110.com/posts/ai/2026-06-16-context-engineering-2026.
- Agent-ready документация и тулчейн — kunalganglani.com/blog/agent-readable-documentation-toolchain; literally.dev/resources/docs-that-perform-in-ai-search.
- MCP: ресурсы против инструментов — aiagents.codeguides.io/building-mcp-servers-clients/exposing-resources-and-prompts-not-just-tools-from-an-mcp-server.
