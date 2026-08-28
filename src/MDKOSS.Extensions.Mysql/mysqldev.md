Connect 打开 MySQL 连接（按 host / port / database / user）。
Disconnect 关闭连接。
Ping 检测连接是否可用。
SetConfig 运行时更新连接参数；若已连接则按新参数重连。
Query 执行 SELECT（或返回结果集的语句），返回列名与行。
Execute 执行 INSERT / UPDATE / DELETE / DDL，返回受影响行数与 lastInsertId。
Scalar 执行并返回第一行第一列。
Status 返回 isConnected、host、port、database、user（不含密码）。

任务类型 `cloud-machine`：每拍若未连接则打开，再 upsert 到表 `machine`。**不断开**共享的 `mysql-cloud` 连接，避免调试页 Query 变成 NotConnected。失败只写告警日志，任务不进入 Fault。

运行时在 Mysql 插件已加载时**默认**注册设备 `mysql-cloud` 和任务 `task-cloud-machine`。`Start()` 后立刻上传一次，之后按间隔心跳。密码启动时从 `scripts/mdkossdb/test_conn.py` 写入进程（及用户）环境变量 `MDKOSS_MYSQL_PASSWORD`。测试进程默认不注册；`MDKOSS_CLOUD_MONITOR=0` 可关掉。宿主 `plugins/` 需带上 `MySqlConnector` 及其 `Microsoft.Extensions.Logging.Abstractions` 依赖。
