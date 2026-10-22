# Studio LowCode 前端

`studio` 构建使用独立入口 `src/studio-main.ts`，仅登记 Core / LowCode 页面；不装配平台控制面、协作入口或 PF09 产品页面。原生平台使用既有入口和 JWT client，Studio 页面经 `/api/v1/studio` 调用。

独立构建：`pnpm build:studio`，产物目录 `dist-studio/`。由 PlatformStudio Host 的 HTTPS 同源静态托管提供这些文件及 SPA 回退，打开 `/pc/studio/low-code`。身份会话使用 HttpOnly Cookie；所有写请求自动附带 `/auth/csrf` 返回的 `X-Studio-CSRF`，浏览器不保存认证令牌。

本地前后端分开调试可运行 `pnpm dev:studio`；Vite 将 `/api` 代理到 `.env.studio` 的 `DEV_STUDIO_API_PROXY_TARGET`（默认 `http://localhost:5258`）。安全 Cookie 生产与完整登录验收使用 HTTPS Host 同源托管，不以普通 HTTP 跨源 Cookie 代替验收。

LocalAdmin 使用服务器创建的真实管理员登录，首次登录强制改密，再重新登录。创建/重置管理员沿 Host 命令，不在前端内置密码。MES / OIDC 配置在 `.env.studio.local` 设置 `VITE_STUDIO_IDENTITY_PROVIDER=MesCode` 或 `Oidc` 后重新构建，登录按钮跳到 Host `/api/v1/studio/auth/login`。可选 `VITE_API_BASE_URL` 仅设置受信同源路径；生产需保持身份 Cookie 与 API 同源。

第一条验收链：新建并发布台账模型 → 新建 CRUD 页面，绑定固定模型修订 → 保存、试运行、发布 → 运行页新增、编辑、查询、删除与恢复记录。Compose 使用结构/属性/字段/动作/数据实例面板；Code 使用登记的 `studio.summary`。数据源使用服务器登记端点键，允许对象必须填写类型和限定名称；前端没有连接串、凭据或任意动作 URL。

资源修改失败保留草稿；409 提供服务端比较与另存。写动作超时或 Unknown 状态禁止直接重发，可查询操作键。附件复用公共分片上传协议，经 Studio File 门面、扫描和个人引用授权后才可保存。
