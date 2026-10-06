"""What each Noita creature bleeds -> design/sheets/enemy_blood.json (stage 2: blood spills as Noita liquids).
From design/sources/noita_facts.json (tncli facts, PC only): the creature's own DamageModelComponent
blood_material (what a hit spills and a body leaves) and blood_spray_material (the spray).

  python tools/extract_blood.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
facts = json.load(open(os.path.join(ROOT, "design", "sources", "noita_facts.json"), encoding="utf-8"))
enemies = json.load(open(os.path.join(ROOT, "design", "sheets", "enemies.json"), encoding="utf-8"))["rows"]

rows = []
missing = []
for e in enemies:
    f = facts.get(e["id"])
    dm = None
    for c in (f or {}).get("components", []):
        if c.get("component") == "DamageModelComponent":
            dm = c.get("attrs", {})
            break
    if dm is None:
        missing.append(e["id"])
    rows.append({"id": e["id"], "blood": ((dm.get("blood_material") or "blood_fading") if dm is not None else "none"),   # Noita default: blood_fading
                 "spray": (dm or {}).get("blood_spray_material") or "none",
                 "stage": "2", "_unverified": {},
                 "_sources": {"all": "noita_facts.json %s DamageModelComponent (no blood_material = Noita default blood_fading, _component_docs)" % e["id"] if dm else "no DamageModelComponent in facts"}})
cols = {
    "id": {"type": "ref", "ref": "enemies", "desc": "Enemy."},
    "blood": {"type": "string", "desc": "Noita blood_material: what hits spill and the body leaves ('none')."},
    "spray": {"type": "string", "desc": "Noita blood_spray_material ('none')."},
    "stage": {"type": "enum", "values": ["1a", "1b", "1c", "2", "3", "4"], "desc": "Stage that builds it."},
}
with open(os.path.join(ROOT, "design", "sheets", "enemy_blood.json"), "w", encoding="utf-8", newline="\n") as fo:
    fo.write(json.dumps({"sheet": "enemy_blood", "description": "Stage 2: what each Noita creature bleeds.",
                         "key": "id", "columns": cols, "rows": rows}, ensure_ascii=False, indent=1) + "\n")
print(len(rows), "rows;", len(missing), "without DamageModelComponent:", " ".join(missing[:20]))
