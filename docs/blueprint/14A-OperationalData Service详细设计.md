# 14A-OperationalData Service详细设计

版本：v1.0
服务名称：OperationalData Service
定位：操作数据域、库存与仓储业务单据中心
架构：DDD + Clean Architecture + Event Driven

---

# 1. 服务定位

OperationalData 是 Industrial Platform 中负责运行期库存事实和仓储业务操作的独立微服务。

它统一管理：

- 库存余额与库存预留
- 库存批次、容器、仓库、库位和库存状态
- 收料、领料、退料、生产入库、调拨、盘点和库存调整单据
- 单据过账、冲销、库存流水和审计链
- MES 与外部 WMS 之间的统一接口和防腐层

客户没有 WMS 时，OperationalData 提供轻量 WMS 能力；客户已有 WMS 时，OperationalData 维持统一的 MES 业务接口和本地投影，不与 WMS 争夺库存权威。

---

# 2. 依赖位置

基础依赖顺序：

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

后续业务服务通过 API、契约和事件使用 OperationalData：

```text
WorkOrder / Weighting
          ↓
   OperationalData
          ↓
 Trace / BatchRecord
```

禁止任何服务直接读取或修改 `operationaldata_db`。

---

# 3. 与其他领域的边界

| 领域 | 负责 | 不负责 |
| --- | --- | --- |
| ReferenceData | 通用 UnitOfMeasure/换算和状态机定义 | 库存数量、单据过账、库存状态和业务执行历史 |
| MasterData | 物料、BOM、工厂、车间、仓库、库位、批次策略与物料专属单位规则 | 库存余额、库存批次实例、收发退单据 |
| OperationalData | 库存事实、库存批次、预留、仓储单据、库存流水、WMS 适配与业务执行状态 | 通用单位/换算、物料/BOM 定义、工单执行、生产批记录 |
| WorkOrder | 工单、执行 BOM、工序任务、领退料和产出需求 | 库存余额与库存批次修改 |
| Weighting | 称量任务、过程和结果 | 库存权威与仓储单据过账 |
| Trace | 批次谱系、去向和影响范围投影 | 库存权威与单据执行 |
| BatchRecord | 生产过程结果聚合、电子批记录和合规归档 | 库存批次余额和仓储操作 |

`InventoryLot` 与 `BatchRecord` 必须保持独立：

- `InventoryLot` 描述库存身份、位置、状态和数量。
- `BatchRecord` 描述生产批次的完整执行历史。
- 一个生产批次可映射多个库存批次，拆包、合包或换容器不改变生产批记录。

---

# 4. 服务内部结构

OperationalData 对外是一个独立部署的微服务，内部采用模块化单体：

```text
OperationalData
├── Inventory
├── Lots
├── Documents
├── WarehouseOperations
└── WmsIntegration
```

模块职责：

| 模块 | 职责 |
| --- | --- |
| Inventory | 余额、预留、可用量、冻结量、在途量和库存流水 |
| Lots | 库存批次、容器、批次状态、拆分与合并 |
| Documents | 单据头、单据行、状态机、过账和冲销 |
| WarehouseOperations | 收料、领料、退料、生产入库、调拨、盘点和调整用例 |
| WmsIntegration | 外部 WMS 命令、回执、库存投影、Inbox/Outbox 和幂等 |

模块之间通过应用用例或领域事件协作，不跨模块直接修改聚合内部状态。

---

# 5. Clean Architecture 项目结构

```text
src/Services/OperationalData
├── IndustrialPlatform.OperationalData.Api
├── IndustrialPlatform.OperationalData.Application
├── IndustrialPlatform.OperationalData.Contracts
├── IndustrialPlatform.OperationalData.Domain
└── IndustrialPlatform.OperationalData.Infrastructure

tests/Services/OperationalData
├── IndustrialPlatform.OperationalData.Domain.Tests
├── IndustrialPlatform.OperationalData.Application.Tests
├── IndustrialPlatform.OperationalData.Infrastructure.Tests
└── IndustrialPlatform.OperationalData.Contract.Tests
```

