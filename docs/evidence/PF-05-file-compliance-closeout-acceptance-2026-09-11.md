# PF05 文件与合规收口独立验收（2026-09-11）

## 本轮范围与结论

本轮只验收控制器指定的 PDA Enter 防误发，以及真实附件、法律保全、管理员合规导出、持久 Audit 与必要 fail-closed；未扩张到 Embedded、双实例、2C4G 或 PF06，也未重复用户已经确认的导航和页面验收。

有限范围结论为 **PARTIAL PASS**：本轮发现的生产代码/固定 HTTP 契约阻断均已修复，隔离真实 HTTP 业务链已完整通过附件、Legal Hold、SYSTEM_ADMIN 自授权导出、持久完成审计、首次成品下载和一次性重用拒绝。扫描使用本地 ClamAV INSTREAM 协议测试端点，只能证明协议适配与业务 fail-closed/放行门禁，不能证明真实 ClamAV 引擎、病毒库或恶意样本扫描；当前环境没有可用的实际扫描器，因此该环境项保持 BLOCKED。不得把本记录扩大解释为外部 PostgreSQL/Redis/RabbitMQ 或其他暂缓矩阵通过。

## 输入快照

- 开发证据：`docs/evidence/PF-05-file-compliance-closeout-2026-09-11.md`。
- 开发清单：`docs/evidence/PF-05-file-compliance-closeout-2026-09-11-manifest.txt`。
- 新清单口径：22 个生产/测试文件；每行使用 `git hash-object -- <relative-path>`，按原顺序以 LF 连接且无末尾 LF，再计算 UTF-8 SHA-256。
- 独立复算：`FileCount=22`、`MismatchCount=0`，aggregate 为 `5bd3f43eb58196e437ddb5056e34083f4c366cdf6a3e0ccc8d6d088858d6068e`，与开发交付一致。
- 未 stage、commit、push；工作区中的其他修改与未跟踪文件均按用户资产保留。

## 上轮阻断复核

1. 附件循环依赖已解除：上传者可在消息产生前完成预授权；`AppendMessageAndBindAttachmentAsync` 在 SQL 事务内同时插入消息并写入 `BoundMessageNId`，幂等重试仍补偿 File bind。
2. Legal Hold 的 File 引用建立与释放均使用冻结案件范围版本 `FrozenCaseScopeRevision = 1`，不再把状态乐观版本误作案件范围版本。
3. 导出 worker 先完成持久 Audit，再持久化 `Succeeded`，Audit 失败不暴露成功态。
4. HTTP Audit 已切换到 `POST /internal/pf05/audits/facts:ingest`；action 使用 `collaboration.*` 冻结前缀，接收端校验 action/payload 白名单并拒绝 IP/UA；事件标识确定性生成，SystemData 幂等 hash 不再因重投时间变化而冲突。

## UTC 返修复核

`SqlCollaborationRepository` 在 SQLite 下把读出的无 offset 时间按 UTC 归一化，PostgreSQL 路径保持原值；覆盖 Legal Hold 释放审批截止、Export 的 `ApprovalExpiresOn`、`ExpiresOn`、`RunDeadlineOn`、`WorkerLeaseUntil`。新增真实 SQLite 仓储 round-trip 测试 1/1 通过。重新执行真实链后，Legal Hold 创建、File Hold 同步、另一用户复核、创建者申请释放、另一用户批准、File Hold 释放同步全部成功，原 `COLLAB_HOLD_RELEASE_APPROVAL_EXPIRED` 不再出现。

## 导出 HTTP action 返修复核

`HttpSystemDataFilePort` 的导出 bind/release 断言已统一为固定接收端接受的 `file.reference.bind/release`；请求路径、业务用途及 requestNId 语义未改变。新增导出 bind/release 请求与断言测试，以及 worker→HTTP 接收契约→Clean 成品引用→完成审计→Succeeded→下载组合测试。

重新执行隔离真实链后，SYSTEM_ADMIN 自授权导出成功，worker 生成 JSON File 成品并在协议扫描 Clean 后建立 `CollaborationComplianceExport` 业务引用；export 最终为 `Succeeded`，数据库同时存在 `collaboration.compliance.export.complete` Audit。首次内容下载解析为 JSON 数组并包含目标 `messageNId`。

