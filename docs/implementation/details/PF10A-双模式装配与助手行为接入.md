# PF-10A 平台复用、独立最小安装与助手行为接入

V0.6 · 2026-11-18 · 平台内优先，两种部署使用同一知识与助手核心。本文列出的“现有”指已在仓库找到实现或合同，“拟用”指 PF-10A 的装配选择。发行包、外部身份映射与实际运行仍需按文末场景验证。

## 1. 平台内哪些可以直接用

| 能力 | 已存在且可以消费的部分 | KnowledgeHub 要做的工作 |
| --- | --- | --- |
| 登录、权限与项目 | 当前会话和服务端身份，前端路由守卫、菜单策略、PermissionGate | 注册知识/问题/助手权限与页面种子，由可信租户确定默认项目。后端检查操作及对象范围，菜单仅用于发现。[当前身份](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.Security/ICurrentUser.cs) · [路由守卫](../../../src/frontend/src/router/guards.ts) |
| 管理页面与导航 | 复用平台外壳、导航、语言主题及 AppDataTable 等已有管理控件 | 新增知识页面和外壳中的助手监听器，把稳定 pageId 映射到命名路由。页面自己声明可用筛选并回报实际结果。[导航策略](../../../src/frontend/src/systemData/runtime/navigation.ts) · [表格组件](../../../src/frontend/src/components/management/AppDataTable.vue) |
| 文件与审计 | IOwnedFileService 提供准备、提交、状态、读取、票据、释放及保留。Audit 服务可接收审计事实 | 当前 OwnedFileService 只接受 platform-studio 及 Studio 归属类型。002 按生产者登记新归属类型并回归旧调用，再实现上传主体/归属校验、修订附件引用、问题转知识时的授权复制和幂等。业务事务保存审计意图再投递。Studio 文件传输接口包含引用操作，但其作用域校验属于 Studio，须核验适用性。[文件合同](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Files/Contracts/OwnedFileContracts.cs) · [传输合同](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Files/Contracts/StudioFileTransferContracts.cs) · [审计实现](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Auditing/Application/AuditService.cs) |
| 后台任务 | 已有一次性任务提交、按操作键查状态和取消合同。现有实现限定 platform-studio、studio.report.render 及 runNId 参数 | 002 扩展受信生产者、处理器与参数校验及操作键隔离，按任务登记导入、索引和导出处理器，校验租户、修订和索引代次，再确认业务完成。排队成功仅代表已受理。[任务合同](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Jobs/Contracts/OnceContracts.cs) |
| 站内提醒、实时刷新 | 通知模块已有站内消息合同和 SignalR 刷新模式 | 问题分派可投递站内消息。助手动作从 KnowledgeHub 执行状态读取；宿主支持时实时提醒发起端刷新，按用户广播的通知不能作为标签页执行凭据。[通知合同](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Contracts/Notifications/NotificationContracts.cs) · [刷新客户端](../../../src/frontend/src/api/systemData/notificationHub.ts) |
| 业务查询、用户导出 | 已启用 Studio Dataset 时消费其公开 execute 入口。Identity 另有用户列表 XLSX 接口，控制器执行 UserView 和租户范围检查 | 绑定获准的具体数据源或目录，传递调用者身份、允许的字段和条件。查询结果需续问或导出时由助手保存受控快照。Identity 导出与 Studio Dataset 分别登记。[Dataset 端点](../../../src/backend/src/Services/PlatformStudio/IndustrialPlatform.PlatformStudio.Api/StudioEndpoints.cs) · [用户导出](../../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Controllers/UsersODataController.cs) |

知识修订、内容解析与引用坐标、检索、问题转知识、跨项目资料包、会话执行、能力注册与 DeepSeek 网关归 PF-10A 新增。两个宿主调用同一套业务用例，避免双份索引规则或重复实现工具调用。

## 2. 独立发行最小清单

