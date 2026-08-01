# 个人MES平台两年开发路线

> 文档版本：V1.0
> 文档类型：个人产品研发规划
> 项目名称：Industrial Platform
> 目标：沉淀个人MES产品能力，形成可复制工业数字化平台
> 周期：24个月
> 技术方向：.NET 10 + Vue3 + PostgreSQL + 微服务

---

# 1. 项目定位

## 1.1 长期目标

打造：

> 一个属于自己的工业数字化平台产品。

不是为了完成一个MES项目，而是沉淀：

* MES产品能力
* 工业软件架构能力
* 行业经验模型
* 可复制实施能力

---

最终形成：

```text
Industrial Platform


生产执行 MES

+

设备数据平台

+

称量执行

+

质量追溯

+

运维监控

+

低代码配置

```

---

# 2. 两年总体规划

## 阶段划分

| 阶段  | 周期      | 目标       |
| --- | ------- | -------- |
| 阶段0 | 第0-1个月  | 基础架构搭建   |
| 阶段1 | 1-6个月   | MES核心MVP |
| 阶段2 | 7-12个月  | 现场执行能力   |
| 阶段3 | 13-18个月 | 平台化      |
| 阶段4 | 19-24个月 | 产品化      |

---

# 阶段0：基础工程建设

## 时间

第0-1个月

## 目标

建立：

* Git工程
* 微服务框架
* 前端框架
* CI/CD
* 文档体系

---

# 0.1 创建代码仓库

推荐：

Git Monorepo

结构：

```text
IndustrialPlatform


├── docs


├── backend


│
├── services


│
├── building-blocks


│
└── tests



├── frontend


│
├── web


│
└── mobile



├── agents


│
└── server-monitor-agent



├── deploy


│
└── docker



```

---

# 0.2 后端基础框架

完成：

## Service模板

每个微服务统一：

```text
Service


├── Api

├── Application

├── Domain

├── Infrastructure

├── Persistence

├── Tests

```

---

## 公共组件

建立：

```text
BuildingBlocks


├── Logging

├── EventBus

├── Result

├── Exception

├── Authorization

├── Audit

```

---

# 0.3 前端基础框架

完成：

```text
Vue3

+

TypeScript

+

Vite

+

Pinia

+

Router

```

建立：

MES组件库：

```text
MES UI Framework

```

---

# 阶段1：MES核心MVP

## 时间

1-6个月

目标：

形成一个可运行MES核心。

---

# 第一阶段功能范围

## 1. 基础数据

完成：

* 工厂模型
* 组织
* 用户
* 权限
* 物料

服务：

```text
Identity

ReferenceData

MasterData

```

---

## 2. BOM管理

支持：

* BOM
* 版本
* 替代料

---

## 3. 工单管理

核心。

完成：

```text
工单创建

↓

工序

↓

派工

↓

执行

↓

完工

```

---

## 4. 物料运行时

完成：

```text
批次

容器

包装

物料移动

```

---

## 5. PDA基础能力

完成：

* 登录
* 扫码
* 工单查询
* 工序报工

---

# 第一阶段成果

可以展示：

```text
创建订单

↓

生成工单

↓

现场扫码

↓

生产执行

↓

完成

↓

追溯

```

达到：

> 一个轻量MES Demo。

---

# 阶段2：现场执行能力

## 时间

7-12个月

目标：

接近商业MES。

---

# 1. 称量系统

重点。

完成：

## 称量任务

```text
工单

↓

配方

↓

称量任务

```

---

## 电子秤接口

支持：

```text
串口

TCP

Modbus

OPC UA

```

---

## 称量防错

实现：

* 物料校验
* 批次校验
* 重量校验

---

# 2. 设备管理

完成：

设备：

* 台账
* 参数
* 状态

---

# 3. IoT采集

完成：

```text
PLC

↓

Collector

↓

MES

```

支持：

* OPC UA
* MQTT

---

# 4. OEE

完成：

指标：

* 开机率
* 稼动率
* 良率

---

# 5. 批记录

完成：

电子批记录。

---

# 阶段2成果

可以支持：

```text
生产

+

称量

+

设备

+

追溯

+

批记录

```

