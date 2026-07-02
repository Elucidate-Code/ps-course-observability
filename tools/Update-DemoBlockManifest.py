"""Regenerates tools/demo-block-manifest.json from the current source.

Run this only after setting the repo to -State M5 (everything uncommented) --
it captures whatever text currently sits inside each managed [DEMO-BLOCK
BEGIN/END] region as the new "on" canonical content for Set-DemoState.ps1.

Usage (from the repo root):
    ./Set-DemoState.ps1 -State M5
    python tools/Update-DemoBlockManifest.py

Only needed if you add/rename a [DEMO-BLOCK] region in the source; the
manifest checked into the repo already reflects the current block set.
"""
import json
import re
import pathlib

root = pathlib.Path("ObservabilityDemo.OrderService")

# name -> group. Groups map to Set-DemoState.ps1's M1-M5 checkpoints.
# Any [DEMO-BLOCK BEGIN/END] region NOT listed here is left untouched by the
# switcher (frozen at whatever is currently committed) -- see the exclusions
# called out in Set-DemoState.ps1's help text.
MANAGED_GROUPS = {
    "tracing-add-source": "tracing",
    "tracing-activitysource": "tracing",
    "tracing-root-span": "tracing",
    "tracing-root-tags-late": "tracing",
    "tracing-root-catch": "tracing",
    "tracing-inventory-span": "tracing",
    "tracing-inventory-catch": "tracing",
    "tracing-persist-span": "tracing",
    "tracing-persist-catch": "tracing",
    "metrics-add-meter": "metrics",
    "metrics-meter-and-instruments": "metrics",
    "metrics-active-increment": "metrics",
    "metrics-active-decrement": "metrics",
    "metrics-success-record": "metrics",
    "metrics-failure-record": "metrics",
    "metrics-dependency-failure-inventory": "metrics",
    "metrics-dependency-failure-payment": "metrics",
    "metrics-dependency-failure-db": "metrics",
    "logging-scope": "logging",
    "logging-error-structured": "logging",
    "tracing-pii-processor": "pii",
    "logging-pii-processor": "pii",
    "tracing-exception-redaction": "pii",
}

begin_re = re.compile(r'^\s*//\s*\[DEMO-BLOCK BEGIN:\s*([a-zA-Z0-9_-]+)\]\s*$')
end_re = re.compile(r'^\s*//\s*\[DEMO-BLOCK END:\s*([a-zA-Z0-9_-]+)\]\s*$')

blocks = []
for path in sorted(root.rglob("*.cs")):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    i = 0
    while i < len(lines):
        m = begin_re.match(lines[i])
        if m:
            name = m.group(1)
            j = i + 1
            body = []
            while j < len(lines) and not end_re.match(lines[j]):
                body.append(lines[j])
                j += 1
            if j >= len(lines):
                raise SystemExit(f"Unterminated block {name} in {path}")
            end_name = end_re.match(lines[j]).group(1)
            if end_name != name:
                raise SystemExit(f"Mismatched END for {name} in {path} (got {end_name})")
            if name in MANAGED_GROUPS:
                rel = str(path.relative_to(".")).replace("\\", "/")
                blocks.append({
                    "name": name,
                    "group": MANAGED_GROUPS[name],
                    "file": rel,
                    "lines": body,
                })
            i = j + 1
        else:
            i += 1

found_names = {b["name"] for b in blocks}
missing = set(MANAGED_GROUPS) - found_names
if missing:
    raise SystemExit(f"Managed block names never found in source: {missing}")

out_path = pathlib.Path("tools/demo-block-manifest.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump({"blocks": blocks}, f, indent=2)
print(f"Captured {len(blocks)} managed blocks across {len({b['file'] for b in blocks})} files -> {out_path}")
