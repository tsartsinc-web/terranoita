"""Preflight: lay every sheet over each other and list what is not done.

For every row x column crossing it reports:
  UNFILLED    the cell is missing, null or "" (an empty list [] means "none" and counts as filled)
  UNVERIFIED  the row's "_unverified" map names the column (with how to verify it)
  BADREF      a ref/ref[] cell names a row that does not exist in the target sheet
  BADENUM     an enum cell holds a value outside the column's list
  BADTYPE     the value does not match the column type

Usage:
  python tools/preflight.py                 # full report, every stage
  python tools/preflight.py --gate 1a       # build gate: fail on any problem in rows needed for stage 1a
  python tools/preflight.py --gate 1a -q    # only the summary
"""
import argparse
import json
import os
import sys
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "design", "sheets")
STAGE_ORDER = ["1a", "1b", "1c", "2", "3", "4"]


def load_sheets():
    sheets = {}
    for name in sorted(os.listdir(SHEETS)):
        if name.endswith(".json"):
            with open(os.path.join(SHEETS, name), encoding="utf-8") as f:
                s = json.load(f)
            sheets[s["sheet"]] = s
    return sheets


def filled(v):
    return v is not None and v != ""


def type_ok(t, v):
    if t in ("string", "enum", "ref"):
        return isinstance(v, str)
    if t == "number":
        return isinstance(v, (int, float)) and not isinstance(v, bool)
    if t == "int":
        return isinstance(v, int) and not isinstance(v, bool)
    if t == "bool":
        return isinstance(v, bool)
    if t == "object":
        return isinstance(v, dict)
    if t.endswith("[]"):
        return isinstance(v, list) and all(type_ok(t[:-2], x) for x in v)
    return True


def check(sheets):
    keys = {n: {r[s["key"]] for r in s["rows"]} for n, s in sheets.items()}
    problems = []  # (sheet, row, column, kind, detail)
    for n, s in sheets.items():
        cols = s["columns"]
        for r in s["rows"]:
            rid = r.get(s["key"], "?")
            unv = r.get("_unverified", {})
            for c, spec in cols.items():
                v = r.get(c)
                if not filled(v):
                    problems.append((n, rid, c, "UNFILLED", spec.get("desc", "")))
                    continue
                if isinstance(v, dict) and any(x is None for x in v.values()):
                    problems.append((n, rid, c, "UNFILLED", "some entries are null: " +
                                     ", ".join(k for k, x in v.items() if x is None)))
                if not type_ok(spec["type"], v):
                    problems.append((n, rid, c, "BADTYPE", "expected %s, got %r" % (spec["type"], v)))
                    continue
                if spec["type"] == "enum" and v not in spec["values"]:
                    problems.append((n, rid, c, "BADENUM", "%r not in %s" % (v, spec["values"])))
                if spec["type"] in ("ref", "ref[]"):
                    target = spec["ref"]
                    if target not in sheets:
                        problems.append((n, rid, c, "BADREF", "sheet %r does not exist" % target))
                        continue
                    for x in (v if isinstance(v, list) else [v]):
                        if x in spec.get("allow", []):
                            continue
                        if x not in keys[target]:
                            problems.append((n, rid, c, "BADREF", "%r not in %s" % (x, target)))
                if c in unv:
                    problems.append((n, rid, c, "UNVERIFIED", unv[c]))
            for c in unv:
                if c not in cols:
                    problems.append((n, rid, c, "BADTYPE", "_unverified names an unknown column"))
            for c in r:
                if c not in cols and not c.startswith("_"):  # _unverified, _sources: bookkeeping
                    problems.append((n, rid, c, "BADTYPE", "cell in a column the sheet does not declare"))
    return problems


def needed_rows(sheets, gate):
    """Rows a stage needs: rows whose stage <= gate, plus everything they reference (transitively)."""
    limit = STAGE_ORDER.index(gate)
    need = defaultdict(set)
    todo = []
    for n, s in sheets.items():
        has_stage = "stage" in s["columns"]
        for r in s["rows"]:
            if has_stage and r.get("stage") in STAGE_ORDER and STAGE_ORDER.index(r["stage"]) <= limit:
                todo.append((n, r[s["key"]]))
    index = {n: {r[s["key"]]: r for r in s["rows"]} for n, s in sheets.items()}
    while todo:
        n, rid = todo.pop()
        if rid in need[n]:
            continue
        need[n].add(rid)
        s = sheets[n]
        r = index[n].get(rid)
        if r is None:
            continue
        for c, spec in s["columns"].items():
            # back-references (used_by) do not pull rows in
            if spec["type"] not in ("ref", "ref[]") or c == "used_by":
                continue
            v = r.get(c)
            for x in (v if isinstance(v, list) else [v] if v else []):
                if x in index.get(spec["ref"], {}):
                    todo.append((spec["ref"], x))
    return need


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gate", choices=STAGE_ORDER, help="fail if rows needed for this stage have problems")
    ap.add_argument("-q", "--quiet", action="store_true", help="summary only")
    args = ap.parse_args()

    sheets = load_sheets()
    problems = check(sheets)
    total_cells = sum(len(s["rows"]) * len(s["columns"]) for s in sheets.values())

    if args.gate:
        need = needed_rows(sheets, args.gate)
        problems = [p for p in problems if p[1] in need[p[0]]]
        scope = "rows needed for stage %s: %s" % (args.gate, ", ".join(
            "%s %d" % (n, len(v)) for n, v in sorted(need.items())))
    else:
        scope = "all rows"

    by_kind = Counter(p[3] for p in problems)
    by_sheet = Counter(p[0] for p in problems)
    if not args.quiet:
        for p in sorted(problems):
            print("%-10s %-14s %-38s %-18s %s" % (p[3], p[0], p[1], p[2], p[4]))
        print()
    print("Preflight (%s)" % scope)
    print("  sheets: %s; %d cells in total" % (", ".join(sorted(sheets)), total_cells))
    print("  problems: %d  (%s)" % (len(problems), ", ".join("%s %d" % kv for kv in sorted(by_kind.items())) or "none"))
    if by_sheet:
        print("  by sheet: " + ", ".join("%s %d" % kv for kv in sorted(by_sheet.items())))
    unfinished_rows = len({(p[0], p[1]) for p in problems})
    print("  unfinished rows: %d" % unfinished_rows)
    if args.gate and problems:
        print("GATE %s: NOT CLEAN - fix these before building" % args.gate)
        sys.exit(1)
    if args.gate:
        print("GATE %s: CLEAN" % args.gate)


if __name__ == "__main__":
    main()
