# PF-05 R13 开发交付证据

日期：2026-09-10（本地）
角色：开发侧交付记录；独立验收应以其单独的结论为准。
边界：本文件没有修改 `docs/evidence/PF-05-acceptance.md`，也没有重新写入验收正在读取的生产快照。

## 稳定候选快照

- 当前标识（R13 合规页局部 VXE 深色表格令牌修复后）：`files=203`，`sha256=f526fa6990d96c6dac2d1c7c26f37eb6fbc0248fd47f8457c33047c0b2aa42de`。
- 当前标识（R13 四个合规独立页标题一致性修复后）：`files=203`，`sha256=c72f7db81e8812f34021d32a5f1073331cc8db779c5b2f47526e7d226f4d327e`。
- 当前标识（R13 SQLite 消息时间读取与立即撤回修复后）：`files=203`，`sha256=bfa52741008bd50c87ab2d8c21a2e5d4b8af4c3668336eb80ea8f66f47e69711`。
- 当前标识（R13 完整聊天页高度、消息摘要合并与页面级 Escape 修复后）：`files=203`，`sha256=9f488881883f87b4be81f3c50519a0a74b523947c4788fedd103ca274fb5cb0b`。
- 前一候选（R13 受控查看空关键词 Step-Up 绑定一致性修复后）：`files=203`，`sha256=e38897f9331464fef5e4a8cf5c9916d0dc3a0d056c9276e47aff402807f047da`。
- 更早候选（R13 Step-Up 验签公钥生命周期与 SQLite UTC 墙钟修复后）：`files=203`，`sha256=ce569ed19a0661a0eac47e39c25325e47ab897ef3cdd2c6a661e8a3ee154abf7`。
- 更早候选（R13 Step-Up binding 时钟与临时 RSA 签名器生命周期修复后）：`files=203`，`sha256=912deb2f4a0644217053662c140bc3e469153c4ffa0d30285497e6725ce4fc6f`。
- 更早候选（R13 跨账号会话修复与旧会话组退出断言后）：`files=203`，`sha256=e30ed5ff4ebd8b2e6d0ef8a79e918c974ca747c1fb22f21447b5f8f86f97807e`。
- 更早候选（跨账号会话修复后）：`files=203`，`sha256=d7b9f76583a28af6962eaa0fbaded5beaa9dc815f708b29efa5419af31e2b5f0`。
- 更早候选（心跳修复后）：`files=203`，`sha256=0842d7a4a97f6c4c524e90911348cd70bedde2dbb697ed4242b7054bd881d217`。
- 更早候选（心跳修复前）：`files=203`，`sha256=938cfd08cf2f45f4180ad5416553b192953802af919abb32df40a81a8af59da5`。
- 保存位置：本文件是该候选的可追溯记录，路径为 `docs/evidence/PF-05-R13-development.md`。当时没有另行落盘逐文件 manifest；若需复核，应在同一脏工作树按下列规则重新计算，不能把哈希误解为已保存的文件清单。
- 输入：Git 未暂存修改和未忽略的未跟踪文件中，仅 `Directory.Packages.props`、`src/**`、`tests/**`；排除 `bin`、`obj`、`TestResults`、`node_modules`、`dist`、`.vite`、`coverage`、`artifacts`。路径以 `/` 归一化、去重排序；每个文件取 SHA-256，再以 UTF-8、平台换行符连接 `path hash` 行并取总 SHA-256。

```powershell
$repo = (Get-Location).Path
$paths = @(
  git -c safe.directory='D:/Code/Industrial Platform/IndustrialPlatform' ls-files -m -o --exclude-standard |
    ForEach-Object { $_.Replace('\','/') } |
    Where-Object {
      ($_ -eq 'Directory.Packages.props' -or $_ -like 'src/*' -or $_ -like 'tests/*') -and
      $_ -notmatch '(^|/)(bin|obj|TestResults|node_modules|dist|\.vite|coverage|artifacts)(/|$)'
    } |
    Sort-Object -Unique
)
$lines = foreach ($path in $paths) {
  $full = Join-Path $repo ($path.Replace('/','\'))
  $digest = (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToLowerInvariant()
  "$path $digest"
}
$aggregate = [Convert]::ToHexString(
  [Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes([string]::Join([Environment]::NewLine, $lines))
  )
).ToLowerInvariant()
"files=$($paths.Count)"
"hash=$aggregate"
```

