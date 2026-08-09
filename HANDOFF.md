# Industrial Platform 协作交接

更新时间：2026-08-09
当前分支：`develop`

## 1. 协作范围

- 本仓库以 `IndustrialPlatform/docs` 作为蓝图设计、实施方案和开发 TODO 的唯一维护源，仓库外层不再维护重复文档。
- 当前协作任务只负责蓝图设计、TODO 拆分与任务派遣、验收信息汇总和结果回写，不在本任务直接继续业务代码开发。
- 具体开发和测试在其他任务或派遣任务中执行；实现发现设计冲突时，先回到 `docs/blueprint` 修订并确认设计，再继续开发。
- `CLAUDE.md`、`.claude/` 属于其他任务上下文；`.tmp_apicheck/` 是临时目录，均不属于本次提交范围。

## 2. 当前架构基线

后端解决方案按独立服务组织：

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
BuildingBlocks → Identity → ReferenceData → MasterData → OperationalData
```

其中：

- `ReferenceData` 管理字典、配置、元数据和编码规则。
- `MasterData` 管理物料、设备、组织、仓库、库位和 BOM 等稳定主数据。
- `OperationalData` 是单一独立微服务，负责库存批次、库存余额、预留，以及领料、收料、退料、移库、调整等仓储业务单据。
- 未部署外部 WMS 时，`OperationalData` 提供轻量 WMS 能力；接入外部 WMS 时，通过仓库级 `Internal` / `ExternalWms` 模式明确库存权威来源，避免双写库存。
- `InventoryLot` 表示可用库存批次；`BatchRecord` 负责生产批记录与审计归档，两者职责分离。

## 3. 时间与数据库约束

- .NET 业务时间统一使用 `DateTimeOffset` / `DateTimeOffset?`。
- 获取 UTC 当前时间统一使用 `DateTimeOffset.UtcNow`。
- API 时间统一使用带 `Z` 或明确偏移量的 ISO 8601 / RFC 3339 格式。
- PostgreSQL 瞬时时间统一使用 `timestamp with time zone`（`timestamptz`），以 UTC 保存。
- 后续新增或修订的蓝图、TODO 和实现不得重新使用 `DateTime` 作为业务时间类型。

## 4. 已整理内容

- BuildingBlocks 已由其他开发任务形成 SharedKernel、Infrastructure、EventBus、Logging、Security、Web 的基础实现和测试；本协作任务仅汇总状态并合并已验证结果。
- 蓝图目录已增加 `14A-OperationalData Service详细设计.md`，并同步总体架构、DDD、数据库、事件、API、测试及上下游服务边界。
- `05-个人MES平台两年开发路线.md` 已从蓝图维护序列删除，路线和执行信息统一收敛到开发 TODO。
- `09-MES MVP第一阶段开发TodoList.md` 已加入 `TASK-OD-001` 至 `TASK-OD-009`，覆盖服务骨架、库存批次与余额、预留、收料、领料、退料、移库调整、WMS 对接和集成测试。
- 所有蓝图和实施方案后续沿用 `DateTimeOffset` / `timestamptz` 设计。

## 5. 后续派遣建议

1. 先按 TODO 完成并验收 `MasterData`，稳定物料、仓库、库位等依赖数据。
2. 再派遣 `TASK-OD-001`，创建单一独立的 `OperationalData` 微服务骨架和数据库边界。
3. 按 `TASK-OD-002` 至 `TASK-OD-009` 的依赖顺序逐项派遣；每项任务必须回写状态、验证命令、测试结果和设计偏差。
4. `WorkOrder`、`Weighting`、`Trace`、`BatchRecord` 开发前，复核它们与 `OperationalData` 的批次、库存和单据契约。

## 6. 交接验证

2026-08-09 最近一次验证结果：解决方案还原成功，构建 0 警告/0 错误，完整测试 74/74 通过（BuildingBlocks 64、Identity 5、ReferenceData 5）。

在后续开发任务接手前，从仓库根目录执行：

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet restore src/backend/IndustrialPlatform.slnx
dotnet build src/backend/IndustrialPlatform.slnx --no-restore
dotnet test src/backend/IndustrialPlatform.slnx --no-build
git diff --check
```

设计入口：

- `docs/blueprint/README.md`
- `docs/blueprint/09-MES MVP第一阶段开发TodoList.md`
- `docs/blueprint/14A-OperationalData Service详细设计.md`
- `docs/implementation/README.md`
