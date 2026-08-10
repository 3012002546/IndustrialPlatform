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

本阶段不再讨论架构设计。

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

当前进度以 `CLAUDE.md` 为准。BuildingBlocks 已完成；Identity、ReferenceData 只有服务骨架；前端和 Docker 尚未实现。

当前执行顺序：

```text
Phase 0  BuildingBlocks 原基础搭建（已完成）
Phase 0A Entity 生命周期与并发调整（TASK-BB-010）
Phase 1  项目可运行基线剩余任务
Phase 2  统一前端第一批
Phase 3  Identity 登录闭环
Phase 4  ReferenceData 服务 + 页面
Phase 5  MasterData 服务 + 页面
Phase 6  OperationalData 服务 + 页面
Phase 7+ WorkOrder / Weighting / IoTCollector / Trace / BatchRecord 服务 + 页面
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

## Phase 3 Identity 登录闭环

在现有骨架上完成用户、角色、权限、JWT、RefreshToken、注销与撤销，并把前端从 `MockAuthGateway` 切换到 `HttpAuthGateway`。阶段验收必须覆盖登录、刷新、401、403、菜单与按钮权限。

## Phase 4 ReferenceData 服务 + 页面

完成字典、配置、元数据和编码规则，并在同阶段交付相应 PC 管理页面、契约测试和关键路径 E2E。ReferenceData 不承载物料、设备、制造组织或 BOM。

## Phase 5 MasterData 服务 + 页面

执行 `TASK-MD-001` 至 `TASK-MD-010`，并同步交付物料、单位、组织、仓库/库位、设备、BOM、工艺路线页面。详细任务见文档 05。

## Phase 6 OperationalData 服务 + 页面

执行 `TASK-OD-001` 至 `TASK-OD-009`，并同步交付库存查询、批次、收发退、调拨和盘点页面。详细任务见文档 06。

## Phase 7 以后：生产闭环服务纵向交付

WorkOrder、Weighting、IoT Collector、Trace、BatchRecord 依次推进。每个阶段都遵循：

```text
服务领域与应用用例
→ API / 事件契约
→ 对应 PC/PDA/Mobile 页面
→ 契约测试与关键路径 E2E
→ 阶段验收
```

不再设置独立的末期“统一补前端”阶段。MVP 业务闭环在各服务纵向交付完成后进行全链路验收。

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

# 18. MVP第一阶段开发路线

目标：先建立可运行产品骨架，再纵向完成工业生产闭环。

```text
BuildingBlocks 原基础搭建（已完成）
↓
TASK-BB-010 Entity 生命周期与并发调整
↓
可运行基线
↓
统一前端第一批
↓
Identity 登录闭环
↓
ReferenceData 服务 + 页面
↓
MasterData 服务 + 页面
↓
OperationalData 服务 + 页面
↓
WorkOrder / Weighting / IoTCollector / Trace / BatchRecord 服务 + 页面
↓
MVP 全链路验收
```

---

# 19. 当前进度与下一步

## 已完成

- Git 仓库、解决方案和后端目录骨架。
- BuildingBlocks 共享组件及测试。
- Identity、ReferenceData 服务骨架与 `/health`。

## 当前第一优先级

- `TASK-BB-010`：见 `02-Industrial Platform BuildingBlocks基础组件开发实施方案.md`。

## 第二优先级

- `TASK-BASE-002` 至 `TASK-BASE-006`：见 `02A-Industrial Platform可运行基线开发实施方案.md`；已完成的 `TASK-BASE-001` 不重复执行。

## 第三优先级

- `TASK-FE-001` 至 `TASK-FE-008`：见 `02B-Industrial Platform统一前端第一批开发实施方案.md`。

## 后续顺序

- Identity 真实登录闭环。
- ReferenceData 服务功能与管理页面。
- MasterData 服务功能与管理页面。
- OperationalData 服务功能与业务页面。

`CLAUDE.md` 由代码协作方回写实际代码进度；本目录维护可派遣任务、验收证据和阶段依赖。

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