计算时 Git 曾提示无法读取 `C:\Users\DONG\.config\git\ignore`（权限拒绝），但命令仍产出上述 203 文件和哈希；复核者应在相同 checkout 与忽略规则下重算。

## 实际改动

- 聊天页与快速抽屉共用 Pinia 会话状态；仅第一个已挂载表面订阅 SignalR/心跳。抽屉采用 `size="chat"`、隐藏底部按钮和独立侧栏/消息滚动区域。
- 发送消息先显示 `Pending`，清空输入框后合并 Hub/REST 回执；不再在发送后同步刷新整段历史。已增加自己消息的复制、撤回、个人删除菜单，撤回墓碑不可复制/不可操作。
- 新增 `PF05-011` 迁移和 `collaboration_message_personal_visibility`，提供 `PUT /collaboration/api/v1/conversations/{conversationNId}/messages/{messageNId}/personal-visibility`（`{"hidden":true}`）。仅活跃租户成员本人可隐藏自己的消息；历史与客户端消息查询按当前用户过滤，并只向 `collaboration-user:{tenant}:{user}` 推送个人隐藏事件。
- 四个合规路由改为独立页面壳。受控查看在打开时不自动搜索、不创建 Step-Up 上下文；显式提交后才校验时间范围并查询。Step-Up 抽屉显示可读动作/范围摘要并在错误后清空密码。
- 增加协作端口、服务、仓储、控制器、迁移、实时发布器、前端 API/Hub/路由/多语言，以及个人可见性、乐观回执、受控查看和 Step-Up 的测试覆盖。
- 验收发现聊天页面与快速抽屉可各自创建 20 秒 Presence 定时器。已删除组件本地心跳，改由 `createCollaborationRuntimePlugin` 在应用级别仅维护一枚定时器；认证身份切换或结束时清除，连接恢复后按需重新建立。组件仍只保留一份聊天订阅与共享状态。
- 验收随后发现跨账号会话风险。聊天 Store 现在保有唯一的实时订阅释放函数和会话代次；认证身份变化或结束时，运行时先清空会话、消息、草稿、筛选、游标与加载状态并移除旧监听，再尽力离开旧会话组并停止连接。旧组件/回连异步请求通过代次校验丢弃结果；新身份首次进入会重新订阅和加载，同账号页面与抽屉仍共享状态。
- Step-Up binding 现在写入规范整数 `iat`；Identity 的共享验证可正确计算不超过两分钟的签发窗口，仍拒绝错签名密钥和过期 binding。
- Collaboration 签发器每次调用后会释放临时 RSA。为避免 JWT 库缓存一个已释放密钥的签名提供方，binding 的临时 `RsaSecurityKey` 禁用签名提供方缓存。此改动直接修复第二次起抛出 `ObjectDisposedException: RSABCrypt` 的服务端 503；没有添加前端重试或数据库重试。
- 验证端以同样的临时 RSA 公钥模型验签：`StepUpBinding.Validate` 的 `RsaSecurityKey` 同样禁用签名提供方缓存，避免 Identity 每次导入并释放公钥后在下一次验签复用已释放的 `RSACrypt`。
- SqlSugar SQLite 将 `DateTimeOffset` 以 UTC 墙钟写入 TEXT、读回时赋本地偏移；Step-up grant 仅在 SQLite 按读回墙钟与 `now.UtcDateTime` 比较。Collaboration preparation 从 SQLite 读回时恢复 UTC offset；PostgreSQL 保持原始 `DateTimeOffset` 瞬时比较与返回语义。
- 受控查看的 Step-Up context 扩展字段现在从已规范化的最终 `ComplianceSearchRequest` 读取：空白 `keyword` 在 context 和 proof-backed 搜索请求中都省略，`readOriginal` 保持不变。未放宽后端 canonical request hash 或绑定校验，且未加入重试。
- 独立真实浏览器矩阵发现完整聊天页的 `.collaboration-chat` 只有 `min-height`，长会话列表会将整个页面撑高，导致左右区域没有独立滚动。完整页改为受 shell 视口约束的 `height`、`min-height: 0` 与外层 `overflow: hidden`；抽屉仍由已有 `height: 100%` 覆盖，不改变已验收的 960 × 680 / 小视口布局。
- 已接受的发送 ACK 与 realtime message 都会先合并消息/会话摘要，却又各触发一次背景 `loadConversations`。已删除这两次冗余全量刷新，保留重连时的显式 reconciliation；单 Hub、共享 Store 与本地 ACK 合并不变。
- 自己消息的更多菜单现在由组件挂载期的 document Escape 监听关闭，并在卸载时移除监听；不拦截其他页面的 Escape 语义。
- SQLite 读回 `MessageTable` 时，现在以既有 `ReadUtcTimestamp` 还原 `AcceptedOn` 与可空 `RetractedOn` 的 UTC offset。此前 SqlSugar 将数据库中的 UTC 墙钟读成当地 offset，`RetractAsync` 再以 UTC 比较而把刚发送的消息误判为已超过两分钟；PostgreSQL 保持原始 `DateTimeOffset` 语义，撤回窗口未放宽。
- 合规独立页继续复用同一 `CompliancePage`，但中文 `legalHolds`/`exports` 页面标题现在与已正确的导航与规格统一为“保全案件”/“导出记录”；英文 `Legal holds`/`Exports` 未改。
- 独立 Chromium 验收发现 `legal-holds` 与 `exports` 复用的 `AppDataTable` 仍继承 VXE 的亮色默认值，造成深色主题下表头、表体和空状态为白色。修复把 VXE 字体、背景、表头/页脚、边框、空状态、悬停、条纹和行选中令牌及必要表面颜色限制在 `CompliancePage` 的 `.collaboration-compliance` 内；没有改动共享 `AppDataTable`、用户页或角色页。

