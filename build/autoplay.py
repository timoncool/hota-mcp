#!/usr/bin/env python3
# Autonomous game driver: plays Heroes of Might and Magic III HotA via the local MCP bridge.
# Goal: win the scenario. Loop per game-day:
#   for each own hero: spend movement on reachable targets (move_to) / reveal map (move_to_tile)
#   then end turn. Record every day to docs/knowledge/sessions/auto-play.log
# All actions go through the bridge HTTP API only (no pixel clicks, no screenshots).

import json
import time
import uuid
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from hotacli import call  # noqa: E402  (reads the token, talks to http://127.0.0.1:18773)

LOG = HERE.parent / "docs" / "knowledge" / "sessions" / "auto-play.log"
LOG.parent.mkdir(parents=True, exist_ok=True)


def log(msg: str) -> None:
    line = time.strftime("%H:%M:%S") + " " + msg
    print(line, flush=True)
    with LOG.open("a", encoding="utf-8") as fh:
        fh.write(line + "\n")


def observe():
    return call("observe")


def resolve_dialogs(max_steps=10):
    """Click through any modal dialogs back to the adventure map."""
    for _ in range(max_steps):
        o = observe()
        s = o.get("screen")
        if s == "adventure":
            return o
        el = {"hero_screen": "hero:close", "message": "message:accept"}.get(s)
        if s == "exchange":
            ok = [e for e in (o.get("elements") or []) if e.get("asset") == "iokay.def"]
            el = ok[0]["key"] if ok else None
        if not el:
            return o
        call("click", {"operationId": uuid.uuid4().hex, "revision": o["revision"], "element": el})
        time.sleep(1.1)
    return observe()


def ensure_adventure():
    return resolve_dialogs()


def hero_state():
    o = ensure_adventure()
    return o, (o.get("hero") or {})


def select_hero_if_needed():
    # NEVER open the hero profile screen: hero:select opens it (user: "NOT the profile again").
    # The active hero is already the one reading `observe.hero`; move_to/move-tile work right away.
    return False


def nearby_targets():
    for _ in range(3):
        ts = call("nearby").get("targets") or []
        if ts:
            return ts
        time.sleep(1.2)
    return []


def interesting(t):
    """Score a target: prefer reachable, non-creature, then by kind value.
    IMPORTANT: never pick the home town (value 8) - `move_to` on our own town
    is not a move, it just opens the town; and never count not_available routes."""
    kind = (t.get("kind") or "").lower()
    if kind in ("town", "creatures", "creature_bank"):
        return (-1, -1)
    state = (t.get("route") or {}).get("state")
    if state not in ("reachable", "partially_reachable"):
        return (-1, -1)


def move_to(target):
    o, h = hero_state()
    if not h.get("position"):
        return False
    r = call("move", {"operationId": uuid.uuid4().hex, "revision": o["revision"], "targetId": target["id"]})
    st = r.get("status")
    time.sleep(1.4)
    o2 = resolve_dialogs()
    h2 = o2.get("hero") or {}
    log(f"  move_to {target.get('kind')} ({target.get('x')},{target.get('y')}) -> {st} | {h2.get('position')} mv {h2.get('movement')}")
    return st in ("completed", "moved", "ok")


def walk_day(rounds=8):
    """Spend the active hero's movement on the best reachable targets."""
    for _ in range(rounds):
        o, h = hero_state()
        if not h.get("movement") or h["movement"] < 100:
            return
        ts = nearby_targets()
        if not ts:
            log("  no targets visible; probing the map")
            reveal(step=3)
            continue
        pick = sorted(ts, key=interesting, reverse=True)[0]
        if interesting(pick)[0] < 0:
            log("  nothing reachable here; probing the map once")
            reveal(step=4)
            continue
        move_to(pick)


def reveal(step=3):
    """Move into the fog to reveal the map (move_to_tile)."""
    o, h = hero_state()
    if not h.get("position") or not h.get("movement"):
        return
    hx, hy, _ = h["position"]
    direction = (1, 0) if (hx // step) % 2 == 0 else (0, 1)
    tx, ty = hx + direction[0] * step, hy + direction[1] * step
    call("move-tile", {"operationId": uuid.uuid4().hex, "revision": o["revision"], "x": tx, "y": ty})
    time.sleep(1.4)
    resolve_dialogs()


def end_turn():
    o = ensure_adventure()
    call("click", {"operationId": uuid.uuid4().hex, "revision": o["revision"], "element": "turn:end"})
    time.sleep(1.2)
    for _ in range(6):
        o = observe()
        acts = [a["key"] for a in o.get("actions", [])]
        if "message:confirm" in acts:
            call("click", {"operationId": uuid.uuid4().hex, "revision": o["revision"], "element": "message:confirm"})
            time.sleep(1.4)
            continue
        break
    return ensure_adventure()


def main(max_days=30):
    log("=== autonomous session start ===")
    for day in range(max_days):
        o = ensure_adventure()
        date = o.get("date")
        res = o.get("resources")
        log(f"--- day {date} | gold {res[6] if res and len(res) > 6 else '?'} ---")
        for _ in range(4):  # up to 4 heroes (main, collector, ...)
            walk_day()
            o, h = hero_state()
            acts = [a["key"] for a in o.get("actions", [])]
            if False:  # hero switching only via the H key if needed; never open profiles
                pass
            break
        log("  ending turn")
        end_turn()
        o = observe()
        if o.get("screen") in ("main_menu", "defeat", "victory"):
            log(f"GAME OVER screen={o.get('screen')}")
            break
    log("=== autonomous session end ===")


if __name__ == "__main__":
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 30)
