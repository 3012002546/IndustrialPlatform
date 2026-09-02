# PF03 Shell 已批准视觉方案：独立验收工作记录

状态：IN PROGRESS；本轮不沿用上轮 ACCEPT。静态 QA passed 不代表产品通过。

- 来源任务：`01a04cb9-f897-78e2-8d72-a3f1cfc81a2b`
- 唯一开发任务：`01a04e86-d2b4-7af2-b61a-43897954c544`
- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 本轮只读起点：`a695aea5c110bf124fc18daaf5d602531bfb92fa` / develop ahead 34
- 初始状态：41 tracked unstaged、15 untracked 路径项、0 staged；保护原有 WIP。
- 基准目录：`D:\Code\Industrial Platform\visual-previews\2026-08-31-shell-review`
- 已完整阅读 implementation-handoff.md、review-notes.md、design-qa.md、仓库 AGENTS.md/CLAUDE.md、设计 6.6 和验收附录。
- 协作：开发写入期间不 build/test、不抢用 Chrome、不重启已有服务；首个 Shell+用户页检查点先截图评审，再进行最终门禁。

## 1. 冻结基准

以下 SHA256 已独立核对，全部与交接吻合；不覆盖或修改基准。

| 文件 | 尺寸 | SHA256 |
|---|---|---|
| index.html | — | 3F74E9498BF534AE69187E105A853737094B6D859E23B0793A1E01D5D7A36698 |
| preview.css | — | 728E3AD86B94ADAD374BE691A4A9E7E65E793DD3DB604F4303A7DB6904DBC6A7 |
| preview.js | — | F40CD003705581BBF2D8397F527280E32058F66E2333B5E307E3D8B4510A39C2 |
| preview-desktop.png | 2048×1090 | C0E39D85D897C3392D404D4E7E18EEAA6D5070BA2DCA15CB26C7FEAE96ED2D78 |
| preview-1440.png | 1440×900 | B0800BE6C4741B1555BD1883449936011735FD99FF43FCA59BD995D7106CD46E |
| preview-1280.png | 1280×720 | 32707CBF1CA678A7CF0C059D8BE3A005424DBD7D64E21DDBC8AB2450055D4E99 |

已实际打开三张主图及 language/account/collapsed/dark/online/sessions/production 全部状态图。

REF-01：1280 图右侧控件/账号与底部分页缺失、部分 SVG 未呈现；dark 图仍浅色；account 图无账号菜单；online 图无抽屉。来源任务已独立确认并更新交接优先更正。原文件保持不变；以下从同一冻结源码补拍的状态证据关闭 REF-01，仅关闭参考缺口，不代表产品通过。

补证目录：`C:\Users\DONG\.codex\visualizations\2026\08\29\01a04e87-b2e3-73e1-94f8-49c4024a8573\shell-review-20260831`

| 接受文件 | 实际视口/文件尺寸 | 状态与实测 | SHA256 |
|---|---|---|---|
| ref01-01-preview-1280.jpg | 1280×720 / 1280×720 | 中文浅色/展开/无浮层；scrollWidth=1280；账号right=1276；工具和分页完整，宽表仅内部滚动 | 581A2B2B435F330ADE4E1F688C359A79DF83E90E7E9DAB0D7AF33C36C464C89D |
| ref01-02-preview-account-1440.jpg | 1440×900 / 1440×900 | 中文浅色/账号菜单展开；菜单192宽，四项36高，图标16，gap10，right=1436 | 47EEB30DA89870D53C3CBE912F63E5CC7EA9400C3F9E679E17963DE759AB9C20 |
| ref01-03b-preview-dark-1440-stable.jpg | 1440×900 / 1440×900 | 中文暗色/展开/无浮层；body.dark，底色rgb(22,33,48)，文字rgb(212,226,240) | BE9557325AD52A68BC86E8235A559D136F74F0FC57383134CECC52F5DB1E3599 |
| ref01-04-preview-online-1440.jpg | 1440×900 / 1440×900 | 中文浅色/“当前在线”静态方案页展开；抽屉[820,0]-[1440,900]，宽620；示例声明清晰 | D67B0CB25AB9A872628983ACD6BB0792C50B68C19E98EFF8FBFF14DEA8334454 |

方法：每次截图返回的同一份字节直接落盘，再用 view_image 打开该文件；工具原生返回 JPEG，因此保留 JPG，不重新编码或改图。最初把一张 JPEG 临时命名为 PNG，确认格式后仅改扩展名，内容哈希不变。所有接受图均已实际查看。

拒绝帧：`ref01-03-preview-dark-1440.jpg`（SHA256 0D98E63DD25A1A80693105CEDD06AED075ED20BA882265B28BB7BD39C217CCA1）在 DOM 已 dark 时仍抓到浅色旧帧，保留诊断但不用于验收；下一次独立捕获并打开落盘文件后才接受 03b。此复现说明 DOM状态不能替代图像内容核验。旧用户预览标签 DOM/截图超时；改用同一 Chrome 新标签后可取证，没有换浏览器、改源码或重启服务。结束已关闭验收新标签、恢复默认视口，未操作用户真实管理会话。

## 2. 紧凑验收清单

所有项目当前均为待验；已有测试仅作为复用索引，不计本轮通过证据。

| ID | 硬门槛 / 真实行为 | 既有实现、测试复用 | 所需新证据 |
|---|---|---|---|
| V01 | 三视口 2048×1090、1440×900、1280×720；同语言/主题/开合状态 | PcLayout、PlatformTopBar、pc-shell | 同状态并列/叠加；逐控件尺寸/比例/颜色，而非全屏相似度 |
| V02 | 顶栏56；PC/DEV间距6；右边4；按钮32/icon18；头像28/icon17；姓名13/账号11 | PlatformTopBar、LocaleControl、PlatformBrand | Logo透明边/比例、账号不裁切、DOM box/computed style；明确数值约±2px |
| V03 | 语言144宽/行36/勾16；账号192宽/icon16/间距10；四项有图标 | LocaleControl.spec、PcLayout.spec | 弹层截图、边界、Escape/焦点；不保留独立锁定icon |
| V04 | 一级72、二级208/52；收起保留授权入口；更多和底部菜单可达 | PlatformFunctionTree.spec、PlatformDomainRail | 无结果筛选→收起→可导航；至少12一级项目/不同高度；键盘 |
| V05 | Tabs38且始于内容列；标题/查询/表格共用白页轴线 | PcWorkspaceTabs、AppPage、AppPageHeader | 三视口区域起点、padding、边线、圆角、21px标题及表格12px |
| V06 | 查询紧凑左对齐，宽屏gap12/窄屏按冻结CSS；32高统一按钮邻接字段 | AppQueryPanel.spec、IdentityUsersPage.spec | 用户/用户组并列；查询/重置/更多条件/空态；无局部复制组件 |
| F01 | 顶部/列头查询互斥，不残留污染；分页/排序/导出真实 | IdentityUsersPage、AppDataTable、OData契约与端点测试 | UI、URL/参数、状态/响应；无alert/pageerror/非预期4xx5xx |
| F02 | 真实授权导航/直达拒绝；zh/en全部导航/Tabs/title同步 | navigation、router、authStore、real-login | admin及受限账号；全局搜索与局部筛选分工 |
| F03 | 管理/生产双向及菜单返回同步；八卡禁用，设置真实接线 | experienceModeStore、pc-operation-mode | 三视口3×3、gap14/card170/icon34；原路由/tab/query保持 |
| F04 | 账号菜单Profile/cache/lock/logout仍真功能 | ProfilePage.spec、uiCacheStore.spec、lockStore、authStore | /auth/me字段/改密；UI缓存白名单且内存刷新；安全/主题/模式不丢 |
| S01 | 有效刷新会话≠在线；不得硬编码1/449或隐藏测试账号 | RefreshSessionStore、PlatformSessionControls | 当前租户真实聚合与声明；没有lastSeen/heartbeat时不宣称实时在线 |
| S02 | 会话view/revoke分权、租户、幂等、目标单会话、当前退出 | Api_SessionManagementEndpointTests、Infrastructure_RefreshSessionRotationTests | fresh安全测试与真实权限；不清/撤销用户现有会话 |
| S03 | 抽屉loading/empty/error/retry/refresh/current；安全投影；PF04发送禁用 | PlatformSessionControls.spec | 状态与键盘/焦点截图；无token/IP/UA原值或hash、新敏感存储 |
| B01 | 无新框架/重复权限OData表格；无Audit/File/Notification/MES扩张 | baseline..final完整diff、依赖/许可证 | 保护文件/生成物/假toast降级审查；新测试仅清理自身会话 |
| G01 | 稳定final后fresh gates，不信任开发摘要 | 现有全量/定向测试与Playwright配置 | 命令、退出码、失败数、真实页面截图/网络；后端变更先Release build |

允许差异仅包括原型底部32px说明条不入产品、真实数据/权限/账号差异、字体抗锯齿；原型的静态toast/示例计数不进入产品。

## 3. 已读代码根因与复用边界

- `RefreshSessionStore.ListActiveForTenantAsync` 以 tenant、非软删、UserIsDeleted=false、UsedOn=null、RevokedOn=null、ExpiresOn>now 选取，再投影账号/姓名/时间；没有 lastSeen/heartbeat 在线判据。
- 旋转后 UsedOn 非空会被排除；不能把历史有效会话直接归因于正常刷新重复。现有登录测试的不对称清理需随本轮测试修正，旧用户会话不能批量撤销。
- 现有单元/端点测试已覆盖会话安全投影、view/revoke分离、租户和单会话撤销；当前抽屉状态/数值视觉与完整真实操作仍须本轮重新验证。
- 复用 Shell/QueryPanel/DataTable/locale/router/mode/auth stores；不重新设计，不添加替代功能实现。

## 4. 检查点与结果

### CP1 首次视觉对齐

阶段结论：REJECT，等待原开发任务修复后 CP1-R2；不是本轮最终结论。

开发确认冻结 Shell/用户页/关联样式后，在真实 Chrome、专用 e2e.admin 管理员会话、zh-CN/industrial-cyan/light、二级展开下取证。HEAD 仍为 a695aea，候选变更尚未提交。最初主查询默认被整个隐藏，只显示“展开”；展开后才取得以下同主查询可见状态证据。

| 项目 | 批准目标 | 真实测量/观察 | 结论 |
|---|---|---|---|
| 主查询 | 五个主条件默认可见、左对齐横排；低频条件单独折叠 | 默认全部隐藏；展开后全部右侧纵排；query高度551.6px，1440表头y822而目标约357 | CP1-P1-01 OPEN |
| 查询根因 | flex横向密集布局 | AppQueryPanel.__body的flex-direction:column被__body--grid继承，align-items:end将字段推右；IdentityUsersPage把整个body collapsed绑定advancedQueryOpen | 已定位，退回开发，不自行修代码 |
| 顶栏色/Logo | 冻结图深蓝灰；按可见图形边界匹配Logo | 仍为工业青多站点渐变；正式紧裁切Logo可见宽184，而源图同184px框内含透明边、可见Logo明显较小 | CP1-P1-02 OPEN |
| 账号右边距 | 4px | 1440 right1393.39、2048 right2001.39，均余46.61px；1280 right1218.19，余61.81px（剔除15px滚动条仍约46.6） | CP1-P1-02 OPEN |
| Tabs | 高38、下划线选中 | 三视口高46.8、pill样式，行内padding导致超过目标 | CP1-P1-02 OPEN |
| 一级/二级宽 | 72/208 | 72.8/208.8，约0.8px边线差可接受；但行距、分组层级和图标视觉尚不同 | 宽度单项符合，其余仍待修 |
| 视口/文件 | 必须核对实际字节尺寸 | 1440文件正确；1280因全页滚动/捕获归一返回1265×712；2048返回2047×1090 | 后两张只支持失败定位，不作为严格同尺寸通过证据 |

CP1截图都来自同一次字节落盘，再由 view_image 打开，并与对应批准参考在同一审查输入中查看：

| 文件 | 请求CSS视口 / 落盘尺寸 | SHA256 |
|---|---|---|
| cp1-01-real-users-1440.jpg | 1440×900 / 1440×900 | 0D330B5FCE146730ECCA71845345ECBFA9DD2E90037B59A9749485E33F97AF28 |
| cp1-02-real-users-1280.jpg | 1280×720 / 1265×712 | 09C1F69E9DAC7448179DAF93FB1924857434B3526D44471DC9D75AA3B10322C3 |
| cp1-03-real-users-2048.jpg | 2048×1090 / 2047×1090 | 61654044A29D5BA9FFC04CF44D9A8DB184894B278A774F7F63473F1D894876FC |

截图目录与 REF-01 补证相同。1280显式clip补捕获返回 Page.captureScreenshot timeout，未生成02b文件，不引用不存在的图片。1280实际innerWidth=1280、clientWidth=1265、visualViewport.width=1264.8，出现全局与内容双滚动条；先修页面布局再复验完整视口捕获，不对图片做缩放来掩盖问题。

| 1440批准参考 | 1440真实候选（失败） |
|---|---|
| ![批准1440](<D:/Code/Industrial Platform/visual-previews/2026-08-31-shell-review/preview-1440.png>) | ![CP1真实1440](C:/Users/DONG/.codex/visualizations/2026/08/29/01a04e87-b2e3-73e1-94f8-49c4024a8573/shell-review-20260831/cp1-01-real-users-1440.jpg) |

