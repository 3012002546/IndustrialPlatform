# Industrial Platform 当前状态

> 由 Codex 维护。只保留当前有效状态；历史细节进入任务归档、evidence、实施方案或 Git 历史。

## 集成基线

- 分支：`develop`
- 架构收敛整改开始基线：`6000bb5`
- 远端关系：整改开始时 `develop` 领先 `origin/develop` 3 个本地提交。
- 2026-08-20 Work Package 1 基线：Release Build 0 警告/0 错误；后端解决方案测试 1219/1219 通过；前端 Vitest 因沙箱无法写入 `node_modules/.vite-temp`（EPERM）阻塞，作为既存环境基线记录，不在文档工作包修复。

## 服务状态

- BuildingBlocks：基线与公共组件已完成。
- 统一前端与 PF-01：已完成；外部真机 safe-area 项仍待验收。
- Identity/PF-00：TASK-ID-001～023 当前范围已完成并合入；真实 PostgreSQL/Redis 联合链路保留为外部整体测试项。
- SystemData/PF-02：已完成并合入 `develop`。菜单发布、七个管理页面、服务初始化、组织/岗位/任职、功能开关、服务目录与主题策略均已交付；用户已在真实云 Docker 的 UnifiedHost 环境完成业务验证并人工清理测试数据，完整中英/亮暗/200% 视觉矩阵已通过。历史分层限制保留在 evidence，不再作为 active 开发阻塞。
- ReferenceData：PF-03 七模块已于 2026-09-05 完成并经独立验收 PASS；功能提交 `969ee156` 通过合并提交 `e9452b47` 进入 `develop`。开发与验收任务已归档，专用工作树和功能分支已清理，后续统一在主工作树处理。
- 部署入口：默认统一部署使用 UnifiedHost（组合 Identity、SystemData、ReferenceData 并托管生产 SPA）；分布式部署使用 Gateway（YARP 反向代理）。两者不互相替代。
- PF-04：2026-09-06 已完成蓝图/TODO 整改与开发就绪复评，并已整包派遣 Core 001～009。功能开发任务 `01a076d2-a8d3-7163-8721-8cae51393d92` 使用 gpt-5.6-luna max（极高），独立验收任务 `01a076d3-3abf-7921-a367-9b70749d0780` 使用 gpt-5.6-sol high（高）；PF-02/PF-03 已提交后，用户改定 PF-04 直接在主工作树 `develop` 连续执行，不保留专用工作树或功能分支，开发与验收顺序交接。入口为实施 07 V1.1 第 1.3、13～17 章；010 Advanced 后续待细化、不阻塞 Core。2026-09-07 只读核验 Core 已提交 `8625efb`；PF-04 evidence 已有历史自测，但真实浏览器/ClamAV/外部中间件/多实例仍待验收，不将提交视为阶段关闭。

## 后续路线（2026-09-07 文档整改）

- PF-05 实施08：已派遣并实现主要功能，2026-09-11 用户确认当天修改已验证通过，整包最终门禁仍未关闭，详见下方最新核对。PF-06 实施09 的当前范围与派遣状态见其独立条目；此处不再沿用9月7日“PF-05未实施”的历史状态。
- PF-05 → PF-06 → **PF-06A 终端运行时与客户端打包**（蓝图34、实施09A）→ PF-07～PF-10A → **PF-10B 标签管理平台**（蓝图35、实施13B）→ PF-11 IoTCollector → MES。原编号保持；PF06A/PF10B已有核心细化规格、待实际前置核验，其余未建方案阶段继续待细化。所列相对独立功能均按蓝图32§2.1～2.2先验平台兼容再验外部装配。
- 本次仅文档，不创建工程、不运行 PoC、不改变现有工作线；来源、需求映射与检查见 `docs/evidence/2026-09-07-platform-roadmap-docs.md`。

## 进行中