依赖方向：

```text
Api → Application → Domain
Infrastructure → Application + Domain
Contracts 独立保存外部稳定契约
```

---

# 6. InventoryBalance 库存余额

库存余额按以下维度唯一：

```text
TenantId
+ WarehouseId
+ LocationId
+ MaterialId
+ InventoryLotId
+ StockStatus
```

核心数量：

```csharp
decimal OnHandQuantity;
decimal ReservedQuantity;
decimal AvailableQuantity;
decimal FrozenQuantity;
decimal InTransitQuantity;
```

余额以物料发布的库存单位累计，并保存该单位的 `UnitDimensionNId`、`UnitNId`、`unitRevision`（整份 `UnitDimension.Revision`）、`sourceScope`、`sourceTenantNId`；精度、舍入和需要的同维度换算在过账时快照。`sourceTenantNId` 是定义来源而非业务对象自身 `TenantNId`：Platform 来源为空，Tenant 来源等于可信当前租户；通用换算仅允许同来源、同维度、同修订。任何输入数量先经 ReferenceData UnitOfMeasure 契约和物料已发布专属规则校验，跨物理维度不得由通用换算直接通过。

约束：

- `AvailableQuantity = OnHandQuantity - ReservedQuantity - FrozenQuantity`
- 默认禁止可用量小于零；需要允许负库存时必须按仓库显式配置并审计。
- 余额只能由单据过账和冲销更新，不提供通用余额覆盖接口。
- 使用乐观并发版本防止并发丢失更新。

---

# 7. InventoryLot 库存批次

库存批次保存：

```csharp
Guid Id;
Guid MaterialId;
string LotNumber;
string? SupplierLotNumber;
string? ProductionBatchNumber;
DateTimeOffset? ManufactureTime;
DateTimeOffset? ExpireTime;
InventoryLotStatus Status; // OperationalData 自有业务状态
```

支持：

- 收料时创建供应商库存批次
- 生产入库时创建或关联生产库存批次
- 批次冻结、解冻、放行和报废
- 容器、托盘、最小包装和序列号关联
- 批次拆分、合并、换容器和库位移动

批次号必须在租户、物料和批次策略定义的范围内保持唯一。

---

# 8. StockReservation 库存预留

WorkOrder 的物料需求通过预留锁定可用库存：

```text
Requested → Reserved → PartiallyConsumed → Consumed
          ↘ Released
```

预留规则：

- 预留必须引用工单、执行 BOM 行或其他明确需求来源。
- 建立预留时校验可用量，不直接减少在手量。
- 实际领料过账时核销预留并减少在手量。
- 工单取消、需求减少或超时释放剩余预留。
- 批次选择支持先进先出、先到期先出和指定批次策略。

---

# 9. InventoryDocument 业务单据

统一单据模型包含：

```text
InventoryDocument
├── DocumentNumber
├── DocumentType
├── Status
├── SourceType / SourceId
├── WarehouseId
├── ExternalWmsId
├── IdempotencyKey
└── Lines[]
```

单据类型：

| 类型 | 用途 |
| --- | --- |
| Receipt | 收料、采购或外部入库 |
| MaterialIssue | 生产领料或仓库出库 |
| MaterialReturn | 生产退料或退库 |
| ProductionReceipt | 半成品或产成品入库 |
| Transfer | 跨仓库、库位或车间调拨 |
| Stocktake | 盘点 |
| Adjustment | 经授权的库存调整 |

---

# 10. 单据状态机

```text
Draft → Confirmed → Posting → Posted
                 ↘ Rejected
Draft/Confirmed → Cancelled
```

规则：

