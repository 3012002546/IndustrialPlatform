# 2026-10-21 PlatformStudio 实施11/12详细设计补齐

## 1. 接手事实与写入边界

工作目录 `D:\Code\Industrial Platform\IndustrialPlatform`。接手核对分支develop、HEAD `56f7e24ca40fac5ebcb8c7ba795f2b158739b9bd`；先读CURRENT及用户指定协作约定、ENGINEERING-NOTES相关条目、PF07末尾新会话接手要点。未恢复旧提交或旧概念版本。

接手时PF08/PF09主文、共享明细、前期评估为未跟踪设计文件；总Todo、蓝图18/32、README、STANDARD、CURRENT及PF07交接相关文件已有未提交修改，均保留。当前会话为设计写入者；只读任务状态显示`PF-07 调度健康 | 主控`空闲且最近交接已完成，交接说明明确未接管PF08/PF09设计。没有派遣、唤醒、停止或向其他任务发消息。

本轮只写实施11/12、四份新增规格、共享验收映射、本证据，以及必要索引状态。共享CURRENT/README/总Todo更新前核对SHA-256与已读版本一致，若变化则脚本终止，避免覆盖另一写入者；PF07、工程笔记、蓝图18/32内容没有由本轮重写。暂存/提交/推送、生产配置、业务源码和站点均未操作。

## 2. 新增具体开发依据

- [数据模型与生命周期](../implementation/details/PF08-PF09-数据模型与生命周期.md)：13张Studio表、通用基类/三Provider映射、PK/FK/索引/版本、字段说明及计划实体落点；台账采用固定记录+类型索引，报表/工作簿各自生命周期；初始化和旧账本升级。
- [接口与配置合同](../implementation/details/PF08-PF09-接口与配置合同.md)：资源/发布、来源、Dataset、台账/动作、看板/轮播、报告、追溯、工作簿；参数/绑定/布局/规则类型、成功失败样例、公开File/Scheduler增量和WOPI边界。
- [报表分页与追溯运行规则](../implementation/details/PF09-报表分页与追溯运行规则.md)：等宽带、可变层级、两类组合、长HTML拆分/完整性/留白判定；自建库存工单来源、双向事实边与模态框。
- [页面与验收规格](../implementation/details/PF08-PF09-页面与验收规格.md)：PageId/路由、管理与各设计器线框、滚动缩放侧栏、参数配置、B01～B34、候选验证及部署测量合同。

用户本轮补充“除通用建模，四部分逐块做好验收”，已按C0共用基础→C1低代码→C2看板→C3报表/Excel→C4追溯→C5集成写入主文/页面规格/总Todo。24个原TASK编号和九字段卡保留，每卡追加精确规格及文件/测试落点；不把PF09三部分只留整包一个验收结论。

## 3. 与当前代码核对的集成差异

| 实际输入 | 核对所得 / 设计处理 |
| --- | --- |
| SharedKernel Entity | 沿用冻结/锁定/软删/时间/双版本；技术不可变修订不机械继承可编辑状态 |
| SystemData FilePorts / FileContracts | 现有BusinessReference包含聊天会话/消息/附件标识；新增owned-reference公开端口和File自有引用表，不伪造聊天键、不直读FileStore |
| SchedulerContracts / SchedulerController | 已有IScheduledTaskHandler及Job/Run触发；新增一次性任务受理合同及job_kind/owner字段，仍沿PF07唯一引擎；不能把Handler存在写成跨进程执行已具备 |
| AuditContracts / ServiceInitializationManifestV2 | 统一审计白名单、一个宿主级可靠投递outbox；具名studio-core初始化单元、服务自有迁移和seed账本，不向控制面传秘密 |
| AppDataTable / AppQueryPanel / IdentityUsersPage | 使用已有row-click/activeRowKey/selectedRowKey及真实model更新契约，四产品独立菜单，原平台壳不锁1920尺寸 |
| 前端package.json | 当前Vue/Element Plus/VXE/Node/TS/Vite范围作为兼容输入；没有为候选降级或修改依赖 |