开发已收到精确根因、尺寸与截图，已解除首轮冻结；复验继续用原任务，不开新修复线程。

### CP1 runtime归属与认证前置

- 初始真实用户标签已处于 `/login?redirect=/pc/identity/users`，前端5173无监听；未将登录页归为视觉缺陷。
- netstat/进程Path确认 PID49756 是用户已有 UnifiedHost Debug（09:57启动），不是单Identity。保留不动；`GET /health`=200 Healthy/UnifiedHost。
- 按既有 `.env.local` 启动缺失前端：VITE_AUTH_MODE=http、VITE_API_BASE_URL=http://localhost:5041，无Vite代理、无业务调用修改。隐藏后台 launcher50760 / node50336，监听127.0.0.1:5173，GET /=200；日志 `C:\Users\DONG\AppData\Local\Temp\ip-shell-cp1-20260831\frontend.out.log` / `frontend.err.log`。
- 源码正式 bootstrap 路径 `/identity/api/v1/bootstrap/status` 返回200/Ready。前置诊断中误探 `/identity/api/v1/auth/bootstrap` 得404，核对 identityApi.ts 后纠正；不是产品请求失败。
- 未启动Gateway/分服务，未重启用户UnifiedHost，保留5188预览PID5960。未执行build/test。
- 仅通过正常登录UI创建一条专用e2e.admin会话；未读取/导出浏览器凭据或旧token，未清理真实admin会话。该测试会话保留给下一检查点复用，最终只退出自身会话。视口已恢复默认，Chrome操作权释放。

### Final gates

待稳定 final commit。将记录实际命令退出码与失败数；本轮尚无测试通过声明。

### CP1-R2：真实 1440 主屏复验（阶段 REJECT）

开发确认冻结 Shell/users/styles、暂停浏览器和同目录测试后，复用同一真实 Chrome、既有 5173/5041 与原专用 e2e.admin 会话。没有重新登录、启动后端或运行门禁。HMR 后“更多条件”保留上轮展开状态，通过真实按钮收起低频条件，五个主要字段仍然可见；以此与批准稿的浅色、中文、展开导航、无浮层主屏同状态比较。

- 截图：`C:/Users/DONG/.codex/visualizations/2026/08/29/01a04e87-b2e3-73e1-94f8-49c4024a8573/shell-review-20260831/cp1-r2-01-real-users-1440.jpg`。
- 请求视口、DOM innerWidth/innerHeight、图片实际尺寸均为 **1440×900**；clientWidth=scrollWidth=1440。
- SHA256：`0534BE37E631A7ABFF02AB48ABF1F2FA7619A259150FCF628A546B8418E5A22A`。
- 保存后以 System.Drawing 读取实际尺寸，再打开同一文件字节与冻结 `preview-1440.png` 并列人工查看；未缩放/裁剪/重编码截图。
- 开发报告的 6 files/45 tests 仅记录为其自验，不计入独立门禁。

| 子项 | R2 独立实测 | 对照目标/结论 |
|---|---|---|
| 主查询可见与布局 | 五主字段可见且横向；低频业务标识/已删单独收起 | CP1-P1-01 的隐藏/纵排根因已消失，但查询整体仍未通过 |
| 顶栏颜色/高度 | rgb(23,42,66)、56px | 该子项符合 |
| 账号右边距 | 用户按钮 right=1436、height=32 | 右边距4px符合 |
| Tabs 外框 | x280.8/y56、height38 | 外框符合；item仅29.2px高且underline未到栏底，仍不符合整项样式 |
| 查询额外标题行 | 仅“更多条件”的header=32px + margin-bottom12px；总query129.6px，控件y288.2 | 批准低频入口紧邻搜索/重置，无此44px独立行；目标query约85px、控件约y252 |
| 内容外边距 | page x290.8/y104，即10px | 批准16px，page约x296/y110 |
| 标题/查询/表格同轴 | 查询x311.6，表头x304.4 | 表格内部12px而查询20px，不同轴；批准三者约x317 |
| 表格比例/样式 | th48px、font14px；竖网格、圆形工具、无“用户列表”及查询/排序/分组可见标签 | 批准th38px、font12px与轻量工具栏；只允许既有统一表格tokens/CSS/公开接缝修正，不重写表格 |
| 页面伸展 | 白色surface终止y630.4 | 批准内容白surface伸展、分页靠底；允许不带32px静态说明条，不接受仍旧自适应内容高度造成整体比例失配 |
| Logo | img x14/w136.16/h29.99；可见比例基本恢复 | 批准可见图形左约x38；紧裁切资产仍需在既有brand slot保持可见位置，不能以CSS框解释 |
| 顶栏中区 | search480px；tenant16px粗体；DEV白描边pill | 冻结CSS在1440 search max300、context12px、DEV10px小圆角标识 |
| 二级导航 | 项40px+4px步距、search y129.6 | 批准36px+2px、search y110；分组/轻量层级仍未对齐 |

| 1440批准参考 | 1440真实R2（失败） |
|---|---|
| ![批准1440](<D:/Code/Industrial Platform/visual-previews/2026-08-31-shell-review/preview-1440.png>) | ![CP1-R2真实1440](C:/Users/DONG/.codex/visualizations/2026/08/29/01a04e87-b2e3-73e1-94f8-49c4024a8573/shell-review-20260831/cp1-r2-01-real-users-1440.jpg) |

已向唯一开发任务发送上述具体 symbols/尺寸/实际期望/截图与复验范围。按来源任务“明显P1未清先退回”的指令，本次不继续1280/2048和状态矩阵，也不启动昂贵整轮门禁。视口恢复默认、真实标签markHandoff、冻结解除；等待CP1-R3。两项P1仅部分修复，不能关闭或给最终ACCEPT。

### CP1-R3 开发自检代采（非独立验收证据）

R3开发先报告定向10 files/129 tests、全量unit90 files/641 tests及lint通过，但未附要求的开发真实比图自检，故未启动独立验收。跨任务Chrome claim失败后，双方核对有效API无release/finalize；`markHandoff`仅标交接，不能释放本任务持有的用户原标签。来源任务明确批准最小协作调整：本任务只代采原图/几何，开发亲自自检并修正；开发稳定交接后，本任务必须独立重新采集。未使用未文档化API、强取、关用户标签或虚假结束验收。

开发提供1440状态/指标并冻结10分钟。原标签已在登录页，依来源授权通过正常UI恢复既有专用e2e.admin一次（13:04:14 +08），不动admin会话、不取浏览器凭据、不重启runtime。五主条件默认可见，因此保持低频折叠、无浮层、中文浅色展开导航，与批准1440同状态。

- 原图：同截图目录 `cp1-r3-dev-capture-1440.jpg`；请求/DOM/实际图片均1440×900；scrollWidth1440、scrollHeight900。
- SHA256：`FF27B8A2EF4E0C8B202CCCB86F8C62455DEC9AE817E48D6C51CCBCF3E7EB593F`。
- 原始几何及日志：同目录 `cp1-r3-dev-capture-1440-raw.txt`。已将同份原始图和数值交开发，不提供替代其自检的产品判断。
- UI alert=[]。日志接口保留两组开发写入期HMR错误（04:28:59Z、04:30:16Z）：`Cannot access PcLayout before initialization`，`routes.ts:149`；Vite提示circular import并自动reload。当前采集没有更新的错误条目，不宣称全部历史console为零。
- 专用pageerror、network请求/响应接口不可用，记 **NOT CAPTURED**，DOM无alert不替代请求成功。首次试取performance resource entries在DOM-only evaluator不可用；该工具异常未生成图片，已纠正后重采，不归因产品。
- 视口已恢复、停止页面操作，开发写入冻结解除。等待开发亲自并列自检/修正及稳定候选指纹。**本代采图不得复用为独立验收通过图。**

开发读取首份代采图后指出部分DOM未反映最新源码，确认存在HMR失效风险并冻结5分钟，要求normal reload后重采。随后本任务对同一tab正常reload，认证会话保留，未再次登录；代采 `cp1-r3-dev-reload-1440.jpg`（实际1440×900，SHA256 `3A0ED2618ABBEA0749C775B3B5BC99146121639618E8406517CBBB9760BF1F10`）及 `cp1-r3-dev-reload-1440-raw.txt`。新图brand槽184×30、search x453.44/w300、page bottom884，确认不能用未reload图替代最终候选渲染；本次仍只交开发自检，不作独立视觉通过。日志增加05:15:43Z开发期同类HMR错误，未见本次reload后的新error条目；网络/pageerror仍NOT CAPTURED。截图保存后打开同字节核验，视口恢复、停止操作、开发冻结解除。

### CP1-R3 独立重采：阶段 REJECT

开发完成自己的normal-reload图像自检后，交付冻结候选HEAD `a695aea5c110bf124fc18daaf5d602531bfb92fa` 及未提交diff指纹。本任务重新读取Git并独立冷reload；第一次返回登录页，未用开发代采图顶替。只读诊断显示现有 `sessionStore.parseStoredSession` 对过期 `expiresAt` 返回null，`authStore.restore`清本地会话而不尝试refresh；Identity `AccessTokenMinutes=30`，与专用13:04:14登录、30分钟后reload返回登录一致。未将其无依据定性为本轮视觉失败或refresh网络失败，未扩展修改Auth。

登录页原图 `cp1-r3-independent-1440.jpg` 实际1440×900，SHA `C6EB1E4BD18BBC5C88CE9D94111AE5A4DF2F97267F3D25F497F170B507853B78`，仅保留认证前置事实，不作主屏证据。按既定正常UI恢复约定仅登录专用e2e.admin一次（13:40:58），不取旧token、不动admin。开发再次确认5分钟冻结后立即独立采集。

- 独立主屏新图：`cp1-r3-independent-users-1440.jpg`，请求/DOM/实际像素均1440×900，clientWidth=scrollWidth=1440、scrollHeight900。
- SHA256：`44D25CE8C239AF6C0D515B35A939C869E182792A3455F282834869703CD2DDBE`。
- 原始断言/几何：`cp1-r3-independent-users-1440-raw.txt`，同外部截图目录。保存后打开同份原图与批准1440并列，完全独立于开发代采图。
- Git指纹闭环：开发早报77EB为逐行LF连接且无尾LF，独立采集前后均为 `77EB25343805D746D5735DE1D3E46DAD61BF8F7CA2F2864438CF81623C83042F`；git原始stdout（含尾LF）UTF-8 SHA为 `B2A266552672FBAE605760B5721D2F9FF970064D70664AD9A5132199E8F66239`，开发补充确认。两者只是尾LF定义差异，不是候选漂移。未把完整dirty diff都归本轮；最终仍需对照受保护WIP。

| 原缺陷子项 | R3独立结果 |
|---|---|
| P1-01：主条件隐藏/纵排/独占更多条件行 | **核心复现关闭**。五字段常显横排，低频独立，搜索/重置/更多同行，query85.6px；后续完整查询/导出门禁仍待执行 |
| P1-02：已达标外框和同轴 | 保留56px顶栏、4px右距、38pxTabs外框、16px内容外边距、标题/查询/表格x317.6、表头38/font12、页面伸展、Logo可见位置比例；不要求无意义重做 |
| P1-02：Tabs项 | **FAIL**。外框padding4px 8px，item y60..90.8/h30.8而批准整项y56..94/h38；字体14 vs12，既有图标/右侧操作视觉仍缺失 |
| P1-02：重复水平滚动 | **FAIL**。VXE横条y522.8..540.8；外层surface另有约16pxnative横条：y355..827.2、offsetH472/clientH456、overflow:auto。原图清楚显示两条横条。属于容器CSS，不以真实业务数据豁免 |
| P1-02：按钮/工具栏 | **FAIL**。查询搜索/重置/更多font16，批准13/13/12；表格按钮均描边高32，批准主要29/右工具28及轻量层级。可见标签已恢复，但样式密度仍不符 |
| P1-02：二级分组/标题计数 | **FAIL**。行高36/步距38已符合；“身份与访问/组织与平台”分组无DOM，首项y150.8 vs批准184。真实计数仍裸放右侧，批准title旁count pill；失败是层级/位置，不是账号或数字内容 |

| 批准1440 | R3独立1440（失败） |
|---|---|
| ![批准1440](<D:/Code/Industrial Platform/visual-previews/2026-08-31-shell-review/preview-1440.png>) | ![R3独立1440](C:/Users/DONG/.codex/visualizations/2026/08/29/01a04e87-b2e3-73e1-94f8-49c4024a8573/shell-review-20260831/cp1-r3-independent-users-1440.jpg) |

实际行数据、账号差异、既有用户列偏好不作为拒收依据；拒收依据是上表仍未清除的批准几何/布局/样式。已仅向原开发任务发送precise symbols/数值/图与回归范围，限制沿既有tokens/CSS/toolbar接缝修，不重写AppDataTable、不扩认证。本次不扩1280/2048或整轮门禁。UI alert=[]，pageerror/network专用证据仍未采集，不标PASS。视口恢复、停止浏览器操作、开发冻结解除。

### 缺陷状态汇总

