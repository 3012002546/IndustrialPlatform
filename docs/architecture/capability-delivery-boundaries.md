# 平台能力交付边界

本表用于决定能力放在哪里，避免把每一项通用能力都拆成服务，也避免领域服务反向拥有平台公共数据。`必需依赖` 是运行前提，`可选依赖` 只能增强能力，`可替换依赖` 必须通过当前适配层接入。

| 能力 | 交付形态 | 必需依赖 | 可选/可替换依赖 | 数据所有权 | 健康与独立启动验收 |
| --- | --- | --- | --- | --- | --- |
| `ApiResult`、异常、权限基础设施 | BuildingBlock / 可复用包 | ASP.NET Core、平台契约 | 日志 provider 可替换 | 不拥有业务数据 | 被引用服务可独立启动，响应信封和 traceId 可验证 |
| QueryDescriptor 与受控 OData/SqlSugar 适配 | BuildingBlock / 可复用包 | 平台 descriptor、服务自有 read model | OData parser、SqlSugar provider 可替换 | 不拥有数据，只执行服务白名单查询 | 单元测试覆盖边界；服务不暴露 `IQueryable` |
| PC Shell、Brand、导航、Tabs、PageHeader | 前端组件/模块 | Vue、Router、Pinia、Element Plus | 图标和主题资源通过现有 token 替换 | 不拥有租户、权限或领域数据 | 前端可独立启动，三态导航、键盘和双模式可验收 |
| QueryPanel、DataTable、表格偏好和导出入口 | 前端组件/模块 | Vue、VXE 仅限适配层 | 导出格式实现可替换 | 不拥有列表数据 | 组件契约测试；业务页不直接操作 VXE DOM |
| Identity 用户、角色、会话和权限裁决 | 可嵌入领域模块 + 独立服务边界 | Identity 自有数据库、平台认证契约 | 邮件/SSO 等外部 provider 仅在需求批准后接入 | Identity 拥有身份与授权数据 | UnifiedHost 与独立 API 均可启动；租户隔离、Policy、Users 只读样板可验证 |
| SystemData 组织、岗位、任职、导航和运行时快照 | 可嵌入领域模块 + 独立服务边界 | SystemData 自有数据库、Identity 只读引用 | 缓存或消息通道通过现有适配层替换 | SystemData 拥有组织和发布快照 | 独立 API/初始化管线可启动；降级、快照不可用和写入门禁可验证 |
| PC 生产操作入口 | 前端体验模块 | 现有认证、权限、租户、主题和 locale | 未来领域模块按真实 route 接入 | 不拥有生产业务数据 | 八个未实现卡片无 route/API；设置卡片复用已存在能力 |

## 依赖规则

- 公共组件不能直接依赖 Identity 或 SystemData 的实现细节；通过 props、稳定 API 契约和现有适配器接入。
- 领域服务拥有自己的迁移、读模型、权限 Policy 和启动/健康边界；跨服务只调用明确的受控 API，不共享 IQueryable、数据库连接或隐式上下文。
- 可替换依赖的替换点必须位于已有 BuildingBlock 或服务 adapter 内，页面和领域规则不能知道 provider 类型。
- 独立启动验收至少包含健康端点、无外部依赖时的精确失败证据、最小真实代表路径和日志位置；不可用服务显示降级或不可用，不填充假状态。

## 所有权与扩展

新增能力先根据数据所有权和启动边界分类，再决定是 NuGet/BuildingBlock、前端组件、可嵌入领域模块还是独立服务/产品。只有当独立部署、独立扩缩或独立合规边界真实存在时，才升级为服务；否则保留在拥有数据和规则的模块内。
