# PF07 原生存储字段字典

更新：2026-10-13。固定 Hangfire.Core/SqlServer 1.8.25、PostgreSql 1.20.13（schema 23）、Storage.SQLite 0.4.3；SQL Server schema 9。此处为实际安装后的原生字段，不新增业务实体或手工改造供应商表。两张自有表见[主规格](PF07-数据接口与页面规格.md)§2。

来源：固定包内置安装脚本及独立 PostgreSQL、SQL Server 2019、SQLite 元数据；实际原生入队→工作器→Succeeded 已验证，详见[实施证据](../../evidence/PF-07.md)。PG/SQL Server均使用scheduler_hangfire schema；SQLite使用受控派生的独立.scheduler.db文件，Prefix仅隔离逻辑键。长度为字符上限，max/-1表示无固定上限；SQLite声明长度不会自动成为约束。

## PostgreSQL

### aggregatedcounter

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| key | text | — | 否 | 逻辑键 |
| value | bigint | — | 否 | 原生值 |
| expireat | timestamp with time zone | — | 是 | 原生清理时间 |
### counter

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| key | text | — | 否 | 逻辑键 |
| value | bigint | — | 否 | 原生值 |
| expireat | timestamp with time zone | — | 是 | 原生清理时间 |
### hash

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| key | text | — | 否 | 逻辑键 |
| field | text | — | 否 | Hash字段键 |
| value | text | — | 是 | 原生值 |
| expireat | timestamp with time zone | — | 是 | 原生清理时间 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### job

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| stateid | bigint | — | 是 | 当前原生状态引用 |
| statename | text | — | 是 | 当前原生状态 |
| invocationdata | jsonb | — | 否 | 固定包装方法及调用类型 |
| arguments | jsonb | — | 否 | 包装入口参数JSON |
| createdat | timestamp with time zone | — | 否 | 原生创建时间 |
| expireat | timestamp with time zone | — | 是 | 原生清理时间 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### jobparameter

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| jobid | bigint | — | 否 | 所属原生任务 |
| name | text | — | 否 | 参数或状态名称 |
| value | text | — | 是 | 原生值 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### jobqueue

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| jobid | bigint | — | 否 | 所属原生任务 |
| queue | text | — | 否 | 原生队列 |
| fetchedat | timestamp with time zone | — | 是 | 原生领取时间 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### list

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| key | text | — | 否 | 逻辑键 |
| value | text | — | 是 | 原生值 |
| expireat | timestamp with time zone | — | 是 | 原生清理时间 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### lock

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| resource | text | — | 否 | 原生分布式锁资源 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
| acquired | timestamp with time zone | — | 是 | 原生锁取得时间 |
### schema

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| version | integer | — | 否 | 实际安装版本 |
### server

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | text | — | 否 | 原生记录主键 |
| data | jsonb | — | 是 | 状态/工作器元数据JSON |
| lastheartbeat | timestamp with time zone | — | 否 | 原生工作器心跳时间 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### set

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| key | text | — | 否 | 逻辑键 |
| score | double precision | — | 否 | Set排序值 |
| value | text | — | 否 | 原生值 |
| expireat | timestamp with time zone | — | 是 | 原生清理时间 |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
### state

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| id | bigint | — | 否 | 原生记录主键 |
| jobid | bigint | — | 否 | 所属原生任务 |
| name | text | — | 否 | 参数或状态名称 |
| reason | text | — | 是 | 原生状态原因 |
| createdat | timestamp with time zone | — | 否 | 原生创建时间 |
| data | jsonb | — | 是 | 状态/工作器元数据JSON |
| updatecount | integer | — | 否 | 供应商并发更新计数 |
## SqlServer

