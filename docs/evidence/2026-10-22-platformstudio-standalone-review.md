# PlatformStudio独立部署复核

日期：2026-10-22。范围仅PF08/PF09的PlatformStudio：低代码、看板、报表/Excel、追溯四部分；公共模块仅作为本产品依赖核对。不整改整个Industrial Platform，不启动服务、不接生产MES、不发布安装包或概念站。

**同日用户澄清后的当前结论：平台原生契合度优先；Studio剥离后不依赖我们的平台即可接入现有MES。** 本轮早先把Identity/SystemData/ReferenceData及Redis/RabbitMQ统一纳入外部必装基线的解释已撤回。以下源码事实仍有效，不能由这些事实推导外部必须沿用相同装配；实际修正见§3，后续以该边界为准。

同日后续：双模式五类适配、精确存储/接口及四产品按需部署已补齐，见[后续设计核验](2026-10-22-platformstudio-adapters-modular-deployment.md)。本文§3“精确合同待补齐”为上一轮快照；实际运行与发行仍未验收。

## 1. 接手与结论

- 核对HEAD为`03e06f31ff99030d93c346c3a7f1e36587fe6297`，分支及最终修改以本次Git检查为准；开始时暂存/未暂存/未跟踪修改均为空。保留已提交PF08/PF09设计，不恢复旧会话WIP。
- 已读取CURRENT、统一约定、工程笔记相关规则和PF07末尾接手说明。任务状态检查中，本设计主控活跃；“PF-08 低代码 | 主控”空闲，最近完成只读准备且未改文件。没有派遣或发送任务消息，本轮文档由当前主控单写。
- **需求和设计方向支持Studio独立部署；当前代码尚不能作为Studio独立安装交付。** 现有平台宿主能运行不等于Studio已实现；概念站是模拟交互，不是后端独立运行证据。

## 2. 实际代码与缺口

| 核对项 | 当前证据与判断 |
| --- | --- |
| Studio工程与入口 | `src/backend/src/Services/PlatformStudio`、`src/backend/src/Hosts/IndustrialPlatform.PlatformStudio.Service`及前端`pages/pc/studio`均未存在，解决方案未登记Studio；只有计划落点，不能声称已有可安装产物 |
| 现有组合宿主 | [UnifiedHostModuleCatalog](../../src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/UnifiedHostModuleCatalog.cs)固定装配Identity/SystemData/ReferenceData/Collaboration，没有Studio。它可以作为显式组合机制的复用依据，不能直接当作Studio精简包 |
| 现有部署材料 | [Dockerfile](../../deploy/application/Dockerfile)发布UnifiedHost和现有前端；[compose](../../deploy/application/compose.yaml)运行平台镜像并引用外部基础设施网络。这不是Studio独立/完全离线安装清单；构建阶段联网与最终离线安装也须分开核验 |
| ReferenceData边界 | Studio已有Rule编号及复用字典/元数据需求；平台内原生使用ReferenceData，外部MES经适配提供同等业务合同。先前据此要求完整ReferenceData随独立包交付不符合用户原意，已撤回；备份按实际数据所有权，不要求目标MES增加平台数据库 |
| 基础设施边界 | [Identity注册](../../src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/DependencyInjection.cs)实际注册Redis、事件总线；[SystemData注册](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DependencyInjection.cs)含缓存/锁、后台任务、File/Audit和扫描。这些是现有平台实现事实，不是Studio外部装配的固有依赖；选择该实现时不能以Outbox退避冒充可永久省略。外部适配清单及故障恢复尚待验证 |
| 文件链 | [扫描适配](../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Files/ClamAvFileScanner.cs)未配可信扫描端点时返回Unknown，生产不能用DevelopmentBypass。完整Excel/附件/报告交付不能漏扫描与文件持久化；当前LocalFileContentStore也不能被误写成强制MinIO |
| 引擎及发行 | 真实HTML/PDF、Office编辑保存、合法离线物料、Windows/Linux方案与整套目标矩阵仍待G02/G04核验；本次未重新认定第三方许可或选择平台许可证 |

## 3. 本轮文档修正

[数据规格§6.1～6.2](../implementation/details/PF08-PF09-数据模型与生命周期.md#61-独立交付装配基线)改为“能力边界—平台原生实现—外部MES适配”逐项对照。平台内直接复用框架、菜单、主题/语言、Identity/ReferenceData/File/Audit/Scheduler公开能力；外部使用同一Studio核心、消费方合同和目标MES/配套能力适配，不要求安装平台服务或访问平台表。文件授权/扫描/保全、审计可靠性、任务幂等与Unknown等语义不因换宿主削弱。无MES最小登录保留为单独场景，不替代外部MES接入。

实施11的001先明确双装配骨架；C1～C4各先验原生、再验对应MES链；实施12的015验完整离线包。B31要求外部目标不安装Identity/SystemData/ReferenceData，网络只允许Studio清单与目标MES；备份覆盖Studio自有和所选适配存储，不固定为四个平台数据库。接口规格§1支持MES适配产生可信身份上下文；§8明确现有SystemData迁移/内部API仅属于平台原生适配。蓝图32仅补Studio澄清，不修改其他PF部署范围。

G04如实保留外部适配精确接口、存储/迁移、所选依赖及实测待办；不能让开发自行决定，也不能用平台原生G03合同冒充已完成外部合同。未受影响的Studio表、页面配置、四部分顺序和已有验证证据保留。没有实施抽离、替换公共组件或新建调度引擎。

## 4. 验证与边界

前轮仅文档和源码只读核查：7份当时变更文档引用/编码/围栏无错误；14份文档、196个本地引用、11个JSON、24张任务卡/216字段、13张Studio表及A16/B42项检查通过；概念入口35/35及原新增8项断言通过，`git diff --check`退出0。该结果对应澄清前快照，不作为本次修正后的新验证。

本次澄清修正仍在`develop`、HEAD未变，共修改8份既有文档及本记录，未改生产源码/私有配置。未运行后端构建或业务测试，未把已有平台测试扩大为Studio独立部署通过。G02/G03/G04相关实物和实测条件继续开放，未提交或推送。

澄清后重新验证：9份变更文档、110个本地引用无错误；既有完整文档检查仍为14份/196引用/11 JSON/24任务/13表/A16/B42全部通过，概念入口35/35及8项对齐断言通过。首次diff检查临时关闭autocrlf造成CRLF被判尾部空白，按仓库正常配置执行`git diff --check`退出0、诊断0行；未改变仓库Git配置，也未修改业务代码以通过检查。
