"""
Convert Yoga's generated gtest fixtures (tests/generated/*.cpp, produced from Chrome by gentest) into
MSTest C# against Sandbox.Layout. One C# file per fixture file, one [TestMethod] per TEST.
Omitted Yoga styles are explicit inputs in FlexFixtureBase.CreateNode; rounding is a test-only
FixtureGeometry observation. Unsupported runtime configuration is rejected, never approximated.

Usage: python convert_flex_fixtures.py <upstream-repo>/tests/generated ../FlexConformance
"""
import os
import re
import sys

SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)

EDGE = {
    "YGEdgeLeft": "Edge.Left", "YGEdgeTop": "Edge.Top", "YGEdgeRight": "Edge.Right", "YGEdgeBottom": "Edge.Bottom",
    "YGEdgeStart": "Edge.Start", "YGEdgeEnd": "Edge.End", "YGEdgeHorizontal": "Edge.Horizontal",
    "YGEdgeVertical": "Edge.Vertical", "YGEdgeAll": "Edge.All",
}
GUTTER = {"YGGutterColumn": "Gutter.Column", "YGGutterRow": "Gutter.Row", "YGGutterAll": "Gutter.All"}

def enum_value(v):
    # YGAlignFlexStart -> Align.FlexStart, YGDirectionLTR -> Direction.LTR etc
    for prefix, name in [
        ("YGAlign", "Align"), ("YGJustify", "Justify"), ("YGFlexDirection", "FlexDirection"),
        ("YGPositionType", "PositionType"), ("YGWrap", "Wrap"), ("YGOverflow", "Overflow"),
        ("YGDisplay", "Display"), ("YGDirection", "Direction"), ("YGBoxSizing", "BoxSizing"),
    ]:
        if v.startswith(prefix):
            return f"{name}.{v[len(prefix):]}"
    raise ValueError(v)

def num(v):
    v = v.strip()
    if v == "YGUndefined":
        return "float.NaN"
    if re.fullmatch(r"-?\d+", v):
        return v
    if re.fullmatch(r"-?\d*\.\d+f?", v):
        return v.rstrip("f") + "f"
    m = re.fullmatch(r"(\S+) / (\S+)", v)
    if m:
        return f"({num(m.group(1))}f / {num(m.group(2))}f)"
    raise ValueError(v)

# statement translators -------------------------------------------------------------------------

def translate(line, ctx):
    line = line.strip()
    if not line or line.startswith("//"):
        return None
    if line == "GTEST_SKIP();":
        ctx["skip"] = True
        return None

    m = re.fullmatch(r"YGConfigRef config = YGConfigNew\(\);", line)
    if m:
        ctx["round"] = True
        return None
    if re.fullmatch(r"YGConfigFree\(config\);", line) or re.fullmatch(r"YGNodeFreeRecursive\(root\);", line):
        return None

    m = re.fullmatch(r"YGConfigSetExperimentalFeatureEnabled\(config, (\w+), (true|false)\);", line)
    if m:
        raise ValueError(f"unsupported experimental configuration: {line}")

    m = re.fullmatch(r"YGConfigSetErrata\(config, (\w+)\);", line)
    if m:
        raise ValueError(f"unsupported errata configuration: {line}")

    m = re.fullmatch(r"YGConfigSetPointScaleFactor\(config, ([\d.f]+)\);", line)
    if m:
        scale = float(m.group(1).rstrip("f"))
        if scale not in (0, 1):
            raise ValueError(f"unsupported fixture observation scale: {line}")
        ctx["round"] = scale == 1
        return None

    m = re.fullmatch(r"YGConfigSetUseWebDefaults\(config, (true|false)\);", line)
    if m:
        if m.group(1) != "false":
            raise ValueError(f"fixture factory supplies non-web source defaults: {line}")
        return None

    m = re.fullmatch(r"YGNodeRef (\w+) = YGNodeNewWithConfig\(config\);", line)
    if m:
        return f"var {m.group(1)} = CreateNode();"

    m = re.fullmatch(r"YGNodeInsertChild\((\w+), (\w+), (\d+)\);", line)
    if m:
        return f"{m.group(1)}.InsertChild( {m.group(2)}, {m.group(3)} );"

    m = re.fullmatch(r"YGNodeRemoveChild\((\w+), (\w+)\);", line)
    if m:
        return f"{m.group(1)}.RemoveChild( {m.group(2)} );"

    m = re.fullmatch(r"YGNodeCalculateLayout\((\w+), (\S+), (\S+), (\w+)\);", line)
    if m:
        return f"{m.group(1)}.CalculateLayout( {num(m.group(2))}, {num(m.group(3))}, {enum_value(m.group(4))} );"

    m = re.fullmatch(r"ASSERT_FLOAT_EQ\((\S+), YGNodeLayoutGet(Left|Top|Width|Height)\((\w+)\)\);", line)
    if m:
        rounding = str(ctx.get("round", True)).lower()
        return f"AssertEq( {num(m.group(1))}, FixtureGeometry.GetRect( {m.group(3)}, {rounding} ).{m.group(2)}, \"{m.group(3)}.{m.group(2)}\" );"

    m = re.fullmatch(r"YGNodeSetContext\((\w+), \(void\*\)\"(.*)\"\);", line)
    if m:
        ctx["text"] = True
        return f"{m.group(1)}.Context = \"{m.group(2)}\";"

    m = re.fullmatch(r"YGNodeSetMeasureFunc\((\w+), &facebook::yoga::test::IntrinsicSizeMeasure\);", line)
    if m:
        return f"{m.group(1)}.MeasureFunc = IntrinsicSizeMeasure;"

    m = re.fullmatch(r"YGNodeStyleSet(\w+?)(Percent|Auto)?\((\w+)(?:, (.*))?\);", line)
    if m:
        prop, suffix, node, args = m.group(1), m.group(2), m.group(3), m.group(4)
        args = [a.strip() for a in args.split(",")] if args else []
        return style_set(prop, suffix, node, args)

    raise ValueError(f"unhandled: {line}")

