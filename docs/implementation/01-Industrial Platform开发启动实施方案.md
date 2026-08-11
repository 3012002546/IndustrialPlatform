# 01-Industrial Platform开发启动实施方案

# Industrial Platform开发启动实施方案

版本：V1.0
阶段：Development Implementation Phase
项目类型：工业数字化执行平台
技术路线：

.NET 10 + DDD + Clean Architecture + Microservices + Vue3

---

# 1. 文档说明

## 1.1 文档目的

本文档用于指导 Industrial Platform 从架构设计阶段进入工程开发阶段。

前期已经完成：

* 产品定位设计
* 微服务架构设计
* DDD领域模型设计
* 数据库模型设计
* API规范设计
* 前端工程规范设计
* 测试体系设计
* 安全体系设计

总体架构不在开发任务中随意变更；每个 PF 阶段只保留一个阶段管理会话，该会话根据实施母版、项目记忆和当前代码完成详细设计、任务派遣、跟踪与验收，实际编码由被派遣任务执行。

目标：

> 将设计文档转换为真实可运行的软件工程。

最终输出：

一个完整的软件仓库：

```
IndustrialPlatform
```

包含：

* 后端微服务
* Vue3前端
* 自动化测试
* Docker环境
* 部署脚本
* CI/CD流程
* Codex辅助开发规范

---

# 2. 开发阶段总体规划

当前进度以任务执行记录、提交和新鲜验证证据为准：BuildingBlocks、可运行基线和统一前端第一批已经完成主要范围，Docker 实机验收仍有保留项；Identity 已暂停在 `TASK-ID-007`；PF-01 开发设计已完成但尚未开发；ReferenceData 代码只有服务骨架；MasterData 和 OperationalData 暂缓。

当前执行顺序：

```text
已完成  BuildingBlocks / Entity 调整 / 可运行基线主要范围 / 统一前端第一批
PF-00    Identity 登录与权限闭环（已暂停，停在 TASK-ID-007）
PF-01    视觉、主题与平台外壳（开发设计已完成，任务待确认）
PF-02    SystemData
PF-03    ReferenceData（现有骨架复核后继续）
PF-04    Audit / File / Notification（分别设计）
PF-05    Collaboration
PF-06    RemoteAssistance 验证与试点
PF-07    Scheduler / Platform Health（分别设计）
PF-08    Low Code
PF-09    Dashboard / Report（分别设计）
PF-10    ServerMonitor
PF-10A   Operations Center Knowledge & Assistant（设计待确认）
PF-11    IoT Collector
MES-01   MasterData（暂缓）
MES-02   OperationalData（暂缓）
MES-03+  生产闭环服务
```

## Phase 0 BuildingBlocks 原基础搭建（已完成）

已完成 SharedKernel、Application.Abstractions、Infrastructure、EventBus、Logging、Security、Web 共享能力。完成证据和关键技术决策由 `CLAUDE.md` 维护，不再派遣本阶段任务。

## Phase 0A Entity 生命周期与并发调整

在任何业务实体开发和 `TASK-BASE-002` 前执行 `TASK-BB-010`：统一 Entity 字段、冻结/锁定/软删除状态、双版本并发和仓储原子更新。该任务只调整 BuildingBlocks 基线，不回退已经完成的 `TASK-BASE-001` 验证。

## Phase 1 项目可运行基线

目标：让 PostgreSQL、Redis、RabbitMQ、Seq、Identity、ReferenceData 和统一 API 入口可按文档重复启动、停止、诊断和验证。

详细任务：`docs/implementation/02A-Industrial Platform可运行基线开发实施方案.md`。

完成标准：基础设施健康、后端构建测试通过、服务与 Gateway 健康检查可访问、新环境启动说明可复现。

## Phase 2 统一前端第一批

目标：交付 Vue 3 单一工程、登录页、PC 管理框架、首页、403/404、PDA 基础壳和 Mobile 基础壳。

本阶段使用显式 Mock 登录，并通过 `AuthGateway` 保持 Phase 3 真实 Identity API 可替换；暂缓全部业务页面、离线同步、扫码、打印、蓝牙和 Capacitor。

详细任务：`docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`。