| 编号 | 类型 | 状态 |
|---|---|---|
| REF-01 | 冻结参考状态截图不匹配 | CLOSED（仅参考）；四张同源补图及拒绝旧帧见上 |
| CP1-P1-01 | 主查询整体隐藏及右侧纵排、独占更多条件行 | CLOSED-RETEST（候选a695+77EB文本diff）；R3核心复现已消失，最终commit完整查询门禁仍待执行 |
| CP1-P1-02 | 顶栏/Logo/Tabs/页面表格视觉偏差 | OPEN；R3已达标子项保留，Tabs项/双横条/按钮工具栏/分组计数层级仍失败，已退回 |

### CP1-R4 开发自检代采（非独立验收）

开发针对R3剩余项继续修订后请求5分钟冻结、normal reload的1440自检原图。依既定顺序仅采主基准，未扩1280/2048。本任务先在原真实Users点击“刷新表格”，通过现有认证刷新链路后普通reload；同一e2e.admin会话保留（登录时间仍13:40:58），没有新登录、取旧token、重启runtime或运行build/test。

- 原图：同外部截图目录 `cp1-r4-dev-reload-1440.jpg`，请求/DOM/实际尺寸1440×900，clientWidth=scrollWidth=1440、scrollHeight900。
- SHA256：`EB810C2C230ADD094A0D0DBCD1B537C54B350729C307BF3B1D2D5A6F5ADEE512`。已落盘后打开同字节查看；不重编码或加工。
- 原始DOM几何与日志：`cp1-r4-dev-reload-1440-raw.txt`。Tabs外38/项37.2/font12；查询85.6、按钮32/font13/13/12；工具栏主29/右28；surface overflow hidden；表头38/font12；导航分组已在DOM。这些只是交开发的原始测量，非独立通过判断。
- 新日志警告：`2026-08-31T06:36:43.026Z Duplicate keys found during update:"identity-users"`，调用组件链 `PlatformCommandSearch → PlatformTopBar → PcLayout`。已要求开发定位重复导航key并回归；不能称console全零。旧HMR历史仍单独保留；专用pageerror/network证据仍NOT CAPTURED。
- 本图强调色实际呈灰色，原样交开发核对真实主题状态，未自行修改主题去贴合参考。
- 已交原图/数值/新警告给唯一开发任务，要求其亲自与批准1440比较、自检并交稳定候选；之后本任务须另行reload拍新图独立验收。R4代采不能关闭P1-02。
- 视口已reset、浏览器操作暂停、开发写入冻结解除。更正此前文字中的“Chrome操作权释放”：有效API的markHandoff不释放跨任务claim，本任务仍是唯一Chrome操作者；仅暂停交互，未强取、关闭用户标签或虚假结束任务。

### CP1-R4b：去重修复后的代采遇到运行时超时

开发报告其已核对R4原图，并修复 `PcLayout.commandItems` 合并静态导航与同id业务tab造成的重复key；自验先红后绿3/3、全量90 files/646 tests、lint/typecheck/build及Mock17/17仅计开发自报。请求再次代采供其提交前自检，仍未转为独立最终验收。

本任务06:55:15Z在同会话点击刷新并normal reload；随后约10秒出现auth/me、runtime navigation/features/theme-policy和users/roles/user-groups timeout。DOM先空后完成渲染为降级/0人，短暂UI alert“请求超时,请稍后重试”，随后两处SystemData降级status保留。未通过再次登录掩盖、未注入Mock、未改请求或重启服务。

- 原图 `cp1-r4b-dev-reload-1440.jpg`，SHA256 `13E3BED133C7AE11476ECE8DEC1C9484F0F5B9E9D78B2A4E34CB9FDFC51B9330`；对应 `cp1-r4b-dev-reload-1440-raw.txt`。均在上述外部截图目录。原图已打开查看，未加工。
- 请求视口1440×900；DOM inner1440×900、clientWidth1425、scrollWidth1790；实际落盘1416×885。不能将此图标作正常1440主屏，也不能拉伸为目标尺寸。
- 降级提示直接挤入顶栏，使用户按钮x1600.69..1790.69超出viewport，右侧消息/用户等工具不可见，页面出现全局横条。该**错误状态布局失败**已按CP1-P1-03退回开发，不能以服务超时豁免；要求保留降级信息/重试而不挤出工具，不得隐藏错误。
- 该窗口未出现新duplicate-key条目，旧06:36:43Z仍保留；由于runtime失败，尚不作为健康导航重复key回归通过。
- 采集前HEAD仍 `a695aea5c110bf124fc18daaf5d602531bfb92fa`；tracked diff LF无尾LF指纹 `D49B0DA602C9142A01C07705F1546DE1FC4C403C548412D1921EF28642E2ABCD`。本轮尚未得到final commit。
- 只读诊断：UnifiedHost49756同09:57:14 Debug路径，IPv4及IPv6仍监听5041，但IPv4 `/health` 6秒和IPv65秒均timeout。Responding=true不足以证明HTTP健康。Development配置File logging=false，repo下未找到运行日志；命令行归属Get-CimInstance被sandbox拒绝。当前根因**未确定**，不归为已证明外部依赖缺口。
- 新观察到IPv6 `[::1]:5173` node7752始于09:57:40，属用户早已存在的前端；我方IPv4 node50336始于12:01:03。两个均保持，不能把首次观察当作新启动。静态5188 PID5960保持。
- 视口reset、停止交互并解除开发冻结；普通缺陷/诊断只交唯一开发任务。未运行门禁、未停止用户进程。

15:10:12 +08 只读复查同 `/health` 已返回200/Healthy/UnifiedHost，期间本任务没有重启进程。超时原因仍未确证，不把自行恢复等同根因闭环，也不据此免除降级布局缺陷。已通知开发继续健康主屏与降级状态两条分别回归。

### CP1-R5 健康主屏开发代采

开发报告已在PlatformServiceStatus通过bounded width/max-width、overflow hidden及文本ellipsis修复降级提示溢出，并保留retry操作和语义；开发自验3/3、定向7 files/55 tests、全量90 files/647 tests、lint/typecheck/build及Mock17/17仅计其自报，不能关闭独立错误态验收。

按其5分钟冻结窗口，本任务07:12:13Z只在1440中文浅色、展开导航、低频折叠、无浮层状态刷新表格并normal reload。真实3用户重新显示，同13:40:58专用e2e.admin会话，未重新登录、重启runtime或跑门禁。

- 新开发自检图 `cp1-r5-dev-reload-1440.jpg`，请求/DOM/落盘1440×900，clientWidth=scrollWidth1440、scrollHeight900。
- SHA256 `94DF7AF40387838F99D5CB6395546A26352E700A124AF14EB20217CCE81A3C8D`；原始 `cp1-r5-dev-reload-1440-raw.txt` 同外部目录。落盘后打开同字节查看。
- 本窗口UI alert/status=[]，未见新error/warn；既往06:36重复key及06:55/07:00timeout历史原样保留。专用pageerror/network仍NOT CAPTURED，不据此宣称完整零失败。
- 原始数值header56/right4、Tabs外38/项37.2/font12、query85.6、按钮32/font13/13/12、toolbar主29/右28、surface hidden、th38/font12，交开发亲自比图，不替代其自检。
- 采集前HEAD仍a695aea5；tracked diff LF无尾LF指纹 `5DB97EC91DC4F3CD8BD1117A7E57C8B5E4DC43A060B823E714A04741504BCD58`。
- 视口reset、交互暂停、冻结解除；已要求开发对冻结无浮层 `preview-1440.png` 完成同状态自检并交稳定对象。之后独立阶段必须重新采图；本图不能成为独立通过依据。健康主屏不关闭降级错误态P1-03。

### CP1-R5 独立验收：阶段 REJECT

开发完成R5主屏自行比图后交候选。其首报完整SHA有误，经本任务`git rev-parse`核对并由开发两次更正，真实候选为 **066bd606c4a23a852ac350d75b8d8c7a20005040**，parent a695aea5c110bf124fc18daaf5d602531bfb92fa，22 files/850+/153-，subject `fix(frontend): tighten shell and users visual contracts`。不存在的长SHA不作为证据。索引空，保留大量既有与当前未提交改动；这不是clean worktree或纯commit渲染。

独立采集前后tracked WIP LF无尾LF指纹均为 `185F049C747526CE03821FC3DB0243C2E390FF58AF3C1483FB05BC99434A0B86`。本任务再次normal reload，不复用开发图；返回登录页后按既定约定正常UI恢复专用e2e.admin一次（15:19:10 +08），不取旧token、不动admin。随后独立拍摄并打开原图与冻结preview-1440.png并列比较。

| 独立文件 | 状态/实际像素 | SHA256 |
|---|---|---|
| cp1-r5-independent-users-1440.jpg | 原中性灰/明亮/舒适，1440×900 | 924B92EEBC6DA8C5EC81AEA588A21AF1406FCA77751C58424BB771BE0E1BBEE4 |
| cp1-r5-independent-users-1440-industrial.jpg | 正常UI切工业青、其他状态不变，1440×900 | 2E0E927637C383A44533173620843CF7D3028CBDC06071113CAED28EEF012E00 |

文件和各自`-raw.txt`均在外部截图目录；DOM inner/client/scrollWidth全部1440、height900；图片以System.Drawing核对实际像素。主题菜单明确中性灰radio checked，故此前灰色强调不定性代码缺陷。为同状态比较，正常UI临时切工业青并重拍，结束恢复中性灰/明亮/舒适、关闭菜单、viewport reset。两图不加工，不冒充开发代采或纯commit证据。

| 子项 | 独立结果 |
|---|---|
| P1-02尺寸/密度/同轴/分组 | **CLOSED-RETEST子项**：header56/right4、Tabs外38/项37.2/font12、query85.6、按钮32/font13/13/12、toolbar29/28、surface hidden、th38/font12；title/query/table同轴，分组/count位置符合 |
| P1-02重复横条 | **CLOSED-RETEST子项**：外层native横条已消失，仅保留真实列偏好需要的内部VXE横条；不以列偏好或真实数据差异拒收 |
| P1-02图标与Tabs可见操作入口 | **FAIL**：Tabs只有工作台/用户管理/关闭3个button，svg全0；批准index.html:56–57有House/User与右端刷新当前页/标签操作入口。源码动作仅在contextmenu，不替代批准可见入口。AppQueryPanel搜索/重置/更多及Users新建同样缺批准Search/Refresh/ArrowDown/Plus，导致控件宽度与层级失配 |
| P1-04中文分页i18n | **FAIL**：真实中文页`.el-pagination`为`Total 325/page 1 Go to`，按钮aria仍`Go to previous page`/`Page`等英文；须经现有Element Plus locale机制同步中英文，不靠业务硬编码 |
| P1-03降级错误态 | **PENDING独立复验**：健康图不关闭错误状态缺陷，开发3/3不替代独立取证 |
| 日志/网络 | 采集窗口UI alerts=[]、无新warn/error；历史日志保留。专用pageerror/network尚NOT CAPTURED，不能声称完整零失败 |

| 批准1440 | R5独立工业青1440 |
|---|---|
| ![批准1440](<D:/Code/Industrial Platform/visual-previews/2026-08-31-shell-review/preview-1440.png>) | ![R5独立工业青](C:/Users/DONG/.codex/visualizations/2026/08/29/01a04e87-b2e3-73e1-94f8-49c4024a8573/shell-review-20260831/cp1-r5-independent-users-1440-industrial.jpg) |

已向唯一开发任务发送最小复现、截图/DOM、批准行号、实际期望及Tabs键盘/查询/统一表格locale必要回归；明确保持已通过尺寸，沿现有图标/事件/locale机制最小修复，不改表格架构。本次仍不扩1280/2048或完整门禁。

另提出最终交付完整性要求：TopBar 52→56/grid/context、foundation toolrail96→72、themes顶栏→#172a42等批准画面必要改动仍在未提交diff，不能仅因未提交就一律称旧WIP并排除。本阶段对象是066bd606+稳定WIP；最终必须由开发区分真正既有受保护输入与本轮遗留，纳入自己的必要改动，不覆盖或混入无关WIP。

### CP1-R6 开发自检代采与浏览器连接变更

开发提交 `ee759efb17bafae39189c509626a0772178c8876`（13 files/268+/43-），报告补齐Tabs及查询图标/动作、App根ElConfigProvider响应式locale、Mock/黄金页截图改testInfo.outputPath。开发自报定向4 files/40、unit90 files/652、lint/typecheck/build exit0、Mock17/17，尚不计独立门禁。其“全部端口无监听”说法经本任务netstat反证后已更正：5041/5173/5188及既有PID仍在，本轮没有启动/停止它们。

16:03首次代采Chrome旧实例不可用。依文档只读发现新Chrome实例（部分Chrome进程16:00:50启动），复用runtime并重新绑定同Chrome家族，未启动浏览器、未强取或使用未文档化API。现有用户标签已变为1547263769、`/pc/home`，用户已正常登录真实admin（16:01:10）；本任务未创建该会话、未读token、未登录/退出admin。按既有真实页面授权点击已存在Users页签，normal reload采集，结束恢复原中文工作台/viewport。

开发冻结顺延到16:11，以下仅为其亲自自检的原始材料，不是独立通过图：

| 文件 | 请求/DOM/落盘 | SHA256 |
|---|---|---|
| cp1-r6-dev-zh-1440.jpg | 1440×900 / 1440×900 / 1440×900 | 3BE986690B0D0726711E17675AC2AA050183B6907CD4BA70A93F490AC294EFFC |
| cp1-r6-dev-en-1440.jpg | 1440×900 / 1440×900 / **1440×766** | 688FCF85DC1F1443CB0E4308A960BDC09D208D6924EF7CE29EAF239CD76795BD |

