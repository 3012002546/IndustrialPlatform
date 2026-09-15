# PF06 独立协作宿主标准化交接证据（历史首轮快照）

> 注意：本文是首轮交接快照，包含当时“fresh build 未完成”等历史状态，不是当前源码的最终证据。最新开发交接、当前源码哈希、构建/测试结果及七项返修对应入口请读取 [PF06 最新开发交接](PF06-latest-development-handoff-20260914.md) 与 [最新交接清单](PF06-latest-development-handoff-manifest-20260914.txt)。独立验收结论文件不在本文修改范围内。

时间：2026-09-14（Asia/Taipei）  
工作区：`D:\Code\Industrial Platform\IndustrialPlatform`  
基线 HEAD：`0a77597f94e38873b67565e0b477e120e72a57f8`  
状态：dirty-worktree 交接；未提交、未推送、未清理既有 WIP。

## 本轮完成

- `dev` 保持 HTTP localhost，`dev:lan` 保持 HTTP 所有 LAN 地址；只有 `dev:lan:https` 与 `dev:lan:https:collaboration` 启用 HTTPS。
- 协作开发页支持 `?account=xxA|xxB|xxC`，无参数默认 xxA；重复或未知值由前后端双重拒绝，不回退到 A。
- FixedDemo 后端目录与 source session 按账号映射，演示响应返回页级 token/binding；HTTP 使用 `X-Embedded-Session`/`X-Embedded-Binding`，SignalR 使用页级凭据，不把账号当正式认证凭据。
- 页级内存凭据、应用实时连接、Presence 续租、媒体邀请与 60 秒会话租约分离；页面停止后 bounded expiry，首个有效媒体接受者获胜的既有规则保留。
- 新增 `src/backend/appsettings.Standalone.Development.local.example.json` 与 `StandaloneConfiguration.Apply`；SQLite 默认、PostgreSQL 可选、SQL Server 拒绝，单一数据库输入投影到 SqlSugar/DatabaseTopology，不读取平台 local 配置。
- 独立启动初始化复用已有初始化器，Identity → SystemData → ReferenceData → Collaboration，具备 inspect/plan/apply/verify 的增量、幂等和并发安全边界；正式 MES 模式也可启用，不限于 FixedDemo。
- EmbeddedHost README 和前端 DEPLOYMENT 已记录实际类/方法、启动命令、URL、HTTP adapter 示例、配置和未实现的独立裁剪产物边界。

## 定向验证

| 命令 | 结果 |
| --- | --- |
| `vue-tsc --noEmit --pretty false -p tsconfig.app.json --tsBuildInfoFile %TEMP%\\industrial-platform-pf06-standalone.tsbuildinfo` | PASS，exit 0 |
| 定向 ESLint（本轮认证、HTTP、Hub、运行时及 4 个测试文件） | PASS，exit 0 |
| `vitest run --configLoader runner tests/unit/runtimeConfig.spec.ts tests/unit/embeddedAuthGateway.spec.ts tests/contract/httpClient.spec.ts tests/unit/collaborationHub.spec.ts` | PASS，4 files / 51 tests |
| `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~Security_EmbeddedHostAdaptersTests` | PASS，4 tests |
| 三个宿主/模板 JSON `ConvertFrom-Json` | PASS |
| 目标文件 `git diff --check` | PASS |

## 环境边界

按仓库协作约定尝试了 fresh Release build；现有后台 `dotnet`/IDE 相关进程持有宿主和测试项目 `obj/bin` 文件，出现 `UnauthorizedAccessException`/`CS2012`，不是源码编译诊断。未终止用户进程、未重启 VS/Vite/容器、未删除 bin/obj；因此本证据不宣称 fresh full-solution build PASS。此前定向后端测试使用的 Release 输出在本轮生产改动编译后通过；文档和末尾小型参数清理之后，完整 fresh compile 仍需在文件锁释放后执行。

未执行：真实浏览器三页身份/媒体现场验收、MES 真实适配器、生产发布、Redis/PostgreSQL 连通性、全量 backend build/test、完整 frontend build。固定演示和前端定向契约不替代这些运行时证据。

## 复核入口

- 文件 SHA-256 清单：[PF06-standalone-collaboration-manifest-20260914.txt](PF06-standalone-collaboration-manifest-20260914.txt)
- 后端说明：[EmbeddedHost README](../../src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/README.md)
- 前端说明：[DEPLOYMENT.md](../../src/frontend/DEPLOYMENT.md)
