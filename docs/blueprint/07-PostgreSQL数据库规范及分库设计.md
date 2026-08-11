# 07-PostgreSQL数据库规范及分库设计

版本：v1.0
项目名称：Industrial Platform
数据库架构：PostgreSQL + TimescaleDB
ORM：SqlSugar
设计思想：领域数据库 + 工业数据平台 + 数据生命周期管理

---

# 1. 数据库设计目标

Industrial Platform 不是传统 MES 单数据库模式。

传统 MES：

```
MES Database

├── 用户
├── 物料
├── 工单
├── 设备
├── 质量
├── 报表
├── 日志
└── 数据采集
```

随着系统运行：

问题：

* 表数量越来越多
* 业务耦合
* 查询越来越慢
* 历史数据无法清理
* 审计数据污染业务表
* 设备数据无法承载

Industrial Platform：

采用：

```
业务数据
    |
    |
领域数据库

+
工业数据数据库

+
审计数据库

+
文件对象存储

+
数据仓库
```

---

# 2. 数据库总体规划

下列数据库名均为服务的逻辑身份，不是固定的物理部署拓扑。Test、Staging 和 Production 必须采用 `PerService` 物理拓扑；Development 默认可把这些逻辑身份映射到一个 Shared 目标（例如 `industrial_platform_dev`），而不改变服务边界。

推荐：

## PostgreSQL Cluster

```
PostgreSQL Server


├── identity_db

├── permission_db

├── referencedata_db

├── masterdata_db

├── mes_db

├── quality_db

├── trace_db

├── batch_db

├── monitor_db

├── audit_db

└── report_db

```

---

## TimescaleDB

独立：

```
industrial_timeseries


├── equipment_metrics

├── sensor_data

├── energy_data

├── server_metrics

└── alarm_history

```

---

# 3. 数据库划分原则

遵循：

DDD Bounded Context。

例如：

## WorkOrder Service

负责：

```
work_order

work_order_item

work_order_status

```

不允许：

```
select equipment
from equipment
```

跨域访问：

通过：

* API
* Event

例如：

工单创建：

```
WorkOrder Service

发布事件


WorkOrderCreatedEvent


↓

Equipment Service

监听


↓

绑定设备

```

---

# 4. 数据库列表

此表列的是逻辑数据库身份；实际 `LogicalDatabaseName` 到 `PhysicalDatabaseName` 的映射、Shared/PerService 校验和环境规则以蓝图 33 为准。

| 服务         | 数据库           |
| ---------- | ------------- |
| Identity   | identity_db   |
| Permission | permission_db |
| ReferenceData | referencedata_db |
| MasterData | masterdata_db |
| OperationalData | operationaldata_db |
| Planning   | planning_db   |
| WorkOrder  | workorder_db  |
| Workflow   | workflow_db   |
| Weighting  | weighting_db  |
| Equipment  | equipment_db  |
| Quality    | quality_db    |
| Trace      | trace_db      |
| Batch      | batch_db      |
| Report     | report_db     |
| Monitor    | monitor_db    |
| Audit      | audit_db      |

---

# 5. PostgreSQL Schema设计

每个数据库：

采用 Schema。

例如：

workorder_db

```
workorder_db


├── public

├── history

├── audit

└── archive

```

---

## public

当前业务。

例如：

```
work_order

work_order_operation

```

---

## history

业务历史。

例如：

```
work_order_status_history

```

---

## audit

审计。

例如：

```
entity_change_log

```

---

## archive

归档。

例如：

```
work_order_2026

```

---

# 6. 通用字段规范

所有业务表必须包含：

```sql
id bigint primary key


created_time timestamptz

created_by varchar(50)


modified_time timestamptz

modified_by varchar(50)


is_deleted boolean


version int

```

---

示例：

```sql
create table work_order
(

id bigint primary key,


order_no varchar(50),


product_id bigint,


qty numeric(18,3),


status varchar(20),


created_time timestamptz,


created_by varchar(50),


modified_time timestamptz,


modified_by varchar(50),


is_deleted boolean default false,


version int default 1

);

```

---

# 7. ID生成策略

禁止：

```
identity auto increment
```

原因：

微服务环境无法保证。

推荐：

Snowflake。

格式：

```
时间
+
机器ID
+
序列号

```

例如：

```
182739182739182
```

.NET实现：

```
Industrial.Core.IdGenerator

```

---

# 8. 字段规范

## 字符类型

禁止：

```
text
```

普通字段：

```
varchar(n)
```

说明：

```
varchar(200)

```

备注：

```
varchar(1000)

```

---

# 数值

MES数量：

统一：

```
numeric(18,3)

```

例如：

重量：

```
25.325 kg

```

---

# 时间

统一：

```
timestamp with time zone
```

PostgreSQL 别名：`timestamptz`。

推荐：

服务器统一UTC。

显示：

根据Factory时区转换。

---

# 9. 枚举设计

禁止：

数据库：

```
1
2
3
```

推荐：

```
varchar
```

例如：

