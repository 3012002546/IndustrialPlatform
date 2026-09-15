# PF06 独立协作接入第三轮独立增量验收

- 日期：2026-09-14（Asia/Taipei）
- 结论：**FAIL / 仍有 1 项确定性代码阻断、2 项行为证据未闭环**
- 冻结基线 HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`
- 最新开发交接 SHA256：`3F85C674D64C82BE19468292EF3BE6A7F9DC48DEB7FD0EED714B278E1A60D5CB`
- 最新 manifest SHA256：`9501EB47BF89E17CAF2D5AD574A74EA93E4832349A9340A7FBEC2D9B609D2691`
- manifest 独立核验：`MISSING=0`、`MISMATCH=0`
- 验收方式：只核对第二轮 5 项阻断和 2 项证据缺口；复用已冻结的完整日志，仅补跑最小后端定向测试。

## 逐项结论

| 第二轮问题 | 结论 | 第三轮证据 |
| --- | --- | --- |
| Embedded 媒体缺 `exp` | **部分修复，仍 FAIL** | principal 已补 `exp`，初次媒体调用不再因缺 claim 直接 unauthorized；但续期后的最新 expiry 没有传到 Hub 和已绑定媒体，详见下方阻断项。 |
| 会话过期/撤销后 WebSocket 不失效 | **PASS（实现）** | `CollaborationHubSessionFilter` 在连接与每次 invocation 调用 `EmbeddedHubSessionValidator`；validator 检查存储会话撤销/过期、account、MES source currentness。前端 heartbeat 或 Hub 鉴权失败会清本地会话并停止实时连接。 |
| FixedDemo 同源 A/B 切换 403 | **PASS** | gateway 仅把 401 或明确的 403 `EMBEDDED_ACCOUNT_MISMATCH` 作为 FixedDemo fallback；测试覆盖 A page credential → B account → B 新 page credential，普通 403 不放行。 |
| 正式 MES account 未贯通 | **PASS（静态与契约）/真实 MES 未覆盖** | `embedded-handshake.js` 已把 account 贯穿 challenge、assertion、exchange、renew；服务端 assertion 阶段由 `IEmbeddedCurrentUserAdapter` 核验请求 account；正式模式使用内置 `ServerDerivedEmbeddedSubjectIdentityMapper`，MES 只需当前用户及目录 Search/Get。 |
| PostgreSQL Standalone 多 Schema | **PASS（静态与单元）/真实 PostgreSQL 未覆盖** | 四模块 target 保留配置的 `SharedDatabaseSchema`；ReferenceData 运行时 SQL、ledger 与迁移均读取配置 schema，不再硬编码 `reference_data.*`。 |
| 两进程、两 CWD 初始化证据 | **PARTIAL / 未闭环** | 新探针确认两个独立 OS 进程、不同 CWD、同一 SQLite 目标上的锁等待与依次获得；它没有执行四模块真实初始化，也没有覆盖重复启动后消息保留。 |
| 路由离开与 pagehide | **PARTIAL / 未闭环** | embedded 路由离开已有行为测试，HTTP 路由变化不误停；`pagehide` 测试只是读取源码后断言字符串存在，没有挂载组件、派发事件并验证 `endAll`/`realtime.stop` 调用。 |

## 确定性代码阻断：续期后仍使用建连时的 60 秒媒体租约

1. `EmbeddedHandshake.cs:23` 默认 `SessionLifetime = 60s`；中间件只在建连时把当时的 `session.ExpiresOn` 写入 principal 的 `exp`（`EmbeddedHandshake.cs:1042`）。
2. heartbeat 会在存储中延长同一 session（`EmbeddedHandshake.cs:314-320`）。
3. 每次 Hub invocation 的 validator 确实重新读取并验证最新存储 session（`EmbeddedHubSessionValidator.cs:32-52`），但接口只返回 `bool`，既不返回最新 `ExpiresOn`，也不更新 `HubCallerContext.Items` 或 principal 的 `exp`。
4. Hub 媒体方法仍只从建连时 principal 读取 `TokenExpiresOn`（`CollaborationHub.cs:167,170,181,184,202,318-325`）。
5. `BindMediaAsync` 把这个旧值保存进 `MediaEndpointSnapshot`（`RemoteAssistanceService.cs:475`）；后续 signal、ready、keepalive、mute 均在 `RemoteAssistanceService.cs:506,520,578,626` 拒绝已过期的旧值。

因此，即使第 20/40 秒 heartbeat 已把服务端 session 续期，现有 WebSocket 和已绑定媒体在原始第 60 秒后仍会进入 `MEDIA_EXPIRED`；这仍违反“正常心跳连续超过 60 秒不断线、媒体不因固定 60 秒会话寿命结束”的合同。

### 最小修复建议

- 让 Hub invocation 校验返回当前存储 session 的 `ExpiresOn`，由 Hub 使用该新鲜租约，而不是继续读取建连时 `exp`。
- 同时刷新该 `connectionId` 已绑定 `MediaEndpointSnapshot.TokenExpiresOn`；只修 Hub 的 `TokenExpiresOn` 仍不足以挽救已经绑定的媒体上下文。
- 增加一条行为测试：先以 60 秒 expiry 建连并绑定媒体，再模拟 heartbeat 延长存储 session，推进超过原 expiry，验证 bind/signal/media-ready/keepalive 继续成功；随后撤销 session，验证下一次 invocation 失败并清理连接。

## 仍缺行为证据

1. **完整初始化链。** `tools/pf06-standalone-lock-probe` 只调用 `StandaloneInitializationLock.AcquireAsync` 并等待，不启动 `StandaloneDatabaseInitializationHostedService`，不执行 Identity → SystemData → ReferenceData → Collaboration，也不写入和复核消息。仍需证明：两个独立进程从不同 CWD 指向同一 SQLite 并发启动，四模块初始化均成功；再次启动后既有消息仍存在。
2. **pagehide 真实调用。** `collaborationMediaStore.spec.ts:25-30` 只检查 `CollaborationMediaHost.vue` 源码字符串。仍需组件级测试实际派发 `pagehide`，验证 embedded 调用 `media.endAll()` 与 `realtime.stop()`，HTTP 模式不误停。

## 验证记录

- 冻结清单：全部路径存在，全部 SHA256 相符。
- 复用 fresh Release build 日志：exit 0、0 warning、0 error。
- 复用后端完整测试日志：**1879 passed / 7 skipped / 0 failed**。
- 复用前端完整日志：**135 files / 1043 tests passed**；生产 build 通过。
- 本轮补跑 Collaboration 定向测试：**28 passed / 0 failed / 0 skipped**，exit 0。
- 本轮补跑 ReferenceData Standalone schema 定向测试：**1 passed / 0 failed / 0 skipped**，exit 0。

## 环境边界

真实 MES 接口尚未提供，因此正式 MES 仅完成契约和静态链路验收；本轮没有运行 PostgreSQL、Redis、真实浏览器或真实双机。未修改生产代码、未提交、未推送、未重启服务、未停止 IDE/dotnet、未触碰 PF06A。