- 2026-09-11 / PF-05 有限收尾最终为 PARTIAL PASS：PDA防误发已修复并独立验证；附件、保全、管理员本人导出、持久Audit、成品首次下载及一次性重用拒绝的隔离真实HTTP业务链已通过，旧绑定/版本/UTC/授权action阻断全部关闭。22文件快照5bd3f43e…独立一致，fresh Release 0警告/0错误，受影响后端1369/1369，PDA Chat25/25、Compliance5/5及类型检查通过。今天的页面修改按用户现场确认通过。见 [最终独立证据](../evidence/PF-05-file-compliance-closeout-acceptance-2026-09-11.md)。无需重复第三套测试，未提交推送。

- PF-05 剩余边界：当前没有真实ClamAV扫描器，仅通过INSTREAM协议适配与业务门禁，实际引擎/病毒库/Clean/EICAR验收BLOCKED；无扫描器时Unknown禁止下载已验证。外部PostgreSQL/Redis/RabbitMQ、Embedded、双实例、2C4G及其他未列入有限收尾的项目按用户要求暂缓，不计通过。用户准备进入PF06，本轮未启动其开发，也不宣称PF05整包生产准入；下方各轮次为历史记录，其“继续全包/待返修”措辞由本段与active/PF-05最新状态覆盖。

- 2026-09-10 / PF-05 R13：独立 evidence §29～30.6 判定页面矩阵及深色合规补验 PASS，主控按该范围关闭 R13。最终 203 文件快照 `f526fa6990d96c6dac2d1c7c26f37eb6fbc0248fd47f8457c33047c0b2aa42de`；两合规页白底浅字缺陷已局部修复，真实四页/再认证/共享用户角色页面复验通过，最终前端 130 文件/949 测试通过。本地反馈 P95 69.2ms、ACK 260.2ms、对方页面可见 2067.7ms，仅本地反馈目标已证实，不作为 2C4G 在线推送门禁结论。正常调试签名私有配置已由开发补齐，隔离验收通过不等于正常进程已加载。原全解测试 exit 1 与 ReferenceData 项目复跑通过分别保留。PF05 整包仍未生产准入，Embedded、双实例恢复、File/Audit 正向与 2C4G 门禁沿原工作包继续；未提交推送。

- PF-06：2026-09-10完成V3.0主控复审，聊天内屏幕共享与独立一对一语音分别邀请/授权/结束、共用选定端点WebRTC；最终数据/slot忙线/15个Hub方法/协商/真实停止及001～011九字段详细步骤已落定。固定001＋009技术验证先行，后续一次实现最终结构。待派遣；G06-1/3待实际媒体证据、G06-2待工作线/环境登记。仅文档，无PoC/开发/运行验收结论。

- 2026-09-09 / PF-05 用户管理查询回归：主控按用户要求直接修复 Collaboration 无模块路由别名与 Identity `/api/v1/users` 的冲突，并清除自动发现的 OData 元数据重复路由、补齐聊天 Hub 路径和浏览器令牌边界。fresh Release 0 警告/0 错误，后端 1778 通过/0 失败/7 环境门控跳过，前端相关 24 项通过及类型/lint/格式通过。用户原调试实例保持运行，修复需后端重新加载后在现场复核；详见 `docs/evidence/PF-05-query-regression-2026-09-09.md`，不据此关闭 PF05 整体验收。

- 2026-09-09 / PF-05 最新调试结论：evidence §17.7 已用真实隔离PostgreSQL验证Shared旧账本完整启动链，3/3通过，旧数据/应用时间保留、Inspect只读、二次启动不再Apply、真实drift拒绝；用户42703缺checksum启动回归按该范围关闭。fresh Release 0警告/0错误，后端1770通过/0失败/6环境门控跳过，其中上述3个PG用例随后已启用通过。PerService真实四库、Redis/RabbitMQ、页面与资源故障仍未验，PF05整包未准入；未操作普通调试库或用户服务。

