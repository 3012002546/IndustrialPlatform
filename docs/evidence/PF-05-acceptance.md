# PF-05 独立验收证据

## 1. 验收身份与交付基线

- 验收任务：PF05 独立验收，`gpt-5.6-sol high（高）`。
- 主控任务：`01a07e50-cf88-7252-ac12-bd5fb54998b8`。
- 功能开发任务：`01a07e55-a422-7072-8c4c-56c4f143e0ab`。
- 唯一工作线：`D:/Code/Industrial Platform/IndustrialPlatform-worktrees/pf-05`；分支 `work/pf-05-collaboration`。
- 初始只读快照：`f86415dab3161eca46e1d20a230b681dfce6fe9c`；核验时 `git status --porcelain=v2 --branch --untracked-files=all` 除分支头外无改动。
- 开发稳定交付：`PF05-G05-precontract-f86415d-evidence-v1`，证据 `docs/evidence/PF-05.md`；已完成独立交叉核对。
- 当前阶段：生产准入门禁复核完成。门禁未关闭，未开始生产代码验收、构建、测试、服务启动或真实页面验收。
- 权限预检：工作线位于可写根内；主仓仅作只读参照；网络受限；系统强制审批由当前自动审查策略处理。未修改生产代码、私有配置、主工作树或运行中服务。

## 2. 已读取基线

- `AGENTS.md`、`docs/agents/EXECUTOR.md`、`docs/status/CURRENT.md`；主仓 `CLAUDE.md` 仅作 PF-02 历史角色指引，未用于扩张本次范围。
- `docs/tasks/pending/PF-05.md`。
- `docs/implementation/STANDARD-派遣前详细设计与页面验收.md` D01～D08及页面/协作门禁。
- `docs/implementation/08-Industrial Platform Collaboration开发实施方案.md` §1～18和 TASK-PF05-001～008 全部引用章节。
- `docs/implementation/details/PF05-数据接口与页面规格.md` §1～6。
- 管理页技能及本地验收、表格选择参考；当前黄金页和 `AppQueryPanel`、`AppDataTable`、`AppFormDrawer` 已读。

当前仓库的 `AppDataTable.vue` 已公开 `row-click`、`activeRowKey`、`selectedRowKey` 和 `selection-change`。后续以当前实现为准：W05-01～03 使用专用消息区，W05-04 才使用管理查询/表格/业务抽屉；不把消息时间线套成 CRUD 表。

## 3. G05-1～3 独立复核

开发交付中的代码路径、类型/字段、成功/拒绝样例及环境未核验声明已逐项与基线 `f86415dab3161eca46e1d20a230b681dfce6fe9c` 交叉核对。三个 Gate 的开发结论与独立取证一致；本节是独立验收结论，不授权开发越过门禁。

### G05-1 Identity、外部参考宿主、权限与菜单：BLOCKED

现行证据：

- Identity 管理目录的 `UserSummary` 含 `Email`、`Phone`、直接/组/有效角色等管理字段（`UserManagementContracts.cs:90`）；列表/详情端点使用 `identity.user.view`（`UsersController.cs:35,99,218`）。这不是普通聊天人员搜索可直接下发的最小目录。
- 现有 `IIdentityUserDirectory` 只有按 `(tenantNId,userNId)` 的 `GetAsync`，没有受限搜索、游标或 `canStart` 投影（`IIdentityUserDirectory.cs:13-20`）；HTTP 实现仍调用管理端 `GET api/v1/users/{id}`（`HttpIdentityDirectoryClient.cs:61-73`）。
- Identity 有 OIDC/SAML SSO 与平台 SSO Client，但当前源码未找到已冻结的 Embedded 参考宿主身份/目录端口、`IdentitySourceNId + ExternalTenantNId + ExternalSubject` 可信交接协议或主体 epoch 契约。这里需要关闭的是外部受信语义；Collaboration 自身 Platform/Embedded 适配器尚未实现属于 PF05 待开发，不作为循环前置。
- SystemData 模块注册会调用 `IIdentityPermissionRegistry.VerifyAsync`；UnifiedHost 实现只验证 Identity 目录中已经存在的权限，不创建缺失权限。现有规格列了 Collaboration 权限，但未冻结全部 permission declaration 的父子/资源类型、ResourceNId、RouteName、NavigationNodeNId、标题 key、终端和排序。权限、路由与菜单的生产注册属于 PF05 待实现；缺少精确 manifest 是派遣前设计缺口。

应有成功样例：同租户、有效且对调用者可见的用户搜索只返回 `userNId/displayName/canStart` 等最小字段；平台/Embedded 可信主体均可稳定映射 TenantNId/UserNId 与安全版本；Collaboration 权限、资源和菜单注册取得真实已验证回执。

应有拒绝样例：跨租户/不可见/不存在统一 404，停用目标稳定拒绝，目录不可安全确认返回 503；普通用户不能取得邮箱、电话、角色清单；浏览器自报主体、过期/重放宿主票据、缺失 Identity 权限目录项和未验证菜单 manifest 全部失败关闭。

最小关闭方案（交总控决定）：冻结一个普通聊天专用的最小 Identity 搜索+按 NId 查询公开契约及 Platform/Embedded 受信身份语义；明确 Collaboration 全量权限与资源/导航 manifest。具体适配器、权限种子、路由和菜单实现由 PF05 生产阶段完成并验收，不要求门禁前先实现。不得复用管理员 `UserSummary` 直接下发，也不得由开发临场自定外部宿主协议。

### G05-2 File 授权、引用与多 Case 保全：BLOCKED

现行证据：

- `FileObjectV1` 已提供 FileNId、文件元数据、ScanStatus、Restricted、DeletionStatus、RetentionUntil、OwnerUserNId 和引用摘要，但没有成员授权 DTO 或 Case 级保全标识（`FileContracts.cs:73-103`）。FileStateVersion 缺失已被规格接受为受权即时查询、FileObservedOn、单飞重查和安全降级，不要求新增或伪造源版本。
- `OpenFileContentAsync` 只允许文件 OwnerUserNId，或要求用途引用的 `OwnerUserNId` 等于当前调用用户（`FileService.cs:251-265`）。
- 新增引用把 `OwnerUserNId` 固定为当前调用 `userNId`（`FileService.cs:275-285`）；HTTP 引用端点要求 `systemdata.file.manage`，下载要求 `systemdata.file.download`，并把当前登录用户直接传给服务（`FilesController.cs:195-213`）。
- 当前只有文件级 `RetentionUntil` 和活动引用阻止删除；源码未找到按 `CaseNId` 幂等创建/释放、多个 Case 并存或解除一个 Case 后仍保持其他 Case 的公开契约。

应有成功样例：受信 Collaboration 服务携带已验证 actor/member scope，为会话双方建立/撤销业务访问并取得短期下载授权；两个独立 Case 同时保全同一附件，释放 Case A 后 Case B 仍阻止清理。

应有拒绝样例：非成员、跨租户、用途/上传者/会话不匹配、扫描非最终安全、撤回/处置后、陈旧授权、普通用户伪造服务身份全部拒绝；不能使用上传者身份代理另一成员下载。

最小关闭方案（交总控决定）：PF-04/File 需提供受信服务身份 + 签名 actor/member scope 的业务引用/授权契约，以及 CaseNId 级幂等保全/解除/查询契约；明确 Collaboration→File 的权限、重验、撤销和成功/拒绝 fixture。`IChatFileGateway`、ChatAttachment、无版本单飞重查和撤回/处置投影属于 PF05-004 待实现。当前 004/007/008 不得生产准入。

### G05-3 Audit、短时提权与独立审批：BLOCKED

现行证据：

- `AuditFactIngestRequest/AuditFactV1` 字段与持久写入、幂等冲突、失败落盘语义存在（`AuditContracts.cs:3-32`、`AuditService.cs:33-75`）。
- 真实 HTTP Ingest 只接受 `ClaimConstants.ServiceKey == systemdata`，并强制 `ProducerServiceKey=systemdata`；`collaboration` 服务身份和 producer 会以 403 `AUDIT_TRUSTED_CHANNEL_REQUIRED` / `AUDIT_PRODUCER_UNTRUSTED` 拒绝（`AuditsController.cs:23-32`）。
- Identity 源码未找到通用短时提权 proof/assurance claim、动作/范围/主体绑定和到期契约。
- 现有数据库编排审批只服务于 DatabaseProvisionPlan；`IsApprovedForAsync` 只检查有效审批匹配计划 checksum/fingerprint，没有可供 Collaboration 导出/解除保全直接复用的申请者不得自批契约（`DatabaseApprovalService.cs:65-71`）。

应有成功样例：`ServiceKey=collaboration` 的受信调用被服务端固定 producer，签名 actor context 写入受控元数据且重放幂等；五分钟内、动作和范围匹配的提权允许一次高风险查看；非申请者对未变 checksum 的导出/解除保全完成审批。

应有拒绝样例：普通用户直写 Audit、错误 producer、伪造 actor、正文/原始 IP/UserAgent 进入 payload、过期/错范围提权、申请者自批、checksum 或版本变化后旧批准全部失败关闭。

最小关闭方案（交总控决定）：扩展 Audit 的受信生产者映射以明确接收 Collaboration，并由服务端覆盖 producer/校验 actor context；冻结 Identity 短时提权契约。绑定申请者、独立批准者、checksum、双版本和到期的审批状态机需要在派遣前完成精确设计，生产实现属于 PF05-007 本身。当前 007/008 不得生产准入。

## 4. D01～D08 独立规格复核

| 项 | 结论 | 证据与缺口 |
| --- | --- | --- |
| D01 范围 | PARTIAL | Platform/Embedded、首期包含与排除、PF-06/PF-06A 边界清楚；Embedded 真实身份/目录协议和客户样本尚未关闭，不能宣称独立装配就绪。 |
| D02 数据 | BLOCKED | §1～2 大部分表/类型/约束已展开；但主文 §7.4 必需 `HiddenThroughSequence`（实施08:471-476），完整 `conversation_member` 字段表未包含该列。不得由实现者自行补字段。 |
| D03 一致性 | PARTIAL | Sequence、幂等、锁顺序、清理/保全原则明确；隐藏恢复依赖缺失字段，File 多 Case/Identity 提权是外部依赖缺口，PF05 自身审批及若干合规状态迁移仍需精确冻结。 |
| D04 接口 | BLOCKED | A05-01～13主体清楚；但消息补拉 DTO 在 details 使用 `snapshotMaxSequence/retentionFloorSequence`（details:280），主文使用 `NextAfterSequence/HighWatermarkSequence/EarliestAvailableSequence`（实施08:407,485,494-496），字段名和 410 载荷未冻结。合规 API 仍使用 `{scope,双版本}` 占位而非精确 DTO；File/Audit/step-up 端口未关闭。 |
| D05 页面 | PARTIAL | W05-01～04 线框、区域、加载/保存、终端和异常态较完整；合规路由在 details 定义主路由+`legal-holds/exports/retention`（details:348），主文候选为 `search/exports/legal-holds`（实施08:1245-1247）。还缺精确 RouteName/ResourceNId/NavigationNodeNId/title key/排序。 |
| D06 工程 | PARTIAL | 前端模块目录、单 Host/三模块、Schema/ledger 和共享写入顺序有基线，当前 `AppDataTable` 能力已核对；外部依赖端口及 Collaboration manifest/共享文件精确变更范围未关闭。Collaboration Host/API/Hub/页面本身尚不存在是 PF05 待实现，不单独判为前置依赖失败。 |
| D07 验收 | READY（设计层） | §13、细化 §5 已覆盖权限、边界、故障、真实浏览器、Platform/Embedded、隔离环境和 2C4G；尚未执行，执行结果必须与未跑/失败/跳过分开。 |
| D08 派遣 | BLOCKED | 001～008 九字段卡和整体交付方式存在，但工作包仍为待派遣/待前置核验；G05-1～3和上述 D02/D04/D05 冲突未关，不得转已就绪或开始生产编码。 |

## 5. 稳定交付后的独立验收清单

