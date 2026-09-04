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
- SystemData：2026-09-04 菜单UI五项及补充隔离场景已独立复验关闭，但用户实测移动主题菜单后校验/发布仍失败；当前交原两任务执行同轮“菜单发布真实链返修”，入口为 active/PF-02 末尾“菜单发布与权限回执一致性返修”。重点修复基线资源版本1与模块/回执版本2不一致、就绪漏检及发布反馈/刷新，保留用户草稿，真实SQL/API分层验证，不用Mock代替用户目标生效。模型及思考强度保留当前设置；真实矩阵、精确测试清理及013/PF-02仍active，不提交推送或归档。
- ReferenceData：保持服务骨架，业务功能未实施。
- 部署入口：默认统一部署使用 UnifiedHost（组合 Identity、SystemData、ReferenceData 并托管生产 SPA）；分布式部署使用 Gateway（YARP 反向代理）。两者不互相替代。

## 进行中

- PF-02 当前执行入口为 `docs/tasks/active/PF-02.md` 末尾“菜单发布与权限回执一致性返修（2026-09-04，当前执行入口）”；前述体验补充和旧计划作兼容与边界依据。主工作区 develop，初始化器仅获本缺陷所需种子一致性/就绪检查窄例外，不扩大用户库写权限。原两任务直接稳定交接/返修，不传 model/thinking；主控派遣即返回，不触碰其他 PF 工作。
- 2026-09-04 启动补充：用户明确授权主控直接小修重编译后 control-plane revision 冲突；原两任务暂停此项编辑/构建。主控修正 UnifiedHost 初始化协调器晚于模块后台写入者注册的问题，保留数据库 CAS，使用临时 SQLite 验证启动顺序与旧基线升级/重复启动；正常库及用户调试服务未操作，真实用户启动和菜单发布验收仍待核验。

## 固定工作线

- PF-00：当前范围已合入；旧工作树 `IndustrialPlatform-worktrees/pf-00` 已于 2026-09-02 按用户要求移除，未经新指派不得重建，不再等待工作线同步。
- PF-02 本轮例外：使用主工作区 `IndustrialPlatform` 的 `develop`；旧 PF-02 工作树已于 2026-09-02 按用户要求移除，不重建或重新启用。原功能开发任务 `01a06269-d1ad-7061-88cb-0cabcb1667c5`、独立验收任务 `01a0626b-dc13-73e2-9094-db433c09207b` 保留各自当前模型及思考强度，后续消息省略覆盖参数；保留 DSH.md 与用户调试环境，不自动提交推送。
- 旧分支 `task/pf-00-id-019`、`task/pf-02-sd-006` 仅保留为恢复指针，不再是当前工作线；其他现有分支名称暂时保留，后续任务不因此创建新分支。

## 内部执行序列

- 当前连续执行：菜单发布/可信回执版本失败复现 → 保留草稿的增量一致性修复及发布反馈 → 真实SQL/API持久化闭环 → 原独立验收。旧精确测试清理/防残留保持待办。保留正常功能、既有 WIP 和用户调试进程；现有用户库先只读诊断，元数据修复需受控目标/差异/恢复方案，不能借用精确测试清理授权，普通写验证使用隔离目标，不自动提交推送，不扩展到其他 PF。

## 阻塞与待决策

- Identity 事件命名以既有 `Identity.UserCreated.v1` 风格为当前实现；若要采用小写连字符风格，需要单独契约变更任务。
- Identity 最后管理员守卫当前使用权威持有者计数，并对组外持有路径做精确放行。
- 云端真实登录 E2E 需要稳定、可重复创建或重置的测试账号夹具；现有一次性 bootstrap 不适合作为持久数据库测试种子。
- 受限沙箱曾阻止 Vite/Playwright 写临时目录；现已在授权环境取得 build、Mock Playwright、真实云依赖、默认 UnifiedHost、七页可达性与真实组织/岗位 CRUD 结果。七页业务操作矩阵仍由 TASK-SD-015 收束。

## 最近验收

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
