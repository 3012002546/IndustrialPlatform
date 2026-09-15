# PF06 独立协作接入第四轮独立增量验收

- 日期：2026-09-14（Asia/Taipei）
- 结论：**FAIL（实现链已修复，但合同要求的跨原 expiry 实际媒体调用证据仍缺失）**
- 冻结基线 HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`
- 最新开发交接 SHA256：`162EA2DDFB4345B1BE6608837439C254CC32035F9612606BE9D8999673CB2764`
- 最新 manifest SHA256：`A7A92CE8CCB17E24892C51238D2BA1F6BA430F0DFC1E781BE99EA10AAB40EDF5`
- manifest 独立核验：`MISSING=0`、`MISMATCH=0`
- 验收范围：只复核第三轮剩余的 expiry/媒体租约、撤销、pagehide、完整独立初始化证据。

## 逐项结论

| 项目 | 结论 | 证据 |
| --- | --- | --- |
| fresh expiry 传播至 Hub principal | **PASS（实现与定向测试）** | `EmbeddedHubSessionValidator.ValidateAsync` 返回存储中的当前 `ExpiresOn`；`CollaborationHubSessionFilter.ApplyValidation` 在每次 invocation 前替换 principal 的 `exp`。 |
| 当前 connectionId 的媒体租约刷新 | **PASS（实现与定向测试）** | filter 调用 `MediaContextRegistry.RefreshConnectionLease`；该方法只更新匹配 connection、只延长不缩短、不影响 peer endpoint。 |
| 撤销后下一次 Hub 调用拒绝 | **PASS** | `Security_EmbeddedHostHandshakeTests` 撤销存储 session 后再次执行 filter invocation，得到 `HubException("unauthorized")`。 |
| 真实 pagehide 事件清理 | **PASS** | 独立补跑 `CollaborationMediaHost.spec.ts`：挂载组件、派发 `pagehide`，`media.endAll` 与 `realtime.stop` 均被调用，`1/1` 通过。 |
| 多进程 EmbeddedHost 初始化与重复启动 | **PASS（SQLite）** | 三个独立 EmbeddedHost 进程使用不同 CWD 和同一 SQLite；均到达 `Application started`。前后均为 109 张表，四模块 migration ledger 为 `24/58/11/13`，seed ledger 为 `2/9/2`；首次启动已有 outbox 消息在 B/C 并发重启后仍存在。 |
| heartbeat 后跨原 expiry 的实际媒体调用 | **FAIL：证据未实现** | 新测试只检查数据传播，没有推进到原 expiry 之后，也没有执行真实 Hub/RemoteAssistance 媒体方法。 |

## 剩余验收阻断

`Security_EmbeddedHostHandshakeTests.Hub_validation_uses_heartbeat_expiry_refreshes_bound_media_and_rejects_revoke` 的实际步骤是：

1. 建立默认 60 秒 session，并用原 `ExpiresOn` 绑定 registry（约 `Security_EmbeddedHostHandshakeTests.cs:95-128`）。
2. 立即 heartbeat，断言存储 expiry 变大（`130-140`）。
3. 立即执行 `CollaborationHubSessionFilter.InvokeMethodAsync`，但传入的 `next` 只返回 `null`，并未调用 `CollaborationHub` 或 `RemoteAssistanceService` 的媒体方法（`142-149`）。
4. 只断言 principal `exp` 与 registry endpoint expiry 已替换（`151-154`）。
5. revoke 后再次执行 filter 并断言 `HubException`（`156-160`）。

因此，该测试证明了“传播”和“撤销拒绝”，但没有证明合同明确要求的“推进超过原始 expiry 后，现有媒体的 bind/signal/media-ready/keepalive 仍得到成功结果”。代码静态链路看起来正确，本轮没有复现新的生产代码缺陷；当前 FAIL 是缺少必须的组合行为证据，不能用两个局部状态断言替代。

### 最小补证要求

只需增加一条定向行为测试，无需再次修改生产代码或重跑全量：

- 使用很短的初始 session lifetime 建连并完成实际媒体绑定；
- 在原 expiry 前 heartbeat，随后推进/等待到原 expiry 之后且新 expiry 之前；
- 通过实际 Hub invocation 或 `RemoteAssistanceService` 调用验证至少 signal、media-ready、keepalive 成功；
- revoke session，验证下一次实际 Hub invocation 为 unauthorized，且不会继续刷新 endpoint lease。

## 验证记录

- Round 4 fresh Release build 日志：0 warning、0 error。
- 开发定向 Collaboration 日志：42/42 通过。
- 本轮独立补跑 expiry/lease 两条定向测试：2/2 通过。
- 本轮独立补跑真实 pagehide 组件测试：1/1 通过。
- `PF06-frontend-unit-20260914.txt` 实际仍记录旧结果 `135 files / 1043 tests`，早于新增的 `CollaborationMediaHost.spec.ts`；交接中“136 files / 1044 tests 且该日志包含 pagehide”的陈述不准确。本轮 1/1 定向通过已独立补足 pagehide 证据，但不能据旧日志宣称新源码前端全量为 136/1044。

## 环境边界

SQLite 完整初始化链已验证；PostgreSQL advisory lock、Redis、RabbitMQ、真实 MES、真实浏览器双机和生产发布仍未覆盖。未修改生产代码、未提交、未推送、未重启既有服务、未停止 IDE/dotnet、未触碰 PF06A。