1. 交付标识与范围：记录 commit 或明确 dirty snapshot；审查生产 diff、未跟踪/忽略/暂存文件和禁止范围，确认无 PF-06、原生打包、其他 PF、私有配置或构建产物。
2. 后端门禁：源码有变化时先 fresh `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`，成功后 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`；记录退出码、通过/失败/跳过数，不能使用陈旧产物。
3. 前端门禁：按影响运行定向 Vitest、typecheck、lint、Prettier、build；核对聊天专用 Store/连接单例、合规 Store 隔离、submit guard、迟到响应与主体 epoch。
4. 真实环境：先复用当前 UnifiedHost/Gateway；仅缺失时按仓库命令启动必要隐藏后台服务并留 PID/日志。写入测试只使用隔离库或隔离租户。
5. 页面：真实菜单进入 W05-01～04；中英、三主题/明暗、舒适/紧凑、PC/PDA/Mobile 视口、窄屏/横屏、键盘/IME/扫码、焦点/ARIA、empty/loading/error/401/403/404/409/410/429/503、控制台和网络。
6. 业务链：两用户文本→重放/未知结果→顺序/补拉→已读/撤回/隐藏；多连接 Presence；真实 File 扫描/授权/撤销/多 Case；真实 Audit、提权、双人审批、保留恢复；Platform/Embedded 和 2C4G 分开结论。

## 6. 有行号的最小设计修订清单

| 修订位置 | 最小修订，不替总控决定新契约 |
| --- | --- |
| `details/PF05-数据接口与页面规格.md:65-82` 对照实施08 `:471-476` | 在完整 `conversation_member` 字典补 `HiddenThroughSequence` 的逻辑/物理名、i64 类型、初值、约束、推进与并发规则；或由总控明确删除主文该机制并给等价恢复规则。 |
| `details/PF05-数据接口与页面规格.md:280` 对照实施08 `:407,485,494-496` | 只保留一套 A05-06 响应字段：统一 `nextCursor/NextAfterSequence`、`snapshotMaxSequence/HighWatermarkSequence`、`retentionFloorSequence/EarliestAvailableSequence` 的正式 JSON 名称与三种查询模式；补 410 data 的精确字段、类型和样例。 |
| `details/PF05-数据接口与页面规格.md:297` | 把 `{scope}`、`{双版本}` 和“均带对应 checksum/双版本/原因”展开为逐端点 Request/Response DTO、字段类型/可空/上限、权限、状态码与稳定错误；同步主文权限清单 `实施08:951-973`，纳入 approve/review 权限。 |
| `details/PF05-数据接口与页面规格.md:244,348` 对照实施08 `:1245-1247` | 冻结 W05-01～04 的 RouteName、ResourceNId、NavigationNodeNId、父节点、title locale key、RequiredPermissionNId、终端和 DisplayOrder；统一合规主路由及 `search/legal-holds/exports/retention` 子路由，不再保留两套候选。 |
| `details/PF05-数据接口与页面规格.md:274-276,369` | 冻结 Identity 最小目录 Search/Get 端口的认证方式、可信 tenant/user 来源、请求/响应字段、游标、可见性与停用缓存时限，并写出同租户成功、跨租户/不可见/停用/依赖不可用拒绝样例。PF05 适配器随后实现。 |
| `details/PF05-数据接口与页面规格.md:288-290,370` | 冻结 File 受信 `collaboration` 服务身份、actor/member scope、业务引用/重新授权和 CaseNId 多保全/解除的实际公开映射与拒绝码；保留 `FileStateVersion=null` 的即时重查方案，不要求新增源版本。 |
| `details/PF05-数据接口与页面规格.md:297,371` | 冻结 Audit `producer=collaboration` 受信接收/actor context 语义，以及 Identity step-up proof 的主体、租户、动作、范围、签发/到期和重放边界；PF05 自身审批状态机按 §2.6/2.7 补完整迁移表后在 007 实现。 |

## 7. 当前结论

- 开发交付复核：`PASS`（仅指 G05 证据准确性与交付卫生；不表示 Gate 通过）。
- 生产准入：`BLOCKED`。
- G05-1：`BLOCKED`；G05-2：`BLOCKED`；G05-3：`BLOCKED`。
- D01/D03/D05/D06：`PARTIAL`；D02/D04/D08：`BLOCKED`；D07：`READY（设计层）`。
- 现有依赖缺口：Identity 最小目录可信调用/Platform-Embedded 受信身份语义；File 受信成员授权与多 Case 保全；Audit `producer=collaboration` 与 Identity step-up。
- PF05 本身待开发：Collaboration Host/API/Hub、Platform/Embedded 适配器、权限/路由/菜单注册、`IChatFileGateway`/ChatAttachment、合规导出/Case/独立审批状态机。它们不作为必须先实现的循环门禁，但必须基于已冻结规格实现后再验收。
- 规格冲突：`HiddenThroughSequence`、消息补拉 DTO/410、合规精确 DTO/权限、合规路由与完整 manifest。
- FileStateVersion：缺失不单独阻塞，执行既定即时受权查询与单飞重查方案。
- 生产验收：未开始。
- 构建/测试/浏览器/外部环境：未运行；不是失败，也不是通过。
- 工作树卫生：本阶段只有 `docs/evidence/PF-05.md` 与本文件两个未跟踪证据文件；未发现生产代码、测试代码、配置或主工作树变更。
- 下一步：把本结论一次性交总控。由总控冻结最小跨服务契约并回写受影响规格后，开发才能重新核验 Gate；门禁关闭前不开始 TASK-PF05-001～008。

## 8. 工作线切换记录

- 2026-09-08：用户授权 PF05 改在主工作树 `D:/Code/Industrial Platform/IndustrialPlatform` 的 `develop` 直接开发；总控已在 `docs/tasks/pending/PF-05.md` 与 `docs/status/CURRENT.md` 记录执行安排。
- 本文件从旧 PF05 工作树只读迁入；保留原核验基线 `f86415dab3161eca46e1d20a230b681dfce6fe9c` 与交付标识。旧工作树自此停止写入，不删除、不覆盖。
- 主仓迁入时目标文件不存在；本次只新增本验收证据。主仓已有 `docs/tasks/pending/PF-05.md`、`docs/status/CURRENT.md` 修改属于总控，`docs/evidence/PF-05.md` 由开发独占，均不由验收任务覆盖。
- 路径切换不改变门禁结论：仍只做分类收敛和设计缺口复核，生产 Gate 未通过，不开始生产验收或并发构建。

## 9. 总控有限规格修订定向复核

复核日期：2026-09-08（Asia/Taipei）
复核范围：仅检查总控在细化规格 §2.2、§3.1、§4.0、§6 及实施08对应段落的有限修订；不重做前置全量核验，不运行构建、测试、服务或浏览器，不修改设计或生产代码。

### 9.1 已收口部分

- `HiddenThroughSequence` 已进入 `conversation_member` 完整字段表，并明确 Conversation→Member 锁顺序、隐藏水位采样、恢复保留水位、对方新消息/本人发送恢复显示、成员双版本推进和旧隐藏请求 409。它与 E 基组“幂等已完成先返回原结果”规则及实施08 §7.4 一致；migration 002 本来就是 Messaging/附件批次，因此该项可从原 D02 阻塞清单移除。
- A05-06 已统一为历史、补拉、窗口三种互斥模式，正式 JSON 使用 camelCase，并补齐规范 Int64 字符串、410 data 和安全恢复动作。原来的字段命名冲突大部分已经消除。
- compliance 子路由已统一为 `search/legal-holds/exports/retention`，父容器按当前主体首个可访问 Tab 跳转且全无权限时 403；该行为不再固定跳到无权 search 页。
- §6 正确保留“外部依赖缺口 / PF05 自身待实现 / 环境未核验”三类边界，未把本次文字修订冒充 G05 关闭或生产准入。

### 9.2 必要修正（一次性）

1. **A05-06 仍有两个协议歧义。** 细化规格 `:311` 使用 400 `COLLAB_MESSAGE_CURSOR_INVALID`，实施08 `:504` 仍使用 `COLLAB_SYNC_CURSOR_INVALID`，必须只保留一个公开错误码并同步契约测试。细化规格 `:306,309` 又把 `highWatermarkSequence` 定义为每次响应时的当前高水位，而实施08 `:485` 要求补到“本次高水位”；在持续写入时，客户端若追逐每页变化的当前值可能无法结束本轮同步。需明确客户端以首个补拉响应冻结本轮 target high-watermark、后续较新消息由已缓冲实时事件/下一轮补拉合并，或在请求中提供受约束的目标上界；不要留下实现者自行选择。
2. **§4.0 需按现有控制面拆成三份可实现信息。** 当前 `RegisterModuleManifestRequest` 仅承载 permissions/resources/features（`ControlPlaneContracts.cs:5-14`），资源字段只有 name、route、permission、terminals（`:46-56`）；导航节点是另一份 `CreateNavigationNodeRequest`（`:71-84`），只持久化原始 `Label`，没有 locale key。前端动态导航只有已知 NodeNId 且 fallbackLabel 匹配时才映射本地化 key（`src/frontend/src/systemData/runtime/navigation.ts:77-132`）。因此细化规格 `:320-331` 应明确：权限/资源属于 `collaboration` manifest；NodeNId/父子/order/label 属于 `collaboration.navigation` tenant seed；title locale key 属于前端 route/runtime 映射，不得作为字段发送给 SystemData。否则“沿现有注册方式”会诱导实现不存在的 manifest 字段。
3. **PC-only 分组终端与当前导航领域契约不一致。** 细化规格 `:324` 把 `collaboration.nav.pc` 标为 Pc，但当前 `NavigationNode.CreateGroup` 和组更新都固定为 Pc/Pda/Mobile（`ControlPlaneModels.cs:128-129,155-161`），运行时再按 terminal 过滤（`ControlPlaneServices.cs:413-429`）。在不扩展 SystemData 契约的前提下，应把该组的存储终端明确写成 Pc/Pda/Mobile，并说明 PDA/Mobile 因无该组子链接而由客户端剪除空组；或改用当前契约可表达的导航层级。不能要求 PF05 seed 写入实际不会生效的 Pc-only Group。
4. **PC 会话深链仍自相矛盾且缺 RouteName。** 细化规格 `:335` 说“聊天详情不新建路由”，但 `:339` 及实施08 `:1154-1155` 又明确 `/pc/collaboration/conversations/:conversationNId`。若保留深链，应像手持端一样冻结 PC RouteName（建议与命名体系一致的 `collaboration-conversation`）、继承 `collaboration.page.pc-chat`/title/permission、不新增菜单节点，并把“不新建路由”改成只指右上用户详情抽屉；若不保留，则删除两处深链声明。当前状态不能交给 PF05-006 自行猜测。

### 9.3 定向复核结论

- 有限修订结论：`PARTIAL`。隐藏水位修订可接受；A05-06、页面注册与路由仍需上述四项最小修正。
- 原门禁结论不变：G05-1～3 仍 `BLOCKED`，合规 DTO/完整审批迁移表仍未就绪，PF05 生产编码仍未授权。
- 本轮只追加本节到独立验收 evidence；没有修改细化规格、实施08、任务卡、状态文件、开发证据、生产代码或测试。

### 9.4 主控修订记录

主控随后已回写并定向关闭 §9.2 的四项意见；本次仅核对对应行，不重启整轮文档复核：

- 细化规格 §3.1 与实施08 §7.5 已统一使用 `COLLAB_SYNC_CURSOR_INVALID`，并规定客户端冻结首个成功补拉响应的目标高水位；后续页水位升高不抬升本轮目标，新消息交由缓冲事件或下一轮同步合并。
- §4.0 已明确权限/资源使用 module manifest，导航节点与原始 Label 使用 `collaboration.navigation` tenant seed，title locale key 属于前端 route/runtime 映射。
- `collaboration.nav.pc` 已按现有 Group 契约写为 Pc/Pda/Mobile 存储，非目标终端过滤子项后由前端剪除空组。
- PC 会话深链已冻结为 RouteName `collaboration-conversation`，继承 PC chat resource/title/read permission 且不新增菜单；不建路由只指右上用户详情与新建会话抽屉。

对应行的只读检查与 `git diff --check` 均通过。因此 §9.2 四项意见状态更新为 `CLOSED（设计文本）`；这不关闭 G05-1～3、合规 DTO/完整审批迁移表或生产准入门禁。

主控另记录用户已授权删除旧 `pf-05` 工作树和分支。只读检查确认旧目录不存在、`work/pf-05-collaboration` 本地分支不存在；主仓两份 evidence 保留。本验收任务不重建旧工作线。

## 10. 前置契约与合规执行补充定向审查

审查日期：2026-09-08（Asia/Taipei）
审查对象：`docs/implementation/details/PF05-前置契约与合规执行补充.md` §1～7。只审安全、持久化和可实现性闭环；未把尚未写出的依赖实现重新归类为设计阻塞，未检查旧证据或运行构建。

### 10.1 可沿用的设计输入

- RS256 固定算法/typ/audience、静态可信 key 配置、method/path/body hash 绑定、数据库持久重放表、失败关闭和新 jti 重试均为可实现边界；不复用初始化密钥或管理员 Bearer 的原则正确。
- Identity 当前 Access Token 已有 `sid` 和 `ver`（`AccessTokenFactory.cs:52-55`），可以支撑 step-up grant 对 session/security version 的绑定。受限目录最小 DTO 不暴露邮箱、电话、角色或管理员标志，符合 G05-1 的最小披露要求。
- 现有 `CreateUploadSessionRequest` 确有 SessionNId 与 Purpose（`FileContracts.cs:3-13`），现有 File 上传/扫描流程可由专用端口复用。每 Case 每 File 独立 hold 行以及 tenant+file 锁能表达多 Case 保全。
- 现有 `AuditService.IngestAsync` 在真实持久写失败时即使写入 failure spool 仍返回 503（`AuditService.cs:33-71`），因此“敏感内容只在真实 Audit 持久接收后返回”可以通过专用受信入口实现。
- W05-04 再认证使用 `AppFormDrawer`、失败保留业务表单但清密码、退出/换人/过期清 proof，符合管理页业务面板与保存所有权规则。

### 10.2 必须修正的核心问题

1. **受信上下文的 `requestNId` 没有传输来源。** §1 `:19` 的 assertion claims 不含 requestNId，`:21` 却要求构造非空 `TrustedCollaborationCall(...,requestNId)`；目录 Search/Get 等 GET 请求（`:31-36`）也没有 body requestNId，而 jti 又被 `:23-25` 明确限定为传输重放键、不得作为业务幂等键。应增加签名 claim `request_n_id` 并让所有有 body 的写请求校验 claim 与 body 一致，读请求由 Collaboration 为本次业务调用生成；或将可信上下文的 requestNId 改成明确可空的 correlation，并禁止下游把 jti 当业务键。当前定义无法按原型实现。
2. **step-up 签发前的 hash/checksum 无法由浏览器可信地产生。** Identity `/auth/step-up` 在 `:44-52` 要求浏览器提交 `scopeChecksum` 与 `requestHash`，但 `:153` 又把 scopeChecksum 定义为包含可信 tenant、服务端解析出的 messageNId/messageStateVersion 集合和 Case Revision。创建 view/export/hold 的请求（`:163-171`）既没有这些冻结结果，也没有服务端 prepare/challenge 端点，浏览器无法预先计算；即便自报一个值，Identity 也无法验证其业务含义。需增加 Collaboration 侧 prepare/challenge：服务端规范化业务请求、解析/冻结必要范围并持久或签名返回一次性 binding，再由 Identity 对该受信 binding 签发 proof；或让 Collaboration 通过受信端口向 Identity 发起签发。不能把客户端自报 hash 当安全范围证明。
3. **同一 download proof 无法跨 POST 与 GET 复用。** `:52` 明确 requestHash 包含 path，而 `:168-180` 要求 `/authorizations` POST 与 `/content` GET 作为同一意图复用同一 proof/消费回执；两个 path 的重算 hash 必然不同。应选择一种闭环：POST 首次消费 proof 后，在 Collaboration 持久化短时、单主体/会话/请求/导出绑定的一次性下载 grant，GET 只消费该 grant并重新鉴权/Audit；或 GET 使用新的 path-bound proof。若导出字段包含 textContent/attachmentMetadata，授权与实际 GET 还必须重新检查当前 `read-original`，不能只检查 export 权限。
4. **Case 与 Export 共用的 scopeChecksum 公式会破坏未来消息保全。** `:153` 把范围内 messageNId/stateVersion 集合纳入通用 scopeChecksum，同时声明 Conversation Case 覆盖未来消息。新消息或处置会持续改变集合；但 Case review/release 与 File hold 又绑定旧 checksum/revision（`:85-93,132-145`），将造成正常活跃会话的复核/解除永久冲突。应拆成至少两种：LegalHold 的不可变 `caseScopeChecksum` 只覆盖 tenant、规范 Scope 描述与不可变 scope revision，未来消息通过 Case 规则追加保全；Export 的 `exportSnapshotChecksum` 才覆盖冻结 message/version 集合与字段。View 的短时 binding 也需单独定义，不能复用 Export 冻结公式。
5. **15 分钟审批期限与异步导出完成守卫可能使任务永远无法成功。** `:139-142` 既要求 start 时审批有效，又要求完成时仍未过期；10,000 条导出还要 File 上传、扫描、绑定和远端 Audit 持久确认，没有最大执行时长或续批状态。应冻结为“审批在 start 原子消费且 checksum 未变即可继续该不可变快照”，记录 ApprovalConsumedOn/等价字段，完成时校验快照和当前安全策略；或定义可证明小于 15 分钟的执行上限与明确续批迁移。不能让慢但正常的 worker 只能反复 Failed 后新建申请。
6. **File 上传 Purpose 尚未真正做到服务端固定。** `:74` 复用的现有 `CreateUploadSessionRequest` 自带可写 Purpose（`FileContracts.cs:3-13`），而同一 internal action 又同时服务 MessageAttachment 与 ComplianceExport。需由不同固定 action/path或已持久化的 attachment/export 上下文派生 Purpose，并忽略/拒绝 public body 的 Purpose；否则实现容易把浏览器传入值或普通附件意图当成合规导出用途。

### 10.3 审查结论

- 新补充的整体分层与同 PF 顺序实现方案可继续；以上均是定义文本内部的安全/持久化闭环问题，不是因为生产代码尚未存在而重新设置的前置循环门禁。
- 受影响的 step-up、合规 view/export/hold/download 与 File upload-purpose 合同测试必须在上述定义修正后冻结；不受影响的服务 assertion 基础、目录最小投影、nonce 持久化和 Audit 固定 producer 适配可继续实现。
- 本轮只追加本节到独立验收 evidence；没有修改补充设计、旧规格、工作包、状态、开发证据、生产代码或测试，也没有并行构建。

### 10.4 六项修订定向复核

主控回写后只复核 §10.2 六项，不重新设计或扩大范围。当前补充文本已形成以下闭环：

- assertion 增加签名 `request_n_id`；用户交互同时绑定 `actor_session_n_id` 与 `actor_security_version`。Trusted context 字段已有传输来源，写操作可核验业务 request，jti 仍只负责传输重放。
- 新增 Collaboration `step-up-context` 与持久 `compliance_preparation`；服务端规范化/冻结命令和必要快照，以独立 typ/audience 的 RS256 binding 交给 Identity，并在最终命令重新校验 preparation。浏览器不再作为 action/hash/scope 的信任来源。
- 下载两步使用固定 canonical 业务意图；POST 首次消费 proof，GET 复核同一消费回执，并在 Audit 持久成功、输出首字节前原子写一次性 `download.claim`。两个阶段都重查 export，敏感字段还重查当前 `read-original`。
- Legal Hold 使用不含未来消息集合的不可变 Case scope checksum；Export 使用冻结 message/version/fields 的 snapshot checksum；View/Disposition 另按请求目标和当前版本绑定。
- Export 在 Queued→Running 原子设置 `ApprovalConsumedOn` 与 30 分钟 `RunDeadlineOn`；完成不再要求原 15 分钟批准窗口仍有效，超时有明确失败码且旧批准不能开启新运行。
- File 拆分 `file.upload.create` 与 `file.export-upload.create`，端点固定 Purpose，拒绝 body 异用途；后续动作从持久 session Purpose 派生。

定向结论：§10.2 六项更新为 `CLOSED（设计文本）`，未发现本轮修订新增的明确高风险缺陷。这只允许开发按顺序实现并做合同测试，不代表生产实现、真实环境 Gate 或整包验收通过。

### 10.5 §1/§2 静态合同验收准备

开发完成最早依赖实现并稳定交接后，独立验收优先执行以下定向检查；当前仅冻结检查点，不提前声称结果：

1. **Assertion 正例与隔离：** RS256/typ/audience/kid、规范 method/path/query/body hash、tenant/actor/request/session/version全部匹配时仅指定 internal endpoint成功；普通 Bearer、Gateway internal 路由和初始化密钥均不能进入处理器。
2. **Assertion 负例与持久重放：** 错 aud/tenant/actor/action/path/body/request/session/version、未知 key、超时/未来 token、重复 jti 均返回既定401/403；两个接收实例并发消费同 jti 只有一个成功，nonce DB 故障503且不降级内存。
3. **请求与重试绑定：** 写请求 assertion `request_n_id` 必须等于强类型 body；重试换 jti 但保持业务 requestNId，同 hash 返回原结果、异 hash 冲突；GET 使用签名 correlation，不能把 jti 落成业务幂等键。
4. **目录最小披露：** Search/Get 只返回已定义最小字段；Search 排除本人、验证 conversation.start，Get 验证 read 并可返回同租户 Inactive；跨租户/不存在不可枚举，邮箱/电话/角色/admin 永不出现。
5. **目录游标与状态：** LIKE 通配符转义、NId ordinal 键集、cursor 绑定 actor/tenant/keyword/limit/末键/期限；无效 cursor 400。停用、安全版本、会话撤销使创建/发送和受权续期失败，缓存不能放行过期正授权。
6. **Preparation/binding：** step-up-context 只接受固定 action+强类型命令；同 request/hash 返回同冻结快照，异命令冲突，过期必须新 request。binding 篡改、错主体/会话/版本/audience、超2分钟均拒绝，command/snapshot 不含正文、密码或 proof。
7. **密码与 proof：** 正确密码才签发，错误密码进入现有锁定/限流；SSO 无本地方法失败关闭。proof 只存 hash，首次消费原子，同业务重试仅在有效期内返回原回执，跨 action/scope/request/actor/session/version 或再次读取原文均拒绝。
8. **日志与持久数据检查：** assertion、binding、proof、密码、正文不得进入日志/Audit/command/preparation；迁移覆盖首次、重复、drift、Shared/PerService 与 SQLite 显式路径，旧 checksum 不被改写。

## 11. PF05 全范围独立验收矩阵

准备日期：2026-09-08（Asia/Taipei）
状态：`PENDING STABLE HANDOFF`。开发正在主仓 `develop` 顺序实现，当前文件变化不是稳定验收输入；本任务不读取开发中的半成品结论、不并行构建、不修改生产代码。开发交接必须提供 commit hash 或明确 dirty snapshot 标识、完整变更清单及 `docs/evidence/PF-05.md` 的最终命令结果。

### 11.1 进入验收的固定门槛

- 开发明确发送一次稳定交接，确认停止生产文件写入和构建；若后续修复，使用新的交付标识。
- 记录 `HEAD`、branch、tracked/untracked/staged/unstaged/ignored 相关状态；区分主控设计、开发实现、验收 evidence 与构建产物，不纳入 `CLAUDE.md`、私有配置、日志、bin/obj/TestResults/node_modules/dist。
- 先审交付 diff、项目/解决方案注册、迁移/seed 版本和测试列表，再执行新鲜 Release build；不得以基线旧产物、开发口头结论或 skip 代替证据。
- 真实 E2E 只使用隔离数据库或隔离租户；复用现有 UnifiedHost/Gateway/浏览器，不停止或重启用户 IDE 和已有服务。

### 11.2 验收项目

| ID | 范围 | 独立检查与可观察结果 | 失败条件 | 状态 |
| --- | --- | --- | --- | --- |
| A00 | 稳定交付与范围 | 交付标识可复现；diff 仅落在 active/PF-05 §7 允许路径和两份 evidence；没有 PF06、原生打包、通用审批/服务身份平台、Audit Advanced 或私有配置 | 快照不稳定、越界或无法区分他人 WIP | PENDING |
| A01 | 构建与总回归 | fresh `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` 后 `dotnet test ... --no-build`；记录 exit、warning/error、passed/failed/skipped；新增 Collaboration 项目必须进入 slnx | build/test 非零、旧产物或新增项目漏注册 | PENDING |
| A02 | 前端总回归 | 在 `src/frontend` 依次执行 `test:unit`、`typecheck`、`lint`、`format:check`、`build`；按实际改动执行 Playwright | 任一失败、静默跳过或只运行局部测试冒充总回归 | PENDING |
| A03 | §1 受信服务调用 | 执行 §10.5 第1～3项：固定RS256/typ/aud/kid、路径与body绑定、request/session/version、持久nonce、两实例重放和503失败关闭 | 普通Bearer/初始化密钥可进internal；错绑定放行；nonce内存降级 | PENDING |
| A04 | Identity 最小目录 | 普通聊天者可搜未聊天同租户Active用户；Search/Get权限、最小字段、Inactive、404不可枚举、游标/限流/缓存失效均有正负合同测试 | 暴露管理字段、跨租户合并、停用后仍可创建/发送 | PENDING |
| A05 | Step-up 与 preparation | 执行 §10.5 第6～8项；服务签名binding、session/version、密码锁定、proof hash/原子消费/同请求回执、prepare快照与日志落盘均验证 | 浏览器自报hash成权威；proof可跨动作/主体/会话/期限；敏感值落盘日志 | PENDING |
| A06 | Embedded 身份 | challenge Origin+cookie+nonce+jti一次性；issuer/audience/tenant映射、稳定ext UserNId、续期、停用、换人epoch、同站BFF/顶层跳转均测试 | 裸传user/tenant、邮箱/显示名匹配、跨站cookie失败后降级无绑定 | PENDING |
| A07 | 初始化与数据库拓扑 | Identity/SystemData/Collaboration migration与seed首次/重复/drift；Shared、PerService、SQLite显式路径；服务自有schema/账本，旧checksum不改写 | EnsureCreated代替迁移、双账本重复apply、共享库误当单schema | PENDING |
| A08 | 会话与消息事实 | 20并发建会话仅一条；50并发发送得到连续已提交Sequence；事务失败无空洞；ClientMessageNId同载荷幂等、异载荷409；正文4000标量边界 | 重复会话/消息、Sequence空洞、超时重发生成新意图 | PENDING |
| A09 | 历史、补拉与投影 | 历史/cursor、after、窗口三模式互斥；首响应固定target high-watermark；410边界恢复；墓碑高版本覆盖且旧Accepted不复活 | 持续写入无限追赶、游标跨主体复用、过期历史伪装不存在 | PENDING |
| A10 | 已读、隐藏与撤回 | 读游标max不倒退；隐藏水位、恢复和新消息竞态按双版本；本人/对方行为正确；撤回2分钟、墓碑、未读重算、附件入口关闭 | 旧隐藏遮住新消息、撤回物理删除、后台同步自动已读 | PENDING |
| A11 | Presence 与实时连接 | 多设备租约20/60/15边界、最后连接离线、安全分组、subject epoch、重连缓冲与ResyncRequired；Redis/背板故障显示Unknown | 局部状态冒充全局、旧主体事件落入新Store、Presence阻断普通发送 | PENDING |
| A12 | File 上传与附件 | 固定message/export Purpose、现有tus适配、4MiB chunk/50MiB、扫描状态；两真实成员Clean下载，非成员/错绑定/撤回/处置/Restricted/恶意拒绝；不放宽普通Owner API | 浏览器选择export Purpose、冒用上传者、复制File存储/扫描、缓存授权越权 | PENDING |
| A13 | File 引用与多Case保全 | binding同载荷幂等/异载荷冲突；两个Case各自hold，释放A仍阻止删除；tenant+file锁下清理/hold竞态失败关闭；FileStateVersion=null安全重查 | Released旧引用复活、单Case释放全部、保全失败却标同步成功 | PENDING |
| A14 | Audit 受信接收 | 固定producer=collaboration、actor来自assertion、8KiB白名单payload；同event重放单事实、异载荷409；真实持久失败503；高风险内容在Audit成功前不输出 | 普通用户写Audit、正文/Token/IP/UA进入payload、failure spool冒充持久成功 | PENDING |
| A15 | Legal Hold/Disposition | Case创建立即本地保全；不可编辑范围、独立复核、自批403、释放双人审批/过期不解除、File协调可查询；处置追加事实和普通墓碑 | 动态message集合改变Case checksum、处置物理清正文、File未确认却解除保护 | PENDING |
| A16 | Export 与敏感下载 | 冻结snapshot/checksum/fields；独立批准、Queued→Running原子消费、30分钟deadline/接管；File扫描绑定+Audit后成功；一次性download.claim及read-original重查 | 旧批准导出新范围、超时仍运行、重复流输出、通用表格导出原文 | PENDING |
| A17 | 合规查看与预算 | scope/reason/step-up、readOriginal开关、31天/50页/每10分钟200条持久预算；201拒绝并提示导出；分页不能扩大scope；敏感内容不进command回执 | 无限分页绕过审批、失败时泄露正文、预算跨实例少计 | PENDING |
| A18 | 权限/资源/菜单 | Identity权限声明与SystemData Verify、manifest资源、navigation tenant seed、前端locale映射分层；用户按聊天/申请/批准角色只见有权入口，动态菜单真实打开 | 假入口、父容器跳入无权Tab、前端隐藏代替后端授权 | PENDING |
| A19 | PC 聊天体验 | `/pc/collaboration`及深链、顶部快捷窗、两区布局、局部loading/错误、快速切换旧响应丢弃、草稿/submit guard、IME/扫码、键盘/focus | 通用CRUD表替代消息区、重复连接/发送、加载遮住整页或敏感信息进详情 | PENDING |
| A20 | PDA/Mobile | 列表→会话全屏、48/44px触控、360/390宽、800x480横屏、safe-area/软键盘、返回恢复滚动、附件取消 | PC页面硬缩放、输入被键盘遮挡、主体切换保留旧Store | PENDING |
| A21 | W05-04 管理页 | AppQueryPanel + AppDataTable(selection=none, full)；原文禁通用export；AppFormDrawer再认证/结构化命令；业务动作与表格工具分离，重复提交单写 | 启用无业务依据选择、通用导出泄露、密码失败后未清空、PermissionGate绕过 | PENDING |
| A22 | 双语/主题/可访问性 | 中英、工业青/科技蓝/中性灰、亮/暗/跟随系统、PC密度；焦点、ARIA、对比、reduced-motion、非颜色状态、抽屉焦点恢复 | 硬编码单语/品牌色、焦点丢失、敏感正文被aria-live朗读 | PENDING |
| A23 | 真实拓扑与可发现性 | 识别当前 UnifiedHost 或独立Gateway模式；验证module组合、端点、health/readiness、Gateway前缀、登录用户菜单、console/network；浏览器视口最终还原 | 只测服务直连、端口假设、认证失效/服务未运行却报告页面通过 | PENDING |
| A24 | 资源与故障门禁 | 2C4G下限、连接/消息/Outbox/Inbox/导出边界、RabbitMQ/Redis/File/Audit短暂故障与恢复；skip单列原因 | mock/skip冒充真实集成、无限缓存/队列/重试、故障放行敏感操作 | PENDING |
| A25 | 最终证据与卫生 | evidence记录实际命令、URL、角色、通过/失败/跳过、截图/日志路径和剩余风险；`git diff --check`；无密钥/私设/产物入源 | 结论无证据、工作树误称clean、真实Gate缺失却标COMPLETE | PENDING |

### 11.3 修复与复验规则

- 首次稳定交付执行 A00～A25 全范围。任何缺陷直接发送原开发任务 `01a07e55-a422-7072-8c4c-56c4f143e0ab`，包含交付标识、最小复现、期望/实际和受影响矩阵项；不让用户中转。
- 修复后仅复验缺陷、修复 diff 与影响路径；共享安全组件、权限、跨模块契约、迁移或数据一致性变化才扩大对应回归，不机械重复整套。
- 同一问题两轮没有新证据时停止往返，按环境/需求/能力分类一次交主控。最终只向主控发送一次完整 verdict；真实运行条件缺失时使用 `PARTIAL`，不得用代码/单元测试完成冒充整包完成。

### 11.4 当前等待记录

- 开发 evidence 当前仅到 `PF05-001 受信调用首段`，并明确 nonce 持久化、真实目录、Host 及 002～008 尚未完成；没有整包稳定交付标识。
- 独立验收向原开发任务发送两次省略 model/thinking 的续作指令；两个短回合均回到 idle，未新增 evidence、交付消息或真实技术阻塞。为避免进度循环，已停止重复催促并一次性报告主控。
- A00～A25 保持 `PENDING`；本任务未读取不稳定生产实现、未并发构建或修改生产代码。等待主控恢复原开发任务，整包稳定交接后再执行首次全范围验收。

## 12. 验收任务重建与本轮权限预检

重建日期：2026-09-08（Asia/Taipei）
本轮验收任务：`01a07f9d-2f78-7c21-9f2e-09144882ac11`，创建配置 `gpt-5.6-sol high（高）`。
当前配对开发任务：`01a07f9e-355b-75f1-ba20-5d41f49f07ca`；主控仍为 `01a07e50-cf88-7252-ac12-bd5fb54998b8`。§1、§11.3～11.4 中旧任务 ID、旧 worktree/分支和历史等待记录只保留为当时证据，不再作为当前通信目标或工作线。

- 当前唯一仓库为 `D:/Code/Industrial Platform/IndustrialPlatform`，分支 `develop`，预检 HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`；没有新建 worktree。工作树已存在总控设计/状态改动和开发中的 PF05-001 文件，当前只记录文件状态，不读取半成品实现内容、不运行共享输出目录构建。
- 实际文件权限：仓库与本验收证据路径可写，仓库外普通路径只读；本角色只写本文件，不改生产代码。网络受限；需要越过沙箱的命令由系统自动审批策略逐项审查，未知权限不记为已授权，也不通过换工具或换任务绕过。
- `git status` 能列出 tracked/untracked/staged/unstaged；读取全局 ignore `C:/Users/DONG/.config/git/ignore` 被权限拒绝，因此本次预检不能把“未显示更多 ignored 文件”表述为完整 ignored 审计。稳定交接后的 A00/A25 将结合仓库内 ignore 规则和可访问范围重新记录。
- 已完整复读实施08、主细化规格、执行补充、active 工作包、两份历史 evidence，以及协作/管理页 Skills 和两份页面 references。A00～A25 矩阵保持有效；当前任务 ID 更新不重置任何历史闭环，也不把历史 `BLOCKED` 当作禁止本轮已授权最小 Identity/File/Audit 增量。
- 当前状态继续为 `PENDING STABLE HANDOFF`。只有开发方明确提供 commit 或 dirty snapshot 标识、完整变更清单、最终命令证据并确认停止生产写入/构建后，才开始 A00～A25 首次全范围验收；否则不读取不稳定代码、不并发构建、不把现有部分 PF05-001 当作整包交付。

