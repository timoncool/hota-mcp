"""Documentation linter for the knowledge corpus.

Checks what the September 2026 agent-documentation practice asks for: a single H1 per file, a
metadata line (type, layer, source, updated), resolvable local links, and a size budget so no file
degenerates into an unreadable blob. Warnings are advisory, errors fail the run.

Run: python build/check-docs.py
"""
import os
import re
import sys

DOCS = 'docs'
MAX_BYTES = 300_000
META_KEYS = ('type', 'layer', 'source', 'updated')
LINK = re.compile(r'\[[^\]]+\]\(([^)#]+\.md)\)')


def files():
    for base, _, names in os.walk(DOCS):
        for name in names:
            if name.endswith('.md'):
                yield os.path.join(base, name)
    for base, _, names in os.walk('skills'):
        for name in names:
            if name == 'SKILL.md':
                yield os.path.join(base, name)


def main():
    errors, warnings, checked = [], [], 0
    for path in sorted(files()):
        checked += 1
        text = open(path, encoding='utf-8').read()
        rel = path.replace('\\', '/')
        if not re.search(r'^# ', text, re.M):
            errors.append(f'{rel}: нет заголовка H1')
        if rel.startswith('docs/'):
            head = text[:1500]
            has_front = text.startswith('---') and '\n---' in text[:1500]
            yaml_keys = ('type:', 'layer:', 'updated:')
            missing = [k for k in yaml_keys if k not in head] if has_front else [k for k in META_KEYS if f'**{k}:**' not in head]
            if missing and 'README' not in rel:
                warnings.append(f'{rel}: в метаданных нет {", ".join(missing)}')
            if len(text.encode('utf-8')) > MAX_BYTES:
                warnings.append(f'{rel}: файл больше {MAX_BYTES // 1000} КБ, стоит разрезать по разделам')
            for target in LINK.findall(text):
                if target.startswith(('http', 'mailto')):
                    continue
                resolved = os.path.normpath(os.path.join(os.path.dirname(path), target))
                if not os.path.exists(resolved):
                    warnings.append(f'{rel}: ссылка не найдена -> {target}')
    print(f'проверено файлов: {checked}')
    for w in warnings:
        print('WARN ', w)
    for e in errors:
        print('ERROR', e)
    print(f'итог: ошибок {len(errors)}, предупреждений {len(warnings)}')
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main())