各自raw在同外部目录。英文稳定再采`cp1-r6-dev-en-1440-stable.jpg`仍相同SHA/尺寸，原图有缩小内容及右/下留白，原因未确定；不能充当完整1440×900视觉通过，也不加工补齐。中文原图及英文异常原图都已打开同字节后交开发。

- 中文分页原始文案`共 3 条25条/页 1 前往页`、aria上一页/下一页；英文文案`Total 325/page 1 Go to`及对应aria同步，切回中文恢复。仅记录本次代采事实，P1-04仍待独立阶段复验。
- Tabs/action/query已有svg，actions x1363.2..1440、高37.2，两按钮28；language浮层x988/y50/w189.6/h81.6位于viewport内，但批准宽144，已交开发核对。DOM存在svg不替代图标视觉大小检查。
- 英文时导航/查询/document.title已英文，但既存Tabs可见标题仍为“工作台/用户管理/角色权限/用户组管理”。该真实i18n残留已直接发开发定位titleKey迁移/fallback，禁止通过清缓存掩盖；动态账号“系统管理员”保持中文不是该缺陷。
- 新Chrome日志采集=[]、UI alerts=[]；专用pageerror/network仍NOT CAPTURED。
- 采集前HEAD ee759ef，tracked WIP LF无尾LF指纹 `EE6172FF1BE938EF41FC14FACDC61DC3E73BE2655A2478EE57D06EECA4EC45D4`。剩余WIP继续保留；`--untracked-files=all`展开原docs/prototypes的12796文件，不与porcelain目录项计数混比，不据此清理它。
- 视口已reset、原中文home恢复、浏览器交互暂停、开发冻结解除。等待开发亲自读图/修正并交稳定对象，独立阶段须再拍新图。

### CP1-R7：旧Tabs迁移代采及真实错误状态再现

开发提交 `ddae5463f9648fb265a958b24be106f7bfcc9a65`（parent ee759ef；3 files/84+/3-），在workspace identity纯映射及bindUser恢复时补旧snapshot canonical titleKey并persist；不清缓存。开发自报定向2 files/34、unit90 files/653、lint/typecheck/build exit0、Mock17/17，仍待独立门禁。本任务先提供当前候选开发自检原图，不复用修复前R6旧图作独立结论。

16:27进入窗口时，真实admin原home已有SystemData降级；08:17/08:22/08:27Z日志持续runtime与auth/me、audit等timeout，本机 `/health` 同样5秒timeout。保持原会话、只点击原Users tab并在16:29（既有30分钟有效期内）normal reload执行迁移；未清缓存、未创建/注销admin会话。

- 中文错误态原图 `cp1-r7-dev-degraded-1440.jpg`，实际1440×900，SHA `9B4CC3B4FEA83AC1D7B8B33838960466895CDC7A0039CF58BE5ACD3B7F4074F4`；raw同名。中文降级条w280、x780..1060，user right1436、document scrollW1440。
- 最初显式clip截图返回CDP Page.captureScreenshot timeout，**未生成**`cp1-r7-dev-en-1440.jpg`，不引用不存在文件。随后正常截图成功为 `cp1-r7-dev-en-fallback.jpg`，实际1440×900，SHA `AD1A51ADE2D00FF6B450373FE3D0D6C63B55D7BB6EC17B7214B5B0057531275A`。原图未裁补/加工，已打开同字节。英文DOM及尺寸见`cp1-r7-dev-en-dom.txt`/`cp1-r7-dev-en-details.txt`。
- 迁移后的真实Tabs已为`Workspace / User management / Roles & permissions / User groups`，document.title英文；属于本次开发代采事实，仍需独立最终复验。
- 新建用户按钮内Plus SVG真实宽高**0×0**；Tabs/查询/更多SVG真实14×14。已发开发定位Element按钮封装/选择器，不以源码或svg数量代替实际可见尺寸。
- 英文降级状态再次失败：status w280、x857.59..1137.59，user x1323.59/right1513.59，document scrollWidth1514 > viewport1440，账号可见裁切。P1-03不能以中文错误态成功关闭。
- 英文表格空态仍为“暂无数据”；`Password change required`表头在原80px列宽下越界叠压Email。已分别按locale/表格显示契约退回，保留用户列偏好但要求既有ellipsis/tooltip等不遮邻列，不重写或泄漏VXE。
- 已交完整原图/几何给唯一开发任务亲自自检修正；恢复中文原home、viewport reset，浏览器暂停、冻结解除。未扩1280/2048或独立全量门禁。

16:34:08只读诊断出现外部运行状态变更：旧UnifiedHost49756已不存在，5041改由PID3360监听，`/health`恢复200；7752/50336与5188/5960保持。本任务期间只有GET/netstat/Get-Process及一次只读CIM/线程诊断，没有启动/停止任何后端。先前超时原因未确证，不标已证明外部DB缺口；此次恢复也不豁免上述代码/视觉失败。

16:53开发本轮结束后明确交付的是**非final状态**：HEAD仍ddae5463，R7 Plus/英文顶栏/共享VXE修复仅在dirty worktree且未完成新的fresh门禁，语言浮层189.6→批准144仍未修复，不请求代采。其所列1352/3后端、635 unit、17 Mock、24 real全部明示为a695历史记录，不能代表当前候选。本任务已直接要求原开发继续实现五项闭环、自验、稳定冻结及自行比图，不把idle当交付；没有自行修代码或运行测试。

16:54:05独立`netstat -ano -p tcp`仍确认127.0.0.1:5041/PID3360及127.0.0.1:5173/PID50336监听，反证开发再次报告的全部端口无监听。本次未看到此前IPv6的7752或静态5188/5960，按新观察记录，不推断停止原因、不主动恢复或清理。

### CP1-R8：五项修复后的开发代采

开发请求10分钟稳定冻结，并自报定向4 files/68、unit90 files/656、Mock17/17及lint/typecheck/build exit0（2452 modules）；仅计开发侧证据。17:03–17:07本任务执行正常UI代采，前后HEAD均ddae5463、tracked WIP LF无尾LF指纹均`0EEFF1633080525D09CD9FC9E8B8788435ECF2E30A9D3BBE6AFA3978CFEB65EE`。未运行门禁或修改生产代码。

进入页面时已是用户真实admin的Users页、更多展开，最后登录16:33:41。点击刷新表格后normal reload仍因既有本地到期策略返回登录；按既定约定仅正常UI登录专用e2e.admin一次（17:04:11 +08），没有主动注销admin、读token、清缓存或重启服务。后续拍摄属于e2e.admin，不能冒充admin模式验收。结束恢复中文Users、空查询、更多折叠，viewport reset并暂停交互。

四张原图均请求/DOM/实际落盘1440×900，已打开同字节查看，不裁补、不重编码，仍仅供开发自行比图：

| 外部截图文件 | SHA256 |
|---|---|
| cp1-r8-dev-zh-1440.jpg | D8C853611280D311E381B6A8FA05E0C51DAE639927F4EC1091232C4E20A0CEE4 |
| cp1-r8-dev-en-1440.jpg | 122E3CC89426DFC289AE8A8C52BD2066CCF375F1AABDB558687AF9E1F660B9EF |
| cp1-r8-dev-language-1440.jpg | 4D118A1E966619AE1C40C1EA3ABD10E515F1C99DED71D548E9064CDAF1E5C3E8 |
| cp1-r8-dev-en-empty-1440.jpg | FE50F2141A015B8ABC1EAC47662A2336213515C8E305A22ACCB984B7B7BF8758 |

各自zh/en/empty raw与`cp1-r8-dev-extra.txt`记录完整DOM、几何及历史日志，位于同外部目录。

- Plus中英均实际14×14；语言borderbox144、x1025.7/right1169.7；长表头80px列内ellipsis不再遮Email。
- 使用真实登录名无匹配查询`cp1-no-match-20260831`，返回空态`No data`，Reset恢复3人；不是DOM注入或Mock。本窗口alerts=[]、无新warn/error；保留08:17–08:31Z历史超时/网络错误。专用network/pageerror仍NOT CAPTURED。
- 中英健康态document1440/user right1436；导航、两项Tabs及title同步英文。当前runtime健康，未出现真实降级状态，P1-03错误态仍未复验，不能用健康数值关闭。
- **新可见回归已退回开发自行比图修正**：中文模式“管理/生产操作”被拆行（按钮宽39.125/62.2625、white-space normal）；table th变为48px而批准及R5通过值为38px；英文二级导航出现底部横条/长label裁切；英文排序辅助提示仍为中文“升序：最低到最高/降序：最高到最低”。列头ellipsis本身不能豁免统一高度或i18n。
- 代采完成即向唯一开发任务解除冻结并交原图/几何；等待其亲自自检、必要最小修复及稳定候选。未抄送源任务、未扩1280/2048矩阵。

### 用户新增验收 HDR-01 / HDR-02 / NAV-01

17:24来源任务传达用户明确增量。本任务已完整重读更新的`D:/Code/Industrial Platform/visual-previews/2026-08-31-shell-review/implementation-handoff.md`并打开用户问题截图`C:/Users/DONG/AppData/Local/Temp/codex-clipboard-75df2cd2-96a4-46df-a5ac-5673f199aea5.png`。该问题图不是目标；不改冻结原型，以下要求优先旧稿对应位置。

| 编号 | 必验行为/几何 | 待采矩阵 |
|---|---|---|
| HDR-01 | 全局搜索相对整个Header几何居中，正常宽屏中心误差约2px内；显示Ctrl+K；真实Ctrl+K和点击打开同一个既有全局搜索并聚焦；窄屏不覆盖左右区域/用户名/快捷键 | 1440/1280/宽屏；中英文、明暗；键盘打开、Escape/焦点回收；不按旧稿偏左位置拒收 |
| HDR-02 | 模式→通知→在线用户→语言→全屏→主题为连续紧凑工具组，紧邻账号，约4px小间距，无主题与账号间弹性大空白；账号外右4px | 1440/1280/宽屏，权限导致按钮隐藏与真实降级状态 |
| NAV-01 | 管理模式一级导航底部始终显示icon+更多；只有少量一级项也可打开真实已授权列表；大量项时显示/选择溢出项；无伪造入口/权限绕过，不受二级收起影响 | 少量/大量溢出两类；真实点击、键盘、权限过滤、当前项选择；1440/1280/宽屏 |

三项纳入原开发自检→独立新鲜复验顺序，不另起任务、不扩后端。主屏CP继续，完整1280/宽屏/安全矩阵仍待稳定候选，不把新增标准追溯成此前冻结参考错误。

### CP1-R9：居中搜索/紧凑工具/常驻更多的开发代采

开发自报本轮lint/typecheck/build exit0、unit91 files/661 tests、Mock19/19（仅原checkbox弃用警告），尚未提交。17:37–17:47稳定冻结内本任务仅执行真实Chrome代采，前后HEAD ddae5463、tracked WIP LF无尾LF指纹均`A21D28EA3B0E6342F11B3D4100E7031E48306E85E33506FD93E3CC0010725147`。本次先正常刷新表格再normal reload，沿用17:04:11专用e2e.admin登录，无新增登录、清缓存或重启。

最初截图又出现1440×766/1425×758及语言切换前旧帧；重新reset/set视口并分开读取DOM后取得正确原图。以下每张均以实际落盘字节打开检查；**仅供开发亲自自检，不是独立通过**：

| 采用文件 | 实际像素/状态 | SHA256 |
|---|---|---|
| cp1-r9-dev-zh-final-1440.jpg | 1440×900 中文无浮层 | D6072534852169C5097C7B1394B5064C39AF4B3B3DDB5F518BD60BB9F8ED37A9 |
| cp1-r9-dev-en-stable-1440.jpg | 1440×900 英文无浮层 | F96C4F420F8912A6992774CC833E7D2B6430508C4FFB7AB046A7EFB5735A0D37 |
| cp1-r9-dev-more-stable-1440.jpg | 1440×900 中文更多展开 | DC6F02787667110200CA8068D2F7A3234928AE92B4D77986DDE26A4D7EB7EC54 |
| cp1-r9-dev-search-final.jpg | **1416×885** 搜索结果错位；不标1440完整图 | D61F981FE1F9A262224BA285961336EFF96299D61EE71D9022BE5CF5F60314DE |

`cp1-r9-dev-zh-stable-1440.jpg`实际抓到旧英文语言展开帧，不能用文件名当中文证据；最初无final/stable的R9图尺寸亦不合目标，均保留但不采用为主屏通过。raw包含zh/en主屏、keyboard/repeat、more与search-geometry。

原始行为/几何：中英Header与搜索centerX均720；搜索300×32；中文模式nowrap单行；theme right1242、user x1246/right1436（gap4）；th38；英文sort title已为Ascending/Descending；二级nav clientWidth=scrollWidth=208。更多按钮58×50、y840/bottom890，两授权一级项真实列出，选择工作台切换一级域，再恢复系统管理。真实`Control+K`从关闭态重复打开与点击相同的8条现有结果、activeElement为全局搜索，Escape关闭；初次小写Control+k未获得有效打开证据，不据此作通过。最终键盘结论仍需独立新鲜复验。

新阻断已交唯一开发自行读图修复：

