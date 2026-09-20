"""Generate docs/knowledge/agent/01-tools.md from the live MCP surface.

Reads tools/list, resources/list and resources/templates/list from the running bridge over the MCP
endpoint and writes a markdown table, so the agent can always ask "what tools do I have" from its
own documentation instead of guessing. Run: python build/gen-tools-doc.py
"""
import json
import os
import urllib.request

BASE = 'http://127.0.0.1:18773/mcp'
TOKEN_FILE = os.path.expandvars(r'%LOCALAPPDATA%\HotaMcp\connection.token')


def rpc(body, sid=None):
    token = open(TOKEN_FILE).read().strip()
    headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json',
               'Accept': 'application/json, text/event-stream'}
    if sid:
        headers['Mcp-Session-Id'] = sid
    req = urllib.request.Request(BASE, data=json.dumps(body).encode(), headers=headers)
    resp = urllib.request.urlopen(req, timeout=30)
    raw = resp.read().decode('utf-8', 'replace')
    data = None
    for line in raw.splitlines():
        if line.startswith('data:'):
            data = json.loads(line[5:].strip())
    return data, resp.headers.get('Mcp-Session-Id')


def main():
    init, sid = rpc({'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {
        'protocolVersion': '2024-11-05', 'capabilities': {}, 'clientInfo': {'name': 'gen-tools-doc', 'version': '1'}}})
    rpc({'jsonrpc': '2.0', 'method': 'notifications/initialized'}, sid)
    tools, _ = rpc({'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list', 'params': {}}, sid)
    resources, _ = rpc({'jsonrpc': '2.0', 'id': 3, 'method': 'resources/list', 'params': {}}, sid)
    templates, _ = rpc({'jsonrpc': '2.0', 'id': 4, 'method': 'resources/templates/list', 'params': {}}, sid)
    lines = ['# Инструменты моста (сгенерировано из живого MCP)',
             '',
             'Файл собран командой `python build/gen-tools-doc.py`: список берётся прямо у работающего '
             'моста (MCP `tools/list`, `resources/list`), поэтому всегда соответствует текущей сборке.',
             '', '## Инструменты', '', '| Инструмент | Что делает |', '| --- | --- |']
    for tool in sorted(tools['result']['tools'], key=lambda t: t['name']):
        desc = ' '.join(tool.get('description', '').split())
        lines.append(f"| `{tool['name']}` | {desc} |")
    lines += ['', '## Ресурсы', '']
    for res in resources['result'].get('resources', []):
        lines.append(f"- `{res['uri']}` — {res.get('title') or res.get('name')} ({res.get('mimeType')})")
    for tpl in templates['result'].get('resourceTemplates', []):
        lines.append(f"- `{tpl['uriTemplate']}` — {tpl.get('title') or tpl.get('name')} ({tpl.get('mimeType')})")
    lines += ['', '## Игровые действия',
              '',
              'Действия внутри экранов игры не перечислены здесь: они приходят в `observe` в списке `actions` '
              'с русским описанием и зависят от текущего экрана (карта, город, бой, диалог). Клик по действию — '
              '`click_ui` с ключом из этого списка и текущей `revision`.',
              '']
    out = os.path.join('docs', 'knowledge', 'agent', '01-tools.md')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines))
    print('wrote', out, len(lines), 'lines,', len(tools['result']['tools']), 'tools')


if __name__ == '__main__':
    main()
