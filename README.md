# Industrial Platform

面向制造企业的工业数字化执行平台，以 **.NET 10 + Vue 3** 为技术基础，覆盖统一管理端、生产操作端、工业数据与 MES 业务扩展。

平台采用 Clean Architecture，按业务复杂度使用 DDD，通过明确的模块契约、数据所有权和部署边界，兼顾统一部署的简洁性与后续独立部署的扩展能力。

## 整体架构

### 统一前端与两种部署入口

```text
统一前端：PC 管理模式 / PC 生产操作模式 / PDA / Mobile
                         │
             相同的外部 API 契约与服务前缀
                         │
            ┌────────────┴────────────┐
            │                         │
     统一进程部署（默认）          分布式部署
     UnifiedHost :5041           Gateway :5080
            │                    YARP 反向代理
            │                         │
       进程内模块组合              独立 API Host
            │                         │
            └────────────┬────────────┘
                         │
          Identity / SystemData / ReferenceData
                         │
       BuildingBlocks：查询、数据访问、安全、事件、日志
                         │
           PostgreSQL / Redis / RabbitMQ / Seq
```

两种入口是并列部署方式，不串联使用；组合部署不改变模块的数据所有权。

| 部署角色 | 主要职责 | 边界 |
| --- | --- | --- |
| UnifiedHost | 在单一 ASP.NET Core 进程中组合模块，统一中间件，协调模块自有初始化，托管生产 SPA | 不运行 YARP，不代理下游，不拥有模块业务规则和迁移实现 |
| Gateway | YARP 路由、服务前缀处理、CORS、下游健康聚合与代理错误处理 | 不加载业务模块，不托管 SPA，不执行迁移，也不是服务间业务总线 |

前端通过 `/identity/**`、`/systemdata/**`、`/referencedata/**` 访问对应能力。默认 API 入口为 `http://localhost:5041`；分布式部署使用 `http://localhost:5080`，前端不绑定业务服务内部端口。

### 分层与模块边界

| 层次 | 职责 |
| --- | --- |
| API | HTTP 入口、身份与权限校验、请求和响应适配 |
| Application | 应用用例、业务流程协调与访问端口 |
| Domain | 领域模型、业务规则与不变量；简单管理功能不强制复杂聚合 |
| Infrastructure | SqlSugar 持久化、缓存、事件发布及外部系统适配 |
| Contracts | 模块对外公开的数据和交互契约 |
| BuildingBlocks | 可复用的技术基础能力，不承载具体业务领域规则 |

- 依赖向内收敛，Domain 不反向依赖 Web、数据库或消息实现。
- 同宿主模块通过公开应用契约协作；跨宿主通过 API 或事件协作，不跨模块直读、直写 Repository 和数据表。
- **Service Host、领域模块、初始化单元、部署单元不是同一个概念**。共用宿主不等于合并领域，也不要求每个模块都独立部署。
- SystemData 负责拓扑、编排、策略与观测；各服务负责自己的 Migration、Seed、Bootstrap、Verify 和 Ledger。
- 以单租户完整可用为基础，保留可信身份上下文中的租户边界；部署拓扑不替代数据隔离规则。

详细边界见 [Service Host 与内部模块设计](docs/blueprint/32-Industrial%20Platform%20Service%20Host与内部模块边界.md)。

## 中间件与基础设施

以下为仓库本地 Docker Compose 中配置的基础组件，运行时也可连接已配置的云端开发环境。

| 组件 | 本地镜像基线 | 用途 |
| --- | --- | --- |
| PostgreSQL | 18 | 业务关系数据、模块持久化与初始化账本 |
| Redis | 7.4 | 缓存、会话相关状态与分布式锁等共享能力 |
| RabbitMQ | 4 | 跨服务集成事件与异步消息传递 |
| Seq | 2025 | 汇集 Serilog 结构化日志，辅助关联请求与排障 |
| Docker Compose | 按环境安装 | 基础设施编排、数据卷和运行环境管理 |

业务事实归属数据库，缓存和消息通道不替代领域数据所有权。对象存储、时序数据、检索及模型运行时等扩展基础设施，随对应模块设计确定，不作为上述基础环境的默认组成。