### AggregatedCounter

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Key | nvarchar | 100 | 否 | 逻辑键 |
| Value | bigint | 8 | 否 | 原生值 |
| ExpireAt | datetime | 8 | 是 | 原生清理时间 |
### Counter

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Key | nvarchar | 100 | 否 | 逻辑键 |
| Value | int | 4 | 否 | 原生值 |
| ExpireAt | datetime | 8 | 是 | 原生清理时间 |
| Id | bigint | 8 | 否 | 原生记录主键 |
### Hash

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Key | nvarchar | 100 | 否 | 逻辑键 |
| Field | nvarchar | 100 | 否 | Hash字段键 |
| Value | nvarchar | -1 | 是 | 原生值 |
| ExpireAt | datetime2 | 8 | 是 | 原生清理时间 |
### Job

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Id | bigint | 8 | 否 | 原生记录主键 |
| StateId | bigint | 8 | 是 | 当前原生状态引用 |
| StateName | nvarchar | 20 | 是 | 当前原生状态 |
| InvocationData | nvarchar | -1 | 否 | 固定包装方法及调用类型 |
| Arguments | nvarchar | -1 | 否 | 包装入口参数JSON |
| CreatedAt | datetime | 8 | 否 | 原生创建时间 |
| ExpireAt | datetime | 8 | 是 | 原生清理时间 |
### JobParameter

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| JobId | bigint | 8 | 否 | 所属原生任务 |
| Name | nvarchar | 40 | 否 | 参数或状态名称 |
| Value | nvarchar | -1 | 是 | 原生值 |
### JobQueue

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Id | bigint | 8 | 否 | 原生记录主键 |
| JobId | bigint | 8 | 否 | 所属原生任务 |
| Queue | nvarchar | 50 | 否 | 原生队列 |
| FetchedAt | datetime | 8 | 是 | 原生领取时间 |
### List

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Id | bigint | 8 | 否 | 原生记录主键 |
| Key | nvarchar | 100 | 否 | 逻辑键 |
| Value | nvarchar | -1 | 是 | 原生值 |
| ExpireAt | datetime | 8 | 是 | 原生清理时间 |
### Schema

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Version | int | 4 | 否 | 实际安装版本 |
### Server

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Id | nvarchar | 200 | 否 | 原生记录主键 |
| Data | nvarchar | -1 | 是 | 状态/工作器元数据JSON |
| LastHeartbeat | datetime | 8 | 否 | 原生工作器心跳时间 |
### Set

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Key | nvarchar | 100 | 否 | 逻辑键 |
| Score | float | 8 | 否 | Set排序值 |
| Value | nvarchar | 256 | 否 | 原生值 |
| ExpireAt | datetime | 8 | 是 | 原生清理时间 |
### State

| 字段 | 原生类型 | 长度 | 可空 | 中文说明 |
| --- | --- | --- | --- | --- |
| Id | bigint | 8 | 否 | 原生记录主键 |
| JobId | bigint | 8 | 否 | 所属原生任务 |
| Name | nvarchar | 20 | 否 | 参数或状态名称 |
| Reason | nvarchar | 100 | 是 | 原生状态原因 |
| CreatedAt | datetime | 8 | 否 | 原生创建时间 |
| Data | nvarchar | -1 | 是 | 状态/工作器元数据JSON |
## SQLite

