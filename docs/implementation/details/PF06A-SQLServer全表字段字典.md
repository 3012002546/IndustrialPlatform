# SQL Server 全表字段字典（2019／2022 共用）

更新：2026-10-06。实际 SQL Server 2019 隔离库目录核对：109 张表、1777 个字段；安全表与索引已通过新增迁移改为 Identity 领域名称，实体入口同步更新。2022 未实机验证；物理类型按 2019 可用语法定义。此处是接手入口，业务详细规则仍以领域聚合、契约和仓储为准。

## 阅读和维护规则

- 每节依次给出用途、实体映射、字段、索引、外键和 CHECK；字段类型/NULL/默认/计算表达式来自实际 sys 目录，不从 DTO 猜测。
- NULL 列表示允许空值；默认值“—”表示数据库不自动填充，服务必须提供必填值。计算列由数据库维护。
- datetimeoffset(7) 保留时刻；应用统一 UTC。nvarchar 保留中文；标識列采用 BIN2，既有服务继续负责大小写规范化和空白校验。
- GUID 主键、业务标识、发布修订、双并发版本各有用途，不能互相替代。
- 软删除关联快照通过复合外键同步；自引用关系使用专用触发器。外键中的 NO_ACTION 不表示业务可以物理删除父行。
- 原始密码、令牌、数据库连接串、私钥禁止写入表说明、审计或事件。
- 新字段/约束追加新迁移；同步修改本字典和 SQL Server 的 MS_Description。Inspect 只读，不补写结构或注释。

## 按服务定位

| 服务 | 表数 | 所属内容 |
| --- | ---: | --- |
| Identity | 23 | 本文 Identity 章节 |
| SystemData | 42 | 本文 SystemData 章节 |
| ReferenceData | 24 | 本文 ReferenceData 章节 |
| Collaboration | 20 | 本文 Collaboration 章节 |

## Identity


### `identity_bootstrap_credential`