订单状态：

```
CREATE

RELEASE

RUNNING

COMPLETE

CANCEL

```

原因：

可读。

---

# 10. MES核心数据库设计

## 10.1 MasterData

```
masterdata_db


material

material_category


product


process_route


operation


factory


work_center


```

---

## 10.2 OperationalData

```text
operationaldata_db

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

`inventory_balance` 是库存流水的汇总结果；所有库存变化由单据过账或冲销生成。单个仓库只能由 OperationalData `Internal` 模式或外部 WMS 其中一方维护权威库存。

---

## 10.3 Planning

```
planning_db


production_plan


capacity_plan


shift_calendar


```

---

## 10.4 WorkOrder

```
workorder_db


work_order


work_order_operation


work_order_material


work_order_history

```

---

## 10.5 Weighting

称量独立。

```
weighting_db


weight_task


weight_record


weight_item


weight_device


weight_formula


weight_history

```

---

## 10.6 Trace

追溯。

核心：

```
trace_lot


trace_material_flow


trace_relation


```

关系：

```
原料批次

   |

生产批次

   |

成品批次

```

---

# 11. 工业时序数据库设计

使用：

TimescaleDB。

场景：

* PLC
* Sensor
* Server Monitor

---

## 11.1 数据表

例如：

equipment_metrics

```sql
create table equipment_metrics
(

time timestamptz,


equipment_id bigint,


tag varchar(100),


value numeric(18,5)


);

```

---

创建Hypertable：

```sql
SELECT create_hypertable(
'equipment_metrics',
'time'
);

```

---

# 12. 数据保留策略

工业数据量巨大。

例如：

服务器监控：

每10秒采集。

一天：

```
8640条/设备

```

100台：

```
864000

```

一年：

```
3亿+
```

必须生命周期管理。

---

# 13. 数据生命周期

设计：

```
实时数据

0-7天

Redis + Timescale


↓

热数据

7天-6个月

Timescale


↓

冷数据

6个月-3年

PostgreSQL Archive


↓

归档

3年以上

MinIO


```

---

# 14. 数据归档服务

独立：

```
DataArchive.Worker

```

职责：

```
扫描

↓

迁移

↓

压缩

↓

校验

↓

删除

```

例如：

```
equipment_metrics

2025年

↓

MinIO

```

---

# 15. 审计设计

独立：

audit_db

表：

```
audit_log

entity_change_log

login_log

operation_log

```

---

## entity_change_log

记录：

谁

什么时候

修改什么

例如：

```json
{
"table":"work_order",
"id":12345,
"before":
{
"qty":100
},

"after":
{
"qty":120
}

}

```

---

# 16. JSON字段使用规范

PostgreSQL优势：

jsonb。

适合：

* 扩展属性
* 配置
* 原始数据

例如：

设备参数：

```sql
extra jsonb

```

内容：

```json
{
 "ip":"192.168.1.20",
 "protocol":"OPC UA"
}

```

---

禁止：

业务核心字段放JSON。

错误：

```
work_order.json

{
 product,
 qty,
 status
}

```

---

# 17. SqlSugar规范

统一封装：

```
Industrial.Persistence


```

---

## Repository

禁止：

Service直接：

```csharp
db.Queryable<T>()

```

必须：

```csharp
IRepository<TEntity>

```

---

结构：

```
Domain

 IRepository


Infrastructure

 SqlSugarRepository

```

---

## 17.1 统一实体持久化规范

所有继承 `Entity` 的业务表统一包含：

```text
id uuid primary key
is_frozen boolean not null default false
is_locked boolean not null default false
is_deleted boolean not null default false
entity_type text not null
created_on timestamptz not null
last_updated_on timestamptz not null
optimistic_version bigint not null default 0
concurrency_version uuid not null
```

创建时 `created_on = last_updated_on`。默认查询过滤 `is_deleted = false`。更新、软删除和恢复必须以 `id`、与操作匹配的原始 `is_deleted` 状态及调用方原始双版本作为同一 SQL 条件；影响行数不是 `1` 时返回并发冲突。

通用仓储不得执行物理删除。物理清理仅允许独立的数据保留或运维流程执行。

表定义、实体字段表和“主要字段”列表只展示当前表拥有的业务字段，不逐表重复上述 `Entity` 生命周期字段；完整建表、迁移和映射仍必须应用全部统一字段。

领域实体自身的稳定业务标识统一使用 `NId`，其他业务表引用时使用 `{EntityName}NId`。PostgreSQL 物理列统一使用 `snake_case`，例如 `NId → n_id`、`MaterialNId → material_n_id`。`Code` 仅允许表示规则生成的编码结果等非实体身份语义。

同库父子表使用主表 `Id + IsDeleted` 作为复合外键目标。子表分别保存 `{ParentEntity}_Id`、`{ParentEntity}_IsDeleted`，物理列为 `{parent_entity}_id`、`{parent_entity}_is_deleted`；例如：

```text
MaterialProperty(Material_Id, Material_IsDeleted) → Material(Id, IsDeleted)
material_property(material_id, material_is_deleted) → material(id, is_deleted)
```

```sql
alter table material
    add constraint uq_material_id_is_deleted unique (id, is_deleted);

