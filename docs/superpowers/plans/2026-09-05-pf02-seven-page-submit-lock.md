# PF-02 七页写操作防重复提交整改计划

> 状态：2026-09-05 已完成只读代码盘点，待原功能开发任务实施；原独立验收任务仅在收到稳定交接后开始验收。

**Goal:** 修复 SystemData 七个管理页面在保存或其他写操作进行中仍可重复触发的问题，保证一次用户动作最多发出一次写请求，同时保留失败后的明确反馈和可重试能力。

**Architecture:** 页面级加载遮罩继续只覆盖 `SystemDataAdminFrame` 内容区；Teleport 到 `body` 的 `AppFormDrawer` 保持位于页面遮罩之上。防重复提交由当前业务操作面的本地写入状态负责，不通过提高遮罩层层级、增加第二套全屏遮罩或修改 `AppDataTable` 实现。共享抽屉继续消费 `busy`，自定义 footer 必须显式消费同一写入状态；所有异步写入口在第一次 `await` 之前同步加锁并在 `finally` 释放。

**Tech Stack:** Vue 3、TypeScript、Pinia、Element Plus、既有 `AppFormDrawer` / `SystemDataAdminFrame` / `AppDataTable`、Vitest 与 Playwright。

## 1. 已确认的问题与基线

- `SystemDataAdminFrame.vue` 的 loading 状态位于页面内容区，`z-index: 2`；`AppFormDrawer.vue` Teleport 到 `body`，使用 `--ip-z-drawer`。抽屉高于页面遮罩是既有正确层级，不应以 z-index 反转修复。
- `AppFormDrawer` 默认提交按钮在 `busy=true` 时会禁用，但它只能在父页面把正确的写入状态传入后生效；当前七页普遍直接使用全局 `store.loading`，缺少业务提交函数入口级的同步 single-flight 锁。
- `managementStore.run()` 只有调用方显式传入 `skipWhenBusy=true` 才短路；大多数组织、任职、菜单、功能、服务、主题写方法未启用该保护。全局 `loading` 同时承担读取和写入，不适合作为每个表单的唯一写入生命周期。
- 菜单页已确认两个直接漏点：默认菜单导入预览和节点编辑均使用自定义 footer；其中节点保存按钮未绑定 `busy/loading/disabled`，绕开了 `AppFormDrawer` 默认 footer 的防重逻辑。发布、回滚、启停等确认后写操作也没有统一入口锁。
- 管理黄金样板 `IdentityUsersPage.vue` 使用操作面本地 `dialogSaving` / `rolesSaving` / `passwordSaving`，并将其绑定到当前抽屉或按钮；SystemData 七页应沿用这一模式。

## 2. 七页整改矩阵

| 页面 | 必须纳入同一规范的写操作 | 当前主要缺口 | 实施要求 |
| --- | --- | --- | --- |
| 行政组织与岗位 | 新建/编辑组织、岗位；组织/岗位启停；移动确认 | 抽屉仅依赖全局 loading，确认后写入口无统一锁 | 表单本地 saving；状态/移动分别防重复；切换选择不覆盖在途表单状态 |
| 用户任职 | 新建任职；结束、取消、设主任职 | 表单和确认动作只依赖全局 loading | 新建表单本地 saving；行操作按当前写入状态禁用，同一动作只发一次 |
| 菜单管理 | 节点新增/编辑/权限关联；默认导入确认；启停/恢复；发布/回滚 | 自定义 footer 绕过 busy；发布/回滚等保护不一致 | 编辑和导入分别使用本地 saving；自定义按钮显示 loading 并禁用；发布、回滚、节点状态写入 single-flight |
| 功能开关 | 保存三态覆盖与原因 | 抽屉只依赖全局 loading | 本地 saving 在校验前同步加锁，失败保留输入并释放后可重试 |
| 服务目录 | 新建/编辑外部服务；服务启停 | 抽屉与确认动作只依赖全局 loading | 表单本地 saving；状态写入防重复；不改变 HTTPS、所有者和权限规则 |
| 租户主题策略 | 保存主题策略 | 页头按钮依赖下一次渲染的全局 loading | 独立 themeSaving；按钮 loading/disabled；校验失败不发请求，409/失败后保留草稿 |
| 服务初始化编排 | 注册、创建计划、审批、备份登记/验证、Apply、Cancel | 只有部分 store 方法和内联按钮有忙碌短路，注册等不一致 | 各高风险动作使用明确写入锁；保留 Idempotency-Key、审批/备份门禁及确认，不用 UI 锁替代服务端幂等 |