长版本串、Unknown活动槽、取消确认、旧账本、真实配置和来源新鲜度均写入具体表/接口/验收，不只在提醒段落提到。报告默认重试次数1；未知业务结果禁止自动重做。台账非兼容结构变更明确拒绝而不偷偷回填业务值；这是本轮实现边界决定，不声称已有自动数据迁移工具。

## 4. 设计完整度与尚未验证的内容

| D项 | 规格入口 / 当前结论 |
| --- | --- |
| D01范围 | 主文§1/2、页面规格§1；四部分/共同基础/现场与模拟界线明确 |
| D02数据 | 数据规格§2～4/8及接口规格配置Schema；字段、类型、默认、索引、来源/维护入口已写 |
| D03一致性 | 数据规格§5及接口规格§8；事务/CAS/幂等/取消/Unknown/引用恢复已写 |
| D04接口 | 接口规格§1～9；新增与复用分开，有路径/DTO/权限/错误/样例 |
| D05页面 | 页面规格§2～7；路由、线框、字段/动作、局部加载、窄屏和真实控件要求已写 |
| D06工程 | 数据规格§1/6/8、主文§10.1；计划工程/实体/测试落点明确。候选精确发行/真实适配仍有门禁，不能标全部准入 |
| D07验收 | A01～A16保留；B01～B34给可观察断言；页规格§8～9给PoC、兼容及测量输入/退出 |
| D08交付 | 原24卡/九字段保留；C1～C4分别验收，不派开发或改模型配置 |

本轮核查官方资料：ECharts registry返回6.1.0及Apache-2.0；interact.js仓库package为1.10.28/MIT；X6官方LICENSE为MIT；Paged.js官方描述HTML分页与headless输出；Collabora源码COPYING为MPL-2.0，WOPI PutFile官方定义锁/409/版本头。链接在页面规格§8。版本读取/源许可证**不是**完整发行包准入，未实际获取的版本、integrity、字体及转分发权不编造；部分官网/registry读取失败保留未核验。

当前没有运行第三方真实PoC、安装引擎或承诺商业授权。G02仍需主控锁包并验证长HTML、xlsx往返、离线部署和图编辑，按对应分部关闭；G03新增合同待实现验收；G04性能/目标环境待实测；G05本轮没有新增在线视觉/交互通过证据。原已确认概念继续作为输入，不把聊天参考中的许可/版本当事实。DevExpress及受限素材仍仅参考。

因此可以按明确规格准备/实施不受候选阻断的公共合同和低代码范围；**不能宣称四部分所有引擎已就绪、全部无前置可投产**。不把选型不确定甩给编码者，也不为通过验证删除首期Excel或动态HTML需求。

## 5. 本轮文档核验

核验脚本位于忽略目录`.run/pf08-pf09-doc-check.py`，检查本轮文档本地链接、JSON代码块、原TASK编号/九字段、任务落点覆盖、A/B/C编号和关键约束；`git diff --check`另验已跟踪diff。未修改生产源码，未重复PF07整解构建/测试；PF07历史通过/失败/未测继续按原范围保留，不重写为本轮通过。

实际执行：文档脚本exit 0，13份文档、184个本地文件引用、10个JSON代码块、24张TASK卡/216个必填卡字段、13张Studio表、A01～A16及B01～B34检查通过。首轮检查发现规范明细缺少真实`row-click`事件名，已补实际公共控件接线和model更新规则后复验通过。`git diff --check`单独执行exit 0；CRLF提示仅是仓库行尾转换提示，无空白错误。暂存区为空，`git diff --name-only -- src`为空，HEAD保持接手值。

人工一致性复核另外修正：PF08先应用资源/台账/审计三步，PF09只追加报告/工作簿，避免分部交付出现迁移编号倒序；Root/ParentRow/Node上下文与集合filter分别定义；组件registryVersion固定；WOPI原锁值加密存储以支持409冲突头，不能只存不可还原hash；关闭Office不应连带阻断LowCode；005正式输出显式依赖009产物基础。上述均为规格修正，未宣称对应生产实现通过。
