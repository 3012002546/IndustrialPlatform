# Industrial Platform 蓝图文档索引

本目录是 Industrial Platform 蓝图设计的唯一维护源。外层历史资料不再维护；所有新增设计、设计修订和实现反馈都必须回写到本目录。

当前协作任务只负责蓝图维护、开发 TODO 拆分、任务派遣和结果回写，不在本任务直接开发业务代码。实际开发与测试由其他任务或被派遣的协作者执行；实现过程中发现设计冲突时，先回到本目录确认并修订设计。

## 全局时间规范

- .NET 时间值统一使用 `DateTimeOffset`，可空时间使用 `DateTimeOffset?`；禁止在新增设计中使用 `DateTime` 作为业务时间类型。
- 获取 UTC 当前时间统一使用 `DateTimeOffset.UtcNow`。
- API 使用包含 `Z` 或明确偏移量的 ISO 8601 / RFC 3339 时间字符串。
- PostgreSQL 持久化瞬时时间统一使用 `timestamp with time zone`（`timestamptz`），以 UTC 保存；展示时按用户、工厂或设备时区转换。

基础服务的实现依赖顺序为：`BuildingBlocks → Identity → ReferenceData → MasterData → OperationalData`。ReferenceData 管理字典、配置、元数据和编码规则；MasterData 管理物料、设备、组织、仓库、库位和 BOM 等稳定主数据；OperationalData 管理库存批次、余额、预留和仓储业务单据。

| 编号 | 文档 | 状态 | 前置阅读 | 说明 |
| --- | --- | --- | --- | --- |
| 01 | [总体架构设计](01-Industrial%20Platform%20总体架构设计%20V1.0.md) | 已整理 | — | 规划 |
| 02 | [Server Monitor 独立产品设计](02-Server%20Monitor%20独立产品设计文档%20V1.0.md) | 已整理 | 01 | 规划 |
| 03 | [MES领域模型DDD设计](03-MES领域模型DDD设计.md) | 已整理 | 01 | 规划 |
| 04 | [Vue3 PC/PDA/Mobile 三端统一架构设计](04-Vue3%20PCPDAMobile%20三端统一架构设计.md) | 已整理 | 01 | 规划 |
| 06 | [微服务解决方案目录设计](06-Industrial%20Platform%20微服务解决方案目录设计.md) | 已整理 | 01 | 规划 |
| 07 | [PostgreSQL数据库规范及分库设计](07-PostgreSQL数据库规范及分库设计.md) | 已整理 | 01 | 规划 |
| 08 | [RabbitMQ事件总线设计规范](08-RabbitMQ事件总线设计规范.md) | 已整理 | 01 | 规划 |
| 09 | [MES MVP第一阶段开发TodoList](09-MES%20MVP第一阶段开发TodoList.md) | 持续维护 | 01, 03, 14, 14A | TODO 拆分与派遣 |
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
| 29 | [自动化测试体系](29-Industrial%20Platform自动化测试体系.md) | 已整理 | 01, 12 | 规划 |
| 30 | [日志审计与可观测性平台设计](30-Industrial%20Platform日志审计与可观测性平台设计.md) | 已整理 | 01 | 规划 |
| 31 | [权限体系与安全架构设计](31-Industrial%20Platform权限体系与安全架构设计.md) | 已整理 | 01, 13 | 规划 |
| 后续设计 | [后续设计](后续设计.md) | 路线参考 | 01–31 | 后续章节生成提示词与路线参考 |
