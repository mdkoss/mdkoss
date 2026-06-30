from pathlib import Path

p = Path(__file__).resolve().parents[1] / "src" / "views" / "monitorPlatform.html"
t = p.read_text(encoding="utf-8")

if "badgeRuntime\">—</div>\n    </div>\n    <motion-platform-app class=\"sub\"" not in t:
    t = t.replace(
        "badgeRuntime\">—</div>\n    </div>\n    <div class=\"sub\"",
        "badgeRuntime\">—</div>\n      </div>\n    </div>\n    <div class=\"sub\"",
    )

t = t.replace("</table></table></div>", "</table></motion-platform-app>")
t = t.replace("</table></motion-platform-app>", "</table></div>")  # noqa: intentional two-step

needle = "<strong id=\"metaKind\">—</strong></div>\n        </div>"
if needle in t:
    t = t.replace(
        needle,
        "<strong id=\"metaKind\">—</strong></div>\n"
        "          <div><span>使能 </span><span id=\"metaMotion\">—</span></div>\n"
        "          <div><span>状态 </span><strong id=\"metaState\">—</strong></div>\n"
        "        </div>",
    )

p.write_text(t, encoding="utf-8")
print("done")
