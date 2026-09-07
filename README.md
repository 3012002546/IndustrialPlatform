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

## 参考项目与设计理念

以下按截至 2026-09-07 的仓库、设计记录与本轮三段参考对话汇总。**已采用**以工程依赖/代码为依据；**设计参考**只借鉴思路；**待验证候选**不表示已安装、已许可交付或已通过现场验收。版本以锁定配置和阶段证据为准。

### 页面布局与平台交互

| 项目/来源 | 当前关系 | 在平台中的落点 |
| --- | --- | --- |
| [MalusAdmin](https://gitee.com/Pridejoy/MalusAdmin) | 设计参考 | 后台视觉、导航和多终端布局；沿用平台主题/领域边界 |
| [mes-TMom](https://gitee.com/thgao/tmom) | 设计参考 | 工业管理、元数据、表单与低代码编排 |
| [Admin.NET](https://gitee.com/zuohuaijun/Admin.NET) | 设计参考 | 权限与后台基础功能、通用管理流程 |
| [yefeng.club](https://www.yefeng.club/#/sys/menu) | 信息布局参考 | 单表、树表、主从详情；菜单配置用全宽树表，预览/历史按入口展开，不复制其视觉/路由/权限 |
| [Element Plus](https://element-plus.org/) | 已采用 | 基础表单、图标、弹窗/抽屉，经平台组件封装 |
| [vxe-table](https://github.com/x-extends/vxe-table) | 已采用 | AppDataTable 底层引擎；业务页不操作第三方实例/DOM |
| [Vue I18n](https://github.com/intlify/vue-i18n) | 已采用 | 稳定资源键、语言切换/回退，贯穿 Shell、页签、页面和表格 |

平台采用高信息密度、平整内容区、紧凑工具栏与清晰层级；管理模式与生产操作模式分开。普通表格占满工作区，主从页让左侧选择上下文、右侧承载详情，加载不重建左侧。结构化业务用 AppFormDrawer，并保留居中弹窗/右侧抽屉偏好；纯确认仍用确认框。详细约定见[页面标准](docs/frontend/platform-shell-and-list-page-standard.md)。

### 文件上传、通知与审计

| 项目/来源 | 当前关系 | 采用的理念与边界 |
| --- | --- | --- |
| [tus 协议](https://tus.io/protocols/resumable-upload)、[tusdotnet](https://github.com/tusdotnet/tusdotnet)、[tus-js-client](https://github.com/tus/tus-js-client) | 传输协议参考/原设计组件候选 | 顺序偏移与可恢复上传；当前 PF-04 已有协议入口，不应写成已集成这两个包；真实客户端/store/多实例验收仍待补 |
| [ClamAV](https://www.clamav.net/) | 已有扫描适配，真实服务待验收 | 隔离→校验→扫描→授权使用，未知/失败不放行；协议 fixture 不等于真实 clamd |
| [Audit.NET](https://github.com/thepirat000/Audit.NET) | 采集/Provider 设计参考 | 不替代本服务审计事务、失败 spool、追加事实与保留；未认定已集成 |
| [CAP](https://github.com/dotnetcore/CAP) | 条件候选，当前不叠加 | 现有 Outbox/Inbox 能力优先；只有明确缺口才重新评估 |
| SignalR（当前 Notification 已使用） | 已采用实时技术 | 只提示变化，持久通知/消息数据库是权威；重连还需刷新旧记录状态 |

上传的跨设备恢复由平台控制：同一用户重新选择同二进制文件，候选发现后完整 hash 证明与授权分开，接管使旧 writer epoch 失效。下载每次重新授权，扫描和权限不能由前端文件名检查替代。不建两套并行分片状态机；预签名 URL 的签发/一次兑换不等于物理下载只发生一次。实现与限制见[PF-04 evidence](docs/evidence/PF-04.md)及[实施 07](docs/implementation/07-Industrial%20Platform%20File%20Notification%20Audit开发实施方案.md)。

### 聊天窗口与屏幕共享

| 项目/来源 | 当前关系 | 参考落点 |
| --- | --- | --- |
| [WuKongIM](https://github.com/WuKongIM/WuKongIM)、[OpenIM](https://github.com/openimsdk/open-im-server) | 设计参考，不引入完整 IM 引擎 | 业务身份与通信能力分离，发送幂等、可靠保存；不复制完整人员管理/部署体系 |
| [Tinode](https://github.com/tinode/chat) | 协议设计参考 | 消息序号/已读游标与旧消息状态恢复分别处理 |
| [Zulip](https://github.com/zulip/zulip) | 同步设计参考 | 初始快照与订阅竞态、重连与一致性验证 |
| [vue-advanced-chat](https://github.com/advanced-chat/vue-advanced-chat) | 窗口交互参考 | 消息列表、输入、历史和状态交互；不作为整体替换窗口的前置 |
| [Screego](https://github.com/screego/server) | 未修改基准 PoC，尚未执行本轮验证 | 屏幕共享/文字可读性/多人观看与网络路径；生产仍需会话授权和媒体撤权证据 |
| [WebRTC](https://www.w3.org/TR/webrtc/)、[Screen Capture](https://www.w3.org/TR/screen-capture/) | PF-06 标准/候选路线依据 | 浏览器显式捕获、受权信令、direct/relay、真实断流；TURN 可中继，业务后端不录制媒体 |

聊天保持 .NET/SignalR/Vue 路线：平台顶栏未读、快捷抽屉、完整页与工作区共享状态，登录即连接；PDA/Mobile 提供完整 Web 聊天。服务端可靠保存、客户端响应丢失重试、旧窗口状态校正与 MessageStateVersion 合并必须闭环。外部 MES 复用可信身份/目录，可裁剪增强 UI，安全和可靠性不裁剪。

远程协助继续“一名共享者、1～3 名授权观看者”，逐人邀请不泄露原一对一聊天；观看者离开与共享者停止分权。不做远控、录屏或无人值守。保留 Screego/最小信令双 PoC 决策，不能把开源演示可见画面写成生产已完成。见[实施 08](docs/implementation/08-Industrial%20Platform%20Collaboration开发实施方案.md)、[实施 09](docs/implementation/09-Industrial%20Platform%20RemoteAssistance开发实施方案.md)。

### 终端运行时与客户端

| 项目/来源 | 当前关系 | 参考/实施方向 |
| --- | --- | --- |
| [Electron](https://www.electronjs.org/)、[electron-vite](https://github.com/alex8088/electron-vite)、[electron-builder / electron-updater](https://github.com/electron-userland/electron-builder) | 已定 PC 路线，待专项落地和版本核验 | Windows 安装包、主进程/preload 隔离、完整包更新 |
| [Capacitor](https://github.com/ionic-team/capacitor) | 已定 PDA 路线，未创建原生工程 | 复用 Vue3，通过 Kotlin/Java 接广播/JAR/AAR/设备；不强制切换 Ionic UI |
| [Cap-go/capacitor-updater](https://github.com/Cap-go/capacitor-updater) | PDA Bundle 更新优先评估候选 | 原生/Web 版本兼容、签名校验、失败恢复与内网部署 |
| [capacitor-zebra-datawedge](https://github.com/Cap-go/capacitor-zebra-datawedge)、[DataWedge Capacitor Demo](https://github.com/darryncampbell/DataWedge-Ionic-Capacitor-Demo)、[Zebra DataWedge](https://techdocs.zebra.com/datawedge/) | 厂商适配参考 | 广播输入/权限/生命周期；不代表支持所有 PDA |
| [Capacitor Barcode Scanner](https://capacitorjs.com/docs/apis/barcode-scanner)、[ZXing](https://github.com/zxing/zxing)、[ML Kit](https://developers.google.com/ml-kit/vision/barcode-scanning) | 相机扫码候选/参考 | 后置相机一次扫码兜底；最低 OS 和识别能力按实际插件核验 |
| [capacitor-community/bluetooth-le](https://github.com/capacitor-community/bluetooth-le)、[@e-is/capacitor-bluetooth-serial](https://www.npmjs.com/package/@e-is/capacitor-bluetooth-serial)、[@kduma-autoid/capacitor-bluetooth-printer](https://www.npmjs.com/package/@kduma-autoid/capacitor-bluetooth-printer) | 蓝牙接入候选/专项参考 | 分别核验 BLE、SPP/串口与打印语义，插件版本/许可和真实设备兼容未冻结，不选一个“万能蓝牙”依赖 |
| [Headwind Server](https://github.com/h-mdm/hmdm-server)、[Android Agent](https://github.com/h-mdm/hmdm-android)、[XUpdate](https://github.com/xuexiangjys/XUpdate) | 后续规模化管理/小规模升级参考 | 首版不建设完整 MDM，不承诺普通 APK 静默升级 |
| React Native、Tauri、uni-app、PWA | 已比较备选 | 当前保持 Vue3 + Electron/Capacitor；不为比较结果迁移业务 UI |

PF-05/06 Web 完成后执行 PF-06A。Runtime 隔离扫码、蓝牙、相机、文件、通知与更新；键盘仿真、广播、厂商 SDK、相机统一返回扫码结果。BLE 与 Classic SPP 分开验证；PC 完整升级，PDA 分开 Bundle/APK 更新，等待业务安全点再激活。详见[蓝图 34](docs/blueprint/34-Industrial%20Platform终端运行时与客户端架构.md)与[实施 09A](docs/implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md)。

### 标签与现场打印

| 项目/来源 | 当前关系 | 参考落点 |
| --- | --- | --- |
| 用户现有 Siemens OP 项目 | 现场经验参考 | 打印机管理、导入 ZPL 模板/字段替换、打印历史；网络共享不作为平台前提 |
| 用户现有 Siemens EBR 项目 | 现场经验参考 | 工位客户端连接设备、跨工位协作；转为服务端受权调度 Agent |
| [pdfme](https://pdfme.com/)、[vue-plugin-hiprint](https://github.com/CcSimple/vue-plugin-hiprint) | 设计器/渲染候选 | 可视化标签、PDF/图片底稿与动态字段；待许可/版本/真机核验，不同时上多设计器 |
| [QZ Tray](https://qz.io/)、[JSPrintManager](https://www.neodynamic.com/products/printing/js-print-manager/)、[PrintNode](https://www.printnode.com/) | 本地桥接/执行模型对照候选 | 比较打印连接、权限、任务状态、确认来源与本地执行记录；不默认采购/集成 |
| Zebra ZPL/厂商驱动与 SDK | 模板与协议路线 | 保留指令模板并做协议转义；连接方式不等于品牌兼容承诺 |

标签平台优先数据规则：业务来源→字段契约→客户映射→校验/快照→渲染→执行。物料/容器/设备预置可扩展；保密客户字段缺失不得回退内部名称；明细与份数分开，预览/打印使用同一准备快照。重试、重打原标签、按最新数据重新生成分别处理；提交队列不代表物理出纸，结果未知不自动重打。

Label.Service 支持独立交付；浏览器和 Electron 共用 Windows Agent，PDA 直接原生蓝牙打印。PF-10B 在 IoTCollector 前完成，称量以后复用设备连接层并自行管理业务。详见[蓝图 35](docs/blueprint/35-Industrial%20Platform标签管理平台设计.md)与[实施 13B](docs/implementation/13B-Industrial%20Platform标签管理平台开发实施方案.md)。

## 整体规划

建设顺序遵循 **先完成 PF-05/06 Web → PF-06A 终端化，继续原 PF-07～PF-10A → PF-10B 标签平台 → PF-11，再进入 MES 业务开发**。以下描述产品方向与模块归属，不作为已交付能力清单或开发进度表。

### 平台基础层

平台宿主规划采用八个核心 Service Host（包含新规划的 Label.Service）；PF-01 的统一外壳属于前端。阶段编号不等于独立微服务数量。

| 规划阶段 | 宿主或工程归属 | 能力方向 |
| --- | --- | --- |
| PF-00 | Identity.Service | 用户、用户组、角色权限、登录与会话、企业身份接入 |
| PF-01 | 统一前端 | 品牌与主题、多语言、导航、管理和生产操作布局、统一页面规范 |
| PF-02、PF-04、PF-07 | SystemData.Service | 行政组织与岗位、导航与功能配置、服务目录、数据库初始化编排；文件、通知、审计、调度和平台健康模块 |
| PF-03 | ReferenceData.Service | 字典、参数、元数据、动态属性与编码规则 |
| PF-05、PF-06 | Collaboration.Service | 消息、在线状态、附件集成与远程协助 |
| PF-06A | 终端运行时与客户端 | Web/Electron/Capacitor 共享桥接、扫码/蓝牙/相机与更新，不新增 Host |
| PF-08、PF-09 | PlatformStudio.Service | 数据源、数据集、低代码、看板、报表与发布 |
| PF-10、PF-10A | OperationsCenter.Service | 服务器监控、项目知识空间、问题与知识管理、知识助手、数据助手与模型接入 |
| PF-10B | Label.Service | 独立标签模板、业务数据绑定、打印任务/历史与 Device Agent 协作 |
| PF-11 | IoTCollector.Service | 驱动、设备连接、采集点、采集任务与边缘管理 |

File、Notification、Audit 及之后的能力在各阶段实施前重新确认范围和设计。Worker、Agent、远程协助组件与模型运行时属于辅助部署单元，不额外计入八个规划核心宿主。

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
│   └── Services/            # 领域服务及分层项目
├── src/frontend/            # Vue 3 统一前端与前端测试
├── tests/                   # 后端及跨模块测试
├── docker/                  # 本地基础设施编排
├── deploy/                  # 应用部署、云开发与运行脚本
└── docs/                    # 架构、设计与工程文档
```

## 开发与文档入口

- 开发环境：[开发指南](docs/DEVELOPMENT.md)、[前端说明](src/frontend/README.md)、[后端测试](tests/README.md)、[VS 与 VS Code 调试](src/DEBUGGING.md)。
- 运行部署：[UnifiedHost](src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/README.md)、[Gateway](src/backend/src/Gateway/README.md)、[应用容器部署](deploy/application/README.md)、[开发脚本](deploy/scripts/README.md)。
- 模块说明：[BuildingBlocks](src/backend/src/BuildingBlocks/README.md)、[Identity](src/backend/src/Services/Identity/README.md)、[SystemData](src/backend/src/Services/SystemData/README.md)、[ReferenceData](src/backend/src/Services/ReferenceData/README.md)。
- 架构设计：[总体架构](docs/blueprint/01-Industrial%20Platform%20总体架构设计%20V1.0.md)、[数据库编排与环境引导](docs/blueprint/33-Industrial%20Platform%20SystemData数据库编排与环境引导.md)、[架构蓝图索引](docs/blueprint/README.md)。

默认开发模式使用 UnifiedHost 单入口；已有 VS、VS Code 或云端调试环境时，按现有配置连接，不重复启动另一套服务。

## 开发协作

以 [AGENTS.md](AGENTS.md)、[当前状态](docs/status/CURRENT.md)、[执行者协议](docs/agents/EXECUTOR.md)、[工程注意事项](docs/agents/ENGINEERING-NOTES.md)为接手入口。蓝图由总控维护，[总 Todo](docs/blueprint/09-Industrial%20Platform开发总TodoList.md)维护顺序，[实施索引](docs/implementation/README.md)指向唯一任务清单，evidence 保存真实结果；历史对话/测试数量不替代当前事实。

Codex 负责蓝图、依赖、PF 整包派遣、冲突裁决和集成；编码执行者在授权范围连续实现、自测并交接，独立验收从稳定交付复核，保留现有开发/验收任务和模型设置。内部 TASK 是执行步骤，不逐项等待确认/提交；轻量明确的修改直接处理。工作树和主工作区例外服从当前 PF 工作包，避免重叠写文件。

PF05起执行[派遣前细化与页面验收规则](docs/implementation/STANDARD-派遣前详细设计与页面验收.md)：完整表字段/类型/约束、接口样例、状态/事务和页面线框先于编码派遣；[待派遣工作包](docs/tasks/pending/README.md)分开记录派遣状态和就绪度，有实际前置缺口不能开工。详细规格与任务精确关联，设计变更由总控先回写，再由执行者连续实现。

## 黄金页面

[Identity 用户管理](src/frontend/src/pages/pc/identity/IdentityUsersPage.vue)是管理列表的唯一黄金样板，组合为 **AppPage.header → AppQueryPanel → AppDataTable → AppFormDrawer/确认弹窗**。规范见[平台 Shell 与列表页标准](docs/frontend/platform-shell-and-list-page-standard.md)。

页面复用同一查询描述、权限、locale 与主题 Token；业务操作与列设置/导出等表格工具分开，紧凑主列表不机械塞入完整工具栏。主从详情、树表、三层配置按信息结构选择布局，不复制截图中的业务规则。验收覆盖中英、主题明暗/密度、权限、空/加载/错误、键盘、200% 缩放、窄窗口和真实浏览器，功能入口需 route/API/权限/菜单真实齐全。

每个页面/结构化弹层先给线框图，标明区域、主数据、选中上下文、动作和加载归属。全宽列表用full工具档；yefeng式左侧紧凑主列表/右侧详情用compact主区、适用时full子表，左区不因右区加载卸载；树配置优先全宽树表，预览/历史经入口打开。消息、媒体和设计器使用专用业务布局。A清空即清B，旧请求不得覆盖新选择；表单同步提交锁加AppFormDrawer.busy；重复label省略时保留aria-label。更多规则与代码当前能力核验见上述共同规则§4～5。

## 注意事项

- 同物理库不等于共享所有权；不跨服务/模块读写 Repository 或建外键。逻辑模块不机械拆空 Schema/账本/Inbox；已初始化服务的 readiness 看本地事实。
- 已采用、设计参考、候选、Mock、真实现场证据分开记录。三端 Web 预留不等于原生兼容；支持 BLE 不等于支持蓝牙打印，打印已提交不等于已物理出纸。
- 保留 VS/VS Code/已有服务和用户数据；优先复用 UnifiedHost，不能凭端口假设环境可用。样式调整不改变 API、权限、查询或业务行为。
- 源码变更后的后端门禁先 fresh Release build，再 `dotnet test ... --configuration Release --no-build`；检查退出码/失败/跳过，文件锁释放后重建。前端按改动执行定向检查/真实页面验收；仅文档修改不冒充业务测试。
- 提交前检查已跟踪/未跟踪/忽略/暂存/未暂存；bin/obj/TestResults/前端产物/缓存/日志和私有配置不得提交。CLAUDE.md、DSH.md 未获明确授权不暂存；精确选取文件，保留他人 WIP。
- 推送故障分别诊断 Git smart-HTTP/代理/SSL/SSH，不以 GitHub 首页可达推断 Git 传输健康；不强推。成功后核对远端哈希及 ahead/behind `0 0`，披露保留的本地修改。
- 新增依赖/SDK/渲染器在实际使用前固定版本、核验许可及目标环境；不因参考项目存在就替换现有成熟实现。标签保密、媒体授权、文件扫描与必要审计始终在服务端执行。