def length(value, suffix):
    if suffix == "Auto":
        return "StyleLength.Auto"
    if suffix == "Percent":
        return f"StyleLength.Percent( {num(value)} )"
    return f"StyleLength.Points( {num(value)} )"

DIMENSIONS = {"Width": "Width", "Height": "Height", "MinWidth": "MinWidth", "MinHeight": "MinHeight", "MaxWidth": "MaxWidth", "MaxHeight": "MaxHeight"}
ENUM_PROPS = {"FlexDirection", "JustifyContent", "AlignContent", "AlignItems", "AlignSelf", "PositionType", "FlexWrap", "Overflow", "Display", "Direction", "BoxSizing"}
EDGE_PROPS = {"Margin", "Padding", "Border", "Position"}

def style_set(prop, suffix, node, args):
    if prop in ENUM_PROPS:
        return f"{node}.Style.{prop} = {enum_value(args[0])};"
    if prop in DIMENSIONS:
        return f"{node}.Style.{DIMENSIONS[prop]} = {length(args[0] if args else None, suffix)};"
    if prop == "FlexBasis":
        return f"{node}.Style.FlexBasis = {length(args[0] if args else None, suffix)};"
    if prop in EDGE_PROPS:
        edge = EDGE[args[0]]
        if prop == "Border":
            return f"{node}.Style.SetBorder( {edge}, {length(args[1], None)} );"
        return f"{node}.Style.Set{prop}( {edge}, {length(args[1] if len(args) > 1 else None, suffix)} );"
    if prop == "Gap":
        return f"{node}.Style.SetGap( {GUTTER[args[0]]}, {length(args[1], suffix)} );"
    if prop == "Flex":
        raise ValueError("Flex shorthand requires explicit source-mode expansion; absent from v3.2.1 corpus")
    if prop in ("FlexGrow", "FlexShrink", "AspectRatio"):
        return f"{node}.Style.{prop} = {num(args[0])};"
    raise ValueError(f"style {prop} {suffix} {args}")

# driver ------------------------------------------------------------------------------------------

HEADER = """// <auto-generated>
// Ported from Yoga's Chrome-generated conformance fixtures ({src}), MIT licensed
// (Copyright (c) Meta Platforms, Inc. and affiliates). Regenerate with convert_flex_fixtures.py.
// Source: https://github.com/facebook/yoga/blob/v3.2.1/tests/generated/{src}
// </auto-generated>
using Sandbox.Layout;

namespace LayoutTests.FlexConformance;

[TestClass]
public class {cls} : FlexFixtureBase
{{
"""

total = 0
for fname in sorted(os.listdir(SRC)):
    if not fname.endswith(".cpp"):
        continue
    src = open(os.path.join(SRC, fname), encoding="utf-8").read()
    cls = fname[:-4].replace("YG", "", 1) if fname.startswith("YG") else fname[:-4]
    if not cls.endswith("Test"):
        cls += "Test"
    out = [HEADER.format(src=fname, cls=cls)]

    for tm in re.finditer(r"TEST\(YogaTest, (\w+)\) \{\n(.*?)\n\}\n", src, re.S):
        name, body = tm.group(1), tm.group(2)
        ctx = {}
        stmts = []
        for raw in body.split("\n"):
            t = translate(raw, ctx)
            if t is not None:
                stmts.append(t)
            elif raw.strip() == "":
                stmts.append("")
        # collapse duplicate blank lines
        cleaned = []
        for s in stmts:
            if s == "" and (not cleaned or cleaned[-1] == ""):
                continue
            cleaned.append(s)
        while cleaned and cleaned[-1] == "":
            cleaned.pop()
        skip = "\t[Ignore( \"Skipped upstream (GTEST_SKIP) - Chrome and upstream disagree here\" )]\n" if ctx.get("skip") else ""
        out.append(f"\t[TestMethod]\n{skip}\tpublic void {name}()\n\t{{\n")
        for s in cleaned:
            out.append(("\t\t" + s if s else "") + "\n")
        out.append("\t}\n\n")
        total += 1

    out.append("}\n")
    with open(os.path.join(OUT, cls + ".cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(out))

print(f"generated {total} tests")