## 开发侧验证

| 检查 | 命令或范围 | 结果 |
| --- | --- | --- |
| 后端 Release 编译（此前 R13 候选） | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 通过，0 warnings、0 errors（约 11.21 秒） |
| 后端 Release 编译（本候选） | 同上 | 未通过：现有 `IndustrialPlatform.UnifiedHost`（PID 49032）锁定其 `bin/Release` 下的 `IndustrialPlatform.Security.dll`，复制重试后 MSB3027/MSB3021。未停止或重启该进程；Collaboration 定向测试已重新编译本次 Security/Collaboration 源码。 |
| 后端 Release 编译（当前候选） | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 通过，0 warnings、0 errors（5.58 秒）；此前锁已自行释放，未执行进程停止。 |
| Step-Up 红灯复现 | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --filter "FullyQualifiedName~Security_StepUpBindingTests|FullyQualifiedName~Distinct_compliance_preparations_persist_without_alternating_failures|FullyQualifiedName~Step_up_context_persists_and_signs_each_distinct_request"` | 修复前 5 项中 2 项失败：binding 正向验证在 `StepUpBinding.Validate` 因缺失 `iat` 拒绝；完整 SQLite context 连续调用在第 2 次签名抛 `ObjectDisposedException: RSABCrypt`。独立 preparation 连续写入通过，排除基础持久化写入交替失败。 |
| Step-Up 绿灯回归 | 同上 | 5 passed，0 failed，0 skipped；验证正向/错 key/过期 binding、10 个不同 request 的 preparation 持久化，以及真实服务的 10 次 context 创建与签名。 |
| 验证公钥生命周期红/绿 | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --filter FullyQualifiedName~Reimported_public_keys_validate_after_each_key_is_disposed` | 修复前失败为 `SecurityTokenInvalidSignatureException`（第 2 次导入公钥验签命中已释放缓存）；修复后 1 passed，0 failed。 |
| SQLite proof 生命周期红/绿 | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --filter FullyQualifiedName~Sqlite_context_password_proof_consume_and_controlled_view_share_utc_expiry` | 修复前先在 grant consume 判“已过期”；修 grant 后推进至 preparation 判“未完成准备绑定”；恢复 SQLite UTC offset 后 1 passed，0 failed。该测试在同一真实 SQLite 中执行 context→BCrypt 正确密码→proof 首次消费→受控查看，并断言 `consumed_on`/`consumed_by_service=collaboration` 已写入。 |
| 协作后端测试（前一 Step-Up 候选） | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build` | 81 passed，0 failed，0 skipped（11 秒） |
| 全解后端测试（当前候选） | `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` | exit 0；Collaboration 83、BuildingBlocks 168、Gateway 14、SystemData 626、ReferenceData 258、Identity 619、UnifiedHost 22、Integration 12 passed/7 skipped，均 0 failed。 |
| 前端构建 | `pnpm.cmd build` | 通过；仅 Vite 大于 500 KB 的构建体积提示 |
| 前端单测 | `pnpm.cmd test:unit` | 130 passed files，941 passed tests（心跳修复前） |
| 受控查看空关键词红灯 | `pnpm.cmd test:unit -- tests/components/CompliancePage.spec.ts` | 修复前新增组件契约断言失败：Step-Up context 含 `keyword: ""`，而最终搜索请求省略该字段；证明 proof binding 输入不一致。 |
| 受控查看空关键词绿灯回归 | 同上 | 130 passed files，945 passed tests；模拟用户提交空白关键词、确认正确口令并完成带 proof 的搜索，断言 context 和搜索请求均无 `keyword` 且结果呈现。 |
| 前端 TypeScript 检查（本候选） | `pnpm.cmd typecheck` | 通过。 |
| 前端 lint（本候选） | `pnpm.cmd lint` | 通过。 |
| 前端 Prettier（本候选） | `pnpm.cmd exec prettier --check src/pages/pc/collaboration/CompliancePage.vue tests/components/CompliancePage.spec.ts` | 通过。 |
| 前端生产构建（本候选） | `cmd.exe /d /c pnpm.cmd build` | 通过；仅 Vite 构建产物大于 500 KB 的提示。 |
| 聊天返修红灯 | `pnpm.cmd test:unit -- tests/components/CollaborationChat.spec.ts` | 修复前 3 项失败：ACK 后 `listConversations` 从 1 次增至 2 次、realtime message 同样增至 2 次、document Escape 后菜单仍存在；其余 944 项通过。 |
| 聊天返修绿灯 | 同上 | 130 passed files，947 passed tests；覆盖 ACK/realtime 本地合并不回拉会话摘要与页面级 Escape 关闭菜单。 |
| 聊天返修类型检查 | `pnpm.cmd typecheck` | 通过。 |
| 聊天返修定向 lint | `pnpm.cmd exec eslint src/components/collaboration/CollaborationChat.vue tests/components/CollaborationChat.spec.ts` | 通过。 |
| 聊天返修 Prettier | `pnpm.cmd exec prettier --check src/components/collaboration/CollaborationChat.vue tests/components/CollaborationChat.spec.ts` | 通过。 |
| SQLite 消息撤回红灯 | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --filter FullyQualifiedName~Sqlite_message_sent_immediately_can_be_retracted` | 修复前 0 passed、1 failed：真实 SQLite 执行发送后立即撤回，在 `CollaborationService.RetractAsync` 第 469 行得到 `COLLAB_MESSAGE_RETRACT_EXPIRED`。 |
| 后端 Release 编译（本次消息 UTC 修复） | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 未通过：正在运行的 `IndustrialPlatform.UnifiedHost`（PID 30124）锁定其 `bin/Release` 中的 Collaboration API/Infrastructure DLL，重试后 20 warnings、4 errors（MSB3027/MSB3021）。未停止或重启该进程。 |
| 受影响协作 Release 编译 | `dotnet build tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release` | 通过，0 warnings、0 errors（4.56 秒）；重新编译本次 Collaboration Application/Infrastructure 依赖与测试程序集。 |
| SQLite 消息撤回绿灯回归 | `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Sqlite_message_sent_immediately_can_be_retracted|FullyQualifiedName~Sqlite_context_password_proof_consume_and_controlled_view_share_utc_expiry"` | 2 passed、0 failed、0 skipped（2 秒）；覆盖真实 SQLite 的发送→立即撤回，以及既有 context→密码→proof→受控查看 UTC 生命周期。 |
| 合规标题红灯 | `pnpm.cmd test:unit -- tests/components/CompliancePage.spec.ts` | 新增四路由标题行为断言，修复前 1 failed：`legal-holds` 的页面 H1 为“法律保全”，规格值为“保全案件”；其余 947 项通过。 |
| 合规标题绿灯回归 | 同上 | 130 passed files，948 passed tests；验证四个页面 H1 依次为“受控查看、保全案件、导出记录、保留策略”。 |
| 合规标题类型检查 | `pnpm.cmd typecheck` | 通过。 |
| 合规标题定向 lint | `pnpm.cmd exec eslint src/locales/zh-CN.ts tests/components/CompliancePage.spec.ts` | 通过。 |
| 合规标题定向 Prettier | `pnpm.cmd exec prettier --check src/locales/zh-CN.ts tests/components/CompliancePage.spec.ts` | 通过。 |
| 合规 VXE 深色表格红灯 | 独立验收 Chromium，`legal-holds`/`exports` 深色 1440×900 截图与计算样式 | 表头背景为 `rgb(248,248,249)`、表体为白色、空状态文字为亮色；确认问题来自 VXE 亮色默认令牌。 |
| 合规 VXE 消费端回归 | `pnpm.cmd test:unit -- tests/components/CompliancePage.spec.ts tests/components/IdentityUsersPage.spec.ts tests/components/IdentityRolesPage.spec.ts` | 130 passed files，949 passed tests；覆盖合规页局部 VXE 字体、表面、空状态、悬停/选中令牌的源码契约，且用户/角色页现有用例保留。Vitest/JSDOM 不注入 Vue SFC scoped CSS，故该契约直接读取消费组件源码；真实颜色仍由独立 Chromium 复验。 |
| 合规 VXE 类型检查 | `pnpm.cmd typecheck` | 通过。 |
| 合规 VXE 定向 lint | `pnpm.cmd exec eslint src/pages/pc/collaboration/CompliancePage.vue tests/components/CompliancePage.spec.ts` | 通过。 |
| 合规 VXE Prettier | `pnpm.cmd exec prettier --check src/pages/pc/collaboration/CompliancePage.vue tests/components/CompliancePage.spec.ts` | 通过。 |
| 心跳修复单测 | `pnpm.cmd test:unit -- tests/components/CollaborationChat.spec.ts tests/unit/collaborationRuntime.spec.ts` | 130 passed files，943 passed tests；覆盖页面与抽屉共存时仅一枚心跳，以及会话结束时清除心跳 |
| 跨账号会话修复单测 | 同上（格式化后重跑） | 130 passed files，944 passed tests；覆盖 A 的会话/消息/草稿清空、旧回连结果不得回填、B 首次重新订阅/加载及同账号双表面复用 |
| 本轮格式检查 | Prettier `--check`（R13 变更文件） | 通过 |

