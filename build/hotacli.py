#!/usr/bin/env python
# Developer CLI for the local HotA MCP bridge (HTTP). Dev-only tool, not shipped.
import json, sys, os, urllib.request, urllib.error

TOKEN = open(os.path.join(os.environ['LOCALAPPDATA'], 'HotaMcp', 'connection.token')).read().strip()
BASE = 'http://127.0.0.1:18773/bridge/'


def call(route, body=None, timeout=90):
    data = json.dumps({} if body is None else body).encode()
    req = urllib.request.Request(BASE + route, data=data,
                                 headers={'Authorization': 'Bearer ' + TOKEN,
                                          'Content-Type': 'application/json'}, method='POST')
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        return {'http_error': e.code, 'body': e.read().decode()}
    except Exception as e:
        return {'error': str(e)}


def summary(o):
    if not isinstance(o, dict):
        print(json.dumps(o, ensure_ascii=False)[:2000]); return
    if 'observation' in o and isinstance(o['observation'], dict):
        print('STATUS', o.get('status'), '::', o.get('message'))
        o = o['observation']
    out = {k: o.get(k) for k in ('revision', 'screen', 'player', 'date', 'resources') if k in o}
    hero = o.get('hero')
    if isinstance(hero, dict):
        out['hero'] = {k: hero.get(k) for k in ('id', 'name', 'position', 'movement', 'mana', 'armyTypes', 'armyCounts')}
    towns = o.get('towns')
    if isinstance(towns, list):
        out['towns'] = [{k: t.get(k) for k in ('id', 'name', 'garrisonTypes', 'garrisonCounts', 'buildings') if k in t} for t in towns]
    acts = o.get('actions')
    if isinstance(acts, list):
        out['actions'] = [a.get('key') for a in acts]
    print(json.dumps(out, ensure_ascii=False, indent=1))
    if 'elements' in o and isinstance(o['elements'], list):
        print('ELEMENTS:')
        for e in o['elements']:
            print('  ui:%(key)s id=%(id)s int=%(interactive)s rect=%(x)s,%(y)s %(width)sx%(height)s asset=%(asset)s text=%(text)s' % {
                'key': e.get('key'), 'id': e.get('id'), 'interactive': e.get('interactive'),
                'x': e.get('x'), 'y': e.get('y'), 'width': e.get('width'), 'height': e.get('height'),
                'asset': e.get('asset'), 'text': (e.get('text') or '')[:60]})
    for k in ('targets', 'setup', 'combat', 'fields'):
        if k in o:
            print(k.upper(), json.dumps(o[k], ensure_ascii=False)[:3000])


def cmd_observe(args):
    summary(call('observe'))


def cmd_click(args):
    import uuid
    element = args[0]
    if len(args) > 1 and args[1] != 'auto':
        revision = args[1]
    else:
        revision = call('observe').get('revision')
    print('revision', revision)
    r = call('click', {'operationId': uuid.uuid4().hex, 'revision': revision, 'element': element})
    summary(r)


def cmd_nearby(args):
    o = call('nearby'); print(json.dumps(o, ensure_ascii=False)[:4000])


def cmd_move(args):
    o = call('observe'); revision = o.get('revision')
    import uuid
    r = call('move', {'operationId': uuid.uuid4().hex, 'revision': revision, 'targetId': args[0]})
    summary(r)


def cmd_movetile(args):
    o = call('observe'); revision = o.get('revision')
    import uuid
    z = int(args[2]) if len(args) > 2 else 0
    r = call('move-tile', {'operationId': uuid.uuid4().hex, 'revision': revision, 'x': int(args[0]), 'y': int(args[1]), 'z': z})
    summary(r)


def cmd_status(args):
    print(json.dumps(call('status'), ensure_ascii=False, indent=1))


def cmd_raw(args):
    print(json.dumps(call(args[0], json.loads(args[1]) if len(args) > 1 else {}), ensure_ascii=False)[:8000])


def cmd_journal(args):
    print(json.dumps(call('journal', {'limit': int(args[0]) if args else 10}), ensure_ascii=False, indent=1)[:6000])


def cmd_snapshot(args):
    # Developer view: game framebuffer PNG plus the structured observation of the same moment.
    d = call('debug-snapshot', {}, timeout=120)
    if 'capture' in d:
        print('PNG', d['capture'].get('path'), d['capture'].get('width'), 'x', d['capture'].get('height'), d['capture'].get('source'))
        print('OBS', d.get('observationPath'))
    summary(d.get('observation') or d)


def cmd_docs(args):
    # Ask the project documentation (lessons, playbooks, manual, hotkeys) for an answer.
    d = call('docs', {'query': ' '.join(args), 'limit': 3})
    print('sections', d.get('sections'), 'note', d.get('note'))
    for h in d.get('hits', []):
        print('---', h['file'], '|', h['heading'], '| score', h['score'])
        print(h['text'][:900])


COMMANDS = {'observe': cmd_observe, 'click': cmd_click, 'nearby': cmd_nearby, 'move': cmd_move, 'movetile': cmd_movetile,
            'status': cmd_status, 'raw': cmd_raw, 'journal': cmd_journal, 'snapshot': cmd_snapshot, 'docs': cmd_docs}

if __name__ == '__main__':
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        print('usage:', ' | '.join(COMMANDS)); sys.exit(2)
    COMMANDS[sys.argv[1]](sys.argv[2:])
