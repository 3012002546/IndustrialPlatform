# 蓝图与开发Todo一致性检查

日期：2026-09-07。性质：文档语义与派遣输入核对；不表示功能已开发或Gate已通过。

## 范围与结论

核对总Todo、蓝图04/05/06/20/32/33/34/35、实施08/09/09A/13B、对应四份details、索引、启动方案与派遣模板/工作包。本轮发现了实际语义不一致，已同步修正如下；没有以“以新版为准”保留相互冲突的规则。

PF05 → PF06 → PF06A → PF07/08/09/10/10A → PF10B → PF11 → 后续MES的编排保持一致。PF07等尚未建立完整细化规格的阶段仍待细化，不能据本记录宣称全部后续开发任务已就绪。

## 差异与统一含义

| 项目 | 原差异/风险 | 修正后与对应入口 |
| --- | --- | --- |
| 前置条件 | Todo、待派遣包绑定某分支、提交或旧文档修订，易随优化失效 | 总Todo、实施01/08/09/09A/13B、两模板与pending改为能力/契约/环境/授权前置；运行环境由总控管理，历史交付标识保留在证据中 |
| 初始化所有权 | 目录/部署蓝图要求每模块独立账本，和服务共享生命周期不一致 | 蓝图06/20/32/33统一默认服务级账本，真实独立生命周期才拆；PF06保留独立初始化边界，无持久化的Presence不建空账本 |
| SQLite选择 | RemoteDevelopment=false被描述为自动切SQLite | 蓝图20/33改为显式DevelopmentInfrastructureMode选择；不得自动切库或迁移正常调试数据 |
| 离线范围 | 三端蓝图要求离线优先，专项首版却明确不做完整离线业务 | 蓝图04包括末尾原则统一为按阶段设计；当前聊天联网补拉、标签仅执行账本/待回执，完整MES离线另行细化 |
| 共享与审计 | Screego看起来已采用、审计IP边界不同 | 蓝图05沿PF06 PoC后定路线，日志只保留允许元数据/脱敏摘要，和实施及细化规格一致 |
| 扫码契约 | keyboard-wedge/vendor-sdk与keyboard枚举不同，去重窗口可能吞掉连续同码 | 蓝图34/实施09A/details统一ScanResultV1、keyboard/broadcast/camera、eventNId/subjectEpoch；同事件重放去重，同值新事件仍接受 |
| 更新与核验时点 | 蓝图/主文承诺所有失败自动回退，插件选择拖到005 | 001先完成设备/签名/更新路线核验并回写规格；PDA Bundle有60秒健康窗，安装包/APK按系统规则恢复，可能人工介入；缺设计输入阻塞相关生产派遣 |
| 标签字段与能力 | 字段NId与JSON key、BusinessItemNId与ItemKey、SPP/BLE的协议层次不同；矩形遗漏 | 蓝图35/details统一稳定字段key、PreparedItemKey/BusinessItemRef、连接与输出协议分层；补Rectangle，首版转换函数使用明确白名单 |
| 打印状态 | Prepared/Leased/FailedBeforeSubmit混成一条状态链，Spooler可被误当物理完成 | 蓝图35按PreparedJob/PrintJob/PrintItem/Agent分表述；Ready/Claimed/SubmitIntent含义明确；SpoolerAccepted保持Submitted，全部明细可信确认才Succeeded |
| 准备快照与页面 | 蓝图承诺固定客户映射/打印配置，细化字段和准备API不完整 | 增加CustomerMappingRevisionNId、RenderProfileSnapshot/Hash；准备前选授权打印配置，服务端固定DPI/介质/格式；提交与发送前复核，变化必须重新准备，不静默重渲染 |
| 幂等与业务复核 | 原始请求与服务端解析快照混算容易让配置变化破坏重试 | RequestHash只比原请求，SnapshotHash包含冻结版本/配置/明细；来源撤销/变化及不可用发送前阻断，SubmitIntent后未知结果先对账 |
| 种子类别 | seed_ledger.scope混入EnvironmentSample/SecretBootstrap | PF05细化规格按实际SeedScope=System/Tenant；四种SeedClass在种子清单独立声明，PF06/Label复用相同语义 |

## 维护规则

[共同细化规则](../implementation/STANDARD-派遣前详细设计与页面验收.md)要求同一设计变化同步蓝图摘要、实施方案、字段/API/页面规格及验收映射。Todo引用文件和章节，不固定Git状态或依赖文档修订号。文档自己的修订记录以及业务schemaVersion、数据修订、并发版本、迁移版本/checksum、安装包/bridge兼容版本继续保留，各自有实际用途。

派遣状态仍为待派遣；G05-1～3、G06-1～2、G06A-1、G10B-1～3的真实契约、PoC、客户/设备和独立适配缺口没有因修改文字而关闭。页面先线框、字段契约先明确、整包派遣与独立验收规则继续有效。

## 验证

临时只读校验结果：30份文档、157个本地Markdown链接、4个JSON样例、34张九字段任务卡、14个线框编号，错误0；围栏完整，git diff --check通过。当前PF05起实施/细化/待派遣输入及索引的旧代码钉住条件、旧扫码/更新/种子scope冲突措辞扫描无命中。语义核对结合SystemData实际SeedClass/SeedScope和现有本地数据库配置说明。只修改文档，没有执行业务构建、测试、实机核验或派遣。