- **NAV-01浮层图标尺寸失败**：更多菜单w199.6/h258.5，两个菜单项高127.325/119.588，House/Setting SVG实际同等约120px大小，挤得文字拆行。常驻与可点不豁免统一图标/行高；要求最小约束现有图标容器，不换导航体系。
- **HDR-01搜索浮层坐标/溢出失败**：input x562.4/w300，results x1124.8/right1434.4/w309.6；打开后document clientWidth1425/scrollWidth1434（关闭即1440/1440），图片可见右侧错误定位并引全局横竖滚动。已提示检查居中transform改变fixed包含块（待开发确认根因），沿既有teleport/定位处理，保证与居中输入关联、在viewport内且键盘保留。

健康态未触发SystemData降级，不关闭P1-03。曾为补证请求顺延至17:52，但17:47即完成并通知解除冻结；Chrome已恢复中文Users无浮层、viewport reset/暂停交互。未运行门禁、未抄送来源任务。

### CP1-R10：修复浮层后再次出现搜索回归

开发请求新冻结，报告移除TopBar transform及More子图标18px，自报定向9 files/121、unit91 files/663、Mock19/19、lint/typecheck/build0（2455 modules），后端fresh Release build0/0及1352 passed/0 failed/3 skipped。这些仍是开发侧证据。本任务17:58:53正常重载，沿用17:04:11专用会话，无新登录。

四张开发代采原图均实际1440×900，已逐张打开同字节查看，位于同外部目录：

| 文件 | SHA256 |
|---|---|
| cp1-r10-dev-zh-1440.jpg | B7885F74C900095B4785493B236631398FD439B409179F142BFD4EE458EC3951 |
| cp1-r10-dev-en-1440.jpg | 587F41F1D072920E5656B0936B82BB9B5B923A403F8BD70C99BCE30B86A4ECA7 |
| cp1-r10-dev-more-1440.jpg | D2A33DAC650C17AE93BA5837B1BE2AF13BFC2564BE7147A5016DB7F78E106578 |
| cp1-r10-dev-search-1440.jpg | 82EEAC16CC50E4ED4774D199253AEFC6570FE54181AE28262F575BDF1645B228 |

- 更多图标18×18、行36，菜单199.6×83.6，消除巨大图标；search results x570与input一致，document1440/1440，无R9全局滚条。仅记录修复后的代采事实。
- **新回归**：移除transform后中英input y28/h32/bottom60，而Header高56，截图下沿可见被裁；目标仍y12..44、vertical center28。水平center720正确不豁免垂直偏移16px。
- **交互失败**：从其他焦点点击或Ctrl+K可开；Escape关闭后input保持焦点，再click同input无法重开。中英复现，`cp1-r10-dev-reopen-raw.txt`记录focus=Global search、resultCount=0。要求沿现有open handler补正常click行为，并回归键盘、结果选择、焦点和几何。
- 18:03恢复中文Users无浮层、viewport reset并解除冻结，精确原图/复现已发唯一开发任务，不接受当前候选。
- **对象稳定性异常**：前HEAD ddae、WIP指纹`EAAF02659E009B1CA9AD8DE85C9AAEA8882ED18DA1F503D3B8D0D746000456B3`；18:03后HEAD仍ddae，指纹变为`DDFF6A20D945BFD98702A8139D633DD158630D4AA71CA1186CFBDEFB509C67C7`。首条开发消息误写前后相同，随即单独明确更正。本任务未改生产/测试文件，已要求开发说明窗口内是否恢复截图等变更；在查清前不能称stable WIP。
- 18:04只读发现22 staged文件，AppDataTable.spec.ts为+1553行；已要求开发按启动保护WIP快照明确hunk归属，不把既有用户测试整体卷入精确提交。开发提到两张测试改写旧截图需恢复，已要求具体路径、事前/恢复hash，不接受更新baseline冒充恢复。当前截图HEAD diff已无项，仍待其恢复证据与窗口时间说明。

### CP1-R11：完整冻结的已提交候选代采

开发18:18:11明确冻结所有生产/测试/截图恢复、stage/commit及build/test。独立确认候选`79eed78ca784e2b7e2b8b70d9f3660a73392a243`（18:17:50提交、parent ddae5463、21 files/456+/48-，fix(frontend): close shell review layout gaps）。AppDataTable.spec.ts仍为`??`且未进提交，开发明确它是启动前保护WIP；此前1553行误暂存已经排除。开发报告两旧截图恢复为HEAD~1同blob：1280 `f51e0b02fd99d66a3560cb5089b6cb83d6f23695`、1440 `ec42c30b30f1f4f62face4b08cb48cce32b290a1`，最终还需独立核对事前原字节；R10窗口变更时间说明仍待补齐。

18:18:51–18:23:22本任务前后HEAD同79eed78、tracked WIP LF无尾LF指纹均`75D99D21F2ED755855C821E0E0C050816423DC65EA9B52C5B277E2749E09F1A5`。先正常刷新表格再reload，仍沿用17:04:11专用e2e会话，无新登录。最初三图1440×766不采用；reset/set视口后下列三图实际1440×900且分别打开同字节查看：

| 供开发自检原图 | SHA256 |
|---|---|
| cp1-r11-dev-zh-stable-1440.jpg | 09ED0FFF84EB35B80719ECF3B7F5B944D9905E6054B64B907AEEF9F833A50927 |
| cp1-r11-dev-en-stable-1440.jpg | 054E6EC98FAF7953B8A42341415FF2BB7C8E48A6F91EFE893212B2A3D3EF25E5 |
| cp1-r11-dev-reopen-stable-1440.jpg | 38AC27F4646BFBF4181C459C71B6D513B4F3DF36C4EFE1AAA6AD64021841CA71 |

原始事实：中英input x570/y12/300×32/bottom44/center720，Header center720，user right1436；真实Ctrl+K打开、Escape关闭（results0且焦点留input）、click同input重开（results1），中文8条既有结果x570/y50/right879.6，document1440/1440，英文相同行为。`cp1-r11-dev-reopen-raw.txt`与zh/en raw可追溯。两个R10问题本次未再现，但这些仍是**供开发自检的代采，不是独立最终PASS**。

完成后恢复中文Users无浮层、viewport reset并停止操作，要求开发亲自读三张图后明确交稳定对象，同时保持全写冻结，之后才另拍NEW独立重载/新图，禁止复用本组代采图替代独立验收。尚未启动完整门禁或扩1280/宽屏矩阵。

最终产品结论：未形成。P1-01核心关闭；P1-02/03/04及HDR-01/HDR-02/NAV-01仍待独立新鲜复验。R8至R11代采修复事实不等于独立关闭，尚无本轮独立全量门禁通过声明。

### CP1：79eed78 独立新鲜复验（健康态主屏范围通过）

开发亲自读R11原图并确认冻结后，18:25:57起本任务重新正常reload，另取全新独立截图，不复用代采图。前后HEAD均79eed78，tracked WIP指纹均75D99D21F2ED755855C821E0E0C050816423DC65EA9B52C5B277E2749E09F1A5，index空；实际对象是该commit加冻结WIP，不能称纯commit运行。沿用17:04:11专用e2e.admin会话；临时经UI切工业青用于对照，结束恢复原中性灰、明亮、舒适密度/中文/Users无浮层并reset viewport。

与批准1440参考同输入并排审阅，健康态1440×900主屏范围通过：Header56、rail72.8/secondary208.8（含边框）、Tabs38、query85.6、th38、主内容16/20间距；工业平台精确裁切品牌可见，无第三方品牌/水印。新批准HDR居中与常驻更多覆盖旧静态相应位置，不误报成偏差。真实数据及已有列宽偏好与参考数据不同，底部32说明条按批准例外省略。

| 独立原图（同外部目录，实际像素均1440×900） | SHA256 |
|---|---|
| cp1-independent-79eed78-zh-stable-1440.jpg | 0D7F931F8A0B63C3835FB52E35EE61573D647E35C4D3DBCEA30203DB5D55EDA9 |
| cp1-independent-79eed78-search-1440.jpg | BB97DE87E8980CD7C778C189743A9580B177B622BDD5CD83E183A64C04D4FA73 |
| cp1-independent-79eed78-more-1440.jpg | B27284ACC4B72D262584956F2626B4E26C7077006E61F7820268925119277729 |
| cp1-independent-79eed78-language-1440.jpg | 2A7824F885A51D7B3793CB378DF4105BC5C396B8DE3E835033E8C8836A14AD45 |
| cp1-independent-79eed78-en-1440.jpg | 45F00B2E25D03A99889EEE0C93A6788385E84435E965466B395332237918EB74 |
| cp1-independent-79eed78-empty-1440.jpg | 1CD9FC30A00732FB3D111560AFCF92F60AAD924B19213FD3C48D5DCB57575A96 |

首张无stable中文图实际1440×766不采用；以上六张均经落盘尺寸检查及同字节打开审阅。raw分别含zh/en/keyboard/more/more-selection/language/empty/logs。中英search x570/y12/300×32，center720等于全Header中心；theme/account gap4、account right1436。Ctrl+K→焦点搜索/results1，Escape→results0，再点同input→results1，同8项；浮层x570/y50/right879.6，document1440/1440。More菜单199.6×83.6、行36/icon18，列出两个真实授权一级域，选择工作台实际切域并恢复系统管理。语言浮层144宽，在视口内。

英文两tabs/导航/分页/排序title和No data空态均已本次确认；真实查询无匹配→0条、Reset恢复3条，alert为零。18:25:50Z起浏览器可读日志recent为空，历史超时日志独立保留；尚未取得本批专用网络/pageerror采集，不将recent空数组夸大为完整失败网络零。

本阶段关闭P1-02健康主屏残余、P1-04当前两Tabs与表格locale、R9/R10的图标/浮层错位/裁切/重开回归；HDR-01/HDR-02/NAV-01的1440健康少量域子项通过。P1-03降级状态、旧Tabs迁移全路径、1280/宽屏/明暗及大量域矩阵、权限/安全/真实接口契约和fresh全量门禁仍待执行。**这是进入扩展验收的CP1范围通过，不是最终ACCEPT。**

### CP2：1280扩展首次复验 FAIL；79eed78独立门禁记录

开发idle/全写冻结状态下运行：fresh `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` exit0/0 warnings/0 errors（12.27s）；其后完整 `dotnet test ... --configuration Release --no-build` exit0，BuildingBlocks151、Integration11+3条件skip、Gateway14、ReferenceData14、SystemData545、Identity597、UnifiedHost20，共1352 passed/0 failed/3 skipped。最后UnifiedHost测试1m51s完成。运行前只读识别用户UnifiedHost3360在Debug、用户Vite7752和本任务Vite50336；没有Release host/testhost/VBCSCompiler锁，保留用户进程，Release构建未发生锁错误。CIM普通沙箱拒绝访问，受控权限重试成功；netstat明确5041/5173真实监听，不能相信开发“无监听”的推断。

前端依次lint exit0、typecheck exit0；test:unit首次配置启动EPERM（node_modules/.vite-temp，无测试断言执行），同命令受控权限重试exit0，91 files/664 passed/0 failed（20.43s）；build exit0，2455 modules，仅既有>500KB chunk warning。本批没有改变生产/测试源码，也未运行Mock/真实Playwright或额外定向测试。DSH SHA256仍D2F2ED4CB3D01B65224BE1E1314A8B30D3229B3605C2BA6641A20E11522C0BD0，保护路径baseline..HEAD diff空。

18:42–18:43扩展真实Chrome1280×720，中文再切英文：

- **CP2-HDR阻断**：英文search x524.9/right739.9、模式组x711.7/right889.8，重叠28.2px；图上Management覆盖搜索尾部和Ctrl+K。居中正确不豁免遮挡。user right1260.8，而窗口1280；全局滚条占15px。
- **CP2-QUERY阻断**：中文/英文主查询均高129.6（批准1280约85单行五字段+操作），操作换到第二排；真实英文pager y694..726越过720，document scrollHeight755，产生全局竖滚、分页首屏被裁。表格x297.6..1248、height395，不能以表格已有横滚解释此全局竖滚。
- `cp2-zh-light-1280.jpg`、`cp2-en-light-1280.jpg`实际原像素1265×712；保留为失败可视证据，不伪称1280完整图。与真实1280的REF01原图并排审阅；`cp2-en-1280-failure-raw.txt`记录DOM实际viewport1280×720及以上坐标。reset/set视口后document仍1265宽/755高，故不是仅截图尺寸误差。
- 失败证据SHA256：中文图`8185B99EC6F2C3D31B75E37FCB5E6F04BAF194F7BED4E9E6AB580F53A6995A76`；英文图`587C748C00E9B368A1A8F3F518371E32515F0784662EC2ABEBECE24A4384300F`；英文DOM raw `FD63496DF15A362CCE64F14A323A5E2FB8D234E590252F97A22B5B8A4E1DB627`。19:31只读补指纹，没有新截图或重新运行页面。
- 精确范围/复现/实际期望/回归已发唯一开发任务。此时所有独立构建测试已结束，明确解除写冻结让其修复，停止后续门禁与浏览器验收。恢复中文时首次错误使用旧locale选项名“中文”，selector失败；新快照确认英文选项名Chinese后正常点击恢复，reset viewport并markHandoff，未修改产品。