首次领取后，同一内容 GET 重放在授权状态门禁返回 403 且不输出第二份内容；随后对同一已领取 requestNId 重投授权返回 409 `COLLAB_EXPORT_DOWNLOAD_ALREADY_CLAIMED`。数据库命令状态为 `Claimed`，只存在一条 `collaboration.compliance.export.download.claim`，满足“重复流不得输出”的负向门禁。

## 真实隔离链路结果

环境仅绑定本机回环地址，最终使用隔离租户 `PF05-ACC-20260911-R3` 与独立 SQLite 文件；Collaboration→Identity/SystemData 强制使用 HTTP 模式，Redis 使用本地协议测试端点。ClamAV 使用本地 INSTREAM 协议测试端点返回 Clean，用于验证扫描适配器及后续业务门禁；这不代表真实 ClamAV 引擎、病毒库或恶意样本扫描通过。

- PASS：实际字节上传、内容 SHA-256、上传完成、ClamAV INSTREAM 扫描适配器、附件预授权、消息与附件原子绑定。
- PASS：同一消息幂等重试返回相同 `messageNId`。
- PASS：会话另一成员取得下载授权并下载，字节与上传内容完全一致。
- PASS：法律保全创建、File Legal Hold 建立同步、双人复核、释放申请、另一用户批准、File Legal Hold 释放同步。
- PASS：SYSTEM_ADMIN 自授权导出请求、worker 启动、实际 JSON File 成品创建、协议测试端点扫描 Clean、业务引用建立、完成 Audit 后进入 `Succeeded`。
- PASS：首次下载得到目标 JSON 内容；同一内容 GET 重放返回 403 且无第二份流，同一已领取授权重投返回 409 `COLLAB_EXPORT_DOWNLOAD_ALREADY_CLAIMED`。
- PASS：SystemData 持久化 producer=`collaboration` 的 14 类/条事实，包含 attachment、message、legal-hold、export prepare/start/complete/download authorize/download claim；未知 payload 字段数为 0，含 source IP/UA 的事实数为 0。
- BLOCKED：未接入真实 ClamAV 引擎与病毒库；协议测试端点不能替代实际恶意样本扫描结论。

未配置 ClamAV 时系统保持 `Unknown` 并禁止下载，已独立观察到 fail-closed；配置本地协议测试端点后附件链成功。RabbitMQ 在隔离验收中故意指向不可用回环端口，相关外部事件投递不在本次有限链路通过声明内。

## 自动化门禁

- `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 warning，0 error。
- Collaboration 全量：99/99 passed，0 failed。
- SystemData 全量：628/628 passed，0 failed。
- Identity 全量：620/620 passed，0 failed。
- UnifiedHost 全量：22/22 passed，0 failed。
- 上述四个受影响项目合计 1369 passed、0 failed；SQLite UTC、导出 HTTP action 与 worker 组合专项已包含在 Collaboration 全量中，不重复计数。
- 额外定向复验：HTTP Audit transport/失败暴露与导出 worker 组合 3/3；SystemData 固定 Audit 接收拒绝 2/2；Audit 同事实不同 `occurredOn` 幂等 1/1。
- 开发按程序集汇总为 1821 passed、0 failed、7 skipped；7 项为依赖外部 Redis/PostgreSQL/RabbitMQ 的 IntegrationTests 环境跳过，不能解释为外部容器链已通过。
- PDA：`vue-tsc --build` exit 0；Chat 组件 25/25 passed；Compliance 组件 5/5 passed。该结论只覆盖键盘事件与组件回归，不冒充实体扫码枪现场测试。

## 环境与边界

- 首次按开发默认外部拓扑启动时 PostgreSQL 不可达；受限环境也未获准使用未核验的远程 Redis/RabbitMQ 凭据，因此改用隔离 SQLite 与本地协议测试端点，没有改动或污染开发私有环境。
- 未修改生产代码、私有配置、迁移 checksum 或 `docs/evidence/PF-05-acceptance.md`，未停止、重启或接管用户调试实例。
- 测试凭据只在验收脚本运行时读取；交付前删除明文管理员凭据、测试私钥与隔离数据库，仅保留脱敏结果和无凭据的测试脚本。

## 复验门禁

本轮生产代码与真实 HTTP 业务链门禁已关闭。若后续提供可用的真实 ClamAV 测试实例及隔离文件授权，仅补实际 Clean/EICAR/不可用三类扫描，不重复附件、Legal Hold、导出与 Audit 链；外部 PostgreSQL/Redis/RabbitMQ 仍按控制器既定暂缓范围单独处理。
