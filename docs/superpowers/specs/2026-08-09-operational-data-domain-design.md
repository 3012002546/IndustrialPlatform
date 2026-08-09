# OperationalData 操作域设计

## 1. 目标

在 Industrial Platform 中新增单一独立微服务 `OperationalData`，统一承接库存、库存批次和仓储业务单据。该服务既可作为 MES 内置的轻量 WMS 使用，也可在客户已有 WMS 时作为 MES 侧统一业务接口和防腐层。

本设计同时移除与开发 TodoList 重复的 `05-个人MES平台两年开发路线.md`，并修订所有受影响的蓝图、实施阶段和任务依赖。

## 2. 核心解决方案结构

当前核心解决方案统一为：

```text
IndustrialPlatform.slnx
├── BuildingBlocks
├── Identity
├── ReferenceData
├── MasterData
├── OperationalData
├── WorkOrder
├── Weighting
├── IoTCollector
├── Trace
└── BatchRecord
```

基础依赖顺序为：

```text
BuildingBlocks
    ↓
Identity
    ↓
ReferenceData
    ↓
MasterData
    ↓
OperationalData
```

业务服务不得直接引用其他服务的数据库或基础设施项目。跨服务协作通过 API、契约和集成事件完成。

## 3. 服务定位

`OperationalData` 是事务型操作数据域，不是主数据服务，也不是生产批记录服务。

它负责把仓储业务单据过账为不可丢失、可审计的库存事实，并向 WorkOrder、Trace、BatchRecord 和外部 WMS 提供统一交互边界。

服务内部采用模块化单体结构，对外仍是一个可独立部署的微服务：

```text
OperationalData
├── Inventory
├── Lots
├── Documents
├── WarehouseOperations
└── WmsIntegration
```

模块之间只能通过应用层用例或领域事件协作，不跨模块直接修改聚合内部状态。

## 4. 领域边界

### 4.1 MasterData

负责相对稳定的定义：

- 物料、物料分类、计量单位和批次管理策略
- BOM、工艺路线和工序
- 工厂、车间、仓库和库位
- 设备、组织和资源

不负责库存余额、库存批次实例、收发退单据或库存流水。

### 4.2 OperationalData

负责运行期操作事实：

- 库存余额、可用量、预留量、冻结量和在途量
- 库存批次、容器、库存状态和库位占用
- 收料、领料、退料、生产入库、调拨、盘点和库存调整
- 单据状态、审批或确认结果、过账记录和库存流水
- 内置库存模式与外部 WMS 模式的统一适配

### 4.3 WorkOrder

负责生产工单、执行 BOM、工序任务和生产需求。它可以提出领料、退料和产成品入库请求，但不维护库存余额，也不直接修改库存批次。

### 4.4 Trace

消费库存移动、批次拆分合并、投料和产出事件，建立物料谱系、去向和影响范围投影。Trace 不作为库存权威源。

### 4.5 BatchRecord

负责生产过程结果聚合、电子批记录、合规归档、审核和放行支持。库存批次 `InventoryLot` 与生产批记录 `BatchRecord` 是不同概念：前者描述库存身份与数量，后者描述一次生产批次的完整执行历史。

## 5. 核心领域模型

### 5.1 InventoryBalance

库存余额按以下关键维度汇总：

```text
Tenant + Warehouse + Location + Material + InventoryLot + StockStatus
```

核心数量：

- `OnHandQuantity`
- `ReservedQuantity`
- `AvailableQuantity`
- `FrozenQuantity`
- `InTransitQuantity`

余额是库存流水的结果，不允许通过通用 CRUD 接口任意覆盖。

### 5.2 InventoryLot

库存批次保存：

- 内部批次号
- 供应商批次号或生产批次号
- 物料、生产日期、失效日期和质量状态
- 当前仓库、库位、容器和库存状态
- 来源单据及外部 WMS 标识

一个生产批次可以映射一个或多个库存批次；拆包、合包、换容器和跨库位移动不改变生产批记录本身。

### 5.3 InventoryDocument

统一单据头和单据行模型，按业务类型区分：

- `Receipt`：收料或外部入库
- `MaterialIssue`：生产领料或出库
- `MaterialReturn`：生产退料或退库
- `ProductionReceipt`：产成品或半成品入库
- `Transfer`：仓库、库位或车间间调拨
- `Stocktake`：盘点
- `Adjustment`：经授权的库存调整

单据状态机统一为：

```text
Draft → Confirmed → Posting → Posted
                 ↘ Rejected
Draft/Confirmed → Cancelled
```

已过账单据不可直接编辑；纠错使用冲销或反向单据，并保留完整审计链。

### 5.4 StockTransaction

每次过账生成不可变库存流水，至少记录：

- 业务单据和单据行
- 物料、批次、仓库、库位和容器
- 方向、数量和计量单位
- 过账前后数量
- 操作人、业务时间和系统接收时间
- 外部 WMS 关联号和幂等键

所有 .NET 时间值使用 `DateTimeOffset`，PostgreSQL 使用 `timestamptz`。

### 5.5 StockReservation

WorkOrder 的领料需求先建立预留，再由实际领料单核销。预留不得使 `AvailableQuantity` 低于零；取消工单或减少需求时释放剩余预留。

## 6. 库存权威模式

