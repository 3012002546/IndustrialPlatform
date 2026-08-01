# Industrial Platform 蓝图整理与工程骨架设计

## 1. 目标

本次工作交付三个相互衔接的成果：

1. 检查并整理现有“个人MES平台开发设计”蓝图，形成可在项目仓库中持续维护的正式版本。
2. 在 `D:\Code\Industrial Platform\IndustrialPlatform` 创建独立 Git 仓库及标准目录结构，供后续上传 GitHub。
3. 根据“开发实施”02、03、04 创建可还原、可构建、可测试的 .NET 10 工程骨架，不实现具体业务功能。

现有外层文档作为原始资料保留，不直接修改。

## 2. 范围

### 2.1 本次包含

- 检查现有 31 份蓝图及“后续设计”，整理正式副本、索引、状态和阅读顺序。
- 将 ReferenceData 正式纳入总体蓝图，服务顺序调整为 BuildingBlocks、Identity、ReferenceData、MasterData。
- 统一文档编号、文件扩展名、标题、术语、项目名称和仓库路径。
- 创建单仓库、单总解决方案的 .NET 10 工程骨架。
- 创建 BuildingBlocks、Identity、ReferenceData 及对应测试项目。
- 配置项目引用、集中构建属性、集中包版本管理和基础持续集成。
- 验证还原、构建、测试和 Git 工作区状态。

### 2.2 本次不包含

- Identity 登录、JWT、权限和管理员初始化等业务功能。
- ReferenceData 字典、参数、EAV、编码规则和缓存等业务功能。
- SqlSugar、Redis、RabbitMQ、Serilog 的完整封装和运行时集成。
- 数据库迁移、容器环境、前端项目、部署环境和生产发布。
- 创建 GitHub 远程仓库或推送；该动作在本地成果验证后单独执行。

## 3. 关键决策

### 3.1 仓库策略

采用单体仓库。仓库目录和 GitHub 仓库名称均为 `IndustrialPlatform`，目录名不含空格，以降低命令行、CI、容器挂载和跨平台路径问题。

### 3.2 解决方案策略

采用一个 `IndustrialPlatform.slnx` 管理全部后端和测试项目。现阶段不为每个服务维护独立解决方案，避免重复配置；服务仍通过项目边界保持隔离，未来可拆分仓库。

### 3.3 ReferenceData 与 MasterData

ReferenceData 与 MasterData 是不同的限界上下文：

- ReferenceData 管理字典、平台参数、元数据、动态属性和编码规则。
- MasterData 管理物料、设备、组织、BOM 等制造主数据。

ReferenceData 是本次创建的第三组工程，MasterData 保留为后续阶段。相关蓝图、路线图、依赖图和实施文档必须使用这一边界。

### 3.4 工程深度

本次只创建工程骨架。每个项目应具备正确 SDK、目标框架、命名空间、引用关系和最小占位类型；API 项目保留最小启动入口和健康检查基础，不引入尚未实现的业务接口。

## 4. 目标目录

```text
IndustrialPlatform/
├── .github/
│   └── workflows/
│       └── build.yml
├── docs/
│   ├── blueprint/
│   │   ├── README.md
│   │   └── ...整理后的蓝图文档
│   ├── implementation/
│   │   └── ...修订后的 01–04 实施文档
│   └── superpowers/
│       ├── specs/
│       └── plans/
├── src/
│   ├── BuildingBlocks/
│   │   ├── IndustrialPlatform.SharedKernel/
│   │   ├── IndustrialPlatform.Application.Abstractions/
│   │   ├── IndustrialPlatform.Infrastructure/
│   │   ├── IndustrialPlatform.EventBus/
│   │   ├── IndustrialPlatform.Logging/
│   │   ├── IndustrialPlatform.Web/
│   │   └── IndustrialPlatform.Security/
│   └── Services/
│       ├── Identity/
│       │   ├── IndustrialPlatform.Identity.Api/
│       │   ├── IndustrialPlatform.Identity.Application/
│       │   ├── IndustrialPlatform.Identity.Domain/
│       │   └── IndustrialPlatform.Identity.Infrastructure/
│       └── ReferenceData/
│           ├── IndustrialPlatform.ReferenceData.Api/
│           ├── IndustrialPlatform.ReferenceData.Application/
│           ├── IndustrialPlatform.ReferenceData.Domain/
│           └── IndustrialPlatform.ReferenceData.Infrastructure/
├── tests/
│   ├── BuildingBlocks/
│   ├── Identity/
│   └── ReferenceData/
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── IndustrialPlatform.slnx
└── README.md
```