### AggregatedCounter

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Key | varchar(100) primary key not null  | 否 | 逻辑键 |
| Value | float  | 是 | 原生值 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### Counter

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | varchar(100) primary key not null  | 否 | 原生记录主键 |
| Key | varchar(100)  | 是 | 逻辑键 |
| Value | float  | 是 | 原生值 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### DistributedLock

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | varchar primary key not null  | 否 | 原生记录主键 |
| Resource | varchar  | 是 | 原生分布式锁资源 |
| ResourceKey | varchar  | 是 | 原生锁索引键 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### Hash

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| Key | varchar(100)  | 是 | 逻辑键 |
| Field | varchar(100)  | 是 | Hash字段键 |
| Value | varchar(16000)  | 是 | 原生值 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### Job

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| StateId | integer  | 是 | 当前原生状态引用 |
| StateName | varchar(20)  | 是 | 当前原生状态 |
| InvocationData | varchar(16000)  | 是 | 固定包装方法及调用类型 |
| Arguments | varchar(16000)  | 是 | 包装入口参数JSON |
| CreatedAt | bigint  | 是 | 原生创建时间 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### JobParameter

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| JobId | integer  | 是 | 所属原生任务 |
| Name | varchar(40)  | 是 | 参数或状态名称 |
| Value | varchar(16000)  | 是 | 原生值 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### JobQueue

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| JobId | integer  | 是 | 所属原生任务 |
| Queue | varchar(50)  | 是 | 原生队列 |
| FetchedAt | bigint | 是 | 原生领取时间 |
### List

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| Key | varchar(100)  | 是 | 逻辑键 |
| Value | varchar(16000)  | 是 | 原生值 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### Server

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | varchar(100) primary key not null  | 否 | 原生记录主键 |
| Data | varchar(16000)  | 是 | 状态/工作器元数据JSON |
| LastHeartbeat | bigint | 是 | 原生工作器心跳时间 |
### Set

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| Key | varchar(100)  | 是 | 逻辑键 |
| Score | float  | 是 | Set排序值 |
| Value | varchar(256)  | 是 | 原生值 |
| ExpireAt | bigint | 是 | 原生清理时间 |
### State

| 字段 | 原生声明 | 可空 | 中文说明 |
| --- | --- | --- | --- |
| Id | integer primary key autoincrement not null  | 否 | 原生记录主键 |
| JobId | integer  | 是 | 所属原生任务 |
| Name | varchar(20)  | 是 | 参数或状态名称 |
| Reason | varchar(100)  | 是 | 原生状态原因 |
| CreatedAt | bigint  | 是 | 原生创建时间 |
| Data | varchar(16000)  | 是 | 状态/工作器元数据JSON |
| ExpireAt | bigint | 是 | 原生清理时间 |

### SQLite显式索引

| 索引 | 定义 |
| --- | --- |
| DistributedLock_Resource | UNIQUE DistributedLock ("Resource") |
| IX_AggregatedCounter_ExpireAt | AggregatedCounter ("ExpireAt") |
| IX_Counter_ExpireAt | Counter ("ExpireAt") |
| IX_Hash_ExpireAt | Hash ("ExpireAt") |
| IX_Hash_Field | Hash ("Field") |
| IX_Hash_Key | Hash ("Key") |
| IX_JobParameter_ExpireAt | JobParameter ("ExpireAt") |
| IX_JobParameter_JobId | JobParameter ("JobId") |
| IX_JobParameter_Name | JobParameter ("Name") |
| IX_JobQueue_FetchedAt | JobQueue ("FetchedAt") |
| IX_JobQueue_Queue | JobQueue ("Queue") |
| IX_Job_ExpireAt | Job ("ExpireAt") |
| IX_Job_StateName | Job ("StateName") |
| IX_List_ExpireAt | List ("ExpireAt") |
| IX_List_Key | List ("Key") |
| IX_Set_ExpireAt | Set ("ExpireAt") |
| IX_Set_Key | Set ("Key") |
| IX_Set_Value | Set ("Value") |
| IX_State_ExpireAt | State ("ExpireAt") |
| IX_State_JobId | State ("JobId") |

运行前拒绝缺表/缺字段，SQLite另外逐项检查20个显式索引。PG/SQL Server校验列集合及安装版本，PrepareSchemaIfNecessary=false。结构升级随固定包版本和新受控迁移进行，不在普通请求/工作器查询中补建。原生锁只保护引擎状态，业务执行资格仍由自有Run活动槽约束。

SchedulerAttempts为原生JobParameter中的受控扩展值：每次实际开始记录Number/Status/StartedOn/CompletedOn/ResultCode/ErrorSummary/TraceId；最多5条，只记录真实调用，不用AttemptsUsed生成行。原始引擎调用数据和异常堆栈不向管理API开放。原生保留期固定90天；历史清除后API明确Unavailable。
