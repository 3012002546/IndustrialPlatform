# E4：固定样式、动态字段查询表格接入

> 当前规则以 [2026-11-17 收尾记录](2026-11-17-Query收尾验证.md) 为准。下文为分阶段历史证据；其中当前页、200 行、保留公式导出及不上传快照等描述已被后续完整工作表方案替代。


2026-11-16，用户批准在当前工作树引入固定版本开源包，复用现有图表，禁止 Pro 及未批准的新直接依赖。本次保持未提交、未推送。E1～E3 独立模板实验仍按原证据保留。

## 已接入范围

- `src/frontend/src/components/management/AppQuerySpreadsheet.vue` 为可复用宿主，`querySpreadsheet/` 隔离 SDK 生命周期、数据转换、输出和本地化。
- 现有“工业应用 → 单报表 → 运行”中，已发布 `Query` 报表使用新组件；固定版式报表和既有服务器生成全部结果的链路保持原有职责。
- 样式固定，列根据字段定义完整生成，横向滚动。8/10 只是样本；没有按 8、10 或 100 列封顶。模型覆盖 8/10/30/60/120 列，真实浏览器覆盖 8/10/30/60 列。
- 字段编辑、公式、撤销/重做、当前页排序/筛选；XLSX 保留当前页全部行、隐藏行状态、筛选条件、公式及缓存值，也可选择仅结果值。打印为当前可见行的固定 A4 横向表格，重复表头；没有复杂模板上传、合并排版配置。
- 图表复用 `ChartRenderer.vue`，支持柱状、折线、饼图和分类合计/平均/计数。数据为捕获时编辑后的可见行，表格变化后点“更新图表”。共享柱/折线图修正负数坐标，饼图拒绝负数。
- 临时修改不自动保存到业务库、浏览器存储或服务器文件。刷新、翻页、离开确认后丢弃；组件销毁取消请求并释放引擎、事件、下载 URL。当前报告路由退出即销毁，不承诺跨工作区标签保留草稿。

## 组件接入契约

建议按需加载组件，避免每个业务页面预载表格引擎：

```ts
const AppQuerySpreadsheet = defineAsyncComponent(
  () => import('@/components/management/AppQuerySpreadsheet.vue'),
)
// columns: [{ field: 'orderNo', title: '工单号', width: 180 }, ...动态字段]
// loader({ pageIndex, pageSize, signal }) -> { items, total, pageIndex, pageSize }
// items 为 field 到标量值的映射；大整数编号由接口作为字符串返回。
// canExport 控制输出入口；authorizeOutput(signal) 在每次下载/打印前复核权限。
```

默认每页 200 行，可选 20/50/100/200；当前页字段定义及数据合计 2 MiB、查询超时 30 秒。超限或结构无效拒绝替换，保留旧结果；不会静默截断字段。图表最多 100 个分类，这不是字段数限制。此预算基于当前已验范围，不代表所有数据源、终端或任意字段规模的容量承诺。

平台使用既有查询 API，并传递 AbortSignal。新增 `GET /api/v1/studio/reports/{nId}/query-output-access?revisionNId=...`，复核有效身份、Run、已启用的固定修订、Query 类型及 Export 授权；运行投影新增 `canExport`。不上传临时表格，不创建正式运行记录。前端权限控制不能撤回此前已读取或下载的数据。

## 依赖与 CI

直接新增仅限：`@univerjs/core`、`@univerjs/presets`、`@univerjs/preset-sheets-core`、`@univerjs/preset-sheets-filter`、`@univerjs/preset-sheets-sort` **1.3.1**，`exceljs` **4.4.0**，`jszip` **3.10.2**。正常传递依赖由 `pnpm-lock.yaml` 锁定；没有新增图表库、Pro 或后端 NuGet 包。

`pnpm check:spreadsheet-dependencies` 已加入 frontend CI，在 frozen 安装后检查批准版本、禁止 Pro、核对已安装 28 个 Univer 包的版本及许可证元数据：icons 1.46.0/MIT，其余 1.3.1/Apache-2.0；ExcelJS MIT，JSZip 选择 MIT。`public/third-party/query-spreadsheet-NOTICES.txt` 保留这 30 个包的原始许可证/通知，并随构建发布。该检查针对本次包，不宣称整个应用的所有依赖已完成法律审计。

