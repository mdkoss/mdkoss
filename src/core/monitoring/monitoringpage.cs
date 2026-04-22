namespace MDKOSS.Core.Monitoring;

internal static class MonitoringPage
{
    public const string Html = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>MDKOSS 监控面板</title>
  <style>
    :root {
      --bg: #0b1220;
      --panel: #101a2e;
      --text: #dce7ff;
      --muted: #90a4d4;
      --ok: #35d08f;
      --warn: #ffcc66;
      --line: #1f2c4a;
      --accent: #4e8cff;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
      background: radial-gradient(circle at top right, #16264a 0%, var(--bg) 40%);
      color: var(--text);
      min-height: 100vh;
    }
    .wrap { max-width: 1100px; margin: 0 auto; padding: 20px; }
    .header { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px; }
    .title { font-size: 26px; font-weight: 700; letter-spacing: .5px; }
    .sub { color: var(--muted); font-size: 13px; }
    .grid { display: grid; gap: 12px; grid-template-columns: repeat(12, 1fr); }
    .card {
      background: linear-gradient(180deg, rgba(255,255,255,.03), rgba(255,255,255,0));
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 14px;
      backdrop-filter: blur(4px);
    }
    .span-4 { grid-column: span 4; }
    .span-8 { grid-column: span 8; }
    .span-12 { grid-column: span 12; }
    .label { color: var(--muted); font-size: 12px; margin-bottom: 6px; }
    .value { font-size: 24px; font-weight: 700; }
    .pill {
      display: inline-block; border-radius: 999px; padding: 4px 10px; font-size: 12px; font-weight: 600;
      border: 1px solid var(--line); color: var(--muted);
    }
    .pill.ok { color: var(--ok); border-color: rgba(53,208,143,.4); background: rgba(53,208,143,.1); }
    .pill.warn { color: var(--warn); border-color: rgba(255,204,102,.4); background: rgba(255,204,102,.1); }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th, td { padding: 8px 10px; border-bottom: 1px solid var(--line); text-align: left; }
    th { color: var(--muted); font-weight: 600; }
    pre {
      margin: 0; background: #0a1120; border: 1px solid var(--line); border-radius: 10px; padding: 12px;
      max-height: 340px; overflow: auto; color: #c8d7ff;
    }
    .dot {
      width: 9px; height: 9px; border-radius: 50%; display: inline-block; margin-right: 8px; vertical-align: middle;
      background: var(--warn);
    }
    .dot.ok { background: var(--ok); box-shadow: 0 0 10px rgba(53,208,143,.8); }
    .footer { margin-top: 10px; color: var(--muted); font-size: 12px; }
    .op-row { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
    .btn {
      border: 1px solid var(--line);
      background: #162644;
      color: var(--text);
      border-radius: 8px;
      padding: 8px 12px;
      cursor: pointer;
      font-size: 13px;
    }
    .btn:hover { background: #1d3157; }
    .btn.green { border-color: rgba(53,208,143,.4); }
    .btn.yellow { border-color: rgba(255,204,102,.4); }
    .btn.red { border-color: rgba(255,93,93,.4); }
    @media (max-width: 900px) { .span-4, .span-8 { grid-column: span 12; } }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="header">
      <div class="title">MDKOSS Runtime Monitor</div>
      <div class="sub">自动刷新间隔：1s</div>
    </div>

    <div class="grid">
      <div class="card span-4">
        <div class="label">项目</div>
        <div class="value" id="projectName">-</div>
      </div>
      <div class="card span-4">
        <div class="label">运行状态</div>
        <div id="runtimeStatus"><span class="pill warn">UNKNOWN</span></div>
      </div>
      <div class="card span-4">
        <div class="label">在线驱动数</div>
        <div class="value" id="driverOnlineCount">0</div>
      </div>

      <div class="card span-8">
        <div class="label">驱动状态</div>
        <table>
          <thead><tr><th>Driver ID</th><th>Type</th><th>Status</th></tr></thead>
          <tbody id="driverRows"></tbody>
        </table>
      </div>

      <div class="card span-4">
        <div class="label">最后更新时间</div>
        <div class="value" id="lastUpdate" style="font-size: 18px">-</div>
      </div>

      <div class="card span-12">
        <div class="label">任务操作</div>
        <div class="op-row">
          <button class="btn green" onclick="sendTaskAction('/api/task/start')">启动任务</button>
          <button class="btn red" onclick="sendTaskAction('/api/task/stop')">停止任务</button>
          <button class="btn yellow" onclick="sendTaskAction('/api/task/reset')">复位任务</button>
        </div>
        <div class="op-row">
          <button class="btn red" onclick="sendTaskAction('/api/task/lamp?color=red')">红灯</button>
          <button class="btn yellow" onclick="sendTaskAction('/api/task/lamp?color=yellow')">黄灯</button>
          <button class="btn green" onclick="sendTaskAction('/api/task/lamp?color=green')">绿灯</button>
        </div>
        <div class="footer" id="taskOperationState">状态: -</div>
      </div>

      <div class="card span-12">
        <div class="label">变量快照</div>
        <pre id="varsBlock">{}</pre>
      </div>
    </div>
    <div class="footer">接口: <code>/api/status</code></div>
  </div>

  <script>
    const projectName = document.getElementById("projectName");
    const runtimeStatus = document.getElementById("runtimeStatus");
    const driverOnlineCount = document.getElementById("driverOnlineCount");
    const driverRows = document.getElementById("driverRows");
    const varsBlock = document.getElementById("varsBlock");
    const lastUpdate = document.getElementById("lastUpdate");
    const taskOperationState = document.getElementById("taskOperationState");

    function setStatus(isRunning) {
      runtimeStatus.innerHTML = isRunning
        ? '<span class="pill ok">RUNNING</span>'
        : '<span class="pill warn">STOPPED</span>';
    }

    function renderDrivers(drivers) {
      const entries = Object.entries(drivers || {});
      let online = 0;
      driverRows.innerHTML = entries.map(([id, d]) => {
        const up = !!d.isConnected;
        if (up) online++;
        return `<tr>
          <td>${id}</td>
          <td>${d.type || "-"}</td>
          <td><span class="dot ${up ? "ok" : ""}"></span>${up ? "Connected" : "Disconnected"}</td>
        </tr>`;
      }).join("");
      driverOnlineCount.textContent = String(online);
      if (!entries.length) {
        driverRows.innerHTML = '<tr><td colspan="3" style="color:#90a4d4">No drivers</td></tr>';
      }
    }

    async function tick() {
      try {
        const res = await fetch("/api/status", { cache: "no-store" });
        if (!res.ok) throw new Error("http " + res.status);
        const data = await res.json();
        projectName.textContent = data.projectName || "-";
        setStatus(!!data.isRunning);
        renderDrivers(data.drivers);
        varsBlock.textContent = JSON.stringify(data.vars || {}, null, 2);
        const state = data.vars?.["task.operation.state"] ?? "unknown";
        const lamp = data.vars?.["task.operation.lamp"] ?? "red";
        const message = data.vars?.["task.operation.message"] ?? "-";
        taskOperationState.textContent = `状态: ${state} | 灯色: ${lamp} | 说明: ${message}`;
        lastUpdate.textContent = new Date().toLocaleTimeString();
      } catch (err) {
        runtimeStatus.innerHTML = '<span class="pill warn">UNREACHABLE</span>';
      }
    }

    async function sendTaskAction(url) {
      try {
        const res = await fetch(url, { method: "POST" });
        if (!res.ok) throw new Error("http " + res.status);
        await tick();
      } catch (err) {
        taskOperationState.textContent = "状态: 操作失败";
      }
    }

    tick();
    setInterval(tick, 1000);
  </script>
</body>
</html>
""";
}
