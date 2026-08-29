# PF03 前平台能力收束开发者完整自验证

记录时间：2026-08-30（Asia/Taipei）
实际工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
分支：`develop`
启动基线：`eadad6224622635db9f0cc91792ae07c2bf05179`（`origin/develop` 同值，ahead/behind `0/0`）
功能代码验证结束 HEAD：`4a6dbe746ef9d077166e8d84d487b03f194deed6`
最终证据提交：本文件和开发计划状态所在的后续提交；交付时以 `git rev-parse HEAD` 为准。
本轮不 push。

## 范围与执行封存

Task 1～15 均已按开发计划执行；Task 1～14 的代码/文档提交已完成，Task 15 的完整门禁、真实环境复现、视觉复核和工作树核对已完成。各工作包只提交自身文件，没有暂存启动时的保护性 WIP。

| Task | 结果 | 对应提交/证据 |
| --- | --- | --- |
| 1 | 通过 | `d1539f0`；启动基线见 `pf03-pre-platform-convergence-baseline-2026-08-30.md` |
| 2 | 通过 | `b67479b`；前端 locale 资源契约与运行时测试 |
| 3 | 通过 | `45af0b4`；品牌 SVG/组件测试 |
| 4 | 通过 | `9dbae11`；PC Header/命令搜索/上下文与锁屏路径 |
| 5 | 通过 | `ea62e0e`；业务域导航与三态验证 |
| 6 | 通过 | `bc221ce`；生产操作模式、权限和 3×3 宫格 |
| 7 | 通过 | `51d7b3b`；Tabs、标题与会话页状态 |
| 8 | 通过 | `e4872b4`；统一 QueryDescriptor/serializer |
| 9 | 通过 | `5cec4de`；AppPage/AppQueryPanel/AppDataTable 契约冻结 |
| 10 | 通过 | `f8c53f6`；Querying/OData/SqlSugar 受控适配器 |
| 11 | 通过 | `cad3279`；Identity Users 受控 OData 样板 |
| 12 | 通过 | `708339f`；用户管理黄金页结构与测试 |
| 13 | 通过 | `283730f`；权限目录、Gate 与稳定错误码 |
| 14 | 通过 | `b73799f`；公共文案、能力边界和 PF-04 决策门 |
| 15 | 通过（真实外部项按缺口记录） | `4a6dbe7`；本文件验证记录 |

## 命令门禁

下表是本轮实际执行的最终/定向命令。退出码来自命令进程；`skipped` 是测试框架报告的测试跳过数，不把外部依赖缺口写成通过。

| 时间（约） | 命令 | 退出码 | 结果 |
| --- | --- | ---: | --- |
| 04:47 | `pnpm.cmd test:unit -- tests/components/AppDataTable.spec.ts tests/components/IdentityUsersPage.spec.ts tests/components/PcLayout.spec.ts`（启动基线） | 0（受控权限重试） | 533 passed / 0 failed / 0 skipped；普通沙箱首次因 Vite `.vite-temp` EPERM 为 1，非断言失败 |
| 04:47 | `dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release`（启动基线） | 0 | 131 passed / 0 failed / 0 skipped |
| 工作包期间 | locale/shell/table/黄金页定向 Vitest | 0 | 各工作包 RED→GREEN；Task 14 受影响定向最终 9 files / 82 tests passed |
| 工作包期间 | backend Querying/Identity/权限/SystemData 定向测试 | 0 | Querying 140、Identity Users 4、BuildingBlocks 142、Identity 全量 575、SystemData 全量 543 均 passed |
| 04:52 | `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`（Task 15 fresh build） | 0 | `已成功生成`；0 warnings / 0 errors |
| 04:53 | `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` | 0 | BuildingBlocks 142、Gateway 14、Integration 10、UnifiedHost 20、Identity 575、ReferenceData 14、SystemData 543；合计 1318 passed / 0 failed / 3 skipped |
| 04:54 | `pnpm.cmd lint` | 0 | ESLint 完成，无失败 |
| 04:54 | `pnpm.cmd typecheck` | 0 | `vue-tsc --build` 完成，无失败 |
| 04:55 | `pnpm.cmd test:unit` | 0 | 86 files / 603 tests passed / 0 failed / 0 skipped |
| 04:55 | `pnpm.cmd build` | 0 | Vite 8.2.1，2444 modules；仅有大 chunk 警告，无构建失败 |
| 04:56 | `pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/pc-shell.spec.ts tests/e2e/pc-operation-mode.spec.ts tests/e2e/workspace-tabs.spec.ts tests/e2e/user-management-golden.spec.ts tests/e2e/systemdata-admin.spec.ts` | 0 | 15 passed；默认 Mock 配置按约定忽略 real-only `user-management-golden.spec.ts` |
| 04:57 | `pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/pc-shell.spec.ts --grep "两个 PC 目标视口无横向滚动并保存外壳截图"` | 0 | 1 passed；修复前完整目标集曾为 14 passed / 1 failed，已由 `4a6dbe7` 修复 1280 header 7px 溢出 |
| 05:08–05:10 | `pnpm.cmd exec playwright test --config=playwright.real.config.ts tests/e2e/user-management-golden.spec.ts` | 1 | 1 failed / 1 retry failed；登录请求网络不可达，页面停留 `/login`，作为外部环境缺口，不是代码通过项 |