- 2026-09-09 / PF-05 当前优先回归：checksum修复验收重新打开。用户PG启动在SystemData Inspect阶段提前读取不存在的checksum列，早于Runner补列；原PASS覆盖不足，按evidence §17.4为REOPENED。已要求集中修复全部模块的只读Inspect/迁移Apply顺序，补隔离旧版PG完整升级与二次启动。主控证实实际配置PG经受控权限可达，默认探针Socket10013属于访问限制，不能记为依赖离线。普通调试库和用户服务未被操作，整包仍未准入。

- 2026-09-09 / PF-05 调试回归：用户正常启动 UnifiedHost 遇到 Identity `ID-004-01` checksum drift，确认是新增校验对历史无checksum账本的升级兼容缺陷。Identity/SystemData 已在描述一致时安全回填checksum，保留业务数据与原应用时间；真实描述/checksum/物理漂移仍拒绝。独立验收 evidence §17 PASS：fresh Release 0警告/0错误、后端1765通过/0失败/3外部跳过；主控定向复跑两套迁移测试各10/10通过。未操作用户普通调试库，PF05整包仍继续R3且未生产准入。

- 2026-09-09 / PF-05 最新里程碑：独立验收 evidence §16 判定 R2 PASS WITH RUNTIME CONTINGENCIES，145文件hash核对一致；fresh Release 0警告/0错误，后端1758通过/0失败/3外部跳过。合同、并发、多Case文件与SQLite Shared/PerService/drift验证通过；开发已进入R3，真实外部依赖/多进程/浏览器/2C4G及故障恢复仍未验。整包保持 NOT PRODUCTION-ACCEPTED，模型与工作线不变。

- 2026-09-09 / PF-05 最新里程碑：独立验收 evidence §15 判定 R1 PASS，Embedded可定位身份缺陷及19个目标前端文件格式问题已关闭；fresh Release 0警告/0错误，后端1725通过/0失败/3外部跳过，Embedded15/15。全仓仍有132个非目标格式基线失败。原开发任务已进入R2合同/一致性验证，随后R3隔离真实环境/页面补验；整包仍 NOT PRODUCTION-ACCEPTED。该记录更新下方历史阻断中的R1项目，不关闭其余未验项。

- 2026-09-09 / PF-05：独立验收冻结 `PF05-dirty-f86415d-472e41094ed37412`，结论 `BLOCKED / NOT PRODUCTION-ACCEPTED`。fresh Release 0警告/0错误，后端1713通过/0失败/3外部跳过，前端125文件/927测试及类型/Lint/build通过；A02格式与A06 Embedded身份失败，其余专项合同/真实链路/页面/2C4G仍缺证据。主控按 `docs/tasks/active/PF-05-R1.md` 将剩余工作分为实现修复、合同验证、隔离环境补验，原两个任务直接闭环，模型不变，未提交推送，不关闭PF05。

- 2026-09-08 / PF-05：用户删除原子任务并要求新建继续方案；新开发/验收任务接续既有成果，在主仓`develop`按`docs/tasks/active/PF-05.md`执行。总控已用`PF05-前置契约与合规执行补充.md`定义最小Identity/File/Audit增量及合规DTO/状态，先实现依赖并通过合同测试，再连续推进001～008。开发`01a07f9e-355b-75f1-ba20-5d41f49f07ca`（gpt-5.6-luna max（极高））唯一写生产代码；验收`01a07f9d-2f78-7c21-9f2e-09144882ac11`（gpt-5.6-sol high（高））稳定交接后直接复验。主仓新鲜基线Release 0警告/0错误，后端1684通过/3外部跳过、前端926通过；它不是PF05功能或真实环境验收。历史前置BLOCKED由本轮明确依赖实现步骤承接，不再作为永久停工指令；原完整范围和真实验收门禁保留。

- PF-02 无进行中开发任务；工作包已归档。后续若出现新缺陷，按独立增量任务处理，不重新打开已经完成的整阶段。

