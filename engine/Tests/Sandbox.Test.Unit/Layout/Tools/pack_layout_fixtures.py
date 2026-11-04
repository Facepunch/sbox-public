"""
Pack Taffy's XML layout fixtures (tests/xml/<group>/*.xml, Chrome-generated) into one file per group so the
unit test project can embed them as data.

Usage: python pack_layout_fixtures.py <upstream-repo>/tests/xml <out-dir> group1 group2 ...
"""
import os
import sys

SOURCE_REVISION = "ac2b86929d35b7e0f1d24919595b89b4ce89baa4"

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
for group in sys.argv[3:]:
    files = sorted(f for f in os.listdir(os.path.join(src, group)) if f.endswith(".xml"))
    with open(os.path.join(out, f"layout-{group}.xml"), "w", encoding="utf-8", newline="\n") as w:
        w.write('<?xml version="1.0" encoding="utf-8"?>\n')
        w.write(
            '<!-- Layout conformance fixtures from Taffy '
            f'(https://github.com/DioxusLabs/taffy/tree/{SOURCE_REVISION}), MIT licensed. '
            f'Generated from Chrome. Group: {group} -->\n'
        )
        w.write(f'<tests group="{group}">\n')
        for f in files:
            text = open(os.path.join(src, group, f), encoding="utf-8").read().strip()
            w.write(text + "\n")
        w.write("</tests>\n")
    print(group, len(files))