配置与连接方式见 [本地基础设施](docker/README.md) 和 [云端开发环境](deploy/cloud-dev/README.md)。真实连接凭据使用本地私有配置或环境变量，不写入仓库文档。

## 所用技术

| 领域 | 技术与组件 | 使用方向 |
| --- | --- | --- |
| 后端框架 | C#、.NET 10、ASP.NET Core | API、模块组合与后台服务 |
| 数据访问 | SqlSugar、Npgsql | PostgreSQL 数据访问、仓储与工作单元 |
| 身份与安全 | JWT Bearer、BCrypt、权限策略 | 身份认证、密码保护、权限与会话控制 |
| 查询与接口 | ASP.NET Core OData、QueryDescriptor、OpenAPI | 受控查询适配、统一查询契约与接口描述 |
| 缓存与消息 | StackExchange.Redis、RabbitMQ.Client | 缓存和分布式锁适配、集成事件总线 |
| 日志与 Web 管线 | Serilog、ASP.NET Core Middleware | 结构化日志、TraceId、统一异常和 ApiResult 响应 |
| 分布式入口 | YARP | 独立服务部署时的反向代理 |
| 前端框架 | Vue 3、TypeScript、Vite | PC、PDA、Mobile 单包前端工程 |
| 前端交互 | Element Plus、vxe-table | 统一表格、查询、表单、弹窗和抽屉 |
| 前端基础 | Vue Router、Pinia、Axios、vue-i18n | 路由、状态、请求封装与多语言 |
| 后端测试 | xUnit、ASP.NET Core MVC Testing、Coverlet | 单元、API、集成验证与覆盖率采集 |
| 前端测试 | Vitest、Vue Test Utils、MSW、Playwright | 单元、组件、接口模拟与端到端验证 |
| 工程工具 | pnpm、ESLint、Prettier、PowerShell | 依赖管理、代码检查、格式化与开发脚本 |

前端沿用统一主题与设计 Token，管理页面复用查询、表格和业务操作组件；PC 生产操作模式以简洁、大触控目标和少层级为设计方向，不复制管理端的复杂导航。

版本以工程配置为准：后端依赖见 [Directory.Packages.props](Directory.Packages.props)，前端依赖见 [package.json](src/frontend/package.json)。工具链由 [global.json](global.json) 与 [.mise.toml](.mise.toml) 固定为 .NET SDK `10.0.302`、Node.js `24.18.0`、pnpm `11.16.0`。

## 参照项目

参考成熟项目的交互与能力设计，优先复用适合本项目的组件，不整体迁入其他项目的架构和领域模型。

### 产品与交互参考

