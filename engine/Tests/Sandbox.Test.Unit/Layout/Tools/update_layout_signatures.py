"""Refresh layout known-failure signatures from a TRX produced by BlockGridFixtureTests.

Usage: python update_layout_signatures.py <test-results.trx> <layout-known-failures.txt>

The command prints the complete replacement file to stdout. Review it and apply it deliberately; the tool
does not overwrite the checked-in baseline.
"""
import re
import sys
import xml.etree.ElementTree as ET
from hashlib import sha256


trx_path, baseline_path = sys.argv[1], sys.argv[2]
root = ET.parse(trx_path).getroot()
messages = [element.text or "" for element in root.iter() if element.tag.endswith("Message")]

actual = {}
changed_pattern = re.compile(
    r"^(?:Assert\.Fail failed\. )?(?P<name>[^\r\n]+) mismatch changed;.*?"
    r"^Actual signature:\s+(?P<signature>[0-9A-F]{64})$",
    re.MULTILINE | re.DOTALL,
)
known_pattern = re.compile(
    r"Known failure (?P<signature>[0-9A-F]{64}) \(Data/Layout/layout-known-failures\.txt\)\r?\n"
    r"(?P<name>[^\r\n]+)",
)
legacy_pattern = re.compile(
    r"Known failure \(Data/Layout/layout-known-failures\.txt\)\r?\n"
    r"(?P<name>[^\r\n]+)\r?\n(?P<failures>.*?)\r?\nINPUT:",
    re.DOTALL,
)
for message in messages:
    match = changed_pattern.search(message)
    if match:
        actual[match.group("name")] = match.group("signature")
        continue

    match = known_pattern.search(message)
    if match:
        actual[match.group("name")] = match.group("signature")
        continue

    match = legacy_pattern.search(message)
    if match:
        failures = match.group("failures").replace("\r\n", "\n").replace("\r", "\n") + "\n"
        actual[match.group("name")] = sha256(failures.encode()).hexdigest().upper()

missing = []
with open(baseline_path, encoding="utf-8") as source:
    for line in source:
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            print(line, end="")
            continue

        name = stripped.split("|", 1)[0].strip()
        signature = actual.get(name)
        if signature is None:
            missing.append(name)
            continue
        print(f"{name} | {signature}")

if missing:
    print("Missing changed-signature results for: " + ", ".join(missing), file=sys.stderr)
    sys.exit(1)