## PF-00 Identity 登录闭环

当前已暂停。`TASK-ID-001～006` 已完成，恢复时从 `TASK-ID-007` 继续；`TASK-ID-007～016` 仍未开发。后续范围继续完成服务端 RBAC、权限缓存、用户上下文、管理 API、审计/Outbox、真实前端、SSO 和联合验收，不得把暂停状态表述为阶段完成。

## PF-01～PF-11（含 PF-10A）平台基础和独立模块

详细边界读取 `docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md`，执行顺序、阶段任务卡和阶段管理会话入口读取 `docs/blueprint/09-Industrial Platform开发总TodoList.md`。

PF-01 已完成开发详细设计、任务依赖和七张九字段任务卡，但尚未执行开发任务。任务确认后才能派遣；其中最终 Identity 联合集成验收仍等待 PF-00 恢复并稳定前端契约。

每个阶段都遵循：

```text
阶段管理会话读取母版、项目记忆和当前代码
→ 详细设计与用户确认
→ 对应编号实施方案和九字段任务卡
→ 派遣实际开发任务
→ 跟踪、阶段验收和总 TodoList 回写
```

PF-03 ReferenceData 复用重编号后的实施文档 06，但开发前必须由 PF-03 阶段管理会话复核当前骨架、任务状态以及与 SystemData 和主题体系的契约。

## MES-01 MasterData 与 MES-02 OperationalData

MasterData、OperationalData 实施文档调整为 15、16 并暂停执行。达到总 TodoList 规定的恢复门禁后，分别由 MES 阶段管理会话复核，再决定保留或调整原任务卡。

## MES-03 以后：生产闭环服务纵向交付

WorkOrder、Weighting、IoT Collector、Trace、BatchRecord 依次推进。每个阶段都遵循：

```text
服务领域与应用用例
→ API / 事件契约
→ 对应 PC/PDA/Mobile 页面
→ 契约测试与关键路径 E2E
→ 阶段验收
```

不再设置独立的末期“统一补前端”阶段。MES 业务闭环在平台基础达到门禁并且各服务纵向交付完成后进行全链路验收。

---

# 3. Git仓库初始化

## 3.1 创建仓库

仓库名称：

```
IndustrialPlatform
```

初始化：

```bash
mkdir IndustrialPlatform

cd IndustrialPlatform

git init
```

---

# 3.2 Git分支规范

采用简化Git Flow。

分支：

```
main

develop

feature/*

bugfix/*

release/*
```

说明：

## main

生产稳定版本。

示例：

```
v1.0.0
```

---

## develop

开发主分支。

所有功能合并到：

```
develop
```

---

## feature

功能开发。

示例：

```
feature/identity-service

feature/referencedata-service

feature/masterdata-service

feature/workorder-service
```

---

# 3.3 Commit规范

采用：

Conventional Commit

格式：

```
type(scope): message
```

示例：

新增：

```
feat(identity): add login api
```

修复：

```
fix(order): fix status transition
```

文档：

```
docs(api): update api document
```

重构：

```
refactor(domain): optimize entity
```

---

# 4. 最终仓库目录

最终结构：

```
IndustrialPlatform

├── docs

├── src

│
├── backend

│
├── frontend


├── tests


├── docker


├── deploy


├── .github


└── .codex
```

---

# 5. Backend工程规划

目录：

```
src
```

结构：

```
IndustrialPlatform

├── IndustrialPlatform.slnx


├── src


│
├── BuildingBlocks


│
├── Services


│
├── Gateway



└── Tools
```

---

# 6. Visual Studio 2026 Solution规划

Solution：

```
IndustrialPlatform.slnx
```

项目结构：

```
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


├── BatchRecord
```

---

# 7. Microservice工程结构规范

以 Identity Service 为例：

```
Identity


├── Identity.Api


├── Identity.Application


├── Identity.Domain


└── Identity.Infrastructure
```

---

# 7.1 API层

职责：

* Controller
* Middleware
* Authentication
* API配置

禁止：

业务逻辑。

---

# 7.2 Application层

职责：

* UseCase
* Command
* Query
* DTO
* Service

---

# 7.3 Domain层

职责：

核心业务。

包含：

