# PF-06A 自测与交接证据

日期：2026-09-20
工作树：`D:/Code/Industrial Platform/IndustrialPlatform-PF-06A`
分支：`pf-06a`
开发交接时 `HEAD`：`e64f59c74812b5663ffd80612e6d0543cad5aca1`
角色：Luna 双角色接管；本文件完成后停止继续写入，交由主控接手。

## 本次实现范围

- 前端新增扫码运行时契约：单活跃会话、sessionNId/subjectEpoch 防旧回调、键盘/广播/相机来源、4096 字符边界、清理与错误事件。
- 新增共享 `AppScanInput` 与 `ScannerTestPage`，PC/PDA 仅保留薄路由入口；PC `/pc/terminal/scanner-test`、PDA `/pda/terminal/scanner-test`。
- 接入真实菜单、路由、双语文案与 `runtime.read`/`runtime.configure`/`runtime.update` 权限目录；测试页只读授权，配置权限不足时禁用测试输入。
- 新增 Web 更新协调器状态机：启动/重新登录门禁、检查、下载、验签、激活、健康确认、回滚与恢复态；业务期不允许下载/激活。
- 后端补齐权限目录、首批种子、策略注册、系统数据基线版本与默认导航测试入口；未触碰 SQL 生产代码。

## WIP 边界

以下文件在本轮开始前已经存在未提交改动，保留且不纳入本次实现归属：

- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/34-Industrial Platform终端运行时与客户端架构.md`
- `docs/implementation/09A-Industrial Platform终端运行时与客户端打包开发实施方案.md`
- `docs/implementation/README.md`
- `docs/implementation/STANDARD-派遣前详细设计与页面验收.md`
- `docs/implementation/details/PF06A-SQLServer2019-2022接入补充规格.md`
- `docs/implementation/details/PF06A-运行时字段与页面规格.md`
- `docs/status/CURRENT.md`
- `docs/tasks/pending/PF-06A.md`
- `docs/tasks/pending/README.md`
- `src/开发注意事项-数据库拓扑与本地配置.md`
- `docs/implementation/details/PF06A-运行时字段字典.md`
- `docs/tasks/active/PF-06A.md`

## 验证矩阵

| 命令 | 结果 |
|---|---|
| `& '.\\node_modules\\.bin\\vue-tsc.CMD' --build`（`src/frontend`） | exit 0 |
| `& '.\\node_modules\\.bin\\vitest.CMD' run tests/unit/scannerRuntime.spec.ts tests/components/AppScanInput.spec.ts tests/unit/updateCoordinator.spec.ts` | 3 files / 11 tests passed，exit 0 |
| `& '.\\node_modules\\.bin\\vitest.CMD' run`（`src/frontend`） | 141 files / 1074 tests passed，exit 0 |
| `& '.\\node_modules\\.bin\\vite.CMD' build`（`src/frontend`） | 2395 modules，exit 0；仅有 chunk >500 kB 提示 |
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 0 warnings / 0 errors，exit 0 |
| `dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --no-build` | 621 passed / 0 failed，exit 0 |
| `dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release --no-build` | 633 passed / 0 failed，exit 0 |
| `git -c safe.directory='D:/Code/Industrial Platform/IndustrialPlatform-PF-06A' diff --check` | exit 0；仅有 LF/CRLF 转换提示 |

## 真实环境验证边界

- 本机未发现 `java`、`adb`；工作树没有 Electron 客户端目录、Capacitor Android 工程或对应依赖。因此 Windows Electron 安装→登录→扫描、Android 真机安装→登录→相机扫码、原生更新包/APK、冷启动/重新登录更新门禁均记为 `BLOCKED/NOT RUN`，没有伪造通过结果。
- 本轮只完成并验证 Web DOM/运行时契约与路由菜单；真实相机设备、厂商广播、PDA 广播和原生桥接未验证。Android 手机相机优先，PDA 广播留待后续阶段。
- SQL 009 仍处于设计闸门；本轮未改 SQL 生产代码、未执行数据库迁移。

## 未提交状态

- 不执行 commit，不执行 push，不改变主控工作树。
- 构建生成的 `bin/`、`obj/`、前端 `dist/` 属于可再生产物，不作为源代码证据。

## SHA-256 清单

清单使用 PowerShell `Get-FileHash -Algorithm SHA256` 对本轮实现/测试文件逐文件计算；本证据文件自身不纳入清单，避免自引用。最终值由交接前补入。

<!-- HASHES_START -->
```text
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Authorization/PermissionPolicies.cs 28E3C70984F7B51951510FE77265C1A4B3A52C16614F38350DBCF8EA4FDE78B8
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Bootstrap/BootstrapSeedCatalog.cs 1B68307E384133935703A26ECDF767778299D11F4E53E1040AE16821FCA3C91E
src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs EB8B80D6AE02B7519338A95063AD8A0636C6267F4C12A7C1B4D68ABC761A94E4
src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/ControlPlane/ControlPlaneServices.cs 3409EC0FA6940C4F536DCA68CFCE8AF595DDB7CF21ADD06BF63BC8A1B34DBAD9
src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SystemDataBaselineSeedRunner.cs 8E703699CEF3829F9F6ABA645B23C985F010C7737797EFE18DBF6134F90DFD86
src/frontend/src/components/home/TerminalFeatureMenu.vue B199E8FE98BE22C3F3F518EFAA6E5361446C41DDA2C7AC26959FAC737581696F
src/frontend/src/components/navigation/navigation.ts FAFA0256704C4B0FEF08A939C224FBD1504F7C4325372DAEEA121B5561A09341
src/frontend/src/locales/en-US.ts EB5ED8C97A80CBB6BE26E719A0A6F8473A5C22D81F29FFA854839EA4BC67DC26
src/frontend/src/locales/zh-CN.ts 9F12419239BC02E689E12B269C023C1286DF14D9934D29EF2B5ABCB5C44CECA8
src/frontend/src/permissions/catalog.generated.ts 37214A1382499A0832A4F9BE4339D693C80A2E6412C7D5E5954CCA2DED3DD282
src/frontend/src/permissions/catalog.ts 57876B552F1C35B7FF86BB042E8EA8729F6BEB19F5F18CC9CA8FA959CA066A0B
src/frontend/src/router/routeNames.ts 6305DFED4F87399373040D2110FEC1B47B516AF60E5C0A229555A2FD19B49E52
src/frontend/src/router/routes.ts 1FC1773CDF5948C983C9C9276679F5FB8B341D3178A2FC67D373D07745B033C4
src/frontend/src/components/runtime/AppScanInput.vue 40123E7F7852497D9F737EC0C9A5803CB54D158830DF44CEEC2D4DF70C95B341
src/frontend/src/components/runtime/ScannerTestPage.vue 2433912949FBF4FC109DC0D7ED9F63EE6F772625FAC7E916C8B956C045C393A8
src/frontend/src/pages/pc/RuntimeScannerTestPage.vue 73CA26AEE0AF4C85A572FE3F1C94A3A15B55B47D4A9F8CD97F4B71A872D5C25C
src/frontend/src/pages/pda/RuntimeScannerTestPage.vue CD8FC2BCE667BC6EE0B9206223E3FE748B471135C224FC939F1E3EA979C74901
src/frontend/src/platform/runtime/scannerRuntime.ts 60686521971BE17684C13F4B9CA7AC54BD053DB64E12B6721936FC0CBF06CE7F
src/frontend/src/platform/runtime/updateCoordinator.ts B5F3C455BC657687E94AC320664837153AF382D507DA5C6290A04D08F626C650
src/frontend/tests/components/AppScanInput.spec.ts 435942393E8B47DFC81B2A9B5880670DBCE88BECD62AE7E2AD8CA438A97F0308
src/frontend/tests/unit/navigation.spec.ts EB5C150EE2439535AE27A35638594349649B44B8BBD1A55F423401968ED73F4F
src/frontend/tests/unit/scannerRuntime.spec.ts A3198011ED41A62AFD73394800CEE5D4711E5B85B2DF3D8766F0BE6AD83DC29E
src/frontend/tests/unit/updateCoordinator.spec.ts FA6767D9CD9486C56BB33F7E3E5232E00FC1B35E3FD2B66571B72A39C476ACF7
tests/Identity/IndustrialPlatform.Identity.Tests/Application_BootstrapSeedCatalogTests.cs 4089DC454D9319FC45550FF5B17E2DBD1D9256130C0CEEFC5791B93432C49540
tests/Identity/IndustrialPlatform.Identity.Tests/Domain_PermissionCatalogTests.cs 4698440540F4B49C9C718F1CA2654500E9773A30910DE86684DA9F05E27C99E6
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_IdentityMigrationTests.cs ADBD9A3F59866B1611E9B1CBC8260E483B26747475D5D5AEC5F5670B57183019
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_IdentityRepositoryTests.cs 7A7AEE7152E95CA2C29228E6382B4553FBC4224E932ECFC36624956E6C0B2FA2
tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_IdentitySeedRunnerTests.cs 576B639A222BE990C317B33E9C5842C53835744FC4969746E05959E879210E88
tests/SystemData/IndustrialPlatform.SystemData.Tests/Application_ControlPlaneServiceTests.cs E3F42C014D9D83C2765BD34D3259F686FC3FC886C8B82AFABD9305DC2ABACE1C
tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SystemDataBaselineSeedRunnerTests.cs 238EB554342FD79D6A139450D0FD8119193BD3A98FEC8A309924E53EE649D70B
```

清单聚合 SHA-256（按上述行顺序、以 LF 连接）：`7A11C85F98E8A4F03129D8E63485736B47C0A13ED738B9758646838BB0E0D415`。
<!-- HASHES_END -->

## 主控合并前核验

- 30 个交接文件的 SHA-256 均与上方清单一致。其中 `tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SystemDataBaselineSeedRunnerTests.cs` 与基线提交内容相同，因此没有形成 Git 变更；其余实现与测试文件及本证据文件单独暂存。
- 新鲜 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：exit 0，0 警告、0 错误。
- 首次全方案测试发现 UnifiedHost 测试仍将资源清单版本断言为 `3`，与本轮增量种子的版本 `4` 不一致。主控只修正 `tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/UnifiedHostTests.cs` 的该断言；重新 Release 构建 exit 0，定向用例 1/1 通过。
- 并行重跑全方案时 ReferenceData 有 1 个临时 SQLite 初始化用例失败；该用例单独 1/1、ReferenceData 全组 261/261 均通过。随后使用 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build -m:1` 顺序运行，exit 0：8 个测试项目合计 1898 通过、0 失败、7 个外部环境用例跳过。并行失败未复现，不能计为外部集成已通过。
- 开发任务的前端类型检查、141 文件/1074 用例与 Vite 构建证据沿上表复用；主控没有修改前端源码，不重复运行。
- 本次仍只交付 Web 扫码与更新协调器及菜单/权限基础。Electron/Capacitor 原生外壳、真实设备扫码、安装包与真实热更新未实现或未验证；PF-06A 001/005 不标记整包通过。SQL Server 009 设计门禁仍未关闭。
