# PF06 独立宿主登录响应修复验证

日期：2026-09-15。基线：`ae22f6a`。用户确认平台 PC 实测通过后，仅恢复独立宿主登录问题的验证。

## 原因与修复范围

实际 EmbeddedHost 的 FixedDemo POST 返回原始 DTO，而 MVC 的 session/heartbeat 返回 `{ success, code, message, data }`。此前 `embeddedAuthGateway` 将后者直接作为会话 DTO，读取不到顶层 identity，成功的 HTTP 200 被当成登录失败。

当前基线已经包含 `embeddedAuthGateway.ts` 使用既有 `parseEnvelope` 解析 data 的修复，以及对应单元测试。本轮没有追加业务代码修改，未改平台认证、聊天、媒体、数据库布局或配置。

## 实际执行

- `dotnet build src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost/IndustrialPlatform.Collaboration.EmbeddedHost.csproj --configuration Release --no-restore --verbosity quiet`：exit 0，0 warning / 0 error。
- `vitest run --configLoader runner tests/unit/embeddedAuthGateway.spec.ts`：exit 0，10/10。
- 临时宿主使用本地端口 55965、Development 配置与 `.run/pf06-entry-check/entry.db` 隔离 SQLite；未连接平台数据库、未接管用户调试服务。
- 临时 Vitest 探针导入真正的 `createEmbeddedAuthGateway`，fetch 请求实际宿主，不 mock HTTP 响应。模拟浏览器 Origin 为 `https://localhost:5173`，实际 HTTP 传输使用本机临时端口。
- xxA、xxB 分别完成初始 session 401 → demo POST 200 → session GET 200；两账户身份及页级 token 不同；A 再次恢复、A/B 心跳返回正确身份；A 注销后 B 仍能恢复自己的会话。定向探针 exit 0，1/1。
- 完成后已结束本轮临时宿主，并移除临时自动测试入口，避免普通单测依赖运行中的宿主。隔离诊断数据库未提交、未删除用户数据。

## 结论边界

独立入口响应解析及真实 HTTP 登录/恢复/续期/单页注销隔离已验证。没有重复执行平台 PC 已通过矩阵，也没有把本机 HTTP 探针描述为手机/跨网/TURN 或真实 MES 联调。HTTPS 浏览器页面现场效果仍由实际启动的 Vite 和用户宿主决定。

启动命令保持 `pnpm dev:lan:https:collaboration`，入口保持 `https://localhost:5173/pc/collaboration?account=xxA`；后端为 `IndustrialPlatform.Collaboration.EmbeddedHost`。
