# PF06 LAN HTTP 协作媒体/消息定向验证

- 日期：2026-09-14（Asia/Taipei）
- 根因：LAN HTTP 非安全上下文中 `crypto.randomUUID()` 不可用，聊天生成 `clientMessageNId` 在 REST/Hub 调用前抛错；协作媒体请求/协商 ID 也存在同类直接调用。
- 修复：复用 `createCorrelationId()`，保留 `randomUUID` 路径；无该 API 时使用 `crypto.getRandomValues` 生成 UUID v4，再按原调用约定移除连字符。媒体采集入口在 `isSecureContext === false` 时返回本地化 `MEDIA_SECURE_CONTEXT_REQUIRED`；屏幕采集 API 缺失仅影响屏幕采集，不阻断语音捕获。
- 定向测试：`tests/unit/correlation.spec.ts`、`tests/unit/collaborationMediaError.spec.ts`、`tests/components/CollaborationChat.spec.ts`、`tests/unit/collaborationMediaStore.spec.ts`；结果 4 files / 66 tests passed。
- 类型/lint：`vue-tsc --noEmit`、`tsc --noEmit`、受影响生产文件 ESLint、`git diff --check` 均通过。媒体测试文件仍有 4 个既存 fake WebRTC 未使用参数 lint 告警，未为此扩大范围。
- 未验证：尚未在华为/Android 实机重新打开 `http://10.13.49.141:5173/mobile`，因此真实浏览器网络、设备权限及该浏览器是否提供屏幕捕获 API仍需现场复验。