- `Draft` 可以编辑。
- `Confirmed` 完成业务校验并等待过账。
- `Posting` 表示正在本地提交或等待外部 WMS 回执。
- `Posted` 已生成库存流水，不可直接编辑。
- `Rejected` 保存拒绝原因，可修正后重新确认或取消。
- 已过账单据只能使用冲销或反向单据纠错。
- 此状态机是 OperationalData 业务执行规则；ReferenceData StateMachine 定义不能替代单据的库存校验、过账事务、状态写入或历史。

---

# 11. StockTransaction 库存流水

每次过账生成不可变库存流水：

```csharp
Guid Id;
Guid DocumentId;
Guid DocumentLineId;
Guid MaterialId;
Guid InventoryLotId;
Guid WarehouseId;
Guid? FromLocationId;
Guid? ToLocationId;
decimal Quantity;
decimal QuantityBefore;
decimal QuantityAfter;
string UnitDimensionNId;
string UnitNId;
int UnitRevision;
string SourceScope;
string? SourceTenantNId;
decimal? ConversionFactorSnapshot;
decimal? OffsetSnapshot;
int DecimalPlacesSnapshot;
string RoundingModeSnapshot;
DateTimeOffset BusinessTime;
DateTimeOffset ReceivedTime;
string IdempotencyKey;
```

库存流水用于：

- 重建余额
- 审计业务操作
- 向 Trace 提供批次移动事实
- 向 BatchRecord 提供领料、退料和产出证据
- 与外部 WMS 对账

---

# 12. 收料流程

```text
创建 Receipt
→ 校验物料、仓库、库位和批次策略
→ 创建或关联 InventoryLot
→ Confirmed
→ 过账
→ 增加库存余额并生成 StockTransaction
→ 发布 MaterialReceived / InventoryLotCreated
```

需要质量检验时，库存进入 `Inspection` 或 `Frozen` 状态，不直接计入可用量。

---

# 13. 生产领料流程

```text
WorkOrder 提交物料需求
→ 建立 StockReservation
→ 创建 MaterialIssue
→ 校验批次和可用量
→ 过账并核销预留
→ 发布 MaterialIssued
→ WorkOrder、Trace、BatchRecord 更新各自状态或投影
```

OperationalData 维护库存事实；WorkOrder 只保留业务请求及执行结果引用。

---

# 14. 生产退料流程

```text
WorkOrder 提交剩余物料退料请求
→ 校验原领料单、物料和库存批次
→ 创建 MaterialReturn
→ 过账恢复库存或创建新的库存批次实例
→ 发布 MaterialReturned
```

退料必须保留与原领料、工单和生产批次的关联。

---

# 15. 生产入库流程

```text
WorkOrder 提交产出
→ 创建 ProductionReceipt
→ 创建或关联 InventoryLot
→ 过账增加库存
→ 发布 ProductionReceived
→ Trace 建立投入与产出谱系
→ BatchRecord 归档执行证据
```

产成品未放行时进入冻结或待检状态。

---

# 16. 调拨、盘点与调整

## Transfer

同仓库库位移动可以一次过账；跨仓库调拨需要出库与入库两个阶段，并使用在途量。

## Stocktake

盘点记录账面数量、实盘数量和差异。盘点本身不直接改库存，审核后生成 Adjustment。

## Adjustment

库存调整必须记录原因、授权人、证据和关联盘点单；禁止使用 Adjustment 代替正常收发退业务。

---

# 17. 库存权威模式

库存权威按仓库配置，单个仓库只能选择一种模式。

## Internal

OperationalData 是权威源：

- 本地完成校验、过账、余额和流水
- 支持仓库、库位、库存批次、收发退、调拨和盘点
- 提供客户未部署 WMS 时的轻量 WMS 能力

## ExternalWms

外部 WMS 是权威源：

- OperationalData 接收 MES 侧业务请求
- WmsIntegration 发送带幂等键的命令
- 单据保持 `Posting` 直到 WMS 明确确认或拒绝
- WMS 确认后更新本地投影并发布统一平台事件
- 超时不推断成功，只允许安全重试、查询或人工确认