Element Plus 的既有 `[el-checkbox] label act as value ... deprecated in version 3.0.0` 仅为控制台 warning，不造成测试失败；本轮未把它扩大为范围外重构。

## 真实 UnifiedHost / Users OData 代表路径

真实配置读取自受保护的 `src/backend/appsettings.Development.local.json`，只验证键名和开关，不输出私有地址、用户名、密码或密钥：`RemoteDevelopment.Enabled=True`、DatabaseTopology=`Development/Shared`、共享逻辑库 `industrial_platform_dev`。该文件未修改、未暂存、未提交。

1. 使用真实 Development 配置启动 UnifiedHost：
   `dotnet run --project src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/IndustrialPlatform.UnifiedHost.csproj --configuration Release --no-build`
   - 启动进程进入模块初始化，但 PostgreSQL 连接失败，随后 UnifiedHost 退出；日志证据为 `SqlSugar ... PostgreSQL ... Connection open error` 和初始化阶段异常。
   - 同一启动输出还报告当前 Windows 用户无法解密既有 ASP.NET Data Protection DPAPI key；未改动密钥目录。
2. 使用仓库已有显式 Development SQLite 回退尝试同一 UnifiedHost：
   `IndustrialPlatform__DevelopmentInfrastructureMode=Sqlite dotnet run ... --no-build --no-launch-profile -- --urls http://127.0.0.1:5041`
   - 进入 SQLite 初始化，但由于本机没有既有数据库且 SQLite 报 `SQLite Error 14: unable to open database file`，宿主仍退出；没有造假用户、假服务状态或修改配置。
3. 代表 HTTP 路径实测：
   - `GET http://127.0.0.1:5041/health/ready`：连接被积极拒绝。
   - `GET http://127.0.0.1:5041/identity/api/v1/odata/users?$top=1`：连接被积极拒绝。
   - 控制器真实路由代码仍为 Identity 自有 `odata/users`；目标实现使用平台 `ApiResult<PageResult<...>>`，不返回 `IQueryable`、不使用 `[EnableQuery]`。由于宿主未达到可监听状态，本轮不能声称 HTTP 200 或真实数据通过。
4. 依赖可达性证据（私有主机名不写入本文件）：
   - PostgreSQL `5432`: `TcpTestSucceeded=False`
   - Redis `6379`: `TcpTestSucceeded=False`
   - RabbitMQ `5672`: `TcpTestSucceeded=False`
   - 私有主机 DNS：解析失败（PowerShell `Resolve-DnsName` 返回反向 DNS 名称不存在）
   - 本机 Gateway `127.0.0.1:5080`: `TcpTestSucceeded=False`