Univer 传递依赖 protobufjs 的可选 postinstall 仅打印版本范围提示，明确设置 `allowBuilds.protobufjs: false`；没有执行商业激活或添加许可证密钥。frozen 安装、版本/许可证检查、生产构建均成功。未推送，未声称远端 GitHub Actions 已运行。构建仍有大包警告：查询组件 JS 约 6.93 MB，gzip 1.77 MB；按需加载，首次进入需加载引擎。输出层另为约 1.03 MB，gzip 287 KB。

## 自测证据

原始日志、下载、截图、隔离数据库在忽略目录 `.run/oss-spreadsheet-poc/evidence/e4-*`；测试账号和 bootstrap 凭据不进入文档或 Git。

| 验证 | 结果/证据 |
| --- | --- |
| frozen 安装、包门禁 | 退出 0；`e4-frozen-install.log`、`e4-dependency-check.log` |
| 类型检查、生产构建 | 退出 0；`e4-typecheck-final.log`、`e4-frontend-build-final.log` |
| 全前端单元/组件测试 | 201 文件、1469 项通过；`e4-frontend-unit-final.log` |
| 仓库 Playwright 回归 | 8/10/30/60 列共 4 项通过；`e4-playwright.log` |
| 额外浏览器流程 | 四种列数编辑、下载、打印、主题/语言、保留/丢弃、拒绝输出、销毁通过；`e4-browser-result.json` |
| 60 列综合 | 降序后公式 `=B2*2` 得 398；筛除 67 行，图表 2 组、打印 133 行；12 次刷新和第二页 15 行通过；`e4-extra.log` |
| 独立 XLSX 解析 | openpyxl 检查全部列、777 编辑值、长编号、字面 `=1+2` 字符串；另验 60 列的公式、缓存 398、67 隐藏行及筛选条件 |
| 真实平台入口 | 真实登录 → 单报表列表 → 运行 → 查询/编辑/下载/图表；资源 Export 撤销后旧页面输出拒绝，刷新后入口隐藏；`e4-platform-result.json` |
| 后端权限回归 | 新测试覆盖功能权限、会话过期、修订/用户隔离、资源授权撤销，1/1 通过；`e4-output-auth-test.log` |
| Release 编译 | 新鲜全解决方案构建退出 0，0 警告/0 错误；`e4-backend-build-final.log` |
| 后端全量测试 | 最终退出 0，2291 通过、15 跳过、0 失败；`e4-backend-test-final.log` |

真实平台验收使用隔离 SQLite、合成 Constant 数据、产品登录和授权服务；测试宿主仅替换外部 Redis 撤销存储，不改用户业务数据库。客户数据源、生产 Redis、真实打印机/WPS、本轮固定查询组件的长期容量不在这些结果中。此前 E1～E3 模板原生 Excel 往返结果不能替代本轮全部客户端兼容验收。

首次后端全量测试保留失败记录：新增测试夹具缺少 query columnId（已修正并重建）及既有 Dashboard 测试清理 SQLite 文件时的锁竞争。重建后最终完整门禁退出 0；本轮未改写该 Dashboard 测试或全局 SQLite 清理逻辑，不把一次复跑通过当成已修复既有并发锁问题。

## 用户验证入口

按原有平台调试方式启动后端与前端，在“工业应用 → 单报表”选择已发布、已启用的 Query 报表点“运行”。任意动态字段定义均沿此路径生成列，不需要安装独立 PoC 或上传模板。固定模式报表不会切换为该组件；没有 Query 资源时，需先使用平台既有设计/发布能力配置查询数据及可见字段。

18:54 收尾：本轮验收前端 19482、隔离后端 19483 均已停止，19484 亦无监听；原独立样例 19382 既有服务保持运行，不接管、不停止。端口复核见 `e4-ports-released.json`。工作树代码供用户自行启动调试，未提交、未推送；测试数据库、凭据、node_modules、dist、bin/obj 未加入版本控制。