```
Entities

Aggregates

ValueObjects

DomainEvents
```

禁止引用：

Infrastructure。

---

# 7.4 Infrastructure层

职责：

外部实现。

包含：

* Database
* Repository
* Cache
* MQ
* External API

---

# 8. Frontend工程规划

目录：

```
src/frontend
```

技术：

* Vue3
* TypeScript
* Vite
* Pinia
* Element Plus
* ECharts

---

结构：

```
frontend


├── src


│
├── api


├── assets


├── components


├── layouts


├── router


├── stores


├── utils


├── hooks


├── views


├── permissions


├── pc


├── pda


└── mobile


```

---

# 9. 三端统一开发规范

原则：

业务代码共享。

端差异只存在：

* 页面布局
* 操作方式
* 展示方式

共享：

```
api

store

hooks

components

utils
```

---

# 10. Tests目录规划

目录：

```
tests
```

结构：

```
tests


├── UnitTests


│
├── Identity.Tests

├── ReferenceData.Tests

├── MasterData.Tests

├── OperationalData.Tests

├── WorkOrder.Tests


├── IntegrationTests


├── ApiTests


├── PerformanceTests


└── E2ETests
```

---

# 11. Docker目录规划

目录：

```
docker
```

结构：

```
docker


├── docker-compose.yml


├── postgres


├── redis


├── rabbitmq


├── nginx


└── seq
```

---

# 12. Deploy目录规划

生产部署。

结构：

```
deploy


├── docker-compose


├── kubernetes


├── nginx


├── scripts


└── environment
```

---

# 13. Github Actions规划

目录：

```
.github


├── workflows


│
├── backend-ci.yml

├── frontend-ci.yml

├── docker-build.yml


├── ISSUE_TEMPLATE


└── pull_request_template.md
```

## 13.1 跨平台路径与 CI 验证约束

后端 GitHub Actions 使用 `ubuntu-latest`。Windows 本地构建和测试通过，只能作为本地验证证据，不能替代 Linux Runner 验收。

已知陷阱：`.csproj` 的 `ProjectReference Include` 可能使用 Windows 反斜杠路径，例如：

```xml
<ProjectReference Include="..\IndustrialPlatform.SharedKernel\IndustrialPlatform.SharedKernel.csproj" />
```

在 Linux 中，反斜杠不是目录分隔符。如果直接执行：

```csharp
Path.GetFileNameWithoutExtension(projectReferencePath)
```

可能返回完整相对路径，而不是项目名称，造成 Windows 测试通过、Ubuntu CI 失败。

统一规则：

1. 从 `.csproj`、JSON、YAML、命令输出或其他外部文本读取路径后，调用 `Path` API 前必须兼容 `/` 和 `\`。对项目引用路径可先在解析边界统一分隔符：

   ```csharp
   var portablePath = projectReferencePath.Replace('\\', '/');
   var projectName = Path.GetFileNameWithoutExtension(portablePath);
   ```

2. 不依赖开发机操作系统解释外部路径文本，也不通过批量改写全部 `.csproj` 路径来掩盖解析器缺陷；应在读取边界完成规范化。
3. 新增或修改路径解析逻辑时，参数化测试必须同时覆盖 `..\A\A.csproj` 和 `../A/A.csproj`。
4. Linux 文件名区分大小写，代码、脚本、Solution 和项目引用中的目录及文件名必须与仓库实际名称完全一致。
5. 后端任务提交前必须按 GitHub Actions 的 Release 配置执行：

   ```bash
   dotnet restore src/backend/IndustrialPlatform.slnx
   dotnet build src/backend/IndustrialPlatform.slnx --configuration Release --no-restore
   dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --logger trx
   ```

6. 最终验收证据必须包含 Ubuntu GitHub Actions 运行结果或链接。本地仅有 Windows 验证时，任务状态必须明确标记“Linux CI 待验证”，不得声明跨平台验收完成。

---

# 14. .codex目录规划

目录：

```
.codex
```

结构：

```
.codex


├── project-context.md

├── architecture.md

├── coding-rule.md

├── database-rule.md

├── api-rule.md

├── task-template.md

