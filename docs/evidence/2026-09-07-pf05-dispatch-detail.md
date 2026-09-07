# 2026-09-07 PF05起派遣规格细化记录

本记录对应用户追加要求：开发TodoList细到表名/字段/类型，任务细化先于派遣，派遣状态明确为待派遣，页面先画线框图，补齐黄金页面、yefeng布局、本地验收与协作要求。只维护文档，不创建或发送外部任务，不实现生产代码。

## 1. 交付入口

| 交付 | 位置与内容 |
| --- | --- |
| 全阶段共同规则 | [派遣前详细设计与页面验收](../implementation/STANDARD-派遣前详细设计与页面验收.md)：D01～D08、两个状态维度、布局/选择/加载/菜单/真实验收/协作，以及PF07起尚未设计阶段的必补清单 |
| PF05规格 | [字段、接口与线框](../implementation/details/PF05-数据接口与页面规格.md)：消息/附件/合规、账本/Outbox/幂等、请求响应/样例、4个PageId和8张任务映射 |
| PF06规格 | [字段、接口与线框](../implementation/details/PF06-数据接口与页面规格.md)：Session/Invitation/Grant/Ticket/Lease/观察、个人邀请/票据恢复、2个PageId和8张任务映射 |
| PF06A规格 | [运行时字段与线框](../implementation/details/PF06A-运行时字段与页面规格.md)：无SQL库，列清配置/清单/状态JSON和TS桥接、更新恢复、2个PageId和8张任务映射 |
| PF10B规格 | [字段、接口与线框](../implementation/details/PF10B-数据接口与页面规格.md)：模板/数据规则/准备快照/打印明细/执行事实/设备/Agent账本、6个PageId和10张任务映射 |
| 待派遣包 | [待派遣索引](../tasks/pending/README.md)：四份排队草案、明确Gate、无虚构工作线/执行者或实际派遣 |

实施08/09升级V1.2，09A/13B升级V1.1；34张九字段任务卡均引用细化规格精确章节，执行记录同步待派遣。共同要求进入实施模板、工作包模板、AGENTS、EXECUTOR、实施索引、总Todo、CURRENT和README末尾。未修改本地CLAUDE.md或现有active工作包。

## 2. 额外降低返工的细节

- 数据字典明确.NET/PG/SQLite/JSON类型、可空/默认/长度/精度、领域公共字段与技术记录差异、索引支撑查询、普通FK/跨域引用和迁移版本。
- 序号/版本/decimal不在JS number中丢精度；参与人排序对齐应用/PG/SQLite；状态/幂等/锁顺序/回滚/消息清理后重放防护写到规则。
- API有请求/响应字段、分页和上限、权限/错误/样例；非空JSON明确schema和大小约束，不能藏成“扩展JSON后补”。
- 每页先画线框：空间、数据、动作、选择、加载与窄屏；同模板复用图，逐路由登记差异。主从清空/快速切换、表单重复点击、IME/扫码冲突、消息/媒体/画布不套CRUD分别处理。
- PoC/硬件核验与生产范围分开；现实中不存在的输入列为Gate，有责任方与退出条件。已能明确的规格本轮完成，不把“编码时决定”保留在生产任务中。

## 3. 代码/规则核验发现

读取用户指定的管理页面Skill及两个reference全文、任务协作Skill，核对当前AGENTS/CLAUDE/EXECUTOR/模板；以用户要求和当前仓库规则为准，不创建额外任务或并行修改工作线。

实际只读核对IdentityUsersPage、AppQueryPanel/AppFormDrawer/AppDataTable及相关组件测试/locale；AppDataTable当前有row-click和active/selected key，与旧技能快照“尚无整行点击”不同，规则已明确按当前公共能力实现。selection-change未合并reserves，不能直接许诺跨页批量写入。黄金样板只作为交互布局依据，不复制账号业务。

读取Entity.cs确认Id/ConcurrencyVersion为Guid、OptimisticVersion从0开始、业务时间DateTimeOffset；读取ReferenceData迁移/初始化/Outbox和SystemData Runner账本发现它们有不同用途，不能当同一通用类型。新服务字典明确采用服务自有初始化模式，Outbox复用模式但不直读/修改ReferenceData具体表。

读取FileContracts/FileService确认FileObjectV1无状态版本，OpenRead按Owner或用途Reference.Owner校验；同租户并不自动授予聊天另一成员下载。已将实际消费差异写为G05-2，未虚构安全代理已存在。读取AuditFactIngestRequest/AuditFactV1和Identity UserSummary，明确最小元数据映射、不可将管理用户DTO全量直接下发聊天目录。

本地配置文档要求显式`IndustrialPlatform__DevelopmentInfrastructureMode=Sqlite`；PF05旧“配置缺失/关闭RemoteDevelopment自动回退”已修正。没有读取或输出私有凭据。

## 4. 就绪状态与限制

待派遣是分派状态；设计就绪度单列待前置核验。PF05尚有Identity/外部参考宿主、File成员授权/保全、Audit/提权消费门禁；PF06仍有双PoC生产路线/媒体证据；PF06A缺实际设备/SDK/签名分发；PF10B缺客户/设备/独立身份存储适配。相关范围未满足Gate不得正式派遣，不能因新增详细字段而宣称业务已实现。

PF07/08/09/10/10A/11尚未创建的实施方案不捏造最终字段；共同规则已逐阶段列出必须细化的数据对象/状态/页面/验收，明确在阶段生产派遣前全部完成。MES暂缓及PF02/PF04现有工作线保持。

## 5. 本轮验证

文档结构检查覆盖本次与上一轮保留的37份工作树Markdown变更：206个本地链接目标存在，34张九字段卡与待派遣执行记录一致，四份规格共14个线框PageId，代码围栏/JSON样例/细化规格章节检查均无错误；`git diff --check`退出码0。src/tests差异为空，暂存区为空。

未运行后端/前端构建、业务测试、真实浏览器、PoC、安装更新、设备打印或媒体断流验收；只读现有测试源码不能声称测试通过。HEAD保持`8625efb`，未暂存/提交/推送。字段/默认值/线框图为设计基线，硬件与客户能力仍需实际证据。
