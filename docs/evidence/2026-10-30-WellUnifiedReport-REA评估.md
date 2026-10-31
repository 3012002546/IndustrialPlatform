# WellUnifiedReport：XtraReport / Spreadsheet REA 评估

日期：2026-10-30。范围：用户指定的 `Z:\WellUnifiedReport\`。用途：为 Industrial Platform 报表和 Excel 的功能借鉴提供证据，不是产品实现完成声明。

后续：[第二轮评估](2026-10-30-WellUnifiedReport-REA第二轮.md) 补充完整预览工具栏、十类导出、查询结果型 Spreadsheet 及高级控件；Spreadsheet 的目标用途以第二轮用户澄清为准。

## 1. 结论与证据边界

可以借鉴，优先提取项目实际使用的功能和业务语义。目录同时提供源码与部署程序集，因此采用 REA 静态托管程序集检查、CIL 调用关系核对、针对性源码阅读和模板 XML 统计；没有执行目标应用或连接其业务数据库，没有改动目标目录。

- 项目是 .NET 5 ASP.NET Core 应用；项目包引用与两个已检查的部署控件程序集均为 DevExpress 21.1.12 系列。
- 报表的重要业务能力是模板管理、数据绑定，以及按批次/工单/工序条件组合子报告。设计、排版和预览由 DevExpress 控件承载。
- Spreadsheet 是控件接入、OP 文档读取与示例按钮的组合。其 Save 路径没有完成业务持久化；部署 DLL 的两个 `DocumentDatabaseMock.SaveDocument` 方法也均只有一条 `ret`，不是仅源码中的遗留注释。
- 本次未验证运行界面、打印效果、复杂分页、公式兼容性和生产授权链；源码与部署程序集仅核对了明确列出的范围，不能推断全量一致。
- 本次仅增加评估材料，不引入 DevExpress DLL、脚本、模板或实现代码到平台，也不作许可证兼容性结论。

## 2. XtraReport 的实际链路

```text
设计器 → 选择数据源/绑定字段/编辑布局 → ReportStorage 保存 XML 布局
预览请求 → 模板与参数解析 → 获取工单/批次/工序数据
        → 选择子模板、注入参数、创建 XRSubreport → 母报告 → Viewer 渲染