曾启动 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` 全解测试。其已输出的部分结果包括 Integration 12 passed/7 skipped、BuildingBlocks 168 passed、Collaboration 78 passed、Gateway 14 passed；但命令返回后仍有并行测试宿主运行，随后按用户要求停止开发者自身测试进程。因此该次全解测试不作为完整、最终的全量通过证据。

心跳修复采用测试先行：修复前，同一命令中“页面与抽屉只有一枚应用级心跳”断言实际得到 2，“运行时拥有一枚心跳”断言实际得到 0；修复后两项均通过。此次修复为前端范围，未重复执行后端测试。

跨账号会话修复也采用测试先行：修复前，A 退出后 `sessionStarted` 仍为 `true`；修复后 A 的 Store 与监听已清空，延迟的 A 回连结果不能回填，B 进入聊天会执行新的订阅和加载。本次同样未运行任何进程清理。

## 测试进程清理记录

用户明确要求终止“你自己测试进程”后，检查到 50 个候选 `dotnet`（启动于 12:10:12、12:10:32 或 12:10:33）及 3 个 `testhost`（PID 10588、41156、57868，均为 12:10:34；可执行位置分别落在本项目 UnifiedHost、ReferenceData、Identity 测试输出目录）。`dotnet test` 于 12:10:12 启动，筛选下界为 `2026-09-10T12:10:00`。

三个 `testhost` 的测试输出路径和紧邻的启动时刻可支持其与本轮测试相关。其余 50 个同名 `dotnet` 只有名称与时间窗口，不能充分证明全部属于本轮，也不能据此排除用户进程。尝试通过 CIM 读取命令行遭拒，故没有额外的命令行归属证据。以下命令当时确实执行过；这是一项历史记录，后续不会再以名称和时间窗口批量停止进程：

```powershell
$cutoff = Get-Date '2026-09-10T12:10:00'
$targets = Get-Process dotnet,testhost -ErrorAction SilentlyContinue |
  Where-Object { $_.StartTime -ge $cutoff }