## 3. 统一交互契约

1. 每个写处理函数第一行先判断当前操作是否在途；未在途则在任何异步校验、确认结果处理或 API 调用之前同步置为在途。
2. 当前操作面的保存/确认按钮立即进入 loading 且不可再次触发；关联的行级写按钮在同一写操作完成前不可再次触发。只读内容保持可见，不用新的整页遮罩盖住抽屉。
3. 成功时只关闭一次当前操作面、只刷新一次对应数据，并以服务端返回/重载数据为准；不能因重复回调产生 409、revision changed 或重复记录。
4. 失败时保留抽屉和用户输入，显示现有本地化错误；`finally` 释放锁后允许用户明确重试。校验不通过不得发请求。
5. 自定义 `#footer` 不享受共享组件默认按钮的 busy 行为，必须显式绑定同一 saving 状态；禁止再出现只给外层 `AppFormDrawer` 传 busy、footer 内按钮仍可点击的情况。
6. 页面卸载、切换节点或关闭操作面后，在途响应不得写入另一条记录的表单状态。是否在 busy 时禁止关闭遵循黄金页现有行为；无论是否关闭，都必须保证旧响应不能造成第二次提交或污染新表单。
7. 不修改 API 权限、资源校验、乐观并发、菜单 revision、初始化幂等语义或数据库数据；UI 防重是体验和客户端保护，不替代后端安全门禁。

## 4. 实施文件与边界

- 主范围：`src/frontend/src/components/systemData/*AdminPage.vue`、必要的 `src/frontend/src/stores/systemData/managementStore.ts`、对应 SystemData locale 与测试。
- `AppFormDrawer.vue` 仅在证明确有共享契约缺口时做向后兼容的最小修改，并必须跑 `AppFormDrawer.spec.ts`、`IdentityUsersPage.spec.ts` 及现有管理页回归。优先使用现有 `busy` 契约，不新建平行弹窗组件。
- `AppDataTable.vue`、用户管理黄金页、SystemData 后端、权限与持久化默认不修改；若实证要求后端补幂等，只能作为窄修复并保留既有协议。
- 保留当前 `develop`、用户未提交文件、正常数据、VS/VS Code/Vite/dotnet/数据库和调试端口；不重启或接管用户进程，不提交、不推送。

## 5. 测试与验收

- 先使用 deferred Promise 的 API mock 重现：连续点击保存/确认至少 3 次，在 Promise resolve/reject 前写 API 调用数始终为 1。
- 七页逐页覆盖至少一个主要保存入口；菜单额外覆盖节点自定义 footer、默认导入确认、发布；初始化额外覆盖创建计划与 Apply。
- 断言在途时按钮可见 loading/禁用状态，失败后操作面仍打开、输入仍在、锁已释放，随后一次重试只新增一次请求；成功只关闭/刷新一次。
- 校验失败为 0 次写请求；确认框取消为 0 次写请求；权限不足不出现可写入口。
- 定向执行 SystemData component/store 测试、`AppFormDrawer.spec.ts`、`IdentityUsersPage.spec.ts`，再执行 typecheck、lint、变更文件格式检查和前端 build。
- 浏览器验收使用真实 VXE/Teleport 渲染，至少覆盖中文 PC 的菜单编辑现场，并补英文、亮/暗主题及 1280/1440 关键视口；记录 Network 中一次操作只有一个写请求。不得用页面遮罩“看起来出现了”代替请求次数断言。

## 6. 任务顺序

1. 原功能开发任务按本计划实现、定向测试并停止编辑。
2. 开发任务以执行标识 `PF02-20260905-七页写操作防重` 直接向原独立验收任务发送稳定交接。
3. 原独立验收任务只读复核同一工作区，独立重跑测试与浏览器请求次数检查；发现问题集中退回原开发任务，不与开发并行编辑。
4. 两任务保持创建时模型和思考强度，后续消息不传 `model`、`thinking`；不创建新任务，不向用户询问常规实现或调试确认。