## 13. 首次稳定交付独立验收与返修

验收日期：2026-09-08（Asia/Taipei）
验收输入：`PF05-dirty-f86415d-49030a97540ded85`，HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`。开发确认冻结后，独立重算 84 个 PF05 `src/`/`tests/` 文件的清单摘要为 `49030a97540ded853953fd51a269a5a68d950c89c92414a00a02a71f6c5a1a9d`，与交付一致；文件数为 backend 56、frontend 17、samples 3、tests 8，最新写入 UTC `2026-09-08T12:59:27.4559503Z`。无 staged 变更。全局 ignore 文件因权限拒绝仍不能完整审计；工作树中同时存在总控设计/状态 WIP，故只把上述 manifest 作为生产交付边界。

### 13.1 独立门禁命令

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning，0 error。 |
| Backend test | `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：exit 0，1686 passed，0 failed，3 skipped；跳过项为真实 PostgreSQL、Redis、RabbitMQ。解决方案中没有 Collaboration 测试项目。 |
| Frontend unit | 首次沙箱内因 Vite 写 `node_modules/.vite-temp` 返回 EPERM；获系统批准后同命令 `pnpm.cmd test:unit`：124 files / 926 tests passed。没有 Collaboration 专用测试文件。 |
| Frontend type/lint/build | `pnpm.cmd typecheck`、`pnpm.cmd lint` 均 exit 0；build 首次同样因沙箱 EPERM，批准后 `pnpm.cmd build` exit 0，并有单 chunk >500 KiB warning。 |
| Frontend format | `pnpm.cmd format:check` exit 1，共 146 个文件；对 PF05 新文件定向检查仍有 `src/api/collaboration.ts`、`src/api/collaborationRegistry.ts`、`src/components/collaboration/CollaborationChat.vue`、`src/pages/pc/collaboration/CompliancePage.vue` 4 个失败。 |
| Diff hygiene | `git diff --check` exit 0，仅行尾转换 warning。构建输出按 ignore 规则不作为交付文件。 |

### 13.2 阻断发现

首次结论为 `BLOCKED`，已直接发送当前开发任务 `01a07f9e-355b-75f1-ba20-5d41f49f07ca`，要求集中返修并重新冻结：

1. `A05/A15/A16/A17`：step-up binding 是客户端可自行计算的无密钥 SHA-256；Collaboration 信任客户端 action/scope checksum，缺 `compliance_preparation`、RS256 专用 binding、冻结命令/快照和同业务消费回执；Identity 直接验密码，未证明进入既有锁定/限流流程。
2. `A03/A04/A12/A13/A14/A23`：新增受信校验器没有接入实际链路。Identity、File、Audit 端口均直接调用同进程 application service，未实现冻结的 internal endpoint、发送端 assertion、接收端 path/body/request/session/version 与持久 nonce 验证；Audit 的 trusted context 参数未使用。
3. `A11/A15/A18`：通用法律保全 action 端点只要求 review 权限却能选择 release 动作；消息发送/隐藏/已读/附件使用未冻结的聚合权限；Presence REST/Hub 只要求 messaging.read；审批 proof 放在 body。
4. `A14～A17`：脱敏合规搜索无 step-up；处置只追加 disposition，不推进消息状态版本或把普通投影变成墓碑；导出授权 POST 和内容 GET 对同 proof 各消费一次，正常两步无法完成，且无持久一次性 download grant/claim；多数 Audit 在业务写入后调用，现有 outbox 仅写 message.accepted 且无 dispatcher。
5. `A08～A10`：正文只 `Trim` 并按 UTF-16 长度，缺 NFC/换行/Unicode scalar/控制与 bidi 规则；Sequence 只靠进程内 Semaphore；游标是可自行构造的 offset page + 公共 hash，无到期/密钥/末键；410 缺结构化 data，Suspended 会话连读也被拒绝。
6. `A11/A24`：Presence 仅进程内字典单状态 60 秒，缺多连接租约、20/60/15、最后连接离线、Redis/背板和 Unknown 降级；Hub/前端缺安全连接续期、subject epoch、实时缓冲与 ResyncRequired。
7. `A12/A13/A15/A16`：附件授权每次新建随机 reference，未持久绑定 30 秒授权/主体/请求/用途，内容失败会向客户端拼接内部 exception message；法律保全先标 Released 再异步解除 File 引用；导出溢出检查的 page/pageSize 组合会把第二条误当第一万零一条；worker 无多实例 claim/lease。
8. `A06/A07/A23`：Collaboration migration ledger 无 checksum/drift；health 无条件 Healthy；EmbeddedHost 只是组合现有模块，配置项未被使用，没有冻结的 Origin+cookie+nonce+jti challenge、外部主体映射、换人 epoch/BFF。
9. `A01/A02/A03～A24`：没有 `tests/Collaboration`，PF05 唯一新增专用后端测试是 Identity 目录端点 2 个 Fact；前端测试总数与既有 926 相同且无 Collaboration spec，无法支撑并发、安全、迁移、权限、状态机、真实拓扑和终端验收声明。

### 13.3 当前矩阵状态

- `A00`：`PARTIAL`，dirty manifest 可复现且无 staged，但全局 ignored 审计受权限限制，且总控 WIP 与开发交付共处主工作树。
- `A01`：`PARTIAL`，构建/总回归通过但没有 Collaboration 测试项目，高风险实现未获专用测试覆盖。
- `A02`：`FAIL`，unit/typecheck/lint/build 通过，format gate 失败且包含 4 个 PF05 新文件；未执行 PF05 Playwright。
- `A03～A18`：`FAIL`，存在上述受信、提权、授权、消息、Presence、File、合规、Embedded 和权限闭环缺陷。
- `A19～A24`：`NOT ACCEPTED`，静态实现已显示实时/终端/故障能力缺失；在 P0/P1 安全和合同缺陷修复前不启动真实业务写入与浏览器验收，不把旧测试或代码存在视为通过。
- `A25`：`PARTIAL`，独立命令和失败已记录；等待返修稳定交付后补最终证据与卫生结论。

返修规则保持 §11.3：开发给出新 delivery ID 后，先核对修复 diff、manifest 和新测试；共享安全、跨模块契约、迁移及消息一致性均被修改，因此复验范围必须覆盖对应 backend/frontend 总回归、合同测试和安全拒绝链路，再决定是否进入隔离真实环境与页面验收。

## 14. 第二轮返修独立复验与最终回写

