# Industrial Platform 蓝图文档索引

本目录是 Industrial Platform 蓝图设计的唯一维护源。外层历史资料不再维护；所有新增设计、设计修订和实现反馈都必须回写到本目录。

当前协作任务只负责蓝图维护、开发 TODO 拆分、任务派遣和结果回写，不在本任务直接开发业务代码。实际开发与测试由其他任务或被派遣的协作者执行；实现过程中发现设计冲突时，先回到本目录确认并修订设计。

## 全局时间规范

- .NET 时间值统一使用 `DateTimeOffset`，可空时间使用 `DateTimeOffset?`；禁止在新增设计中使用 `DateTime` 作为业务时间类型。
- 获取 UTC 当前时间统一使用 `DateTimeOffset.UtcNow`。
- API 使用包含 `Z` 或明确偏移量的 ISO 8601 / RFC 3339 时间字符串。
- PostgreSQL 持久化瞬时时间统一使用 `timestamp with time zone`（`timestamptz`），以 UTC 保存；展示时按用户、工厂或设备时区转换。

当前执行优先级由 [`09-Industrial Platform开发总TodoList.md`](09-Industrial%20Platform开发总TodoList.md) 的 PF/MES 阶段编号维护，不再由文档文件编号推断。当前状态为：`BuildingBlocks/可运行基线/统一前端（已完成） → PF-00 Identity（当前范围已完成） → PF-01（已完成） → PF-02 SystemData（2026-09-04：已知整改缺陷复验关闭，26项分层证据与017回写完成，完整UI/200%/真实链与外部门禁仍待验收，013/PF-02保持active） → PF-03 ReferenceData（未启动） → 后续 PF/MES`。架构收敛整改不新增 PF 编号或子计划；PF-03 仅在用户明确启动后进入。

平台基础层当前七个 Service Host、内部模块边界和阶段映射统一读取 [`32-Industrial Platform Service Host与内部模块边界.md`](32-Industrial%20Platform%20Service%20Host与内部模块边界.md)。`Service Host != Domain Module != Initialization Unit != Deployment Unit`；旧文档中的独立 Service 名称在冲突时只表示未来拆分目标。服务初始化统一读取 [`33-Industrial Platform SystemData数据库编排与环境引导.md`](33-Industrial%20Platform%20SystemData数据库编排与环境引导.md)：SystemData 负责 Topology、Orchestration、Policy、Observation，各服务负责 Migration、Seed、Bootstrap、Verify、Ledger，runtime readiness 只取本地数据库事实。

ReferenceData 当前规划为一个 Service Host 和七个逻辑模块（原五模块加 StateMachine、UnitOfMeasure），代码仍是骨架。固定使用一个 `referencedata_db`、一个 `reference_data` Schema、模块表前缀、一个服务级 Migration/Ledger、一个带 `ModuleKey` 的服务级 Outbox 与共享基础设施；只有出现真实入站事件消费者时才增加服务级 Inbox/Checkpoint，只有形成独立持久化生命周期时才拆分初始化单元。测试项目与门禁统一读取蓝图 29：常规测试按服务/部署角色收敛，真实基础设施进入统一 IntegrationTests，发布验证分别覆盖 Gateway 分布式入口和 UnifiedHost 统一入口。

ReferenceData 管理字典、参数、动态配置、元数据、编码规则、状态机定义及通用计量单位/换算；SystemData 管理行政组织、岗位、菜单导航、功能开关、服务目录和主题默认值；MasterData 管理物料、设备、制造组织、仓库、库位、BOM 与物料专属单位比例；OperationalData 管理库存批次、余额、预留和仓储业务单据。业务当前状态、权限/前置条件、事务与状态历史仍归业务服务；发布新参考定义不自动迁移在途业务。PF-03 当前方案为实施 06 V2.7，仍未启动。

