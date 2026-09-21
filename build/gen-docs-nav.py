"""Build the agent-facing navigation layer of the documentation corpus.

Follows the September 2026 state of the art for agent-readable corpora:
  * llms.txt v2 in the hierarchical form: a root index that links one child index per branch, each
    child saying what it contains (Mintlify's scaled llms.txt pattern).
  * Diataxis-style typing per document (how-to / reference / explanation / tutorial).
  * Per-branch README tables of contents with structured columns.
  * llms-full.txt with the whole corpus concatenated for one-shot reading.

Also splits the in-game help extract (HEROES3.HLP) into headed parts so the bridge can search and
read it section by section.

Run: python build/gen-docs-nav.py
"""
import collections
import datetime
import os
import re

ROOT = 'docs/knowledge'
STAMP = datetime.date.today().isoformat()
BRANCHES = [
    ('agent', 'Как играть через мост', 'С чего начать, реестр экранов, инструменты, пошаговые сценарии'),
    ('rules', 'Как игра считает', 'Формулы урона и движения, экономика города, навыки и магия, охрана банков, объекты карты, ростеры HotA'),
    ('play', 'Как решать', 'Оценка боя, порядок построек, игровые циклы, приоритеты, реестр ситуаций'),
    ('official', 'Официальные источники', 'Мануал Heroes III, справка из установки игры, документация HotA'),
    ('engine', 'Внутренности настоящего exe', 'Адреса и структуры h3hota HD.exe, на которые опирается мост'),
    ('sources', 'Происхождение знаний', 'Разбор источников и заметки по алгоритмам'),
]
TYPE_HINTS = [
    ('agent/00', 'how-to'),
    ('agent/1', 'how-to'),
    ('agent/01-tools', 'reference'),
    ('agent/01-screens', 'reference'),
    ('rules', 'reference'),
    ('play', 'explanation'),
    ('official', 'reference'),
    ('engine', 'reference'),
    ('sources', 'explanation'),
]


def doc_type(rel):
    for key, value in TYPE_HINTS:
        if key in rel.replace('\\', '/'):
            return value
    return 'reference'


HELP_FILE = os.environ.get('HOTA_HELP_FILE', r'G:\HoMM 3 Complete\HEROES3.HLP')


def hlp_blocks():
    raw = open(HELP_FILE, 'rb').read()
    runs = [r.decode('cp1251', 'replace') for r in re.findall(rb'[\x20-\x7e\xc0-\xff]{18,}', raw)]
    blocks, seen = [], set()
    for r in runs:
        t = re.sub(r'\s+', ' ', r).strip()
        if len(t) < 25:
            continue
        letters = sum(ch.isalpha() for ch in t)
        if letters / len(t) < 0.6:
            continue
        if collections.Counter(t).most_common(1)[0][1] / len(t) > 0.25:
            continue
        if t.count(' ') < 3 or t in seen:
            continue
        seen.add(t)
        blocks.append(t)
    return blocks


def anchor(blocks):
    for t in blocks:
        if len(t) > 55 or t[-1] in '.,;:!?)' or t.count(' ') > 6:
            continue
        if re.search(r'\d{3,}', t) or not re.match(r'^[А-ЯЁA-Z]', t):
            continue
        return t
    return None


def write_help():
    blocks = hlp_blocks()
    out = ['# Справка игры Heroes of Might and Magic III (извлечение из HEROES3.HLP)',
           '',
           '> **type:** reference · **layer:** official · **source:** `G:/HoMM 3 Complete/HEROES3.HLP` (русская справка «Полное собрание») '
           f'· **updated:** {STAMP}',
           '',
           'Тексты справки игры, разбитые на части по порядку следования в файле. Ценность для агента: '
           'русские названия существ, навыков, заклинаний и объектов — ровно те, что видны в наблюдениях моста. '
           'Уровень основной справки, без профессиональных тонкостей и скрытых механик.',
           '']
    part = 0
    for start in range(0, len(blocks), 45):
        chunk = blocks[start:start + 45]
        if not chunk:
            continue
        part += 1
        title = anchor(chunk)
        out.append(f'## Часть {part}' + (f': {title}' if title else ''))
        out.append('')
        if title and title in chunk:
            chunk = [b for b in chunk if b != title]
        out += [b + '\n' for b in chunk] + ['']
    text = '\n'.join(out)
    path = os.path.join(ROOT, 'official', 'manual', 'h3-help-ru-hlp-extract.md')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    return path, part, len(text)