复验日期：2026-09-09（Asia/Taipei）
独立冻结标识：`PF05-dirty-f86415d-472e41094ed37412`。HEAD 仍为 `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`；独立枚举 `src/backend/IndustrialPlatform.slnx`、`src/backend/src`、`src/frontend/src`、`src/frontend/tests`、`src/samples`、`tests` 下全部 tracked/untracked 变更并排除 `bin/obj/dist/node_modules/TestResults` 后，共 121 个文件：backend 75、frontend 19、samples 6、tests 21；逐文件 SHA-256 清单摘要为 `472e41094ed3741253baae3f6de7c8c086abc0e5a7e8087dce138370d74c80ae`，最新写入 UTC `2026-09-08T19:00:45.3926732Z`。无 staged 变更。全局 ignore 文件 `C:/Users/DONG/.config/git/ignore` 仍因权限拒绝无法完整审计；主工作树同时包含总控设计/状态 WIP，不能称为 clean。

### 14.1 第二轮修复复核

本轮确认以下首次阻断点已有实质修复并进入独立编译/测试：

- Identity HTTP 目录适配器不再使用 `system/internal/1`，而是从当前已认证主体绑定 tenant/user/session/authVersion，并原样透传 Identity 返回的 opaque cursor；Identity 入站新增普通 Bearer 拒绝、有效 RS256、篡改签名、持久 nonce 重放与不可用失败关闭测试。
- 合规 step-up 改为专用 RS256 binding；缺少专用密钥时失败关闭。高危动作使用强类型规范化命令，完整绑定 scope、fields、case reference、retention 字段与并发版本，命令原文和 hash 均持久化并在最终执行阶段匹配。
- 敏感导出下载增加执行期 `read-original` 权限复核；SystemData Audit 请求号与 AuditEventNId 绑定修正；外部 503 不再拼接内部异常文本。
- 消息 Sequence 改为数据库 CAS + 事务 + 有界重试；新增多个 repository 实例的 SQLite 并发测试。前端新增 SignalR 连接、自动重连、会话组与消息/ack/typing/presence 事件；Presence 改为逐连接 Redis lease。
- 新增 EmbeddedHost challenge/cookie/nonce/jti 样例和 Collaboration 测试项目；Collaboration 专用测试由首次 0 增至 25，前端新增 1 个 realtime 单测。

这些修复关闭了首次报告中的直接伪造 hash、HTTP Identity 硬编码主体、导出 proof 双消费、无数据库 Sequence CAS、完全缺少前端实时连接等具体缺陷，但不等于 A03～A24 的完整验收已通过。

### 14.2 独立门禁命令与结果

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning，0 error。 |
| Backend full test | `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：exit 0，1713 passed、0 failed、3 skipped；Collaboration 25/25、Identity 611/611、SystemData 609/609。3 个 skip 仍是 PostgreSQL、Redis、RabbitMQ 真实依赖测试，未算通过。 |
| EmbeddedHost Release build | `dotnet build src/samples/Collaboration.EmbeddedHost/Collaboration.EmbeddedHost.csproj --configuration Release --no-restore`：exit 0，0 warning，0 error。 |
| Frontend type/lint | `pnpm.cmd lint` exit 0；`pnpm.cmd typecheck` 首次因沙箱不能写 `node_modules/.tmp` 返回 EPERM，获受控权限后同命令 exit 0。 |
| Frontend unit | `pnpm.cmd test:unit` 首次因沙箱不能写 `node_modules/.vite-temp` 返回 EPERM，获受控权限后 exit 0：125 files / 927 tests passed。 |
| Frontend build | `cmd.exe /d /c pnpm.cmd build` exit 0；仅有既有单 chunk 大于 500 KiB warning。 |
| Frontend format | `pnpm.cmd format:check` exit 1，共 142 个文件不符合 Prettier。对 19 个当前变更的前端文件独立定向检查仍 exit 1，其中 10 个失败：`createIndustrialApp.ts`、`navigation.ts`、两份 locale、`localization/types.ts`、两份 permission catalog、两份 router 文件、`navigation.spec.ts`。 |
| Playwright / real browser | 没有 PF05 Collaboration Playwright 文件；未运行真实浏览器验收，不能把 realtime unit mock 或页面源码存在记为通过。 |
| Diff hygiene | `git diff --check` exit 0；`git diff --cached --name-only` 为空。只有行尾转换与全局 ignore 权限 warning。 |

### 14.3 仍然阻断生产准入的证据

1. **A06 Embedded 身份仍为 FAIL。** `src/samples/Collaboration.EmbeddedHost/EmbeddedHandshake.cs` 用进程内 `ConcurrentDictionary` 保存 challenge，无法证明多实例一次性消费；更关键的是 complete 请求直接接受浏览器 `ExternalSubject` 字符串，再由配置表映射平台身份，没有受信 issuer/audience 签名断言或父宿主认证证据。允许来源中的任意脚本可选择任一已配置 external subject。当前 3 个测试只覆盖 allowlist、cookie 和同进程单次消费，没有续期、停用、换人 epoch、多实例、顶层跳转或 BFF。
2. **A02 仍为 FAIL。** 仓库规定的完整 `format:check` 非零，且当前 19 个前端变更文件中仍有 10 个不符合格式；同时实际改动的 Collaboration 页面没有 Playwright 覆盖。开发 evidence §11 的“PF05 修改文件定向 Prettier 通过”与独立结果不一致。
3. **A03～A18 只能 PARTIAL。** 新增 25 个 Collaboration 测试主要覆盖 helper、反射权限、8 并发 SQLite Sequence、HTTP Identity 适配器、3 个合规服务场景和 Embedded 样例；没有完整覆盖 SystemData internal HTTP 正反例、两实例 nonce、真实 Identity/File/Audit 组合、20 并发建会话、50 并发连续 Sequence/失败无空洞、历史 410/墓碑竞态、真实 Hub/Redis/backplane、多 Case File 引用、worker 接管、持久预算、迁移首次/重复/drift 与三种数据库拓扑。不能用 1713 个全库测试中的既有测试替代这些 PF05 专属合同。
4. **A19～A24 未接受。** 本轮没有隔离租户/数据库、真实 UnifiedHost 或独立 Gateway、登录角色、浏览器视口、PC/PDA/Mobile/管理页、可访问性、2C4G、PostgreSQL/Redis/RabbitMQ、File/Audit 短暂故障与恢复证据。3 个真实依赖测试明确 skip；没有启动或接管用户现有服务，因此这些项目保持 `NOT ACCEPTED`，不是代码测试失败，也不是已通过。
5. **A25 仅 PARTIAL。** `docs/evidence/PF-05.md` 最后仍记录旧的 1702 后端通过、13 个 Collaboration 测试、926 个前端测试及 `.tmp-pf05-build` 占用说明，没有同步第二轮最终 1713/25/927 结果和新快照；工作树又含总控 WIP且全局 ignored 审计不可访问。

### 14.4 最终矩阵与结论

- `A00 = PARTIAL`：独立 dirty manifest 可复现、无 staged，但开发未给出新的 manifest 交付标识，主工作树混有总控 WIP且 ignored 审计不完整。
- `A01 = PASS`：新鲜 Release build、完整 slnx 测试和 Collaboration 项目注册均通过，实际计数 1713/0/3。
- `A02 = FAIL`：unit/typecheck/lint/build 通过；format gate 与 PF05 变更文件定向格式检查失败，且无 Collaboration Playwright。
- `A03～A05 = PARTIAL`：主要安全实现已修复并有部分正反例，但缺两实例/真实服务/完整锁定与准备消费合同。
- `A06 = FAIL`：Embedded 外部主体没有受信断言，challenge 仅进程内，完整宿主身份生命周期未闭环。
- `A07～A18 = PARTIAL`：已有生产代码与少量专项测试，但完整并发、迁移、投影、File/Audit、审批、预算、权限与菜单矩阵未被真实合同覆盖。
- `A19～A24 = NOT ACCEPTED`：缺浏览器、真实拓扑、资源下限和故障恢复证据。
- `A25 = PARTIAL`：本文件记录了独立命令和残余风险，diff hygiene 通过；开发 evidence 陈旧、format 非零且 ignored 审计受限。

**最终 verdict：`BLOCKED / NOT PRODUCTION-ACCEPTED`。** 当前实现相较首次交付已有显著安全与一致性修复，且编译、后端总回归、前端 unit/type/lint/build 都有独立通过证据；但 A02、A06 存在可定位失败，A03～A24 的真实环境与行为证据不足。按 §11.3“同一问题两轮无新证据停止往返”的规则，本验收任务不再向开发任务发起第三轮修复，不修改生产代码、不暂存、不提交、不推送；由总控决定后续新工作包与隔离环境补验。

## 15. PF05-R1 定向整改独立验收

复验日期：2026-09-09（Asia/Taipei）
验收输入：`PF05-R1-dirty-d3f3fc492c2ee9902d9a33f2230e533732ac6f66415a2161037cd6e4eea1d125`，HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`。独立按开发证据 §15 的 25 行清单逐文件重算 SHA-256，25/25 匹配；按 LF 连接且无末尾换行重算 aggregate 为 `d3f3fc492c2ee9902d9a33f2230e533732ac6f66415a2161037cd6e4eea1d125`，与交付一致。工作树仍混有总控、开发与验收 WIP；全局 ignore 文件仍因权限拒绝不能完整审计，故不称 clean。

### 15.1 独立结果

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning，0 error。 |
| Backend full test | fresh build 后执行 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --logger "console;verbosity=minimal"`：exit 0，1725 passed、0 failed、3 skipped；8 个测试程序集实际计数为 164+14+611+609+257+37+21+12，3 个 skip 仍为真实 PostgreSQL、Redis、RabbitMQ 外部依赖。 |
| Embedded 定向 | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~Security_EmbeddedHostHandshakeTests`：15 passed、0 failed、0 skipped。 |
| R1 前端格式 | 对 R1 清单中的 19 个前端变更文件执行项目本地 Prettier：exit 0，全部匹配。完整 `format:check` 已独立确认仍 exit 1，132 个文件为未在 R1 扩大的仓库基线；目标 19 文件不在失败集合。 |
| 卫生 | `git diff --check` exit 0，仅行尾转换 warning；`embedded-handshake-*.db` count=0。 |

### 15.2 A06 行为复核

- 真实 RS256 签发/验证路径覆盖独立 typ/issuer/audience/kid、nonce、JTI、Origin、browser-binding、external tenant mapping、60 秒期限、篡改/过期拒绝和两 store 实例的一次性消费。
- session token 不再通过 JSON 或 Authorization Bearer 暴露/接受；session cookie 与 HttpOnly browser-binding cookie 必须联合匹配。无绑定的新浏览器即使复制 token 也保持匿名。
- middleware 每次认证重新核对当前主体映射、平台租户、security version、主体状态和 source session 状态；停用、版本变化或 A→B 换人立即撤销旧 session。renew 重新取上游 assertion，旧 session/epoch/主体事件由明确的 epoch filter 拒绝。
- 六个 controller 入口将 `EmbeddedPersistenceException` 稳定映射为 503 `EMBEDDED_HANDSHAKE_STORE_UNAVAILABLE`；middleware 存储故障保持匿名并继续下游，不回退为认证成功。

### 15.3 R1 阶段结论

`R1 = PASS`：R1 所要求的 A06 可定位实现缺陷与负例已关闭；A02 的 19 个当前变更文件格式门禁通过，132 个非目标文件的既有全仓基线已被明确隔离，未通过改规则、ignore 或全仓格式化掩盖。允许同一开发任务按 `docs/tasks/active/PF-05-R1.md` 进入 R2。

该结论只表示 R1 阶段通过，不替代整包生产准入。真实 MES/Identity/File/Audit 多进程、真实浏览器/Playwright、2C4G、PostgreSQL/Redis/RabbitMQ、迁移库及 A03～A18 的剩余合同仍按 R2/R3 验收；当前整包 verdict 继续为 `NOT PRODUCTION-ACCEPTED`，直到后续阶段完成。

## 16. PF05-R2 合同与一致性独立验收

复验日期：2026-09-09（Asia/Taipei）
验收输入：R2 最终 dirty snapshot，HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`。独立从开发 evidence §16 提取 145 个 source/test/project/config 清单项并逐文件重算 SHA-256，145/145 匹配；按 LF 连接且无末尾换行重算 aggregate 为 `93a40eea05ace769744bca3445a351d2145c209d616462bb4b1be70bb2f13a5d`，与 evidence 一致。无 staged 文件；全局 ignore 文件仍因权限拒绝无法完整审计，工作树不能称 clean。

### 16.1 独立门禁

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning，0 error。 |
| Backend full test | fresh build 后执行 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --logger "console;verbosity=minimal"`：exit 0，1758 passed、0 failed、3 skipped；程序集计数为 BuildingBlocks 164、Gateway 14、Identity 614、SystemData 617、ReferenceData 257、Collaboration 59、UnifiedHost 21、Integration 12 passed/3 skipped。 |
| File/迁移定向 | SystemData `FileServiceTests`、`Pf04MigrationTests`、`SchemaMigrationRunnerTests` 合并筛选：22/22；Identity `SchemaMigrationRunnerTests`：7/7；Collaboration initializer、HTTP File port、并发 repository 合并筛选：12/12。均 exit 0。 |
| 真实依赖探针 | 独立 TCP 探针 `127.0.0.1:5432/6379/5672/15672` 均为 `False`；对应 PostgreSQL、Redis、RabbitMQ 的 3 个 IntegrationTests 保持 skipped，未记为通过。 |
| 卫生 | `git diff --check` exit 0，仅行尾转换 warning；`git diff --cached --name-only` 为空。 |

### 16.2 关键合同复核

- 两独立 store 的 nonce 重放与 exp+5s 保留、SystemData internal HTTP 固定 method/path/action/body/envelope/503、20 并发建会话、50 并发发送及事务失败无 Sequence 空洞均有共享 SQLite 行为测试。
- 历史 410、hide/retract 幂等与墓碑投影、Hub 调用时授权、export 双 worker 单 claim/接管/deadline/Clean gate，以及 200 条跨实例持久预算和第 201 条拒绝均进入完整回归。
- SystemData 已新增专用 `system_collaboration_file_reference` 与 `system_collaboration_file_hold` 表、冻结 internal 路径与冲突码；同文件两 Case 独立保全/释放、Released 不复活、tenant+file 条件删除与真实 Pf04Store SQLite 竞态测试通过。
- Identity、SystemData、Collaboration 均增加隔离 SQLite 的 Shared/PerService、first/repeat、ledger checksum drift 和已登记物理列/索引 drift fail-closed 测试。真实 PostgreSQL 物理目标仍留作环境验收，未由 SQLite 结果替代。

### 16.3 R2 阶段结论

`R2 = PASS WITH RUNTIME CONTINGENCIES`：R2 中可在当前隔离环境执行的合同与一致性缺口已闭环，已知多 Case 专用持久化和三模块 SQLite 迁移矩阵不再是实现缺口。允许同一开发任务进入 R3。

以下仍是 R3/最终验收条件而非 R2 伪通过项：真实 Identity/File/Audit 多进程、Redis Presence/backplane、PostgreSQL Shared/PerService 迁移、RabbitMQ、真实浏览器/Playwright、页面/角色/主题/响应式/可访问性、2C4G 与故障恢复。整包 verdict 继续为 `NOT PRODUCTION-ACCEPTED`，直到这些真实门禁执行并汇总。

## 17. 历史迁移账本 checksum drift 定向返修验收

