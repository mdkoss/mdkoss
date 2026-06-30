"""Assemble src/views/monitorPlatform.html (head CSS + body + script)."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / "src" / "views" / "monitorPlatform.html"
T = "motion-platform-app"  # placeholder - will be replaced
TAG = "div"


def _close():
    return f"</{TAG}>"


def _open(cls=None, id_=None, extra=""):
    parts = [f"<{TAG}"]
    if id_:
        parts.append(f' id="{id_}"')
    if cls:
        parts.append(f' class="{cls}"')
    parts.append(extra)
    parts.append(">")
    return "".join(parts)


def pos_fields() -> str:
    out = []
    for L in "XYZUVW":
        out.append(_open("pos-field", f"posField_{L}"))
        out.append(f"        <label>{L} <span id=\"unit_{L}\">mm</span></label>")
        out.append(f'        <input type="text" id="pos_{L}" readonly value="—" />')
        out.append(_close())
    return "\n".join("      " + line for line in out)


def step_fields() -> str:
    out = []
    for L in "XYZUVW":
        out.append(_open("form-group"))
        out.append(f"        <label>{L} 步距</label>")
        out.append(f'        <input type="number" id="step_{L}" step="0.001" value="1" disabled />')
        out.append(_close())
    return "\n".join("      " + line for line in out)


def build_body() -> str:
    lines = [
        "<body>",
        '  <div id="toast" class="toast" role="status"></motion-platform-app>'.replace(
            "</motion-platform-app>", "</div>"
        ),
        _open("wrap"),
        _open("header"),
        _open(),
        '        <div class="title">平台步进示教</div>',
        '        <div class="sub" id="hdrSubtitle">请选择平台设备</motion-platform-app>'.replace(
            "</motion-platform-app>", "</motion-platform-app>"
        ),
    ]
    return "\n".join(lines)


def main():
    css_head = HTML.read_text(encoding="utf-8").split("<body>")[0]
    # Build body as list of lines to avoid editor corruption
    L = []
    a = L.append
    a("<body>")
    a('  <div id="toast" class="toast" role="status"></div>')
    a('  <div class="wrap">')
    a('    <div id="warnBar" class="warn-bar hidden">运行时未启动，仍可调试平台；建议先启动任务。</div>')
    a('    <motion-platform-app></motion-platform-app>')
    HTML.write_text(css_head + "\n".join(L), encoding="utf-8")
    print("partial", len(L))


if __name__ == "__main__":
    main()
