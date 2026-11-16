# 生产工作流测试样例

本样例用合成数据验证追溯所用公共 X6 图形工作台在生产流程中的复用。流程定义包含领料、称量、加工与检验并行、汇合和放行。运行状态来自本地测试快照，同一检验节点可以对应多条执行记录。

## 打开与重建

双击工作区中的 `visual-previews/production-workflow/index.html`，可在 Edge 或 Chrome 本地打开。脚本与样式都在这一个文件中，可离线使用。

重建时使用项目规定的 Node.js 版本以及 `src/frontend` 已安装的锁定依赖。在仓库根目录执行：

```powershell
node examples/production-workflow/build.mjs
```

默认产物位于仓库上一级的 `visual-previews/production-workflow/index.html`。也可指定输出文件，例如 `node examples/production-workflow/build.mjs D:/Temp/workflow-sample.html`。样例通过单独的 Vite 构建入口直接引用公共图形组件，产品构建与系统菜单均不引用本目录。

## 体验与复验

1. 在「运行回放」选中检验节点，分别查看第一次失败和第二次检验的记录。将快照从 00:06 切到 00:08，观察第二次检验、汇合和放行的状态。
2. 切到「定义草稿」，修改检验活动的名称和位置后应用。一次撤销与一次重做应分别恢复整组旧值和新值。
3. 保存草稿，再改一次名称，然后重开草稿，核对保存的名称和位置。切回运行回放，应看到发布修订对应的原检验节点。

已安装 Edge 的开发机可从仓库根目录运行 `node examples/production-workflow/verify.cjs`，使用离线浏览器检查上述操作，结果和截图写入忽略目录 `.run/pf09-trace/workflow-sample-checks/`。
