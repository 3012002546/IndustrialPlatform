# PF05 用户管理查询回归修复

日期：2026-09-09。执行：主控按用户要求在当前会话直接修复，主工作树 develop；未暂存、提交或重启用户调试进程。

## 现场证据与根因

- 复用 `http://localhost:5173/pc/identity/users` 和 UnifiedHost `http://localhost:5041`，当前用户 admin / development，原调试进程连接既有云 Docker。
- 普通查询点击搜索显示“网络不可用”。只读 HTTP 探测：`/health/live` 200，未认证 `/api/v1/users` 500，而 `/api/v1/odata/users` 401。浏览器中的角色查询和用户列头查询均能返回 3 条现有记录，未修改这些数据。
- 新增组合路由测试在修复前明确失败：`GET api/v1/users` 出现两次。Collaboration 的 `~/api/v1` 别名与 Identity 用户管理冲突。路由异常发生在 CORS 前，浏览器因此不能读取错误信封。
- 完整路由扫描进一步发现 OData MetadataController.GetMetadata / GetServiceDocument 同时映射 `GET api/v1`；平台只使用受控 OData 输入适配器，不需要这两个自动发现的控制器端点。
- 浏览器已有聊天 WebSocket/SSE 连接失败日志；认证配置未读取这些浏览器传输使用的 query access_token，Hub 外部路径也缺少 Gateway 所需的服务前缀。

## 修复边界

- Collaboration REST 别名改为完整模块路径 `/collaboration/api/v1/*`，保留 `/api/v1/collaboration/*`。UnifiedHost 模块声明和 Gateway 服务配置均保留其外部前缀；Identity、SystemData、ReferenceData 保持原有语义。
- 平台 AddIndustrialApi 排除 OData 元数据 ApplicationPart，保留平台查询解析器和 UsersODataController。
- 聊天 Hub 新增 `/collaboration/hubs/collaboration-v1`，前端使用此地址；保留原 Hub 地址。仅精确 Hub GET 路径允许一个 query token，不覆盖 Authorization 头或已有认证结果，不改变 JWT 验签、会话撤销和权限策略。
- 未更改用户查询实现、表格组件、数据库结构、迁移账本、云端配置或用户数据。

## 验证

- 新增完整 MVC 路由重复检查，覆盖 Identity 普通/OData 查询、角色、权限目录，以及 Collaboration 目录、会话与合规入口的 401 和 CORS 行为。
- 扩展真实 HTTP 登录测试，在隔离 SQLite 中验证普通用户筛选、OData 筛选、角色、权限树、用户组查询返回 200，并断言用户数据包含 admin；保留原有 SystemData 共享认证检查。
- 增加 6 个 Hub 查询令牌边界测试，检查普通 API 拒绝 query token、精确路径限制、已有头优先和先前事件保留。
- `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release --verbosity quiet`：退出码 0，0 警告、0 错误。
- `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：退出码 0，1778 通过、0 失败、7 个外部环境门控测试跳过。分项：BuildingBlocks 164、Collaboration 66、Gateway 14、Identity 619、IntegrationTests 12、ReferenceData 258、SystemData 623、UnifiedHost 22。
- 前端 `IdentityUsersPage.spec.ts`、`identityUserQueryApi.spec.ts`、`collaborationHub.spec.ts`：3 个文件、24 个测试通过；变更文件 Prettier、ESLint 以及全局 `vue-tsc --build` 均退出码 0。
- 初始普通沙箱运行存在构建产物写权限、pnpm 执行路径限制；通过正常审批后的执行完成验证，未将这些限制解释为产品故障。

## 当前调试实例的限制

上述修复构建的查询验证使用隔离测试宿主；现有云 Docker 调试实例用于复现及只读对照，未擅自停止或替换。它仍可能加载旧后端程序集，需后端重新加载后才会应用新的路由和认证注册，不能以刷新浏览器或上述测试通过宣称现场普通查询已经恢复。真实跨浏览器消息互发、断线恢复及完整 PF05 验收不由本次回归结果替代。