真实 real Playwright 同样在 `/identity/api/v1/bootstrap/status` 与 `/identity/api/v1/auth/login` 记录 `kind=network`，因此黄金页真实登录、数据加载和真实截图均标记为 blocked by external environment；Mock 截图和组件/契约测试不替代该结论。

## 视觉、键盘和边界复核

- 已人工查看 `src/frontend/tests/e2e/screenshots/pc-shell-1280x720.png`、`pc-shell-1440x900.png`：PC 管理 Shell 在两个目标宽度无横向滚动；1280 宽度信息密度较高但可用，1440 宽度布局稳定。
- 已人工查看 `src/frontend/tests/e2e/screenshots/pc-operation-1280x720.png`：3×3 大卡片、一跳入口、高对比图标/标题层级符合布局思路；八个未来项均显示“待实现”，只有“界面设置”为可用项。该截图是启动时已存在的保护性输入，未覆盖、未提交。
- `pc-shell.spec.ts` 覆盖 1280×720、1440×900 无横向滚动与截图；`pc-operation-mode.spec.ts` 覆盖生产模式入口、权限切换、禁用卡片、无路由/无请求和键盘行为；`workspace-tabs.spec.ts` 覆盖页签键盘/状态路径；组件测试覆盖 `Ctrl+K`、Escape、焦点回收、无权限和错误态。
- `user-management-golden.spec.ts` 的 Mock 配置被明确排除；real 配置已实际执行并因外部登录不可达失败，故未伪造黄金页截图。

## 静态边界与保护证明

- 业务页及平台组件除应用 bootstrap、`AppDataTable.vue` 和 `vxeDomAdapter.ts` 外无直接 VXE 引用；专门扫描退出码 0（无匹配）。
- `TODO/TBD` 交付占位扫描无匹配（rg 的“无匹配”退出码为 1，语义为通过）。
- `git diff --check` 退出码 0；仅报告既有工作树 CRLF 转换 warning。
- 启动时与当前 `DSH.md` SHA-256 都是 `D2F2ED4CB3D01B65224BE1E1314A8B30D3229B3605C2BA6641A20E11522C0BD0`；`DSH.md` 仍保持启动前的 unstaged protected modification。
- 提交 delta `eadad622..HEAD` 没有 `ReferenceData`、独立 MES、Audit、File、Notification 路径；本轮只在能力边界文档中写了 PF-04 re-decision gate，没有安装依赖、生产接口或未来实现。当前工作树中既有 Identity Audit、SystemData 和导出 WIP 是启动前保护输入，未被本轮提交。
- 启动时的 `docs/prototypes/`、AppDataTable、Identity/SystemData `Export/`、锁屏和既有测试均保留；未使用 `git reset`、`git checkout`、`git clean`，未 push。

## 交付时工作树状态

功能代码最后一次提交 `4a6dbe7` 时：`git status --porcelain=v1` 为 39 个 tracked unstaged、0 staged、15 untracked；untracked 主要是启动前保护 WIP 与原型输入。提交本证据和计划后，状态为 39 个 tracked unstaged、0 staged、14 个 untracked；不应把它称为 clean。

交付前再次执行并报告：

```text
git rev-parse HEAD
git branch --show-current
git rev-list --left-right --count origin/develop...HEAD
git status --short
git status --short --ignored
```

ignored 的 `bin/`、`obj/`、`node_modules/`、`dist/`、`playwright-report/`、`test-results/` 和本地配置均为可再生或私有运行时文件，未进入提交；本轮启动的端口 `4173/5041/5080` 最终均无监听，验证期间启动的 Vite/UnifiedHost 进程已停止。由于 Git 用户 global excludes 无读取权限，`git status` 会重复报告该既有 warning；不影响工作树条目读取。

最终验收应以独立任务重新执行门禁为准；本记录对真实外部环境缺口保持失败/阻塞事实，不将其包装为通过。
