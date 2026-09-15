# PF06 最新开发交接（源码冻结快照）

时间：2026-09-14（Asia/Taipei）  
工作区：`D:\Code\Industrial Platform\IndustrialPlatform`  
HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`  
状态：dirty-worktree；未提交、未推送、未清理既有 WIP。  
权威清单：[PF06-latest-development-handoff-manifest-20260914.txt](PF06-latest-development-handoff-manifest-20260914.txt)

本文只描述本轮开发冻结交接，不替代独立验收结论，也不覆盖 PF06A。旧首轮记录 [PF06-standalone-collaboration-20260914.md](PF06-standalone-collaboration-20260914.md) 已明确标为历史快照，不能用其旧的未完成构建结论裁决当前源码。

## 最新验证证据

| 命令/证据 | 结果 | 原始日志或说明 |
| --- | --- | --- |
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | exit 0；0 warning；0 error | [PF06-fresh-build-20260914.txt](logs/PF06-fresh-build-20260914.txt) |
| `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` | 先前冻结源码的 exit 0；1,879 passed；7 skipped；0 failed | [PF06-full-test-20260914.txt](logs/PF06-full-test-20260914.txt)；本轮修复后按要求未重复全量后端测试 |
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`（Round 4 fresh） | exit 0；0 warning；0 error | [PF06-round4-fresh-build-20260914.txt](logs/PF06-round4-fresh-build-20260914.txt) |
| Collaboration/Embedded 受影响后端定向测试 | exit 0；42 passed；0 skipped；0 failed | [PF06-round4-collaboration-targeted-20260914.txt](logs/PF06-round4-collaboration-targeted-20260914.txt) |
| Round 4 组合行为定向测试 | 相关测试项目 build exit 0；0 warning；0 error；目标测试 1 passed；0 failed；0 skipped | [PF06-round4-combination-test-20260914.txt](logs/PF06-round4-combination-test-20260914.txt)；覆盖跨原 expiry 的真实 Hub Bind/Signal/Ready/KeepAlive 与 revoke |
| `pnpm.cmd test:unit`（`src/frontend`） | 既有全量日志 exit 0；135 test files passed；1,043 tests passed | [PF06-frontend-unit-20260914.txt](logs/PF06-frontend-unit-20260914.txt)；新增 pagehide 定向测试另行 1/1 通过 |
| `pnpm.cmd build`（`src/frontend`） | exit 0；Vite 2,380 modules | [PF06-frontend-build-20260914.txt](logs/PF06-frontend-build-20260914.txt)；仅有既有 chunk 体积提示 |
| Vite 模式冒烟 | `dev` 为 `http://localhost`；`dev:lan` 为所有 Network `http://`；`dev:lan:https` 为所有 Network `https://` | 临时端口 55176、55174、55175；临时进程已结束 |
| 实际双/三 OS 进程 SQLite 初始化与锁证据 | 三个独立 EmbeddedHost 进程、不同 CWD 均到达 `Application started`；109 张表；四模块 ledger 与 seed ledger 在重复启动前后保持一致 | [PF06-round4-standalone-initialization-20260914.md](PF06-round4-standalone-initialization-20260914.md)；PostgreSQL advisory lock、Redis、RabbitMQ 未运行 |

本轮未重启既有服务或用户进程；前端构建期间保留既有 chunk-size warning，不把 warning 误报为失败。真实 MES、真实双机、设备采集、TURN/跨网和生产发布仍需由独立验收按其范围判断。

## Round 4 独立验收阻断修复

第三轮验收指出：heartbeat 只延长存储会话，Hub principal 的 `exp` 与已绑定媒体 endpoint lease 仍停留在建连时的 60 秒。Round 4 的最小修复为：

- `ICollaborationHubSessionValidator` 返回已验证会话的新鲜 `TokenExpiresOn`；Embedded validator 每次 invocation 返回当前存储会话的 `ExpiresOn`，撤销/过期/来源变化仍返回无效。
- `CollaborationHubSessionFilter` 在放行前更新当前 principal 的 `exp`，并只刷新当前 `connectionId` 的 `MediaEndpointSnapshot.TokenExpiresOn`；不延长其他 connection，也不绕过过期校验。
- 新行为测试覆盖 heartbeat 后跨原 expiry 通过真实 Hub Bind/Signal/Ready/KeepAlive 继续使用当前媒体 lease，以及 revoke 后下一次实际 Hub invocation 抛出 unauthorized；`MediaContextRegistry` 单测覆盖不缩短 lease 和不影响 peer endpoint。
- `CollaborationMediaHost.spec.ts` 真实派发 `pagehide`，断言 `endAll` 与 realtime `stop` 都被调用，不再只有源码字符串断言。

## 本轮八项返修与代码入口

