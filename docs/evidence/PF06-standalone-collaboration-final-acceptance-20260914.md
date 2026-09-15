# PF06 独立协作接入最终独立验收

- 日期：2026-09-14（Asia/Taipei）
- 结论：**PASS（约定范围内通过）**
- 冻结基线 HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`
- 最新开发交接 SHA256：`86F2FABEAD6C616F6E42363AA893DA81E60EA3C0A88E896859A65A3253E16014`
- 最新 manifest SHA256：`8B8BE92AA82D6CC1A7FE208BFD5529784B1825BBCDE2C15E2FA266D885373D0C`
- 最终组合测试日志 SHA256：`40E761A88A76D31D69C7C8375223A5C285C9135F5F08DE16D575993A2F3FE98C`
- manifest 独立核验：`MISSING=0`、`MISMATCH=0`

## 最终关闭项

1. **续期后的新鲜 expiry 与媒体租约：PASS。**
   - Embedded validator 每次 Hub invocation 读取当前存储 session，返回当前 `ExpiresOn`。
   - Hub filter 在放行前更新 principal `exp`，并只延长当前 `connectionId` 的媒体 endpoint lease；不缩短租约、不影响 peer endpoint。
   - 最终组合测试使用短 session lifetime，在原 expiry 前 heartbeat，推进到旧 expiry 之后、新 expiry 之前，通过真实 Hub 与 `RemoteAssistanceService` 完成 `BindMedia → SignalMedia → MediaReady → KeepAliveMedia`，全部成功。

2. **撤销后的下一次调用：PASS。**
   - 同一组合测试在 revoke 后再次执行真实 Hub invocation，session filter 返回 unauthorized；业务 delegate 未执行，现有 endpoint lease 未继续刷新。

3. **页面销毁：PASS。**
   - `CollaborationMediaHost.spec.ts` 挂载真实组件并派发 `pagehide`，验证 embedded 页面调用 `media.endAll()` 与 `realtime.stop()`；独立定向执行 `1/1` 通过。
   - embedded 路由离开已有行为测试；HTTP 模式路由变化不停止平台应用级实时连接。

4. **Standalone 多进程完整初始化与重复启动：PASS（SQLite）。**
   - 三个独立 EmbeddedHost 进程从不同 CWD 使用同一绝对 SQLite 文件，均在四模块初始化完成后到达 `Application started`。
   - 首次及 B/C 并发重复启动后均为 109 张表；Identity/SystemData/ReferenceData/Collaboration migration ledger 保持 `24/58/11/13`，seed ledger 保持 `2/9/2`。
   - 首次启动已存在的 `SystemData.PermissionReconciled.v1` outbox 消息在重复启动后仍保留，未被清空或覆盖。

5. **其余第二、三轮问题：PASS。**
   - FixedDemo 同源 A/B/C 页面凭据隔离。
   - 正式 MES account 贯穿 challenge、assertion、exchange、renew，并由服务端当前用户适配器核验。
   - 正式接入只需 MES 当前用户及目录 Search/Get；宿主内置稳定身份映射与最小协作权限。
   - Standalone PostgreSQL 四模块读取同一个配置 schema；平台原有 Shared 非 Development 保护保持。
   - 离线媒体服务端拒绝、同账号多连接、断连只结束当前连接媒体等既有修复保持通过。

## 原平台聊天/语音/屏幕共享隔离检查

- 平台宿主注册 `AllowAllCollaborationHubSessionValidator`；其结果没有 `TokenExpiresOn`，因此新 Hub filter 对平台 principal 与媒体 lease 是 no-op。
- `RefreshConnectionLease` 只在 embedded validator 返回当前 expiry 时触发，且只按当前 `connectionId` 更新 endpoint。
- 前端路由、heartbeat 与 pagehide 收口均受 `authMode === 'embedded'` 条件保护；HTTP 平台路径继续使用应用级 Collaboration 连接。
- Standalone 数据库例外由显式 `IsStandalone` 控制；平台 Shared 非 Development 仍拒绝。ReferenceData 非 standalone 继续使用原 `reference_data` schema。
- Round 4 后未发现原平台路径被改写或放宽的证据；遵照用户红线，没有重复执行已实测通过的平台聊天、语音、屏幕共享矩阵。

## 验证证据

- 后端完整基线：`1879 passed / 7 skipped / 0 failed`。
- Round 4 fresh Release solution build：`0 warning / 0 error`。
- Round 4 Collaboration/Embedded 定向：`42 passed / 0 failed / 0 skipped`。
- 最终组合测试项目 fresh Release build：`0 warning / 0 error`；目标测试 `1 passed / 0 failed / 0 skipped`。
- 前端既有全量：`135 files / 1043 tests passed`；新增 pagehide 定向 `1/1` 通过；生产 build 通过。
- 冻结交接、清单、构建/测试日志、初始化 stdout/stderr 与 before/after 数据探针均已纳入最新 manifest，全部文件存在且 SHA256 相符。

## 未覆盖边界

以下内容没有被本次 PASS 扩大声明：真实 MES 接口联调、PostgreSQL advisory lock 实跑、Redis/RabbitMQ、真实双机与跨网/TURN 媒体、生产部署。它们仍应在相应环境具备后单列验证。

独立验收未修改生产源码、未提交、未推送、未重启既有服务、未停止 IDE/dotnet、未触碰 PF06A。