Identity 的引导凭据持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[BootstrapCredentialTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/BootstrapCredentialTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_n_id` | `nvarchar(128)` | 否 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `state` | `int` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `delivery_reference_hash` | `nvarchar(max)` | 是 | — | 一次性领取引用哈希(SHA-256);引用本身只存在于内存/受保护响应。 |
| `recovery_reference_hash` | `nvarchar(max)` | 否 | — | 一次性恢复引用哈希(SHA-256);紧急恢复门禁凭此授权。 |
| `delivered_on` | `datetimeoffset(7)` | 是 | — | 投递时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `recovered_on` | `datetimeoffset(7)` | 是 | — | 恢复时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_bootstrap_credential_tenant`：普通 (tenant_n_id,user_n_id,created_on)。
- `PK__identity__3213E83F7D22E0C6`：主键 (id)。

### `identity_login_audit`

Identity 的登录审计持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[LoginAuditTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/LoginAuditTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_n_id` | `nvarchar(128)` | 是 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `login_name_snapshot` | `nvarchar(max)` | 否 | — | 登录名称快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `result` | `int` | 否 | — | 结果；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `failure_reason` | `nvarchar(max)` | 是 | — | 失败原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `ip_address_hash` | `nvarchar(max)` | 否 | — | IP地址哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `user_agent_hash` | `nvarchar(max)` | 否 | — | 用户客户端哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `trace_id` | `nvarchar(max)` | 否 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |

索引：
- `ix_login_audit_tenant`：普通 (tenant_n_id,created_on)。
- `ix_login_audit_user`：普通 (user_n_id,created_on)。
- `PK__identity__3213E83FF9E1D986`：主键 (id)。

### `identity_operation_audit`

Identity 的操作审计持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[OperationAuditTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/OperationAuditTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(max)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action` | `nvarchar(max)` | 否 | — | 动作；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_type` | `nvarchar(128)` | 否 | — | 对象类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_n_id` | `nvarchar(128)` | 否 | — | 对象业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `before_summary` | `nvarchar(max)` | 是 | — | 变更前摘要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `after_summary` | `nvarchar(max)` | 是 | — | 变更后摘要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `trace_id` | `nvarchar(max)` | 否 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |

索引：
- `ix_operation_audit_object`：普通 (object_type,object_n_id)。
- `ix_operation_audit_tenant`：普通 (tenant_n_id,created_on)。
- `PK__identity__3213E83F87AB2111`：主键 (id)。

### `identity_outbox`

Identity 的发件箱持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[OutboxTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/OutboxTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `event_type` | `nvarchar(max)` | 否 | — | 事件类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `event_version` | `int` | 否 | — | 事件契约版本；消费者按契约处理，不能用数据库版本替代。 |
| `payload` | `nvarchar(max)` | 否 | — | 序列化事件内容；Outbox/Inbox 按对应事件契约读写，数据库只校验 JSON 结构，不替代业务校验。 |
| `event_created_time` | `datetimeoffset(7)` | 否 | — | 原事件发生/创建时刻；消费时保留生产方事实。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retry_count` | `int` | 否 | — | 重试计数；由工作器维护，受现有重试上限约束。 |
| `last_error` | `nvarchar(max)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |

索引：
- `ix_outbox_publish`：普通 (published_on,event_created_time)。
- `PK__identity__2370F7270EC154A7`：主键 (event_id)。

### `identity_permission`

Identity 的权限持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[PermissionTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/PermissionTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `type` | `int` | 否 | — | 类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `parent_permission_n_id` | `nvarchar(max)` | 是 | — | 父级权限业务标识,可为空;仅做格式校验,父级结构由持久化层建立。 |
| `parent_permission_id` | `uniqueidentifier` | 是 | — | 父级权限标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `parent_permission_is_deleted` | `bit` | 是 | — | 父级权限是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `PK__identity__3213E83FC3B4EA85`：主键 (id)。
- `uq_permission_id_isdel`：唯一 (id,is_deleted)。
- `uq_permission_nid`：唯一 (normalized_n_id)。

外键：
- `FK__identity___paren__440B1D61`：(parent_permission_id) → `identity_permission` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `ck_permission_parent`：`([parent_permission_n_id] IS NULL AND [parent_permission_id] IS NULL AND [parent_permission_is_deleted] IS NULL OR [parent_permission_n_id] IS NOT NULL AND [parent_permission_id] IS NOT NULL AND [parent_permission_is_deleted] IS NOT NULL)`。

### `identity_refresh_session`

Identity 的刷新会话持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[RefreshSessionTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/RefreshSessionTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `family_n_id` | `nvarchar(128)` | 否 | — | 令牌族业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `user_id` | `uniqueidentifier` | 否 | — | 用户标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `user_is_deleted` | `bit` | 否 | — | 用户是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `token_hash` | `nvarchar(128)` | 否 | — | 令牌哈希；服务验证/撤销使用，禁止保存或输出原始令牌。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `used_on` | `datetimeoffset(7)` | 是 | — | 使用时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `revoked_on` | `datetimeoffset(7)` | 是 | — | 撤销时间;为空表示会话仍有效。 |
| `revoke_reason` | `nvarchar(max)` | 是 | — | 撤销原因。 |
| `replaced_by_session_n_id` | `nvarchar(max)` | 是 | — | 替代操作方会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `replaced_by_session_id` | `uniqueidentifier` | 是 | — | 替代操作方会话标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `replaced_by_session_is_deleted` | `bit` | 是 | — | 替代操作方会话是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `user_agent_hash` | `nvarchar(max)` | 是 | — | 用户客户端哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `ip_address_hash` | `nvarchar(max)` | 是 | — | IP地址哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |

索引：
- `ix_refresh_family`：普通 (family_n_id)。
- `ix_refresh_user`：普通 (user_id)。
- `PK__identity__3213E83FB6FD29D2`：主键 (id)。
- `uq_refresh_id_isdel`：唯一 (id,is_deleted)。
- `uq_refresh_nid`：唯一 (n_id)。
- `uq_refresh_token`：唯一 (token_hash)。

外键：
- `FK__identity___repla__534D60F1`：(replaced_by_session_id) → `identity_refresh_session` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__identity_refresh__52593CB8`：(user_id,user_is_deleted) → `identity_user` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_role`

Identity 的角色持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[RoleTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/RoleTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `is_system` | `bit` | 否 | — | 是否系统角色,创建后不可变;系统角色禁止删除。 |

索引：
- `PK__identity__3213E83FF7C31CE8`：主键 (id)。
- `uq_role_id_isdel`：唯一 (id,is_deleted)。
- `uq_role_nid`：唯一 (tenant_n_id,normalized_n_id)。

### `identity_role_permission`

Identity 的角色权限持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[RolePermissionTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/RolePermissionTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `role_id` | `uniqueidentifier` | 否 | — | 角色标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `role_is_deleted` | `bit` | 否 | — | 角色是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `permission_id` | `uniqueidentifier` | 否 | — | 关联权限主键。 |
| `permission_is_deleted` | `bit` | 否 | — | 创建时权限软删除状态快照,由持久化层在权限删除后批量更新。 |

索引：
- `PK__identity__3213E83F6D053DF5`：主键 (id)。
- `ux_role_permission_active`：唯一 (role_id,permission_id)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_role_pe__4BAC3F29`：(role_id,role_is_deleted) → `identity_role` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。
- `FK__identity_role_pe__4CA06362`：(permission_id,permission_is_deleted) → `identity_permission` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_schema_migrations`

Identity 不可变迁移执行账本；只登记已成功提交的步骤，禁止手工删记录或改 checksum。

实体入口：[SchemaMigrationRecord.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/SchemaMigrationRecord.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `migration_id` | `varchar(255)` | 否 | — | 不可变迁移步骤标识；执行成功后记账，已应用步骤不得原地改 SQL 或删除记录。 |
| `description` | `varchar(255)` | 否 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `checksum` | `varchar(255)` | 是 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `applied_on` | `datetimeoffset(7)` | 否 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |

索引：
- `PK_identity_schema_migrations_migration_id`：主键 (migration_id)。

### `identity_seed_ledger`

Identity 种子应用账本；记录版本、作用域与提交事实，不保存秘密值。

实体入口：[SeedLedgerTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SeedLedgerTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `seed_n_id` | `nvarchar(128)` | 否 | — | 种子业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `seed_version` | `nvarchar(128)` | 否 | — | 种子版本；升级追加新版本，不覆盖已应用事实。 |
| `seed_class` | `nvarchar(max)` | 否 | — | 种子类别，按 SeedClass 对应系统基线、初始化或业务数据分类。 |
| `scope` | `nvarchar(max)` | 否 | — | 作用域编码；种子表对应 SeedScope，其余表按所属领域作用域使用，不能跨对象套枚举。 |
| `checksum` | `nvarchar(max)` | 否 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `applied_on` | `datetimeoffset(7)` | 是 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |
| `system_data_operation_n_id` | `nvarchar(max)` | 是 | — | 关联 SystemData Operation 业务标识(非敏感)。 |
| `trace_id` | `nvarchar(max)` | 是 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |

索引：
- `ix_seed_ledger_tenant`：普通 (tenant_n_id,created_on)。
- `PK__identity__3213E83F33EE7BDC`：主键 (id)。
- `uq_seed_ledger_version`：唯一 (tenant_n_id,seed_n_id,seed_version)。

### `identity_sso_browser_session`

Identity 的SSO浏览器会话持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SsoBrowserSessionTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SsoBrowserSessionTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(max)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `provider_n_id` | `nvarchar(max)` | 否 | — | 建立会话所用 Provider 业务标识(联邦注销目标),创建后不可变。 |
| `user_id` | `uniqueidentifier` | 否 | — | 用户标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `user_is_deleted` | `bit` | 否 | — | 用户是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `session_handle_hash` | `nvarchar(128)` | 否 | — | SSO 浏览器会话句柄哈希；不得作为明文会话票据返回。 |
| `auth_version` | `int` | 否 | — | 认证版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `last_activity_on` | `datetimeoffset(7)` | 否 | — | 最近活动时间,用于空闲过期滑动。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `revoked_on` | `datetimeoffset(7)` | 是 | — | 撤销时间;为空表示会话仍有效。 |
| `revoke_reason` | `int` | 是 | — | 撤销原因。 |

索引：
- `ix_sso_browser_session_tenant`：普通 (tenant_n_id,user_id)。
- `PK__identity__3213E83F6F7A4A60`：主键 (id)。
- `uq_sso_browser_session_id_isdel`：唯一 (id,is_deleted)。
- `uq_sso_browser_session_nid`：唯一 (n_id)。
- `ux_sso_browser_session_active_handle`：唯一 (session_handle_hash)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_sso_bro__6EF57B66`：(user_id,user_is_deleted) → `identity_user` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_sso_client`

Identity 的SSO客户端持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SsoClientTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SsoClientTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `oauth_client_id` | `nvarchar(256)` | 否 | — | OAuth ClientId(用于授权请求的 client_id 参数)。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |

索引：
- `PK__identity__3213E83FACC2EB58`：主键 (id)。
- `uq_sso_client_id_isdel`：唯一 (id,is_deleted)。
- `uq_sso_client_nid`：唯一 (tenant_n_id,normalized_n_id)。
- `ux_sso_client_active_oauth`：唯一 (tenant_n_id,oauth_client_id)；过滤 ([is_deleted]=(0))。

### `identity_sso_client_endpoint`

Identity 的SSO客户端端点持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SsoClientEndpointTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SsoClientEndpointTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `sso_client_id` | `uniqueidentifier` | 否 | — | SSO客户端标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `sso_client_is_deleted` | `bit` | 否 | — | 创建时所属 Client 软删除状态快照,由持久化层维护。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `endpoint_type` | `int` | 否 | — | 端点类型。 |
| `uri` | `nvarchar(2048)` | 否 | — | 原始 URI(精确匹配依据)。 |
| `normalized_uri` | `nvarchar(2048)` | 否 | — | 按 SSO 端点规则规范化的 URI；展示或配置值另看 uri。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `normalized_uri_hash` | `binary(32)` | 是 | 计算：(CONVERT([binary](32),hashbytes(N'SHA2_256',[normalized_uri]))) | 规范 URI 的 SHA-256 计算列；由数据库生成，解决长 URI 唯一索引键长限制。 |

索引：
- `PK__identity__3213E83FD74480CB`：主键 (id)。
- `ux_sso_client_endpoint_active_nid`：唯一 (sso_client_id,n_id)；过滤 ([is_deleted]=(0))。
- `ux_sso_client_endpoint_active_uri`：唯一 (sso_client_id,endpoint_type,normalized_uri_hash)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_sso_cli__6A30C649`：(sso_client_id,sso_client_is_deleted) → `identity_sso_client` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_sso_external_account`

Identity 的SSO外部账户持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SsoExternalAccountTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SsoExternalAccountTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(max)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `sso_provider_id` | `uniqueidentifier` | 否 | — | 所属 Provider 主键(复合外键)。 |
| `sso_provider_is_deleted` | `bit` | 否 | — | 创建时 Provider 软删除状态快照,由持久化层维护。 |
| `external_subject` | `nvarchar(256)` | 否 | — | 不可变外部主体标识(IdP 侧稳定标识)。 |
| `user_id` | `uniqueidentifier` | 否 | — | 用户标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `user_is_deleted` | `bit` | 否 | — | 用户是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `external_name` | `nvarchar(max)` | 是 | — | 外部显示名称。 |
| `external_email` | `nvarchar(max)` | 是 | — | 外部邮箱。 |
| `last_login_on` | `datetimeoffset(7)` | 是 | — | 最近登录时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `PK__identity__3213E83F426415DE`：主键 (id)。
- `uq_sso_external_account_id_isdel`：唯一 (id,is_deleted)。
- `uq_sso_external_account_nid`：唯一 (n_id)。
- `ux_sso_external_account_active`：唯一 (sso_provider_id,external_subject)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_sso_ext__628FA481`：(sso_provider_id,sso_provider_is_deleted) → `identity_sso_provider` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。
- `FK__identity_sso_ext__6383C8BA`：(user_id,user_is_deleted) → `identity_user` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_sso_provider`

Identity 的SSO提供方持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SsoProviderTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SsoProviderTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `protocol` | `int` | 否 | — | 协议(Oidc / Saml2)。 |
| `authority_or_metadata_url` | `nvarchar(1024)` | 否 | — | OIDC Authority 或 SAML 元数据地址。 |
| `client_id_or_entity_id` | `nvarchar(256)` | 否 | — | OIDC ClientId 或 SAML EntityId。 |
| `secret_or_certificate_reference` | `nvarchar(256)` | 是 | — | SSO 凭据/证书的受控引用；禁止填写明文秘密。 |
| `callback_path` | `nvarchar(256)` | 否 | — | 回调路径(相对路径,如 /identity/api/v1/sso/callback/oidc/{nId})。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `auto_redirect` | `bit` | 否 | — | 登录页是否自动跳转该 Provider。 |
| `provisioning_mode` | `int` | 否 | — | 账号供给策略(默认 ExistingOnly)。 |
| `logout_mode` | `int` | 否 | — | 注销策略(默认 LocalOnly)。 |
| `allowed_email_domains_json` | `nvarchar(max)` | 否 | — | 允许的邮箱域名JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `jit_default_role_n_ids_json` | `nvarchar(max)` | 否 | — | 即时开通默认角色业务标识列表JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |

索引：
- `PK__identity__3213E83F87483B4C`：主键 (id)。
- `uq_sso_provider_id_isdel`：唯一 (id,is_deleted)。
- `uq_sso_provider_nid`：唯一 (tenant_n_id,normalized_n_id)。
- `ux_sso_provider_active_client`：唯一 (tenant_n_id,client_id_or_entity_id)；过滤 ([is_deleted]=(0))。

### `identity_user`

Identity 的用户持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[UserTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/UserTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `login_name` | `nvarchar(64)` | 否 | — | 登录名。 |
| `normalized_login_name` | `nvarchar(64)` | 否 | — | 按身份模块规则标准化的登录名，用于租户内登录及唯一性判断。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `password_hash` | `nvarchar(1024)` | 否 | — | 用户口令的算法哈希；身份服务生成，禁止保存明文或写日志。 |
| `email` | `nvarchar(256)` | 是 | — | 邮箱。 |
| `phone` | `nvarchar(32)` | 是 | — | 电话。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `failed_login_count` | `int` | 否 | — | 当前连续登录失败次数。 |
| `locked_until` | `datetimeoffset(7)` | 是 | — | 临时锁定截止时间;为空表示未锁定。 |
| `auth_version` | `int` | 否 | — | 认证版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `last_login_on` | `datetimeoffset(7)` | 是 | — | 最近登录时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `must_change_password` | `bit` | 否 | ((0)) | 是否强制首次登录改密(§29A.4)。内置 admin 创建后为 false; 普通新建用户为 true,首次登录只允许改密与注销,改密成功后清除该标记。 |

索引：
- `ix_user_status_updated`：普通 (status,last_updated_on,id)。
- `PK__identity__3213E83FFA1BB1C1`：主键 (id)。
- `uq_user_id_isdel`：唯一 (id,is_deleted)。
- `uq_user_nid`：唯一 (tenant_n_id,normalized_n_id)。
- `ux_user_active_login_name`：唯一 (tenant_n_id,normalized_login_name)；过滤 ([is_deleted]=(0))。

### `identity_user_group`

Identity 的用户组持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[UserGroupTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/UserGroupTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `PK__identity__3213E83FFDDB4BE0`：主键 (id)。
- `uq_user_group_id_isdel`：唯一 (id,is_deleted)。
- `uq_user_group_nid`：唯一 (tenant_n_id,normalized_n_id)。

### `identity_user_group_membership`

Identity 的用户组成员关系持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[UserGroupMembershipTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/UserGroupMembershipTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_group_id` | `uniqueidentifier` | 否 | — | 所属用户组主键。 |
| `user_group_is_deleted` | `bit` | 否 | — | 创建时所属用户组软删除状态快照,由持久化层在父级删除后批量更新。 |
| `user_id` | `uniqueidentifier` | 否 | — | 用户标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `user_is_deleted` | `bit` | 否 | — | 用户是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |

索引：
- `ix_user_group_membership_group`：普通 (user_group_id)。
- `PK__identity__3213E83FBCC63F1B`：主键 (id)。
- `ux_user_group_membership_active`：唯一 (tenant_n_id,user_id,user_group_id)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_user_gr__75A278F5`：(user_group_id,user_group_is_deleted) → `identity_user_group` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。
- `FK__identity_user_gr__76969D2E`：(user_id,user_is_deleted) → `identity_user` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_user_group_role`

Identity 的用户组角色持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[UserGroupRoleTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/UserGroupRoleTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_group_id` | `uniqueidentifier` | 否 | — | 所属用户组主键。 |
| `user_group_is_deleted` | `bit` | 否 | — | 创建时所属用户组软删除状态快照,由持久化层在父级删除后批量更新。 |
| `role_id` | `uniqueidentifier` | 否 | — | 角色标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `role_is_deleted` | `bit` | 否 | — | 角色是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |

索引：
- `ix_user_group_role_group`：普通 (user_group_id)。
- `PK__identity__3213E83FD39360DC`：主键 (id)。
- `ux_user_group_role_active`：唯一 (tenant_n_id,user_group_id,role_id)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_user_gr__797309D9`：(user_group_id,user_group_is_deleted) → `identity_user_group` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。
- `FK__identity_user_gr__7A672E12`：(role_id,role_is_deleted) → `identity_role` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_user_role`

Identity 的用户角色持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[UserRoleTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/UserRoleTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_id` | `uniqueidentifier` | 否 | — | 用户标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `user_is_deleted` | `bit` | 否 | — | 用户是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `role_id` | `uniqueidentifier` | 否 | — | 角色标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `role_is_deleted` | `bit` | 否 | — | 角色是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |

索引：
- `PK__identity__3213E83F380873E9`：主键 (id)。
- `ux_user_role_active`：唯一 (tenant_n_id,user_id,role_id)；过滤 ([is_deleted]=(0))。

外键：
- `FK__identity_user_ro__47DBAE45`：(user_id,user_is_deleted) → `identity_user` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。
- `FK__identity_user_ro__48CFD27E`：(role_id,role_is_deleted) → `identity_role` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `identity_write_idempotency`

Identity 的写入幂等持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[IdentityWriteIdempotencyTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/IdentityWriteIdempotencyTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(128)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `idempotency_key` | `nvarchar(128)` | 否 | — | 请求幂等键；在既定租户/用户/操作范围内去重，同键不同请求拒绝。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `completed` | `bit` | 否 | — | 完成；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `PK__identity__3213E83F5B2B9C9B`：主键 (id)。
- `ux_write_idempotency_active`：唯一 (tenant_n_id,actor_user_n_id,idempotency_key)；过滤 ([is_deleted]=(0))。

### `identity_service_call_nonce`

Identity 受信服务调用防重放记录；按调用方和一次性随机值去重，保留至过期清理。由 Identity 服务维护。

实体入口：[TrustedServiceCallNonceTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/TrustedServiceCallNonceTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `issuer` | `nvarchar(256)` | 否 | — | 受信内部请求的签发服务标识；与 nonce 共同构成防重放唯一键。 |
| `nonce` | `nvarchar(128)` | 否 | — | 一次性随机值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |

索引：
- `ix_identity_service_call_nonce_expires`：普通 (expires_on)。
- `pk_identity_service_call_nonce`：主键 (issuer,nonce)。

### `identity_step_up_grant`

Identity 敏感操作再次认证授权；绑定用户、会话、操作和请求摘要，记录有效期及消费结果。由 Identity 服务维护。

实体入口：[StepUpGrantTable.cs](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/StepUpGrantTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(128)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `actor_session_n_id` | `nvarchar(128)` | 否 | — | 操作主体会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `actor_security_version` | `nvarchar(64)` | 否 | — | 操作主体安全版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `action` | `nvarchar(96)` | 否 | — | 动作；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `request_n_id` | `nvarchar(64)` | 否 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `scope_checksum` | `nvarchar(64)` | 否 | — | 范围校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `request_hash` | `nvarchar(64)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `binding_hash` | `nvarchar(64)` | 否 | — | 绑定哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `proof_hash` | `nvarchar(64)` | 否 | — | 一次性证明的哈希；用于校验，不保存原始证明。 |
| `issued_on` | `datetimeoffset(7)` | 否 | — | 签发时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `consumed_on` | `datetimeoffset(7)` | 是 | — | 已消费时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `consumed_by_service` | `nvarchar(64)` | 是 | — | 已消费操作方服务；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `consumed_receipt_n_id` | `nvarchar(128)` | 是 | — | 已消费回执业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |

索引：
- `ix_identity_step_up_grant_expiry`：普通 (expires_on)。
- `pk_identity_step_up_grant`：主键 (id)。
- `uq_identity_step_up_grant_proof`：唯一 (proof_hash)。

## SystemData


### `system_audit_fact`

SystemData 的审计事实持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `producer_service_key` | `nvarchar(128)` | 否 | — | 生产方服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `audit_event_n_id` | `nvarchar(128)` | 否 | — | 审计事件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `occurred_on` | `datetimeoffset(7)` | 否 | — | 事件发生时间。 |
| `received_on` | `datetimeoffset(7)` | 否 | — | 接收时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `actor_user_n_id` | `nvarchar(max)` | 是 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action` | `nvarchar(128)` | 否 | — | 动作；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_type` | `nvarchar(max)` | 否 | — | 对象类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_n_id` | `nvarchar(max)` | 是 | — | 对象业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `payload_json` | `nvarchar(max)` | 否 | — | 事件或命令的 JSON 内容；按对应契约序列化，秘密不得进入持久消息。 |
| `payload_hash` | `nvarchar(max)` | 否 | — | 消息内容哈希；用于重复消息一致性核对，不能代替消息身份。 |
| `trace_id` | `nvarchar(max)` | 是 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |
| `severity` | `nvarchar(max)` | 否 | — | 严重级别；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `source_ip` | `nvarchar(max)` | 是 | — | 来源IP；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `user_agent` | `nvarchar(max)` | 是 | — | 用户客户端；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `ix_system_audit_fact_query`：普通 (tenant_n_id,occurred_on,producer_service_key,action)。
- `PK__system_a__3213E83F6F90A7DC`：主键 (id)。
- `ux_system_audit_fact_event`：唯一 (tenant_n_id,producer_service_key,audit_event_n_id)。

### `system_audit_ingress_failure`

SystemData 的审计接入失败持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `failure_n_id` | `nvarchar(128)` | 否 | — | 失败业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `producer_service_key` | `nvarchar(max)` | 否 | — | 生产方服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `audit_event_n_id` | `nvarchar(max)` | 是 | — | 审计事件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `error_code` | `nvarchar(max)` | 否 | — | 错误编码；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `error_summary` | `nvarchar(max)` | 否 | — | 可用于诊断的脱敏错误摘要；不作为状态机成功条件。 |
| `payload_hash` | `nvarchar(max)` | 是 | — | 消息内容哈希；用于重复消息一致性核对，不能代替消息身份。 |
| `occurred_on` | `datetimeoffset(7)` | 否 | — | 事件发生时间。 |

索引：
- `ix_system_audit_ingress_failure_time`：普通 (tenant_n_id,occurred_on)。
- `PK__system_a__3213E83FD9BA5FCF`：主键 (id)。
- `ux_system_audit_ingress_failure_nid`：唯一 (tenant_n_id,failure_n_id)。

### `system_audit_lifecycle`

SystemData 的审计生命周期持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `producer_service_key` | `nvarchar(128)` | 否 | — | 生产方服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `audit_event_n_id` | `nvarchar(128)` | 否 | — | 审计事件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `state` | `nvarchar(max)` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `retention_until` | `datetimeoffset(7)` | 是 | — | 保留截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `archived_on` | `datetimeoffset(7)` | 是 | — | 归档时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `deleted_on` | `datetimeoffset(7)` | 是 | — | 软删除或删除确认时刻；未删除为 NULL，具体删除状态由领域规则维护。 |
| `changed_on` | `datetimeoffset(7)` | 否 | — | 变更时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `legal_hold` | `bit` | 否 | ((0)) | 对象是否受到保全约束；受保全时不能通过普通保留策略清理。 |
| `legal_hold_reason` | `nvarchar(max)` | 是 | — | 法律保全原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `PK__system_a__3213E83FE43FAE1E`：主键 (id)。
- `ux_system_audit_lifecycle_event`：唯一 (tenant_n_id,producer_service_key,audit_event_n_id)。

### `system_audit_outbox`

SystemData 的审计发件箱持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `payload` | `nvarchar(max)` | 否 | — | 序列化事件内容；Outbox/Inbox 按对应事件契约读写，数据库只校验 JSON 结构，不替代业务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retry_count` | `int` | 否 | — | 重试计数；由工作器维护，受现有重试上限约束。 |
| `last_error` | `nvarchar(max)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |
| `next_attempt_on` | `datetimeoffset(7)` | 是 | — | 下一次允许重试时刻；UTC，工作器只能领取到期项。 |
| `dead_on` | `datetimeoffset(7)` | 是 | — | 失效时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_system_audit_outbox_pending`：普通 (published_on,created_on)。
- `PK__system_a__3213E83F74A69142`：主键 (id)。
- `ux_system_audit_outbox_event`：唯一 (event_id)。

### `system_collaboration_file_hold`

SystemData 的协作文件保全持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `case_n_id` | `nvarchar(128)` | 否 | — | 保全案件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_n_id` | `nvarchar(128)` | 否 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `scope_checksum` | `nvarchar(max)` | 否 | — | 范围校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `case_revision` | `bigint` | 否 | — | 保全案件修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `owner_service` | `nvarchar(max)` | 否 | — | 所有者服务；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(128)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `updated_on` | `datetimeoffset(7)` | 否 | — | 最近状态或投影更新时间；由写入方记录 UTC 时间。 |
| `released_on` | `datetimeoffset(7)` | 是 | — | 释放时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_system_collaboration_file_hold_file_status`：普通 (tenant_n_id,file_n_id,status)。
- `PK__system_c__997DF579D8EE5D72`：主键 (tenant_n_id,case_n_id,file_n_id)。

### `system_collaboration_file_reference`

SystemData 的协作文件引用持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `reference_n_id` | `nvarchar(128)` | 否 | — | 引用业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_n_id` | `nvarchar(128)` | 否 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `conversation_n_id` | `nvarchar(max)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `message_n_id` | `nvarchar(max)` | 否 | — | 消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `attachment_n_id` | `nvarchar(128)` | 否 | — | 附件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `uploader_user_n_id` | `nvarchar(max)` | 否 | — | 上传者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `purpose` | `nvarchar(128)` | 否 | — | 用途；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `owner_service` | `nvarchar(max)` | 否 | — | 所有者服务；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(128)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `version` | `bigint` | 否 | — | 版本；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `released_on` | `datetimeoffset(7)` | 是 | — | 释放时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_system_collaboration_file_reference_file_status`：普通 (tenant_n_id,file_n_id,status)。
- `PK__system_c__82882EB11F3B961C`：主键 (tenant_n_id,reference_n_id)。
- `ux_system_collaboration_file_reference_attachment_purpose`：唯一 (tenant_n_id,attachment_n_id,purpose)。

### `system_data_database_approval`

SystemData 的数据库审批持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseApprovalTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseApprovalTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `approval_n_id` | `nvarchar(128)` | 否 | — | 审批记录业务标识。 |
| `plan_n_id` | `nvarchar(128)` | 否 | — | 计划业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `plan_checksum` | `nvarchar(max)` | 否 | — | 计划校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `target_state_fingerprint` | `nvarchar(max)` | 否 | — | 目标状态指纹；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `approved_by_user_n_id` | `nvarchar(max)` | 否 | — | 审批人业务标识。 |
| `reason` | `nvarchar(max)` | 是 | — | 原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `approved_on` | `datetimeoffset(7)` | 否 | — | 审批时间。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `ix_approval_plan`：普通 (plan_n_id,approved_on)。
- `PK__system_d__3213E83F92E0C3BC`：主键 (id)。
- `uq_approval_id_isdel`：唯一 (id,is_deleted)。
- `ux_approval_active_nid`：唯一 (tenant_n_id,approval_n_id)；过滤 ([is_deleted]=(0))。

### `system_data_database_backup_evidence`

SystemData 的数据库备份证据持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseBackupEvidenceTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseBackupEvidenceTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `evidence_n_id` | `nvarchar(128)` | 否 | — | 备份证据业务标识。 |
| `plan_n_id` | `nvarchar(128)` | 否 | — | 计划业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `plan_checksum` | `nvarchar(max)` | 否 | — | 计划校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `target_state_fingerprint` | `nvarchar(max)` | 否 | — | 目标状态指纹；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `backup_provider` | `nvarchar(max)` | 否 | — | 备份提供方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `backup_reference` | `nvarchar(max)` | 否 | — | 备份引用；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `captured_on` | `datetimeoffset(7)` | 否 | — | 采集时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `verified_on` | `datetimeoffset(7)` | 是 | — | 核验时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retention_until` | `datetimeoffset(7)` | 否 | — | 保留截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `verified_by_user_n_id` | `nvarchar(max)` | 是 | — | 验证人业务标识。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `ix_backup_evidence_plan`：普通 (plan_n_id,captured_on)。
- `PK__system_d__3213E83F1E1C401C`：主键 (id)。
- `uq_backup_evidence_id_isdel`：唯一 (id,is_deleted)。
- `ux_backup_evidence_active_nid`：唯一 (tenant_n_id,evidence_n_id)；过滤 ([is_deleted]=(0))。

### `system_data_database_environment_policy`

SystemData 的数据库环境策略持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseEnvironmentPolicyTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseEnvironmentPolicyTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `environment_n_id` | `nvarchar(128)` | 否 | — | 环境业务标识。 |
| `environment_kind` | `int` | 否 | — | 环境种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `approval_required` | `bit` | 否 | — | 审批必需；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `backup_required` | `bit` | 否 | — | 备份必需；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `plan_ttl_seconds` | `int` | 否 | — | 计划有效时长秒；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `plan_timeout_seconds` | `int` | 否 | — | 计划超时秒；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `apply_timeout_seconds` | `int` | 否 | — | 执行超时秒；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `max_pre_migration_retries` | `int` | 否 | — | 最大执行前迁移重试；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `policy_revision` | `int` | 否 | — | 策略修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |

索引：
- `PK__system_d__3213E83F9112D170`：主键 (id)。
- `uq_env_policy_id_isdel`：唯一 (id,is_deleted)。
- `ux_env_policy_active`：唯一 (tenant_n_id,environment_n_id)；过滤 ([is_deleted]=(0))。

### `system_data_database_migration_observation`

SystemData 的数据库迁移观察持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseMigrationObservationTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseMigrationObservationTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `environment_n_id` | `nvarchar(128)` | 否 | — | 环境业务标识。 |
| `service_key` | `nvarchar(128)` | 否 | — | 服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `database_identity_fingerprint` | `nvarchar(max)` | 否 | — | 数据库身份指纹；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `observed_version` | `nvarchar(max)` | 否 | — | 观察版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `artifact_checksum` | `nvarchar(max)` | 否 | — | 产物校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `observed_on` | `datetimeoffset(7)` | 否 | — | 观察时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `operation_n_id` | `nvarchar(max)` | 是 | — | 操作业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `verification_status` | `int` | 否 | — | 核验状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `module_key` | `nvarchar(128)` | 是 | — | 所属模块的稳定键；由模块目录或产物指定，用于账本及路由范围隔离。 |

索引：
- `ix_migration_observation_module_target`：普通 (tenant_n_id,environment_n_id,service_key,module_key,observed_on)。
- `ix_migration_observation_target`：普通 (tenant_n_id,environment_n_id,service_key,observed_on)。
- `PK__system_d__3213E83F58F45DCF`：主键 (id)。
- `uq_migration_observation_id_isdel`：唯一 (id,is_deleted)。

### `system_data_database_operation`

SystemData 的数据库操作持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseProvisionOperationTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseProvisionOperationTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `operation_n_id` | `nvarchar(128)` | 否 | — | 操作业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `kind` | `int` | 否 | — | 种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `environment_n_id` | `nvarchar(max)` | 否 | — | 环境业务标识。 |
| `service_key` | `nvarchar(max)` | 否 | — | 服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `plan_n_id` | `nvarchar(max)` | 是 | — | 计划业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `requested_version` | `nvarchar(max)` | 否 | — | 请求版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `idempotency_key` | `nvarchar(128)` | 否 | — | 请求幂等键；在既定租户/用户/操作范围内去重，同键不同请求拒绝。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `phase` | `int` | 否 | — | 阶段；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `attempt` | `int` | 否 | — | 尝试；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `lease_owner` | `nvarchar(max)` | 是 | — | 任务租约持有者；由工作器领取时设置，完成/续租需匹配持有者。 |
| `lease_expires_on` | `datetimeoffset(7)` | 是 | — | 租约到期时间。 |
| `heartbeat_on` | `datetimeoffset(7)` | 是 | — | 最近一次心跳时间。 |
| `queued_on` | `datetimeoffset(7)` | 否 | — | 入队时间。 |
| `started_on` | `datetimeoffset(7)` | 是 | — | 开始时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `timeout_on` | `datetimeoffset(7)` | 否 | — | 超时时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `sanitized_error_code` | `nvarchar(max)` | 是 | — | 脱敏错误码。 |
| `sanitized_error_summary` | `nvarchar(max)` | 是 | — | 已脱敏错误摘要；只能保存允许的技术说明。 |
| `trace_id` | `nvarchar(max)` | 否 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |
| `created_by_user_n_id` | `nvarchar(max)` | 否 | — | 创建操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `module_key` | `nvarchar(max)` | 是 | — | 所属模块的稳定键；由模块目录或产物指定，用于账本及路由范围隔离。 |

索引：
- `ix_operation_tenant_status`：普通 (tenant_n_id,status,queued_on)。
- `PK__system_d__3213E83FEF741F6D`：主键 (id)。
- `uq_operation_id_isdel`：唯一 (id,is_deleted)。
- `ux_operation_active_idempotency`：唯一 (tenant_n_id,idempotency_key)；过滤 ([is_deleted]=(0))。
- `ux_operation_active_nid`：唯一 (tenant_n_id,operation_n_id)；过滤 ([is_deleted]=(0))。

### `system_data_database_operation_step`

SystemData 的数据库操作步骤持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseOperationStepTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseOperationStepTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `operation_id` | `uniqueidentifier` | 否 | — | 操作标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `operation_is_deleted` | `bit` | 否 | — | 操作是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `sequence` | `int` | 否 | — | 对象内部顺序号；由所属聚合/执行器分配，不以客户端时间替代。 |
| `phase` | `int` | 否 | — | 阶段；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `attempt` | `int` | 否 | — | 尝试；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `started_on` | `datetimeoffset(7)` | 是 | — | 开始时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `sanitized_error_code` | `nvarchar(max)` | 是 | — | 脱敏错误码。 |
| `sanitized_error_summary` | `nvarchar(max)` | 是 | — | 已脱敏错误摘要；只能保存允许的技术说明。 |

索引：
- `ix_operation_step_parent`：普通 (operation_id,sequence)。
- `PK__system_d__3213E83F3B343D4A`：主键 (id)。

外键：
- `FK__system_data_data__3C34F16F`：(operation_id,operation_is_deleted) → `system_data_database_operation` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `system_data_database_plan`

SystemData 的数据库计划持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseProvisionPlanTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseProvisionPlanTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `plan_n_id` | `nvarchar(128)` | 否 | — | 计划业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `environment_n_id` | `nvarchar(max)` | 否 | — | 环境业务标识。 |
| `service_key` | `nvarchar(max)` | 否 | — | 服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `requested_migration_version` | `nvarchar(max)` | 否 | — | 请求的目标迁移版本。 |
| `current_migration_version` | `nvarchar(max)` | 否 | — | 当前迁移版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `target_state_fingerprint` | `nvarchar(max)` | 否 | — | 目标状态指纹；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `plan_checksum` | `nvarchar(128)` | 否 | — | 计划校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `risk_level` | `int` | 否 | — | 风险级别；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `destructive_change_detected` | `bit` | 否 | — | 是否检测到破坏性变更。 |
| `required_policies` | `int` | 否 | — | 必需策略；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `created_by_user_n_id` | `nvarchar(max)` | 否 | — | 创建操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `module_key` | `nvarchar(max)` | 是 | — | 所属模块的稳定键；由模块目录或产物指定，用于账本及路由范围隔离。 |
| `service_requires_apply` | `bit` | 是 | — | 服务初始化器首次计划声明的 RequiresApply；legacy SQL Runner 计划为空。 |

索引：
- `PK__system_d__3213E83FBC2E58E5`：主键 (id)。
- `uq_plan_id_isdel`：唯一 (id,is_deleted)。
- `ux_plan_active_checksum`：唯一 (plan_checksum)；过滤 ([is_deleted]=(0))。
- `ux_plan_active_nid`：唯一 (tenant_n_id,plan_n_id)；过滤 ([is_deleted]=(0))。

### `system_data_database_plan_step`

SystemData 的数据库计划步骤持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabasePlanStepTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabasePlanStepTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `plan_id` | `uniqueidentifier` | 否 | — | 计划标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `plan_is_deleted` | `bit` | 否 | — | 计划是否删除：关联父行的软删除快照；复合外键或专用触发器同步，不能由调用方伪造。 |
| `sequence` | `int` | 否 | — | 对象内部顺序号；由所属聚合/执行器分配，不以客户端时间替代。 |
| `step_kind` | `nvarchar(max)` | 否 | — | 步骤种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `input_summary` | `nvarchar(max)` | 是 | — | 输入摘要(不含敏感信息)。 |
| `precondition_summary` | `nvarchar(max)` | 是 | — | 前置条件摘要。 |
| `postcondition_summary` | `nvarchar(max)` | 是 | — | 后置条件摘要。 |
| `risk_level` | `int` | 否 | — | 风险级别；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `ix_plan_step_parent`：普通 (plan_id,sequence)。
- `PK__system_d__3213E83F855D71BD`：主键 (id)。

外键：
- `FK__system_data_data__30C33EC3`：(plan_id,plan_is_deleted) → `system_data_database_plan` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `system_data_database_registration`

SystemData 的数据库登记持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseRegistrationTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseRegistrationTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `environment_n_id` | `nvarchar(128)` | 否 | — | 环境业务标识。 |
| `service_key` | `nvarchar(128)` | 否 | — | 服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `provider` | `nvarchar(max)` | 否 | — | 目标数据库引擎编码；Sqlite/PostgreSQL/SqlServer 三种沿既有注册规则解析。 |
| `logical_database_name` | `nvarchar(max)` | 否 | — | 服务逻辑数据库键；用于拓扑映射，不直接当物理库名连接。 |
| `physical_database_name` | `nvarchar(max)` | 否 | — | 已解析物理数据库名；SQL Server 是数据库名称，SQLite 是绝对文件路径。 |
| `is_shared_physical_database` | `bit` | 否 | — | 目标是否多个模块共享物理库；来自已解析拓扑事实。 |
| `topology_mode` | `nvarchar(max)` | 否 | — | 拓扑模式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `topology_revision` | `nvarchar(max)` | 否 | — | 拓扑修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `migration_artifact_id` | `nvarchar(max)` | 否 | — | 迁移产物标识。 |
| `migration_version` | `nvarchar(max)` | 否 | — | 迁移版本。 |
| `artifact_checksum` | `nvarchar(max)` | 否 | — | 产物校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `artifact_signature` | `nvarchar(max)` | 是 | — | 产物签名；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `owner_n_id` | `nvarchar(max)` | 否 | — | 所有者业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `desired_state` | `int` | 否 | — | 期望状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `auto_provision` | `bit` | 否 | — | 自动开通；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `auto_migrate` | `bit` | 否 | — | 自动迁移；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `manifest_version` | `nvarchar(max)` | 否 | — | 清单版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `manifest_checksum` | `nvarchar(max)` | 否 | — | 清单校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `module_key` | `nvarchar(128)` | 是 | — | 所属模块的稳定键；由模块目录或产物指定，用于账本及路由范围隔离。 |
| `seed_sets` | `nvarchar(max)` | 是 | — | 种子集合；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `uses_service_initializer` | `bit` | 否 | ((0)) | 使用服务初始化器；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `PK__system_d__3213E83FD3F43578`：主键 (id)。
- `uq_registration_id_isdel`：唯一 (id,is_deleted)。
- `ux_registration_active`：唯一 (tenant_n_id,environment_n_id,service_key,module_key)；过滤 ([is_deleted]=(0))。

### `system_data_feature`

SystemData 的功能持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `feature_n_id` | `nvarchar(max)` | 否 | — | 功能业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `owner_module_n_id` | `nvarchar(max)` | 否 | — | 所有者模块业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `default_enabled` | `bit` | 否 | — | 默认启用；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `feature_revision` | `bigint` | 否 | — | 功能修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |

索引：
- `PK__system_d__3213E83F904638AF`：主键 (id)。

### `system_data_feature_override`

SystemData 的功能覆盖持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `feature_n_id` | `nvarchar(max)` | 否 | — | 功能业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `mode` | `nvarchar(max)` | 否 | — | 模式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `reason` | `nvarchar(max)` | 是 | — | 原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `PK__system_d__3213E83F89DA9661`：主键 (id)。

### `system_data_module_manifest`

SystemData 的模块清单持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `module_n_id` | `nvarchar(max)` | 否 | — | 模块业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `manifest_version` | `nvarchar(max)` | 否 | — | 清单版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `checksum` | `nvarchar(max)` | 否 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `permission_n_ids_json` | `nvarchar(max)` | 否 | — | 权限业务标识列表JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `permission_receipt_version` | `nvarchar(max)` | 是 | — | 权限回执版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `permission_receipt_checksum` | `nvarchar(max)` | 是 | — | 权限回执校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `permission_verified_on` | `datetimeoffset(7)` | 是 | — | 权限核验时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `permission_manifest_json` | `nvarchar(max)` | 否 | (N'[]') | 权限清单JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `resource_manifest_json` | `nvarchar(max)` | 否 | (N'[]') | 资源清单JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `feature_manifest_json` | `nvarchar(max)` | 否 | (N'[]') | 功能清单JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |

索引：
- `PK__system_d__3213E83FE74F57D3`：主键 (id)。

### `system_data_navigation`

SystemData 的导航持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `node_n_id` | `nvarchar(max)` | 否 | — | 节点业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `navigation_set_n_id` | `nvarchar(max)` | 否 | — | 导航集合业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `parent_node_n_id` | `nvarchar(max)` | 是 | — | 父级节点业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `kind` | `nvarchar(max)` | 否 | — | 种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `label` | `nvarchar(max)` | 否 | — | 标签；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `icon_key` | `nvarchar(max)` | 是 | — | 图标键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `resource_n_id` | `nvarchar(max)` | 是 | — | 资源业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `feature_n_id` | `nvarchar(max)` | 是 | — | 功能业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `display_order` | `int` | 否 | — | 页面展示顺序；由管理配置维护。 |
| `visible_terminals_json` | `nvarchar(max)` | 否 | — | 可见终端JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `action_resource_n_ids_json` | `nvarchar(max)` | 否 | (N'[]') | 动作资源业务标识列表JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |

索引：
- `PK__system_d__3213E83F3B1A52B7`：主键 (id)。

### `system_data_navigation_set`

SystemData 的导航集合持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `navigation_set_n_id` | `nvarchar(max)` | 否 | — | 导航集合业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `draft_revision` | `bigint` | 否 | — | 草稿修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `active_snapshot_revision` | `bigint` | 是 | — | 活动快照修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `previous_snapshot_revision` | `bigint` | 是 | — | 前一个快照修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |

索引：
- `PK__system_d__3213E83F115B0787`：主键 (id)。

### `system_data_navigation_snapshot`

SystemData 的导航快照持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `snapshot_revision` | `bigint` | 否 | — | 快照修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `published_on` | `datetimeoffset(7)` | 否 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `checksum` | `nvarchar(max)` | 否 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `nodes_json` | `nvarchar(max)` | 否 | — | 节点JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |

索引：
- `PK__system_d__3213E83FFAE87AEA`：主键 (id)。

### `system_data_operation_audit`

SystemData 的操作审计持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationAuditTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationAuditTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(max)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action` | `nvarchar(max)` | 否 | — | 动作；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_type` | `nvarchar(max)` | 否 | — | 对象类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_n_id` | `nvarchar(max)` | 否 | — | 对象业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `reason` | `nvarchar(max)` | 是 | — | 原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `before_summary` | `nvarchar(max)` | 是 | — | 变更前摘要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `after_summary` | `nvarchar(max)` | 是 | — | 变更后摘要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `trace_id` | `nvarchar(max)` | 否 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |

索引：
- `PK__system_d__3213E83FB3B99190`：主键 (id)。

### `system_data_organization`

SystemData 的组织持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[AdministrativeOrganizationTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/AdministrativeOrganizationTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `normalized_name` | `nvarchar(128)` | 否 | — | 标准化名称，用于已有名称比较规则，不能由调用方任意指定。 |
| `type` | `int` | 否 | — | 类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `parent_organization_n_id` | `nvarchar(128)` | 是 | — | 父级组织业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `parent_organization_id` | `uniqueidentifier` | 是 | — | 父组织主键快照(复合外键引用)。 |
| `parent_organization_is_deleted` | `bit` | 否 | — | 父组织删除状态快照(复合外键同步)。 |
| `display_order` | `int` | 否 | — | 页面展示顺序；由管理配置维护。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `organization_revision` | `bigint` | 否 | — | 组织修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |

索引：
- `ix_organization_parent`：普通 (tenant_n_id,parent_organization_n_id,status,display_order)。
- `PK__system_d__3213E83FB5D1997F`：主键 (id)。
- `uq_organization_id_isdel`：唯一 (id,is_deleted)。
- `ux_organization_active_nid`：唯一 (tenant_n_id,normalized_n_id)；过滤 ([is_deleted]=(0))。
- `ux_organization_active_sibling_name`：唯一 (tenant_n_id,parent_organization_n_id,normalized_name)；过滤 ([is_deleted]=(0))。

外键：
- `FK__system_da__paren__45BE5BA9`：(parent_organization_id) → `system_data_organization` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

### `system_data_outbox`

SystemData 的发件箱持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOutboxTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOutboxTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `event_type` | `nvarchar(max)` | 否 | — | 事件类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `event_version` | `nvarchar(max)` | 否 | — | 事件契约版本；消费者按契约处理，不能用数据库版本替代。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `payload` | `nvarchar(max)` | 否 | — | 序列化事件内容；Outbox/Inbox 按对应事件契约读写，数据库只校验 JSON 结构，不替代业务校验。 |
| `event_created_time` | `datetimeoffset(7)` | 否 | — | 原事件发生/创建时刻；消费时保留生产方事实。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retry_count` | `int` | 否 | — | 重试计数；由工作器维护，受现有重试上限约束。 |
| `last_error` | `nvarchar(max)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |
| `dead_on` | `datetimeoffset(7)` | 是 | — | 失效时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `next_attempt_on` | `datetimeoffset(7)` | 是 | — | 下一次允许重试时刻；UTC，工作器只能领取到期项。 |

索引：
- `ix_system_data_outbox_pending`：普通 (published_on,event_created_time)。
- `PK__system_d__2370F727249AB014`：主键 (event_id)。

### `system_data_position`

SystemData 的岗位持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[PositionTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/PositionTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `organization_n_id` | `nvarchar(128)` | 否 | — | 组织业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `organization_id` | `uniqueidentifier` | 否 | — | 所属组织主键快照(复合外键引用)。 |
| `organization_is_deleted` | `bit` | 否 | — | 所属组织删除状态快照(复合外键同步)。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `normalized_name` | `nvarchar(128)` | 否 | — | 标准化名称，用于已有名称比较规则，不能由调用方任意指定。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `display_order` | `int` | 否 | — | 页面展示顺序；由管理配置维护。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `ix_position_organization`：普通 (tenant_n_id,organization_n_id)。
- `PK__system_d__3213E83F192FF2AC`：主键 (id)。
- `uq_position_id_isdel`：唯一 (id,is_deleted)。
- `ux_position_active_nid`：唯一 (tenant_n_id,normalized_n_id)；过滤 ([is_deleted]=(0))。
- `ux_position_active_org_name`：唯一 (tenant_n_id,organization_n_id,normalized_name)；过滤 ([is_deleted]=(0))。

外键：
- `FK__system_data_posi__4B7734FF`：(organization_id,organization_is_deleted) → `system_data_organization` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `system_data_projection_revision`

SystemData 的投影修订持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `area` | `nvarchar(128)` | 否 | — | 区域；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `revision` | `bigint` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `generated_on` | `datetimeoffset(7)` | 否 | — | 生成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `PK__system_d__3213E83FC71E6809`：主键 (id)。
- `ux_system_data_projection_revision_tenant_area`：唯一 (tenant_n_id,area)。

### `system_data_schema_migrations`

SystemData 不可变迁移执行账本；只登记已成功提交的步骤，禁止手工删记录或改 checksum。

实体入口：[SystemDataSchemaMigrationRecord.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SystemDataSchemaMigrationRecord.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `migration_id` | `nvarchar(128)` | 否 | — | 不可变迁移步骤标识；执行成功后记账，已应用步骤不得原地改 SQL 或删除记录。 |
| `description` | `nvarchar(max)` | 否 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `checksum` | `varchar(64)` | 是 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `applied_on` | `datetimeoffset(7)` | 否 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |

索引：
- `PK__system_d__DE86177D3EC789C6`：主键 (migration_id)。

### `system_data_seed_ledger`

SystemData 种子应用账本；记录版本、作用域与提交事实，不保存秘密值。

实体入口：[SystemDataSeedLedgerTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataSeedLedgerTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `seed_key` | `nvarchar(max)` | 否 | — | 种子逻辑键；与模块、版本组成幂等身份。 |
| `seed_version` | `nvarchar(max)` | 否 | — | 种子版本；升级追加新版本，不覆盖已应用事实。 |
| `checksum` | `nvarchar(max)` | 否 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `applied_on` | `datetimeoffset(7)` | 否 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |

索引：
- `PK__system_d__3213E83FA53ECFE2`：主键 (id)。

### `system_data_seed_observation`

SystemData 的种子观察持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[DatabaseSeedObservationTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/DatabaseSeedObservationTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `environment_n_id` | `nvarchar(128)` | 否 | — | 环境业务标识。 |
| `service_key` | `nvarchar(128)` | 否 | — | 服务键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `module_key` | `nvarchar(128)` | 否 | — | 所属模块的稳定键；由模块目录或产物指定，用于账本及路由范围隔离。 |
| `seed_key` | `nvarchar(128)` | 否 | — | 种子逻辑键；与模块、版本组成幂等身份。 |
| `seed_version` | `nvarchar(max)` | 否 | — | 种子版本；升级追加新版本，不覆盖已应用事实。 |
| `checksum` | `nvarchar(max)` | 否 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `scope` | `int` | 否 | — | 作用域编码；种子表对应 SeedScope，其余表按所属领域作用域使用，不能跨对象套枚举。 |
| `status` | `int` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `applied_on` | `datetimeoffset(7)` | 是 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |
| `operation_n_id` | `nvarchar(max)` | 是 | — | 操作业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `verification_status` | `int` | 否 | — | 核验状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `ix_seed_observation_target`：普通 (tenant_n_id,environment_n_id,service_key,module_key,seed_key,created_on)。
- `PK__system_d__3213E83F9F93C898`：主键 (id)。
- `uq_seed_observation_id_isdel`：唯一 (id,is_deleted)。

### `system_data_service_catalog`

SystemData 的服务目录持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `service_n_id` | `nvarchar(max)` | 否 | — | 服务业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `kind` | `nvarchar(max)` | 否 | — | 种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `entry_point` | `nvarchar(max)` | 否 | — | 入口点；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `gateway_path_prefix` | `nvarchar(max)` | 是 | — | 网关路径前缀；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `health_path` | `nvarchar(max)` | 是 | — | 健康路径；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `supported_terminals_json` | `nvarchar(max)` | 否 | — | 支持的终端JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `source` | `nvarchar(max)` | 否 | — | 来源；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `owner_organization_n_id` | `nvarchar(max)` | 是 | — | 所有者组织业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `owner_organization_name_snapshot` | `nvarchar(max)` | 是 | — | 所有者组织名称快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `owner_display_snapshot` | `nvarchar(max)` | 是 | — | 所有者显示快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `technical_lead_user_n_id` | `nvarchar(max)` | 是 | — | 技术负责人用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `technical_owner_user_n_id` | `nvarchar(max)` | 是 | — | 技术所有者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `technical_owner_auth_version` | `nvarchar(max)` | 是 | — | 技术所有者认证版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `technical_lead_display_name_snapshot` | `nvarchar(max)` | 是 | — | 技术负责人显示名称快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |

索引：
- `PK__system_d__3213E83FF06832FB`：主键 (id)。

### `system_data_theme_policy`

SystemData 的主题策略持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `policy_n_id` | `nvarchar(max)` | 否 | — | 策略业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `allowed_palettes_json` | `nvarchar(max)` | 否 | — | 允许的配色JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `allowed_modes_json` | `nvarchar(max)` | 否 | — | 允许的模式JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `allowed_pc_densities_json` | `nvarchar(max)` | 否 | — | 允许的PC密度JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `default_palette` | `nvarchar(max)` | 否 | — | 默认配色；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `default_mode` | `nvarchar(max)` | 否 | — | 默认模式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `default_pc_density` | `nvarchar(max)` | 否 | — | 默认PC密度；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `policy_revision` | `bigint` | 否 | — | 策略修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |

索引：
- `PK__system_d__3213E83FA0E4C911`：主键 (id)。

### `system_data_ui_resource`

SystemData 的界面资源持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[ControlPlaneTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/ControlPlaneTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `resource_n_id` | `nvarchar(max)` | 否 | — | 资源业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `owner_module_n_id` | `nvarchar(max)` | 否 | — | 所有者模块业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `manifest_version` | `nvarchar(max)` | 否 | — | 清单版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `resource_type` | `nvarchar(max)` | 否 | — | 资源类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `name` | `nvarchar(256)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `route_name` | `nvarchar(max)` | 是 | — | 路由名称；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `required_permission_n_id` | `nvarchar(max)` | 是 | — | 必需权限业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `supported_terminals_json` | `nvarchar(max)` | 否 | — | 支持的终端JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `PK__system_d__3213E83F24719078`：主键 (id)。

### `system_data_user_assignment`

SystemData 的用户分配持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[UserAssignmentTable.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/UserAssignmentTable.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(max)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `normalized_n_id` | `nvarchar(128)` | 否 | — | 标准化业务标识，用于按既有规则去重和查询；与展示值同时由服务维护。 |
| `user_n_id` | `nvarchar(128)` | 否 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `user_display_name_snapshot` | `nvarchar(max)` | 否 | — | 用户显示名称快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `organization_n_id` | `nvarchar(max)` | 否 | — | 组织业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `position_n_id` | `nvarchar(128)` | 否 | — | 任职岗位业务标识。 |
| `position_id` | `uniqueidentifier` | 否 | — | 岗位主键快照(复合外键引用)。 |
| `position_is_deleted` | `bit` | 否 | — | 岗位删除状态快照(复合外键同步)。 |
| `is_primary` | `bit` | 否 | — | 是否主要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `effective_from` | `datetimeoffset(7)` | 否 | — | 生效起始；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `effective_to` | `datetimeoffset(7)` | 是 | — | 生效目标；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `state` | `int` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `cancelled_on` | `datetimeoffset(7)` | 是 | — | 取消时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `cancel_reason` | `nvarchar(max)` | 是 | — | 取消原因(可选)。 |

索引：
- `ix_user_assignment_position_time`：普通 (position_n_id,effective_from)。
- `ix_user_assignment_user_time`：普通 (tenant_n_id,user_n_id,effective_from)。
- `PK__system_d__3213E83F2E13F870`：主键 (id)。
- `uq_user_assignment_id_isdel`：唯一 (id,is_deleted)。
- `ux_user_assignment_active_nid`：唯一 (tenant_n_id,normalized_n_id)；过滤 ([is_deleted]=(0))。

外键：
- `FK__system_data_user__4F47C5E3`：(position_id,position_is_deleted) → `system_data_position` (id,is_deleted)；UPDATE CASCADE，DELETE NO_ACTION。

### `system_file_object`

SystemData 的文件对象持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `file_n_id` | `nvarchar(128)` | 否 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `upload_session_n_id` | `nvarchar(128)` | 否 | — | 上传会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `owner_user_n_id` | `nvarchar(max)` | 否 | — | 所有者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_name` | `nvarchar(max)` | 否 | — | 文件名称；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `content_type` | `nvarchar(max)` | 否 | — | 内容类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `content_length` | `bigint` | 否 | — | 内容长度；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `sha256` | `nvarchar(128)` | 否 | — | 完整文件 SHA-256；上传完成后核验内容完整性。 |
| `storage_key` | `nvarchar(max)` | 否 | — | 文件在存储提供方中的受控对象键；不可作为用户可写本机路径。 |
| `scan_status` | `nvarchar(max)` | 否 | — | 文件安全扫描状态；只能由实际扫描链更新，不能把 Unknown/Error 当 Clean。 |
| `restricted` | `bit` | 否 | — | 文件是否受限；下载/引用必须执行现有权限及安全状态校验。 |
| `deletion_status` | `nvarchar(max)` | 否 | — | 文件删除阶段；Active/DeletionRequested/Deleted 等按文件生命周期维护。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `deleted_on` | `datetimeoffset(7)` | 是 | — | 软删除或删除确认时刻；未删除为 NULL，具体删除状态由领域规则维护。 |
| `retention_until` | `datetimeoffset(7)` | 是 | — | 保留截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_system_file_object_sha256`：普通 (tenant_n_id,sha256,content_length)。
- `PK__system_f__3213E83F747FF6FB`：主键 (id)。
- `ux_system_file_object_nid`：唯一 (tenant_n_id,file_n_id)。
- `ux_system_file_object_session`：唯一 (tenant_n_id,upload_session_n_id)。

### `system_file_reference_grant`

SystemData 的文件引用授权持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `reference_n_id` | `nvarchar(128)` | 否 | — | 引用业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_n_id` | `nvarchar(128)` | 否 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `owner_user_n_id` | `nvarchar(128)` | 否 | — | 所有者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `purpose` | `nvarchar(128)` | 否 | — | 用途；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `deleted_on` | `datetimeoffset(7)` | 是 | — | 软删除或删除确认时刻；未删除为 NULL，具体删除状态由领域规则维护。 |

索引：
- `ix_system_file_reference_grant_file`：普通 (tenant_n_id,file_n_id,deleted_on)。
- `PK__system_f__3213E83FBE9A47AA`：主键 (id)。
- `ux_system_file_reference_grant_active`：唯一 (tenant_n_id,file_n_id,owner_user_n_id,purpose)。
- `ux_system_file_reference_grant_nid`：唯一 (tenant_n_id,reference_n_id)。

### `system_file_scan_attempt`

SystemData 的文件扫描尝试持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `scan_n_id` | `nvarchar(128)` | 否 | — | 扫描业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_n_id` | `nvarchar(128)` | 否 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `engine` | `nvarchar(max)` | 否 | — | 数据库登记的引擎信息；与目标连接和 Provider 一致，不能据此自动跨库迁移。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `started_on` | `datetimeoffset(7)` | 否 | — | 开始时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `detail` | `nvarchar(max)` | 是 | — | 详情；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `ix_system_file_scan_attempt_file`：普通 (tenant_n_id,file_n_id,started_on)。
- `PK__system_f__3213E83FAB54257A`：主键 (id)。
- `ux_system_file_scan_attempt_nid`：唯一 (tenant_n_id,scan_n_id)。

### `system_file_status_outbox`

SystemData 的文件状态发件箱持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `file_n_id` | `nvarchar(max)` | 否 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `scan_status` | `nvarchar(max)` | 否 | — | 文件安全扫描状态；只能由实际扫描链更新，不能把 Unknown/Error 当 Clean。 |
| `restricted` | `bit` | 否 | — | 文件是否受限；下载/引用必须执行现有权限及安全状态校验。 |
| `deletion_status` | `nvarchar(max)` | 否 | — | 文件删除阶段；Active/DeletionRequested/Deleted 等按文件生命周期维护。 |
| `observed_on` | `datetimeoffset(7)` | 否 | — | 观察时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `next_attempt_on` | `datetimeoffset(7)` | 是 | — | 下一次允许重试时刻；UTC，工作器只能领取到期项。 |
| `retry_count` | `int` | 否 | — | 重试计数；由工作器维护，受现有重试上限约束。 |
| `last_error` | `nvarchar(max)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |
| `dead_lettered_on` | `datetimeoffset(7)` | 是 | — | 失效信时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_system_file_status_outbox_pending`：普通 (published_on,dead_lettered_on,next_attempt_on,observed_on)。
- `PK__system_f__2370F727C158E726`：主键 (event_id)。

### `system_file_upload_session`

SystemData 的文件上传会话持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `session_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `transport_id` | `nvarchar(128)` | 否 | — | 传输标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `uploader_user_n_id` | `nvarchar(128)` | 否 | — | 上传者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `purpose` | `nvarchar(128)` | 否 | — | 用途；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `file_name` | `nvarchar(max)` | 否 | — | 文件名称；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `content_type` | `nvarchar(max)` | 否 | — | 内容类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `expected_length` | `bigint` | 否 | — | 预期完整文件字节数；上传完成时必须与实际长度一致。 |
| `expected_sha256` | `nvarchar(max)` | 是 | — | 上传声明的完整文件哈希；用于最终核验，不信任未完成上传。 |
| `sample_fingerprint` | `nvarchar(128)` | 是 | — | 样本指纹；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `current_offset` | `bigint` | 否 | — | 已确认上传字节偏移；追加需匹配旧偏移和写入代次。 |
| `writer_epoch` | `int` | 否 | — | 上传写入代次；恢复写入时推进，旧代次追加请求拒绝。 |
| `status` | `nvarchar(128)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `file_n_id` | `nvarchar(max)` | 是 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `error_code` | `nvarchar(max)` | 是 | — | 错误编码；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |

索引：
- `ix_system_file_upload_session_candidate`：普通 (tenant_n_id,uploader_user_n_id,purpose,sample_fingerprint,status)。
- `PK__system_f__3213E83FDF05537D`：主键 (id)。
- `ux_system_file_upload_session_nid`：唯一 (tenant_n_id,session_n_id)。
- `ux_system_file_upload_session_transport`：唯一 (tenant_n_id,transport_id)。

### `system_notification_announcement`

SystemData 的通知公告持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `announcement_n_id` | `nvarchar(128)` | 否 | — | 公告业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `title` | `nvarchar(max)` | 否 | — | 标题；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `body` | `nvarchar(max)` | 否 | — | 正文；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `priority` | `int` | 否 | — | 优先级；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(128)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `audience_user_n_ids_json` | `nvarchar(max)` | 否 | — | 接收对象用户业务标识列表JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `expires_on` | `datetimeoffset(7)` | 是 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `created_by_user_n_id` | `nvarchar(max)` | 否 | — | 创建操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `revoked_on` | `datetimeoffset(7)` | 是 | — | 撤销时间;为空表示会话仍有效。 |
| `resource_n_id` | `nvarchar(max)` | 是 | — | 资源业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `target_route` | `nvarchar(max)` | 是 | — | 目标路由；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `idempotency_key` | `nvarchar(max)` | 是 | — | 请求幂等键；在既定租户/用户/操作范围内去重，同键不同请求拒绝。 |

索引：
- `ix_system_notification_announcement_status`：普通 (tenant_n_id,status,published_on)。
- `PK__system_n__3213E83FF17DBDFD`：主键 (id)。
- `ux_system_notification_announcement_nid`：唯一 (tenant_n_id,announcement_n_id)。

### `system_notification_inbox_delivery`

SystemData 的通知收件箱交付持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `notification_n_id` | `nvarchar(128)` | 否 | — | 通知业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `recipient_user_n_id` | `nvarchar(128)` | 否 | — | 接收人用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `delivered_on` | `datetimeoffset(7)` | 否 | — | 投递时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `read_on` | `datetimeoffset(7)` | 是 | — | 已读时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `expires_on` | `datetimeoffset(7)` | 是 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `revoked_on` | `datetimeoffset(7)` | 是 | — | 撤销时间;为空表示会话仍有效。 |

索引：
- `ix_system_notification_delivery_inbox`：普通 (tenant_n_id,recipient_user_n_id,read_on,delivered_on)。
- `PK__system_n__3213E83F75D571AC`：主键 (id)。
- `ux_system_notification_delivery_recipient`：唯一 (tenant_n_id,notification_n_id,recipient_user_n_id)。

### `system_notification_message`

SystemData 的通知消息持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataOperationalTables.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `notification_n_id` | `nvarchar(128)` | 否 | — | 通知业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `announcement_n_id` | `nvarchar(max)` | 是 | — | 公告业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `kind` | `nvarchar(max)` | 否 | — | 种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `title` | `nvarchar(max)` | 否 | — | 标题；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `body` | `nvarchar(max)` | 否 | — | 正文；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `sender_user_n_id` | `nvarchar(max)` | 否 | — | 发送者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `expires_on` | `datetimeoffset(7)` | 是 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `revoked_on` | `datetimeoffset(7)` | 是 | — | 撤销时间;为空表示会话仍有效。 |
| `resource_n_id` | `nvarchar(max)` | 是 | — | 资源业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `target_route` | `nvarchar(max)` | 是 | — | 目标路由；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `idempotency_key` | `nvarchar(max)` | 是 | — | 请求幂等键；在既定租户/用户/操作范围内去重，同键不同请求拒绝。 |

索引：
- `ix_system_notification_message_created`：普通 (tenant_n_id,created_on)。
- `PK__system_n__3213E83FA685F0EA`：主键 (id)。
- `ux_system_notification_message_nid`：唯一 (tenant_n_id,notification_n_id)。

### `system_trusted_service_call_nonce`

SystemData 的可信服务调用一次性随机值持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[SystemDataTrustedServiceCallNonceStore.cs](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Security/SystemDataTrustedServiceCallNonceStore.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `issuer` | `nvarchar(128)` | 否 | — | 受信内部请求的签发服务标识；与 nonce 共同构成防重放唯一键。 |
| `nonce` | `nvarchar(128)` | 否 | — | 一次性随机值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |

索引：
- `ix_system_trusted_service_call_nonce_expiry`：普通 (expires_on)。
- `PK__system_t__6A41D828B14A83CB`：主键 (issuer,nonce)。

## ReferenceData


### `reference_data_cache_generation`

ReferenceData 的缓存代次持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `generation_key` | `nvarchar(64)` | 否 | — | 代次键；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `generation_token` | `nvarchar(64)` | 否 | — | 代次令牌；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |

索引：
- `PK__referenc__74C0CAE6CC38A82C`：主键 (generation_key)。

CHECK：
- `CK__reference__gener__6D2D2E85`：`((len([generation_token]+N'#')-(1))=(64))`。
- `ck_sql_length_cache_generation_generation_key`：`((len([generation_key]+N'#')-(1))=(64))`。

### `reference_data_coding_rule_definition`

ReferenceData 的编码规则定义持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `target_entity_nid` | `nvarchar(64)` | 否 | — | 目标实体业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `template` | `nvarchar(1024)` | 否 | — | 模板；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `reset_policy` | `nvarchar(max)` | 否 | — | 重置策略；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `revision` | `int` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `source_revision` | `int` | 是 | — | 来源修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_by` | `nvarchar(max)` | 是 | — | 发布操作方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `coding_rule_platform_current_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL AND [status]=N'Published' AND [is_deleted]=(0))。
- `coding_rule_platform_draft_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL AND [status]=N'Draft' AND [is_deleted]=(0))。
- `coding_rule_platform_revision_uq`：唯一 (n_id,revision)；过滤 ([tenant_nid] IS NULL AND [is_deleted]=(0))。
- `coding_rule_tenant_current_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL AND [status]=N'Published' AND [is_deleted]=(0))。
- `coding_rule_tenant_draft_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL AND [status]=N'Draft' AND [is_deleted]=(0))。
- `coding_rule_tenant_revision_uq`：唯一 (tenant_nid,n_id,revision)；过滤 ([tenant_nid] IS NOT NULL AND [is_deleted]=(0))。
- `PK__referenc__3213E83FEDD20916`：主键 (id)。
- `UQ__referenc__8D84F31AAA6E4F2A`：唯一 (id,revision)。

CHECK：
- `CK__reference___n_id__278EDA44`：`([n_id]=upper([n_id]))`。
- `CK__reference__reset__2A6B46EF`：`([reset_policy]=N'Daily' OR [reset_policy]=N'Monthly' OR [reset_policy]=N'Yearly' OR [reset_policy]='Never')`。
- `CK__reference__revis__2B5F6B28`：`([revision]>(0))`。
- `CK__reference__sourc__2D47B39A`：`([source_revision] IS NULL OR [source_revision]>(0))`。
- `CK__reference__statu__2C538F61`：`([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published' OR [status]=N'Draft')`。
- `CK__reference__targe__2882FE7D`：`([target_entity_nid]=upper([target_entity_nid]))`。
- `CK__reference_data_c__2E3BD7D3`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。
- `CK__reference_data_c__2F2FFC0C`：`([status]=N'Draft' AND [published_on] IS NULL OR [status]=N'Disabled' OR ([status]=N'Superseded' OR [status]=N'Published') AND [published_on] IS NOT NULL)`。
- `CK__reference_data_c__30242045`：`([published_on] IS NULL AND [published_by] IS NULL OR [published_on] IS NOT NULL AND [published_by] IS NOT NULL)`。
- `CK__reference_data_c__3118447E`：`([source_revision] IS NULL OR [source_revision]<[revision])`。
- `ck_sql_length_coding_rule_definition_template`：`((len([template]+N'#')-(1))>=(1) AND (len([template]+N'#')-(1))<=(1024))`。

### `reference_data_coding_rule_idempotency_record`

ReferenceData 的编码规则幂等记录持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 否 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `rule_nid` | `nvarchar(64)` | 否 | — | 规则业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `idempotency_key_hash` | `nvarchar(64)` | 否 | — | 幂等键哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `request_hash` | `nvarchar(64)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `coding_rule_id` | `uniqueidentifier` | 否 | — | 编码规则标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `rule_revision` | `int` | 否 | — | 规则修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `source_scope` | `nvarchar(max)` | 否 | — | 来源范围；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `source_tenant_nid` | `nvarchar(max)` | 是 | — | 来源租户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `code` | `nvarchar(128)` | 否 | — | 编码；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `sequence_value` | `bigint` | 否 | — | 编码规则当前分配序号；事务锁下递增，禁止应用先读后写。 |
| `period_key` | `nvarchar(8)` | 否 | — | 编码规则重置周期键；依据 reset_policy 和业务时间确定。 |
| `generated_on` | `datetimeoffset(7)` | 否 | — | 生成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `coding_rule_idempotency_expiry_ix`：普通 (expires_on)。
- `PK__referenc__3213E83FC78C3992`：主键 (id)。
- `UQ__referenc__B0AD271A0FA98F67`：唯一 (tenant_nid,rule_nid,idempotency_key_hash)。

外键：
- `FK__reference_data_c__451F3D2B`：(coding_rule_id,rule_revision) → `reference_data_coding_rule_definition` (id,revision)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference__rule___3B95D2F1`：`([rule_nid]=upper([rule_nid]))`。
- `CK__reference__rule___3E723F9C`：`([rule_revision]>(0))`。
- `CK__reference__seque__414EAC47`：`([sequence_value]>(0))`。
- `CK__reference__sourc__3F6663D5`：`([source_scope]=N'Tenant' OR [source_scope]='Platform')`。
- `CK__reference_data_c__4336F4B9`：`([source_scope]='Platform' AND [source_tenant_nid] IS NULL OR [source_scope]=N'Tenant' AND [source_tenant_nid] IS NOT NULL)`。
- `CK__reference_data_c__442B18F2`：`([expires_on]>[generated_on])`。
- `ck_sql_length_coding_rule_idempotency_record_code`：`((len([code]+N'#')-(1))>=(1) AND (len([code]+N'#')-(1))<=(128))`。
- `ck_sql_length_coding_rule_idempotency_record_idempotency_key_hash`：`((len([idempotency_key_hash]+N'#')-(1))=(64))`。
- `ck_sql_length_coding_rule_idempotency_record_period_key`：`((len([period_key]+N'#')-(1))>=(1) AND (len([period_key]+N'#')-(1))<=(8))`。
- `ck_sql_length_coding_rule_idempotency_record_request_hash`：`((len([request_hash]+N'#')-(1))=(64))`。

### `reference_data_coding_rule_sequence`

ReferenceData 的编码规则序号持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `coding_rule_id` | `uniqueidentifier` | 否 | — | 编码规则标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `rule_revision` | `int` | 否 | — | 规则修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `period_key` | `nvarchar(8)` | 否 | — | 编码规则重置周期键；依据 reset_policy 和业务时间确定。 |
| `context_hash` | `nvarchar(64)` | 否 | — | 上下文哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `last_value` | `bigint` | 否 | — | 最近值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |

索引：
- `PK__referenc__8C8D9AFD80AB6C0D`：主键 (coding_rule_id,rule_revision,period_key,context_hash)。

外键：
- `FK__reference_data_c__37C5420D`：(coding_rule_id,rule_revision) → `reference_data_coding_rule_definition` (id,revision)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference__conte__35DCF99B`：`((len([context_hash]+N'#')-(1))=(64))`。
- `CK__reference__last___36D11DD4`：`([last_value]>(0))`。
- `CK__reference__rule___33F4B129`：`([rule_revision]>(0))`。
- `ck_sql_length_coding_rule_sequence_period_key`：`((len([period_key]+N'#')-(1))>=(1) AND (len([period_key]+N'#')-(1))<=(8))`。

### `reference_data_dictionary_definition`

ReferenceData 的字典定义持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `revision` | `int` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_by` | `nvarchar(max)` | 是 | — | 发布操作方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_scope` | `nvarchar(128)` | 否 | 计算：(isnull([tenant_nid],N'')) | 由租户列计算的唯一索引作用域；NULL 租户转为空串，由数据库维护，应用不写。 |

索引：
- `dictionary_current_uq`：唯一 (tenant_scope,n_id)；过滤 ([status]=N'Published' AND [is_deleted]=(0))。
- `dictionary_draft_uq`：唯一 (tenant_scope,n_id)；过滤 ([status]=N'Draft' AND [is_deleted]=(0))。
- `dictionary_scope_revision_uq`：唯一 (tenant_scope,n_id,revision)。
- `PK__referenc__3213E83F0F22818C`：主键 (id)。

CHECK：
- `CK__reference___n_id__26CFC035`：`([n_id]=upper([n_id]))`。
- `CK__reference__revis__27C3E46E`：`([revision]>(0))`。
- `CK__reference__statu__28B808A7`：`([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published' OR [status]=N'Draft')`。
- `CK__reference_data_d__29AC2CE0`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。

### `reference_data_dictionary_item`

ReferenceData 的字典条目持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `dictionary_definition_id` | `uniqueidentifier` | 否 | — | 字典定义标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `PK__referenc__3213E83F423D801B`：主键 (id)。
- `UQ__referenc__839395492A2900C0`：唯一 (dictionary_definition_id,n_id)。

外键：
- `FK__reference__dicti__2D7CBDC4`：(dictionary_definition_id) → `reference_data_dictionary_definition` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__2E70E1FD`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__2F650636`：`([sort]>=(0))`。

### `reference_data_dynamic_property_definition`

ReferenceData 的动态属性定义持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `revision` | `int` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_by` | `nvarchar(max)` | 是 | — | 发布操作方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `dynamic_platform_draft_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL AND [status]=N'Draft')。
- `dynamic_platform_published_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL AND [status]=N'Published')。
- `dynamic_platform_revision_uq`：唯一 (n_id,revision)；过滤 ([tenant_nid] IS NULL)。
- `dynamic_tenant_draft_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL AND [status]=N'Draft')。
- `dynamic_tenant_published_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL AND [status]=N'Published')。
- `dynamic_tenant_revision_uq`：唯一 (tenant_nid,n_id,revision)；过滤 ([tenant_nid] IS NOT NULL)。
- `PK__referenc__3213E83F34FF0C19`：主键 (id)。

CHECK：
- `CK__reference___n_id__4DE98D56`：`([n_id]=upper([n_id]))`。
- `CK__reference__optim__50C5FA01`：`([optimistic_version]>=(0))`。
- `CK__reference__revis__4EDDB18F`：`([revision]>(0))`。
- `CK__reference__statu__4FD1D5C8`：`([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published' OR [status]=N'Draft')`。
- `CK__reference_data_d__51BA1E3A`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。

### `reference_data_dynamic_property_field`

ReferenceData 的动态属性字段持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `definition_id` | `uniqueidentifier` | 否 | — | 定义标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `data_type` | `nvarchar(max)` | 否 | — | 配置字段的数据类型；由定义维护，已有值后的类型变更遵循原模块限制。 |
| `required` | `bit` | 否 | — | 必需；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `default_value_json` | `nvarchar(max)` | 是 | — | 默认值JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `min_length` | `int` | 是 | — | 最小长度；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `max_length` | `int` | 是 | — | 最大长度；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `min_value` | `decimal(28,10)` | 是 | — | 最小值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `max_value` | `decimal(28,10)` | 是 | — | 最大值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `scale` | `int` | 是 | — | 小数位；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `pattern` | `nvarchar(256)` | 是 | — | 格式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `dictionary_nid` | `nvarchar(64)` | 是 | — | 字典业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `reference_target` | `nvarchar(128)` | 是 | — | 引用目标；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `has_had_value` | `bit` | 否 | — | 是否具有曾有值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `was_published` | `bit` | 否 | — | 曾经发布；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `PK__referenc__3213E83F9BECC67C`：主键 (id)。
- `UQ__referenc__4B4C1DD578F1DBC4`：唯一 (id,definition_id)。
- `UQ__referenc__62C840A3AE587AA2`：唯一 (definition_id,n_id)。

外键：
- `FK__reference__defin__567ED357`：(definition_id) → `reference_data_dynamic_property_definition` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__5772F790`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__595B4002`：`([sort]>=(0))`。
- `CK__reference__data___58671BC9`：`([data_type]=N'Reference' OR [data_type]=N'Json' OR [data_type]=N'Enum' OR [data_type]=N'DateTime' OR [data_type]=N'Date' OR [data_type]=N'Boolean' OR [data_type]=N'Decimal' OR [data_type]=N'Integer' OR [data_type]=N'String')`。
- `CK__reference__defau__5A4F643B`：`([default_value_json] IS NULL OR isjson([default_value_json])=(1))`。
- `CK__reference__optim__5C37ACAD`：`([optimistic_version]>=(0))`。
- `CK__reference__scale__5B438874`：`([scale]>=(0) AND [scale]<=(10))`。

### `reference_data_dynamic_property_record`

ReferenceData 的动态属性记录持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `definition_id` | `uniqueidentifier` | 否 | — | 定义标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 是 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `category` | `nvarchar(100)` | 是 | — | 分类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `dynamic_record_page_ix`：普通 (definition_id,sort,n_id)。
- `PK__referenc__3213E83FD86B74CA`：主键 (id)。
- `UQ__referenc__4B4C1DD5057AB3B5`：唯一 (id,definition_id)。
- `UQ__referenc__62C840A3323DD245`：唯一 (definition_id,n_id)。

外键：
- `FK__reference__defin__60FC61CA`：(definition_id) → `reference_data_dynamic_property_definition` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__61F08603`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__62E4AA3C`：`([sort]>=(0))`。
- `CK__reference__optim__63D8CE75`：`([optimistic_version]>=(0))`。

### `reference_data_dynamic_property_value`

ReferenceData 的动态属性值持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `definition_id` | `uniqueidentifier` | 否 | — | 定义标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `record_id` | `uniqueidentifier` | 否 | — | 记录标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `field_id` | `uniqueidentifier` | 否 | — | 字段标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `value_type` | `nvarchar(max)` | 否 | — | 动态值类型判别码；与定义数据类型及实际有值列一致。 |
| `string_value` | `nvarchar(max)` | 是 | — | 动态属性字符串/枚举值；保留空串与 NULL 的差异。 |
| `integer_value` | `bigint` | 是 | — | 动态属性有符号 Int64 值；保持精确整数，不经浮点数转换。 |
| `decimal_value` | `decimal(28,10)` | 是 | — | 动态属性十进制值；按定义精度处理，禁止经 double 转换造成精度丢失。 |
| `boolean_value` | `bit` | 是 | — | 动态属性布尔值；NULL 与 false 区分，非布尔类型不能写此列。 |
| `date_value` | `date` | 是 | — | 动态属性日期值；只含日期，不附加时区或当前时间。 |
| `date_time_value` | `datetimeoffset(7)` | 是 | — | 动态属性时间值；按 UTC 时刻保存和读回，不能丢弃偏移后当本地时间。 |
| `json_value` | `nvarchar(max)` | 是 | — | 动态属性 JSON 类型值列；仅对应 JSON 类型时使用，与其他类型值列互斥。 |
| `reference_value` | `nvarchar(128)` | 是 | — | 动态属性引用业务标识；按 reference_target 校验引用目标。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `dynamic_value_definition_ix`：普通 (definition_id)。
- `PK__referenc__3213E83FF99DCE2D`：主键 (id)。
- `UQ__referenc__4E74DB9FF4F6FC1E`：唯一 (record_id,field_id)。

外键：
- `FK__reference__defin__67A95F59`：(definition_id) → `reference_data_dynamic_property_definition` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__reference__field__6991A7CB`：(field_id) → `reference_data_dynamic_property_field` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__reference__recor__689D8392`：(record_id) → `reference_data_dynamic_property_record` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__reference_data_d__6D6238AF`：(record_id,definition_id) → `reference_data_dynamic_property_record` (id,definition_id)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__reference_data_d__6E565CE8`：(field_id,definition_id) → `reference_data_dynamic_property_field` (id,definition_id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference__json___6A85CC04`：`([json_value] IS NULL OR isjson([json_value])=(1))`。
- `CK__reference__optim__6B79F03D`：`([optimistic_version]>=(0))`。
- `CK__reference_data_d__6C6E1476`：`(([value_type]=N'Enum' OR [value_type]=N'String') AND [string_value] IS NOT NULL AND [integer_value] IS NULL AND [decimal_value] IS NULL AND [boolean_value] IS NULL AND [date_value] IS NULL AND [date_time_value] IS NULL AND [json_value] IS NULL AND [reference_value] IS NULL OR [value_type]=N'Integer' AND [string_value] IS NULL AND [integer_value] IS NOT NULL AND [decimal_value] IS NULL AND [boolean_value] IS NULL AND [date_value] IS NULL AND [date_time_value] IS NULL AND [json_value] IS NULL AND [reference_value] IS NULL OR [value_type]=N'Decimal' AND [string_value] IS NULL AND [integer_value] IS NULL AND [decimal_value] IS NOT NULL AND [boolean_value] IS NULL AND [date_value] IS NULL AND [date_time_value] IS NULL AND [json_value] IS NULL AND [reference_value] IS NULL OR [value_type]=N'Boolean' AND [string_value] IS NULL AND [integer_value] IS NULL AND [decimal_value] IS NULL AND [boolean_value] IS NOT NULL AND [date_value] IS NULL AND [date_time_value] IS NULL AND [json_value] IS NULL AND [reference_value] IS NULL OR [value_type]=N'Date' AND [string_value] IS NULL AND [integer_value] IS NULL AND [decimal_value] IS NULL AND [boolean_value] IS NULL AND [date_value] IS NOT NULL AND [date_time_value] IS NULL AND [json_value] IS NULL AND [reference_value] IS NULL OR [value_type]=N'DateTime' AND [string_value] IS NULL AND [integer_value] IS NULL AND [decimal_value] IS NULL AND [boolean_value] IS NULL AND [date_value] IS NULL AND [date_time_value] IS NOT NULL AND [json_value] IS NULL AND [reference_value] IS NULL OR [value_type]=N'Json' AND [string_value] IS NULL AND [integer_value] IS NULL AND [decimal_value] IS NULL AND [boolean_value] IS NULL AND [date_value] IS NULL AND [date_time_value] IS NULL AND [json_value] IS NOT NULL AND [reference_value] IS NULL OR [value_type]=N'Reference' AND [string_value] IS NULL AND [integer_value] IS NULL AND [decimal_value] IS NULL AND [boolean_value] IS NULL AND [date_value] IS NULL AND [date_time_value] IS NULL AND [json_value] IS NULL AND [reference_value] IS NOT NULL)`。

### `reference_data_metadata_attribute_definition`

ReferenceData 的元数据属性定义持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `entity_schema_id` | `uniqueidentifier` | 否 | — | 实体结构标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `data_type` | `nvarchar(max)` | 否 | — | 配置字段的数据类型；由定义维护，已有值后的类型变更遵循原模块限制。 |
| `required` | `bit` | 否 | — | 必需；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_array` | `bit` | 否 | — | 元数据属性是否集合；由定义维护，决定数据形态。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `default_value` | `nvarchar(max)` | 是 | — | 默认值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `min_length` | `int` | 是 | — | 最小长度；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `max_length` | `int` | 是 | — | 最大长度；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `min_value` | `decimal(28,12)` | 是 | — | 最小值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `max_value` | `decimal(28,12)` | 是 | — | 最大值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `pattern` | `nvarchar(max)` | 是 | — | 格式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `dictionary_nid` | `nvarchar(64)` | 是 | — | 字典业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `reference_target` | `nvarchar(64)` | 是 | — | 引用目标；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `precision_value` | `int` | 是 | — | 精度值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `scale_value` | `int` | 是 | — | 小数位值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `unit_dimension_nid` | `nvarchar(64)` | 是 | — | 单位维度业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `default_unit_nid` | `nvarchar(64)` | 是 | — | 默认单位业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `unit_revision` | `int` | 是 | — | 单位修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `unit_source_scope` | `nvarchar(max)` | 是 | — | 单位来源范围；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `unit_source_tenant_nid` | `nvarchar(max)` | 是 | — | 单位来源租户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `was_published` | `bit` | 否 | — | 曾经发布；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `PK__referenc__3213E83F3550F26F`：主键 (id)。
- `UQ__referenc__05FE6CCAD77A4AD0`：唯一 (entity_schema_id,n_id)。

外键：
- `FK__reference__entit__0DCF0841`：(entity_schema_id) → `reference_data_metadata_entity_schema` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__0EC32C7A`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__10AB74EC`：`([sort]>=(0))`。
- `CK__reference__data___0FB750B3`：`([data_type]=N'Reference' OR [data_type]=N'Enum' OR [data_type]=N'DateTime' OR [data_type]=N'Date' OR [data_type]=N'Boolean' OR [data_type]=N'Decimal' OR [data_type]=N'Integer' OR [data_type]=N'String')`。
- `CK__reference__defau__1E05700A`：`([default_unit_nid] IS NULL OR [default_unit_nid]=upper([default_unit_nid]))`。
- `CK__reference__dicti__1B29035F`：`([dictionary_nid] IS NULL OR [dictionary_nid]=upper([dictionary_nid]))`。
- `CK__reference__max_l__1293BD5E`：`([max_length] IS NULL OR [max_length]>=(0))`。
- `CK__reference__min_l__119F9925`：`([min_length] IS NULL OR [min_length]>=(0))`。
- `CK__reference__preci__147C05D0`：`([precision_value] IS NULL OR [precision_value]>=(1) AND [precision_value]<=(28))`。
- `CK__reference__refer__1C1D2798`：`([reference_target] IS NULL OR [reference_target]=upper([reference_target]))`。
- `CK__reference__scale__15702A09`：`([scale_value] IS NULL OR [scale_value]>=(0) AND [scale_value]<=(12))`。
- `CK__reference__unit___16644E42`：`([unit_revision] IS NULL OR [unit_revision]>(0))`。
- `CK__reference__unit___1758727B`：`([unit_source_scope] IS NULL OR ([unit_source_scope]=N'Tenant' OR [unit_source_scope]='Platform'))`。
- `CK__reference__unit___1D114BD1`：`([unit_dimension_nid] IS NULL OR [unit_dimension_nid]=upper([unit_dimension_nid]))`。
- `CK__reference_data_m__184C96B4`：`([min_length] IS NULL OR [max_length] IS NULL OR [min_length]<=[max_length])`。
- `CK__reference_data_m__1940BAED`：`([min_value] IS NULL OR [max_value] IS NULL OR [min_value]<=[max_value])`。
- `CK__reference_data_m__1A34DF26`：`([precision_value] IS NULL OR [scale_value] IS NULL OR [scale_value]<=[precision_value])`。
- `CK__reference_data_m__1EF99443`：`([data_type]=N'String' OR [min_length] IS NULL AND [max_length] IS NULL AND [pattern] IS NULL)`。
- `CK__reference_data_m__1FEDB87C`：`([data_type]=N'Decimal' OR [data_type]=N'Integer' OR [min_value] IS NULL AND [max_value] IS NULL)`。
- `CK__reference_data_m__20E1DCB5`：`([data_type]=N'Decimal' OR [precision_value] IS NULL AND [scale_value] IS NULL AND [unit_dimension_nid] IS NULL AND [default_unit_nid] IS NULL AND [unit_revision] IS NULL AND [unit_source_scope] IS NULL AND [unit_source_tenant_nid] IS NULL)`。
- `CK__reference_data_m__21D600EE`：`([data_type]=N'Enum' AND [dictionary_nid] IS NOT NULL OR [data_type]<>N'Enum' AND [dictionary_nid] IS NULL)`。
- `CK__reference_data_m__22CA2527`：`([data_type]=N'Reference' AND [reference_target] IS NOT NULL OR [data_type]<>N'Reference' AND [reference_target] IS NULL)`。
- `CK__reference_data_m__23BE4960`：`([unit_dimension_nid] IS NULL AND [default_unit_nid] IS NULL AND [unit_revision] IS NULL AND [unit_source_scope] IS NULL AND [unit_source_tenant_nid] IS NULL OR [data_type]=N'Decimal' AND [unit_dimension_nid] IS NOT NULL AND [unit_revision] IS NOT NULL AND ([unit_source_scope]=N'Platform' AND [unit_source_tenant_nid] IS NULL OR [unit_source_scope]=N'Tenant' AND [unit_source_tenant_nid] IS NOT NULL))`。
- `ck_sql_length_metadata_attribute_definition_pattern`：`([pattern] IS NULL OR (len([pattern]+N'#')-(1))<=(256))`。

### `reference_data_metadata_entity_schema`

ReferenceData 的元数据实体结构持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `revision` | `int` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `source_revision` | `int` | 是 | — | 来源修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_by` | `nvarchar(max)` | 是 | — | 发布操作方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_scope` | `nvarchar(128)` | 否 | 计算：(isnull([tenant_nid],N'')) | 由租户列计算的唯一索引作用域；NULL 租户转为空串，由数据库维护，应用不写。 |

索引：
- `metadata_current_uq`：唯一 (tenant_scope,n_id)；过滤 ([status]=N'Published' AND [is_deleted]=(0))。
- `metadata_draft_uq`：唯一 (tenant_scope,n_id)；过滤 ([status]=N'Draft' AND [is_deleted]=(0))。
- `metadata_scope_revision_uq`：唯一 (tenant_scope,n_id,revision)。
- `PK__referenc__3213E83F6F9AEA8D`：主键 (id)。

CHECK：
- `CK__reference___n_id__04459E07`：`([n_id]=upper([n_id]))`。
- `CK__reference__revis__0539C240`：`([revision]>(0))`。
- `CK__reference__sourc__07220AB2`：`([source_revision] IS NULL OR [source_revision]>(0))`。
- `CK__reference__statu__062DE679`：`([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published' OR [status]=N'Draft')`。
- `CK__reference_data_m__08162EEB`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。
- `CK__reference_data_m__090A5324`：`([source_revision] IS NULL OR [source_revision]<[revision])`。
- `CK__reference_data_m__09FE775D`：`([status]=N'Draft' AND [published_on] IS NULL OR ([status]=N'Superseded' OR [status]=N'Published') AND [published_on] IS NOT NULL OR [status]=N'Disabled')`。

### `reference_data_outbox_message`

ReferenceData 的发件箱消息持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `module_key` | `nvarchar(32)` | 否 | — | 所属模块的稳定键；由模块目录或产物指定，用于账本及路由范围隔离。 |
| `event_name` | `nvarchar(96)` | 否 | — | 事件路由名称；由已登记事件契约确定。 |
| `aggregate_id` | `uniqueidentifier` | 否 | — | 聚合标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `revision` | `bigint` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `payload` | `nvarchar(max)` | 否 | — | 序列化事件内容；Outbox/Inbox 按对应事件契约读写，数据库只校验 JSON 结构，不替代业务校验。 |
| `status` | `nvarchar(16)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `attempt_count` | `int` | 否 | — | 执行或投递尝试次数；重试后递增，不能当作成功次数。 |
| `next_attempt_on` | `datetimeoffset(7)` | 是 | — | 下一次允许重试时刻；UTC，工作器只能领取到期项。 |
| `last_error` | `nvarchar(500)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |
| `created_time` | `datetimeoffset(7)` | 否 | — | 消息或事件创建时刻；由生产方记录，SQL Server 保存带偏移时间，应用按 UTC 使用。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `PK__referenc__2370F727BE9BB1B7`：主键 (event_id)。
- `reference_data_outbox_module_ix`：普通 (module_key,status)。
- `reference_data_outbox_pending_ix`：普通 (status,next_attempt_on,created_time)。

CHECK：
- `CK__reference__attem__6774552F`：`([attempt_count]>=(0) AND [attempt_count]<=(10))`。
- `CK__reference__modul__63A3C44B`：`([module_key]=N'unit-of-measure' OR [module_key]=N'state-machine' OR [module_key]=N'coding-rule' OR [module_key]=N'metadata' OR [module_key]=N'dynamic-property' OR [module_key]=N'parameter' OR [module_key]=N'dictionary')`。
- `CK__reference__paylo__658C0CBD`：`([payload] IS NULL OR isjson([payload])=(1))`。
- `CK__reference__revis__6497E884`：`([revision]>(0))`。
- `CK__reference__statu__668030F6`：`([status]=N'Failed' OR [status]=N'Published' OR [status]=N'Pending')`。
- `CK__reference_data_o__68687968`：`([status]=N'Published' AND [published_on] IS NOT NULL OR ([status]=N'Failed' OR [status]=N'Pending') AND [published_on] IS NULL)`。
- `CK__reference_data_o__695C9DA1`：`([status]<>N'Failed' OR [attempt_count]=(10) AND [next_attempt_on] IS NULL)`。

### `reference_data_parameter_app_domain`

ReferenceData 的参数应用域持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `revision` | `bigint` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `parameter_platform_nid_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL)。
- `parameter_tenant_nid_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL)。
- `PK__referenc__3213E83F2EBBD6BA`：主键 (id)。

CHECK：
- `CK__reference___n_id__324172E1`：`([n_id]=upper([n_id]))`。
- `CK__reference__revis__3335971A`：`([revision]>(0))`。
- `CK__reference__statu__3429BB53`：`([status]=N'Disabled' OR [status]=N'Active')`。
- `CK__reference_data_p__351DDF8C`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。

### `reference_data_parameter_history`

ReferenceData 的参数历史持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `app_domain_id` | `uniqueidentifier` | 否 | — | 应用域标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `key_id` | `uniqueidentifier` | 是 | — | JWT Header kid(密钥标识)。 |
| `object_type` | `nvarchar(max)` | 否 | — | 对象类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `object_id` | `uniqueidentifier` | 否 | — | 对象标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `full_nid` | `nvarchar(max)` | 否 | — | 完整业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `change_type` | `nvarchar(max)` | 否 | — | 变更类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `change_reason` | `nvarchar(500)` | 否 | — | 变更原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `before_summary` | `nvarchar(max)` | 否 | — | 变更前摘要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `after_summary` | `nvarchar(max)` | 否 | — | 变更后摘要；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `revision` | `bigint` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `user_nid` | `nvarchar(max)` | 否 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `trace_id` | `nvarchar(max)` | 否 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |

索引：
- `parameter_history_path_ix`：普通 (app_domain_id,key_id,revision)。
- `PK__referenc__3213E83F1166BD84`：主键 (id)。

外键：
- `FK__reference__app_d__4B0D20AB`：(app_domain_id) → `reference_data_parameter_app_domain` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

### `reference_data_parameter_key`

ReferenceData 的参数键持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `configuration_app_domain_id` | `uniqueidentifier` | 否 | — | 配置应用域标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `data_type` | `nvarchar(max)` | 否 | — | 配置字段的数据类型；由定义维护，已有值后的类型变更遵循原模块限制。 |
| `value_mode` | `nvarchar(max)` | 否 | — | 值模式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `value_json` | `nvarchar(max)` | 是 | — | 值JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `default_value_json` | `nvarchar(max)` | 是 | — | 默认值JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `is_mandatory` | `bit` | 否 | — | 是否必填；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_read_only` | `bit` | 否 | — | 是否已读只读；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `has_had_value` | `bit` | 否 | — | 是否具有曾有值；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `dictionary_nid` | `nvarchar(64)` | 是 | — | 字典业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `reference_target` | `nvarchar(128)` | 是 | — | 引用目标；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `PK__referenc__3213E83FD5814326`：主键 (id)。
- `UQ__referenc__FEB7CA918F36927D`：唯一 (configuration_app_domain_id,n_id)。

外键：
- `FK__reference__confi__38EE7070`：(configuration_app_domain_id) → `reference_data_parameter_app_domain` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__39E294A9`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__3F9B6DFF`：`([sort]>=(0))`。
- `CK__reference__data___3AD6B8E2`：`([data_type]=N'Reference' OR [data_type]=N'Json' OR [data_type]=N'Enum' OR [data_type]=N'DateTime' OR [data_type]=N'Date' OR [data_type]=N'Boolean' OR [data_type]=N'Decimal' OR [data_type]=N'Integer' OR [data_type]=N'String')`。
- `CK__reference__defau__3DB3258D`：`([default_value_json] IS NULL OR isjson([default_value_json])=(1))`。
- `CK__reference__statu__3EA749C6`：`([status]=N'Disabled' OR [status]=N'Active')`。
- `CK__reference__value__3BCADD1B`：`([value_mode]=N'Multi' OR [value_mode]=N'Single')`。
- `CK__reference__value__3CBF0154`：`([value_json] IS NULL OR isjson([value_json])=(1))`。
- `CK__reference_data_p__408F9238`：`([value_mode]=N'Single' OR [value_json] IS NULL AND [default_value_json] IS NULL)`。

### `reference_data_parameter_multi_value`

ReferenceData 的参数多值值持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `configuration_key_id` | `uniqueidentifier` | 否 | — | 配置键标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 是 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `value_json` | `nvarchar(max)` | 否 | — | 值JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `canonical_value_hash` | `nvarchar(64)` | 否 | — | 规范化配置值哈希；用于配置去重及相同值比较。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `is_default` | `bit` | 否 | — | 是否默认；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `parameter_enabled_value_uq`：唯一 (configuration_key_id,canonical_value_hash)；过滤 ([enabled]=(1))。
- `PK__referenc__3213E83F4AE06BDD`：主键 (id)。
- `UQ__referenc__3C275F458535F18B`：唯一 (configuration_key_id,n_id)。

外键：
- `FK__reference__confi__4460231C`：(configuration_key_id) → `reference_data_parameter_key` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__45544755`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__473C8FC7`：`([sort]>=(0))`。
- `CK__reference__value__46486B8E`：`([value_json] IS NULL OR isjson([value_json])=(1))`。
- `CK__reference_data_p__4830B400`：`([is_default]=(0) OR [enabled]=(1))`。

### `reference_data_schema_migrations`

ReferenceData 不可变迁移执行账本；只登记已成功提交的步骤，禁止手工删记录或改 checksum。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `migration_id` | `nvarchar(128)` | 否 | — | 不可变迁移步骤标识；执行成功后记账，已应用步骤不得原地改 SQL 或删除记录。 |
| `checksum` | `varchar(64)` | 是 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `target_identity` | `varchar(64)` | 是 | — | 目标身份；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `applied_on` | `datetimeoffset(7)` | 否 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |

索引：
- `PK__referenc__DE86177DD5A055C7`：主键 (migration_id)。

### `reference_data_seed_ledger`

ReferenceData 种子应用账本；记录版本、作用域与提交事实，不保存秘密值。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `seed_key` | `nvarchar(128)` | 否 | — | 种子逻辑键；与模块、版本组成幂等身份。 |
| `seed_version` | `nvarchar(128)` | 否 | — | 种子版本；升级追加新版本，不覆盖已应用事实。 |
| `checksum` | `varchar(64)` | 否 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `scope` | `nvarchar(32)` | 是 | — | 作用域编码；种子表对应 SeedScope，其余表按所属领域作用域使用，不能跨对象套枚举。 |
| `applied_on` | `datetimeoffset(7)` | 否 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |
| `operation_n_id` | `nvarchar(128)` | 否 | — | 操作业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `trace_id` | `nvarchar(128)` | 否 | — | 调用链跟踪标识；用于定位操作，不保存凭据或业务正文。 |

索引：
- `PK__referenc__9CFA6DDD1FAB2459`：主键 (seed_key,seed_version)。

### `reference_data_state_machine_definition`

ReferenceData 的状态状态机定义持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `revision` | `int` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `source_revision` | `int` | 是 | — | 来源修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_by` | `nvarchar(max)` | 是 | — | 发布操作方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_scope` | `nvarchar(128)` | 否 | 计算：(isnull([tenant_nid],N'')) | 由租户列计算的唯一索引作用域；NULL 租户转为空串，由数据库维护，应用不写。 |

索引：
- `PK__referenc__3213E83F9B6E13F7`：主键 (id)。
- `state_machine_current_uq`：唯一 (tenant_scope,n_id)；过滤 ([status]=N'Published' AND [is_deleted]=(0))。
- `state_machine_draft_uq`：唯一 (tenant_scope,n_id)；过滤 ([status]=N'Draft' AND [is_deleted]=(0))。
- `state_machine_scope_revision_uq`：唯一 (tenant_scope,n_id,revision)。
- `UQ__referenc__8D84F31A17B153F0`：唯一 (id,revision)。

CHECK：
- `CK__reference___n_id__48EFCE0F`：`([n_id]=upper([n_id]))`。
- `CK__reference__revis__49E3F248`：`([revision]>(0))`。
- `CK__reference__sourc__4BCC3ABA`：`([source_revision] IS NULL OR [source_revision]>(0))`。
- `CK__reference__statu__4AD81681`：`([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published' OR [status]=N'Draft')`。
- `CK__reference_data_s__4CC05EF3`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。
- `CK__reference_data_s__4DB4832C`：`([source_revision] IS NULL OR [source_revision]<[revision])`。
- `CK__reference_data_s__4EA8A765`：`([status]=N'Draft' AND [published_on] IS NULL OR ([status]=N'Superseded' OR [status]=N'Published') AND [published_on] IS NOT NULL OR [status]=N'Disabled')`。

### `reference_data_state_machine_node`

ReferenceData 的状态状态机节点持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `state_machine_definition_id` | `uniqueidentifier` | 否 | — | 状态状态机定义标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `definition_revision` | `int` | 否 | — | 记录所属定义发布修订；读运行数据必须匹配该修订。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `is_initial` | `bit` | 否 | — | 是否初始；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_terminal` | `bit` | 否 | — | 是否终态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `outcome` | `nvarchar(max)` | 否 | — | 结果；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `color` | `nvarchar(7)` | 是 | — | 颜色；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |

索引：
- `PK__referenc__3213E83FA50D82B0`：主键 (id)。
- `UQ__referenc__1953E0FE353D925C`：唯一 (state_machine_definition_id,definition_revision,n_id)。

外键：
- `FK__reference_data_s__573DED66`：(state_machine_definition_id,definition_revision) → `reference_data_state_machine_definition` (id,revision)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__536D5C82`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__5649C92D`：`([sort]>=(0))`。
- `CK__reference__color__5555A4F4`：`([color] IS NULL OR (len([color]+N'#')-(1))=(7) AND [color] like N'#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')`。
- `CK__reference__defin__52793849`：`([definition_revision]>(0))`。
- `CK__reference__outco__546180BB`：`([outcome]=N'Skipped' OR [outcome]=N'Failure' OR [outcome]=N'Success' OR [outcome]=N'None')`。

### `reference_data_state_machine_transition`

ReferenceData 的状态状态机转换持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `state_machine_definition_id` | `uniqueidentifier` | 否 | — | 状态状态机定义标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `definition_revision` | `int` | 否 | — | 记录所属定义发布修订；读运行数据必须匹配该修订。 |
| `from_status_nid` | `nvarchar(64)` | 否 | — | 起始状态业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action_nid` | `nvarchar(64)` | 否 | — | 动作业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action_name` | `nvarchar(200)` | 否 | — | 动作名称；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `to_status_nid` | `nvarchar(64)` | 否 | — | 目标状态业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |

索引：
- `PK__referenc__3213E83F2918C84F`：主键 (id)。
- `UQ__referenc__75863AEB904C6F6B`：唯一 (state_machine_definition_id,definition_revision,from_status_nid,action_nid)。

外键：
- `FK__reference_data_s__5EDF0F2E`：(state_machine_definition_id,definition_revision) → `reference_data_state_machine_definition` (id,revision)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__reference_data_s__5FD33367`：(state_machine_definition_id,definition_revision,from_status_nid) → `reference_data_state_machine_node` (state_machine_definition_id,definition_revision,n_id)；UPDATE NO_ACTION，DELETE NO_ACTION。
- `FK__reference_data_s__60C757A0`：(state_machine_definition_id,definition_revision,to_status_nid) → `reference_data_state_machine_node` (state_machine_definition_id,definition_revision,n_id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference__actio__5CF6C6BC`：`([action_nid]=upper([action_nid]))`。
- `CK__reference__defin__5B0E7E4A`：`([definition_revision]>(0))`。
- `CK__reference__from___5C02A283`：`([from_status_nid]=upper([from_status_nid]))`。
- `CK__reference__to_st__5DEAEAF5`：`([to_status_nid]=upper([to_status_nid]))`。

### `reference_data_unit_of_measure_dimension`

ReferenceData 的单位所属计量维度持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_nid` | `nvarchar(128)` | 是 | — | 所属租户业务标识；平台级定义允许 NULL，租户级定义必须指定租户。 |
| `scope_type` | `nvarchar(max)` | 否 | — | 定义适用层级，按 ReferenceScopeType 的 Platform/Tenant/Factory 规则校验；当前工厂级能力仍受原契约限制。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `description` | `nvarchar(max)` | 是 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `revision` | `int` | 否 | — | 业务发布或快照修订号；不同于并发版本，由该领域的发布/修订操作推进。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `source_revision` | `int` | 是 | — | 来源修订；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `published_by` | `nvarchar(max)` | 是 | — | 发布操作方；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `is_system_defined` | `bit` | 否 | — | 是否系统定义；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `conversion_kind` | `nvarchar(max)` | 否 | — | 换算种类；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `base_unit_nid` | `nvarchar(64)` | 否 | — | 基准单位业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `PK__referenc__3213E83F139E0421`：主键 (id)。
- `uom_platform_current_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL AND [status]=N'Published' AND [is_deleted]=(0))。
- `uom_platform_draft_uq`：唯一 (n_id)；过滤 ([tenant_nid] IS NULL AND [status]=N'Draft' AND [is_deleted]=(0))。
- `uom_platform_revision_uq`：唯一 (n_id,revision)；过滤 ([tenant_nid] IS NULL AND [is_deleted]=(0))。
- `uom_tenant_current_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL AND [status]=N'Published' AND [is_deleted]=(0))。
- `uom_tenant_draft_uq`：唯一 (tenant_nid,n_id)；过滤 ([tenant_nid] IS NOT NULL AND [status]=N'Draft' AND [is_deleted]=(0))。
- `uom_tenant_revision_uq`：唯一 (tenant_nid,n_id,revision)；过滤 ([tenant_nid] IS NOT NULL AND [is_deleted]=(0))。

CHECK：
- `CK__reference___n_id__7132C993`：`([n_id]=upper([n_id]))`。
- `CK__reference__base___75F77EB0`：`([base_unit_nid]=upper([base_unit_nid]))`。
- `CK__reference__conve__75035A77`：`([conversion_kind]=N'AbsoluteTemperature' OR [conversion_kind]=N'Ratio')`。
- `CK__reference__revis__7226EDCC`：`([revision]>(0))`。
- `CK__reference__sourc__740F363E`：`([source_revision] IS NULL OR [source_revision]>(0))`。
- `CK__reference__statu__731B1205`：`([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published' OR [status]=N'Draft')`。
- `CK__reference_data_u__76EBA2E9`：`([scope_type]=N'Platform' AND [tenant_nid] IS NULL OR [scope_type]=N'Tenant' AND [tenant_nid] IS NOT NULL)`。
- `CK__reference_data_u__77DFC722`：`([status]=N'Draft' AND [published_on] IS NULL OR [status]=N'Disabled' OR ([status]=N'Superseded' OR [status]=N'Published') AND [published_on] IS NOT NULL)`。
- `CK__reference_data_u__78D3EB5B`：`([is_system_defined]=(0) OR [scope_type]=N'Platform' AND ([status]=N'Disabled' OR [status]=N'Superseded' OR [status]=N'Published'))`。

### `reference_data_unit_of_measure_unit`

ReferenceData 的单位所属计量单位持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：ReferenceData 对应模块的 *Row/Repository；表名由 ReferenceDataSqlNames 统一解析。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `unit_dimension_id` | `uniqueidentifier` | 否 | — | 单位维度标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `n_id` | `nvarchar(64)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `name` | `nvarchar(200)` | 否 | — | 对象的可读名称；由管理操作填写，供页面显示，不作为唯一身份。 |
| `symbol` | `nvarchar(64)` | 否 | — | 符号；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `factor_to_base` | `decimal(28,12)` | 否 | — | 换算到基准单位的乘数；按精确十进制文本读取，不能用浮点近似。 |
| `offset_to_base` | `decimal(28,12)` | 否 | — | 换算到基准单位的偏移量；按精确十进制文本处理。 |
| `decimal_places` | `int` | 否 | — | 十进制位数；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `rounding_mode` | `nvarchar(max)` | 否 | — | 舍入模式；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `sort` | `int` | 否 | — | 业务展示排序值；由配置操作维护，不表示事件先后顺序。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |

索引：
- `PK__referenc__3213E83FB3DA3649`：主键 (id)。
- `UQ__referenc__11DF5E79AD58B896`：唯一 (unit_dimension_id,n_id)。

外键：
- `FK__reference__unit___7CA47C3F`：(unit_dimension_id) → `reference_data_unit_of_measure_dimension` (id)；UPDATE NO_ACTION，DELETE NO_ACTION。

CHECK：
- `CK__reference___n_id__7D98A078`：`([n_id]=upper([n_id]))`。
- `CK__reference___sort__0169315C`：`([sort]>=(0))`。
- `CK__reference__decim__7F80E8EA`：`([decimal_places]>=(0) AND [decimal_places]<=(12))`。
- `CK__reference__facto__7E8CC4B1`：`([factor_to_base]>(0))`。
- `CK__reference__round__00750D23`：`([rounding_mode]=N'AwayFromZero' OR [rounding_mode]=N'ToEven')`。

## Collaboration


### `collaboration_chat_attachment`

Collaboration 的聊天附件持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `conversation_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `attachment_n_id` | `nvarchar(128)` | 否 | — | 附件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `uploader_user_n_id` | `nvarchar(128)` | 否 | — | 上传者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_n_id` | `nvarchar(128)` | 是 | — | 文件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `file_name_snapshot` | `nvarchar(max)` | 否 | — | 文件名称快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `content_type_snapshot` | `nvarchar(max)` | 否 | — | 内容类型快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `size_snapshot` | `bigint` | 否 | — | 大小快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `purpose` | `nvarchar(max)` | 否 | — | 用途；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `file_state_projection` | `nvarchar(max)` | 否 | — | 文件状态投影；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `file_state_version` | `int` | 是 | — | 文件服务状态版本；附件投影只接收更新版本，拒绝旧事件覆盖。 |
| `file_observed_on` | `datetimeoffset(7)` | 是 | — | 文件观察时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `reference_state` | `nvarchar(max)` | 否 | — | 引用状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `retention_state` | `nvarchar(max)` | 否 | — | 保留状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `bound_message_n_id` | `nvarchar(max)` | 是 | — | 绑定消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `intent_request_n_id` | `nvarchar(128)` | 否 | — | 意图请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `intent_request_hash` | `nvarchar(max)` | 否 | — | 意图请求哈希；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `reference_n_id` | `nvarchar(max)` | 是 | — | 引用业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |

索引：
- `ix_collaboration_attachment_file`：普通 (tenant_n_id,file_n_id)。
- `PK__collabor__3213E83F4EEC3FEE`：主键 (id)。
- `uq_collaboration_attachment_intent`：唯一 (tenant_n_id,uploader_user_n_id,intent_request_n_id)。
- `uq_collaboration_attachment_nid`：唯一 (tenant_n_id,conversation_n_id,attachment_n_id)。

### `collaboration_compliance_command`

Collaboration 的合规命令持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(128)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_n_id` | `nvarchar(128)` | 否 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action` | `nvarchar(max)` | 否 | — | 动作；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `target_n_id` | `nvarchar(max)` | 是 | — | 目标业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `scope_checksum` | `nvarchar(max)` | 否 | — | 范围校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `command_json` | `nvarchar(max)` | 否 | — | 命令JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `result_json` | `nvarchar(max)` | 是 | — | 结果JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `pk_collaboration_compliance_command`：主键 (tenant_n_id,actor_user_n_id,request_n_id)。

### `collaboration_compliance_disposition`

Collaboration 的合规处置持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `disposition_n_id` | `nvarchar(128)` | 否 | — | 处置业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `subject_type` | `nvarchar(128)` | 否 | — | 主体类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `subject_n_id` | `nvarchar(128)` | 否 | — | 事件主体业务标识,按事件语义固定为 UserNId 或 RoleNId。 |
| `state` | `nvarchar(128)` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `reason` | `nvarchar(max)` | 否 | — | 原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_by_user_n_id` | `nvarchar(128)` | 否 | — | 创建操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `expires_on` | `datetimeoffset(7)` | 是 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `request_n_id` | `nvarchar(128)` | 是 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 是 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |

索引：
- `ix_collaboration_disposition_subject`：普通 (tenant_n_id,subject_type,subject_n_id,state)。
- `PK__collabor__3213E83FDA6A8673`：主键 (id)。
- `uq_collaboration_disposition`：唯一 (tenant_n_id,disposition_n_id)。
- `uq_collaboration_disposition_request`：唯一 (tenant_n_id,created_by_user_n_id,request_n_id)。

### `collaboration_compliance_export`

Collaboration 的合规导出持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `export_n_id` | `nvarchar(128)` | 否 | — | 导出业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `state` | `nvarchar(128)` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `scope_json` | `nvarchar(max)` | 否 | — | 范围JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `scope_checksum` | `nvarchar(max)` | 否 | — | 范围校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `reason` | `nvarchar(max)` | 否 | — | 原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_by_user_n_id` | `nvarchar(128)` | 否 | — | 创建操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `expires_on` | `datetimeoffset(7)` | 是 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `artifact_reference` | `nvarchar(max)` | 是 | — | 产物引用；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `request_n_id` | `nvarchar(128)` | 是 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 是 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `case_reference` | `nvarchar(max)` | 是 | — | 保全案件引用；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `approved_by_user_n_id` | `nvarchar(max)` | 是 | — | 审批人业务标识。 |
| `approved_on` | `datetimeoffset(7)` | 是 | — | 审批时间。 |
| `approval_expires_on` | `datetimeoffset(7)` | 是 | — | 审批过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `approval_consumed_on` | `datetimeoffset(7)` | 是 | — | 审批已消费时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `run_deadline_on` | `datetimeoffset(7)` | 是 | — | 运行截止时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `completed_on` | `datetimeoffset(7)` | 是 | — | 完成时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `error_code` | `nvarchar(max)` | 是 | — | 错误编码；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `fields_json` | `nvarchar(max)` | 是 | — | 字段JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `operation_id` | `nvarchar(128)` | 是 | — | 操作标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `export_retention_hours` | `int` | 否 | ((24)) | 导出保留小时；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `worker_lease_n_id` | `nvarchar(max)` | 是 | — | 一次工作器领取令牌；防止旧工作器提交新租约结果。 |
| `worker_lease_until` | `datetimeoffset(7)` | 是 | — | 工作器租约截止时刻；续租、完成必须校验当前令牌。 |

索引：
- `ix_collaboration_export_state`：普通 (tenant_n_id,state,created_on)。
- `PK__collabor__3213E83F9E97B8BB`：主键 (id)。
- `uq_collaboration_export`：唯一 (tenant_n_id,export_n_id)。
- `uq_collaboration_export_operation`：唯一 (tenant_n_id,operation_id)。
- `uq_collaboration_export_request`：唯一 (tenant_n_id,created_by_user_n_id,request_n_id)。

### `collaboration_compliance_preparation`

Collaboration 的合规准备持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(128)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_n_id` | `nvarchar(128)` | 否 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `actor_session_n_id` | `nvarchar(max)` | 否 | — | 操作主体会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `action` | `nvarchar(max)` | 否 | — | 动作；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `target_n_id` | `nvarchar(max)` | 是 | — | 目标业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `scope_checksum` | `nvarchar(max)` | 否 | — | 范围校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `command_json` | `nvarchar(max)` | 否 | — | 命令JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `snapshot_json` | `nvarchar(max)` | 否 | — | 快照JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `prepared_on` | `datetimeoffset(7)` | 否 | — | 准备时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `pk_collaboration_compliance_preparation`：主键 (tenant_n_id,actor_user_n_id,request_n_id)。

### `collaboration_compliance_view_budget`

Collaboration 的合规查看额度持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `actor_user_n_id` | `nvarchar(128)` | 否 | — | 操作主体用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `window_start_on` | `datetimeoffset(7)` | 否 | — | 窗口开始时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `result_count` | `int` | 否 | — | 结果数量；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `pk_collaboration_compliance_view_budget`：主键 (tenant_n_id,actor_user_n_id,window_start_on)。

### `collaboration_conversation`

Collaboration 的会话持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `participant_low_user_n_id` | `nvarchar(128)` | 否 | — | 参与人较小用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `participant_high_user_n_id` | `nvarchar(128)` | 否 | — | 参与人较大用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `last_message_sequence` | `bigint` | 否 | — | 最近消息序号；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `last_message_n_id` | `nvarchar(max)` | 是 | — | 最近消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `last_message_on` | `datetimeoffset(7)` | 是 | — | 最近消息时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retention_floor_sequence` | `bigint` | 否 | — | 已清理消息序号下界；用于判断拉取缺口。 |

索引：
- `PK__collabor__3213E83F1C8E11D6`：主键 (id)。
- `uq_collaboration_conversation_nid`：唯一 (tenant_n_id,n_id)。
- `uq_collaboration_conversation_pair`：唯一 (tenant_n_id,participant_low_user_n_id,participant_high_user_n_id)。

CHECK：
- `ck_collaboration_conversation_pair`：`([participant_low_user_n_id]<[participant_high_user_n_id])`。

### `collaboration_conversation_member`

Collaboration 的会话成员持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `conversation_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `user_n_id` | `nvarchar(128)` | 否 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `display_name_snapshot` | `nvarchar(256)` | 否 | — | 显示名称快照；记录当时事实供历史展示，不随来源对象改名自动重写。 |
| `joined_on` | `datetimeoffset(7)` | 否 | — | 加入时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `visibility_state` | `nvarchar(128)` | 否 | — | 可见性状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `hidden_on` | `datetimeoffset(7)` | 是 | — | 隐藏时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `hidden_through_sequence` | `bigint` | 否 | — | 用户隐藏到的消息序号边界；仅影响该用户可见性。 |
| `last_read_sequence` | `bigint` | 否 | — | 成员已读到的消息序号；只可按既有规则向前推进。 |
| `last_read_on` | `datetimeoffset(7)` | 是 | — | 最近已读时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `unread_count` | `bigint` | 否 | — | 成员未读数量投影；由消息/已读操作更新，不作为原始消息事实。 |
| `projection_version` | `bigint` | 否 | — | 投影版本；所属业务事实的修订号，比较时按原模块规则防止旧事实覆盖。 |

索引：
- `ix_collaboration_conversation_member_user`：普通 (tenant_n_id,user_n_id,visibility_state)。
- `PK__collabor__3213E83F33B1FBB9`：主键 (id)。
- `uq_collaboration_member`：唯一 (tenant_n_id,conversation_n_id,user_n_id)。

### `collaboration_event_inbox`

Collaboration 的事件收件箱持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `event_type` | `nvarchar(max)` | 否 | — | 事件类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(max)` | 否 | — | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |
| `received_on` | `datetimeoffset(7)` | 否 | — | 接收时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `processed_on` | `datetimeoffset(7)` | 是 | — | 处理时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retry_count` | `int` | 否 | — | 重试计数；由工作器维护，受现有重试上限约束。 |
| `last_error` | `nvarchar(max)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |
| `lease_n_id` | `nvarchar(max)` | 是 | — | 租约业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `lease_until` | `datetimeoffset(7)` | 是 | — | 任务租约截止时刻；到期后按已有恢复规则处理。 |

索引：
- `PK__collabor__2370F72785260E94`：主键 (event_id)。

### `collaboration_legal_hold_case`

Collaboration 的法律保全保全案件持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `hold_case_n_id` | `nvarchar(128)` | 否 | — | 保全保全案件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `state` | `nvarchar(128)` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `scope_json` | `nvarchar(max)` | 否 | — | 范围JSON；按该模块的列表/对象契约序列化，读取时做业务校验，禁止加入凭据。 |
| `scope_checksum` | `nvarchar(max)` | 否 | — | 范围校验和；由规范化事实计算，用于重复/漂移/一致性核验，不保存原始秘密。 |
| `reason` | `nvarchar(max)` | 否 | — | 原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `created_by_user_n_id` | `nvarchar(128)` | 否 | — | 创建操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `released_on` | `datetimeoffset(7)` | 是 | — | 释放时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `request_n_id` | `nvarchar(128)` | 是 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 是 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `operation_id` | `nvarchar(max)` | 是 | — | 操作标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `release_requested_by_user_n_id` | `nvarchar(max)` | 是 | — | 释放请求操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `release_request_n_id` | `nvarchar(max)` | 是 | — | 释放请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `release_approval_expires_on` | `datetimeoffset(7)` | 是 | — | 释放审批过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `release_approved_by_user_n_id` | `nvarchar(max)` | 是 | — | 释放已批准操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `reviewed_by_user_n_id` | `nvarchar(max)` | 是 | — | 复核操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `reviewed_on` | `datetimeoffset(7)` | 是 | — | 复核时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `external_case_reference` | `nvarchar(max)` | 是 | — | 外部保全案件引用；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `file_sync_state` | `nvarchar(max)` | 否 | (N'Pending') | 文件同步状态；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `ix_collaboration_hold_state`：普通 (tenant_n_id,state)。
- `PK__collabor__3213E83F121C0210`：主键 (id)。
- `uq_collaboration_hold`：唯一 (tenant_n_id,hold_case_n_id)。
- `uq_collaboration_hold_request`：唯一 (tenant_n_id,created_by_user_n_id,request_n_id)。

### `collaboration_message`

Collaboration 的消息持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `conversation_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `message_n_id` | `nvarchar(max)` | 否 | — | 消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `sequence` | `bigint` | 否 | — | 对象内部顺序号；由所属聚合/执行器分配，不以客户端时间替代。 |
| `sender_user_n_id` | `nvarchar(128)` | 否 | — | 发送者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `client_message_n_id` | `nvarchar(128)` | 否 | — | 客户端消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `message_type` | `nvarchar(max)` | 否 | — | 消息类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `text_content` | `nvarchar(max)` | 是 | — | 文本内容；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `reply_to_message_n_id` | `nvarchar(max)` | 是 | — | 回复目标消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `attachment_n_id` | `nvarchar(max)` | 是 | — | 附件业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `accepted_on` | `datetimeoffset(7)` | 否 | — | 入队时间。 |
| `retracted_on` | `datetimeoffset(7)` | 是 | — | 撤回时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retracted_by_user_n_id` | `nvarchar(max)` | 是 | — | 撤回操作方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `retraction_reason` | `nvarchar(max)` | 是 | — | 撤回原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `message_state_version` | `int` | 否 | — | 消息状态版本；撤回、清理等投影用于拒绝旧事实覆盖。 |

索引：
- `ix_collaboration_message_conversation_sequence`：普通 (tenant_n_id,conversation_n_id,sequence)。
- `ix_collaboration_message_sender`：普通 (tenant_n_id,sender_user_n_id,accepted_on)。
- `PK__collabor__3213E83F072EFBB9`：主键 (id)。
- `uq_collaboration_message_client`：唯一 (tenant_n_id,sender_user_n_id,client_message_n_id)。
- `uq_collaboration_message_sequence`：唯一 (tenant_n_id,conversation_n_id,sequence)。

### `collaboration_message_personal_visibility`

Collaboration 的消息个人可见性持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_n_id` | `nvarchar(128)` | 否 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `conversation_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `message_n_id` | `nvarchar(128)` | 否 | — | 消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `hidden_on` | `datetimeoffset(7)` | 否 | — | 隐藏时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_collaboration_message_personal_visibility_lookup`：普通 (tenant_n_id,user_n_id,conversation_n_id,message_n_id)。
- `PK__collabor__022D5CEF177CF21B`：主键 (tenant_n_id,user_n_id,message_n_id)。

### `collaboration_messaging_deduplication`

Collaboration 的消息去重持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：所属服务的迁移/仓储映射（此表为技术账本或原生 SQL 表）。

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `sender_user_n_id` | `nvarchar(128)` | 否 | — | 发送者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `client_message_n_id` | `nvarchar(128)` | 否 | — | 客户端消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `message_n_id` | `nvarchar(max)` | 否 | — | 消息业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `conversation_n_id` | `nvarchar(max)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `sequence` | `bigint` | 否 | — | 对象内部顺序号；由所属聚合/执行器分配，不以客户端时间替代。 |
| `accepted_on` | `datetimeoffset(7)` | 否 | — | 入队时间。 |
| `content_purged_on` | `datetimeoffset(7)` | 是 | — | 内容清除时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `pk_collaboration_message_deduplication`：主键 (tenant_n_id,sender_user_n_id,client_message_n_id)。

### `collaboration_outbox_message`

Collaboration 的发件箱消息持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `event_id` | `uniqueidentifier` | 否 | — | 事件唯一标识；同一事实重投沿用此 ID，用于消费去重。 |
| `tenant_n_id` | `nvarchar(max)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `event_type` | `nvarchar(max)` | 否 | — | 事件类型；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `payload` | `nvarchar(max)` | 否 | — | 序列化事件内容；Outbox/Inbox 按对应事件契约读写，数据库只校验 JSON 结构，不替代业务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `published_on` | `datetimeoffset(7)` | 是 | — | 发布时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `retry_count` | `int` | 否 | — | 重试计数；由工作器维护，受现有重试上限约束。 |
| `last_error` | `nvarchar(max)` | 是 | — | 最近一次脱敏失败摘要；不得包含连接串、密码、令牌或业务敏感正文。 |
| `lease_n_id` | `nvarchar(max)` | 是 | — | 租约业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `lease_until` | `datetimeoffset(7)` | 是 | — | 任务租约截止时刻；到期后按已有恢复规则处理。 |
| `dead_lettered_on` | `datetimeoffset(7)` | 是 | — | 失效信时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_collaboration_outbox_pending`：普通 (published_on,dead_lettered_on,lease_until,created_on)。
- `PK__collabor__2370F7270D0C40E0`：主键 (event_id)。

### `collaboration_remote_assistance_session`

Collaboration 的远程协助会话持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[RemoteAssistanceTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/RemoteAssistanceTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `conversation_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `initiator_user_n_id` | `nvarchar(128)` | 否 | — | 发起方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `invitee_user_n_id` | `nvarchar(max)` | 否 | — | 被邀请方用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `direction` | `nvarchar(max)` | 否 | — | 方向；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `sharer_user_n_id` | `nvarchar(128)` | 否 | — | 共享者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `viewer_user_n_id` | `nvarchar(max)` | 否 | — | 查看者用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `state` | `nvarchar(128)` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `answer` | `nvarchar(max)` | 是 | — | 应答；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `request_n_id` | `nvarchar(128)` | 否 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `deadline_on` | `datetimeoffset(7)` | 否 | — | 截止时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `accepted_on` | `datetimeoffset(7)` | 是 | — | 入队时间。 |
| `started_on` | `datetimeoffset(7)` | 是 | — | 开始时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `ended_on` | `datetimeoffset(7)` | 是 | — | 结束时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `end_reason` | `nvarchar(max)` | 是 | — | 结束原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `initiator_connection_id` | `nvarchar(max)` | 否 | — | 发起方连接标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `invitee_connection_id` | `nvarchar(max)` | 是 | — | 被邀请方连接标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `initiator_alive_until` | `datetimeoffset(7)` | 是 | — | 发起方存活截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `invitee_alive_until` | `datetimeoffset(7)` | 是 | — | 被邀请方存活截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `initiator_stopped_reported_on` | `datetimeoffset(7)` | 是 | — | 发起方已停止上报时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `invitee_stopped_reported_on` | `datetimeoffset(7)` | 是 | — | 被邀请方已停止上报时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_collaboration_remote_assistance_session_conversation_created`：普通 (tenant_n_id,conversation_n_id,created_on,n_id)。
- `ix_collaboration_remote_assistance_session_state_deadline`：普通 (tenant_n_id,state,deadline_on)。
- `PK__collabor__3213E83F9551151C`：主键 (id)。
- `uq_collaboration_remote_assistance_session_active_conversation`：唯一 (tenant_n_id,conversation_n_id)；过滤 ([is_deleted]=(0) AND ([state] IN (N'Pending', N'Accepted', N'Connecting', N'Sharing')))。
- `uq_collaboration_remote_assistance_session_active_sharer`：唯一 (tenant_n_id,sharer_user_n_id)；过滤 ([is_deleted]=(0) AND ([state] IN (N'Accepted', N'Connecting', N'Sharing')))。
- `uq_collaboration_remote_assistance_session_nid`：唯一 (tenant_n_id,n_id)。
- `uq_collaboration_remote_assistance_session_request`：唯一 (tenant_n_id,initiator_user_n_id,request_n_id)。

CHECK：
- `ck_collaboration_remote_assistance_session_answer`：`([answer] IS NULL OR ([answer]=N'Decline' OR [answer]=N'Accept'))`。
- `ck_collaboration_remote_assistance_session_direction`：`([direction]=N'RequestPeer' OR [direction]=N'ShareMine')`。
- `ck_collaboration_remote_assistance_session_state`：`([state]=N'Ended' OR [state]=N'Failed' OR [state]=N'Expired' OR [state]=N'Cancelled' OR [state]=N'Declined' OR [state]=N'Sharing' OR [state]=N'Connecting' OR [state]=N'Accepted' OR [state]=N'Pending')`。

### `collaboration_remote_assistance_voice_call`

Collaboration 的远程协助语音调用持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[RemoteAssistanceTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/RemoteAssistanceTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `n_id` | `nvarchar(128)` | 否 | — | 本对象的稳定业务标识；新增时确定，关联与外部接口按业务范围使用。 |
| `conversation_n_id` | `nvarchar(128)` | 否 | — | 会话业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `caller_user_n_id` | `nvarchar(128)` | 否 | — | 主叫用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `callee_user_n_id` | `nvarchar(max)` | 否 | — | 被叫用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `state` | `nvarchar(128)` | 否 | — | 该对象的状态值；数值或文本编码对应源码领域枚举/状态机，不代表统一跨模块状态。 |
| `answer` | `nvarchar(max)` | 是 | — | 应答；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `request_n_id` | `nvarchar(128)` | 否 | — | 请求业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `request_hash` | `nvarchar(max)` | 否 | — | 规范化请求内容哈希；与幂等键联合核对，不能保存秘密原文。 |
| `deadline_on` | `datetimeoffset(7)` | 否 | — | 截止时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `accepted_on` | `datetimeoffset(7)` | 是 | — | 入队时间。 |
| `started_on` | `datetimeoffset(7)` | 是 | — | 开始时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `ended_on` | `datetimeoffset(7)` | 是 | — | 结束时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `end_reason` | `nvarchar(max)` | 是 | — | 结束原因；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `caller_connection_id` | `nvarchar(max)` | 否 | — | 主叫连接标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `callee_connection_id` | `nvarchar(max)` | 是 | — | 被叫连接标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `caller_alive_until` | `datetimeoffset(7)` | 是 | — | 主叫存活截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `callee_alive_until` | `datetimeoffset(7)` | 是 | — | 被叫存活截止；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `caller_ready_on` | `datetimeoffset(7)` | 是 | — | 主叫就绪时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `callee_ready_on` | `datetimeoffset(7)` | 是 | — | 被叫就绪时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `caller_stopped_reported_on` | `datetimeoffset(7)` | 是 | — | 主叫已停止上报时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `callee_stopped_reported_on` | `datetimeoffset(7)` | 是 | — | 被叫已停止上报时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_collaboration_remote_assistance_voice_call_conversation_created`：普通 (tenant_n_id,conversation_n_id,created_on,n_id)。
- `ix_collaboration_remote_assistance_voice_call_state_deadline`：普通 (tenant_n_id,state,deadline_on)。
- `PK__collabor__3213E83F9D03CA75`：主键 (id)。
- `uq_collaboration_remote_assistance_voice_call_active_conversation`：唯一 (tenant_n_id,conversation_n_id)；过滤 ([is_deleted]=(0) AND ([state] IN (N'Ringing', N'Accepted', N'Connecting', N'Active')))。
- `uq_collaboration_remote_assistance_voice_call_nid`：唯一 (tenant_n_id,n_id)。
- `uq_collaboration_remote_assistance_voice_call_request`：唯一 (tenant_n_id,caller_user_n_id,request_n_id)。

CHECK：
- `ck_collaboration_remote_assistance_voice_answer`：`([answer] IS NULL OR ([answer]=N'Decline' OR [answer]=N'Accept'))`。
- `ck_collaboration_remote_assistance_voice_state`：`([state]=N'Ended' OR [state]=N'Failed' OR [state]=N'Missed' OR [state]=N'Cancelled' OR [state]=N'Declined' OR [state]=N'Active' OR [state]=N'Connecting' OR [state]=N'Accepted' OR [state]=N'Ringing')`。

### `collaboration_remote_assistance_voice_slot`

Collaboration 的远程协助语音占位持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[RemoteAssistanceTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/RemoteAssistanceTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `user_n_id` | `nvarchar(128)` | 否 | — | 用户业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `voice_call_n_id` | `nvarchar(128)` | 否 | — | 语音调用业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `expires_on` | `datetimeoffset(7)` | 否 | — | 过期时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |

索引：
- `ix_collaboration_remote_assistance_voice_slot_call`：普通 (tenant_n_id,voice_call_n_id)。
- `pk_collaboration_remote_assistance_voice_slot`：主键 (tenant_n_id,user_n_id)。

### `collaboration_retention_checkpoint`

Collaboration 的保留检查点持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `operation_n_id` | `nvarchar(128)` | 否 | — | 操作业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `cutoff_on` | `datetimeoffset(7)` | 否 | — | 截止时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `last_accepted_on` | `datetimeoffset(7)` | 是 | — | 最近接受时刻；由对应生命周期操作记录 UTC 时刻，未发生/无期限是否允许 NULL 见类型列。 |
| `last_message_id` | `uniqueidentifier` | 是 | — | 最近消息标识：关联对象的持久化主键；关系及更新规则见本表外键。 |
| `stage` | `nvarchar(128)` | 否 | — | 阶段；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `lease_owner` | `nvarchar(max)` | 是 | — | 任务租约持有者；由工作器领取时设置，完成/续租需匹配持有者。 |
| `lease_expires_on` | `datetimeoffset(7)` | 是 | — | 租约到期时间。 |
| `updated_on` | `datetimeoffset(7)` | 否 | — | 最近状态或投影更新时间；由写入方记录 UTC 时间。 |
| `error_code` | `nvarchar(max)` | 是 | — | 错误编码；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |

索引：
- `ix_collaboration_retention_checkpoint_active`：普通 (tenant_n_id,stage,updated_on)。
- `PK__collabor__3213E83F5C2D90D9`：主键 (id)。
- `uq_collaboration_retention_checkpoint`：唯一 (tenant_n_id,operation_n_id)。

### `collaboration_retention_policy`

Collaboration 的保留策略持久事实；由该服务维护，不作为跨服务直接写表接口。

实体入口：[CollaborationTables.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationTables.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `id` | `uniqueidentifier` | 否 | — | 持久化主键，由服务生成 GUID；业务标识另看 n_id，调用方不能用此列替代租户校验。 |
| `is_frozen` | `bit` | 否 | — | 冻结标志；影响业务操作资格，由该领域已有冻结规则决定。 |
| `is_locked` | `bit` | 否 | — | 锁定标志；由该领域的锁定操作维护，不能绕过操作权限。 |
| `is_deleted` | `bit` | 否 | — | 软删除标志；正常业务查询排除已删除行，不等于物理删除。 |
| `entity_type` | `nvarchar(max)` | 否 | — | 持久化实体类别标识；由服务设置，用于现有实体契约。 |
| `created_on` | `datetimeoffset(7)` | 否 | — | 创建时刻；由服务记录 UTC 时间，既有创建事实不随修改重写。 |
| `last_updated_on` | `datetimeoffset(7)` | 否 | — | 最近业务修改时刻；服务在成功写入时更新 UTC 时间。 |
| `optimistic_version` | `bigint` | 否 | — | 递增整数并发版本；修改必须携带读取时版本，成功后递增，旧版本拒绝。 |
| `concurrency_version` | `uniqueidentifier` | 否 | — | GUID 并发令牌；与整数版本同时匹配，成功修改生成新值。 |
| `tenant_n_id` | `nvarchar(128)` | 否 | — | 所属租户业务标识；来自已认证主体，读写必须带租户条件。 |
| `policy_n_id` | `nvarchar(128)` | 否 | — | 策略业务标识：对应对象的稳定业务标识；按租户和领域权限读取，存在性由关联或服务校验。 |
| `message_retention_days` | `int` | 否 | — | 消息保留天；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `attachment_retention_days` | `int` | 否 | — | 附件保留天；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `audit_retention_days` | `int` | 否 | — | 审计保留天；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `enabled` | `bit` | 否 | — | 是否启用。 |
| `export_retention_hours` | `int` | 否 | ((24)) | 导出保留小时；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `review_due_hours` | `int` | 否 | ((24)) | 复核到期小时；由所属服务的对应创建、配置或状态变更操作维护，不能通过直接改表绕过业务校验。 |
| `status` | `nvarchar(max)` | 否 | (N'Active') | 该对象的生命周期状态；只允许领域状态机已有取值，由相应命令推进，不能直接改表跳状态。 |

索引：
- `PK__collabor__3213E83F281BF81A`：主键 (id)。
- `uq_collaboration_retention`：唯一 (tenant_n_id,policy_n_id)。

### `collaboration_schema_migrations`

Collaboration 不可变迁移执行账本；只登记已成功提交的步骤，禁止手工删记录或改 checksum。

实体入口：[CollaborationSchemaMigrations.cs](../../../src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/CollaborationSchemaMigrations.cs)

| 字段 | SQL 类型 | NULL | 默认／计算 | 中文含义与维护 |
| --- | --- | --- | --- | --- |
| `migration_id` | `nvarchar(128)` | 否 | — | 不可变迁移步骤标识；执行成功后记账，已应用步骤不得原地改 SQL 或删除记录。 |
| `description` | `nvarchar(max)` | 否 | — | 对象用途或业务说明；由管理操作维护，空值表示未填写。 |
| `checksum` | `varchar(64)` | 是 | — | 内容校验和；由受控执行器计算，用于拒绝同版本内容漂移，不是密码学签名。 |
| `applied_on` | `datetimeoffset(7)` | 否 | — | 迁移或种子成功提交时刻；由执行器记录 UTC 时间。 |

索引：
- `PK__collabor__DE86177DC3E59058`：主键 (migration_id)。