禁止 OperationalData 与 WMS 同时独立修改同一仓库的权威库存。

---

# 18. API 设计

基础前缀：

```text
/api/operational-data
```

主要能力：

```text
GET  /inventory
GET  /inventory/lots
POST /reservations
POST /reservations/{id}/release
POST /documents
POST /documents/{id}/confirm
POST /documents/{id}/post
POST /documents/{id}/reverse
GET  /documents/{id}
POST /wms/callbacks
```

所有写操作必须支持幂等键；外部调用不得直接传入新的库存余额。

---

# 19. 数据库设计

数据库：

```text
operationaldata_db
```

核心表：

```text
inventory_balance
inventory_lot
inventory_container
stock_reservation
inventory_document
inventory_document_line
stock_transaction
wms_request
wms_callback
outbox_message
inbox_message
```

时间列统一使用 `timestamptz`；领域、DTO、契约和事件时间统一使用 `DateTimeOffset`。

---

# 20. 集成事件

OperationalData 发布：

```text
InventoryReserved
InventoryReservationReleased
MaterialReceived
MaterialIssued
MaterialReturned
ProductionReceived
InventoryTransferred
InventoryAdjusted
InventoryLotCreated
InventoryLotStatusChanged
```

事件至少包含：业务租户、工厂、仓库、物料、库存批次、业务单据、数量、`UnitDimensionNId`、`UnitNId`、`unitRevision`、`sourceScope`、`sourceTenantNId`、业务时间和幂等标识；必要时还携带过账换算与舍入快照。ReferenceData 定义后续停用或修订不影响历史事件解释。

---

# 21. 一致性与失败处理

- 单据、库存流水、余额和 Outbox 在同一本地事务提交。
- 消费事件通过 Inbox 去重。
- 库存余额和预留使用乐观并发控制。
- 跨服务通过事件最终一致，不使用分布式事务。
- WMS 失败保留请求、回执和重试轨迹。
- 重复 API、事件或 WMS 回执不得重复过账。
- 冲销生成相反流水，不删除原始记录。

---

# 22. 安全与审计

- 查询库存需要对应工厂和仓库的数据权限。
- 过账、冲销、盘点审核和库存调整使用独立权限。
- 库存调整必须执行更严格的授权和审计。
- 所有单据保存创建人、确认人、过账人和业务时间。
- 对外 WMS 凭据只保存在安全配置中，不写入业务表和日志。

---

# 23. 测试体系

领域测试：

- 单据状态机
- 可用量与预留核销
- 批次状态和冻结规则
- 负库存限制
- 过账、冲销和幂等

应用与基础设施测试：

- 收料、领料、退料、生产入库、调拨和盘点完整用例
- 单据、流水、余额和 Outbox 原子提交
- 并发库存冲突
- Inbox/Outbox 去重与重试
- `Internal` 与 `ExternalWms` 双模式
- WorkOrder、Trace、BatchRecord 和 WMS 契约兼容性

---

# 24. 开发顺序

```text
服务骨架与边界
→ 库存批次与余额
→ 单据与状态机
→ 预留
→ 收料/领料/退料/生产入库
→ 调拨/盘点/调整
→ 事件与 Trace/BatchRecord 集成
→ WMS 适配器
```

总体阶段、恢复门禁和结果索引统一维护在 `09-Industrial Platform开发总TodoList.md`；OperationalData 的详细任务继续维护在重编号后的实施文档 16。

---

# 25. 完成标准

- OperationalData 是一个可独立部署的微服务。
- MasterData、WorkOrder、Trace 和 BatchRecord 不再承担库存权威职责。
- 所有库存变化由单据过账或冲销产生，并有不可变库存流水。
- 单个仓库只存在一个库存权威源。
- 无 WMS 时可完成收、发、退、调、盘和生产入库闭环。
- 有 WMS 时可通过统一适配器完成命令、回执、投影和事件发布。
- 库存、批次、单据、并发、幂等和双模式测试全部通过。
