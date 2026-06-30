from pathlib import Path

p = Path(__file__).resolve().parents[1] / "src" / "views" / "monitoringpage.html"
lines = p.read_text(encoding="utf-8").splitlines()
out = []
skip_until_grid = False
seen_header_end = False
for i, line in enumerate(lines):
    if line.strip() == '<motion-platform-app class="grid">'.replace("<motion-platform-app", "<div"):
        pass
    if line.strip() == '<div class="grid">':
        skip_until_grid = False
        out.append(line)
        continue
    if line.strip() == "</div>" and i > 0 and "自动刷新" in lines[i - 1]:
        out.append(line)
        if not seen_header_end:
            seen_header_end = True
            skip_until_grid = True
        continue
    if skip_until_grid:
        if line.strip() == '<div class="grid">':
            skip_until_grid = False
            out.append(line)
        continue
    out.append(line)

# simpler: remove second block manually
text = p.read_text(encoding="utf-8")
dup = (
    "      <div>\n"
    "        <div class=\"title\">MDKOSS Runtime Monitor</div>\n"
    "        <nav class=\"nav\">\n"
    "          <a href=\"/monitorPlatform.html\">平台步进示教</a>\n"
    "          <a href=\"/monitorIO.html\">IO 监控</a>\n"
    "          <a href=\"/debugSerialDev.html\">串口调试</a>\n"
    "        </nav>\n"
    "      </div>\n"
    "      <div class=\"sub\">自动刷新间隔：1s</div>\n"
    "    </div>\n\n"
)
if text.count(dup) >= 1:
    text = text.replace(dup, "", 1)
text = text.replace("  <div class=\"wrap\">\n        <div class=\"header\">", '  <div class="wrap">\n    <div class="header">')
p.write_text(text, encoding="utf-8")
print("deduped")
