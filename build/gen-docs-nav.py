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
    ('manual', 'Официальная документация игры', 'Мануал Heroes III и файл справки из установки игры'),
    ('hota', 'Официальная документация Horn of the Abyss', 'Что дополнение HotA добавляет и меняет, по официальной странице'),
    ('core', 'Правила игры для агента', 'Выжимка правил: календарь, движение, города, герои, магия, бой'),
    ('playbooks', 'Сценарии работы через мост', 'Пошаговые how-to: сохранение и загрузка, город, ход, бой'),
    ('agent', 'Материалы для агента', 'Как играть через мост, игровой цикл, список инструментов'),
    ('sources', 'Исходники и заметки', 'Разбор источников и заметки по алгоритмам'),
]
TYPE_HINTS = [
    ('playbooks', 'how-to'),
    ('agent/01-tools', 'reference'),
    ('manual', 'reference'),
    ('hota/0', 'reference'),
    ('core', 'reference'),
    ('controls-hotkeys', 'reference'),
    ('sources', 'explanation'),
]


def doc_type(rel):
    for key, value in TYPE_HINTS:
        if key in rel.replace('\\', '/'):
            return value
    return 'reference'


def hlp_blocks():
    raw = open(r'G:\HoMM 3 Complete\HEROES3.HLP', 'rb').read()
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
    path = os.path.join(ROOT, 'manual', 'h3-help-ru-hlp-extract.md')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    return path, part, len(text)


def read_meta(path):
    """Return (title, meta line, first real paragraph) of a markdown document."""
    with open(path, encoding='utf-8') as f:
        lines = f.read().splitlines()
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
        files = sorted(f for f in os.listdir(base) if f.endswith('.md') and f.lower() != 'readme.md')
        rows = []
        for name in files:
            rel = f'{folder}/{name}'
            title, _, summary = read_meta(os.path.join(base, name))
            rows.append(f'| `{name}` | {doc_type(rel)} | {title or name} | {summary} |')
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
        files = [f for f in os.listdir(os.path.join(ROOT, folder)) if f.endswith('.md') and f.lower() != 'readme.md']
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
    help_path, parts, size = write_help()
    tocs = write_branch_tocs()
    index, full_size = write_llms()
    print(f'help: {help_path} parts={parts} chars={size}')
    for t in tocs:
        print('toc:', t)
    print(f'llms.txt: {index}')
    print(f'llms-full.txt: {full_size} bytes')
