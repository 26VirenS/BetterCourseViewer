"""Swift Codable structs -> C# classes (System.Text.Json): stored `var`/`let` fields only; computed ones and enums skipped.
Usage: swift2cs.py <namespace> <out.cs> <file.swift>... ; prints what it skipped."""
import re, sys
ns, out, files = sys.argv[1], sys.argv[2], sys.argv[3:]
SIMPLE = {"String": "string", "Int": "int", "Double": "double", "Bool": "bool", "CGFloat": "double", "Float": "double", "Int64": "long", "Date": "string", "UUID": "string"}
def cs_type(t):
    t = t.strip()
    opt = t.endswith("?")
    if opt: t = t[:-1].strip()
    m = re.fullmatch(r"\[(.+):(.+)\]", t)
    if m:
        r = f"Dictionary<{cs_type(m.group(1))[0]}, {cs_type(m.group(2))[0]}>"
        return r + ("?" if opt else ""), not opt, "new()"
    m = re.fullmatch(r"\[(.+)\]", t)
    if m:
        r = f"List<{cs_type(m.group(1))[0]}>"
        return r + ("?" if opt else ""), not opt, "new()"
    base = SIMPLE.get(t, t.split(".")[-1])
    if opt: return base + "?", False, None
    if base == "string": return "string", True, '""'
    if base in ("int", "double", "bool", "long"): return base, False, None
    return base, True, "new()"
lines_out = [f"// Generated from the Swift models (scripts: windows/tools/swift2cs.py). Every field the page may leave out is", "// optional, so a page a version apart still reads.", "#nullable enable", f"namespace {ns};", ""]
skipped = []
for f in files:
    src = open(f).read()
    for m in re.finditer(r"^(?:final class|struct) (\w+)(?:<[^>]*>)?\s*(?::[^{]*)?\{", src, re.M):
        name = m.group(1)
        # the body: brace matching
        i, depth = m.end(), 1
        while depth and i < len(src):
            depth += {"{": 1, "}": -1}.get(src[i], 0); i += 1
        body = src[m.end():i - 1]
        if "Codable" not in src[m.start():m.end()] and "Decodable" not in src[m.start():m.end()]:
            skipped.append(name); continue
        fields = []
        d = 0
        for line in body.split("\n"):
            s = line.strip()
            if d == 0:
                fm = re.match(r"^(?:public |private\(set\) )?(?:var|let) (\w+): ([^={]+?)(?:\s*=\s*(.+))?$", s)
                if fm and "{" not in s:
                    fields.append((fm.group(1), fm.group(2), fm.group(3)))
            d += line.count("{") - line.count("}")
        lines_out.append(f"public sealed class {name}")
        lines_out.append("{")
        for fname, ftype, default in fields:
            t, needs_init, init = cs_type(ftype)
            prop = fname[0].upper() + fname[1:]
            if prop == name: prop += "Value"
            dv = ""
            if default is not None and t in ("bool", "int", "double"): dv = f" = {default.strip()};"
            elif needs_init: dv = f" = {init};"
            attr = f'    [System.Text.Json.Serialization.JsonPropertyName("{fname}")]\n' if prop != fname[0].upper() + fname[1:] else ""
            lines_out.append(f"{attr}    public {t} {prop} {{ get; set; }}{dv}")
        lines_out.append("}")
        lines_out.append("")
open(out, "w").write("\n".join(lines_out))
print("skipped (not Codable):", skipped)