| 编号 | 文档 | 状态 | 前置阅读 | 说明 |
| --- | --- | --- | --- | --- |
| 01 | [总体架构设计](01-Industrial%20Platform%20总体架构设计%20V1.0.md) | 已整理 | — | 规划 |
| 02 | [Server Monitor 历史独立产品设计](02-Server%20Monitor%20独立产品设计文档%20V1.0.md) | 历史规划 | 01, 32 | 仅作为 Operations Center 的 ServerMonitor 模块详细设计输入，部署边界以 32 为准 |
| 03 | [MES领域模型DDD设计](03-MES领域模型DDD设计.md) | 已整理 | 01 | 规划 |
| 04 | [Vue3 PC/PDA/Mobile 三端统一架构设计](04-Vue3%20PCPDAMobile%20三端统一架构设计.md) | 已整理 | 01 | 规划 |
| 05 | [平台基础功能与独立模块设计](05-Industrial%20Platform平台基础功能与独立模块设计.md) | 已确认 | 01, 04, 13, 21, 22, 30, 31 | 正式平台基础蓝图 |
| 06 | [微服务解决方案目录设计](06-Industrial%20Platform%20微服务解决方案目录设计.md) | 已整理 | 01 | 规划 |
| 07 | [PostgreSQL数据库规范及分库设计](07-PostgreSQL数据库规范及分库设计.md) | 已整理 | 01 | 规划 |
| 08 | [RabbitMQ事件总线设计规范](08-RabbitMQ事件总线设计规范.md) | 已整理 | 01 | 规划 |
| 09 | [Industrial Platform开发总TodoList](09-Industrial%20Platform开发总TodoList.md) | 持续维护 | 01, 05 | PF/MES 阶段编排、独立会话与门禁 |
| 10 | [Codex协作开发规范](10-Codex协作开发规范.md) | 持续维护 | 01 | 协作边界与派遣流程 |
| 11 | [代码初始化设计](11-Industrial%20Platform代码初始化设计.md) | 已整理 | 01, 06 | 规划 |
| 12 | [.NET10 Clean Architecture模板设计](12-.NET10%20Clean%20Architecture模板设计.md) | 已整理 | 11 | 规划 |
| 13 | [Identity Service详细设计](13-Identity%20Service详细设计.md) | 已整理 | 01, 11 | 规划 |
| 14 | [MasterData Service详细设计](14-MasterData%20Service详细设计.md) | 已整理 | 01, 13 | 后续阶段 |
| 14A | [OperationalData Service详细设计](14A-OperationalData%20Service详细设计.md) | 已确认 | 01, 07, 08, 14 | 操作域与轻量 WMS |
| 15 | [WorkOrder Service详细设计](15-WorkOrder%20Service详细设计.md) | 已整理 | 01, 14, 14A | 规划 |
| 16 | [Weighting Service详细设计](16-Weighting%20Service详细设计.md) | 已整理 | 01, 14, 14A | 规划 |
| 17 | [IoT Collector Service详细设计](17-IoT%20Collector%20Service详细设计.md) | 已整理 | 01 | 规划 |
| 18 | [Trace Service详细设计](18-Trace%20Service详细设计.md) | 已整理 | 01, 14, 14A | 规划 |
| 19 | [Batch Record Service详细设计](19-Batch%20Record%20Service详细设计.md) | 已整理 | 01, 14, 14A | 规划 |
| 20 | [部署架构设计](20-Industrial%20Platform部署架构设计.md) | 已整理 | 01 | 规划 |
| 21 | [低代码配置平台设计](21-低代码配置平台设计.md) | 已整理 | 01 | 规划 |
| 22 | [工业数据分析平台设计](22-工业数据分析平台设计.md) | 已整理 | 01 | 规划 |
| 23 | [多租户SaaS架构设计](23-多租户SaaS架构设计.md) | 已整理 | 01 | 规划 |
| 24 | [工业AI助手设计](24-工业AI助手设计.md) | 已整理 | 01 | 规划 |
| 25 | [完整技术白皮书](25-Industrial%20Platform完整技术白皮书.md) | 已整理 | 01–24 | 规划 |
| 26 | [数据库最终模型](26-Industrial%20Platform数据库最终模型.md) | 已整理 | 07, 14 | 规划 |
| 27 | [API规范](27-Industrial%20Platform%20API规范.md) | 已整理 | 01, 12 | 规划 |
| 28 | [前端工程规范](28-Industrial%20Platform前端工程规范.md) | 已整理 | 01, 04 | 规划 |
| 29 | [自动化测试体系](29-Industrial%20Platform自动化测试体系.md) | 已确认 V1.1 | 01, 12 | 当前测试分层与门禁母版 |
| 30 | [日志审计与可观测性平台设计](30-Industrial%20Platform日志审计与可观测性平台设计.md) | 已整理 | 01 | 规划 |
| 31 | [权限体系与安全架构设计](31-Industrial%20Platform权限体系与安全架构设计.md) | 已整理 | 01, 13 | 规划 |
| 32 | [Service Host 与内部模块边界](32-Industrial%20Platform%20Service%20Host与内部模块边界.md) | 已确认 V1.3 | 01, 05, 09 | Service Host、模块、初始化单元、部署角色权威母版 |
| 33 | [SystemData 服务初始化编排与环境引导](33-Industrial%20Platform%20SystemData数据库编排与环境引导.md) | 已确认 V3.2 | 05, 06, 07, 20, 27, 30, 31, 32 | SystemData 控制面与服务初始化所有权母版；V3.2 只增补 PF-03 模块摘要，不改变 V3.1 初始化协议 |
| 后续设计 | [后续设计](后续设计.md) | 路线参考 | 01–33 | 后续章节生成提示词与路线参考 |