└── commit-rule.md
```

---

# 15. Codex协作开发方式

本实施方案由当前协调任务维护，但其中的代码实现、测试、Review 和提交必须派遣到其他开发任务执行。当前协调任务负责确认输入设计、拆分 TODO、限定修改范围、收集验收证据并回写状态；除非用户明确改变范围，否则不直接修改业务代码。

禁止：

直接要求：

```
帮我写代码
```

推荐流程：

```
需求

↓

任务拆分

↓

技术方案

↓

代码实现

↓

测试

↓

Review

↓

Commit

↓

返回验证证据与设计偏差

↓

协调任务回写 TODO 和蓝图
```

---

# 16. Codex任务模板

每个可派遣任务必须包含：任务编号、目标、输入文档、依赖、允许修改范围、预期输出、验证命令或验收证据、结果回写位置和建议提交信息。

涉及路径、脚本、文件名大小写或换行符的任务，还必须在“要求/验收”中写明：目标 Runner 为 `ubuntu-latest`、路径分隔符兼容策略、Linux 大小写敏感约束、相应跨平台回归用例，以及 Release CI 命令和 GitHub Actions 运行证据。不得只填写“本地测试通过”。

示例：

```
任务：

实现 Identity 登录接口


背景：

Industrial Platform


技术：

.NET10

DDD

Clean Architecture


要求：

1. 创建领域模型

2. 创建Application Service

3. 创建API接口

4. 增加Unit Test

5. 增加数据库实体


验收：

登录成功返回JWT Token

测试通过
```

---

# 17. 开发规范

## Backend规范

必须：

* DDD
* Clean Architecture
* 异步编程
* DTO隔离
* Repository模式
* Domain不依赖Infrastructure

---

## Database规范

所有业务表必须包含：

```
Id

CreateTime

CreateUser

ModifyTime

ModifyUser

IsDeleted

Version
```

---

## API规范

统一：

```
/api/{service}/{resource}
```

例如：

```
GET

/api/workorder/orders
```

---

# 18. 当前总体开发路线

目标：在已建立的可运行产品骨架上，先完成平台基础和独立模块，再恢复工业生产闭环。

```text
已完成：BuildingBlocks / Entity 调整 / 可运行基线主要范围 / 统一前端第一批
→ PF-00 Identity
→ PF-01～PF-11（含 PF-10A）平台基础和独立模块
→ MES-01 MasterData
→ MES-02 OperationalData
→ MES-03+ 生产闭环
```

完整阶段和门禁只在 `docs/blueprint/09-Industrial Platform开发总TodoList.md` 维护。

---

# 19. 当前进度与下一步

## 已完成

- Git 仓库、解决方案和后端目录骨架。
- BuildingBlocks 共享组件及测试。
- 可运行基线主要范围；Docker 实机项仍保留待验收。
- 统一前端第一批 `TASK-FE-001～010`。
- Identity、ReferenceData 服务骨架与健康检查。

## 当前暂停项

- PF-00 Identity：`TASK-ID-001～006` 已完成，暂停点为 `TASK-ID-007`；等待用户明确恢复。

## 当前待确认项

- PF-01 视觉、主题与平台外壳：实施文档 04 的开发详细设计、依赖图和七张九字段任务卡已完成；尚未开发，等待任务确认/派遣。

## 后续优先级

- PF-02 SystemData 与 PF-03 ReferenceData；ReferenceData 当前代码只有骨架，开发前复核实施文档 06。

## 后续顺序

- PF-04～PF-11（含 PF-10A）按总 TodoList 的阶段门禁推进。
- MasterData 和 OperationalData 暂缓，达到 MES 恢复门禁后分别复核。

实施文档执行记录、提交和新鲜验证证据共同构成进度依据；`CLAUDE.md` 可以记录协作过程，但不替代正式验收。

---

# 20. 可运行基础完成标准

新开发环境按仓库说明能够：

```text
启动 PostgreSQL / Redis / RabbitMQ / Seq
构建并测试后端解决方案
启动 Identity / ReferenceData / Gateway
访问后端与聚合健康检查
安装并启动统一前端
通过 Mock 登录访问 PC / PDA / Mobile 基础壳
```

达到上述标准后，才进入 Identity 真实登录闭环。
