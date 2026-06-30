from pathlib import Path

p = Path(__file__).resolve().parents[1] / "src" / "views" / "monitoringpage.html"
t = p.read_text(encoding="utf-8")
e = "</div>"
header = (
    "    <div class=\"header\">\n"
    "      <div>\n"
    "        <div class=\"title\">MDKOSS Runtime Monitor</div>\n"
    "        <nav class=\"nav\">\n"
    "          <a href=\"/monitorPlatform.html\">平台步进示教</a>\n"
    "          <a href=\"/monitorIO.html\">IO 监控</a>\n"
    "          <a href=\"/debugSerialDev.html\">串口调试</a>\n"
    "        </nav>\n"
    "      " + e + "\n"
    "      <div class=\"sub\">自动刷新间隔：1s</div>\n"
    "    " + e
)
bad = "<motion-platform-app></motion-platform-app>"
if bad in t:
    t = t.replace(bad, header)
t = t.replace(
    'href="/monitorPlatform.html">monitorPlatform.html</a></motion-platform-app>',
    'href="/monitorPlatform.html">monitorPlatform.html</a></div>',
)
p.write_text(t, encoding="utf-8")
print("ok", bad in Path(p).read_text(encoding="utf-8"))
