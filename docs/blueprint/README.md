# Industrial Platform 蓝图文档索引

本目录保存原始蓝图的仓库内副本。当前里程碑仅整理、归档与对齐依赖顺序；不实现业务能力。

基础服务的实现依赖顺序为：`BuildingBlocks → Identity → ReferenceData → MasterData`。ReferenceData 管理字典、配置、元数据和编码规则；MasterData 管理物料、设备、组织和 BOM 等业务主数据。

| 编号 | 文档 | 状态 | 前置阅读 | 说明 |
| --- | --- | --- | --- | --- |
| 01 | [总体架构设计](01-Industrial%20Platform%20总体架构设计%20V1.0.md) | 已整理 | — | 规划 |
| 02 | [Server Monitor 独立产品设计](02-Server%20Monitor%20独立产品设计文档%20V1.0.md) | 已整理 | 01 | 规划 |
| 03 | [MES领域模型DDD设计](03-MES领域模型DDD设计.md) | 已整理 | 01 | 规划 |
| 04 | [Vue3 PC/PDA/Mobile 三端统一架构设计](04-Vue3%20PCPDAMobile%20三端统一架构设计.md) | 已整理 | 01 | 规划 |
| 05 | [个人MES平台两年开发路线](05-个人MES平台两年开发路线.md) | 已整理 | 01–04 | 规划 |
| 06 | [微服务解决方案目录设计](06-Industrial%20Platform%20微服务解决方案目录设计.md) | 已整理 | 01, 05 | 规划 |
| 07 | [PostgreSQL数据库规范及分库设计](07-PostgreSQL数据库规范及分库设计.md) | 已整理 | 01 | 规划 |
| 08 | [RabbitMQ事件总线设计规范](08-RabbitMQ事件总线设计规范.md) | 已整理 | 01 | 规划 |
| 09 | [MES MVP第一阶段开发TodoList](09-MES%20MVP第一阶段开发TodoList.md) | 已整理 | 01, 05 | 规划 |
| 10 | [Codex协作开发规范](10-Codex协作开发规范.md) | 已整理 | 01 | 规划 |
| 11 | [代码初始化设计](11-Industrial%20Platform代码初始化设计.md) | 已整理 | 01, 06 | 规划 |
| 12 | [.NET10 Clean Architecture模板设计](12-.NET10%20Clean%20Architecture模板设计.md) | 已整理 | 11 | 规划 |
| 13 | [Identity Service详细设计](13-Identity%20Service详细设计.md) | 已整理 | 01, 11 | 规划 |
| 14 | [MasterData Service详细设计](14-MasterData%20Service详细设计.md) | 已整理 | 01, 13 | 后续阶段 |
| 15 | [WorkOrder Service详细设计](15-WorkOrder%20Service详细设计.md) | 已整理 | 01, 14 | 规划 |
| 16 | [Weighting Service详细设计](16-Weighting%20Service详细设计.md) | 已整理 | 01, 14 | 规划 |
| 17 | [IoT Collector Service详细设计](17-IoT%20Collector%20Service详细设计.md) | 已整理 | 01 | 规划 |
| 18 | [Trace Service详细设计](18-Trace%20Service详细设计.md) | 已整理 | 01, 14 | 规划 |
| 19 | [Batch Record Service详细设计](19-Batch%20Record%20Service详细设计.md) | 已整理 | 01, 14 | 规划 |
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
