"""Emit monitorPlatform.html body; run: python scripts/emit_body.py"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / "src" / "views" / "monitorPlatform.html"
D = "div"


def main():
    css = HTML.read_text(encoding="utf-8")
    if "<body>" in css:
        css = css.split("<body>")[0]
    elif "</style>" in css:
        css = css.split("</style>")[0] + "</style>\n</head>\n"

    L = []
    a = L.append
    a("<body>")
    a(f'  <{D} id="toast" class="toast" role="status"></{D}>')
    a(f'  <{D} class="wrap">')
    a(f'    <{D} class="header">')
    a(f"      <{D}>")
    a('        <div class="title">平台步进示教</div>')
    a('        <div class="sub" id="hdrSubtitle">请选择平台设备</div>')
    a(f"      </{D}>")
    a('      <nav class="nav">')
    a('        <a href="/">监控首页</a>')
    a('        <a href="/monitorIO.html">IO 监控</a>')
    a('        <a href="/debugSerialDev.html">串口调试</a>')
    a("      </nav>")
    a(f"    </{D}>")
    a('    <div id="warnBar" class="warn-bar hidden">运行时未启动，仍可调试平台。</motion-platform-app>'.replace(
        "</motion-platform-app>", f"</{D}>"
    ))
    a('    <div class="form-row">')
    a('      <div class="form-group" style="flex:2">')
    a("        <label>平台设备</label>")
    a('        <select id="selPlatform"><option value="">— 选择平台 —</option></select>')
    a(f"      </{D}>")
    a('      <div class="form-group">')
    a("        <label>项目</label>")
    a('        <div id="badgeProject" style="padding:8px 0">—</div>')
    a(f"      </{D}>")
    a('      <div class="form-group">')
    a("        <label>运行</label>")
    a('        <motion-platform-app></motion-platform-app>')
    a(f"    </{D}>")
    a('    <div class="sub" style="margin-bottom:12px">刷新: <span id="pollHint">1s</span></div>')
    a('    <div class="main-layout">')
    a(f'      <{D} class="card">')
    a('        <h2 class="panel-title">步进</h2>')
    a('        <div class="form-row">')
    a('          <div class="form-group"><label>模式</label><select id="selMode"><option value="default">默认</option><option value="joint">关节</option><option value="world">世界</option></select></div>')
    a('          <div class="form-group"><label>速度</label><select id="selSpeed"><option value="low">低</option><option value="medium">中</option><option value="high">高</option></select></div>')
    a(f"        </{D}>")
    a('        <div id="jogGrid" class="jog-grid"></div>')
    a('        <div class="btn-group">')
    a('          <button type="button" class="btn primary" id="btnEnable">平台使能</button>')
    a('          <button type="button" class="btn danger" id="btnDisable">平台去使能</button>')
    a("        </div>")
    a(f"      </{D}>")
    a(f'      <{D} class="card">')
    a('        <h2 class="panel-title">目前位置</h2>')
    a('        <div class="radio-row">')
    a('          <label><input type="radio" name="coord" value="world" /> 世界 (W)</label>')
    a('          <label><input type="radio" name="coord" value="joint" checked /> 关节 (J)</label>')
    a('          <label><input type="radio" name="coord" value="pulse" /> 脉冲 (U)</label>')
    a("        </div>")
    a('        <div class="pos-grid">')
    for letter in "XYZUVW":
        a(f'          <{D} class="pos-field" id="posField_{letter}">')
        a(f'            <label>{letter} <span id="unit_{letter}">mm</span></label>')
        a(f'            <input type="text" id="pos_{letter}" readonly value="—" />')
        a(f"          </{D}>")
    a(f"        </{D}>")
    a('        <h2 class="panel-title">平台与轴状态</h2>')
    a('        <div class="meta-row">')
    a('          <div><span>类型 </span><strong id="metaKind">—</strong></motion-platform-app>'.replace(
        "</motion-platform-app>", f"</{D}>"
    ))
    a("        </div>")
    a('        <div class="table-wrap"><table><thead><tr><th>轴</th><th>子设备</th><th>驱动</th><th>在线</th><th>使能</th><th>备注</th></tr></thead><tbody id="axisTableBody"></tbody></table></div>')
    a('        <h2 class="panel-title">步进距离</h2>')
    a('        <div class="radio-row">')
    for val, lab in [
        ("continuous", "连续 (C)"),
        ("long", "长 (L)"),
        ("medium", "中 (M)"),
        ("short", "短 (S)"),
    ]:
        chk = " checked" if val == "continuous" else ""
        a(f'          <label><input type="radio" name="stepPreset" value="{val}"{chk} /> {lab}</label>')
    a("        </div>")
    a('        <div class="form-row">')
    for letter in "XYZUVW":
        a(f'          <{D} class="form-group"><label>{letter} 步距</label><input type="number" id="step_{letter}" step="0.001" value="1" disabled /></{D}>')
    a(f"        </{D}>")
    a(f"      </{D}>")
    a(f"    </{D}>")
    a(f'    <{D} class="card bottom-card">')
    a('      <div class="tabs">')
    a('        <button type="button" class="tab active" data-tab="teach">示教点</button>')
    a('        <button type="button" class="tab" data-tab="action">执行动作</button>')
    a('        <button type="button" class="tab" data-tab="io">关联 IO</button>')
    a("      </div>")
    a('      <div id="panel-teach" class="tab-panel active">')
    a('        <div class="form-row">')
    a('          <div class="form-group"><label>点文件</label><select id="selTeachFile"><option value="default">default.pts.json</option></select></div>')
    a('          <div class="form-group"><label>点</label><select id="selPoint"></select></div>')
    a(f"        </{D}>")
    a('        <div class="btn-group">')
    a('          <button type="button" class="btn primary" id="btnTeach">示教 (T)</button>')
    a('          <button type="button" class="btn" id="btnGoto">定位</button>')
    a('          <button type="button" class="btn" id="btnAddPoint">新增点</button>')
    a('          <button type="button" class="btn" id="btnExport">导出</button>')
    a('          <label class="btn">导入<input type="file" id="importFile" accept=".json" hidden /></label>')
    a("        </div>")
    a(f"      </{D}>")
    a('      <div id="panel-action" class="tab-panel">')
    a('        <div class="form-row">')
    a('          <div class="form-group"><label>设备 ID</label><input type="text" id="customDevice" placeholder="platformId 或 axisId" /></div>')
    a('          <div class="form-group"><label>action</label><input type="text" id="customAction" placeholder="enable / move" /></div>')
    a(f"        </{D}>")
    a('        <div class="form-group"><label>parameters (JSON)</label><textarea id="customParams" rows="2">{}</textarea></div>')
    a('        <button type="button" class="btn primary" id="btnCustomAction">执行</button>')
    a('        <div class="log-box" id="actionLog"></motion-platform-app>'.replace(
        "</motion-platform-app>", f"</{D}>"
    ))
    a(f"      </{D}>")
    a('      <div id="panel-io" class="tab-panel">')
    a('        <div class="table-wrap"><table><thead><tr><th>ID</th><th>名称</th><th>类型</th><th>状态</th><th>链接</th></tr></thead><tbody id="ioTableBody"></tbody></table></motion-platform-app>'.replace(
        "</motion-platform-app>", "</table></div>"
    ))
    a(f"      </{D}>")
    a(f"    </{D}>")
    a('    <p class="footer">API: <code>/api/status</code> · <code>/api/devices/{id}/action</code></p>')
    a(f"  </{D}>")
    a('<script src="monitorPlatform.js"></script>')
    a("</body>")
    a("</html>")

    # fix corrupted lines from .replace hacks
    text = "\n".join(L) + "\n"
    text = text.replace("<motion-platform-app></motion-platform-app>", f'<{D} id="badgeRuntime">—</{D}>')
    text = text.replace("</motion-platform-app>", f"</{D}>")
    text = text.replace("<motion-platform-app>", "")

    # inject platform page styles from debugserialdev copy head if needed
    head = css
    if "jog-grid" not in head:
        extra = Path(ROOT / "src" / "views" / "monitorPlatform.html").read_text(encoding="utf-8")
        if "jog-grid" in extra:
            # keep styles from first 107 lines of broken file if exists
            pass
    # Use styles embedded in debugserialdev copy + add jog styles
    if "jog-grid" not in head:
        head = head.replace(
            "</style>",
            """
    .main-layout { display: grid; grid-template-columns: minmax(280px, 38%) 1fr; gap: 16px; }
    @media (max-width: 1024px) { .main-layout { grid-template-columns: 1fr; } }
    .jog-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; margin: 12px 0; }
    .jog-btn { min-height: 44px; border-radius: 8px; border: 1px solid var(--line); background: #162644; color: var(--text); font-weight: 700; cursor: pointer; }
    .jog-btn:disabled { opacity: .35; }
    .jog-btn .arr { color: var(--warn); margin-right: 4px; }
    .pos-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
    .pos-field.na input { color: var(--muted); font-style: italic; }
    .tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--line); margin-bottom: 12px; }
    .tab { padding: 8px 14px; border: none; background: transparent; color: var(--muted); cursor: pointer; border-bottom: 2px solid transparent; margin-bottom: -1px; }
    .tab.active { color: var(--accent); border-bottom-color: var(--accent); }
    .tab-panel { display: none; } .tab-panel.active { display: block; }
    .toast { position: fixed; top: 16px; right: 16px; z-index: 100; transform: translateX(120%); transition: transform .25s; padding: 12px; background: #1a2844; border: 1px solid var(--line); border-radius: 8px; }
    .toast.show { transform: translateX(0); } .toast.err { color: #ffb4b4; } .toast.ok { color: #9ef0c8; }
    .warn-bar { padding: 10px; border-radius: 8px; margin-bottom: 12px; background: rgba(255,204,102,.08); border: 1px solid rgba(255,204,102,.3); color: var(--warn); }
    .warn-bar.hidden { display: none; }
    .meta-row { display: flex; gap: 16px; flex-wrap: wrap; font-size: 13px; }
    .nav a { color: var(--accent); text-decoration: none; font-size: 13px; padding: 6px 10px; border: 1px solid var(--line); border-radius: 6px; margin-left: 6px; }
  </style>""",
        )

    HTML.write_text(head + text, encoding="utf-8")
    print("Wrote", HTML)


if __name__ == "__main__":
    main()