| 项目 | 本平台的参考方向 |
| --- | --- |
| [MalusAdmin](https://gitee.com/Pridejoy/MalusAdmin) | 后台视觉、导航布局与多终端适配思路 |
| [mes-TMom](https://gitee.com/thgao/tmom) | 工业管理场景、低代码元数据、表单与页面编排思路 |
| [Admin.NET](https://gitee.com/zuohuaijun/Admin.NET) | 后台基础功能覆盖、权限管理与通用管理交互 |

参考不代表依赖、代码移植或功能等同；实际选型以本仓库实现为准，复用代码和资源前需核对相应许可证。

### 表格与多语言方案

以下不仅是设计参考，也是前端实际采用的基础依赖，由平台统一封装和接入。

| 成熟方案 | 本平台的使用方式 |
| --- | --- |
| [vxe-table](https://github.com/x-extends/vxe-table) | 统一表格 `AppDataTable` 的底层表格引擎；在其基础上封装查询、列配置、行选择和操作区等平台交互，业务页面复用统一组件，不直接绑定第三方实例 |
| [Vue I18n](https://github.com/intlify/vue-i18n) | 多语言运行时，复用语言资源、语言切换与回退机制；统一接入平台外壳、导航、页签、页面及表格公共文案 |

表格以用户管理黄金样板页为统一交互基准；多语言使用稳定资源键，并与组件库语言保持一致。平台专属的权限、查询语义与业务行为仍由本项目维护。

## 整体规划

建设顺序遵循 **先收束平台基础能力至 PF-11，再进入 MES 业务开发**。以下描述产品方向与模块归属，不作为已交付能力清单或开发进度表。

### 平台基础层

平台宿主规划采用七个核心 Service Host；PF-01 的统一外壳属于前端。阶段编号不等于独立微服务数量。

| 规划阶段 | 宿主或工程归属 | 能力方向 |
| --- | --- | --- |
| PF-00 | Identity.Service | 用户、用户组、角色权限、登录与会话、企业身份接入 |
| PF-01 | 统一前端 | 品牌与主题、多语言、导航、管理和生产操作布局、统一页面规范 |
| PF-02、PF-04、PF-07 | SystemData.Service | 行政组织与岗位、导航与功能配置、服务目录、数据库初始化编排；文件、通知、审计、调度和平台健康模块 |
| PF-03 | ReferenceData.Service | 字典、参数、元数据、动态属性与编码规则 |
| PF-05、PF-06 | Collaboration.Service | 消息、在线状态、附件集成与远程协助 |
| PF-08、PF-09 | PlatformStudio.Service | 数据源、数据集、低代码、看板、报表与发布 |
| PF-10、PF-10A | OperationsCenter.Service | 服务器监控、项目知识空间、问题与知识管理、知识助手、数据助手与模型接入 |
| PF-11 | IoTCollector.Service | 驱动、设备连接、采集点、采集任务与边缘管理 |

File、Notification、Audit 及之后的能力在各阶段实施前重新确认范围和设计。Worker、Agent、远程协助组件与模型运行时属于辅助部署单元，不额外计入七个核心宿主。

### MES 业务层

在平台基础层之上，按业务边界构建制造执行闭环：

- **MasterData**：物料、设备、制造组织、仓库、库位与 BOM 等稳定主数据。
- **OperationalData**：库存批次、余额、预留、收发退及仓储业务单据。
- **生产与质量执行**：工单、生产执行、称量、追溯和批记录，并与设备采集、看板及报表衔接。

SystemData 的行政组织与平台配置、ReferenceData 的参考数据、MasterData 的业务主数据和 OperationalData 的业务运行数据保持独立，避免将所有基础数据集中到同一个模块。

## 工程目录

```text
IndustrialPlatform/
├── src/backend/src/
│   ├── BuildingBlocks/      # 共享技术基础组件
│   ├── Hosts/               # UnifiedHost 统一进程入口
│   ├── Gateway/             # 分布式反向代理入口
│   ├── Services/            # 领域服务及分层项目
│   └── Tools/               # 后端工具
├── src/frontend/            # Vue 3 统一前端与前端测试
├── tests/                   # 后端及跨模块测试
├── docker/                  # 本地基础设施编排
├── deploy/                  # 应用部署、云开发与运行脚本
└── docs/                    # 架构、设计与工程文档
```

## 开发与文档入口

- 开发环境：[开发指南](docs/DEVELOPMENT.md)、[前端说明](src/frontend/README.md)、[VS 与 VS Code 调试](src/DEBUGGING.md)。
- 运行部署：[UnifiedHost](src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/README.md)、[Gateway](src/backend/src/Gateway/README.md)、[应用容器部署](deploy/application/README.md)、[开发脚本](deploy/scripts/README.md)。
- 模块说明：[BuildingBlocks](src/backend/src/BuildingBlocks/README.md)、[Identity](src/backend/src/Services/Identity/README.md)、[SystemData](src/backend/src/Services/SystemData/README.md)、[ReferenceData](src/backend/src/Services/ReferenceData/README.md)。
- 架构设计：[总体架构](docs/blueprint/01-Industrial%20Platform%20总体架构设计%20V1.0.md)、[数据库编排与环境引导](docs/blueprint/33-Industrial%20Platform%20SystemData数据库编排与环境引导.md)、[架构蓝图索引](docs/blueprint/README.md)。

默认开发模式使用 UnifiedHost 单入口；已有 VS、VS Code 或云端调试环境时，按现有配置连接，不重复启动另一套服务。
