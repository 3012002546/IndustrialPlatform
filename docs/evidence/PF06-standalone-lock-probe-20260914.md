# PF06 独立初始化双进程锁证据

日期：2026-09-14

## 实际双进程验证

探针项目：`tools/pf06-standalone-lock-probe`

验证方式：编译后的独立可执行文件由两个 OS 进程启动；两个进程使用同一临时 SQLite 文件，但工作目录分别为 `a`、`b`。第一个进程持锁 1500ms，第二个进程只有在锁释放后才能打印 `ACQUIRED`。

```text
FIRST=ACQUIRED|pid=51292|cwd=C:\Users\DONG\AppData\Local\Temp\pf06-dual-process-241268d01cd24dacad8e24ed4e83f2d8\a|utc=2026-09-14T14:31:32.5423805+00:00
SECOND=ACQUIRED|pid=59144|cwd=C:\Users\DONG\AppData\Local\Temp\pf06-dual-process-241268d01cd24dacad8e24ed4e83f2d8\b|utc=2026-09-14T14:31:34.1592528+00:00
SECOND_WAIT_MS=1620
EXIT_CODES=0,0
```

结论：SQLite 锁在不同 OS 进程、不同 CWD、同一绝对数据库目标上生效；第二进程等待超过第一个进程的持锁时间后成功。

## 未覆盖项

- PostgreSQL advisory lock：本机本轮未连接 PostgreSQL，未运行。
- Redis：锁探针不依赖 Redis，本轮未运行 Redis 覆盖。