库存权威按仓库配置，单个仓库只能选择一种模式：

### 6.1 Internal

`OperationalData` 是库存权威源，负责校验、过账、余额和流水。该模式提供轻量 WMS 能力，适用于客户未部署 WMS 的场景。

### 6.2 ExternalWms

外部 WMS 是该仓库的库存权威源。`OperationalData`：

- 接收 MES 侧领料、退料和入库需求
- 通过 WMS 适配器发送带幂等键的业务命令
- 接收 WMS 确认、拒绝和库存变化事件
- 维护 MES 侧库存投影、单据状态和关联关系
- 向 WorkOrder、Trace 和 BatchRecord 发布统一平台事件

在收到 WMS 成功确认前，不把请求标记为已过账。禁止 OperationalData 与 WMS 同时独立修改同一仓库的权威库存。

## 7. 关键业务流程

### 7.1 生产领料

```text
WorkOrder 产生物料需求
→ OperationalData 建立库存预留
→ 创建并确认 MaterialIssue
→ Internal 模式直接过账；ExternalWms 模式等待 WMS 确认
→ 生成 StockTransaction 并核销预留
→ 发布 MaterialIssued 事件
→ WorkOrder、Trace、BatchRecord 分别更新自身状态或投影
```

### 7.2 生产退料

```text
WorkOrder 提交剩余物料退料请求
→ OperationalData 校验原领料关系和批次
→ MaterialReturn 过账
→ 恢复或新建对应库存批次余额
→ 发布 MaterialReturned 事件
```

### 7.3 生产入库

```text
WorkOrder 完成产出
→ 提交 ProductionReceipt
→ OperationalData 创建或关联 InventoryLot
→ 单据过账并增加库存
→ 发布 ProductionReceived 事件
→ Trace 建立投入批次与产出批次谱系
→ BatchRecord 归档对应执行证据
```

### 7.4 收料与外部入库

`Receipt` 可以引用采购单、ASN、委外单或手工授权来源。过账时创建库存批次和库存余额；需要质量检验时先进入冻结或待检状态，不直接进入可用库存。

## 8. 一致性、失败与审计

- 使用乐观并发控制库存余额与预留，冲突时重新读取并重试业务操作。
- API、消息和 WMS 回执都必须携带幂等键，重复请求不得重复过账。
- 单据过账、库存流水、余额变更和 Outbox 事件在同一本地事务提交。
- 跨服务不使用分布式事务；通过 Outbox、Inbox、重试和补偿单据保证最终一致性。
- WMS 超时保持单据在 `Posting`，允许安全重试或人工确认，不推断成功。
- 所有库存调整必须记录原因、授权人和审计信息。

## 9. 主要集成事件

OperationalData 至少发布：

- `InventoryReserved`
- `InventoryReservationReleased`
- `MaterialReceived`
- `MaterialIssued`
- `MaterialReturned`
- `ProductionReceived`
- `InventoryTransferred`
- `InventoryAdjusted`
- `InventoryLotCreated`
- `InventoryLotStatusChanged`

事件使用稳定契约，包含租户、工厂、仓库、物料、批次、单据、数量、`DateTimeOffset` 业务时间和幂等标识。

## 10. 验证策略

- 领域测试：状态机、可用量、预留核销、批次状态、负库存限制和冲销规则。
- 应用测试：收料、领料、退料、生产入库、调拨和盘点的完整用例。
- 持久化测试：单据、流水、余额和 Outbox 的原子提交及并发冲突。
- 契约测试：WorkOrder、Trace、BatchRecord 和 WMS 适配器消息兼容性。
- 模式测试：同一业务分别验证 `Internal` 与 `ExternalWms`，确认一个仓库只有一个权威源。

## 11. 蓝图迁移

### 11.1 新增文档

新增 `docs/blueprint/14A-OperationalData Service详细设计.md`，放在 MasterData 与 WorkOrder 详细设计之间，不重命名 06–31，避免破坏已有链接和历史引用。

### 11.2 删除文档

删除 `docs/blueprint/05-个人MES平台两年开发路线.md`。README 中删除 05 索引，并将原本依赖 05 的文档改为依赖总体架构和开发 TodoList 所需的直接前置文档。

### 11.3 一致性调整范围

至少调整以下内容：

- 总体架构、DDD 服务边界和微服务目录
- PostgreSQL 分库设计、RabbitMQ 事件和最终数据库模型
- MVP TodoList 的 Sprint 顺序和可派遣任务
- 代码初始化、解决方案结构、部署结构和测试体系
- MasterData、WorkOrder、Weighting、Trace、BatchRecord 的服务边界
- API 规范、完整技术白皮书和开发启动实施方案

现有 `Material.Service` 与 `MaterialRuntime.Service` 的职责迁移到 `MasterData` 和 `OperationalData`：物料、BOM 等定义归 MasterData；库存批次、容器、库存和业务单据归 OperationalData。

## 12. 非目标

- 本阶段不实现完整采购、销售、财务、计费或运输管理。
- 本阶段不把 OperationalData 拆为多个部署单元。
- 本阶段不让 OperationalData 直接访问外部 WMS 数据库。
- 本阶段不合并 InventoryLot、Trace 谱系和 BatchRecord 生产批记录。
- 当前协调任务只修改蓝图和开发 TodoList，不执行 OperationalData 代码开发。