`src/backend/src` 不作为有效路径；仓库统一从根目录使用 `src/...` 和 `tests/...`。

## 5. 项目边界与引用

### 5.1 BuildingBlocks

- SharedKernel：领域原语和通用契约。
- Application.Abstractions：应用层接口，不依赖基础设施实现。
- Infrastructure：基础设施通用实现入口。
- EventBus：事件总线抽象入口。
- Logging：日志能力入口。
- Web：Web API 通用能力入口。
- Security：认证授权通用契约入口。

BuildingBlocks 项目不得依赖 Identity 或 ReferenceData。

### 5.2 服务项目

每个服务遵循 Api、Application、Domain、Infrastructure 四层：

- Domain 仅依赖 SharedKernel。
- Application 依赖 Domain 和 Application.Abstractions。
- Infrastructure 依赖 Application、Domain 及必要的 BuildingBlocks。
- Api 依赖 Application、Infrastructure 和 Web；API 不直接承载领域逻辑。

Identity 与 ReferenceData 之间不建立直接项目引用。跨服务协作未来通过公开契约或消息完成。

### 5.3 测试项目

测试按能力域分组，引用其被测项目。骨架阶段至少包含可运行的冒烟测试，以证明测试发现和 CI 配置有效。

## 6. 蓝图整理规则

- 原始资料保留在仓库外，不原地改写。
- 正式文档使用单一 `.md` 扩展名，移除文件名和正文标题中的多余 `.md`。
- 建立 `docs/blueprint/README.md`，记录文档编号、主题、状态、前置依赖和建议阅读顺序。
- 修正 04 实施文档正文中误写的 03 编号。
- 统一使用 `Api` 项目后缀，不混用 `API`、`WebApi`，除非蓝图明确记录兼容原因。
- 统一服务名称为 Identity、ReferenceData、MasterData；不把 ReferenceData 当作 MasterData 的别名。
- 清理失效路径、重复章节、相互矛盾的阶段编号和项目列表。
- 对超出当前阶段或尚未验证的内容标记为“规划”，不把它描述为已实现。

## 7. 构建与配置

- 所有项目目标框架统一为 `net10.0`。
- `global.json` 固定可用的 .NET 10 SDK 特性带，允许合理的补丁版本滚动。
- `Directory.Build.props` 统一启用 nullable、隐式 using、分析器和警告策略。
- `Directory.Packages.props` 负责集中包版本；骨架阶段只加入测试和实际需要的最小包。
- 不为尚未实现的组件预装大量依赖。
- GitHub Actions 在 Windows 或 Ubuntu 托管环境执行 restore、build 和 test。

## 8. 错误处理与可维护性

- 构建配置缺失或 SDK 不匹配时，验证应明确失败，不静默跳过项目。
- API 骨架只包含标准启动与健康状态，不返回伪造业务结果。
- 命名、路径或项目引用冲突在蓝图与工程中同步修正，避免文档和代码形成两套事实来源。
- 各项目保留最小职责说明，便于后续按实施文档逐项开发。

## 9. 验证与验收

本次工作满足以下条件才算完成：

1. `dotnet restore IndustrialPlatform.slnx` 成功。
2. `dotnet build IndustrialPlatform.slnx --no-restore` 成功且无非预期警告。
3. `dotnet test IndustrialPlatform.slnx --no-build` 成功，所有测试项目均被发现。
4. 项目引用符合第 5 节约束，无循环依赖。
5. 蓝图索引可以解释 ReferenceData 与 MasterData 的关系及开发顺序。
6. 正式文档没有 `.md.md`、错误编号、`src/backend/src` 或把 ReferenceData 等同于 MasterData 的遗留问题。
7. Git 工作区只包含本次预期文件，适合创建首个提交并后续上传 GitHub。

## 10. 后续阶段

本次骨架完成后，后续按以下独立周期推进：

1. BuildingBlocks 基础组件实现与测试。
2. Identity 服务实现与测试。
3. ReferenceData 服务实现与测试。
4. MasterData 服务设计校准与实现。
5. 前端、容器、部署和运行环境集成。

每个阶段应单独形成设计校准、实施计划和验收记录，避免一次性扩张范围。
