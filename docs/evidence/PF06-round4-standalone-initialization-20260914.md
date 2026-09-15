# PF06 Round 4 独立初始化实证

时间：2026-09-14（Asia/Taipei）  
范围：仅临时 SQLite、临时 EmbeddedHost 进程；未触碰既有服务或 PF06A。

## 进程与工作目录

同一临时数据库由三个独立 OS 进程依次/并发使用：

- 数据库：`C:\Users\DONG\AppData\Local\Temp\pf06-round4-init-evidence-6ac2820beed141f081645cb721fe3f96\standalone.db`
- 进程 A CWD：`...\a`；先单独启动并完成初始化。
- 进程 B CWD：`...\b`；随后与进程 C 并发启动。
- 进程 C CWD：`...\c`；随后与进程 B 并发启动。
- 三个进程日志均出现 `Application started`，说明四模块初始化完成后宿主才进入运行态。

原始输出：

- [A stdout](logs/PF06-round4-evidence-a.out.txt)
- [B stdout](logs/PF06-round4-evidence-b.out.txt)
- [C stdout](logs/PF06-round4-evidence-c.out.txt)
- [A/B/C stderr](logs/PF06-round4-evidence-a.err.txt)、[logs/PF06-round4-evidence-b.err.txt](logs/PF06-round4-evidence-b.err.txt)、[logs/PF06-round4-evidence-c.err.txt](logs/PF06-round4-evidence-c.err.txt)

进程由验证脚本在 `Application started` 后结束；RabbitMQ 未运行只产生可选事件总线告警，不影响初始化完成证据，也未被标成 RabbitMQ PASS。

## 四模块与重复启动对比

探针使用真实数据库读取，不是锁文件计数：

- 初始化前（进程 A 完成后）：109 张表；Identity/SystemData/ReferenceData/Collaboration migration ledger 为 `24/58/11/13` 行；seed ledger 为 `2/9/2` 行。
- 重复启动后（B、C 并发完成后）：仍为 109 张表；四模块 migration ledger 仍为 `24/58/11/13` 行；seed ledger 仍为 `2/9/2` 行。
- 初始化前已有的 `SystemData.PermissionReconciled.v1` outbox 消息仍保留；重复启动新增当前启动应产生的 outbox 事件，没有清空或覆盖既有消息。
- `system_data_seed_ledger` 的 9 个 seed key、版本和 checksum 在前后探针输出中保持一致。

原始探针输出：

- [before](logs/PF06-round4-evidence-before.txt)
- [after](logs/PF06-round4-evidence-after.txt)

PostgreSQL advisory lock、Redis 和 RabbitMQ 在本证据中未运行，仍保持未覆盖声明。
