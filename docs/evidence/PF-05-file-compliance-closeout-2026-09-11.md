# PF05 文件与合规收口证据（2026-09-11）

## 范围

本次仅收口 PF05 的文件附件、法律保全、合规导出、审计链路，以及 Identity 迁移在基础表缺失时的事务自愈；未触碰控制器已暂停的其他 PF05 前端范围，也未启动 PF06。

## 已处理问题

- 文件删除状态统一按 `Requested`、`DeletionRequested`、`Deleted` 或受限状态 fail closed；文件对象已不存在时不再返回可继续使用的快照内容。
- 协作附件打开内容要求当前授权引用与附件绑定引用完全一致；授权引用按租户、附件和用途稳定生成，避免多成员授权触发唯一键冲突。
- 合规导出使用专用用途 `CollaborationComplianceExport`，通过带会话、导出、消息/附件快照身份的业务引用绑定文件；释放支持新业务引用并兼容历史通用引用。
- 文件绑定校验支持从上传会话恢复历史文件用途；已知用途不匹配时返回 `FILE_BUSINESS_REFERENCE_INVALID`，避免把文件绑定到错误业务用途。
- PostgreSQL 文件业务引用与法律保全插入在唯一键并发竞争时使用冲突无害路径，避免连接进入 `25P02 current transaction is aborted` 后继续执行失败。
- Identity `ExtendStepUpGrantAsync` 在扩展旧结构前确保基础表存在，避免迁移账本已记账但物理表缺失时首条 `ALTER` 失败并污染后续事务。
- 审计接收固定为 `/internal/pf05/audits/facts:ingest`，生产者、actor、来源元数据和 payload 字段均由受信调用上下文与白名单约束；重投递哈希不再受每次投递时间影响，保持同一 Outbox 事件幂等。
- 合规导出按冻结 Case revision 调用 File 保全；完成审计先于导出状态写入，审计失败时不会把导出误标为成功。
- 合规导出文件引用 bind/release 的受信 action 与 SystemData 接收端统一为 `file.reference.bind` / `file.reference.release`；HTTP Worker 组合回归覆盖 Clean 成品绑定、完成审计顺序和一次性下载。
- 附件授权在消息尚未创建时只登记稳定业务引用；真实 SQL 仓储在同一事务内写入消息与 `BoundMessageNId`，随后按稳定引用调用 File，重复发送可重试完成绑定。
- SQLite 持久化读取统一将无 offset 时间解释为 UTC，保全审批窗口、导出 Approval/Expires/RunDeadline 和 Worker lease 不再因本机时区提前过期。

## 变更文件

- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/IdentitySchemaMigrations.cs`
- `tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_SchemaMigrationRunnerTests.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Application/CollaborationService.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Application/CollaborationPorts.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/SqlCollaborationRepository.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/CollaborationExportWorker.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Files/CollaborationFileStatusConsumer.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/SystemDataFilePort.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/HttpSystemDataFilePort.cs`
- `tests/Collaboration/IndustrialPlatform.Collaboration.Tests/Infrastructure_HttpSystemDataFilePortTests.cs`
- `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/HttpSystemDataAuditPort.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/Files/FileService.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/CollaborationSystemDataFileInternalController.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/CollaborationSystemDataInternalController.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/Auditing/AuditPayloadRules.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/SystemData/Pf04Store.cs`
- `tests/Collaboration/IndustrialPlatform.Collaboration.Tests/Application_ComplianceCommandExecutionTests.cs`
- `tests/Collaboration/IndustrialPlatform.Collaboration.Tests/Infrastructure_AttachmentMessageBindingTests.cs`
- `tests/Collaboration/IndustrialPlatform.Collaboration.Tests/Infrastructure_SqliteUtcTimestampTests.cs`
- `tests/Collaboration/IndustrialPlatform.Collaboration.Tests/Infrastructure_HttpSystemDataAuditPortTests.cs`
- `tests/SystemData/IndustrialPlatform.SystemData.Tests/Domain_Pf04RulesTests.cs`
- `tests/SystemData/IndustrialPlatform.SystemData.Tests/Api_Pf05AuditIngressTests.cs`

## 验证结果

验证目录：`D:\Code\Industrial Platform\IndustrialPlatform`。

- `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：通过，0 warnings，0 errors。
- `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Infrastructure_AttachmentMessageBindingTests"`：1 passed，0 failed；覆盖真实 SQLite `SqlCollaborationRepository`、授权预登记、消息/附件同事务绑定、File 业务引用和重复发送。
- `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Infrastructure_SqliteUtcTimestampTests"`：1 passed，0 failed；覆盖真实仓储的保全审批窗口、导出 Approval/Expires/RunDeadline 和 Worker lease UTC round-trip。
- `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ComplianceCommandExecutionTests"`：15 passed，0 failed；覆盖冻结 Case revision 与导出完成审计失败回滚语义。
- `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Infrastructure_HttpSystemDataAuditPortTests"`：2 passed，0 failed；覆盖固定审计路由和 payload。
- `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Pf04RulesTests"`：5 passed，0 failed；覆盖审计重投递哈希稳定性。
- `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Pf05AuditIngressTests"`：2 passed，0 failed；覆盖审计接收白名单、来源元数据和未知字段拒绝。
- `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Collaboration_reference_binding_is_idempotent_and_released_rows_cannot_be_revived|FullyQualifiedName~Sql_file_store_persists_dedicated_reference_and_hold_and_closes_the_delete_race"`：2 passed，0 failed。
- `dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Search_ReturnsTheDirectoryCursorAndRejectsAssertionReplay"`：1 passed，0 failed。
- `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --filter "FullyQualifiedName~Infrastructure_HttpSystemDataFilePortTests|FullyQualifiedName~Worker_over_http_receiver_binds_clean_artifact_before_completion_audit_and_allows_download"`：5 passed，0 failed；覆盖导出 bind/release action、Clean 成品引用、完成审计顺序和一次性下载。
- `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：各测试程序集均通过；最终按项目结果合计 1821 passed，0 failed，7 skipped，总计 1828；其中 Collaboration 99、SystemData 628、Identity 620、UnifiedHost 22、ReferenceData 258、BuildingBlocks 168、Gateway 14、IntegrationTests 12 passed；IntegrationTests 7 项外部 Redis/PostgreSQL/RabbitMQ 检查按现有条件跳过。
- `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --no-parallel`：该参数不是当前 MSBuild 的有效开关，未作为验证依据。
- `git diff --check`：通过；仅有 Git 对既有工作区换行格式的提示。

## 边界与快照说明

上述验证以本地 SQLite、内存替身和自动化测试为主；未将跳过的真实 PostgreSQL、Redis、RabbitMQ 检查误报为已完成的外部集成验证。工作区本来就包含其他用户/并行任务的修改，本次未进行 stage、commit、reset、checkout 或清理，也未修改验收证据文件 `docs/evidence/PF-05-acceptance.md`。

验收任务应以本文件、当前工作区实际文件内容和下列 Release 测试结果为准，并独立复核真实 HTTP/容器链路。

稳定文件清单：`docs/evidence/PF-05-file-compliance-closeout-2026-09-11-manifest.txt`。清单包含 22 个生产/回归文件；每行哈希由 `git hash-object -- <relative-path>` 计算，aggregate 为按清单顺序以 LF 连接这些行后对 UTF-8 字节计算 SHA-256，值为 `5bd3f43eb58196e437ddb5056e34083f4c366cdf6a3e0ccc8d6d088858d6068e`。证据文档和清单本身排除在 aggregate 外，避免自引用。