本轮仍无最终ACCEPT。CP1健康1440范围通过不能覆盖CP2窄屏失败；修复公共Shell后将重新运行相关完整门禁。

### CP2等待修复期间的独立静态发现（均已发唯一开发，尚未运行新测试）

1. **SESSION-TIME**：`RefreshSessionStore.ListActiveForTenantAsync`把当前有效refresh行CreatedOn/LastUpdatedOn交给`IdentitySessionManagementService`作为LoginOn/LastRefreshedOn；`AuthenticationService.RefreshAsync`新旋转行CreatedOn=now、ToTable.LastUpdatedOn=now。t1登录→t2刷新后“登录时间”会变t2，不再是原登录时刻。既有API测试用Fake预造不同时间，未覆盖实际Store旋转。要求真实Store fixture证明t1/t2语义并最小修复，不增加敏感存储。
2. **ODATA-PAGING**：parser的`checked((int)(skip/top)+1)`与adapter的`checked((PageIndex-1)*PageSize)`没有转换OverflowException为稳定400；top1/skip2147483647及top100/skip2147483700分别命中两处溢出。要求parser/adapter/API边界回归，客户端参数越界不得500。
3. **ODATA-LIKE**：SqlSugarQueryAdapter.Contains/StartsWith把%value%/value%作为参数但不转义LIKE的%/_，合法OData字面百分号/下划线被扩大匹配。要求含特殊字符账号与普通账号SQLite fixture及PostgreSQL一致性，不将参数化等同于字面语义正确。
4. **NAV-AUTH**：PcLayout.commandItems无permission/anyPermissions过滤且非递归；runtime无snapshot/空结果恢复完整静态目录。仅platform权限的mock或真实降级limited可在全局搜索列出无权Identity菜单，Router403不替代授权搜索。applyNavigationPolicy.filterItem在所有children被过滤时空对象spread保留原children，且未检查anyPermissions。要求同一既有授权路径最小修复、空组删除、递归子菜单断言。
5. **覆盖缺口**：现ToolRail仅2/3域测试，缺用户指定>=12域高度变化/More活动项/键盘回焦/权限过滤；SessionControls仅5项，缺loading→empty、error→retry/refresh、current revoke→local logout及Escape回焦；降级英文Header仍缺受控状态视觉。已要求开发补同现有测试体系，不能用只有源码/测试名的项判PASS。

上述为独立源码逐条审查所得，标明未在开发写入期间运行测试；需要开发先RED复现/修复/自验，再由本任务fresh独立复验，不引用此前全量通过掩盖未覆盖分支。

6. **QUERY-PARITY/ROW-CONTRACT**：`IdentityUsersPage.loadUsers`顶部查询走legacy含groupNId/roleNId/includeDeleted，表格loader/Excel另走builder只合并nId/loginName/name/status，分页/排序/刷新/导出会丢组/角色等已选条件。AppDataTable.descriptor.select仅可见列；Users列没有userNId/optimisticVersion/concurrencyVersion/原角色数组，OData后行操作却使用这些字段。normalizeODataUser仅用空数组替缺省，不会恢复ID/并发或真实角色。要求用组/角色不同用户对照列表→排序/分页/刷新/导出同集合，并验列头查询后详情/编辑/分配角色的真实身份版本；保持OData软删/权限基础范围，不能用虚构默认值或静默放宽解决。此项已明确发回唯一开发任务。
7. 真实golden既有测试只有切换/截图，没有覆盖完整列头输入清空分页导出的请求/响应与console/pageerror/alert，已要求同现有real spec补URL/参数/status/脱敏body附件。独立验收将重跑并读原附件、与自启fresh Release日志交叉核对；不通过Chrome隐藏状态或fetch伪造网络证据。
8. **ODATA-STATUS/EXPORT-TZ**：Users status在schema/前端为Active/Disabled文本，SqlSugar原始参数却直交数据库INTEGER status列（UserTable.UserStatus、IdentitySchemaMigrations），缺受控代码→存储类型映射。要求Active/Disabled与非法状态真实Store/API回归。Excel当前timeZone来自浏览器Intl而非独立localization.preferences.timeZone；要求与浏览器不同的已保存时区仍准确传给服务端、locale切换不重置。已发开发，尚未运行独立复现。

9. **I18N-ERROR/DATE**：`api/errors.ts`通用network/timeout/server/unknown文案硬编码中文，`reportManagementError`优先输出ApiError.message，因此英语真实失败路径不能仅依靠页面t()判通过；`shared.ts`409并发冲突对话框仍硬编码中文。其formatTime仅replaceT/slice19，不使用现有locale/timeZone格式化能力。要求英语network/timeout/409、未知服务端code/trace兜底和独立时区回归，复用已有formatter，不新增库。已发唯一开发，属于规格既定国际化/时区范围；尚未运行独立动态复现。

10. **SESSION-REVOKE-WRITE**：明确对79eed78的git show复核，新管理员撤销service调用既有SessionRevocationStore，后者吞掉Redis写撤销键异常仍返回；API随后revoked=true。PermissionEvaluator仅查Redis键，故仅写入瞬断而随后读恢复时，目标Access Token仍可能授权。已要求现有mock/store异常RED证明，新增管理员路径必须确认撤销或稳定错误/可重试，禁止假成功；不借此重建认证。回归单目标access/refresh失效、其他会话不受影响、跨租户、view-only、重复幂等。另PlatformSessionControls.formatDate未取preferences.timeZone，作为同一I18N-ERROR/DATE补充已交开发。此项为静态可达路径，动态证据待修复冻结后独立补齐。

11. **ODATA-NULL**：79eed78原提交BuildModel将字段声明nullable=true，ToBinaryFilter接受ConstantNode.Value=null，但adapter Eq/Ne生成`column = @p`/`column <> @p`并传null。SQL三值逻辑不符合合法email/lastLoginOn eq null和ne null查询语义。已交开发在parser→真实Store fixture证明NULL/非NULL行及列表导出一致性，最小处理或对不支持组合稳定400；不扩展查询语法。此时仍未在写入工作树运行动态测试。

12. **TABLE-BOUNDARY/VXE-BOUNDARY**：设计6.6/9.2要求在线抽屉统一表格和AppDataTable唯一出口；79eed78新PlatformSessionControls却直接ElTable/ElTableColumn。另vxeDomAdapter仅包装任意selector的querySelector/closest，AppDataTable脚本仍掌握private header/custom/fixed-hidden选择器及body--wrapper类和结构操作，不能支持“VXE升级只改适配”声明。20:00后补发原开发：沿现有公开表格契约复用最小抽屉能力，小步收拢实际私有DOM知识/契约断言；禁止整表重写或覆盖保护WIP。私有结构与公共主题CSS需在交付明确区分，不作无界表格重构。这是对既定边界的补核，不运行写入中测试。

### CP2交接状态（20:00）

19:59只读检查进程/文件时间，没有运行构建测试或操作浏览器。开发回合01a0576a-e248-7361-91f2-f95fad5f603c在wait_threads显示completed/idle，但wait与read_thread的最终消息均为空、items=[]，没有新final commit/稳定WIP/自验交接。已仅向原开发任务要求继续并显式补齐；20:00新回合01a057b0-ea95-7651-901f-af50d82e9d87变为active。不能将任务回合结束等同交付完成，本任务继续停测；此前门禁1352/664不验证当前修复WIP。

### 追加独立只读依赖/边界核对

- `Directory.Packages.props` baseline→79仅新增Microsoft.AspNetCore.OData9.5.0；本地`C:\Users\DONG\.nuget\packages\microsoft.aspnetcore.odata\9.5.0\microsoft.aspnetcore.odata.nuspec`直接确认version9.5.0/license MIT。
- package新增vue-i18n11.4.10，pnpm-lock +101/0；其本地LICENSE为MIT。baseline Git未包含的VXE4.15.13/XE3.5.31在启动前WIP及启动表格测试已存在，不当成本轮新选第二表格框架；两者本地LICENSE亦MIT。最终需和开发WIP归属清单再确认交付差异。
- Querying.csproj空SDK项目，无Web/OData/SqlSugar/业务依赖；Web引用Querying+官方OData，Infrastructure引用Querying并使用SqlSugar。UsersODataController ActionResult为平台PageResult/投影字典，无IQueryable输出或EnableQuery特性。
- `rg`在生产业务页/Stores检查直接vxe-table/xe-utils import、localStorage.clear/sessionStorage.clear无匹配；测试fixture的clear只用于测试隔离，不误报生产实现。
- ApiResult/ApiResult<T>新增Parameters/TraceId均WhenWritingNull，保持旧成功JSON；八卡注册仅title/icon/state，无route/API；PF04边界文档明确未实现Audit/File/Notification。不将既有Identity登录审计误报为PF04新增服务。
- uiCacheStore按tenant/user Tabs精确key、table-preferences前缀、page-state v2前缀移除，dispatch内存清理事件；无全量clear/服务端调用。白名单源码不能替代尚待本轮实际UI与失败态验证。

### CP2-R12与暂停后恢复（20:29–21:40，尚未形成接收结论）

- 开发交付 `dca66c79ab664bfb35e794c71ab93d18490d221e`（parent `490d084e3db8b74ab3f9d6da938e21a865afebf5`），自报后端1364 passed/3 skipped、前端91 files/675 passed、Mock19/19。这些是开发自验，不是本任务独立结果；本任务尚未在dca候选运行fresh门禁。
- 独立只读核对HEAD相符，实际为42 tracked modified、16 untracked、index空、ahead40，不是开发所报40 modified。tracked WIP指纹为 `D9257AA58EE27324140A010924406D6366B55C35329ED5B294D1270DEFE7A84D`（git diff HEAD文本以LF连接，无末尾LF，再UTF8 SHA256）。两个额外SystemData测试的归属仍需解释；不笼统将全部WIP判成开发前即有。
- R12冻结后代采发现：真实用户已切到admin，本任务没有退出/重登或创建账号。20:31前5041实际ready200；20:31–20:33外部调试服务停止，Vite记录断开/重连，5041最终拒绝连接。本任务未执行启动/停止命令，不能将这一时间段失败归成代码或外部云依赖故障。
- 退化态中文/英文1280与英文1440原图分别保存为 `cp2-r12-dev-zh-degraded-1280.jpg`、`cp2-r12-dev-en-degraded-1280.jpg`、`cp2-r12-dev-en-degraded-1440.jpg`。前两图已实际打开且尺寸1280×720，SHA256分别为 `CFD175222D960934F522800B31864EA8A5ABEE944169D8591210FDD0DA47FFB4`、`E40E721EF10109FD911DF8CC92350992E61280B6363FCA6A46AFE966064D8100`；第三图暂停前尚未完成像素/哈希核验，不作通过证据。
- R12 DOM：1280英文search x560..720与mode x474.625..652.725重叠；1440英文search x570..870与mode x597.9..776重叠。退化提示重复出现在header及右下。英文提示本身已翻译，不能误报为中文残留。折叠二级宽52.8但内容仍始于280.8，空保留列约155px。空数据退化态query85.6、真实el-pagination bottom691.2，未复现旧query换行/越界，但不足以关闭健康数据矩阵。
- `cp2-r12-dev-logs.json`保留失败网络及大量Vue reactive component warnings；未以“无异常”判通过。开发golden定位`.users-page__pagination`不存在且以null→0兜底，不能证明真实分页在视口内；Mock配置明确排除golden，19/19不能声称golden运行通过。
- 用户20:35要求暂停，本任务立即停止，保留所有会话、服务、WIP和证据，未继续发消息、运行测试或恢复偏好。用户21:31明确继续，并要求按最新交接、复用现有调试环境。
- 完整重读交接最新“暂停后恢复补充”，文件SHA256 `344093AA998CB330FD5F04E9499E7D150C84C704895FC3D47227F9FFE29ECDFC`。新增HDR-03（搜索按左右真实占位响应式避让）、NAV-02（折叠释放外层列宽并保留图标/嵌套导航/筛选无结果后可达）、USR-01（Element Plus菜单192/36/13/16/10px）为本次强制增量。它优先于旧稿相应条款，不继续用“折叠必须完全隐藏图标入口”误判最新要求。
- 已向唯一原开发任务发送恢复指令并解除修复写冻结；其wait_threads显示active。开发写入期间本任务仅只读检查/整理证据，不运行共享工作树build/test/format/clean。未新增任务、未询问用户、未抄送来源任务。
- 恢复时实测用户UnifiedHost PID12804（21:10:53启动）5041 ready200；用户Vite PID62952（21:11:02启动）5173 users HTTP200。本任务不重启或另启任何服务。Chrome当前真实admin/英文/2048×1090、`/pc/home`，与暂停前页面不同，保留用户最新状态。
- 21:38只读截图 `resume-2138-existing-home.jpg`已实际打开：仅More一级、无二级，主内容落在约x72..280的窄列，其余宽屏空白；可能空导航/HMR与外层网格隐式定位组合，尚未在稳定重载后定因。该现场已统一并入NAV-02交开发，不将开发写入中的画面当最终复验。页面只读取，没有重新登录、改主题或导航。

当前结论仍未形成；旧ACCEPT不适用于本轮。下一步等待稳定候选及开发自检，再统一新鲜复验三项增量与原必验范围。

