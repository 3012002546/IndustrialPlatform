# TASK-SD-006 验证证据

## 状态

已实现，待 Codex 验收。全解决方案 Debug/Release 构建 0 警告、0 错误；SystemData 五层测试全绿（492 用例 0 失败）。

## 提交哈希

未提交（按执行协议保留完整未提交工作树，交由 Codex 编译与提交）。

## 主要修改范围

新增（Contracts，`Contracts/Administration/`）：

- `OrganizationContracts.cs`、`PositionContracts.cs`、`AssignmentContracts.cs`、`IdentityUserDirectoryContracts.cs`（并入 Administration 命名空间）——05 方案 §9.3 全部请求/响应 DTO。

新增（Application）：

- `Administration/` 写路径守卫与错误码（§9.9）、`Organizations/AdministrativeOrganizationService.cs`、`Positions/PositionService.cs`、`Assignments/UserAssignmentService.cs`、`Auditing/`（`ILocalAuditCommand`/`LocalAuditEntry`）、`IdentityDirectory/IIdentityUserDirectory`。
- 端口扩展：`IAdministrativeOrganizationStore`（Tree/Counts/Descendants/Move 校验）、`IPositionStore`（分页过滤）、`IUserAssignmentStore`/`IUserAssignmentAdvisoryLock`。

新增（Api）：

- `Controllers/OrganizationsController.cs`（7 端点）、`PositionsController.cs`（4 端点）、`AssignmentsController.cs`（6 端点）；`Authorization/`（权限策略 + JwtBearer 自校验 fail-closed + `ICurrentUser`）、`Extensions/`。
- 对 `DatabaseOrchestrationController`、`ServiceInitializationController` 补 `[Authorize]` + 权限策略（原注释「鉴权由 SD-006 接入」落地）。

新增（Infrastructure）：

- `AdministrativeOrganizationStore`/`PositionStore` 的查询实现（树展开、分页过滤、Counts/Descendants）。仅补充查询，未改动既有写路径。

新增/修改（测试）：

- Application.Tests：`AdministrativeOrganizationServiceTests`、`PositionServiceTests`、`UserAssignmentServiceTests`、`AdministrationContractResponseTests`、`AdministrationTestDoubles`。
- Api.Tests：`AdministrationEndpointTests`（51 端点用例）、`OpenApiContractTests`（4 用例）、`TestAuthHandler`、`AdministrationEndpointTestDoubles`；对既有 `DatabaseOrchestrationEndpointTests` 补测试鉴权方案（修复本任务鉴权接入导致的 17 例 401）。
- Contract.Tests：`AdministrationContractTests`（25 用例）、`JointIdentityContractTests`（4 用例，联合验证）；csproj 增加对 `Identity.Contracts` 的**测试期**引用。

修改（配置/基建）：

- `Api.csproj`（JwtBearer 包）、`Program.cs`（`AddSystemDataAuthentication/Authorization/CurrentUser`）、`appsettings.json`（空 `Jwt` 节，fail-closed，未含任何凭据）、Application `DependencyInjection.cs`。

证据产物：

- `docs/evidence/TASK-SD-006-openapi.json`（真实 Program 生成的 OpenAPI 快照，3.1.1，43 个 Schema）。
- `docs/evidence/TASK-SD-006-openapi-summary.json`（17 端点 / 14 路径模板摘要）。

## 关键决策

- **租户/执行者只从 `ICurrentUser` 读取**：请求 DTO 一律不携带 TenantNId/ActorUserNId，契约测试锁定（`RequestDtosDoNotCarryTenantOrActorIdentifiers`），防租户伪造。
- **响应不暴露数据库 Guid**：只暴露稳定 NId + 双并发版本（Guid 属性仅 `ConcurrencyVersion`/移动预览 `ExpectedConcurrencyVersion`），契约测试锁定。
- **枚举以枚举名字符串传输**：请求/响应类型/状态一律 string，不引入 JSON 数字依赖；API 采用 `AddControllers` 默认 JsonOptions（不忽略 null 写出），无界任职区间线上为 `effectiveTo:null`（经真实管线实测，契约测试与端点测试双向锁定）。
- **时间保持瞬时**：DateTimeOffset 比较一律用 `GetDateTimeOffset()`；`JsonElement.GetDateTime()` 会把输入转成 Local 墙钟（探针实证），写入请求由服务端按瞬时回传。
- **§9.9 结构化错误码**：400 `SD_VALIDATION_FAILED`/`SD_ORG_PARENT_TYPE_INVALID`；404 `SD_NOT_FOUND`；409 `SD_ORG_CYCLE`/`SD_ORG_HAS_ACTIVE_DEPENDENCIES`/`SD_POSITION_HAS_ACTIVE_ASSIGNMENTS`/`SD_ASSIGNMENT_INTERVAL_OVERLAP`/`SD_ASSIGNMENT_PRIMARY_REQUIRED`/`SD_ASSIGNMENT_PRIMARY_OVERLAP`/`SD_CONCURRENCY_CONFLICT`；503 `SD_IDENTITY_DIRECTORY_UNAVAILABLE`；403 `SD_PERMISSION_DENIED`（平台 403 信封）。
- **联合验证以 `48c5374` 为基线**：SystemData 不直接依赖 Identity.Contracts（生产）；`JointIdentityContractTests` 以测试期引用真实 `UserSummary` 契约，反射核对 `IdentityUserDirectoryEntryV1` 的 UserNId/LoginName/Name/Status 字段存在且类型一致，AuthVersion 为 SystemData 增量可空字段；信封统一为共享 BuildingBlock `ApiResult`（Identity/SystemData 同源），权限令牌声明名与 Identity 签发对齐。

