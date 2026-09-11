# PF05 聊天折叠与窄屏布局小修

- 根因：AppQueryPanel 是受控组件，聊天页将 collapsed 写死为 false，未处理 update:collapsed，所以收起按钮无效。改为局部响应式 v-model，只折叠筛选区，保留标题、展开按钮、会话列表与当前聊天。
- <= 640px 改为上下单列区域，限制会话区高度，两区独立滚动；初次打开窄屏默认折叠筛选区。聊天头部、操作按钮可换行，长姓名允许换行，详情浮层限定在聊天区域。顶部弹窗不再受手机页面的 min-height 覆盖。
- 只修改共享 CollaborationChat 和它的组件回归，不改通用查询组件、服务端或数据库。

## 验证

- 页面和顶部弹窗各执行连续三次收起/展开，检查真实 AppQueryPanel 的 aria-expanded、展开入口可见、筛选容器可见、筛选值和会话保留。
- 窄屏 matchMedia 场景验证默认收起及展开可达。
- 聊天、PDA 首页、Mobile 首页：最终 3 文件 41 / 41 通过；vue-tsc 和修改文件 ESLint 通过。
- 新增可见性用例最初在脱离 document 的挂载下出现展开后 isVisible 假阴性，改为 attachTo document.body 后反复切换通过；测试环境缺少 matchMedia，新增窄屏用例显式 stub 并在 afterEach 清理。
- 浏览器控制调用 getState 失败（Browsers: nodeRepl.fetch request failed），无可操作浏览器。因此尚未进行当前调试实例的真实屏幕尺寸视觉验收，不以组件测试代替视觉结论。
- 未重启用户调试、改动云 Docker、本地连接配置或提交代码。
