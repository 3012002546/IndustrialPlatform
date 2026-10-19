# PF07 文档细化记录（2026-10-07）

本文件保留首稿交付及当时检查事实；2026-10-12已按用户要求收敛，当前范围以[新复核记录](2026-10-12-pf07-document-convergence.md)及V1.1实施/明细为准。下文9表/5页是历史草案计数，不是当前实现要求。

## 范围与结论

用户要求按既有习惯调整文档、补齐表结构/字段/线框图；本轮仅文档。实施10沿既有母版组织：设计正文→任务依赖→九字段卡→完成标准→执行记录→下一阶段输入。

新增实施10、PF07明细、pending/PF-07工作包；同步总Todo、实施索引、共同派遣标准、pending索引及CURRENT，纠正相关PF05/PF06A索引过时摘要，保留历史验证边界。PF07设计就绪度待前置核验、派遣状态待派遣。

## 交付内容

- 两个模块加入SystemData.Service，复用服务级初始化/可靠设施，不新增核心宿主。
- 9张业务表：scheduler_job/trigger/run/attempt/lease/request、platform_health_probe/observation/incident；通用生命周期只集中定义一次，技术表不机械继承；每表有用途/字段语义/类型/约束/索引/保留及人工维护说明。
- PG/SQLite/SQL Server2019/2022逻辑映射；2022没有本轮实机证据，不能视为适配验证完成。
- Cron/时区/DST/错过、双版本、幂等、租约/旧fence、取消/失联Unknown、安全处置；来源新鲜度/降级/连续异常。
- API路径/DTO/权限/成功失败JSON/公开Handler与Health适配；五管理路由、任务/计划/详情弹层和三端现有工作台摘要线框。
- S01～S05/H01～H03 → 内部TASK-PF07-001～006 → A01～A20需求验收映射。

## 明确未闭合事项

| 项目 | 状态与下一步 |
| --- | --- |
| G07-1 | 引擎首选沿历史Hangfire；包版本/三Provider存储/底层完整字典/受控DDL/恢复探针尚未核验，不能由9张业务表冒充全部引擎存储 |
| G07-2 | 各模块公开Health/审计/业务适配实际缺口待冻结；源码阅读不是运行契约验收 |
| G07-3 | 实际隔离库、双副本、故障注入与页面验证环境待登记；本轮未启动环境 |

## 本轮验证边界

文档核验覆盖本地链接存在、Markdown代码块/表格结构、9表/6卡/5管理路由/20案例、九字段完整性、JSON样例可解析和需求映射，以及修改范围/空白；不把文档结构检查当业务测试。

| 本轮实际命令/检查 | 结果与覆盖边界 |
| --- | --- |
| PowerShell here-string脚本传给`node -`，只用fs/path标准库检查4份新增文档 | exit 0；17个本地链接存在，29张Markdown表列数一致，代码块闭合，3段JSON样例解析成功；9业务表、6任务卡各9字段、5管理路由、20唯一验收案例及8需求映射齐全；failures=[] |
| 第二次`node -`脚本检查5份修改文档的git diff新增行 | exit 0；17个新增本地链接存在；`git diff --name-only -- src tests`为空；原PF06A文件仍只有1新增/0删除且末尾“怎么”保留；failures=[] |
| `git diff --check` | exit 0；无空白错误，Git只提示现有LF/CRLF自动转换规则 |
| 主控语义自查 | 修正5管理页计数、恢复不自动复活计划、Unknown授权处置/旧进程核验、新鲜度来源时间及响应幂等快照大小；未做源码或运行验收 |

源码依据为本轮实际读取的Entity/ControlPlaneContracts/模块注册/迁移/种子/对账/Health响应及前端黄金组件、路由。技术资料仅使用[Cronos官方说明](https://github.com/HangfireIO/Cronos)和[Hangfire SQL Server官方文档](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html)，用于说明候选能力与必须核验的自动DDL/存储边界，不是已选包的验收证据。

## 保留与未执行

检查起点HEAD为`5dfea10`；本地develop相对本地记录origin/develop领先5提交，未fetch核实实时远端。原`details/PF06A-运行时字段与页面规格.md`末尾“怎么”是既有未提交编辑，本轮保留。

未修改生产代码/数据库配置，未派遣或创建外部任务，未运行PoC/业务build/test/页面验收，未启动/停止IDE或服务，未暂存/提交/推送。PF-06A历史1915测试通过及旧阶段延期项不作为PF07证据。
