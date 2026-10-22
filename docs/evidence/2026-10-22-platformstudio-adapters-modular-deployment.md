# PlatformStudio双模式适配与按需部署设计核验

日期：2026-10-22。只细化实施11/12及其规格；范围是PlatformStudio，不整改整个Industrial Platform，不启动服务/生产MES连接，不修改概念站。

## 1. 基线与单写边界

- `develop`，HEAD=`03e06f31ff99030d93c346c3a7f1e36587fe6297`。接手时存在8份已跟踪设计WIP及`2026-10-22-platformstudio-standalone-review.md`未跟踪记录，均保留。本轮新增适配合同、当前记录并同步原规格/索引；未清理或覆盖其他任务代码。
- 按要求核对CURRENT、协作约定、相关工程笔记和PF07接手要点；任务状态中PF08开发主控空闲、近期只读准备。当前会话单写这些设计文件，无子代理/任务派遣，无跨会话消息。实现阶段共享File/Audit由PF08-001交接，Jobs由PF09-009接续，不能并写公共文件。
- 上轮[独立部署澄清](2026-10-22-platformstudio-standalone-review.md)已明确“无需我们的平台即可接现有MES”；本轮补齐其尚未确定的具体适配、存储及模块装配合同，保留未受影响的概念和验证证据。

## 2. 实际源码依据与结论

| 核对项 | 本轮发现 / 设计处置 |
| --- | --- |
| Studio当前实现 | 当前HEAD仍无Studio服务/前端实现或独立安装产物；本文是待开发合同，不是实现验收 |
| Scheduler耦合 | [SchedulerRuntime](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Scheduler/SchedulerRuntime.cs)依赖IControlPlaneTenantSource并引导systemdata.reconcile，不能整体注册到独立包；RuntimeSupport只接显式租户/handler，平台对账引导留原生适配 |
| File/Audit持久化 | 复用[实际实体](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Entities/SystemDataOperationalTables.cs)及当前扫描/上传/可靠审计实现，抽离平台聊天引用/控制面查找耦合；Embedded不初始化聊天/通知。新增owned-reference保全字段；审计镜像表固定迁移、投递器可选 |
| 密码与最小登录 | 实际平台采用BCrypt workFactor=12、12～128字符复杂度、5次失败锁15分钟；规格引用实际文件，限定抽最小策略/哈希代码复用，不能误写为平台已有ASP.NET PasswordHasher或另装Identity服务 |
| 任务安全 | Hangfire关闭显式重试不排除至少一次补偿；保留PF07持久活动槽、幂等/epoch、Unknown核对及服务器证据检查，不以单worker冒充可靠性 |
| 旧迁移/能力来源 | 旧已应用ID/checksum不变；当前尚无STU已应用步骤，首次交付冻结STU-001～006（16表）。健康来源时点、版本字符串、实际配置与真实公共控件测试约束保持 |

## 3. 设计补齐范围

权威细节见[双模式适配与按需部署](../implementation/details/PF08-PF09-双模式适配与按需部署.md)，主任务仍在[11](../implementation/11-Industrial%20Platform%20Low%20Code开发实施方案.md)与[12](../implementation/12-Industrial%20Platform%20Dashboard%20Report开发实施方案.md)。

1. 两种装配沿同一Studio核心；原生沿平台外壳、身份权限、ReferenceData/File/Audit/Scheduler，外部MES使用受信身份/数据映射和随包Embedded运行组件。
2. 身份明确OIDC/MesCode、本地最小管理员独立场景、稳定主体/角色/租户/工厂映射、撤权时效、三技术表、端点/相关性/回放防护。数据明确Dataset/动作/字典/编号与源权限；文件明确自有引用/扫描/保全和MES附件模态/归档快照；审计明确本地事务Outbox/镜像；任务明确两种实现和Unknown保护。
3. 四模块任意非空子集；共用基础随带。未选模块API/Handler/菜单/worker不注册，不引入其外部引擎；Report仍含Excel。安装清单、脱敏配置、运维命令/诊断页、依赖矩阵、增装/排空停用/恢复均有固定合同。
4. 原13表保留，新增3身份表；WorkbookSession补身份依据列、Report既有授权快照保存原会话依据，供后台/Office无浏览器cookie时重验。Embedded表复用现有精确字段且有独立固定迁移集合。客户端子集不改变核心DDL或冒充研发完成；原24张九字段任务卡全部增加精确适配/验收引用。
5. D01～D16分别定义动作与可观察结果，包含30装配组合检查（2模式×15非空子集）、两模式四单模块+全模块真实链，以及客户实际组合/离线/升级/恢复。继续C0共用基础→C1低代码→C2看板→C3报表/Excel→C4追溯逐块验收，C5集成不替代各部分；研发顺序不强加为客户安装顺序。

## 4. 本轮验证

检查使用仓库本地只读文档脚本，结果如下；脚本/输出在忽略的`.run/`目录，不进入产品交付。

| 检查 | 结果 |
| --- | --- |
| `pf08-pf09-doc-check.py` | 15份完整规格/索引，217个本地引用，12个JSON样例；24张任务卡/216个字段、原13业务表、A16/B42检查通过 |
| `pf08-pf09-concept-check.py` | 最新本地概念源hash=`189cd031bb163a6657c09a5724b5e082749143e2a9a2d2146b5bb860c3a71a4d`；35/35看板入口映射、原新增8项对齐断言通过；未改变概念源或站点 |
| `studio-adapter-doc-check.py` | 12份变更文档，175个本地引用、11个JSON样例；3张新增身份表、24张任务卡适配引用、D01～D16及示例角色hash检查通过；所有源/部署文件及暂存区无改动 |
| `git diff --check` | 退出0，无空白差异错误；仓库正常autocrlf的LF→CRLF提示不是检查失败，未改变Git配置 |

这些是文档/概念映射检查；尚未运行Studio业务、平台回归、MES身份桥、30装配测试或新的在线视觉复验。后端业务源码未改，本轮不重复全量Release构建/测试。结果对应本轮文档，不把模拟页面或既有平台测试扩大为Studio运行通过。

## 5. 保留的实施与验收条件

G04的设计空白已补齐，实际RuntimeSupport抽离/原生兼容、外部身份与数据适配、离线安装仍待按任务实施验证；G02实际引擎版本/合法发行包、Windows/Linux及既有Provider/客户端未测项继续待验，未更改平台自身许可证。目标MES端点、映射、凭据、真实对照数据由客户/用户明确提供，生产接入由用户负责；开发以模拟接口先验机制，不能据此关闭现场项。没有修改源代码、私有连接或部署，没有提交/推送。

后续整体一致性复核及V1.3补充见[本日后续记录](2026-10-22-platformstudio-consistency-review.md)；本记录V1.2检查数字保留为当时结果，不冒充最新执行结果。