## 固定工作线

- PF-05：按2026-09-08用户指令统一使用主工作树`develop`；原`IndustrialPlatform-worktrees/pf-05`及`work/pf-05-collaboration`已清理，无独有提交丢失，开发与验收证据已完整迁入主仓。不重建旧工作线。

- PF-03：功能提交 `969ee156`、合并提交 `e9452b47`；归档工作包为 `docs/tasks/archive/PF-03.md`，证据为 `docs/evidence/PF-03.md`。原 `pf-03` 工作树和 `work/pf-03-reference-data` 分支已清理，不再是固定工作线。

- PF-00：当前范围已合入；旧工作树 `IndustrialPlatform-worktrees/pf-00` 已于 2026-09-02 按用户要求移除，未经新指派不得重建，不再等待工作线同步。
- PF-02：已合入并归档，归档工作包为 `docs/tasks/archive/PF-02.md`；旧 PF-02 工作树已于 2026-09-02 按用户要求移除，不重建或重新启用。
- 旧分支 `task/pf-00-id-019`、`task/pf-02-sd-006` 仅保留为恢复指针，不再是当前工作线；其他现有分支名称暂时保留，后续任务不因此创建新分支。

## 内部执行序列

- PF-02 内部执行序列已结束；历史菜单发布、写操作防重复提交与数据清理记录仅作为归档证据保留。

## 阻塞与待决策

- Identity 事件命名以既有 `Identity.UserCreated.v1` 风格为当前实现；若要采用小写连字符风格，需要单独契约变更任务。
- Identity 最后管理员守卫当前使用权威持有者计数，并对组外持有路径做精确放行。
- 云端真实登录 E2E 需要稳定、可重复创建或重置的测试账号夹具；现有一次性 bootstrap 不适合作为持久数据库测试种子。
- 受限沙箱曾阻止 Vite/Playwright 写临时目录；PF-02 后续已在真实云 Docker 的 UnifiedHost 环境完成业务验证，测试数据已人工清理，该历史环境限制不再阻塞 PF-02。

## 最近验收

- 2026-09-07 / PF-02 最终状态复核：功能提交已进入 `develop`；后端 fresh Release 构建 0 警告/0 错误，当前工作区全量后端 1684 通过、0 失败、3 个外部条件测试跳过，前端 124 文件/926 测试、类型检查、Lint 与生产构建通过。用户确认已在真实云 Docker 的 UnifiedHost 环境完成七页业务验证、人工清理测试数据，并完成中英/亮暗/200% 视觉矩阵。PF-02 按当前功能范围关闭并归档；历史报告未保存的逐项细节作为证据完整性限制，不重新打开功能开发。
- 2026-09-04 / PF-02 提交前隔离测试：主控确认旧组织树失败快照伴随网络错误，后续7项为夹具断连，不计7个业务缺陷；修正测试夹具所有权和过期断言后，两次新进程均8/8通过，定向类型/ESLint/格式检查通过。仅测试与证据修改，不动AppDataTable、业务数据或调试服务，不代表整包真实验收；详见 evidence“提交前隔离测试复核”。
- 2026-09-03 / PF-02 UI 专项复核：现有 Chrome 同视口实拍用户管理、导航、组织、Feature 与主题错误态；黄金页控件正常，SystemData 原生按钮、缺失布局样式和未统一页壳可复现，TASK-SD-014 视觉未通过。已补充工作包的纯视觉边界与逐页截图门禁，交原功能开发、独立验收任务稳定交接；无业务写入、未改调试服务。详情见 `docs/evidence/PF-02.md` 最新视觉复核节。