```

下列源码定位均以 `Z:\WellUnifiedReport\WellUnifiedReport\` 为根。

| 功能 | 观察与定位 | 平台借鉴方式 |
| --- | --- | --- |
| 在线设计 | `Views/Home/Designer.cshtml`：ReportDesigner、全窗口布局、中文消息、参数编辑器 | 延续 Studio 设计器，完善工具栏、字段绑定、属性编辑、试运行入口 |
| 数据源 | `Startup.cs` 注册 SQL / JSON provider 和 ObjectDataSource 类型提供器；HomeController 的设计入口还注入 Northwind 示例 | 用平台受控数据查询与 Schema；不要把示例数据库或任意连接配置作为生产默认 |
| 模板列表 | `Services/Sys/CustomReportStorageWebExtension.cs:42`：数据库列表含类型、编码、名称、版本，并合并本地 XML 文件 | 独立模板身份、修订、来源和发布状态；显示名称不充当结构化协议 |
| 模板保存 | 同文件 `:1279`：SaveLayoutToXml，写 Reportlayout 字段，更新或插入数据库，同时保存本地布局文件 | 可借鉴可编辑模板持久化；代码中的版本号不等于不可变发布版本，仍需平台生命周期 |
| 参数化运行 | 同文件 `:214` 起 GetDataAsync：解析 ReportPreviewModel，选择模板并设置数据源参数 | 统一参数定义、校验、运行上下文与诊断 |
| 动态子报告 | 同方法内按批次查关联工单、按工序排序，依配置/检查标志选择模板，构造 XRSubreport 加到 DetailBand | 复用平台 AssemblyPlan，以规则描述顺序、条件、参数映射与空数据处理 |
| 扩展点 | `ObjectBinding/ObjectDataFactory/ReportFactory.cs` 与 BatchRecordFactory：反射发现服务，按 Key 子串匹配 | 可借鉴业务处理器扩展点；平台使用明确标识与注册，不照搬子串路由 |
| 预览 | HomeController 的 Viewer / DocumentViewer 与对应视图，调用 DevExpress Viewer 模型生成器 | 保持设计试运行与正式生成的取数、展开和分页算法一致 |

需要准确理解：项目组装的是**报表对象及子报告**，不是先生成多份 PDF 再拼整页。源码中的 `BoundsF` 和递增位置只是构造参数；最终伸展、分页效果仍须运行样本才能确认。

### 本地模板的实际使用范围

统计 `Data/ReportLayout/*.xml` 的 14 份文件，仅代表本地副本，不代表数据库内全部模板。

| 控件/结构 | XML 节点数 |
| --- | ---: |
| XRTable / XRTableRow / XRTableCell | 31 / 53 / 350 |
| DetailBand / DetailReportBand | 25 / 11 |
| PageHeaderBand / GroupHeaderBand | 10 / 8 |
| XRLabel / XRLine / XRPageInfo | 21 / 17 / 15 |
| XRPictureBox | 1 |

11 份模板含 ExpressionBindings，11 份显式引用 DataSource，8 份含 DataMember；14 份显式设 PaperKind，11 份有 Landscape 属性。属性存在不代表取值为 true。未显式序列化某属性也不代表能力不存在，可能使用默认值。

因此第一轮功能对齐应聚焦：表格单元格与边框、字段表达式、等宽主从明细、分组标题、页眉页脚、页码、纸张方向、子报告展开。不能仅凭控件产品名称推定图表、条码、复杂脚本等都已被该项目实际使用。

### 不应照搬的实现细节

- CanSetData 与 IsValidUrl 均直接返回 true（同文件 `:61`、`:71`）；这些钩子不能成为平台的权限或输入校验实现。
- 保存过程先写本地文件再写数据库，不能据此认定两份存储具备原子一致性。
- 条件组装存在具体报表 ID、工序类型及特殊灌装分支；应提炼为受控业务规则，不放入通用报表组件。
- 模板找不到时部分分支直接跳过；平台应区分条件跳过、数据为空、必需模板缺失和取数失败，并保留运行诊断。

## 3. Spreadsheet 的实际链路

```text
业务文档 NId → 查询 OP Document / FileValueUrl → 文件字节
            → Spreadsheet 编辑会话 → Xlsx / 当前 Sheet HTML / 打印
            → SaveToBytes → DocumentDatabaseMock.SaveDocument（空方法）
```

| 行为 | 源码定位 | 判定 |
| --- | --- | --- |
| 打开业务工作簿 | `Controllers/HomeController.cs:88`，OP 查询在 `:102` 起 | 已有读取接入；不传 doc 打开 EmptyDocument 示例 |
| Ribbon 和操作入口 | `Views/Home/Spreadsheet.cshtml:99`，按钮 `:108` 起 | Save / Xlsx / Html / Print / Edit；主体编辑能力由控件提供 |
| 编辑会话请求 | `HomeController.cs:114` | SpreadsheetRequestProcessor 处理客户端状态 |
| Save | `HomeController.cs:140` → `Utils/DocumentDatabaseMock.cs:23`、`:28` | 两个保存重载均为空；REA 部署证据亦确认 |
| 自动保存 | `Startup.cs:66` | 仅设置 e.Handled=true，没有业务写入 |
| Edit document | `HomeController.cs:123` | 只把活动表 AB9 设为 0.155，是服务器改格子示例 |
| Xlsx 导出 | `HomeController.cs:160` | 导出当前会话的整个工作簿副本 |
| HTML 导出 | `HomeController.cs:171` | 只导出当前活动 Sheet，UTF-8、内嵌图片、Style CSS |
| 打印 | `Spreadsheet.cshtml:56` | 调用客户端 print；分页结果未实测 |
| 休眠 | `Startup.cs:59` | 20 分钟休眠、1 天释放配置；属于控件会话机制，不是业务版本存储 |

视图 `:144` 关闭未保存退出确认。SaveToFile 方法虽然调用控件 Save，但当前页面按钮没有指向它；SaveToStream 仍进入空 mock。不能用方法存在证明保存链闭合。

所检查接入路径未见明确的业务版本/CAS、文档锁、OP 文件回写和权限撤回实现；未检查外围部署，不据此断言整个系统没有鉴权。

## 4. 与平台现状的衔接建议

平台已有对应基础，不需要重新另建一套报表和 Excel 子系统：

- `docs/implementation/details/PF09-报表分页与追溯运行规则.md` 已规定 DataResolver、AssemblyPlanner、BandExpander、统一内容流分页与运行诊断；对照本项目的工单/工序组装补充真实场景样例。
- 平台现有 `ReportPlanning.cs`、`ReportCommandRenderer.cs` 及 Studio 报表设计组件是承接位置。继续按既有分页方案实现、验收，不把原项目控件的排版结果默认归给平台。
- `StudioWopiEndpoints.cs` 已有 CheckFileInfo、GetFile、Lock、PutFile、版本响应与冲突恢复文件返回路径；Excel 保留现有 Workbook/WOPI 底座，在其上完善业务入口与操作语义。
- CURRENT 中仍记录复杂布局、Office 真实服务往返等未关闭项；接口存在不等于端到端验收通过。本次静态分析不关闭这些门禁。

建议顺序（待实施功能的评估建议，不表示本次已编码）：

1. **报表基础编辑和预览**：以一份真实表格模板为功能样例，对齐表格编辑、字段绑定、主从带、页眉页脚与页码。
2. **业务报告组合**：选择一份批记录样例，把工单/工序条件、子模板参数和顺序转成平台组装规则；记录每个包含/跳过/失败原因。
3. **复杂内容分页**：长表格单元格、跨页重复表头、无数据子报告、不同长度明细、子报告连续排版；核对设计预览与正式 PDF。
4. **Excel 完整往返**：业务文档打开 → 编辑 → 保存版本 → 关闭重开；再核对另存、导出整个工作簿、导出当前 Sheet、打印、只读和冲突恢复。

可复用的边界应是报表模板/运行/组合与 Workbook 文档/会话/版本服务。报表设计、流程画布和 Excel 可共用平台文件、权限、数据引用和审计服务，但各自保留适合其任务的编辑模型，不把 X6 画布强行作为报表排版器或表格计算引擎。

## 5. 最小验收样例

- 报表：一对多明细、分组、多工序可选子报告、空数据、中文长文本、超过一页的单元格、页码和表头；每例保留输入、模板修订、预期顺序和实际 PDF。
- Excel：多 Sheet、公式、样式、合并、图片、打印区域；保存重开后比较值、公式与格式，确认不会仅保存表面显示值。
- 生命周期：两个编辑会话冲突、断线恢复、保存失败、权限撤回、原始报表产物与工作副本分离。
- 不把截图相似、按钮可点击或 HTTP 200 单独作为验收依据。本次没有执行这些运行验收。

## 6. REA 可追溯身份

| 部署文件 | SHA-256 | MVID |
| --- | --- | --- |
| WellUnifiedReport.dll | `9c18c0d7cb9b7d5fc757b18dbe2334983d8b169403285ba3b6b56542a60597b2` | `699fed4f-d18a-4f2c-9a24-d5bac1b1affe` |
| DevExpress.XtraReports.v21.1.dll | `8f4e594c84a9db7ed60d777768c784abec1e4814d383ccb0b490932c496c89ff` | `466f6176-636c-451e-9ef2-60c95e7631c4` |
| DevExpress.AspNetCore.Spreadsheet.v21.1.dll | `77822e2994bffc376d1a57015a5b86bb2a8fc30d8bb53da23fb501c9e66070aa` | `b9f9629f-ca6e-40bb-9487-d65b225edb8f` |

关键父证据：

- 应用托管身份：`ev_bb08de785f830f4f404cc1eef7d3176e39a9ef9d0a00a97d156366396c361518`。
- HomeController / 报表存储类成员与调用：`ev_1328c50c972fee1c425dab2ff218b022294fb9660b28b90cc105711c7ca02ac3`。只涵盖选定声明类型，未自动包含异步嵌套状态机。
- DocumentDatabaseMock：`ev_bde61b5dc8c43b57fd27e303d1fcf4541cddab0df08a35ff9d989ebe95c7e9e6`。方法 token `0x060000ef`、`0x060000f0` 均为 1 字节 IL、1 条 ret；token 仅对上表该 SHA/MVID 有意义。
- XtraReports / Spreadsheet 托管身份：`ev_c5952d9f64a7c9e0a3c8a38bdf6b2747871bf361887bf1fbb57b3440c41c0006`、`ev_9b991d27ca83779f198da7896f50aa5fd0b93cb9eeff9f9c2783367bc5aab9a9`。

根目录另有同名 CustomReportStorageWebExtension.cs；本报告源码定位使用项目子目录实际文件，不把两份文件混作同一版本。未复制原项目源码、业务模板或业务文档进入本仓库。
