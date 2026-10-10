import json
from pathlib import Path
j = json.loads(Path("outputs/P3BurstDefault-20261010-01/player-validation-01/journal.json").read_text(encoding="utf-8"))
for r in j["runtimeRuns"]:
    rr = r.get("result") or {}
    print(r["label"], {k: rr.get(k) for k in ("medianMs","p95Ms","p99Ms","maxMs","over33","over50","frames","equalityCases","equalityCells","equalityMismatches","equalityAllBurst")})
    print("  nav-lines", [l[:220] for l in r.get("notableLogLines", []) if "nav" in l.lower() or "burst" in l.lower()][:3])