补证：恢复后只读取已落盘文件，确认`cp2-r12-dev-en-degraded-1440.jpg`实际1440×900、SHA256 `639C176ACC573F34B38483FD6C62F020B5A99F7514EE1A31645416F47FCE57BD`，已打开相同字节，模式与搜索遮挡、155px空保留列及重复退化提示均可见。`resume-2138-existing-home.jpg`实际2048×1090、SHA256 `76637CFC47EA8F363B71EF721CBA1440CB08BC97C745E23C319A281F731F23D4`。均是失败/恢复现场记录，不是稳定候选通过证据。

### CP2-R13：90a4d7e 稳定候选真实页面统一取证，三项退回

22:00开发回合completed，但wait/read返回空final/items；要求其直接发送交付后取得明确全写冻结。独立HEAD为`90a4d7ea6cd9721a918f231d91c60cc7607e9f75`，parent dca66c79，7 files/189+/52-，ahead41；前后tracked WIP指纹均D9257AA58EE27324140A010924406D6366B55C35329ED5B294D1270DEFE7A84D。42M/16??/index空。开发自报91 files/677 unit、3 files/48 targeted、Mock23/23（golden仍被Mock排除）、lint/typecheck/build0/2456modules；本任务未重新执行这些命令。

22:02正常reload既有Chrome，仍是真实admin会话（登录21:11:33），无新登录。5041 ready200/PID12804与5173监听PID62952实际存在，反驳交付再次声称所有端口NO_LISTEN。本任务未重启或另开服务。下面是稳定代码上的开发代采/独立问题核实；因为实际阻断明确，未进入全量门禁或最终PASS阶段。

| 项目 | 实际结果 | 状态 |
| --- | --- | --- |
| HDR-03 中文1280 | search499.2..780.8/center640，模式不重叠，user right1276，document1280×720 | 此子态未见原遮挡，不代表整个矩阵通过 |
| HDR-03 英文1280 | actions788.8..1103.575、overflow hidden；Theme1109.9..1143.9完全在裁切区，主题中心命中User menu1107.575..1276；全屏右部亦裁 | FAIL，已退回 |
| HDR-03 英文1440 | Theme right1250.5、user x1248.94，工具末端仍部分裁切；search561.6..878.4/center720 | FAIL，已退回 |
| 宽屏2048 DOM | search450.55宽、center1024、user right2044；提示完整可见 | 原图实际2047×1090，不标完整2048截图PASS |
| 无二级主内容 | 真实nav为空时main从72.8开始填充剩余宽度，恢复时被挤进208px窄列问题不再出现 | 原布局症状改善，NAV整体待验 |
| NAV-01/02真实授权导航 | reload健康admin后仅More，无FunctionTree；打开More为0 menuitems；Ctrl+K仅3个recent Tabs，无导航项。真实Users3人、首页有6个授权快链 | FAIL/待根因，不能用Mock折叠PASS替代 |
| USR-01菜单几何 | popper192宽、四行36高/13字/16图标/10gap、退出分隔与危险色；英文1280及中文工业青1440实测 | 尺寸子项符合 |
| USR-01真实账号摘要 | 与批准REF01同1440×900/中文/工业青明亮并排查看，当前popper只有四命令，高168.4，缺姓名13/账号·租户11的摘要区和分隔 | FAIL，已退回；header按钮两行不代替菜单摘要 |
| 管理/生产往返 | 实际`/pc/operation`可达，8按钮disabled=true且aria-disabled=true；3×3布局，第九设置可用；返回`/pc/identity/users`仍4 Tabs/3人 | 局部行为证据，零网络及无权账号等仍未验 |

R13严格读取DOM与截图，不调用页面隐藏状态/储存/fetch。初次语言option点击时浮层已关闭导致selector no_matches；读新状态后正常重新打开并切换，不隐瞒工具失配、不将其误记成产品异常。可读浏览器日志仅发现21:46/21:49开发HMR时的`Cannot access PcLayout before initialization`，不把早于22:02正常reload的历史错误冒充稳定候选错误；`cp2-r13-browser-logs.json`保留原记录。尚无该批完整request/response/pageerror专用捕获，不能宣称零失败网络。

截图同外部证据目录；以下均核对实际尺寸，主要缺陷图已打开同字节：

| 文件 | 实际像素 | SHA256 |
| --- | --- | --- |
| cp2-r13-dev-zh-1280.jpg | 1280×720 | C5B72A918C6788B5A8383EE894FDDFE80FE06E409160C9F675C77E0FB8FD4B4F |
| cp2-r13-dev-en-1280.jpg | 1280×720 | 4F6D4DD3443430DD07E1B3627299F8684179E5FFDBF24392119878222FE0B634 |
| cp2-r13-dev-user-en-1280.jpg | 1280×720 | F57BA0A1160E05FC8E2B8CC309C6A36EE2ACC4BF37C32FACAF3D56566A1688CF |
| cp2-r13-dev-en-1440.jpg | 1440×900 | 940AC7CEBF0CB73BEE140A4946DDFD8D7A3C911B11658BC990845427E5FA5A3B |
| cp2-r13-dev-empty-more-1440.jpg | 1440×900 | 58C8244580F0FDE12364BA7CA45EA9609B5FF807DA402E59EF5C39B258042B49 |
| cp2-r13-dev-user-zh-cyan-1440.jpg | 1440×900 | E8E721A4FCFB2CDEC3F7547F6D0F5D6A4C6A5CD7F6B89A9130DF6F3171323A57 |
| cp2-r13-dev-operation-en-1440.jpg | 1440×900 | 8F5C320D9C9C1F8BDE3F53C8120D3DA61C5C6695A5D4FDA4666E666B61CA6CC7 |
| cp2-r13-dev-en-cyan-2048.jpg | **2047×1090** | 39B4FCD58453A947A78C408DDFB157C9D4143EAFE86DE2CFCFA3273F1E5C37FC |

已统一向原开发发送三项条款/文件/几何/复现/回归范围及原图，并解除写冻结供其修复。要求不能用`overflow:hidden`隐藏真实工具来满足box不越viewport，补全部工具祖先裁切与点击断言、英文/长用户名/退化/连续宽度；调查真实nav snapshot与授权，不直接改库伪造导航。再次要求真实配置复用5173/5041及golden真实pager/查询网络覆盖。

本批结束经UI恢复中性灰、跟随系统、舒适密度、英文（恢复时原状态），关闭浮层、viewport reset、停在Users四Tabs，保留真实用户会话。没有build/test/格式化/清理/生产或测试源码修改、没有push、没有新服务。当前90a4d7e仍不能ACCEPT，等待原开发统一修复后重新验证。

R13等待修复期间补充同一NAV回归：`PcLayout.authorizedNavigationGroups`仍以`applyNavigationPolicy(pcNavigationGroups, permissions, new Set())`二次过滤已经由runtime按实际feature集合过滤的导航。若服务返回有权且feature f1已启用的真实条目，runtime保留后会被搜索的空feature集合再次剔除。已将该最小场景、已关闭feature/无权限不得fallback恢复的反例一并交开发，未在写入工作树运行fixture。本记录为静态路径，不包装成已执行动态测试。真实Playwright5173/5041复用入口及golden错误分页选择器/请求契约覆盖再次明确要求闭环。

### THEME-01：最新用户强制边界（22:50追加，未验收）

完整重读交接文件最新顶部“保留既有功能与顶栏主题联动（优先于静态配色）”，文件SHA256为`C6E40C1730EFC9973EA852A57FD42383E1A771597D7480B3E5F409B4A0BD05E9`。该增量已直接发送唯一开发任务，要求并入R13当前修复；没有另开任务或自行改生产代码。

- 独立只读`git show eadad6224622635db9f0cc91792ae07c2bf05179:src/frontend/src/styles/themes.css`确认原工业青、科技蓝、中性灰分别存在不同的已批准顶栏渐变，背景/文字/次文字通过语义token消费；旧基线未另将dark顶栏覆盖成统一色。
- 当前工作文件三配色的`--ip-shell-topbar-background`均为`#172a42`，dark统一覆盖`#101c2b`。这是有基线支持的主题能力退化静态证据；R13两配色截图同见海军蓝仅作辅助，不冒充新增要求后已经完成逐配色动态矩阵。
- 必验：三个既有配色、明亮/暗色/跟随系统，真实顶栏背景及文字/icon可读，刷新保留偏好，管理/生产模式往返不丢主题；不能用清空偏好或固定原型色制造通过。精确效果以恢复原有主题语义链路为准，不擅自为暗色设计新渐变。
- 原有正常功能默认保留；未获用户明确同意的功能删除、替换、禁用或行为变化均不予放行。修复退化已授权，视觉接近不能豁免退化。
- 开发仍active；本任务仅只读检查与报告记录，没有运行build/test/format/cleanup，没有操作真实浏览器，也没有重启/另开调试服务。等待开发稳定final commit、WIP指纹和自验证，再统一新鲜复验。原ACCEPT仍不适用。

同次等待只读审查发现开发已提交`11ebfa239c1278fa48791880dcc125aad6dfc6a6`，13 files/350+/46-，尚无最终冻结。导航新增configured=false时使用真实本地目录再授权过滤、搜索改为仅做二次权限过滤、菜单加入真实摘要；这些仅为实现事实，仍须真实验证。其新`PlatformTopBar.updateSearchLayout`将inline transform设为none却保留top:50%，会失去搜索垂直居中；width继续受centeredWidth硬上限，在右侧占位超过顶栏中心时可变0，即使左右仍有可用空间。已交开发补中心Y/上下边界、窄屏可用宽度、英文/长用户名/退化回归。本任务未执行该候选页面，不把静态推演当动态实测。

11ebfa2的golden只更正pager定位并新增大于0断言，尚未补输入/清空/分页/导出请求契约及alert/console/pageerror/network采集；已再次明确未覆盖清单，避免用测试名称判通过。真实配置已支持5173复用，API默认仍5080，最终运行必须显式使用现有5041；不得因配置回退新启调试服务。

### CACHE-STATE-01：等待期间新增静态调用链证据

23:05只读核对既定清理缓存必验项：`uiCacheStore.clearCurrentUserUiCache`向globalThis/window派发事件，`AppDataTable`却在document监听相同事件；该事件不会从window向document下传。`PcLayout.clearCache`随后仅重置Tabs store，未导航；当前reloadVersion通常0→0，RouterView key未必改变，因此不能假设当前表格通过重挂自动清除内存。表格`clearUiCacheState`即使被调用也未重置列preferences；Users卸载仍有persistPageState路径，需避免清理后回写旧状态。已向唯一开发发送精确符号/链路及回归断言，要求通过真实公共清理入口+挂载组件重现，不能测试中另向document派发事件规避缺陷。此时没有在开发写入中的工作树跑测试，也未动用户实际Tabs；最终动态证据待修复冻结。

23:13开发直接消息确认仍处理THEME-01/HDR-03/CACHE-STATE-01，无等待验收代采的卡点；其自述已用TDD暴露并处理主题渐变、搜索垂直居中/窄屏非零及浮层border-box，但连续视口浮层定位仍受resize时序影响，继续修复。此为开发进度，非独立PASS；尚无新final commit/写冻结，本任务继续停测并保留现有环境。

23:18将模式往返的列头查询状态补入同一状态回归：Users的tableQueryMode每次初始top，进入header清空top query，onTableQuery仅写页码/每页数，persistPageState不含header模式/filters；AppDataTable.headerFilters是局部ref，PcLayout RouterView无KeepAlive，生产模式切换会卸载。静态路径显示“列头账号admin生效→生产→返回”不能仅凭URL/Tabs恢复判通过，必须新鲜实测模式/筛选保持。已要求原开发复用公共表格/会话page-state契约作必要最小修复和回归，不允许将业务查询长期存localStorage，不允许整表重写。本任务未运行写入期间的动态复现。

23:25等待机制返回开发回合01a0582a completed/idle（revision7），read_thread仍items=[]/final=null；HEAD仍11ebfa2，主题/搜索修复及相关测试仍有工作树修改，无稳定最终交付。已要求原开发直接提供交接；若工作未完则继续原任务。未将回合结束当完成，没有趁idle启动验收测试或操作浏览器。

### CP2-R14：c992caa冻结交付与独立门禁（2026-09-01）

- 开发交付`c992caa3d997cf85e90b6eb0fc681faf48c91f24`（parent/本轮baseline `11ebfa239c1278fa48791880dcc125aad6dfc6a6`），16 files/622+/32-。独立核对HEAD相符、ahead43、41 tracked unstaged/16 normal untracked/staged0；tracked WIP指纹`BA3FCF262C0893BE4EA97D904EDCDC01CFE3D2E7BF4023FC324DB0AE5AC153A8`，DSH哈希仍`D2F2ED4CB3D01B65224BE1E1314A8B30D3229B3605C2BA6641A20E11522C0BD0`。开发明确全写冻结后才开始门禁。
- 开发自报Mock24/24但未人工打开新截图；真实golden/网络契约未补也未运行；THEME-01生产`themes.css`未由c992修改，而是当前最终无diff的既有渐变状态，c992只加主题契约/E2E。上述均不被包装成独立通过。
- 独立环境发现开发“所有端口无监听”报告不符：原用户UnifiedHost PID12804（08-31 21:10:53）仍同时监听127.0.0.1/[::1]:5041，Vite PID62952（21:11:02）仍监听[::1]:5173，5173 Users HTTP200；5041 TCP约0.8ms连接但`/health/ready`和`/`分别15s/5s无响应，存在大量CLOSE_WAIT。Chrome未reload的现有admin页面仍保留四Tabs/真实导航，但数据为0且SystemData degraded/unavailable。VS标题为`IndustrialPlatform (正在调试) - ManagementStore.cs`；只读截图权限超时，未控制/恢复调试器。此现场是“监听但不响应/可能调试中暂停”，不是DNS/TCP或服务未启动，不能作外部缺口结论。
- fresh backend Release build exit0，0 warnings/0 errors，11.68s；full `--no-build` exit0，BuildingBlocks158/Gateway14/ReferenceData14/Integration11+3skip/Identity602/SystemData545/UnifiedHost20，合计1364 passed/0 failed/3 skipped。
- frontend lint exit0；标准typecheck首次因node_modules/.tmp EPERM exit1，按原命令受控重试exit0；标准unit首次因.vite-temp EPERM exit1，按原命令受控重试92 files/689 passed；标准build首次因.vite-temp EPERM exit1，按原命令受控重试exit0、2456 modules，仅既有大chunk warning。

