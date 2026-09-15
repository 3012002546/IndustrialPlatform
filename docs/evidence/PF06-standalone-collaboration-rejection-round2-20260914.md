# PF06 独立协作接入第二轮独立增量验收

- 日期：2026-09-14（Asia/Taipei）
- 结论：**FAIL / 退回原开发任务继续整改**
- 验收基线 HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`
- 验收方式：针对首轮 7 项缺陷检查当前工作区最新源码，并只补跑关键定向测试；未复跑后端完整套件。
- 生产代码：本轮独立验收未修改。

## 已确认改善

1. `RemoteAssistanceService.RequireActivePeerAsync` 已同时检查人员 Active 状态及有效 Online Presence；定向测试覆盖语音、共享我的屏幕、请求对方共享三类离线拒绝。
2. Hub 断连已按 `connectionId` 结束该连接拥有的屏幕/语音会话，并保留同账号其他连接的媒体。
3. 前端 embedded 实时身份键不再包含续期时间，正常 heartbeat 不再因 `expiresAt` 变化主动重连。
4. embedded 实时连接已按 Collaboration 路由持有，媒体宿主也增加路由离开、`pagehide` 和卸载收口。
5. Standalone 使用显式 `IsStandalone`，平台原有 Shared 非 Development 保护仍保留。
6. SQLite 已使用 OS 文件锁，PostgreSQL 已使用 advisory lock；统一初始化器在锁内按 Identity → SystemData → ReferenceData → Collaboration 顺序执行。

这些改善不等于整体验收通过；以下阻断仍存在。

## 上线阻断项

1. **Embedded 媒体 Hub 缺少到期声明，屏幕/语音调用会被拒绝。**
   - `EmbeddedHandshake.cs:987-999` 构造 embedded principal 时没有 `exp` claim。
   - `CollaborationHub.cs:125,128,139,142,160,276-283` 的媒体邀请、响应和绑定均强制读取 `exp`，不存在即抛 `HubException("unauthorized")`。
   - 当前没有 embedded principal → Hub 媒体方法的端到端测试覆盖此路径。

2. **健康 WebSocket 不会在会话过期或撤销后有界失效。**
   - embedded 中间件只在建连时校验会话。
   - `collaborationRuntime.ts:52-56` 先刷新 Presence，再调用 heartbeat，并吞掉 heartbeat 失败。
   - 移除到期时间驱动重连后，旧连接可继续以建连时 claims 调用 Hub，并持续把 Presence 刷为 Online；停止心跳、服务端撤销或 `IsCurrent=false` 时缺少有界断开与服务端调用期复核。
   - 修复不能重新引入固定 60 秒媒体断线。

3. **FixedDemo 同源第二账号首次打开仍失败。**
   - 已有账号 A Cookie 时，账号 B 页首次 `GET /api/v1/embedded/session?account=xxB` 返回 403 `EMBEDDED_ACCOUNT_MISMATCH`。
   - `embeddedAuthGateway.ts:156-170` 仅在错误类型为 `unauthorized`（401）时调用 `/embedded/demo/session`；403 被当作 server error，B 无法建立自己的 page session。
   - 现有测试只覆盖首次 401，不覆盖已有 A Cookie 后打开 B 的 403 路径。

4. **正式 MES 的 account 参数没有贯通握手，且仍要求用户实现额外身份映射器。**
   - `wwwroot/embedded-handshake.js:32-66,70-102` 的建立/续期 API 不接收 account，也未把 account 传入 challenge、assertion、exchange/renew。
   - 因此正式链路无法以页面 account 调用服务端 `IEmbeddedCurrentUserAdapter` 完成“参数账号 == 当前 MES 登录用户”的二次确认。
   - `Program.cs:59-62` 正式模式仍注册 `NotConfiguredEmbeddedSubjectIdentityMapper`，要求用户额外实现平台身份映射，不符合合同约定的最小接入面。

5. **PostgreSQL Standalone 仍不是单一公共 Schema。**
   - `DatabaseTopologyResolver.cs:68-72` 在 standalone + ReferenceData 时把 schema 强制改为 `reference_data`。
   - `ReferenceDataInitializationLedger.cs:19,30,46,143,169` 继续固定创建并使用 `reference_data.*`。
   - Identity、SystemData、Collaboration 使用配置的公共 schema（默认 `public`），所以同一数据库内仍是至少两个 schema，违反单一公共 Schema 合同。

## 关键证据缺口

1. **初始化行为证据不足。** `Security_StandaloneInitializationTests.cs:13-31` 只是同一测试进程、同一个 locker 实例的两次获取，不是两个独立 OS 进程，也没有从两个 CWD 解析同一 SQLite、四模块实际初始化、重复启动保留消息的行为验证。
2. **页面生命周期证据不足。** 当前 `collaborationRuntime.spec.ts` 未注入 router；没有证明 embedded 从 Collaboration 路由离开会断开，也没有 `CollaborationMediaHost` 的 route leave / `pagehide` 自动化验证。代码方向合理，但合同要求的行为证据未闭环。

## 本轮新鲜验证结果

- 后端关键定向测试：`34 passed / 0 failed / 0 skipped`，退出码 0。
  - 覆盖：RemoteAssistance 在线/离线与并发绑定、Standalone initialization、Embedded handshake、Embedded adapters。
- 前端：由于仓库脚本把传入测试路径放在 `--` 之后，实际执行了完整 Vitest：`135 files / 1038 tests passed`，退出码 0。
- 开发方保留的后端完整测试日志：`%TEMP%/industrial-platform-pf06-full-test-final.log`，SHA256 `8199DF6616CAF4627238E1EC97945D923034858BD95A11BD3A693302BF99B6CD`。
  - 日志分项为 BuildingBlocks 170、Collaboration 147、Gateway 14、Identity 621、Integration 12（另 7 skipped）、ReferenceData 258、SystemData 633、UnifiedHost 22。
  - 分项合计实际为 **1877 passed / 7 skipped / 0 failed**，不是开发报告中的 1875。
  - 日志没有记录 fresh build 命令和 build 退出码；现有二进制时间晚于本轮审查的相关源码修改时间，可证明测试使用了修复后的编译产物，但不能替代完整可审计的 fresh Release build 日志。

## 未覆盖环境

本轮没有真实浏览器、真实 MES、Redis 或 PostgreSQL 环境验证；没有重启服务、停止 IDE/dotnet、提交、推送或触碰 PF06A。上述未覆盖项不得由静态检查或旧媒体证据替代。