复验日期：2026-09-09（Asia/Taipei）
验收输入：HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`；以 Identity/SystemData 两套 `SchemaMigrationRecord`、`SchemaMigrationRunner` 和对应测试共 6 个文件冻结定向 dirty snapshot。逐文件 SHA-256 按下列顺序以 LF 连接且无末尾换行重算，aggregate 为 `a036da2f0f3d23967e3cae3d6ed7e64be8a33c8883ebb8cc4cd73ebb28a94f8c`：

```text
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/SchemaMigrationRecord.cs fb28e8065bfe49a38abfdd89935d7c7e8ce6d5d9e3051ba2b1c3a2dd29bd1f4c
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/SchemaMigrationRunner.cs 90c91ba589e22a4cde28497e19810510af6d0265ce6a48c196a57554d4eb604e
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_SchemaMigrationRunnerTests.cs 15dea138277696856697e9ad98de2b0c4d398150f4c66abe2b4b85939e6b6382
src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SchemaMigrationRecord.cs b4fad7dc1116e5f7a5ed4d9a4f15f4a48c7619e14f008c1721de738b9e9eac6c
src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SchemaMigrationRunner.cs b90b99b48e436df00f641432276230e6434855ea4f0bee7041a468b110675c75
tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SchemaMigrationRunnerTests.cs 8bf5f3bff38d24c9c8a78c54635249f1010b371a20e5470e46d0d7ec897cf95f
aggregate a036da2f0f3d23967e3cae3d6ed7e64be8a33c8883ebb8cc4cd73ebb28a94f8c
```

### 17.1 独立门禁

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning，0 error。 |
| Identity 定向迁移 | `dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~SchemaMigrationRunnerTests`：10 passed、0 failed、0 skipped。首次误用 `Infrastructure_SchemaMigrationRunnerTests` 筛选时无用例匹配，未计入通过。 |
| SystemData 定向迁移 | `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~SchemaMigrationRunnerTests`：10 passed、0 failed、0 skipped。 |
| Backend full test | fresh build 后执行 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：exit 0，1765 passed、0 failed、3 skipped；程序集计数为 BuildingBlocks 164、Gateway 14、Identity 617、SystemData 620、ReferenceData 257、Collaboration 60、UnifiedHost 21、Integration 12 passed/3 skipped。3 个 skip 仍为 PostgreSQL、Redis、RabbitMQ 外部依赖。 |
| 卫生 | `git diff --check` exit 0；`git diff --cached --name-only` 为空。全局 ignore 文件仍因权限拒绝无法完整审计，故不称工作树 clean。 |

### 17.2 历史库升级与拒绝语义

- Identity 的历史 `ID-004-01` 与 SystemData 的历史 `SDM-001-01` 均覆盖两种 SQLite 账本：原三列 `migration_id/description/applied_on`，以及已存在 nullable `checksum` 但值为 `NULL`。两种情况都先确保 nullable 列存在，再在描述完全一致时回填 `SHA-256(id|description)`；对应 4 个 theory case 全部通过。
- 两套测试在旧迁移已登记后分别写入 `legacy-user` 与 `legacy-policy` 业务行，升级后业务行仍存在。运行器的回填 SQL 只更新目标账本行的 `checksum`，不更新 `applied_on`、`description` 或业务表，因此原应用时间与业务数据保持原值；本轮没有把旧迁移重新执行或重写记账时间。
- nullable checksum 配合错误描述时，两套定向测试均要求抛出 description drift，且 checksum 保持 `NULL`，没有借回填掩盖历史描述漂移。
- 非空错误 checksum 仍由 Shared/PerService 两组完整 SQLite 矩阵拒绝，并保留错误值；把 checksum 恢复为期望值后再删除已登记物理索引，运行器继续以 physical schema drift 拒绝。
- 执行顺序为按 migration ID 排序逐项处理：已登记步骤先完成描述/checksum 与 `Validate` 物理结构校验，随后才进入后续迁移。因此历史步骤漂移不会被后续迁移静默越过。

### 17.3 定向结论

`历史账本 checksum drift 返修 = PASS`：针对用户正常调试启动出现的 Identity `ID-004-01 checksum drift` 根因，旧三列账本与已补列 `NULL` 的兼容升级、业务数据/应用时间保留、错误描述拒绝、非空错误 checksum 拒绝及物理漂移顺序均已有独立代码复核和隔离 SQLite 回归证据。

该 PASS 只接受本次历史账本升级路径，不表示本验收任务接管或重跑了用户正在使用的普通调试数据库；验收没有重启/停止 IDE 服务，也没有写入该数据库。PF-05 整包仍沿用 R3 的真实依赖限制，保持 `NOT PRODUCTION-ACCEPTED`，不得由本次定向修复替代 PostgreSQL/Redis/RabbitMQ、真实页面与故障恢复验收。

### 17.4 真实 PostgreSQL 再现后的结论更正

2026-09-09 用户在正常 PostgreSQL 调试启动中再次得到 `Npgsql 42703: column checksum does not exist`。独立追踪确认，`SystemDataServiceInitializer.InspectAsync` 在协调器进入 Apply 之前即通过 `Queryable<SchemaMigrationRecord>().ToListAsync()` 按含 `checksum` 的新实体读取历史账本；旧三列 PostgreSQL 表因此在到达 §17 已验证的 Runner 补列逻辑前就失败。该异常不属于“缺表”或物理 drift 的既有 NotReady 映射，也不能由 §17 的 SQLite Runner 定向用例证明已闭环。

因此 §17.3 的 `PASS` 自本小节起**撤销并重新打开**：此前证据只证明直接调用 Runner 的兼容路径，不证明真实初始化器的 `Inspect → Plan/Apply → Inspect → 重复启动` 全链。当前定向 verdict 改为 `REOPENED / NOT ACCEPTED`，等待隔离真实 PostgreSQL 按项目 Shared/PerService/search_path 规则完成旧表缺列、nullable `NULL`、真实 drift、数据与 applied time 保留、只读 Inspect 和重复启动验证。验收继续禁止对用户普通调试库执行 DDL，也不会以 localhost 端口状态推断实际远程 PostgreSQL 不可用。

### 17.5 PostgreSQL 可达性探针更正

同日主控对项目实际有效配置目标重新执行分层探针：默认沙箱下连接被本机策略拒绝并返回 `Socket 10013`；通过系统受控权限后，对同一目标执行 `ConnectAsync` 成功。故此前任何把沙箱拒绝或 localhost 未监听直接解释为“实际 PostgreSQL 网络不可用”的推断均撤销。后续真实 PostgreSQL 验收必须使用该实际配置目标与任务专属隔离数据库/架构，并将网络连接、账号认证、`CREATE DATABASE`/schema/search_path 权限分别判定；普通调试库保持只读且不得执行 DDL。

### 17.6 第一版 PostgreSQL 整链夹具复核

开发新增 `UnifiedHost_LegacyPostgreSqlStartupChainTests`，但当前 `CreateLegacyLedgersAsync` 只创建五张空账本表，没有插入已应用迁移记录、旧业务行或固定 `applied_on`；测试仅使用 `DatabaseTopologyMode.Shared` 与 `SearchPath=public`，两次协调器运行后也只断言五个 checksum 列存在。因此它没有复现用户库中“已登记 `ID-004-01` 但缺 checksum”的真实前置状态，不能证明回填不重跑旧迁移、业务数据/applied time 保留、nullable `NULL`/错误非空 checksum 拒绝、PerService/search_path 隔离或物理漂移先于后续迁移。

本轮稳定交付的定向验收暂为 `REPAIR REQUIRED`。在补齐上述任务专属 PostgreSQL 数据与断言前，不执行重复的整套 backend 门禁，也不恢复 §17.3 的 PASS；用户普通调试库继续禁止写入。

### 17.7 第二版 PostgreSQL 整链独立复验

复验日期：2026-09-09（Asia/Taipei）

验收输入：HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`；冻结 Identity、SystemData、Collaboration 的 initializer/runner 及对应单元和 PostgreSQL 集成测试共 13 个文件。逐文件 SHA-256 以 `path:sha256` 按固定顺序、LF 连接且无末尾换行计算，aggregate 为 `d156b9db36bc0ea9bfb56fb45c23996dd59898d757c3c55a54fcee234f16a3cf`。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release --nologo --verbosity:minimal`：exit 0，0 warning，0 error。 |
| 三模块定向单测 | Identity initializer/runner：17 passed；SystemData initializer/runner：17 passed；Collaboration initializer：4 passed。合计 38 passed、0 failed、0 skipped。 |
| Backend full test | fresh build 后执行 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：exit 0，1770 passed、0 failed、6 skipped；6 个 skip 均为必须显式启用的 PostgreSQL/Redis/RabbitMQ 环境门禁，其中本节的 3 个 PostgreSQL 用例随后已独立启用并执行。 |
| 真实 PostgreSQL 门禁 | 从项目实际 `appsettings.Development.local.json` 在受控权限内读取连接参数，设置 `PF05_STARTUP_CHAIN_PG=1` 并以 `PF05_GATE_ENABLED` 编译运行 `UnifiedHostLegacyPostgreSqlStartupChainTests`：5m31s，3 passed、0 failed、0 skipped。凭据未输出。 |
| 数据库隔离与清理 | 三个用例只创建唯一 `pf05_*` 临时数据库，均在 `finally` 中执行 `DROP DATABASE ... WITH (FORCE)`；清理异常会使测试失败，本次 3 个用例均正常通过。没有对项目普通调试数据库执行 DDL，也没有停止或重启用户 IDE/服务。 |

真实 PostgreSQL 证据已补齐第一版夹具缺口：fixture 物理执行历史迁移、写入 Identity/SystemData 三列账本和 Collaboration nullable-NULL checksum 账本，保留固定 `applied_on` 与业务行；四个 initializer 的首次 Inspect 均返回 NotReady，且 Inspect 前后完整快照相等；协调器首次 Apply 后 checksum 被回填、原 migration id/description/applied time 和业务数据保持不变，第二次启动 `ApplyCount=0` 且快照不变。

错误非空 checksum、错误 description 与已登记物理结构 drift 均在真实 PostgreSQL 上 fail-closed，并断言后续迁移账本行未写入。Shared 模式使用同一真实临时数据库完成四服务整链；search_path 使用两个真实 schema 验证隔离。PerService 本轮验证的是 resolver 对四个服务返回不同物理库名及缺失映射拒绝，没有另建四个真实物理库重复执行整条旧账本启动链，因此该项只记为拓扑路由合同通过，不扩大为真实 PerService 端到端运行证明。

本节结论：`历史账本 checksum drift 返修 = PASS WITH TOPOLOGY LIMITATION`。用户所报告的 Shared PostgreSQL 旧三列账本在 Inspect 阶段抛 `42703 column checksum does not exist` 的启动链，现已有真实 PostgreSQL 任务专属库的可重复通过证据，§17.6 的 `REPAIR REQUIRED` 对该 Shared 故障关闭；真实 PerService 四库整链仍是后续运行环境验收项。该结论不证明用户普通调试库已被验收任务实际升级，也不替代 Redis、RabbitMQ、真实页面、多进程、容量与故障恢复门禁；PF-05 整包继续保持 `NOT PRODUCTION-ACCEPTED`。

### 17.8 PerService 四库补测复验

复验日期：2026-09-09（Asia/Taipei）

开发在同一集成测试文件新增 `PostgreSql_per_service_four_physical_databases_run_owned_initialization_chains`；本轮冻结文件 SHA-256 为 `db84cb1fa9d3a1a5aa222502b99f77b8ee6c49bcf6dc1647d2545aa01314b0ab`。独立 fresh Release build exit 0，0 warning，0 error。

真实 PostgreSQL 第一次执行该单例门禁时 20s 失败，错误为清理阶段 `NpgsqlException: Exception while connecting`；随后受控只读连接同一实际目标成功，`pf05ps_*` 数据库计数为 0。确认目标恢复可达后仅复跑一次，同一用例 4m04s 通过，1 passed、0 failed、0 skipped；复跑后再次只读查询 `pf05ps_*` 计数为 0。该结果证明当前账号可创建四个不同物理库、四服务账本互不串库、首次空库初始化和重复启动 Ready。

但本新增用例的四个物理库均为空库开始：创建数据库后直接对各服务执行 `Inspect → Plan → Apply → Verify`，没有为 Identity/SystemData/Collaboration/ReferenceData 分别预置旧三列或 nullable-NULL checksum 账本，也没有写入固定 `applied_on` 与旧业务行并在升级后校验保留。因此它没有关闭 §17.7 明确保留的“真实 PerService 四库旧账本升级链”限制；Shared 旧账本证据不能自动外推为四个物理库各自的旧库升级证据。

夹具另有稳定性缺口：`finally` 按顺序调用会抛异常的 `DropDatabaseAsync`，首个清理连接失败会中止其余清理并覆盖测试主体的原始异常。本次没有实际残留，但首跑输出无法确认主体在失败前执行到哪一步，门禁也未保留 primary failure。应在修复时继续尝试清理全部已创建数据库，并在报告清理错误的同时保留主体异常。

本节结论：`PerService 四库旧账本补测 = REPAIR REQUIRED`。单次复跑通过只接受为空库初始化/物理隔离的新增证据，不恢复为完整 PerService 旧账本 PASS；§17.7 的 Shared 故障结论不变，拓扑限制继续保留。鉴于本轮只增加集成夹具且已发现验收缺口，没有机械重复上一轮 1770 项完整 backend 门禁。

## 18. A18/A23 菜单可发现性重新打开

复验日期：2026-09-09（Asia/Taipei）

主控在用户当前运行的 `http://localhost:5173`、admin/development 会话实测：菜单管理返回 3 个一级、33 个节点，实际导航只有“工作台 / 基础配置 / 系统管理”，没有聊天入口；直接访问 `/pc/collaboration` 可以打开页面。独立静态追踪确认前端已有 Collaboration 路由、页面、权限常量、双语标题及静态 navigation 组，但运行时菜单节点会覆盖静态导航；当前证据没有证明现有数据库通过版本化、幂等资源/菜单升级获得聊天节点。因此“直接 URL 可达”不能替代 A18/A23 的登录用户可发现性。

用户已明确目标位置：聊天位于一级“工作台”下，作为与“首页”“终端预览”同级的二级菜单，顺序在“终端预览”之后；不得成为“终端预览”的子菜单，不得产生重复入口。该指令只确定聊天入口位置，不机械迁移其他合规页面，也不得移除无关菜单。

当前结论：`A18/A23 菜单可发现性 = FAIL / REPAIR REQUIRED`。开发必须通过项目现有版本化、幂等资源/导航升级机制更新已存在数据库，同时保护手工菜单、草稿/发布流程和权限关联；不得在前端硬塞菜单或清空重建。稳定交付后，独立验收需复用当前 UnifiedHost/前端与云 Docker 环境，以当前登录用户确认“工作台 → 首页 / 终端预览 / 聊天”的顺序、无重复入口、点击进入真实 `/pc/collaboration` 页面，并检查控制台和失败网络请求。不得向真实人员发送测试消息；消息联调用隔离测试人员与可清理记录。

## 19. implementation 08 全量稳定交付验收矩阵

冻结日期：2026-09-09（Asia/Taipei）

本矩阵以 `docs/implementation/08-Industrial Platform Collaboration开发实施方案.md`、两份 PF05 细化规格、派遣前详细设计与页面验收标准、完成度审计和查询回归报告为输入。它只是下一轮独立验收的检查清单，**不是实现存在或验收通过的证据**；历史局部 PASS、测试总数、路由/页面文件存在均不能替代下列实际证据。

| 条款/任务 | 代码/页面核对 | 必须实际证据 | 稳定交付前状态 |
| --- | --- | --- | --- |
| G05-1 / 001 宿主与初始化 | 单一 Collaboration.Service、三个逻辑模块、迁移/种子账本、Shared/PerService、Platform/Embedded、真实 readiness/degradation；禁止 `EnsureCreated` 与静默降级 | 新鲜 Release build；Shared 与四物理库 PerService 的旧账本、固定 `applied_on`、业务数据保留、Inspect 只读、重复启动、drift fail-closed；Embedded 参考宿主 | 待稳定 hash 复核；§17.8 的 PerService 旧账本与 cleanup/primary error 仍为 `REPAIR REQUIRED` |
| G05-2 / 002 消息领域 | 会话/成员/消息/附件、严格序列、`clientMessageNId` 幂等、outbox/audit 原子性、已读未读、撤回、隐藏恢复、三种查询模式与 410、fixed-target catch-up | 双用户多设备真实发送/重试/离线追赶/并发顺序/删除恢复；断言失败与重试不重复写消息、outbox 或审计 | 待稳定 hash 复核 |
| G05-3 / 003 在线状态 | 登录后应用级连接、20/60/15 时序、多连接、隐私投影、Redis 全局 presence、SignalR backplane、多实例、Unknown/降级与非聊天页提醒 | 两浏览器或多设备、多实例 Redis/SignalR；断连/恢复/隐私/提醒；页面关闭后连接仍归应用所有 | 待稳定 hash 复核；完成度审计所列页面级连接、无 backplane/提醒缺口必须关闭 |
| 004 附件闭环 | File 正式契约、50 MiB/4 MiB 分片桥接、仅 Clean 可发送、引用/holds、每次下载重新鉴权、状态事件 inbox、singleflight 对账；不读取 File 内部表 | 实际 File 上传/扫描状态/发送/下载授权/失效/对账/重复事件；跨服务只走契约 | 待稳定 hash 复核；状态事件消费/inbox/对账缺口必须关闭 |
| 005 API 与可靠性 | 规格 REST/Hub 路径、DTO/错误码/权限、安全投影、outbox dispatcher lease/confirm/retry/DLQ/current projection、真实 consumer inbox、RabbitMQ recovery、审计摄取 | API 合同与授权矩阵；RabbitMQ 中断恢复、500 条 outbox、重复投递幂等、DLQ；Audit 实际可追溯 | 待稳定 hash 复核；无 dispatcher 等审计缺口必须关闭 |
| 查询回归保护 | Collaboration 别名仅 `/collaboration/api/v1/*`；OData metadata 排除 Collaboration ApplicationPart；Hub query token 仅精确 Hub GET；外部 Hub 为 `/collaboration/hubs/collaboration-v1` | user/role/permission/menu/auth/init 回归；metadata 与非 Hub 请求不受污染；Gateway/UnifiedHost 路径实测 | 待稳定 hash 复核，不得回退既有修复 |
| 006 / W05-01 PC 聊天 | `/pc/collaboration` 双栏工作区、单一业务会话列表、时间线不使用通用表；搜索、附件、presence、pending/unknown、旧状态纠正 | 当前 UnifiedHost/Vite 真实页面；隔离用户搜人→建会话→发送/重试/追赶；控制台与失败请求为零；主题/中英文/密度/键盘/IME | 待稳定 hash 复核 |
| 006 / W05-02 顶栏快捷抽屉 | 与完整页共享 Store/应用级连接；文本/图片/文件；无新建会话与合规入口；焦点与 Esc | 完整页和抽屉切换不重连、不丢 pending、同一消息状态一致；键盘与焦点实测 | 待稳定 hash 复核；完成度审计缺口必须关闭 |
| 006 / W05-03 PDA/移动端 | 列表到全屏会话、48/44 px、键盘/安全区/旋转/返回/深链；同一 Store 与重试 client id | PDA/移动视口真机或等价浏览器实测并恢复原视口；旋转、软键盘、返回、深链、扫描枪/IME | 待稳定 hash 复核 |
| 007 / W05-04 合规 | 独立路由/页签；原文查看、处置、导出申请/审批/下载、legal hold 创建/复核/释放申请/审批、保留策略与 checkpoint、step-up 绑定/证明、双人审批、audit fail-closed；普通页使用 AppDataTable，无选择；高风险表单使用 AppFormDrawer | 隔离数据完成四眼流程、拒绝同人审批、step-up 过期/重放、导出下载授权、hold 阻止删除、保留恢复；双语/主题/权限/审计 | 待稳定 hash 复核；菜单/原文/下载/释放审批/共享管理组件等缺口必须关闭 |
| 菜单与可发现性 | route→API bootstrap→permission→版本化 manifest/resource/navigation→旧库增量→动态菜单→locale→实际点击；保护手工菜单、草稿/发布与权限 | 当前 admin/development 实测“工作台 → 首页 / 终端预览 / 聊天”，聊天位于终端预览之后、同级且无重复；点击进入真实页面 | §18 `FAIL / REPAIR REQUIRED`，待稳定交付关闭 |
| 008 实际环境与容量 | Gateway/Identity/SystemData/PostgreSQL/Redis/RabbitMQ/File/Audit/Embedded 组合，健康/指标/告警，恢复与资源约束 | 当前云 Docker + UnifiedHost/Vite；双用户多设备；2C4G 下 20 连接、2 msg/s×10 min、5 msg/s×1 min、2000 历史、500 outbox 恢复及规格 p95；故障注入/恢复 | 待稳定 hash 复核；固定 Healthy、无专属指标/告警等缺口必须关闭 |
| 最终工程门禁 | 全部改动、测试与证据范围；不包含本地私密配置、构建产物或运行日志 | fresh backend Release build 后 full test；frontend typecheck/lint/prettier/unit/build；定向真实依赖门禁；`git diff --check`；0 staged；临时库/tenant/消息/文件清理 | 待稳定 hash 复核 |

