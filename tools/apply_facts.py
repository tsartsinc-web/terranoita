"""Apply facts read from the player's data.wak (tncli facts) to the design sheets.

  dotnet run --project src/Terranoita.Cli -- facts "<Noita folder>" design/sheets/enemies.json build/noita_facts.json
  python tools/apply_facts.py build/noita_facts.json [--stage 1a]

For each enemy in scope it fills and verifies: noita_entity, sprite, hitbox, noita_hp (data.wak wins over the wiki;
differences are printed), and for its attacks/projectiles the timings, ranges, counts and projectile numbers.
A cell is only marked verified when its value came from data.wak. Anything it cannot match is printed and left
unverified for a person (or agent) to resolve by reading the files with `tncli wak-cat`.
"""
import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGES = ["1a", "1b", "1c"]
PIXEL_SCALE = 2.0      # 1 Noita pixel = 2 Terraria world pixels
TILE = 16.0
FPS = 60.0


def load(d, name):
    with open(os.path.join(d, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def save(d, s):
    with open(os.path.join(d, s["sheet"] + ".json"), "w", encoding="utf-8") as f:
        json.dump(s, f, ensure_ascii=False, indent=1)
        f.write("\n")


def verify(row, col, value):
    row[col] = value
    row.setdefault("_unverified", {}).pop(col, None)


def basename(p):
    return os.path.splitext(os.path.basename(p or ""))[0].lower()


def tokens(s):
    return set(t for t in re.split(r"[^a-z0-9]+", s.lower()) if t and t not in ("shot", "projectile", "weak"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("facts")
    ap.add_argument("--stage", choices=STAGES, default="1c", help="apply to rows up to this stage (default: all)")
    ap.add_argument("--sheets", default=os.path.join(ROOT, "design", "sheets"))
    args = ap.parse_args()
    limit = STAGES.index(args.stage)

    facts = json.load(open(args.facts, encoding="utf-8"))
    enemies, attacks, projectiles = (load(args.sheets, n) for n in ("enemies", "attacks", "projectiles"))
    attack_by_id = {a["id"]: a for a in attacks["rows"]}
    proj_by_id = {p["id"]: p for p in projectiles["rows"]}
    notes = []

    for e in enemies["rows"]:
        if STAGES.index(e["stage"]) > limit:
            continue
        f = facts.get(e["id"])
        if not f or "error" in f:
            notes.append("%s: %s" % (e["id"], f.get("error") if f else "no facts"))
            continue
        verify(e, "noita_entity", f["entity"])
        if f.get("sprite") and f.get("sprite_image_exists"):
            verify(e, "sprite", f["sprite"])
        else:
            notes.append("%s: sprite %r not usable (image %r)" % (e["id"], f.get("sprite"), f.get("sprite_image")))
        if f.get("hitbox_noita_px") and min(f["hitbox_noita_px"]) > 0:
            verify(e, "hitbox", [int(round(v * PIXEL_SCALE)) for v in f["hitbox_noita_px"]])
        if f.get("display_hp") is not None:
            if e.get("noita_hp") is not None and abs(e["noita_hp"] - f["display_hp"]) > 0.01:
                notes.append("%s: hp wiki %s, data.wak %s (using data.wak)" % (e["id"], e["noita_hp"], f["display_hp"]))
            verify(e, "noita_hp", f["display_hp"])
        if f.get("name_key"):
            verify(e, "name_key", f["name_key"])
        dm = f.get("damage_multipliers") or {}
        if dm and e.get("dmg_mult"):
            for k in e["dmg_mult"]:
                if k in dm:
                    e["dmg_mult"][k] = dm[k]
            if all(k in dm for k in e["dmg_mult"]):
                e["_unverified"].pop("dmg_mult", None)

        # melee timing
        for aid in e["attacks"]:
            a = attack_by_id[aid]
            if a["kind"] == "melee" and f.get("melee_frames_between"):
                verify(a, "cooldown_frames", int(f["melee_frames_between"]))
                if f.get("melee_max_distance_px"):
                    verify(a, "range_tiles", round(f["melee_max_distance_px"] * PIXEL_SCALE / TILE, 2))

        # ranged: match sheet projectile attacks to data.wak ranged attacks
        ranged = list(f.get("ranged") or [])
        proj_attacks = [attack_by_id[a] for a in e["attacks"] if attack_by_id[a]["kind"] in ("projectile", "retaliate")]
        pairs = []
        if len(ranged) == 1 and len(proj_attacks) == 1:
            pairs = [(proj_attacks[0], ranged[0])]
        else:
            free = list(ranged)
            for a in proj_attacks:
                best, score = None, 0
                for r in free:
                    s = len(tokens(a["projectile"]) & tokens(basename(r["entity_file"])))
                    if s > score:
                        best, score = r, s
                if best:
                    pairs.append((a, best))
                    free.remove(best)
            unmatched = [a["id"] for a in proj_attacks if a not in [p[0] for p in pairs]]
            if unmatched or free:
                notes.append("%s: could not match attacks %s to data.wak files %s" % (
                    e["id"], unmatched, [r["entity_file"] for r in free]))
        for a, r in pairs:
            if r.get("frames_between"):
                verify(a, "cooldown_frames", int(r["frames_between"]))
            if r.get("max_distance_px"):
                verify(a, "range_tiles", round(r["max_distance_px"] * PIXEL_SCALE / TILE, 2))
            if r.get("count_min") and r.get("count_max"):
                verify(a, "count", [int(r["count_min"]), int(r["count_max"])])
            elif r.get("count_min") is None and r.get("count_max") is None and a["count"] == [1, 1]:
                verify(a, "count", [1, 1])  # attributes absent: Noita's default of one projectile per shot
            p = proj_by_id.get(a["projectile"])
            pf = r.get("projectile")
            if not p or not pf:
                notes.append("%s: no projectile facts for %s (%s)" % (e["id"], a["id"], r.get("projectile_error", "")))
                continue
            verify(p, "noita_file", r["entity_file"])
            if pf.get("sprite"):
                verify(p, "sprite", pf["sprite"])
            if pf.get("speed_min") is not None:
                speed = (pf["speed_min"] + (pf.get("speed_max") or pf["speed_min"])) / 2.0
                verify(p, "speed", round(speed * PIXEL_SCALE / FPS, 3))
            verify(p, "gravity", round((pf.get("gravity_y") or 0.0) * PIXEL_SCALE / (FPS * FPS), 4))
            if pf.get("lifetime_frames") is not None:
                verify(p, "lifetime_frames", int(pf["lifetime_frames"]))
            verify(p, "explosion_radius", round((pf.get("explosion_radius_px") or 0.0) * PIXEL_SCALE / TILE, 2))

    for s in (enemies, attacks, projectiles):
        save(args.sheets, s)
    print("applied facts up to stage %s" % args.stage)
    for n in notes:
        print("  NOTE " + n)
    print("Run: python tools/preflight.py --gate %s" % args.stage)


if __name__ == "__main__":
    sys.exit(main())
