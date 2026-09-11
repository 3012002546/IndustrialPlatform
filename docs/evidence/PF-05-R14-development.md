# PF-05 R14 开发交接

时间：2026-09-10

## 实现范围

- 顶栏聊天入口移动到真实通知按钮左侧；未读 badge 使用 Store 的独立 `unreadConversations` 投影，不会以 `unreadOnly=true` 响应覆盖聊天页完整会话列表；刷新采用单飞、尾随合并和请求代次丢弃旧响应。
- 会话列表使用固定 72px 行、40px 头像/在线点、单行安全预览、固定时间和未读区域。
- 摘要接口增加安全的 `lastMessagePreview`；文本最多 160 字符，隐藏、撤回和合规限制不返回正文。消息与成员均按当前页批量读取，不增加逐会话消息历史或成员查询。
- PC 本人消息移除可见“更多”按钮，保留右键与键盘上下文菜单；菜单会在外部按下、Escape、切换会话或关闭抽屉时关闭。
- 发送、ACK、实时消息仅滚动当前可见聊天表面；历史前插保留滚动锚点；活动表面在有文档焦点时合并已读请求并同步 Store/topbar。撤回不会触发滚动；右键菜单使用固定定位并按视口上下翻转、左右收束。
- 会话摘要按 `messageStateVersion` 单调合并。同序的高版本摘要可以前进；低版本迟到 ACK 无法复活已撤回正文。个人隐藏同时抑制消息面和摘要面，迟到 ACK 不会重新插入正文。
- 可信服务调用签名补充数值 `iat` 声明，与 Identity 内部验证器的签发时间要求对齐；验证器对请求级 RSA 公钥关闭签名 provider 缓存，避免释放后的 RSA 被跨请求复用；实际签名器到 Identity 内部目录端点已连续验证 6 个不同 jti/request_n_id。

## 验证

- `dotnet build tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release`：成功，0 警告、0 错误。
- `dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build`：84/84 通过。
- `dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --filter FullyQualifiedName~Search_AcceptsMultipleSignerIssuedTrustedAssertionsThenFailsClosedWhenDirectoryIsUnavailable`：1/1 通过（同一 Host 连续 6 个不同服务断言，含新编译的 Release 目标）。
- `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`：成功，0 警告、0 错误。
- `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`：1803/1803 通过，7 个外部依赖测试按既有条件跳过，0 失败。
- `pnpm.cmd typecheck`：通过。
- `pnpm.cmd test:unit -- tests/components/CollaborationChat.spec.ts tests/components/PcLayout.spec.ts tests/contract/collaborationApi.spec.ts`：130 文件、962 测试通过。
- `node node_modules/.pnpm/prettier@3.9.6/node_modules/prettier/bin/prettier.cjs --check`（本轮 Chat 源码与测试）：通过。
- `git diff --check`：通过；仅输出工作区既有文件的 LF/CRLF 警告。

## 真实 UI 状态

本开发任务未将用户正在运行的实例停止、重启或接管。此前 CUA 无法读取页面树，故顶栏相邻、亮暗主题、中英文、100 条会话、跨设备和浏览器焦点场景均留给独立验收任务在现有实例取证，不能以本文件替代真实页面验收。

## 交接文件 SHA-256

| 文件 | SHA-256 |
| --- | --- |
| `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Contracts/CollaborationContracts.cs` | `84AA4443695B6DAC70CAB13223962C1BA294B7AFF311379057ACC106E2742FC3` |
| `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Application/CollaborationPorts.cs` | `747E4622CA62D6FED270E30CAF5B52261882111A9764CC01B4EDC8D93B381B1A` |
| `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Application/CollaborationService.cs` | `8B8A9C5AA386C3B5753A5D3904D5087A3E076E33CEE78D198711A6256E28B1E3` |
| `src/backend/src/Services/Collaboration/IndustrialPlatform.Collaboration.Infrastructure/Persistence/SqlCollaborationRepository.cs` | `17D88D69328A7AF8BFB7F96F642980945BDB66D191E133C6270DA86BB9CE8A2C` |
| `src/frontend/src/api/collaboration.ts` | `F9FBFCAC735450CEA14B2793516D6162639CD49440B7B5762E0AD464DE99CCB4` |
| `src/backend/src/BuildingBlocks/IndustrialPlatform.Security/TrustedServiceCallSigner.cs` | `2EA61BE6FAA3E7F3E42B02394E64C0DA3B3C7983AC4E981796B265F61E8B44FB` |
| `src/backend/src/BuildingBlocks/IndustrialPlatform.Security/TrustedServiceCallValidator.cs` | `58AC24C21B7992F506F06A71C728E6F4537F6CB2160BBA0F1DBFB47586978DF2` |
| `tests/Identity/IndustrialPlatform.Identity.Tests/Api_CollaborationDirectoryEndpointTests.cs` | `07E1689FDE1BD4FD604172D0B8FC77B34A6DDA2C6C96BCBC214DC73F2489AD16` |
| `src/frontend/src/components/collaboration/CollaborationChat.vue` | `5F8AFF272DF803B30A5EA679600DC119FB7D973F3FBAE0E96868120BF8AF2A84` |
| `src/frontend/src/components/collaboration/CollaborationQuickDrawer.vue` | `AF6978CF42BDA4DA5291504FD6999D8AD31E18CE36A0A75F434A51917469B780` |
| `src/frontend/src/layouts/PcLayout.vue` | `28F47C41A1AF8080B4383DFB04F8AD83162F3E75B661F1F71AF8F8095F71945E` |
| `src/frontend/src/stores/collaborationChatStore.ts` | `A665DE5399835C8EAF611FE04FB80347BF31635E2608F511CCA814D87B47BA04` |
| `src/frontend/tests/components/CollaborationChat.spec.ts` | `5426AD15D05D14E5C9D91E53F169601A61D144D59AD90389CC9D71F072C11A51` |
| `src/frontend/tests/components/PcLayout.spec.ts` | `1D9F826CF754136557A5BEBF4523DFBA44CCAA91F5ED56CB47C022B6FC75D01A` |
| `src/frontend/tests/contract/collaborationApi.spec.ts` | `78995A331F6DF479C6754A48068CAD21CD4B9C8BB0DF5442554411B929272137` |