---

# 阶段3：平台化建设

## 时间

13-18个月

目标：

从MES变成工业平台。

---

# 1. 低代码能力

增加：

## 页面设计器

支持：

拖拽：

* 表单
* 表格
* 查询

---

## 流程设计器

支持：

```text
工艺流程

审批流程

异常流程

```

---

## 看板设计器

支持：

拖拽：

* 图表
* 指标
* 状态

---

# 2. 报表平台

支持：

* 模板设计
* 数据源配置
* 参数化

---

# 3. Server Monitor 集成

集成：

```text
工业平台

 |

Server Monitor


```

实现：

统一驾驶舱。

---

# 4. 数据平台

增加：

* 数据归档
* 数据分析
* 数据接口

---

# 阶段3成果

形成：

```text
工业数字化平台

```

---

# 阶段4：产品化

## 时间

19-24个月

目标：

达到商业产品水平。

---

# 1. 多租户

支持：

```text
Tenant

Factory

Project

```

---

# 2. 实施配置

增加：

## 工厂配置

不用改代码：

配置：

* 工厂
* 产线
* 工序

---

## 工艺配置

配置：

```text
流程

参数

规则

```

---

# 3. 行业模板

沉淀：

## 电子行业模板

包含：

* 工单
* 测试
* 追溯

---

## 新能源模板

包含：

* 电芯
* 模组
* 批次

---

## 医药模板

包含：

* 称量
* 批记录
* GMP

---

# 4. AI能力

后期增加：

## 智能分析

例如：

设备：

预测：

```text
未来7天故障概率
```

---

生产：

预测：

```text
订单延期风险
```

---

# 3. Git开发规范

## 分支

```text
main

|

develop

|

feature/*

bugfix/*

release/*

```

---

# Commit规范

例如：

```text
feat: add weighting task

fix: fix workorder status

refactor: optimize trace service

docs: update architecture
```

---

# 4. Codex协同开发方式

建议：

Codex作为：

> 架构助手 + 开发助手 + Code Review助手

---

# 4.1 每个功能建立任务文件

例如：

```text
docs/tasks


TASK-WO-001.md


```

内容：

```markdown
# 工单模块


## 目标

实现工单生命周期


## 技术要求


.NET10

DDD


## 数据模型


## API


## 测试要求

```

---

# 4.2 开发流程

推荐：

```text
需求

↓

设计文档

↓

数据库设计

↓

接口设计

↓

Codex生成代码

↓

人工Review

↓

测试

↓

提交

```

---

# 5. 测试体系

## 单元测试

每个Service：

必须：

```text
Domain Test

Application Test

```

---

## 集成测试

例如：

```text
WorkOrder

+

Material

+

Trace

```

---

## UI测试

未来：

Playwright

---

# 6. 文档体系

仓库：

```text
docs


├── architecture

│
├── domain

│
├── database

│
├── api

│
├── deployment

│
├── tasks

```

---

# 7. 每周开发节奏建议

结合你目前MES工作。

建议：

## 工作日

沉淀：

每天30分钟

内容：

* 代码整理
* 文档补充

---

## 周末

4-8小时。

安排：

上午：

核心开发

下午：

测试

文档

---

# 8. 第一年的最终目标

12个月后：

拥有：

```text
Industrial Platform V1.0


✔ 用户权限

✔ 基础数据

✔ BOM

✔ 工单

✔ PDA

✔ 物料批次

✔ 称量

✔ 设备

✔ OEE

✔ 追溯

✔ 批记录

✔ Server Monitor

```

---

# 9. 两年后的最终目标

形成：

```text
Industrial Platform V2.0


制造平台

+

运维平台

+

低代码平台

+

行业模板


```

具备：

* MES项目快速复制能力
* Demo展示能力
* 产品化能力

---

# 10. 最终建议

这个项目不要按照：

> 写一个MES系统

而应该按照：

> 建设一个工业软件平台

你的优势：

* 8年MES经验
* C#/.NET技术积累
* 接触过真实制造现场
* 知道客户真正需求

重点沉淀：

```text
领域模型

+

行业经验

+

可配置能力

+

实施能力

```