1. **Embedded Hub 会话不再只在连接时鉴权。** 页面会话恢复时补回真实 `exp` claim；`CollaborationHubSessionFilter` 对 Hub 每次 invocation 重新检查存储会话，`EmbeddedHubSessionValidator` 同时检查撤销、过期、account、MES source currentness 和 subject 映射。失效后返回 unauthorized 并中止连接；前端 heartbeat/Hub unauthorized 会清本地 embedded session、聊天、Presence 和实时连接。正常续期不因 expiresAt/token 变化而重建连接。

2. **FixedDemo 同 cookie A/B 隔离。** 页面级 credential 是身份边界；旧 A 页面 credential 不会阻塞 B 的 `/session?account=...`，B 账号不匹配时由服务端返回明确 `EMBEDDED_ACCOUNT_MISMATCH`，gateway 只对该明确分支执行 demo fallback。测试覆盖 A cookie → B 请求、B 新 page token，以及 A 请求头不被污染。

3. **正式 MES account 链路可执行。** `wwwroot/embedded-handshake.js` 已将 account 贯穿 challenge、assertion、exchange、renew 的 query/body；正式宿主使用内置的 server-derived stable identity mapper，MES 只需提供服务端当前用户/租户与目录 Search/Get 适配。可执行的 .NET Framework 4.5.2/.NET 6 兼容思路示例位于 [EmbeddedMesHandshakeExample.cs](../examples/EmbeddedMesHandshakeExample.cs)，宿主接线说明位于 `EmbeddedHost/README.md`。正式模式未配置或 account 不匹配时 fail closed，不回退 FixedDemo。

4. **协作页面和媒体生命周期收口。** embedded 离开协作路由时停止 heartbeat、清聊天并停止 realtime；媒体 Host 在路由离开、`beforeUnmount`、`pagehide` 调用页级 `endAll`；Hub disconnect 只清理当前 connection 绑定的媒体。HTTP 模式路由变化不误停应用级协作连接。

5. **Standalone PostgreSQL schema 统一使用配置值。** `DatabaseTopologyResolver`、ReferenceData ledger、迁移脚本与运行时表名都读取 `SharedDatabaseSchema`；不再把 standalone 强制改写为 `reference_data`。`StandaloneSchemaMetadataTests` 覆盖 Identity/SystemData/ReferenceData/Collaboration 四模块目标与 SQL schema 一致性；Platform Shared 的非 Development 约束保持不变。

6. **跨进程锁有真实探针证据。** `tools/pf06-standalone-lock-probe` 启动两个独立 OS 进程、不同工作目录、同一 SQLite DB，验证第二进程等待首进程释放后再取得锁。PostgreSQL advisory lock 与 Redis 未运行，未将其标成已验证。

7. **验证证据已刷新。** 先执行 fresh Release build，再执行 `--no-build` 全后端测试；当前总计为 1,879 passed / 7 skipped / 0 failed。前端既有全量单测为 135 文件 / 1,043 tests，新增 pagehide 定向测试为 1/1；Round 4 组合行为定向测试为 1/1，日志与文件 SHA-256 见最新 manifest。

8. **原有 Vite 功能保持。** `dev` 仍是 localhost HTTP；`dev:lan` 是所有 IP 的 HTTP；`dev:lan:https` 与 collaboration mode 才启用 HTTPS 和证书。未把 `dev:lan` 误切到 HTTPS。

## MES 接线最短路径

- 宿主入口：`src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/Program.cs`。
- 当前用户替换点：`EmbeddedAdapters.cs` 的 `IEmbeddedCurrentUserAdapter`；从 MES 服务端读取当前登录用户、租户和 accountNId，并和请求中的可选 `account` 做服务端比较。
- 目录替换点：`IEmbeddedCollaborationAccessAdapter` 的 Search/Get；不要从浏览器 subject 或权限列表建立可信身份。
- 握手路径：`POST /api/v1/embedded/challenges` → `GET /api/v1/embedded/assertions?nonce=...&account=...` → `POST /api/v1/embedded/exchanges`；session/heartbeat/revoke 继续带同一页的 account 选择。
- 前端接入：配置 `VITE_AUTH_MODE=embedded`，使用宿主 HttpOnly Cookie；`src/frontend/DEPLOYMENT.md` 记录资源、API/Hub、HTTPS 和 account 约束。
- 失败行为：正式适配未配置、当前 MES 用户与 account 不匹配、会话撤销或权限不在最小目录授权内时，服务端拒绝并不回退到固定演示账户。

## 协作边界

- 本轮未提交、未推送、未部署；未停止 VS、Vite、Docker、PF06A 或其他用户进程。
- 本文及最新 manifest 是开发交接文件；不修改独立验收的 rejection/验收结果文件。
- 真实 MES 当前用户、目录字段、凭据和部署地址仍由用户后续接线；SQL Server/Native、完整裁剪前端、真实跨网媒体和终端专项不在本轮完成声明内。
