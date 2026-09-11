# PF05 PDA 扫码回车防误发

2026-09-11 主控直接小修。用户本轮只要求此项与附件/保全/导出/审计联动收尾，其他剩余 PF05 验收明确暂缓。

## 实际修改

- 共享 CollaborationChat 在 `terminal === 'pda'` 时不将 Enter 转成发送，不拦截原生输入框换行，不注册全局扫码监听。发送仍使用现有显式按钮；PC/Mobile 的 Enter 发送、Shift+Enter换行及输入法组合保护保持原逻辑。
- PDA 中英文输入提示改为回车换行、点击发送。使用原终端包装页传入的 terminal 属性，不按屏幕尺寸误判 PC 为 PDA。
- 类型检查发现既有管理员“生成导出”文案 `generateExport` 缺失类型声明，已补齐，与本轮导出收尾范围一致。
- 组件测试补充 PDA 模拟扫码 Enter 不调用发送且不 preventDefault、草稿保留及按钮发送；PC/Mobile 原快捷键回归。修正既有测试传入可选 terminal=undefined 和可选实时回调的严格类型问题。旧撤回滚动测试直接赋 scrollTop 却没触发实际滚动事件，会与异步滚动恢复竞争；改为模拟用户滚动事件，不改生产滚动逻辑、不放宽断言。

## 验证

- `node node_modules/vue-tsc/bin/vue-tsc.js --build`：退出0。
- 修改的组件、测试、localization/types.ts及中英文locale的定向 ESLint：退出0；Prettier通过。
- `node node_modules/vitest/vitest.mjs run tests/components/CollaborationChat.spec.ts`：25/25通过。
- `node node_modules/vitest/vitest.mjs run tests/components/CompliancePage.spec.ts`：5/5通过。
- 定向 `git diff --check`：退出0，仅换行提示。
- 首次组件回归24通过/1失败（旧撤回滚动测试模拟不足），修正模拟后25/25；首次类型检查的遗漏如上列明，不将首次结果写成通过。
- pnpm命令包装未找到本地工具；使用已安装包的Node入口。沙箱初次拒绝本地文件/缓存写入，经工具系统审批后完成检查。未安装依赖、未改调试/云Docker/连接配置、未改后端代码、未提交推送。

本轮是组件键盘事件验证，不冒充实体扫码枪现场测试。用户此前已确认的导航/界面验收保留，不要求重复。

联动工作由原开发任务接续，生产代码和文件仍为未提交工作树；此文件只关闭本项小修，不宣称PF05整包PASS。