alter table material_property
    add constraint fk_material_property_material
    foreign key (material_id, material_is_deleted)
    references material (id, is_deleted)
    on update cascade;
```

子表自身仍有独立的生命周期 `IsDeleted/is_deleted`，不得复用它表示主表删除状态。被引用主表必须声明 `unique (id, is_deleted)`；父表软删除或恢复时使用 `ON UPDATE CASCADE` 或同一事务内的等价机制同步子表父删除状态快照，不得改写子表自身 `is_deleted`。默认有效子记录查询同时过滤 `child.is_deleted = false` 与 `child.{parent_entity}_is_deleted = false`。

跨服务、跨数据库只保存对方 `{EntityName}NId` 和必要业务快照，通过 API/事件保持最终一致性，不建立数据库外键。

## 17.2 索引决策

- `id` 主键索引是每张实体表唯一强制的基础索引。
- 不统一创建 `(id, is_deleted)` 或 `is_deleted` 单列索引。
- 仅当主表被同库子表以 `Id + IsDeleted` 引用时，为该主表创建可引用的 `unique (id, is_deleted)`；这是复合外键所需的条件性例外，不推广到所有实体表。
- 业务唯一键使用 `where is_deleted = false` 的部分唯一索引。
- 存在增量同步或更新时间游标分页时，按需创建 `(last_updated_on desc, id)` B-tree；只同步活跃记录时使用部分索引。
- 超大、近似按时间追加的表经 `EXPLAIN (ANALYZE, BUFFERS)` 验证后可选择 `last_updated_on` BRIN。
- PostgreSQL `CLUSTER` 不作为默认持续索引机制。

SqlSugar 查询过滤、软删除和并发 SQL 位于 Infrastructure；SharedKernel 禁止引用 SqlSugar Attribute。具体索引由各服务迁移负责。

---

# 18. 数据库迁移

推荐：

每个服务独立Migration。

例如：

```
WorkOrder.Service


Migrations

20260731_Create_WorkOrder.sql

```

---

启动时不得由各业务 API 使用管理员凭据直接执行 `DatabaseInitializer` 建库。后续服务通过声明式 manifest 与 `SystemData.Service` 数据库编排 API 握手：先登记/查询期望状态，再由 SystemData 生成 plan，并按环境策略异步 provision/apply；服务在目标迁移版本确认前保持 NotReady。`DatabaseName` 是稳定逻辑身份，完整的 `DatabaseTopology` 及其拓扑切换、drift 和 Shared 规则以蓝图 33 为准。

每个服务仍独立拥有 Migration Assembly/Bundle、迁移历史语义、expand/contract 说明和恢复方案。SystemData 只编排数据库、最小角色、授权、并发锁与迁移执行，不维护业务表定义。

Development/测试可以配置自动 provision + migrate；生产默认执行 `plan → 审批 → 备份 → apply`。禁止使用 `EnsureCreated` 或删除重建替代版本化迁移。同一数据库使用 PostgreSQL advisory lock 或等效分布式锁，禁止多个副本并发迁移。

SystemData 自身数据库由 PostgreSQL 18 Compose/init 或部署步骤最小引导，这是唯一 bootstrap 例外；SystemData API 只管理其他服务数据库。完整注册字段、安全、失败与本地 SQLite 回退规则读取蓝图 33。

---

# 19. 多租户设计

未来SaaS。

所有业务表预留：

```sql
tenant_id bigint
```

例如：

```
tenant_id

factory_id

```

---

查询：

自动过滤。

例如：

```
TenantScope

```

---

# 20. 数据权限

结合：

Permission Service。

维度：

```
公司

工厂

车间

产线

```

字段：

```
organization_id

```

---

# 21. 数据库备份策略

## 在线库

每日：

```
pg_dump

```

---

## 时序库

连续：

```
WAL

```

---

## 文件

MinIO：

```
版本控制

生命周期

```

---

# 22. Docker数据库目录

对应：

```
docker/database

```

结构：

```
database


├── postgres

│
├── init

│   ├── identity.sql
│   ├── mes.sql
│
├── timescaledb

└── backup

```

---

# 23. 最终数据库架构

```
                    API Gateway


                         |


 -------------------------------------------------

 |          |            |          |            |


Identity  MES        Equipment   Quality    Monitor


 |          |            |          |            |


identity  mes_db   equipment_db quality_db monitor_db



                         |


                 TimescaleDB


                         |


                 Industrial Data


                         |

                       MinIO

                  Archive Storage

```

---

# 24. 本阶段落地建议

第一版不要创建全部数据库。

MVP阶段：

建议：

```
industrial_identity

industrial_mes

industrial_equipment

industrial_trace

industrial_timeseries

industrial_audit

```

对应：

6个数据库即可。

随着服务拆分：

再独立。

---

#
