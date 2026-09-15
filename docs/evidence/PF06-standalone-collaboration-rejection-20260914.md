# PF06 独立协作接入首轮独立验收

- 日期：2026-09-14
- 结论：**FAIL / 退回原开发任务整改**
- 基线 HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`
- 开发交付证据：`docs/evidence/PF06-standalone-collaboration-20260914.md`
- 开发交付清单：`docs/evidence/PF06-standalone-collaboration-manifest-20260914.txt`
- 清单核验：28 项，`MISSING=0`，`MISMATCHES=0`
- 开发证据 SHA256：`AEBD3A1504B4898E203127AAD6D8D5B17C71EFC4A46A18370079AB39082A9844`
- 清单 SHA256：`EDD30CF36D0A870D56F73D0112E720237B78EBF6ED919460440EE3725C2866C7`

## 阻断项

1. **嵌入式心跳会周期性重启实时连接。** `src/frontend/src/systemData/runtime/collaborationRuntime.ts` 在心跳后更新会话到期时间，同时把 `expiresAt` 纳入身份键和 watch；每次续租都会走聊天清理和 SignalR stop/start。SignalR 用户声明中的 `exp` 又是建连时快照，不能用周期重连掩盖长会话媒体授权问题。
2. **离线媒体的服务端判断未实现。** `RemoteAssistanceService.RequireActivePeerAsync` 只检查人员目录的 `Status == Active`，未检查 Presence 中是否至少存在一条有效通讯页面连接。因此语音、共享我的屏幕、请求对方共享三类离线邀请没有合同要求的后端拒绝。
3. **独立页面生命周期未与平台应用外壳分离。** embedded 模式仍在应用级安装 Collaboration runtime；离开聊天路由不会停止连接。媒体宿主卸载只清本地状态，Hub 断开只移除 Presence 和内存绑定，不结束对应的持久化媒体会话。
4. **正式 MES 接入面与合同不一致。** 正式 embedded 模式没有把页面 `account` 传给服务端并与 MES 当前登录二次确认；`IEmbeddedCollaborationAccessAdapter` 反而要求用户实现细粒度权限投影和 `HasPermissionAsync`，违背“用户只接当前登录确认与 people Search/Get、宿主映射最小协作权限”的约定。README 中向 `/api/v1/embedded/session` 发送 `X-MES-Session` 的示例也没有对应的服务端读取实现。
5. **Standalone 数据库拓扑绕过原平台保护。** 模板固定 `EnvironmentName=Development`，`StandaloneConfiguration` 将其投影给 `DatabaseTopologyResolver`，使正式 Shared 被伪装为 Development。PostgreSQL 的 `DatabaseName` 还可能与连接串实际数据库不一致；ReferenceData 使用 `reference_data` schema，其他模块使用 `current_schema()`，不满足单一公共 schema 的合同。
6. **跨进程初始化安全没有成立。** 宿主只按模块串行，`InProcessServiceInitializationInvoker` 只做进程内去重；Identity、SystemData、Collaboration 的迁移流程均缺少覆盖同一物理目标的跨进程协调。ReferenceData 的局部 gate / PostgreSQL advisory lock 不能覆盖全部四模块。
7. **后端新鲜门禁缺失。** 已报告的后端 4/4 来自 2026-09-14 11:43 的既有 Release 测试 DLL，不代表最终源码。fresh test/build 因 workspace 内 `MSB3491`、`MvcTestingAppManifest`、static web assets 和 `CS2012` AccessDenied 失败；本轮不能宣称最终源码已编译通过。失败原因仍需区分 sandbox 写权限与真实进程锁，不能通过停止用户 IDE 或禁用 StaticWebAssets 绕过。

## 必须补齐的行为证据

- 同源 A/B/C 页面身份隔离，未知、重复、无参数账户，以及正式参数防冒充。
- 离线文字留言、未读恢复，以及三类离线媒体的服务端拒绝。
- 同账户多连接、首个接听终端、其他终端取消、信令只到选中终端、关闭参与页结束媒体。
- 连续超过 60 秒不断线；停止心跳后凭据和 Presence 有界失效。
- 配置优先级、两个 CWD 指向同一 SQLite、重复启动保留消息、两个独立进程并发初始化。
- 独立正式拓扑可用，同时平台原有 Shared 非 Development 仍拒绝；PostgreSQL 目标/schema/锁至少做定向验证。
- 最终源码的 fresh Release build、受影响测试、完整命令、退出码和日志路径。

## 验收边界

本轮未修改生产代码、未提交、未推送、未重启服务、未停止 IDE/dotnet 进程、未触碰 PF06A。真实浏览器、真实 MES、Redis/PostgreSQL 和生产发布仍需在环境具备时单列验证，不能由静态检查或旧媒体证据替代。