$targets | Stop-Process -Force
```

清理后再次查询 `Get-Process dotnet,testhost` 没有输出。该观察只能证明当时没有可见的同名进程；它不能单独证明所有已停止的 `dotnet` 都是本轮测试，也不能证明没有影响用户的调试会话。没有执行直接停止或重启 5041 服务、IDE、调试器或 Vite 的命令，但这些服务/会话与被筛选 `dotnet` 进程的关系仍属未知。

## 运行时观测与未验边界

- 2026-09-10 正常开发入口配置复核：基础 Host/Identity 配置与当时的统一私有配置均未含 collaboration signer。随后在已忽略的 `src/backend/appsettings.Development.local.json` 中补齐 `TrustedServiceCalls:Signers:collaboration`、`TrustedServiceCalls:Callers:collaboration` 与 `Collaboration:ServiceIdentity` 的本地路径配置，并在已忽略的 `src/backend/.ssh/` 新建一对 3072 位开发 RSA PEM 密钥。脱敏自检确认 JSON 有效、三段 KeyId/Issuer 与路径引用一致，且该私钥可由对应公钥验证；未输出、提交或硬编码密钥。
- 配置加载链由 `DevelopmentInfrastructureConfiguration` 在 Development 模式发现并 `AddJsonFile` 此统一私有配置。复核时 5041、5141、5173 均无监听；未停止或重启任何用户服务，故该配置为“已就绪、待正常调试入口启动或重载”，不能替代现场密码提交通过证据。
- 本次 Step-Up 复核时 5141/5041 没有监听。此前全解 Release 构建仅显示 PID 49032 持有 UnifiedHost 输出 DLL；读取该进程命令行被系统拒绝，因此没有安全归属或停止。锁随后自行释放，前一候选完成了全解 Release 构建与测试；本次消息 UTC 修复的全解编译又被 PID 30124 锁定，故只完成受影响协作项目的干净 Release 编译与定向测试。开发侧未重新启动本地宿主，真实 HTTP 的 context→密码→consume→受控查看闭环仍交由独立验收在其隔离环境执行。
- 两次浏览器/桌面自动化初始化均未取得可用应用或浏览器状态，并返回 `nodeRepl.fetch request failed`；没有进行 UI 输入或浏览器交互。
- R13 通过后的深色主题定向补核再次连续两次得到同一 `nodeRepl.fetch request failed`，开发者侧仍无可用浏览器/应用，故未以本机自动化宣称真实页面通过。静态链路已复核：四个合规页面及 `CollaborationStepUpDrawer` 的关键操作均使用 Element Plus `type="primary"`；桥接层把其主色映射到 `--ip-color-primary`，共享抽屉原生主按钮显式采用 `--ip-color-on-primary`。`themeContrast.spec.ts` 覆盖三套主色配白字的普通文字阈值（≥4.5:1）。独立验收随后在真实 Chromium 中确认四页标题、按钮与 Step-Up 页脚正常，并发现 VXE 表格亮色缺陷；本次仅对该局部表格令牌返修，真实色值仍待其复验。
- 因而尚未完成真实双用户页面/抽屉会话验证、20 次发送的 P50/P95 性能测量、真实网络请求计数，或现场 Step-Up 签名链验证。
- 当前工作区环境变量与可用的 User Secrets 均未发现 TrustedServiceCalls 键；Docker CLI 不可用，未核实云 Docker 的运行时环境变量。上述本地私有配置只覆盖本机正常开发入口，不能据此断言云 Docker 已配置。
- 先前 PostgreSQL `25P02` 栈仅说明某条前序 SQL 已使事务中止；在缺少前序数据库错误和可连接本地服务的情况下，未将其宣称为已定位或已修复。

## 验收交接重点

请独立以本文件中的候选标识复算或核对变更，并重点核查：个人隐藏的权限与用户隔离、历史/重连合并不回退、四个合规入口及受控查看的显式查询门槛、Step-Up 实际签名配置；用真实会话执行连续多次 context→正确密码（不得 403/200 交替）→proof consume→受控查看，并复测空白关键词时 context 与最终搜索请求的绑定字段一致，以及 10 个不同 requestNId 的 context 不再出现 200/503 交替。对聊天返修仅复验完整页与长消息的独立滚动、20 次发送期间的会话/消息 GET 数、页面级 Escape 关闭更多菜单和 ACK 后撤回路径；新增复验真实 SQLite/HTTP 链路中“发送后等待约 500 ms 仍可在两分钟窗口内撤回、出现撤回墓碑”，以及四个独立合规页的 H1 必须依次为“受控查看、保全案件、导出记录、保留策略”。对 VXE 返修，请以深色 1440×900 重测 `legal-holds` 与 `exports`：记录 header/body/footer/empty 的计算背景与前景、边框、hover、selected/checked 状态，并确认用户/角色的共享表格不受影响；同时复看四页主按钮与 Step-Up 页脚。开发侧没有可用真实浏览器矩阵。