建议先验单节点装配。浏览器静态资源由 KnowledgeHub 宿主提供，API 与任务 worker 同进程。业务表、作业、文件元数据和审计可共用一个受支持的数据库实例，各自按所有者迁移。首个最小安装候选采用 SQLite 与本地文件卷，实际兼容性以冷安装和并发恢复验证为准。以下是发行物与运行依赖，不要求每行单开一个服务。

| 必带项 | 最小内容及启动检查 |
| --- | --- |
| 宿主与独立外壳 | KnowledgeHub 服务、静态前端资源、共享业务模块、初始化/升级命令和健康检查。检查数据库迁移、默认项目及前后端能力版本。[Studio 独立宿主参照](../../../src/backend/src/Hosts/IndustrialPlatform.PlatformStudio.Host/Program.cs) |
| 身份与授权 | 无 MES 时装配本地管理员引导、普通用户登录、查看/维护等权限、会话与退出。可复用 RuntimeSupport 的密码散列和登录限制代码，KnowledgeHub 负责主体持久化和项目授权。外部 MES 身份是显式替换选项，身份源失效时保留原模式。[散列实现](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Security/BcryptPasswordHasher.cs) · [登录策略](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Security/LoginAttemptPolicy.cs) |
| 数据、原件与审计 | 持久数据库、上传/暂存及正式文件空间、文件状态与票据、审计存储和密钥保护。原件、数据库与受保护密钥一起纳入备份恢复。启动检查存储可写、文件生命周期与审计投递可运行。[Studio 嵌入式装配参照](../../../src/backend/src/Services/PlatformStudio/IndustrialPlatform.PlatformStudio.Infrastructure/Integration/Embedded/StudioFileAuditRegistration.cs) |
| 文件扫描 | 包含可工作的扫描适配与扫描引擎连接。单机安装可随发行部署现场可维护的引擎与规则库，也可配置现场已有扫描服务。未确认 Clean 的文件保留待处理或隔离，禁止进入原文读取和索引。上线前用实际上传验证从扫描到可读。[文件读取门禁](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Files/Application/OwnedFileService.cs) · [ClamAV 适配](../../../src/backend/src/BuildingBlocks/IndustrialPlatform.RuntimeSupport/Files/Infrastructure/ClamAvFileScanner.cs) |
| 知识检索与作业 | 随包提供[PdfPig、Open XML SDK 及 FTS5 索引](../13A-Industrial%20Platform知识中心开发实施方案.md#deploy)、段落/页/单元格定位、关键词索引及可重建机制。装配持久任务存储、worker、重试和租约恢复，注册知识导入、索引、导出任务。Studio 的 AddStudioJobs 由 Report 激活，知识中心自行注册自己的任务处理器。[Studio 作业装配参照](../../../src/backend/src/Services/PlatformStudio/IndustrialPlatform.PlatformStudio.Infrastructure/Integration/Embedded/StudioJobs.cs) |
| 助手运行与配置 | 随包提供行为目录、会话/步骤存储、页面领取与回执、结果卡、资料包和 ModelGateway。启用模型问答须有服务端 Secret 和到 DeepSeek 的出站连接。部署诊断分别报告知识读写、索引 worker 和模型可用性。 |

**按现场添加**：可信 MES 身份、业务 API/MCP、外部页面适配、通知、实时提醒或后续选定的向量检索。断开主平台、Studio、MES、VPN 和模型网络仍应能用本地身份上传、搜索、读原文、处理问题和交换资料包；模型网络恢复后验证带引用问答。首装物料仍须明确数据库驱动、解析器/扫描引擎版本与许可、存储路径及实际启动参数。

## 3. 同一行为在两个宿主中怎样接入

项目级行为定义按[主规格 §9.2.1](../13A-Industrial%20Platform知识中心开发实施方案.md#host-actions)执行。平台业务操作限定平台数据和页面，独立接入 MES 时登记该项目自己的业务行为。能力提供方登记稳定的 capabilityId、版本、参数、处理器、权限与完成条件。部署适配器将它绑定到当前身份、项目资源和外壳。每次对话取“已启用且绑定成功、当前用户有权、当前终端可执行”的交集生成 tools。调用前再次校验绑定和权限。能识别的对象缺少数据源时显示接入状态，其他来源的授权不能作为替代。模型只收到本轮函数说明，主机地址、凭据和路由解析留在适配器内。

| 行为 | 平台内提供方 | 独立部署提供方 | 成功证据 |
| --- | --- | --- | --- |
| 查询知识、创建问题草稿 | 平台身份 → KnowledgeHub 业务用例 | 本地/MES 映射身份 → 同一业务用例 | 有权限的修订和定位引用，或返回已创建草稿 NId |
| 查询某个业务对象 | 绑定平台拥有并授权开放的 Dataset 或平台业务接口 | 项目重新定义 MES 业务口径、接口和权限；可对齐时映射到公共查询合同，缺失时提示未接入 | 实际查询条件、时间、结果及稳定标识。空结果与不可用分别显示 |
| 打开页面、应用筛选 | 平台菜单和命名路由，经原生外壳执行 | 独立外壳登记自身路由。外部 MES 页面只有目标宿主能鉴权、接收动作并回执时才声明相应能力；普通链接仅供人工打开 | 发起标签页回报 Navigated、FiltersApplied 或 DataLoaded，以本次请求所需阶段为准 |
| 导出一份用户目录 | 原生 Identity 导出适配器 | 仅在现场有授权目录和导出接口时登记其提供方。本地登录账号不默认充当业务用户目录 | 生成完整且可下载的 XLSX，权限、列及范围来自当前目录 |
| 导出刚才的结果 / 导出知识资料包 | 平台文件和任务适配 | 内置文件和任务适配 | 对应会话快照或所选知识修订的产物就绪，并在下载时复核权限 |

**扩展的具体做法。** 新增“到货单查询”时，在各自部署绑定实际业务名称、源修订或已登记操作、日期口径、字段和权限，共用查询处理器。新增“定位附件页签”时，先声明参数与成功阶段，再由支持该页面的外壳注册处理器。仅一个宿主支持时，该行为只在该宿主出现。后端记录动作，原标签页凭执行标识和页面实例领取并回报。消息重放、断线和版本不匹配均沿用[页面动作与回执](PF10A-核心接口与页面规格.md#44-页面动作与回执)。

## 4. 验收时怎样证明装配正确

知识主线另按[同资料、同题集的双模式验收矩阵](PF10A-资料解析检索与引用验收.md)记录 K01～K06。既验证内容质量，也分别证明文件、作业、身份和阅读器使用了本模式的组件。

| 环境 | 验证路径 | 留存证据 |
| --- | --- | --- |
| 原生 | 从平台菜单用普通用户和维护人分别进入，上传手册并按权限搜索/问答，创建问题草稿，打开页面，使用已启用的真实业务目录导出 | 业务身份、文件引用、审计事件、作业状态、页面实际回执以及导出范围 |
| 独立最小 | 全新数据目录初始化，只开放安装清单内依赖。真实文件通过扫描、解析和检索。中途重启并恢复索引任务，检查审计和文件仍可读；两个浏览器标签仅发起页执行 | 进程/监听与出站依赖清单，数据库账本，扫描与索引记录，原文和导航回执 |
| 缺依赖与扩展 | 关闭扫描、模型或业务源逐项检查影响。给第二个资源绑定相同行为，再停用适配或撤回权限并恢复旧会话 | 未扫描文件不可读取，模型不可用时原文仍可查，工具清单/实际执行均随授权更新。无现场源时如实记为未联调 |
| 跨模式 | 同一资料包原生 → 独立 → 原生，验证修订来源、重复跳过、冲突处理和新引用 | 两端可独立打开相同原件，引用不依赖对端在线 |

三类工程缺口分别验证：跨业务文件引用复制的授权，独立身份/文件/作业的实际装配，真实数据源与 DeepSeek 的联调。源码能证明公共入口存在，以上验收须用 KnowledgeHub 自身的发行物执行。