只有上述矩阵逐项拥有可复现证据且未发现 P0/P1/P2 缺陷时，PF-05 才能从 `NOT PRODUCTION-ACCEPTED` 转为通过。若当前运行实例未加载稳定交付的新代码，真实浏览器验收必须先确认实例版本；不得把旧实例结果归因于新代码，也不得为验收擅自停止或重启用户 IDE/服务。

## 20. R4 稳定交付第一次独立复验

复验日期：2026-09-09（Asia/Taipei）

开发回写未提交工作树 diff hash `e43387f4e2ff5456addbc96df0b1ecc56131a56b`；验收在 HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c` 上重新枚举 189 个已修改/新增文件，按 `path=SHA256` 排序、UTF-8/LF 连接计算内容聚合为 `2d81470b01e5b10b0e1ca9629ffdc805441b88fb6d6d4cefecc82e9257e9b47b`。该聚合包含验收证据文件，故只作为本轮输入冻结标识，不与开发 diff hash 等同；暂存文件为 0。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release --nologo --verbosity:minimal`：exit 0，0 warning，0 error。 |
| Backend full test | fresh build 后执行 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：exit 0，1780 passed、0 failed、7 skipped。7 个 skip 均为显式真实 PostgreSQL/Redis/RabbitMQ 门禁，本节未把它们计为通过。 |
| Collaboration tests | 68 passed、0 failed、0 skipped，包含在上述全量结果中。 |
| Frontend typecheck | `pnpm.cmd typecheck`：exit 1；除 `node_modules/.tmp/tsconfig.app.tsbuildinfo` 的 EPERM 外，`CollaborationChat.vue(445,6)` 明确报 TS1005 `',' expected`。 |
| Frontend lint | `pnpm.cmd lint`：exit 1；同一文件第 445 行 Parsing error `',' expected`。 |
| Frontend format check | `pnpm.cmd format:check`：exit 1，133 个文件不符合；其中包含本轮触及的 `createIndustrialApp.ts`、`PcLayout.vue` 等。该全仓基线不能证明所有警告均由 PF05 引入，但 PF05 改动文件仍须定向检查。 |

静态定位显示 `CollaborationChat.vue:431-445` 的 `realtime.subscribe?.({ ... onReconnected: async () => { ... }` 在回调块后直接以 `})` 结束，缺少订阅对象的闭合 `}`。因此本轮稳定交付不是可编译前端，开发方此前的 `vue-tsc --noEmit --incremental false` 结果已经过时，不能用于当前工作树。

本节结论：`R4 STABLE HANDOFF = REPAIR REQUIRED`。缺陷已直接回写功能开发任务；在新稳定 hash 到达前，不对变化中的前端执行 Vitest、Vite build 或真实浏览器验收。Backend 门禁通过只作为本轮后端证据，不恢复 PF-05 整包通过，也不关闭 §17.8 PerService 旧账本、§18 动态菜单和 §19 实际依赖/容量矩阵。

## 21. R5 前端返修与全条款静态复核

复验日期：2026-09-09（Asia/Taipei）；开发回写 diff hash `f5f6f811fdd9d0d286ad967ea21a5a7e382571e1`。`CollaborationChat.vue:431-447` 已补齐订阅对象闭合，验收重新执行以下门禁：

| 门禁 | 独立结果 |
| --- | --- |
| Frontend no-emit typecheck | 项目本地 `vue-tsc.cmd --noEmit --incremental false`：exit 0。 |
| Frontend lint | `pnpm.cmd lint`：exit 0。 |
| PF05 changed-file format | 对 `git ls-files -m -o --exclude-standard` 中 21 个前端 `.ts/.vue` 文件执行项目本地 Prettier `--check`：全部通过。 |
| PF05 targeted unit | Vitest `--configLoader runner`：3 files、42 tests passed。 |
| Frontend full unit | Vitest `--configLoader runner`：125 files、928 tests passed。 |
| Production bundle | Vite `--configLoader runner` 输出到任务专属系统临时目录：2346 modules transformed，exit 0；仅有既有的 >500 kB chunk warning。17 个临时输出文件已清理。 |

上述结果关闭 §20 的语法返修，但不能关闭以下静态合同缺口：

- `应用级实时与提醒 = FAIL`：`createIndustrialApp.ts` 只注册 manager；`PcLayout.vue` 只订阅和 REST 刷新；唯一连接启动仍在 `CollaborationChat.vue` 挂载。Hub 仅在 `JoinConversation` 加入当前会话组，没有登录用户级消息组，未进入/未选中会话不能驱动非聊天页提醒。
- `W05-02 顶栏快捷抽屉 = FAIL`：顶栏聊天按钮直接路由跳转，没有共享 Store 的抽屉、抽屉内文本/图片/文件和焦点/Esc 合同。
- `SignalR Redis backplane = FAIL`：Collaboration 只调用 `AddSignalR()`；Redis presence 租约不等价于 Hub backplane。
- `RabbitMQ outbox/inbox = FAIL`：新增 dispatcher 只调用本机 SignalR publisher；没有 RabbitMQ/EventBus publish、publisher confirm、真实 consumer inbox 或重复投递处理。
- `File 状态事件闭环 = FAIL`：Collaboration 源码/测试无 File status event、consumer inbox、attachment reconciliation 或 singleflight；持久化表也没有 inbox。
- `W05-04 管理页合同 = FAIL`：Compliance 页的 legal hold/export 列表仍为原生 `<table>`，结构化/高风险操作没有 `AppFormDrawer`，不符合冻结的管理页标准。
- `指标与告警 = FAIL`：Outbox health 已从固定 Healthy 改为真实查询，但 Collaboration 无 Meter/Counter/Histogram/ActivitySource 或告警定义。

静态同时确认 PerService 四库夹具已加入各服务 legacy fixture、Inspect 只读快照、固定时间/业务数据保留断言，以及 best-effort 全库清理与 primary failure 保留；该项仍须实际 PostgreSQL gate 通过后才能关闭 §17.8。SystemData 的聊天位置与四项合规资源种子已有定向测试，仍须实际登录用户从动态菜单点击后才能关闭 §18。

本节结论：`R5 AUTOMATION = PASS；IMPLEMENTATION 08 FULL SCOPE = REPAIR REQUIRED`。上述 7 项已直接回写功能开发任务；在新稳定交付前不运行会被这些缺口直接否决的双用户、多实例、RabbitMQ/File 恢复与真实页面通过判定。

## 22. R6 可靠性返修独立复核

复验日期：2026-09-09（Asia/Taipei）；开发回写 198 个文件的稳定标识 `f5906a6a8153864fa7e27b39211c124a140ec352a1350a9e59161d06b3778108`。验收枚举文件数一致；因包含持续更新的本验收文档，验收方 `path=SHA256` 聚合为 `a75c8e5a0af652482ea30f39f000d0fa9143b347c40565292a5d0351b6c0ae1e`，不与开发口径混用；暂存文件为 0。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | exit 0，0 warning、0 error。 |
| Backend full test | 1780 passed、0 failed、7 skipped；7 项仍为显式真实 PostgreSQL/Redis/RabbitMQ 门禁。 |
| Frontend type/lint/format | no-emit typecheck exit 0；ESLint exit 0；22 个 changed `.ts/.vue` 文件 Prettier 全部通过。 |
| Frontend full unit | 125 files、928 tests passed。 |
| Frontend production bundle | Vite runner 隔离构建 2348 modules，exit 0；仅 >500 kB chunk warning，17 个临时输出已清理。 |

R5 所列代码外形已经出现：应用级 runtime plugin、用户消息组、QuickDrawer、Redis backplane、IntegrationEvent publisher/consumer、event inbox 表、File status consumer、合规 AppDataTable/AppFormDrawer、Meter/ActivitySource 和 Outbox health threshold。但故障语义复核发现：

- `File status durable publish = FAIL`：SystemData File lifecycle 直接发布 EventBus；失败只记日志并吞掉，没有持久 Outbox。Rabbit 在唯一状态变化时不可用会永久丢事件。
- `consumer inbox recovery = FAIL`：TryClaim 仅 INSERT，并把 PostgreSQL/SQLite 的任意异常当作重复；claim 后业务失败会留下永久 `Processing`，后续投递永远跳过，RetryCount/LastError 没有恢复路径。
- `Rabbit failure routing = FAIL`：消费异常执行 `NACK requeue=false`，队列声明未配置 DLX/DLQ，消息实际被丢弃，与日志所称“重试/DLQ”不符。
- `File authoritative reconciliation = FAIL`：冻结契约要求状态事件只触发 tenant/file singleflight 的 File 权威重查；当前消费者直接把 event status/version 写入附件投影，无法抵御伪造、过时或缺失事件。
- `W05-04 shared step-up drawer = FAIL`：所有高风险动作仍通过 `ElMessageBox.prompt` 收密码；处置、review/release/approve/download 没有契约要求的当前用户/动作/范围/密码共享 AppFormDrawer、busy 与失败恢复语义。
- `initial realtime recovery = FAIL`：初次 `connection.start()` 失败被吞掉，应用 plugin 只在认证状态变化时重试；`withAutomaticReconnect` 不覆盖初次连接失败，依赖恢复后非聊天页仍可能永久离线。
- `quick drawer locale = FAIL`：标题硬编码中文“协作消息”。
- `metrics export = NOT PROVEN`：定义了 Meter/ActivitySource，但未找到把 `IndustrialPlatform.Collaboration` 接入 OTel `AddMeter/AddSource` 或其他 scrape/export pipeline 的代码；health 文本不等于实际告警链。
- `targeted failure tests = MISSING`：测试未引用新 File consumer、event inbox、realtime consumer、QuickDrawer、runtime plugin、backplane/diagnostics；1780/928 的全绿结果不能证明上述故障路径。

本节结论：`R6 AUTOMATION = PASS；RELIABILITY / W05-04 = REPAIR REQUIRED`。缺陷已直接回写开发任务；§17.8、§18 和 §19 的真实依赖/页面/容量门禁继续保持未关闭。

## 23. R7 可靠性返修再次独立复核

复验日期：2026-09-10（Asia/Taipei）。开发回写 189 个源代码/测试/项目/配置文件的稳定 aggregate `b5dd0f96a972f79f0bc347297892a9011fef48e7a2b919c7cfeee52a8320509a`；HEAD 仍为 `f86415dab3161eca46e1d20a230b681dfce6fe9c`，暂存文件为 0。验收确认 R6 所列主要缺口已有对应实现：SystemData File 状态同事务 outbox、Collaboration File 权威回查、Rabbit retry/DLQ、共享 step-up drawer、应用级 realtime 初始重试、QuickDrawer 双语、SignalR Redis backplane、Meter/Activity scrape 端点及定向测试均已出现。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning、0 error。 |
| Backend full test | exit 1：1787 passed、1 failed、7 skipped。失败为 `InitializationHttpTests.Internal_protocol_resolves_local_target_and_returns_raw_plan_and_state`；全量首跑 readiness 返回 503，定向复跑业务断言通过但 finally 删除临时 SQLite 报文件仍被使用。测试在 host/factory 尚未 DisposeAsync 前进入外层 finally 删除数据库，生命周期不可靠，已回写修复。 |
| Frontend no-emit typecheck | 项目本地 `vue-tsc.CMD --noEmit --pretty false`：exit 0。 |
| Frontend lint | `pnpm.cmd lint`：exit 0。 |
| PF05 changed-file format | 稳定交接后的 25 个前端 changed files 中，开发开始处理本轮缺陷时新增的 `tests/unit/collaborationRuntime.spec.ts` 未通过 Prettier；因此该结果只记录为在途输入，不作为稳定版本门禁结论。 |

静态故障语义仍有以下缺口：

- `application realtime transition recovery = FAIL`：`CollaborationRealtime.start()` 初次失败后吞掉异常并启动后台 retry，调用方随即执行 `setPresence('Online')`；未连接时该调用 reject，使 runtime 的 `transition` 永久 rejected，后续令牌/用户变化追加的 `.then(...)` 不再执行，身份切换和 stop/start 无法恢复。
- `inbox lease fencing = FAIL`：claim 写入 `LeaseNId` 但接口只返回 bool；Processed/Failed 更新不带 lease token，consumer 也忽略 mark 返回值。旧 worker 超过 30 秒被新 worker reclaim 后仍能覆盖新 owner 状态并 ACK，不能证明并发恢复安全。
- `Rabbit publisher concurrency = FAIL`：singleton `RabbitMqEventBus` 复用单一 `IChannel`，互斥只保护建 channel，不保护 publish。UnifiedHost 多 outbox/调用者并发发布会并发共享 channel，publisher confirm 不能据此判为可靠。
- `Rabbit retry attempt semantics = RECHECK`：当前把全部 `x-death` count 相加；主队列 reject 和 retry queue TTL 会分别形成死亡记录，可能每轮计数两次，使配置的最大投递次数早于语义值进入 DLQ。需要按主消费队列/reason 计数并用真实 header/真实 Rabbit 证明。

本节结论：`R7 FRESH BUILD / TYPE / LINT = PASS；FULL TEST / RELIABILITY = REPAIR REQUIRED`。缺陷与失败证据已直接回写开发任务；开发正在处理，待新的稳定交接后再从 fresh build 开始复验，不对变化中的工作树执行全量 Vitest、生产 bundle 或真实 PG/Rabbit/双实例浏览器验收。PF-05 整包继续保持 `NOT PRODUCTION-ACCEPTED`。

## 24. R8 对 R7 稳定交接的再次独立复验

复验日期：2026-09-10（Asia/Taipei）。开发回写 191 个 source/test/project/config 文件的 aggregate `a7e5d6be5029f499ce60609a224bfbf6b9c2ca554dac0baa798e404481284b41`；HEAD 仍为 `f86415dab3161eca46e1d20a230b681dfce6fe9c`，暂存文件为 0。本轮只核对 R7 修复、受影响回归路径和发布门禁，不机械重做 §19 尚未具备环境条件的全场景验收。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning、0 error。 |
| Backend full test | fresh build 后 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：exit 0，1789 passed、0 failed、7 skipped；7 项仍为显式真实 PostgreSQL/Redis/RabbitMQ 门禁，不计为通过。 |
| Frontend no-emit typecheck | 项目本地 `vue-tsc.CMD --noEmit --pretty false`：exit 0。 |
| Frontend lint / changed-file format | `pnpm.cmd lint`：exit 0；25 个 changed 前端文件 Prettier `--check` 全部通过。 |
| Frontend full unit | Vitest `run --configLoader runner`：127 files、932 tests passed。 |
| Frontend production bundle | 隔离输出目录执行 Vite：2351 modules transformed，exit 0；仅既有 >500 kB chunk warning。17 个构建文件随后从验收专属目录清理，未覆盖默认 `dist`。 |

R7 所回应的四项缺口已有代码与专项测试：runtime transition 会吸收前序 rejection 且仅 Connected 后设置 Online；inbox 完成/失败更新带 lease fencing token；singleton Rabbit publisher 对同一 channel 的 publish 串行化；Rabbit retry 只统计主队列 `reason=rejected` 的 `x-death`；ReferenceData HTTP 测试会先释放 DB context/factory 再清理临时 SQLite。上述自动化结果关闭 §23 的 build/test/format 失败。

但继续沿故障链审查发现：

- `inbox busy/crash redelivery = FAIL`：`TryClaimEventInboxAsync` 仍用同一个 `null` 同时表示 AlreadyProcessed 和 active lease Busy，两个 consumer 在 null 时都正常 return，EventBus 随即 ACK。若 owner claim 后进程或 channel 崩溃，Rabbit 在 30 秒 lease 到期前立即重投，新 worker 会把仍 Busy 的消息误 ACK；随后没有消息再触发过期 reclaim，事件永久停在 Processing。`CollaborationRealtimeIntegrationEventConsumer` 的本地发布异常还没有带当前 lease 执行 MarkFailed。现有仓储测试只断言 Busy 返回 null，没有覆盖 consumer ACK 语义或 crash→redelivery→reclaim 整链。
- `authenticated user switch = FAIL`：runtime 虽 watch `userId/token` 并再次调用 start，但实际 realtime 在 connection 已 Connected 时直接 return，不会用新 token 重建 WebSocket；U1→U2 的账号切换仍保持旧 Hub 身份，之后 SetPresence 也是旧用户。现有 runtime 单测用 mock 的第二次 start 人为切到 Connected，没有覆盖真实 manager 的 Connected→换账号路径。

本节结论：`R7 AUTOMATION = PASS；INBOX CRASH RECOVERY / USER SWITCH = REPAIR REQUIRED`。两项已直接回写原开发任务；在新稳定交接前不把自动化全绿扩大为真实 RabbitMQ、双用户、多实例或 PF-05 生产通过。§17.8、§18、§19 的真实依赖、菜单点击、浏览器、Embedded、恢复和 2C4G 容量证据仍未关闭，整包继续保持 `NOT PRODUCTION-ACCEPTED`。

## 25. R9 对 R8 稳定交接的再次独立复验