独立源码复核c992新增阻断，已退回唯一开发并解除写冻结：

1. **CACHE-STATE-02**：`PcLayout.clearCache`清storage后仅调用`tabsStore.clearUiCache()`；store把tabs/active变为固定Workspace，但PcLayout无activeTab watch，clearCache未调用navigateToActive。位于Users执行会出现RouterView仍Users、Tabs只剩Workspace的不一致；新UiCacheClear测试只挂AppDataTable，未走真实PcLayout命令/路由。
2. **QUERY-STATE-02**：`IdentityUsersPage.toPageStateRecord`只保留string/string[]；现有`mustChangePassword`列头option值是boolean，true/false在管理→生产→管理往返会丢失。新增测试只覆盖loginName string。要求安全持久化现有合法过滤类型并对未知/旧字段容错，不放入长期localStorage。

当前仍无ACCEPT；等待下一稳定提交，仅复跑受影响前端/Mock后再恢复真实Chrome验收，后端无关范围不重复运行。

### CP2-R15：3cfd133 独立复验，真实 1280 黄金页阻断（2026-09-01）

- 开发交付 `3cfd1335c73c7daf434136cc4fc44d5974df99e0`（parent/baseline `c992caa3d997cf85e90b6eb0fc681faf48c91f24`），5 files/75+/15-，ahead44。独立核对HEAD、完整增量diff及`git show --check`均相符；只涉及PcLayout缓存后导航、Users/page-state布尔值及相应测试，没有生产边界扩张。
- CACHE-STATE-02代码链路关闭：PcLayout公共清理命令在tabs重置后调用既有`navigateToActive()`，定向PcLayout回归证明固定Workspace、activeTab与`pc-home`路由同步。QUERY-STATE-02代码链路关闭：安全值域为string/string[]/boolean，Users测试覆盖loginName文本与mustChangePassword=true同时写入/恢复。未知或非法旧state由既有read校验返回null，不形成错误OData参数。
- fresh frontend：lint exit0；标准typecheck首次仅node_modules/.tmp EPERM，受控原命令重试exit0；完整unit首次仅.vite-temp EPERM，受控原命令重试92 files/691 passed；build首次仅.vite-temp EPERM，受控原命令重试exit0、2456 modules，仅既有大chunk warning。后端因该提交纯前端且c992已独立fresh build+1364/3skip，不重复运行。
- 独立Mock目标以临时4173运行后自动停止，pc-shell/pc-operation-mode/workspace-tabs/systemdata-admin共24 passed/0 failed/0 skipped；输出定向外部证据目录`.../shell-review-20260831/mock-3cfd133/`。独立实际打开四张新图：1280/1440管理壳无整体裁切；生产壳3×3卡片，第九界面设置可用，其余八卡均待实现。Mock日志仍有Element Plus checkbox deprecation warning，未伪称console全静默。
- 用户UnifiedHost未由本任务干预即自行恢复；独立`GET http://localhost:5041/health/ready`返回200/Healthy，Identity/SystemData/ReferenceData PostgreSQL、Redis、RabbitMQ、Outbox均Healthy。真实Chrome正常reload后仍为admin会话，`/auth/me`、features、navigation、theme-policy均200；Users显示3名真实用户，完整14个系统管理入口，顶栏含生产模式、通知、独立在线用户、语言/全屏/主题。
- **REAL-GOLDEN-03阻断**：独立真实配置复用现有5173运行`user-management-golden.spec.ts`，首轮真实登录和用户页数据成功，但line62失败：1280×720下`pagerBottom=726 > viewportHeight=720`。同一用户Chrome独立DOM测得`scrollHeight=755`、pager top694/bottom726/height32；外部原图`real-admin-users-3cfd133-1280x720.png`已保存并实际打开，分页底部被裁。real Playwright retry第二次登录停留/login，不改变首轮确定性几何失败。
- 已向唯一开发任务发送精确规格、SHA、命令、实际/期望、截图与回归范围，禁止删断言/改阈值或控制用户服务。开发写入期间再次停止共享测试与真实页面操作。3cfd不能ACCEPT，等待最小布局修复稳定提交后复跑affected gates、Mock/real golden并继续真实主题/权限/会话矩阵。

### CP2-R16：5f2c6e6 几何闭环、主题实测与英语首页 P1（2026-09-01）

- 418a31e第一次修在错误层级，独立真实golden仍为pagerBottom726。现有Chrome计算样式定位`.ip-pc-function-and-workspace`高664，但隐式auto grid row使`.ip-pc-content`高698.8/bottom754.8；将完整链路退回后，开发提交`e63bec011b9b0b2087979b469dfaf190dd27b3ae`，改为`grid-template-rows:minmax(0,1fr)`并保留/新增真实content/pager/document几何断言。纯测试提交`5f2c6e6ab28814408eb855c5daa61cba7a08975f`随后把错误的submit+ShiftTab→reset断言改为自然DOM顺序submit+Tab→reset。
- 独立最终HEAD本地门禁：lint0、typecheck0、unit92 files/692、build0/2456 modules、Mock pc-shell/operation/workspace/systemdata 24/24。目标前端Session/Profile/Cache/Theme/Operation六文件26/26。后端沿用c992 fresh build与full1364/3skip；另在同一Release二进制定向复核OData parser16/16、SessionManagement9/9、RefreshRotation12/12、ManagementEndpoint28/28。
- 几何闭环的现有真实admin DOM（不刷新/不请求云）：content bottom720、main720、users704、footer703.2、pager691.2、document.scrollHeight720、grid row664，1280×720首屏分页不再裁切。真实golden只执行一次：本次登录停留/login，按协调边界记为同一`CLOUD-DEFERRED`，不循环登录、健康或刷新；开发此前在e63上已越过content/pager/document三高度断言后命中过期键盘方向，不能将全套real golden记PASS。
- THEME-01真实DOM 3×3：industrial-cyan/technology-blue/neutral-gray分别为原工业青/科技蓝/中性灰不同linear-gradient；light/dark/system均不把渐变覆盖成固定色，顶栏文字/按钮均白色可读，system当前有效light。管理→生产→管理保持neutral-gray/system/light和原四Tabs；生产模式9卡中八卡disabled+aria-disabled，第九Interface settings可用。结束恢复用户原neutral-gray/system偏好并关闭面板。
- 本地`theme.spec.ts`中入口、配色刷新持久化、顶栏渐变、明暗刷新、system跟随、密度、暗色bootstrap共7项PASS；200%用例两次超时仅因仍查找已删除的旧首页标题，不是主题切换失败。该过期定位及`visual-matrix.spec.ts`PC矩阵同样失去新版首页覆盖，已并入下方P1退回。
- 真实用户菜单：摘要`系统管理员 / admin · development`，四命令依次Profile/Clear cache/Lock workspace/Sign out，192设计宽（实测190.4内容宽）、每行36、13px、16px图标；顶栏无独立锁定icon。Profile真实显示账号admin、姓名系统管理员、租户development、角色SYSTEM_ADMIN；Change password进入现有真实修改密码页，未提交密码。缓存清理确认文案明确只清tabs/page/table；实际执行后Tabs仅Workspace并最终安全导航`/pc/home`，auth账号、tenant、en-US、neutral-gray、system、Management均保持，出现`Interface cache cleared`，无服务端debug/error请求日志。通知入口真实空态、Escape关闭并将焦点归还Notifications。
- **I18N-HOME-01 P1**：当前`html.lang=en-US`且Shell/导航英文，但清理后`/pc/home`整页固定中文（上午好、快速开始、服务与环境、最近登录审计、刷新及快捷卡）；`PcHomePage.vue`存在大量硬编码中文且未接现有locale。Profile为英文，进入`/change-password`又全部中文。该事实不依赖云端，违反用户要求的中英文/既有功能回归；同一根因使主题200%与12状态PC视觉门禁继续寻找已删除旧标题，形成假覆盖。已把源码位置、实际/期望、失效测试和最小i18n回归范围一次性发回唯一开发任务，并按首个P1停止扩大真实取证。5f2c6e6仍不能ACCEPT。

### CP2-R17：登录 i18n/窄屏与最终本地门禁（2026-09-01）

- 开发提交 `aff6462f6248c2313bc7b62e79456c42b64e704f` 完成登录页 `LocaleControl`、中英文资源、路由标题和 12 张 PC 当前 Shell 基线，`528dbcda92fdbb62c5f6b3992618a9ac7b959220` 将登录 E2E 提交按钮定位收敛到唯一 test id。独立门禁为 lint0、typecheck0、unit 93 files/697、build0/2456 modules、login-i18n 8/8、PC visual 12/12、theme 8/8、Shell Mock 24/24。
- 独立 390/360 明暗截图发现登录方法面板绝对定位覆盖用户名/密码/按钮，深色品牌几乎不可读，形成 `LOGIN-VISUAL-02`。退回唯一开发任务后，`7a6b846322c17ef06ff1ddaf89366efb34f2042c` 仅修改 LoginPage 与登录 E2E：窄屏方法面板进入正常文档流，页面可纵向滚动，深色品牌反白，语言图标保持可读。
- `7a6b846` 独立截图目录 `...\pf03-final-login-7a6b8463`；1280 dark 与 390/360 light/dark 均无覆盖，360 的 scrollHeight 835/viewport 800 且可完整滚动，console/pageerror 为零。真实 5173 无认证页面目录 `...\pf03-real5173-login-7a6b8463`；1280×720、360×800 dark English 的 title/lang/状态保持、无溢出和无失败请求均通过。
- 最终 source 状态 fresh backend Release build exit0/0 warnings/0 errors，full tests 1,364 passed/0 failed/3 skipped；frontend lint/typecheck/build exit0、unit93/697。后续只改二进制视觉快照，不改变上述编译/测试对象。

### CP2-R18：完整视觉矩阵闭环与最终真实环境处置（2026-09-01）

- 独立在 `7a6b846` 运行 `visual-matrix.spec.ts --grep UiBaseline`，12 个截图断言全部失败并稳定重试；代表 technology-blue dark compact 为 193,336 pixels / ratio 0.21。人工检查 actual 为当前新 Shell，expected 为旧品牌/旧顶栏/旧加载占位。退回后开发提交 `2c0ecd02d3876b60ebe0717ece76e3ca1390dc35`，精确只含 12 张 UiBaseline 快照。独立子集实际 13/13 passed。
- 独立随后运行完整矩阵，实际 49 项而非开发消息中的 48 项：25 passed / 24 failed，失败全部为 PDA 12 + Mobile 12，差异约 3%～5%。人工抽查 actual 保留任务 baseline 时已有的欢迎/日期时间/刷新/终端信息和底栏，expected 是更旧首页；baseline→final 的 PDA/Mobile page 源无修改，仅 MobileLayout 在批准品牌提交 `45af0b4` 有 3 行品牌资产切换，排除本轮擅改正常功能。
- 开发提交 `3db004e1fb821f379e4bffb946c28f1e60da06b1`，精确只含 24 张 PDA/Mobile 快照，未改生产代码、1% 阈值、PC 或 UiBaseline。独立最终 `pnpm exec playwright test tests/e2e/visual-matrix.spec.ts --reporter=dot`：49 passed / 0 failed，29.7s，exit0。测试生成的 `src/frontend/test-results` 已删除。
- 最终 commit 相对 origin/develop behind0/ahead53；baseline→final 53 commits、325 files、22,493+/1,159-。保护路径提交 diff、生成物提交 diff及index均为空；工作树仍保留用户既有 41 tracked unstaged/16 untracked，验收没有覆盖或提交。
- 最终真实 Chrome 仅有 `http://localhost:5173/login?redirect=/pc/home`：en-US、`Sign in · Industrial Platform`、2048×1090 无溢出；标签已 mark handoff 保留，未登录、未改偏好。浏览器日志记录 `/identity/api/v1/bootstrap/status` network warning。
- `netstat` 显示 `[::1]:5173` LISTENING PID62952，HTTP登录页200；`5041`无监听，`/health/ready`约4.2秒后连接被拒。本任务未启动/停止/重启用户服务。
- 因最终提交上无法执行真实管理员/无权限账号、Users OData 列头输入清空分页导出和在线会话 view/revoke/跨租户/幂等/会话失效安全矩阵，最新交接限定的唯一最终结论为 **REJECT**，不能沿用上轮 ACCEPT，也不能把安全/权限强制路径写成通过。
