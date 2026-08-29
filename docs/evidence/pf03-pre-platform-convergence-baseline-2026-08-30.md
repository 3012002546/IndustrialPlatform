# PF03 前平台能力收束启动基线

记录时间：2026-08-30（Asia/Taipei）  
实际工作树：`D:\Code\Industrial Platform\IndustrialPlatform`  
分支：`develop`  
启动 HEAD：`eadad6224622635db9f0cc91792ae07c2bf05179`  
`origin/develop`：`eadad6224622635db9f0cc91792ae07c2bf05179`  
启动 ahead/behind：`0/0`

## 工具版本与仓库状态

| 项目 | 启动值 |
| --- | --- |
| Node | `v24.18.0` |
| pnpm | `11.16.0` |
| .NET SDK | `10.0.400` |
| staged 文件 | `0` |
| tracked unstaged 文件 | `60` |
| untracked（`git ls-files --others --exclude-standard`） | `12814` |
| ignored 状态行 | `99905`（Git 同时报告无法读取用户 global excludes） |
| `DSH.md` SHA-256 | `D2F2ED4CB3D01B65224BE1E1314A8B30D3229B3605C2BA6641A20E11522C0BD0` |

启动时 `DSH.md` 已为受保护的 unstaged 修改；本任务不改、不暂存、不提交该文件。启动时没有 staged 输入。

## 受保护输入快照

以下 WIP 在任务启动前已存在，后续必须保留并在重叠文件上增量修改：

- `src/frontend/src/components/management/AppDataTable.ts`
- `src/frontend/src/components/management/AppDataTable.vue`
- `src/frontend/src/components/management/download.ts`
- `src/frontend/src/components/shell/AppLockOverlay.vue`
- `src/frontend/src/stores/lockStore.ts`
- `src/frontend/tests/components/AppDataTable.spec.ts`
- `src/frontend/tests/unit/lockStore.spec.ts`
- `src/frontend/tests/unit/systemDataRuntimeCoordinator.spec.ts`
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Export/`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Export/`
- 已修改的 Identity/SystemData 控制器、管理存储、前端管理页、`PcLayout.vue`、`PcWorkspaceTabs.vue` 及对应测试。

任务启动时还存在 `docs/prototypes/user-management-table-layout/` 及其生成的 `node_modules/`、`dist/`；原型目录为只读输入，生成物不属于本任务交付。

## 启动前定向验证

1. `pnpm.cmd test:unit -- tests/components/AppDataTable.spec.ts tests/components/IdentityUsersPage.spec.ts tests/components/PcLayout.spec.ts`
   - 首次沙箱运行：退出码 `1`，Vitest 启动阶段 `EPERM`，无法写入 `src/frontend/node_modules/.vite-temp/vitest.config.ts.timestamp-*.mjs`；无测试断言失败。
   - 同一命令受控权限重试：退出码 `0`，`62` test files、`533` tests，`533 passed / 0 failed / 0 skipped`。
2. `dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release`
   - 退出码 `0`，`131 passed / 0 failed / 0 skipped`。

基线结论：既有定向测试本身通过；前端普通沙箱的临时目录写入限制已被单独记录，不归因于代码回归。未发现 `dotnet`、`testhost` 或 `VBCSCompiler` 进程占用构建目录。

## 保护规则

不使用 `git reset`、`git checkout` 或 `git clean`；不修改 `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/prototypes/`；不提交 `node_modules/`、`dist/`、`bin/`、`obj/`、`TestResults/`、日志或缓存；不 push。