复验日期：2026-09-10（Asia/Taipei）。开发回写 191 个文件 aggregate `7b60848729d60818dfb55366614bdf1642a1e0f6c8d7660f5ca358a6abed319d`；HEAD 仍为 `f86415dab3161eca46e1d20a230b681dfce6fe9c`，未 stage/commit/push。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | exit 0，0 warning、0 error。 |
| Backend full test | 1790 passed、0 failed、7 skipped；skip 仍为真实 PostgreSQL/Redis/RabbitMQ 门禁。 |
| Frontend type/lint/format | no-emit typecheck exit 0；ESLint exit 0；25 个 changed files Prettier 全部通过。 |
| Frontend full unit | 127 files、935 tests passed。 |
| Frontend production bundle | 隔离 Vite build 2351 modules，exit 0；仅既有大 chunk warning；17 个临时构建文件已清理。 |

上轮两项代码缺口已有明确修复：inbox claim 现在区分 Claimed、AlreadyProcessed、Busy；只有 AlreadyProcessed 正常返回，Busy 抛稳定错误触发 broker 重投；实时 consumer 失败也用当前 lease MarkFailed。runtime 会按 userId+accessToken 检测身份切换，先 stop 旧连接再 start 新连接，重连回调还会核对 requested identity。

但 crash recovery 的默认跨组件时间窗仍为 `FAIL`：inbox lease 固定 30 秒；Rabbit retry delay 默认 5 秒且最大失败投递次数为 5。owner crash 后立即重投时，消息大约在 t0/t5/t10/t15/t20 持续遇到 Busy 并消耗普通失败预算，t25 已达到进入 DLQ 的条件，而 lease 到 t30 才允许 reclaim。因此“Busy 延迟重投直到 lease 过期再接管”在默认配置下不会发生，消息仍可能先进入 DLQ 而未最终处理。现有测试分别验证 Busy 抛错、lease reclaim 与 retry 计数，没有用同一确定性时钟验证这三个组件的组合时序。

本节结论：`R8 AUTOMATION = PASS；INBOX CRASH-TO-RECLAIM TIMING = REPAIR REQUIRED`。该跨组件缺口已直接回写原开发任务；修复应让 lease contention 不消耗 poison-message 预算，或强制 retry horizon 带安全裕量地大于 lease，并增加 A claim→crash→Busy redelivery→lease expiry→B reclaim→Processed、最终不进 DLQ 的整链测试。未进入真实 RabbitMQ、双实例或浏览器最终验收；PF-05 继续保持 `NOT PRODUCTION-ACCEPTED`。

## 26. R10 对 R9 稳定交接的再次独立复验

复验日期：2026-09-10（Asia/Taipei）。验收输入为开发回写的 193 个 source/test/project/config 文件 aggregate `039377d6b21e3436bcb7eb9f4f7612c90671ee316b95b7f6d924cf2afe5fabe0`；HEAD `f86415dab3161eca46e1d20a230b681dfce6fe9c`、分支 `develop`，暂存文件为 0。本轮只写本验收证据，不修改生产代码、测试、配置或开发 evidence。

### 26.1 R9 修复与独立自动化

- 静态复核确认 EventBus 为 Busy lease contention 声明独立 exchange/queue，Busy queue 通过 TTL/DLX 回主交换器；consumer 对 `IEventBusDeferredRetryFailure` 单独发布 Busy retry，并只在 publisher confirm 成功后 ACK 原 delivery，失败则 NACK/requeue。普通异常继续使用有界 retry/DLQ，Busy 不计主队列 rejected 预算。
- fresh Release build：`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` exit 0，0 warning、0 error。
- fresh build 后 full test：`dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` exit 0，1792 passed、0 failed、7 skipped；7 个环境门禁没有计作通过。
- 前端 no-emit typecheck、ESLint、25 个 changed files Prettier 均 exit 0；Vitest `run --configLoader runner` 为 127 files / 935 tests passed；隔离 Vite production build 2351 modules、exit 0，仅既有 >500 KiB warning，17 个临时构建文件已清理。

### 26.2 真实依赖补验

| 门禁 | 独立结果 |
| --- | --- |
| Redis / Rabbit 基础可达 | 使用项目私有 Development 配置但不输出凭据；Redis 唯一临时键写读删与 Rabbit exclusive auto-delete queue 共 2/2 通过。 |
| PostgreSQL 启动链 | 显式启用 `PF05_GATE_ENABLED=1`、`PF05_STARTUP_CHAIN_PG=1`，实际运行 `UnifiedHostLegacyPostgreSqlStartupChainTests`：4 passed、0 failed、0 skipped，耗时 11m18s；覆盖 Shared 与 PerService 四物理库旧账本、drift/fail-close。随后只读查询 `datname LIKE 'pf05%'` 为 0，临时库无残留。 |
| Rabbit Busy TTL/DLX | 任务专属唯一 exchange/queue 上实际执行 publisher confirm、Busy queue 500ms TTL、DLX 回主交换器并校验 body，结果 `PF05_RABBIT_BUSY_RETRY=PASS`。 |
| 构建恢复 | PostgreSQL gate 后重新执行普通 Release build，exit 0，0 warning、0 error，避免以 gate 编译产物替代普通构建。 |

### 26.3 实际页面与菜单

本机原有 5173/5041 等端口均未监听，Docker CLI 不可用；因此按本地验收约束启动任务专属隔离环境：SQLite 独立文件、租户 `pf05r9-20260910`、两个一次性验收用户、远程 Redis、Rabbit `pf05r9-20260910.*` 唯一拓扑，UnifiedHost 监听 5141，Vite 监听 5173。首次从 `127.0.0.1:5173` 打开因项目 CORS 只允许 `localhost:5173` 被正确拒绝；改用项目规定 origin 后执行真实 Chromium，不把该配置差异记为产品失败。

单用户真实页面结果：登录进入 PC 壳；工作台二级菜单严格为 `首页 → 终端预览 → 聊天`，聊天入口全局计数 1，点击进入 `/pc/collaboration`；聊天空状态、人员搜索入口、会话栏均可见。1440/1024/900 三档稳定复测无横向溢出，暗色主题与英文 locale 生效，console error、pageerror、非预期 failed request 均为 0。视觉证据保留在 `C:/Users/DONG/.codex/visualizations/2026/09/08/01a07f9d-2f78-7c21-9f2e-09144882ac11/pf05-browser-acceptance/pf05-chat-dark-en.png` 和 `pf05-chat-900.png`。因此 §18 的菜单位置、顺序、唯一性和实际点击缺口本轮关闭。

### 26.4 双用户实际链路缺陷

两个独立 Chromium context 均以真实账号登录并保持 Collaboration Hub 连接；管理员通过人员搜索创建与第二用户的会话成功，presence 正常。随后发现两个稳定缺陷：

1. `A09/A19/W05-01 SignalR 发送 = FAIL`：在页面输入文本并点击“发送”，发送方稳定显示 `Failed to invoke 'SendMessage' due to an error on the server.`，自身时间线不出现该消息。浏览器 console/pageerror/requestfailed 均为 0。对同一账号、同一 conversation 和同结构请求调用 REST `POST /collaboration/api/v1/conversations/{conversationNId}/messages` 则为 200 / `success=true`；Rabbit 连接稍后暂断时再次调用 REST 仍为 200，故不能把 Hub 失败归因于 broker。REST 诊断消息经 outbox/Rabbit/用户组到达第二浏览器，第二用户菜单提醒与会话未读从 0 更新为 1，证明真实事件分发链本身有正向证据，但不能替代页面 Hub 发送失败。
2. `A09/A19 read-cursor = FAIL`：第二浏览器加载已有消息后，前端触发的 `PUT /collaboration/api/v1/conversations/{conversationNId}/read-cursor` 返回 500；后端明确记录 `BadHttpRequestException: Unexpected end of request content`，异常发生在 `ReadCursorRequest` JSON body model binding。页面调用路径为 `loadMessages → api.markRead(conversationNId, Number(last.sequence))`，需修正前端 PUT 请求体/HttpClient 参数合同并补真实 HTTP 测试。

失败截图保留为 `pf05-two-user-admin-failure.png` 与 `pf05-two-user-peer-failure.png`。两项已直接回写原开发任务 `01a07f9e-355b-75f1-ba20-5d41f49f07ca`，要求补真实 SignalR client 集成测试和 read-cursor HTTP 合同测试，并在新稳定 manifest 后复验。

### 26.5 清理与结论

只停止本轮启动的 UnifiedHost/Vite；RabbitMQ 中精确限定的 4 个 `pf05r9-20260910.*` queue 与 4 个 exchange 已删除并返回 `PF05_RABBIT_TOPOLOGY_CLEANUP=PASS`。本地一次性凭据、SQLite 数据库、临时 Playwright 脚本和 Rabbit harness/bin/obj 已删除；这些均为隔离测试数据且不可恢复，只保留不含真实业务数据的截图。未停止或重启用户 IDE/服务，未 stage、commit 或 push。

本节结论：`R9 BUSY RETRY / REAL PG / REAL RABBIT / MENU & EMPTY PAGE = PASS；TWO-USER PAGE SEND / READ CURSOR = REPAIR REQUIRED`。PF-05 整包继续为 `NOT PRODUCTION-ACCEPTED`；开发回写新稳定交付后只复验上述两项、修复 diff 及其影响路径，不机械重复已经通过的 1792/935、PostgreSQL 与 Rabbit Busy 门禁。Embedded、双实例崩溃恢复、File/Audit 正向操作和 2C4G 容量仍是后续独立环境门禁。

## 27. R11 对 R10 稳定交接的定向独立复验

复验日期：2026-09-10（Asia/Taipei）。开发回写当前 dirty source/test/project/config 共 195 个文件，按既有算法排除文档、`CLAUDE.md`、`src/开发注意事项-数据库拓扑与本地配置.md`、`bin/`、`obj/`、`TestResults/`、缓存、日志与构建输出；验收任务独立重算得到 aggregate `85c9ff4b80625bb7b1ebb684d997ef55dc729cb703b5d3276cdfa01b81c3f2d6`，与开发回写完全一致。HEAD 仍为 `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`，暂存文件为 0。

### 27.1 修复 diff 与定向自动化

- `CollaborationHub.JoinConversation/SendMessage/SetTyping` 不再把 `CancellationToken` 暴露为 Hub 客户端业务参数，统一使用 `Context.ConnectionAborted`；新增 TestServer + SignalR Client 长轮询集成测试，真实执行认证、JoinConversation、SendMessage 返回值与 `message.ack`。
- 前端 `markRead` 明确发送 `{ sequence: String(sequence) }` 和 `Content-Type: application/json`；新增 MSW 请求体/请求头合同。后端同一新增集成测试类以真实 HTTP controller 验证 JSON 绑定及游标推进。
- fresh Release build 首次在受限沙箱中因前一任务生成的多个测试 `bin/obj` 临时清单无写权限失败；关闭 MSBuild/VB/C# build server 后仍同样失败。转到宿主权限、覆盖同一既有产物后重新运行 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`，exit 0，0 warning、0 error；没有把前两次文件权限失败误记为源码失败。
- `Api_CollaborationHubSignalRIntegrationTests`：2 passed、0 failed、0 skipped；前端 Vitest 实际执行全量 128 files / 936 tests，全部通过。`vue-tsc --noEmit`、修复文件 ESLint、修复文件 Prettier 均 exit 0；隔离 Vite production build 2351 modules、exit 0，仅有既有大 chunk 提示。

### 27.2 真实双用户页面复验

使用任务专属 SQLite、租户 `pf05r10-20260910`、两个一次性验收账号、远程 Redis、唯一 `pf05r10-20260910.*` Rabbit 拓扑，UnifiedHost 监听 5141、Vite 监听 5173；两个独立 Chromium context 从实际登录页进入 `/pc/collaboration`，管理员通过人员搜索创建会话并在页面输入文本发送。

| 路径 | 独立结果 |
| --- | --- |
| SignalR 页面发送 | PASS；发送方时间线即时出现唯一测试消息，未再出现 Hub invocation error。 |
| 双用户实时交付 | PASS；第二浏览器会话列表收到提醒，打开会话后实际看到同一消息。 |
| read-cursor HTTP 与服务端投影 | PASS；浏览器捕获 `PUT /read-cursor` 200；随后只读 REST 会话摘要确认服务端 unread total 已为 0。 |
| 浏览器诊断 | PASS；console error、pageerror、非 `ERR_ABORTED` failed request 均为 0。 |
| 页面未读即时收敛 | FAIL；接收方已打开消息、`PUT /read-cursor` 200 且服务端 unread=0 后等待 1.5 秒，页面顶部仍显示“未读 1”，左侧会话 badge 仍为 1，只有重新加载列表才会取回权威 0。 |

可视证据保留为 `C:/Users/DONG/.codex/visualizations/2026/09/08/01a07f9d-2f78-7c21-9f2e-09144882ac11/pf05-r10-browser-acceptance/pf05-r10-two-user-pass.png`。残留缺陷与当前源码一致：`loadMessages` 对 `api.markRead(...)` fire-and-forget 后不消费返回的 unread/projection，也不刷新当前会话摘要；页面实时订阅同时没有处理已经由 realtime manager 暴露的 `onReadCursor`。该问题已直接回写原开发任务，要求补成功游标后的本地/实时收敛及并发旧事件组件测试。

### 27.3 清理与结论

只停止本轮启动的 UnifiedHost/Vite；精确删除 4 个 `pf05r10-20260910.*` queue 和 4 个 exchange，返回 `PF05_R10_RABBIT_TOPOLOGY_CLEANUP=PASS`。一次性凭据、SQLite、临时 Playwright 脚本、Rabbit cleanup harness/bin/obj 与隔离构建目录已删除且不可恢复，只保留截图；未停止或重启用户 IDE/服务，未 stage、commit 或 push。

本节结论：`R10 SIGNALR SEND / TWO-USER DELIVERY / READ-CURSOR SERVER CONTRACT = PASS；READ-CURSOR PAGE UNREAD CONVERGENCE = REPAIR REQUIRED`。下一稳定回写只复验该页面收敛缺陷及其影响路径，不重复 PostgreSQL、Rabbit Busy、全量后端或全量前端门禁。PF-05 整包仍为 `NOT PRODUCTION-ACCEPTED`；Embedded、双实例崩溃恢复、File/Audit 正向操作和 2C4G 容量仍是后续独立环境门禁。

## 28. R12 对 R11 未读页面收敛修复的定向独立复验

复验日期：2026-09-10（Asia/Taipei）。开发回写修复范围为 `CollaborationChat.vue`、`collaborationHub.ts`、`collaboration.ts` 与新增 `CollaborationChat.spec.ts`；验收静态确认成功 read-cursor 响应会合并 `unreadCount/projectionVersion`，列表刷新不会用旧摘要覆盖已确认游标，并且页面只接受当前用户、更高版本/序号的 `onReadCursor` 事件。

### 28.1 稳定快照与定向自动化

- 开发回写 196 文件 aggregate `f9ab2ff5a339a146ebb44a29389c171cb3cd3f4e80f56ecce23052952f9908b4`；验收按上一轮已得到相同结果的 manifest 算法连续两次重算当前 196 文件工作树，均得到 `b335a6a62a917a6f4161739d984bfa976412370813e96f1ec83d4512c81a8d9e`。文件数相同但 aggregate 不一致，已直接回写开发任务。为保证本轮证据可追溯，以下结论明确绑定验收实际读取并复核稳定的 `b335...` 快照，不把交接 hash 差异隐去。
- 四个直接相关前端测试：`CollaborationChat.spec.ts`、`collaborationApi.spec.ts`、`collaborationHub.spec.ts`、`collaborationRuntime.spec.ts`，结果 4 files / 9 tests passed，0 failed。
- `vue-tsc --noEmit --pretty false`、六个受影响文件 ESLint、六个受影响文件 Prettier check 均 exit 0。本轮按返修非重复原则未再执行全量 938、Vite build、后端 build/test、PostgreSQL 或 Rabbit Busy 门禁。

### 28.2 真实双用户页面

使用全新任务专属 SQLite、租户 `pf05r11-20260910`、两个一次性账号、远程 Redis 与唯一 `pf05r11-20260910.*` Rabbit 拓扑；UnifiedHost 为 5141、Vite 为 5173。两个独立 Chromium context 从真实登录页进入聊天页，通过页面创建会话、SignalR 发送并由接收方打开消息。

| 断言 | 独立结果 |
| --- | --- |
| `PUT /read-cursor` | 200；PASS。 |
| 页面顶部总未读 | 打开消息后在不刷新页面的情况下从 1 即时收敛为 0；PASS。 |
| 当前会话 badge | 同一时点已移除；PASS。 |
| 消息可见与浏览器诊断 | 接收方可见唯一测试消息；console error、pageerror、非预期 failed request 均为 0；PASS。 |

视觉证据：`C:/Users/DONG/.codex/visualizations/2026/09/08/01a07f9d-2f78-7c21-9f2e-09144882ac11/pf05-r11-browser-acceptance/pf05-r11-unread-zero.png`，其中聊天页明确显示“未读 0”且当前会话无数字徽标。

### 28.3 清理与结论

只停止本轮启动的 UnifiedHost/Vite；精确删除 4 个 `pf05r11-20260910.*` queue 与 4 个 exchange，返回 `PF05_R11_RABBIT_TOPOLOGY_CLEANUP=PASS`。一次性凭据、SQLite、临时 Playwright 脚本和 Rabbit cleanup harness/bin/obj 已删除且不可恢复，只保留截图；5141/5173 均已确认无监听。未停止或重启用户 IDE/服务，未 stage、commit 或 push。

本节结论：`R11 READ-CURSOR PAGE UNREAD CONVERGENCE = PASS`，§27 唯一返修项已关闭。该结论绑定验收快照 `b335a6a62a917a6f4161739d984bfa976412370813e96f1ec83d4512c81a8d9e`；开发回写 hash 差异保留为交接完整性记录，但不改变本快照的实际页面与定向测试结果。PF-05 整包仍为 `NOT PRODUCTION-ACCEPTED`，因为 Embedded、双实例崩溃恢复、File/Audit 正向操作和 2C4G 容量尚未执行。