## OpenAPI/JSON 契约报告（§9.3，联合验证）

- 以真实 Program 生成 `/openapi/v1.json`（OpenAPI 3.1.1），17 个管理端点全部出现在快照：组织 7（tree/detail/create/update/move-preview/move/status）、岗位 4（list/create/update/status）、任职 6（list/create/update-scheduled/end/cancel/set-primary）；对应 14 个路径模板、43 个组件 Schema（含管理契约全部 17 个 DTO）。
- `OpenApiContractTests` 冻结：全部 17 端点路径+HTTP 方法、管理契约 Schema 存在性、响应模型不暴露数据库审计字段、文档版本。
- JSON 形状联合验证：线上信封 `{success, code, message, data}` 由共享 `IndustrialPlatform.Web.Results.ApiResult` 提供，Identity 控制器（Auth/Management/Sso）同源；错误码与 §9.9 表逐项核对一致。
- Identity 契约联合验证：以 `48c5374` 真实 Identity `UserSummary`（UserNId/LoginName/Name/Status）核对目录视图映射，4 项断言全过（见 JointIdentityContractTests）。

## 验证命令与逐项结果

| 命令 | 退出码 | 通过/失败数 | 结论 |
| --- | ---: | ---: | --- |
| `dotnet build IndustrialPlatform.slnx -v minimal` | 0 | 0 警告 / 0 错误 | 通过 |
| `dotnet build IndustrialPlatform.slnx -c Release -v minimal` | 0 | 0 警告 / 0 错误 | 通过 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Domain.Tests/...csproj --no-build` | 0 | 153 / 0 | 通过 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Infrastructure.Tests/...csproj --no-build` | 0 | 84 / 0 | 通过 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Application.Tests/...csproj --no-build` | 0 | 152 / 0 | 通过 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Contract.Tests/...csproj --no-build` | 0 | 30 / 0（含联合验证 4） | 通过 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Api.Tests/...csproj --no-build` | 0 | 73 / 0（51 管理端点 + 4 OpenAPI + 17 编排） | 通过 |

覆盖维度：成功路径、400（校验/父子类型）、401（无令牌）、403（缺权限）、404（不存在/跨租户）、409（循环/活动依赖/任职冲突/主任职规则/双版本/revision）、503（用户目录不可用）、租户伪造、双版本冲突、用户目录失败、响应无数据库 Id/审计字段、open-ended 区间、时区瞬时保持。

## 剩余风险

- JwtBearer 自校验 + fail-closed 未在真实 Identity 签发的端到端令牌上实测（本地无完整 SSO 联调）；`TokenValidationParameters` 与 Identity 签发配置对齐性标记**待外部联合验收**。测试以 TestAuthHandler 替换 JwtBearer 验证信封形状，与生产事件输出一致但非真实签名路径。
- 真实 Identity `UserSummary` 联合验证以 `48c5374` 基线为准；若 Identity 契约后续演进（TASK-ID-017～023），`JointIdentityContractTests` 会失败提醒适配，属预期护栏。
- OpenAPI 快照为当前 Program/契约生成；非 17 端点的 Schema（DatabaseOrchestration/ServiceInitialization 等历史任务产物）在快照中保持兼容，未逐一断言。

## 范围外发现

- `docs/status/CURRENT.md` 记录 PF-02 当前分支 `task/pf-02-sd-005`、SD-006 待派遣；属 Codex 维护的状态文件，未修改（按协议不碰）。
- Identity `UserSummary` 不含 SystemData 需要的 `AuthVersion`；已作为 SystemData 侧增量可空字段处理，不要求 Identity 提供，记录于联合验证测试注释。
