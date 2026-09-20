"""Normalise corpus documents: guarantee an H1 title and a metadata block.

Two metadata styles are accepted in this repository:
  * YAML front matter (id/title/type/layer/updated/...) - used by the core, hota and sources trees;
  * a blockquote line (`> **type:** ... **layer:** ... **source:** ... **updated:** ...`) - used by
    the newer agent-facing documents.
The fixer adds whatever is missing without touching the body: an H1 taken from `title:` (or the file
name) and the missing front-matter keys.

Run: python build/fix-docs.py
"""
import datetime
import os
import re

STAMP = datetime.date.today().isoformat()
LAYER = {
    'manual': ('official', 'Справка и мануал игры'),
    'hota': ('official', 'Официальная документация HotA'),
    'core': ('agent', 'Правила игры, выжимка для агента по мануалу'),
    'playbooks': ('agent', 'Проверенные сценарии работы через мост'),
    'agent': ('agent', 'Материалы для агента'),
    'sources': ('agent', 'Разбор источников'),
}


def doc_type(rel):
    if 'playbooks' in rel:
        return 'how-to'
    if 'sources' in rel:
        return 'explanation'
    return 'reference'


def humanise(name):
    base = re.sub(r'^\d+[-_]', '', name)
    base = re.sub(r'\.md$', '', base).replace('-', ' ').replace('_', ' ')
    return base[:1].upper() + base[1:]


def process(path):
    text = open(path, encoding='utf-8').read()
    rel = path.replace('\\', '/')
    folder = rel.split('/')[2] if len(rel.split('/')) > 2 else ''
    layer, source = LAYER.get(folder, ('agent', 'Внутренний документ репозитория'))
    changed = []
    if text.startswith('---'):
        end = text.find('\n---', 3)
        if end != -1:
            head, body = text[:end + 4], text[end + 4:]
            title_match = re.search(r'^title:\s*"?(.*?)"?\s*$', head, re.M)
            title = title_match.group(1) if title_match else humanise(os.path.basename(path))
            add = []
            if 'type:' not in head:
                add.append(f'type: {doc_type(rel)}')
            if 'layer:' not in head:
                add.append(f'layer: {layer}')
            if 'updated:' not in head:
                add.append(f'updated: "{STAMP}"')
            if add:
                head = head[:end] + '\n' + '\n'.join(add) + head[end:]
                changed.append('front-matter+' + ','.join(a.split(':')[0] for a in add))
            if not re.search(r'^# ', body, re.M):
                body = f'\n# {title}\n' + body
                changed.append('H1')
            text = head + body
    else:
        if not re.search(r'^# ', text, re.M):
            text = f'# {humanise(os.path.basename(path))}\n' + text
            changed.append('H1')
        head = text[:1200]
        if '**type:**' not in head:
            marker = text.find('\n\n')
            block = (f'\n> **type:** {doc_type(rel)} · **layer:** {layer} · **source:** {source} '
                     f'· **updated:** {STAMP}\n')
            text = text[:marker] + block + text[marker:]
            changed.append('meta-block')
    if changed:
        with open(path, 'w', encoding='utf-8') as f:
            f.write(text)
    return changed


if __name__ == '__main__':
    touched = 0
    for base, _, names in os.walk('docs/knowledge'):
        for name in sorted(names):
            if not name.endswith('.md') or name.lower() == 'readme.md':
                continue
            path = os.path.join(base, name)
            changed = process(path)
            if changed:
                touched += 1
                print('fixed', path, '->', ', '.join(changed))
    print('files touched:', touched)