## 29. R13 聊天体验、四页导航与再认证独立复验

复验日期：2026-09-10（Asia/Taipei）。最终输入为开发回写的 203 个 `Directory.Packages.props`、`src/**`、`tests/**` 源码/测试文件；按 `PF-05-R13-development.md` 的 Windows 平台换行 manifest 算法独立重算为 `e38897f9331464fef5e4a8cf5c9916d0dc3a0d056c9276e47aff402807f047da`，与开发回写一致。HEAD/分支不因本轮验收改变；未 stage、commit 或 push，也未把 `CLAUDE.md` 纳入交付。

### 29.1 缺陷闭环与自动化

本轮在稳定候选上先后发现并直接退回开发、最终关闭五条可复现缺陷链：页面与快速抽屉各自创建 Presence 定时器；退出/跨账号后聊天 Store 和旧实时监听残留；Step-Up binding 缺少 `iat`；签发端和验签端的临时 RSA 被签名提供程序缓存后在下一次调用复用已释放对象；SQLite 丢失 `DateTimeOffset` 偏移后把新签发 grant/preparation 立即判为过期；以及空关键字在 Step-Up 上下文中为 `""`、最终查询却为 `null` 导致 canonical hash 不一致。最终实现保留后端动作、会话、范围、请求哈希和一次性消费约束，没有引入重试或绕过校验。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | 最终后端修复快照执行 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning、0 error。最终 `e388...` 相比该后端快照只改前端两文件与开发 evidence。 |
| Backend solution test | `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` 中 Collaboration 83、BuildingBlocks 168、Gateway 14、SystemData 626、Identity 619、UnifiedHost 22、Integration 12 passed / 7 environment-skipped；ReferenceData 首轮 257 passed / 1 failed，失败为内部初始化 HTTP 瞬时 503，使原命令 exit 1。精确失败用例随后 1/1 passed，ReferenceData 全项目复跑 258/258 passed。故记录为“受影响项目及独立复跑全绿，但原始全解命令不是全绿”，不篡改退出码。 |
| Frontend full unit | 最终快照 `pnpm.cmd test:unit`：130 files / 945 tests passed，exit 0。覆盖双表面单心跳、身份切换清理、Step-Up 抽屉、受控查看及空关键字请求一致性。 |
| Frontend quality | `pnpm.cmd typecheck`、`pnpm.cmd lint`、最终两文件 Prettier `--check` 均 exit 0。 |
| Frontend production build | `pnpm.cmd build`：2360 modules transformed，exit 0；仅既有 >500 KB chunk warning。 |
| Whitespace / snapshot | `git diff --check` 无 whitespace error（只有 Git LF→CRLF 提示）；最终 203 文件 aggregate 与开发完全一致。 |

### 29.2 隔离 API 与真实浏览器

使用独立 SQLite、租户 `pf05-r13-20260910`、隔离账号、临时 RSA key pair、远程 Redis 和唯一 `pf05r13-20260910.*` Rabbit 拓扑；UnifiedHost 仅监听 127.0.0.1:5141，Vite 仅监听 127.0.0.1:4173。私钥、密码、access/refresh token 和 proof 从未写入验收文档或截图。

- 个人消息隐藏实际 API：`hidden=false` 返回 400；非发送者隐藏返回 403；本人隐藏返回 200 且重复调用幂等；本人历史不再返回该消息，对方历史仍返回。该结果证明权限、本人限定与服务端用户隔离；同用户第二设备的页面事件收敛本轮没有取得独立浏览器证据。
- Step-Up 红态定位：旧候选 10 个不同 context 严格交替 200/503；修正签发缓存后 10/10 context 为 200，但 Identity password 又严格交替 403/200；修正验签缓存后 password 稳定 200。不可逆 proof SHA-256 与数据库 `proof_hash` 相同，grant 与 preparation 的 session/action/scope/request hash 全匹配，最终定位 SQLite UTC 墙钟读取偏移导致首次消费即过期。修正后 10 个唯一 32 字符 requestNId context 全为 200；两组“字段均省略”和一组“字段均为空”的 context→正确密码→受控查看均为 200。
- 最终真实 Chromium：直接打开 `/pc/collaboration/compliance/search` 后等待，`step-up-context`、Identity step-up、`/compliance/views` 调用数均为 0；页面不会自动读取。关键词输入三个空格并显式查询后，错误密码链为 context 200 / password 403，密码框清空且查询条件保留；再次输入正确密码为 context 200 / password 200 / view 200。两次 context body 和最终 view body 均没有 `keyword` 属性，关闭 `""`/`null` 哈希差异。唯一 console error 是刻意错误密码请求的 403；无 pageerror 或 failed request。
- 脱敏浏览器结果：`C:/Users/DONG/.codex/visualizations/2026/09/08/01a07f9d-2f78-7c21-9f2e-09144882ac11/pf05-r13-browser-acceptance/r13-browser-result.json`；视觉证据：同目录 `r13-controlled-view-success.png`，可见四个独立协作菜单、受控查看标题/查询区和成功返回的数据表格，没有页内重复导航 Tab。

### 29.3 未扩大结论的边界

R13 要求的“同环境改前/改后至少 20 次发送 P50/P95 与请求数”没有可用的同口径改前基线；本轮没有伪造对比数字。固定 960×680 快速抽屉、长会话/长消息双滚动、右键与键盘消息操作、同用户第二设备个人隐藏、双账号在线/离线、明暗主题/中英文和用户管理共享组件回归已有实现与自动化覆盖，但本节没有逐项取得新的真实浏览器证据。因此本节只判定 `R13 STEP-UP / PERSONAL VISIBILITY API / AUTOMATION = PASS`，不宣称 R13 全部现场矩阵或 PF-05 整包生产验收完成。既有 Embedded、双实例崩溃恢复、File/Audit 正向操作、2C4G 容量和本条 20 次性能对比继续保留为独立门禁。

### 29.4 清理

只停止本轮启动的 5141 UnifiedHost、4173 Vite 和无头 Chromium；精确删除 4 个 `pf05r13-20260910.*` queue 与 4 个 exchange。隔离 SQLite、一次性凭据、临时私钥/公钥、诊断/Playwright/Rabbit 脚本及其 bin/obj 已删除且不可恢复，只保留脱敏 JSON 与截图。5141/4173 无监听；未停止或重启用户 IDE、调试器、云 Docker 或正常租户服务。

## 30. R13 完整真实页面矩阵与返修闭环

复验日期：2026-09-10（Asia/Taipei）。本节续接 §29，只补齐 R13 尚未取得的真实页面矩阵，并对矩阵中发现的缺陷逐项返修复验；不机械重复 PostgreSQL、Rabbit Busy、全量后端和 Step-Up 已通过门禁。完整矩阵先绑定 203 文件候选 `c72f7db81e8812f34021d32a5f1073331cc8db779c5b2f47526e7d226f4d327e`，深色表格补验返修后的最终候选为 `f526fa6990d96c6dac2d1c7c26f37eb6fbc0248fd47f8457c33047c0b2aa42de`；验收按 §29 同一 Windows 换行 manifest 算法独立重算，最终文件数与哈希同开发回写完全一致。HEAD 仍为 `f86415dab3161eca46e1d20a230b681dfce6fe9c`，分支 `develop`，暂存文件为 0。

### 30.1 返修链与自动化

真实 Chromium 首轮矩阵发现：完整聊天页会被长列表撑到 1201px、页面外层滚动；发送 ACK/realtime 合并后仍冗余刷新会话列表；消息菜单的 Escape 只在焦点局部生效；SQLite 读回消息 `AcceptedOn` 时偏移语义错误，导致刚 ACK 的消息立即撤回也返回 409；两条中文独立页 H1 与既定导航文案不一致。开发依次修正完整页视口高度/双区域滚动、删除两次冗余发送后列表刷新、挂载期 document Escape、SQLite 消息时间 UTC 读取，以及中文 H1 文案。PostgreSQL 时间语义与英文文案未改变。

| 门禁 | 独立结果 |
| --- | --- |
| Backend fresh Release build | SQLite 撤回修复后执行 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning、0 error；此后的 `c72...` 与 `f526...` 只改变前端中文文案/合规页局部样式及对应测试。 |
| SQLite 时间专项 | 两条 UTC/立即撤回专项测试 2/2 passed；实际 API 发送后时间差 0 秒，立即撤回由修复前 409 收敛为 200。 |
| 前端定向回归 | 中文标题候选独立为 130 files / 948 tests passed。深色表格返修后首次独立回归发现新测试依赖 Prettier 换行，结果 1 failed / 948 passed；开发改为不依赖格式的语义正则后，同一受影响命令独立复跑为 130 files / 949 tests passed，0 failed。开发另回写 typecheck、受影响文件 ESLint、Prettier 与用户/角色共享页回归均通过。 |
| 源码一致性 | 最终 `files=203`、aggregate `f526fa6990d96c6dac2d1c7c26f37eb6fbc0248fd47f8457c33047c0b2aa42de`；`git diff --check` 无 whitespace error，只有 Git 的 LF→CRLF 提示。 |

§29 已记录的最终生产构建、Step-Up、个人可见性 API 和完整自动化结果继续有效；本节没有把 §29 原始 solution test exit 1 改写为全绿。

### 30.2 真实 Chromium 页面矩阵

使用隔离 SQLite、租户 `pf05-r13-matrix2`、一次性账号、远程 Redis 与唯一 `pf05r13matrix2.*` Rabbit 拓扑；UnifiedHost 为 127.0.0.1:5141，Vite 为 127.0.0.1:4173。两个独立账号和同账号第二浏览器 context 均从实际登录页进入，全部测试消息仅写入隔离租户。

| 场景 | 独立结果 |
| --- | --- |
| 快速抽屉 | 1440×900 视口为 960×680、无外层 footer、body `overflow:hidden`；800×600 时四边均为 16px、实际 768×568。PASS。 |
| 完整页与长内容 | 1440×900 时聊天页底边 862px、外层不滚动；消息区 `424/3056`、会话区 `505/1136`（client/scroll height）均独立 `overflow:auto`，输入区留在视口内。hover 前后宽度分别保持 825/275，无布局跳动。PASS。 |
| 消息菜单与键盘 | 本人消息右键和“更多”入口均显示“复制、撤回、仅为我删除”；复制正文完全一致；document Escape 可关闭菜单。PASS。 |
| ACK 后撤回 | 实际 SignalR 发送，随后 `PUT .../retract` 为 200；页面显示“消息已撤回”，墓碑操作数为 0。PASS。 |
| 个人删除隔离 | 本人页面删除后，同账号第二设备同步移除；对方仍能看到；本人刷新后仍不出现。PASS。 |
| 静默刷新 | 对方实时发送时，草稿、当前会话选择、消息滚动 180、会话滚动 140 均保持，loading overlay 为 0。PASS。 |
| 双账号 Presence / 已读 | 对方在线时为绿色圆点和“在线”；对方 context 关闭并越过租约后刷新为“离线”；本人消息显示一次“对方已读”。PASS。 |
| 四独立导航 | 最终中文 H1 依次严格为“受控查看、保全案件、导出记录、保留策略”，四页旧 `.collaboration-compliance__tabs` 均为 0。PASS。 |
| 主题、语言、共享管理页 | 深色模式下英文聊天“Send”可见；用户管理页有标题、查询面板和表格，角色权限页有标题和表格；无 pageerror。PASS。 |

最后一条中文标题修复通过 Vite HMR 后单独以真实 Chromium 再验，四个 H1 全部 `matches=true`，浏览器错误数组为空。§29 的受控查看“打开不请求、显式查询才 Step-Up、错密码后保留条件并清空密码、正确密码完成查询”与本节矩阵共同关闭 R13 页面范围。

### 30.3 当前版本发送性能与边界

最终前端交互修复候选上，两个真实浏览器执行 20 次文本发送：点击到本地反馈 P50 52.3ms / P95 69.2ms，满足 P95 ≤100ms；请求 ACK P50 197.0ms / P95 260.2ms；对方页面可见 P50 2046.9ms / P95 2067.7ms。20 次采样窗口内消息历史 GET 为 0，会话列表 GET 为 20（shell 未读摘要刷新）；保存结果对象随后又执行 8 次长内容/菜单动作并共享同一计数引用，因此落盘 JSON 的会话 GET 累计值为 28，不能误写成 20 次发送本身产生 28 次。发送路径不再等待或重新加载整段消息历史。

当前工作树没有可核实且可安全启动的改前源码/产物，故没有回退主工作树，也没有伪造同口径“改前”P50/P95 或提升百分比。首轮矩阵产生的请求统计使用了不同代码候选和计数口径，仅作缺陷定位，不作为性能前后对比。本节只判定“本地反馈 P95 ≤100ms”这一明确目标达标；ACK 和对方可见数字是当前测量值，不扩大为其他 SLA 通过。特别是对方可见 P95 2067.7ms 不能表述为实施 08 §13.9 的在线推送 ≤2 秒已通过；二者口径/环境不同，本节也不据此直接判该门禁失败。前后提升仍无法量化。

### 30.4 脱敏证据

- 初始矩阵与最终布局/性能：`C:/Users/DONG/.codex/visualizations/2026/09/08/01a07f9d-2f78-7c21-9f2e-09144882ac11/pf05-r13-matrix/matrix-result.json`、`matrix-retest-result.json`、`matrix-retest-full-page.png`。
- 固定抽屉与暗色英文：同目录 `matrix-drawer.png`、`matrix-chat-dark-en.png`。
- ACK 撤回、静默刷新、最终四标题：同目录 `retract-browser-result.json`、`silent-scroll-result.json`、`navigation-title-result.json`。

保留 JSON 已检查，不含 password、access/refresh token、Authorization 或私钥字段。

### 30.5 清理与结论

只停止本轮启动的 5141 UnifiedHost 和 4173 Vite；两个端口最终监听数均为 0。精确删除 `pf05r13matrix.*` 与 `pf05r13matrix2.*` 共 8 个 queue、8 个 exchange，返回 `PF05_R13_RABBIT_TOPOLOGY_CLEANUP=PASS deleted=16`。一次性凭据、SQLite、诊断/Playwright 脚本和 Rabbit cleanup harness/bin/obj 已删除且不可恢复，只保留上述脱敏 JSON 与截图；未停止用户 IDE/调试器或其他服务，未 stage、commit 或 push。

本轮完整矩阵之后的深色合规页补验、返修及额外清理见 §30.6；最终 R13 结论以该节为准。PF-05 整包仍为 `NOT PRODUCTION-ACCEPTED`：Embedded、双实例崩溃恢复、File/Audit 正向操作和 2C4G 容量是既有后续独立环境门禁。

### 30.6 四个合规页深色主题补验与返修闭环

主控针对用户原始“深色背景/文字与黄金按钮”要求追加窄范围真实页面检查。隔离租户 `pf05-r13-dark`、1440×900、真实管理员登录与四个真实路由中，四个 H1 均正确且 `data-ip-color-mode=dark`；H1 `rgb(249,250,251)` / 背景 `rgb(17,24,39)`，卡片标题白 / `rgb(31,41,55)`，正常 primary 为白字 / `rgb(0,119,161)`。再认证 drawer/footer 也是深色 `rgb(31,41,55)`，正文 `rgb(249,250,251)`，enabled primary 为白字 / `rgb(0,119,161)`；disabled submit 为浅蓝且 DOM `disabled=true`，状态可区分。上述区域无“深底黑字”缺陷。

初次补验确实发现“保全案件”和“导出记录”的 VXE 表格仍使用亮色底：表头文字 `rgb(209,213,219)` 落在 `rgb(248,248,249)`，body 为白色，空态浅灰落在白底，几乎不可读。开发只在 `.collaboration-compliance` scoped 深度选择器内绑定 VXE 语义 token，没有修改共享 `AppDataTable`。最终 `f526...` 通过同一浏览器/HMR 定向复验：两页 header 为 `rgb(249,250,251)` / `rgb(55,65,81)`，body 为 `rgb(249,250,251)` / `rgb(31,41,55)`，empty 为 `rgb(156,163,175)` / `rgb(31,41,55)`；实际渲染根同时取得 header/footer/border、12% hover、20% selected/checked 的局部 token。视觉截图确认亮白表块已消失，表头、空态、分页和正常 primary 均可辨。

用户管理与角色权限真实路由仍分别显示正确标题且各有 1 个 `.app-data-table`，浏览器错误数组为 0；源码作用域和开发的 IdentityUsersPage/IdentityRolesPage 回归共同证明本次没有全局覆盖共享表格。脱敏最终证据：`C:/Users/DONG/.codex/visualizations/2026/09/08/01a07f9d-2f78-7c21-9f2e-09144882ac11/pf05-r13-dark/dark-compliance-result.json`、`dark-search.png`、`dark-legal-holds.png`、`dark-exports.png`、`dark-retention.png`、`dark-stepup-disabled.png`、`dark-stepup-enabled.png`、`dark-shared-role-page.png`。

只停止该补验启动的 5141 UnifiedHost 和 4173 Vite；精确删除 `pf05r13dark.*` 的 4 个 queue、4 个 exchange，返回 `PF05_R13_DARK_RABBIT_CLEANUP=PASS deleted=8`。一次性凭据、SQLite、Playwright 脚本和 cleanup harness/bin/obj 已删除且不可恢复；保留 JSON/截图不含凭据、token、Authorization 或私钥。端口最终均无监听，未影响其他服务。

最终结论：`R13 AUTHORIZED PAGE MATRIX / DARK COMPLIANCE = PASS`。性能仅按 §30.3 的明确边界判定本地反馈目标通过，不声称在线推送 ≤2 秒门禁已通过，也不伪造改前基线。PF-05 整包继续为 `NOT PRODUCTION-ACCEPTED`，其余已授权工作沿原任务继续，不能以 R13 局部通过终止。
