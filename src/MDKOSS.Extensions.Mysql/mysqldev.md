Connect 打开 MySQL 连接（按 host / port / database / user）。
Disconnect 关闭连接。
Ping 检测连接是否可用。
SetConfig 运行时更新连接参数；若已连接则按新参数重连。
Query 执行 SELECT（或返回结果集的语句），返回列名与行。
Execute 执行 INSERT / UPDATE / DELETE / DDL，返回受影响行数与 lastInsertId。
Scalar 执行并返回第一行第一列。
Status 返回 isConnected、host、port、database、user（不含密码）。