- 2026-09-03 / PF-02 第五轮独立复验：fresh Release 0 警告/0 错误；后端 1378 通过、0 失败、3 跳过；八项独立探针全部达到预期。前端第三轮 103 文件/769 测试及 build/typecheck/lint/定向格式通过，本轮未重复执行。任务自有 SQLite UnifiedHost health 200，readiness 因缺 Redis/RabbitMQ 为 503，临时进程已清理。没有已确认的安全外部账号/目标库夹具，七页/三端 runtime/十三门禁真实矩阵仍待验收；PF-02 保持 active，不提交推送。证据见 `docs/evidence/PF-02.md` 第五轮及 `TestResults/pf02-independent-20260903/round5-review.md`。

- 2026-08-27 / PF-02 阶段历史验证：fresh Release Build 0 警告/0 错误；后端 1298 通过、0 失败、3 跳过（总计 1301）；前端定向 Vitest 6 文件/16 测试、Lint、Typecheck、定向 Prettier 与 `git diff --check` 通过；默认 UnifiedHost 连接云 PostgreSQL/Redis/RabbitMQ 启动，真实组织/岗位 CRUD Playwright 1/1 通过。该证据继续有效，但不替代 TASK-SD-014～017 的收束验收。完整证据见 `docs/evidence/PF-02.md`。
- `1427d18` / TASK-SD-007～010：资源导航、Feature、服务目录、主题策略、缓存/审计/Outbox/Identity 对账后端闭环已完成；云 PostgreSQL/Redis/RabbitMQ 3/3。2026-08-21 本地调试复验修正补录迁移版本识别后，fresh Release build 0/0、常规后端 1278/1278、UnifiedHost 云配置启动并完成 Identity → SystemData → ReferenceData 初始化。
- WP4（本次提交）：fresh Release Build 0 警告/0 错误；静态审查修正后的常规后端 1227/1227；前端 Vitest 受限沙箱 EPERM；IntegrationTests 8/8 为未启用外部环境门控后的早退，不代表真实外部链路通过；real-login E2E 因 Playwright runner 不可用未执行。
- `9f48d89`：PF-00 用户与用户组管理 API、前端闭环、首次改密与幂等写入。
- `1b72c6b`：TASK-SD-006 SystemData 组织、岗位与任职管理 API。
- `1b32ae6`：TASK-ID-019 Identity 三层种子与正式 admin 引导。
- `69c49b7`：TASK-SD-005 组织、岗位与时间化任职领域和持久化。
- `83a00d1`：TASK-ID-018 用户与用户组安全删除恢复。
- `07d6863`：TASK-ID-017 用户组授权模型。
- `05fe591`：TASK-SD-004 初始化握手、NotReady 契约与验收夹具。
- `61753dc`：TASK-SD-003 数据库编排 Runner。

## 更新规则

- 派遣单位固定为整个 PF，记录负责人、PF 工作包和固定工作线。
- 内部 `TASK-*` 不是派遣、验收或提交门；执行智能体连续完成整个 PF 后才交回 Codex。
- PF 完成后从“进行中”删除，工作包移入 archive，结果写入一份 PF evidence。
- 不累计会话快照、完整变更清单或历史测试日志。
- 执行智能体负责任务内测试；Codex 只做最终 Release 编译，成功后提交和集成。
- PF 开始或恢复前默认由 Codex 将原工作线同步到最新 `develop`，不新建内部任务分支；PF-02 本轮主工作区例外以当前工作包为准，不恢复旧工作树。


## 2026-09-07 PF05起派遣规格细化（仅文档）

新增共同细化/页面/协作规则及PF05、PF06、PF06A、PF10B四份字段/接口/线框图规格，34张内部任务引用精确章节；待派遣草案位于 `docs/tasks/pending/`。派遣状态均为待派遣，设计就绪度待前置核验；各规格Gate未闭合不得派遣相关生产范围。PF07～PF11未建实施方案的阶段继续先设计、后派遣，MES暂停不变。未创建/启动/通信任何开发任务、未实现生产代码、未运行PoC/设备验收。当前PF02/04工作线和已有WIP保持。