def read_meta(path):
    """Return (title, meta line, first real paragraph) of a markdown document."""
    with open(path, encoding='utf-8') as f:
        lines = f.read().splitlines()
    # A YAML front matter block is metadata, not the document's own words: skip it whole,
    # otherwise the table of contents quotes "game_version_scope:" as if it were a summary.
    if lines and lines[0].strip() == '---':
        end = next((i for i in range(1, len(lines)) if lines[i].strip() == '---'), 0)
        lines = lines[end + 1:]
    title, meta, summary = '', '', ''
    for line in lines:
        s = line.strip()
        if not title and s.startswith('# '):
            title = s[2:].strip()
            continue
        if not meta and s.startswith('>'):
            meta = s.lstrip('> ').strip()
            continue
        if not summary:
            if not s or s.startswith(('#', '>', '|', '-', '*')) or re.match(r'^(id|title|source|layer|status|audience|updated|scope|type)\s*:', s, re.I) or len(s) < 25:
                continue
            summary = re.sub(r'\s+', ' ', s)[:180]
    return title, meta, summary


def write_branch_tocs():
    made = []
    for folder, _, _ in BRANCHES:
        base = os.path.join(ROOT, folder)
        if not os.path.isdir(base):
            continue
        files = []
        for where, _, names in os.walk(base):
            for entry in sorted(names):
                if entry.endswith('.md') and entry.lower() != 'readme.md':
                    files.append(os.path.relpath(os.path.join(where, entry), base).replace(os.sep, '/'))
        files.sort()
        rows = []
        for name in files:
            rel = f'{folder}/{name}'
            title, _, summary = read_meta(os.path.join(base, name.replace('/', os.sep)))
            rows.append(f'| [`{name}`]({name}) | {doc_type(rel)} | {title or name} | {summary} |')
        text = [f'# {folder} — оглавление ветки',
                '',
                '> Собрано автоматически командой `python build/gen-docs-nav.py`; не править руками.',
                '', '| Файл | Тип | Заголовок | О чём |', '| --- | --- | --- | --- |'] + rows + ['']
        path = os.path.join(base, 'README.md')
        with open(path, 'w', encoding='utf-8') as f:
            f.write('\n'.join(text))
        made.append(path)
    return made


def write_llms():
    lines = ['# Корпус знаний агента: Heroes III и HotA для MCP-моста', '',
             '> Что доступно агенту и где это лежит. Документы читаются через мост: '
             '`hota_docs` (поиск по корпусу), `hota_docs_read` (чтение файла или раздела), ресурс `hota://docs/index`. '
             f'Корпус собран из официальной документации игры и дополнения плюс материалы для самого агента. Обновлено {STAMP}.', '']
    for folder, title, inside in BRANCHES:
        if not os.path.isdir(os.path.join(ROOT, folder)):
            continue
        files = [f for where, _, names in os.walk(os.path.join(ROOT, folder)) for f in names
                 if f.endswith('.md') and f.lower() != 'readme.md']
        lines.append(f'## {title}')
        lines.append(f'- [{folder}/]({folder}/README.md) — {inside}. Файлов: {len(files)}.')
        lines.append('')
    lines += ['## Отдельные документы', '']
    for name in ('controls-hotkeys.md', 'DESIGN.md', 'README.md'):
        if os.path.exists(os.path.join(ROOT, name)):
            title, _, summary = read_meta(os.path.join(ROOT, name))
            lines.append(f'- [{title or name}]({name}) — {summary}')
    lines += ['', '## Другие формы', '',
              '- [llms-full.txt](llms-full.txt) — весь корпус одним файлом (для чтения целиком).', '']
    index = os.path.join(ROOT, 'llms.txt')
    with open(index, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    full = ['# Корпус знаний агента: полный текст', '', f'> Собрано {STAMP} командой `python build/gen-docs-nav.py`. '
            'Каждый документ начинается со своего пути в репозитории.', '']
    for base, _, files in os.walk(ROOT):
        for name in sorted(f for f in files if f.endswith('.md')):
            path = os.path.join(base, name)
            rel = os.path.relpath(path, ROOT).replace('\\', '/')
            full += ['', f'<!-- ===== {rel} ===== -->', '', open(path, encoding='utf-8').read().rstrip(), '']
    with open(os.path.join(ROOT, 'llms-full.txt'), 'w', encoding='utf-8') as f:
        f.write('\n'.join(full))
    return index, os.path.getsize(os.path.join(ROOT, 'llms-full.txt'))


if __name__ == '__main__':
    if os.path.exists(HELP_FILE):
        help_path, parts, size = write_help()
    else:
        help_path, parts, size = HELP_FILE + ' (не найден, извлечение справки пропущено)', 0, 0
    tocs = write_branch_tocs()
    index, full_size = write_llms()
    print(f'help: {help_path} parts={parts} chars={size}')
    for t in tocs:
        print('toc:', t)
    print(f'llms.txt: {index}')
    print(f'llms-full.txt: {full_size} bytes')
