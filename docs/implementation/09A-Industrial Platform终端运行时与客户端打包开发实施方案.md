# Industrial Platform 终端运行时与客户端打包开发实施方案

版本：V2.0；日期：2026-09-20；阶段：PF-06A；状态：Web 扫码和更新协调器基础已合入，客户端原生与 SQL Server 范围继续开发，真实工具链/设备/数据库待核验；当前指派见 `docs/tasks/active/PF-06A.md`。

## 1. 本次做什么，怎样阅读

本文合并原实施方案、运行时明细、运行时字段字典和SQL Server补充规格，作为**唯一详细开发文档**。旧文件只保留跳转，不再分别维护设计。先看页面及测试，再按需要查接口和字段。

| 阅读目的 | 直接阅读 |
| --- | --- |
| 了解范围和开发顺序 | [§2 三个任务](#2-只保留三个开发任务) |
| 实现扫码控件、页面并人工测试 | [§3 线框、属性和操作步骤](#3-共享扫码控件与测试页面) |
| 实现热更新并验证实际效果 | [§4 简化更新规则](#4-简化更新流程与恢复)，重点看[§4.5测试准备](#45-task-pf06a-005的测试准备) |
| 接页面与原生能力 | [§5 方法、身份及设备规则](#5-方法签名与业务边界) |
| 查每个字段的含义与维护方式 | [§6 逐对象字典](#6-运行时数据与逐字段说明) |
| 接现有工程、打包和发布 | [§7 工程接入](#7-工程接入原生生命周期和发布) |
| 开发前核对输入、整体验收 | [§8 输入核对与验收](#8-开发输入核对与整体验收) |
| 接入SQL Server 2019/2022 | [§9 数据库接入](#9-sql-server-20192022接入) |
| 核对交付和技术参考 | [§10 交付说明](#10-交付说明与技术参考) |

**交付范围：** 同一Vue业务层运行于Web、Windows Electron和Capacitor Android。新增一个共享扫码输入控件和一个共享扫码测试页面；本次Android只用真实手机验证输入与拍照扫码。PDA厂商广播、专用SDK/Intent协议、接收器及PDA真机验收不属于本次交付，后续如有真实设备再单独立项。控件可保留`broadcast`模式属性作为页面接口，但只做组件行为测试，不能形成PDA支持声明。已有BLE、基础打印、协作生命周期和SQL Server接入范围保留，不新增其他业务。

**更新规则：** 使用中保持当前页面版本；重新启动App或退出登录后重新登录时，必须完成检查和必要更新，无“稍后/跳过”。普通后台切回、托盘恢复、有效Token刷新不触发更新。Vue/HTML/CSS/JS页面变化走Web资源更新；JAR/AAR、SDK、原生插件、权限或外壳变化走完整安装包。

**前置说明：** 复用PF-05/06已确认的Web功能与公开契约，原有后置验收沿既定安排。接入核对实际差异，不复制消息/媒体业务。客户端只经API/Hub访问后端，不直连SQL Server；Runtime不新增业务SQL表或核心Service Host。

## 2. 只保留三个开发任务

按下面三个任务交付，不再把技术核验、公共接口、两个外壳、页面和验收拆成九个阶段。任务内按顺序连续执行，验收随实现进行，不逐小步派遣或等待审批。

| 任务 | 一次交付什么 | 顺序 |
| --- | --- | --- |
| TASK-PF06A-001 客户端与扫码页面 | 共享Runtime、Windows/Android外壳、扫码控件/测试页、既定设备和协作适配、终端诊断 | 先完成最小安装→登录→扫码显示链，再补既有能力 |
| TASK-PF06A-005 更新与终端整体交付 | 两端页面热更新、安装包升级，以及用实际扫码页完成更新和终端回归 | 接001的可安装客户端；测试方法直接见§4.5～4.7 |
| TASK-PF06A-009 SQL Server兼容接入 | 当前平台服务支持SQL Server 2019/2022，保留PostgreSQL/SQLite | 可独立推进，共享文件按顺序交接；与范围外的PDA广播无关 |

旧编号只用于追溯：原001/002/003/004/006/007合入现001；原005/008合入现005；009保留。后续不再派遣旧002/003/004/006/007/008。

### TASK-PF06A-001 客户端与扫码页面

- **状态：** 待开发；实际工具链、设备及认证输入按§8.1核对。
- **目标：** 用户可安装客户端、登录、打开扫码测试页，输入或拍照扫码后立即在下方看到结果并清空。
- **输入文档：** 本文§3、§5～8，复用现有Web公开契约。
- **依赖：** 实际Windows/Android手机及可用测试服务；不需要PDA设备或厂商广播资料。
- **允许修改范围：** §7.1原生目录、Runtime、共享控件/页面及必要路由、权限和增量导航；不改其他业务边界。
- **预期输出：** 可安装Windows/APK、AppScanInput控件、ScannerTestPage页面、可发现入口、既定设备/媒体适配和诊断说明。
- **实施与验证：** 核验并固定工具链/安装身份 → 安装与真实登录 → §3控件/页面 → 既有能力接入；执行S01～S05、S07～S12及§8相关案例。S06为范围外说明，不纳入完成条件；广播替身只验证控件属性处理。
- **结果回写：** 本文实际版本/支持矩阵、字段注释和evidence；缺设备、未支持及失败分开记录。
- **提交策略：** 任务内连续开发/自测，稳定交接后按仓库流程集成，不逐内部步骤提交。

### TASK-PF06A-005 更新与终端整体交付

- **状态：** 待开发；更新工具、签名映射及实际渠道按§7.8～7.9核验。
- **目标：** 用户正在操作不受页面发布影响，重开必须更新；页面变更不重新安装外壳，原生变化明确下载安装包。
- **输入文档：** §4全章、§6发布/状态字段、§7构建发布、§8整体验收。
- **依赖：** 001的可安装客户端及扫码测试页；同一测试环境的版本资源与受信清单。
- **允许修改范围：** 两端更新器、启动界面、现有Web构建/分发和受限bootstrap；不新建发布管理服务。
- **预期输出：** 两端页面更新、Windows/APK升级、失败恢复、发布命令和可复现实测记录。
- **实施与验证：** 先准备N1/W1基线 → 只改扫码页发布W2 → 执行U01～U09 → 再发布外壳N2执行U10～U12 → 跑§8整体回归。每例均记录实际版本和用户可见结果，不以文件生成或Mock调用代替。
- **结果回写：** §4测试表的实际包/hash/截图/结果、支持矩阵、操作手册和evidence。
- **提交策略：** 沿001相同交接流程；终端交付不抵算009数据库支持。

### TASK-PF06A-009 SQL Server兼容接入

- **状态：** 待接入细化、待真实环境核验。
- **目标：** 当前服务在真实SQL Server 2019/2022中初始化、运行、升级与恢复。
- **输入文档：** §9全章，特别是§9.6.1接入顺序、§9.6.5字段说明与§9.7案例。
- **依赖：** 两种真实引擎的隔离环境、认证/TLS/排序规则和数据库拓扑；不依赖终端设备。
- **允许修改范围：** §9.6所列数据库/拓扑/初始化/仓储及必要配置模板、测试；不迁移现用库。
- **预期输出：** Provider、逐服务SQL适配、初始化/运维执行链、字段字典、版本支持说明。
- **实施与验证：** 按§9连续接入，分别执行2019/150与2022/160的SQL-A01～10及PG/SQLite回归。
- **结果回写：** 本文§9精确字段/配置/迁移与evidence；源码注释、数据库说明和字典同步。
- **提交策略：** 保持共享文件单一写入者，完成自测后稳定交接，不逐数据库适配步骤提交。

## 3. 共享扫码控件与测试页面

### 3.1 控件外观：标题、文本框、拍照入口

新增共享控件，建议名称`AppScanInput.vue`。复用项目已有基础输入框、图标按钮及主题，不再造一套表单框架。参考用户图片的**横向排列关系**：左侧标题、中间文本框、右侧拍照扫码图标；标题是标签，不是按钮。

```text
输入框模式，显示拍照扫码
┌──────────┬────────────────────────────┬────────┐
│ 扫描条码 │ 请输入或扫描条码            │  扫码  │
└──────────┴────────────────────────────┴────────┘
   标题              文本框                图标按钮

广播模式（右侧没有拍照入口）
┌──────────┬─────────────────────────────────────┐
│ 扫描条码 │ 等待设备扫码…                       │
└──────────┴─────────────────────────────────────┘

输入框模式，隐藏拍照扫码
┌──────────┬─────────────────────────────────────┐
│ 扫描条码 │ 请输入条码                          │
└──────────┴─────────────────────────────────────┘
```

文字“扫码”在实现中使用与参考图同类的取景框图标，提供“拍照扫码”的本地化可访问名称。手持端控件及按钮最小48px；标题紧凑固定区域、文本框占剩余宽度，图标隐藏后不留空白。深浅主题沿项目变量，不硬编码截图颜色。

### 3.2 属性、事件与两种模式

`inputDisabled`只控制**手工编辑文本框**，不停止广播接收，也不禁用独立的拍照入口。这样广播模式下禁用输入框仍能显示扫到的值。要停止接收，由页面离开/取消扫描会话处理，不把“文本框禁用”混同“整控件停用”。

| 属性 | 类型 / 默认 | 人工接手说明 |
| --- | --- | --- |
| title | string；本地化“扫描条码” | 左侧标题，由使用控件的页面传入；不作为设备或业务标识 |
| modelValue | string；默认空串 | 文本框当前值，v-model双向绑定；正在编辑的草稿不等于已确认结果 |
| mode | 'broadcast' / 'input'；默认'input' | 选择广播接收或输入框模式；它是控件交互模式，不是Runtime source枚举 |
| inputDisabled | boolean；默认false | true时禁止手工编辑/键盘输入，广播或拍照的有效结果仍可写入文本框 |
| showCamera | boolean；默认true | 仅input模式有效；broadcast模式下无论传什么都不显示拍照入口 |
| ownerPageId | string；必填 | 当前已登记测试页/业务页标识，供Runtime取得扫描所有权；不是显示标题 |

| 事件 / 方法 | 参数 | 触发规则 |
| --- | --- | --- |
| update:modelValue | string | 手工编辑或有效扫描写入文本框时发出；不能直接用它更新结果Label |
| result | ScanResultV1（字段见§6） | 一次确认输入或一次有效扫描只发一次；页面取value更新Label；同码两次真实扫描都发出 |
| error | {code:string, retryable:boolean} | 输入无效、权限拒绝、能力不支持或设备错误；仅显示本控件提示，不覆盖旧有效结果 |
| clear():Promise<void> | 无参数；页面调用 | 立即清空草稿并使旧回调失效，再取消在途相机/释放旧扫描会话并恢复当前模式；Promise在清理/恢复处理结束后完成，失败发error，结果仍保持清空 |

```vue
<!-- 测试页首轮默认；这是开发规格示例，不表示代码已存在 -->
<AppScanInput
  ref="scanInput"
  v-model="inputValue"
  :title="t('runtime.scanner.title')"
  mode="input"
  :input-disabled="false"
  :show-camera="true"
  :owner-page-id="currentPageId"
  @result="lastBarcode = $event.value"
/>
```

| 模式 | inputDisabled | showCamera | 预期 |
| --- | --- | --- | --- |
| broadcast | false | false或true | 文本框可编辑；广播直接接收；拍照入口始终隐藏 |
| broadcast | true | false或true | 文本框不可手工编辑；广播仍更新文本框和结果；拍照入口始终隐藏 |
| input | false | true | 可输入、可按Enter/软键盘完成确认，也可点击拍照扫码 |
| input | false | false | 仅输入确认；不显示拍照入口 |
| input | true | true | 禁止手工输入，仍可点拍照扫码；结果显示到禁用文本框及Label |
| input | true | false | 只读显示已有值；不能从键盘/相机产生新结果，清空仍可用 |

mode由当前调用页面的属性决定，不自动覆盖终端全局scannerMode；修改modelValue只更新草稿，不自动产生result。测试页清空时先把lastBarcode置空，再调用控件clear()，恢复会话期间不接收旧结果。

输入规则与Runtime保持一致：确认时才产生result，输入过程中不更新Label；Enter/软键盘完成默认确认，IME组合中不确认，也不误提交外层表单。若使用现有扫码配置的Tab/None结束符，Tab按原焦点规则确认；None时文本框内提供明确“确认”动作，不按延时猜输入结束。空值不提交，最多4096字符，保留前导零和大小写，不默认trim或转数字。广播模式输入框启用时允许人工确认，结果source仍为keyboard，不能伪称广播。

拍照入口显示条件只由mode/showCamera决定；当前容器不支持相机或未验证时显示禁用按钮及原因，不能点了无反应。首次点击再申请OS权限，取消不产生result，拒绝时保留旧Label并提示设置/重试。连续点击只启动一次相机。

控件通过Runtime管理一个活动扫描会话。input模式使用keyboard路径；点击拍照时暂停原会话并取得camera会话，完成/取消后恢复输入路径。broadcast模式使用广播路径，手工确认走本地keyboard结果标准化，不开启第二个设备receiver。切mode、clear、离页、锁屏或换人时取消旧上下文；迟到事件按sessionNId/subjectEpoch拒绝。广播接收不依赖输入框获得焦点。

### 3.3 测试页面线框与入口

新增**一个共享扫码测试页面**，PC/PDA仅路由薄入口不同。页面使用上面的同一控件，扫码成功直接显示最近一次条码；不建历史表、数据库或后台接口。

```text
┌──────────────────────────────────────────────────────┐
│ 扫码测试                                      返回   │
│                                                      │
│ ┌──────────┬──────────────────────────────┬────────┐  │
│ │ 扫描条码 │ 请输入或扫描条码              │  扫码  │  │
│ └──────────┴──────────────────────────────┴────────┘  │
│                                                      │
│ 扫码结果                                             │
│ ┌─────────────────────────────────────────────────┐  │
│ │ 000123ABC                                       │  │
│ └─────────────────────────────────────────────────┘  │
│ Label显示区（无输入光标；初始显示“暂无条码”）          │
│                                            [清空]    │
└──────────────────────────────────────────────────────┘
```

- 成功：控件发result后，页面把value作为纯文本显示在下方Label，同时文本框显示相同值；只显示最新一次，长条码换行。
- 清空：同时清空文本框草稿和Label；Label恢复“暂无条码”，不是清除服务器数据。清空前在途结果不能复活，清空后新的扫码仍正常显示。
- 取消/失败：本次不产生结果，保留上一次有效Label；错误在控件附近局部显示，不用全页遮罩。
- 返回/离页/换人：释放当前输入/相机/广播会话并清空本页内存，不写日志、URL、localStorage或数据库。条码通过文本插值显示，禁止v-html。
- 页面不放生产参数编辑器。测试不同属性组合用组件挂载用例或专用测试构建设置props；正式页面首轮默认input、inputDisabled=false、showCamera=true。后续PDA页调用同一组件传broadcast，不复制控件。

| PageId / 命名路由 | 路径 | 入口与权限 |
| --- | --- | --- |
| pc.runtime.scanner-test / pc-runtime-scanner-test | /pc/terminal/scanner-test | 终端与版本→测试扫码；页面继承runtime.read，实际测试要求runtime.configure |
| pda.runtime.scanner-test / pda-runtime-scanner-test | /pda/terminal/scanner-test | 同上；Android手机首轮使用此入口，48px触控 |

组件放`src/frontend/src/components/runtime/AppScanInput.vue`，共享页面放同目录`ScannerTestPage.vue`，在现有PC/PDA pages及routes登记薄入口和中英文标题。沿既有终端菜单进入，不另建测试后台服务/权限体系。普通业务日后复用控件时使用所属业务权限，不要求操作员拥有终端配置权限。

### 3.4 页面状态字段

| 字段 / 类型 | 来源与修改时机 | 含义及限制 |
| --- | --- | --- |
| inputValue:string；初始'' | v-model随编辑/扫码变化，清空时置空 | 当前输入草稿；不能把每次输入当成功条码 |
| lastBarcode:string；初始'' | 仅result更新，清空/离页清除 | 下方Label的最近一次有效条码；空串显示“暂无条码” |
| currentPageId:string | 由当前PC/PDA路由固定 | 控件所有权来源；不接受query传任意页面标识 |
| component ref | 页面挂载时取得，卸载清除 | 只用clear()清理控件，不读取其原生全局对象 |

组件内部相机请求、sessionNId和subjectEpoch沿§5/6，避免在页面复制第二套扫描状态。控件文本值与业务确认结果分开，有利于人工接手时理解“正在输入”和“已扫描”。

### 3.5 扫码控件与页面怎么测试

人工最小流程：安装Android测试APK → 完成启动检查/登录 → 终端与版本→测试扫码 → 输入`000123ABC`并确认 → 下方显示相同值 → 清空 → 点右侧图标拍摄同一条码 → 下方再次显示 → 返回后不再收到本页扫描结果。

| 案例 | 操作 | 必须看到的结果 / 证据 |
| --- | --- | --- |
| S01 输入与确认 | 输入000123ABC，确认前观察Label，再按Enter/软键盘完成 | 编辑时Label不变；确认后原样显示前导零和大小写；外层表单未误提交 |
| S02 手机拍照扫码 | 用真实Android手机点图标，授权并扫实际条码 | 文本框与Label一致；相机关闭；再次点击可以继续扫描，记录实机结果 |
| S03 禁用输入仍可拍照 | input / inputDisabled=true / showCamera=true | 键盘无法编辑，拍照仍可成功并更新两处 |
| S04 隐藏拍照 | input模式，showCamera=false，分别启用/禁用文本框 | 无图标且无占位空白；启用时可输入，禁用时不能新增输入 |
| S05 广播模式属性 | broadcast模式，遍历inputDisabled和showCamera两种布尔值 | 共4组合均不显示相机；文本框可编辑性只看inputDisabled；只验证控件属性，不连接PDA广播 |
| S06 PDA广播真实接收（范围外） | 本次不执行、不计入001/005完成条件 | 不开发厂商SDK/Intent接收器，不准备替身冒充真机；以后有PDA时另立任务 |
| S07 清空及迟到结果 | 成功后清空；另在相机等待时清空，再使旧回调到达 | 草稿/Label都清空，旧结果不复活；新的扫码可再次显示 |
| S08 取消和拒绝 | 保留已有结果，取消相机、拒绝权限、撤销权限再返回 | 保留旧Label，提示当前原因；不循环弹授权、不伪造成功 |
| S09 重复与长内容 | 连扫相同码两次、重放同event一次；测4096/4097字符及HTML样式文本 | 两次真实扫描发两次result，重放不重复；超限明确拒绝；内容按纯文本显示 |
| S10 切模式/离页/换人 | 延迟旧扫码回调，再切mode、返回页面或换用户 | 旧会话结果不写新页面/新用户；无重复receiver，资源释放 |
| S11 相机不可用/重复点击 | 在不支持容器显示图标，或手机快速双击图标 | 不支持有原因；支持时只一个相机请求，不启动两次 |
| S12 真实入口与布局 | 最低权限账号、直接访问路由、两语言/两主题、窄屏/软键盘 | 权限接收端有效；标题/输入/图标不挤压，Label换行，清空可点，旧聊天Enter行为保持 |

组件级用例覆盖全部8个属性组合（broadcast 4个、input 4个）；真实手机覆盖S01～04、S07～12。S06明确排除在本次交付之外；模拟注入只能证明控件属性规则，不能记为PDA扫码支持。S09同码事件次数由组件事件断言验证，不为测试在产品页增加计数器或历史列表。

### 3.6 终端与版本（既有设计）

```text
┌ 终端与版本                                         刷新 ┐
│ 当前客户端版本 / 页面版本 / 运行状态                    │
│ 更新：当前版本 / 检查结果 / 下次启动时更新               │
│ [检查更新]（仅查看，不在业务期间切换版本）               │
├ 设备名称 / 能力 / 权限 / 状态                  配置 更多 ┤
│ 扫码器   支持/未授权         [测试扫码]                 │
│ 蓝牙设备  已连接/断开        [连接/断开]                │
│ 系统打印  支持/未配置       [打印测试页]                │
└ 诊断导出入口（脱敏）                                    ┘
配置抽屉：支持模式 / 结束符 / 受控设备 → 取消 / 保存
```

路由`/pc/terminal`、`/pda/terminal`，菜单“终端 / 终端与版本”；runtime.read控制查看，runtime.configure控制配置，runtime.update控制业务内手动检查；启动强制更新由受信bootstrap执行，不依赖登录权限。系统安装授权另行校验。Web模式仍显示当前Web版本与Unsupported能力，不展示假安装包更新。设备列表selection=none,compact，状态主页面不机械提供导出/分组。参考1440×900/360×800，窄屏上下排列，PDA48px触控。

### 3.7 终端页面的路由与权限

| PageId / 命名路由 | 路径/入口 | 数据和动作 |
| --- | --- | --- |
| `pc.runtime.terminal` / `pc-runtime-terminal` | `/pc/terminal`，终端→终端与版本 | W06A-01；1440×900、1280×720及200%缩放；复用AppPage/AppDataTable compact，selection=none |
| `pda.runtime.terminal` / `pda-runtime-terminal` | `/pda/terminal`，PDA功能入口→终端与版本 | 同W06A-01，360×800；纵向分区、48px触控；不套PC多列工具栏 |
| `runtime.scanner.settings` / 无独立路由 | W06A-01“扫码配置”抽屉 | AppFormDrawer居中/侧滑偏好；scannerMode下拉只含已核验能力、suffix枚举、diagnosticConsent开关；expectedRevision隐含随快照提交 |
| `pc.runtime.scanner-test` / `pda.runtime.scanner-test` | 终端与版本→§3.3独立扫码测试页 | 使用AppScanInput；结果仅内存显示，返回清空；精确路由/权限见§3.3 |
| `runtime.update.startup` / 内置启动入口 | App冷启动或重新登录，业务装配之前 | 当前/目标版本、进度和说明；Web更新自动进行，外壳更新提供下载安装，失败重试/退出；无稍后/跳过，不注册业务菜单 |
| `runtime.update.recovery` / 内置恢复入口 | W06A-01失败区或Vue无法加载时原生恢复界面 | 当前/目标版本、错误说明、重试/重新安装/联系支持；不暴露任意shell/下载URL输入 |
| `runtime.screen.source` / 无业务路由 | 仅Electron用户触发分享、系统选源不可用时W06A-03 | 见§3.8；不注册为SystemData菜单，不独立运行聊天/媒体Host |

Mobile Web继续已有三端业务入口；首版手机安装同一Android APK并使用既有PDA布局及`/pda/terminal`；不新增`/mobile/terminal`、独立Mobile包或iOS包。PC/PDA浏览器打开终端页可看Web版本、实际浏览器能力和不支持原因，不展示原生更新成功假数据。

`runtime.read`为Page权限，`runtime.configure`、`runtime.update`为Action权限，精确登记进Identity PermissionCatalog/BootstrapSeedCatalog、前端生成目录和权限常量。业务扫码/BLE仍按所属业务动作权限，不要求业务操作者拥有终端配置权限；终端页的测试扫码、测试连接、打印测试页和诊断导出要求runtime.configure。能看runtime.read不等于可以使用设备。启动强制更新是安装实例的维护流程，不受业务菜单/动作权限开关影响。

SystemData导航资源稳定NId分别为`systemdata.navigation.pc-runtime-terminal`与`systemdata.navigation.pda-runtime-terminal`，绑定相应route name/runtime.read/终端类别；菜单分组只创建一次。用新增幂等种子版本upsert本阶段受管条目，不重放/覆盖已有自定义菜单，不改历史checksum。实际增量ID、顺序、父菜单及已有种子调用点在001核对后写入§8.1认证输入；不得以当前文档的资源NId冒充已应用种子版本。

| 页面区域/字段 | 唯一数据来源 | 加载、错误与动作规则 |
| --- | --- | --- |
| 容器/OS/Native/Web/bridge版本 | capabilities.get | 页面打开局部骨架；未知显示“尚未取得”，不以package.json前端版本代替宿主实际版本 |
| 当前环境/通道/扫码设置 | config.get | 环境/通道只读，服务器/证书由部署配置；配置冲突保留草稿，提示重读比较 |
| 下载/校验/目标/失败原因 | updater.status + onChange | 进度局部更新，total未知用不确定进度；重复按钮同步guard；状态退回只允许当前updateEpoch/stateRevision |
| 设备行状态 | devices.list + onChange | row-key稳定，不预选；连接错误只影响本行；刷新列表不能中断进行中的业务操作 |
| 业务中检查更新 | updater.check/status | read可看、update才可手动检查；无支持隐藏原生动作；发现更新只提示下次启动更新 |
| 启动下载/校验/安装 | 受限bootstrap updater方法 | 登录前可用，仅允许内置信任目标；完成前无业务入口，失败重试/退出 |
| 扫码配置/测试/打印/导出 | §5.2方法 | configure权限；OS授权另外必需；返回后检查新状态而非先改UI为成功 |
| 权限拒绝/永久拒绝 | 能力reasonCode | 提示恢复步骤与受控设置入口，刷新不弹授权；无权限不发原生副作用调用 |

首次启动“有包无可用配置/服务不可达”显示本地版本、环境标识及“联系管理员配置/重试连接”，不默默选择localhost、不允许未登录网页改apiOrigin。UI文案只显示操作员需要的环境名与处理建议，技术错误详情进入脱敏诊断。首次配置由受控安装配置提供，首版不增加任意服务器编辑表单。

### 3.8 选源与更新恢复界面

```text
W06A-03：选择要共享的内容（仅系统选择器不可用时）
┌ [屏幕] [窗口]                           取消 ┐
│ 来源缩略图 / 可辨认标题；默认不选择           │
│ 本次共享仅发送选中画面，语音沿原聊天控制       │
└ 来源失效：请重新选择            [开始共享] ┘
W06A-04：客户端需要恢复（内置资源，不依赖故障Bundle）
┌ 当前客户端版本 / 页面版本 / 当前仍可用状态    ┐
│ 更新失败原因 / 已恢复文件或需要重新安装        │
│ [重试] / [退出] / [重新安装说明] / [导出诊断]  │
└ 不要求先加载坏页面；不开放任意链接和命令      ┘
```

选源只有用户确认才返回当前请求有效source句柄；关闭弹层/源窗口消失/超时/邀请结束即取消并清缩略图。来源列表不写日志，不能默认选第一屏。远端观看继续PF06页内浮窗：缩放后拖动保持DOM实际尺寸，最小化有重新展开入口；多显示器/DPI变化后窗口边界可达。恢复页是宿主安全操作入口，离线也可看版本和说明；不因此绕过清单验签或启动必更，恢复旧文件不等于允许旧版业务。普通业务诊断要求权限；无法登录的恢复界面仅允许本机显式同意导出宿主启动/更新脱敏记录，不读取旧用户聊天、Token或业务文件。

## 4. 简化更新流程与恢复

### 4.1 用户能看到的流程

首版统一采用“使用中保持当前版本，重新启动必须更新”的方式。Windows与Android共用下表，不保留使用中激活或稍后升级的分支。

**首期实现上限：** 更新器只维护“当前Web包、上一个已健康Web包、一个下载暂存包”三个资源位置，加一个固定60秒健康确认。只保存完成中断恢复所需的当前目标、hash、一次激活标识、截止时间和错误码；不建设更新策略中心、版本历史库、审计平台、多级回滚、灰度/班次编排、后台预下载或断点续传。`lastGood`只用于打开修复界面，不能绕过本次必更进入旧业务。清单验签、文件hash和外壳兼容校验属于必要安全检查，不扩展成独立安全版本治理系统。

| 场景 | 应执行的行为 |
| --- | --- |
| 正在业务页面使用 | 本次运行固定当前Web资源版本；发布新版不刷新、不切换、不发起自动下载，不因更新打断扫码、表单、文件或媒体 |
| 冷启动 / 系统回收后启动 / 业务renderer重建 | 先检查更新，再加载可进入业务的版本；自动登录和通知冷启动不能绕过 |
| 后台切回 / 锁屏恢复 / 托盘恢复 | 同一存活实例按§7.4恢复，不触发启动更新；版本切换不作为resume动作 |
| 退出登录后重新登录 | 先完成原有登出清理，再走同一更新检查；相同身份的有效Token刷新不触发 |
| 有Web更新且外壳兼容 | 显示当前/目标页面版本与进度，自动下载、校验、切换、健康确认后继续登录 |
| 外壳版本不足或存在必需安装包 | 显示客户端升级说明、大小和“下载安装”；系统安装完成并重启核对实际版本后再检查页面版本 |
| 无更新 | 检查成功且当前版本符合发布清单后继续登录/恢复身份 |
| 检查、下载、校验失败 / 用户取消OS安装 | 显示可理解原因及“重试 / 退出”，安装权限问题给受控设置说明；不进入旧版业务，无稍后或跳过 |

重新登录和重启共用一个检查入口；不建设服务端业务版本准入系统。版本检查失败不能当“没有更新”。无网络时可以看本地版本、失败原因和修复说明，首版不提供离线业务放行。

`Idle → Checking → Available → Downloading → Verifying → Staged → Activating → Healthy`只是一次启动更新的内部界面状态，不形成业务工作流或历史记录。无更新由Checking回Idle并放行；失败到Failed；Web资源启动失败到`RollingBack → RecoveryRequired`。Idle只表示更新器空闲，是否允许进入业务由本次检查结果决定，不能仅根据旧状态文件的Healthy放行。

### 4.2 启动检查与业务之间的边界

1. 宿主在启动时建立内部更新检查阶段；只允许内置更新界面调用受限检查/下载/安装能力。更新完成前不装配业务Router/Hub、扫码会话、打印或媒体；健康确认只检查本地Vue资源和bridge，不依赖登录或API联网成功。
2. 已进入业务后，终端页可查看版本、有权限可手动检查；即使发现新版也只提示“下次启动时更新”，不提供下载、切换或立即重启按钮。宿主接收端同样拒绝业务阶段的download/activate，返回UPDATE_BUSY，不能只在页面隐藏按钮。
3. 重新登录的更新检查发生在旧身份清理后；原生更新协调器是安装实例级，不使用旧业务Token，也不要求普通操作者拥有runtime.update。成功后再核验新身份，不能借更新入口调用设备/业务方法。
4. 本次检查固定releaseNId与已验证manifest hash；下载中发布新版不能替换本次文件。进入业务前再次检查当前发布目标，若有新目标则继续更新，不能无限使用缓存清单跳过必更。
5. 不再实现跨所有业务的busy lease或“等待空闲后自动更新”。原有退出/登出清理、设备互斥和结果未知语义继续有效；强杀后的物理操作结果不能靠更新器推断，更不能自动补发设备命令。

### 4.3 每步校验、完整性与恢复

| 步骤 | 必须保证的结果 | 失败后 |
| --- | --- | --- |
| 检查清单 | HTTPS及重定向白名单、签名/有效期、appId/环境/通道/架构/版本范围正确；单实例只一个更新所有者 | 保留原文件，显示检查失败；不进入业务 |
| 下载 | releaseNId与清单hash绑定，只写一个暂存目录；先检查空间 | 失败后删除本次临时文件并从头重试，不做断点续传、不拼坏包 |
| 校验并暂存 | 长度、SHA256、签名绑定及Native/bridge/API兼容通过；解压拒绝绝对路径、穿越、链接及异常大小 | 清理本次无效暂存，保留当前和最后良好资源 |
| 原子切换 | 仅启动检查阶段；再次校验目标及兼容；记录一次activationNId后切换当前slot | 中断后根据当前、lastGood、暂存三处事实进入恢复，不启动半包 |
| 健康确认 | 两端固定60秒；只检查Vue入口、必要资源与bridge，由当前activation回报；不等用户登录 | 失败切回lastGood只打开修复界面，不能进入旧版业务；不做多级回滚 |
| 安装包升级 | 实际安装身份/签名、Android versionCode、Native版本与清单一致；OS“已受理”不算完成 | 取消/安装失败仍阻止业务；只给重试/退出/受控设置，不承诺静默降级 |
| 崩溃后重启 | 核对当前、lastGood、暂存及待确认activation；拒绝旧健康回报 | 未完成激活直接进入修复；同一坏包不自动循环激活；无lastGood时使用内置修复页 |

发布版本与资源文件不可变，保留正在使用的旧资源及其动态chunk；不得用覆盖目录或自动更新的Service Worker让旧页面混入新资源。前后端沿既有兼容接口发布，不通过本任务重建API版本管理。

保留清单签名、releaseSequence防旧清单回放和文件hash校验。同序号只能对应同一产物；首期不建设独立安全下限策略或版本治理中心。lastGood只保证修复入口，不绕过当前发布要求。仅保留内置修复页、当前slot、lastGood和一个暂存slot；不删除业务文件腾空间。

Web包不能改变信任根、API白名单、安装身份、安全存储格式、原生权限/插件或SDK；这些变化走完整安装包。普通本地配置在前后支持版本间保持可读。包大小/解压大小/文件数和空间余量由001按实际工具链冻结。

### 4.4 更新工具核验范围

001核验Electron和Capacitor的Web资源加载/更新能力，并记录清单字段、实际更新工具元数据、产物及健康回报的对应关系。electron-updater仅用于Windows完整安装包，不能视作已提供页面热更新；Capgo仍为候选，未核验不写成已采用。

两端均须证明：无需重新编译外壳可更新页面；使用中及后台恢复不自动切换；启动检查可在登录前完成；下载完整后原子切换；健康失败进入修复；安装包升级后再核验实际版本。禁用工具默认的后台切换、退出自动安装或失败后放行旧业务逻辑，统一由启动协调器决定。

复用现有发布存储/渠道，不新增更新Service Host、灰度平台、班次管理或业务版本准入服务。

### 4.5 TASK-PF06A-005的测试准备

更新测试直接复用§3扫码页，不再新增一套“更新专用控件/业务页面”。扫码测试验证输入到Label的链路；更新测试验证**改这个页面后，已安装App何时看到变化**。更新器的启动提示/进度/失败界面仍按本章实现。

先准备下列受控测试产物；N/W是本节便于阅读的代号，实际清单仍使用§6的NativeVersion/WebVersion和releaseNId。版本仅为示例，正式实验使用未占用的合法递增版本。

| 产物 | 示例版本 | 内容 / 用途 |
| --- | --- | --- |
| 基线安装包N1 | Native 1.0.0；Bridge 1；Android versionCode示例10001 | Windows/APK均内置页面W1，安装并在真实设备登录成功 |
| 基线页面W1 | Web 1.0.0 | §3扫码页：标题“扫描条码”，默认提示“请输入或扫描条码”，扫码显示与清空可用 |
| 页面更新W2 | Web 1.0.1；仍兼容N1/Bridge 1 | 只改Vue/HTML/CSS/JS：标题为“扫码验证”、增加一行静态说明“请扫描测试条码”，用现有主题变量调整结果区边框；保持扫码行为/权限不变 |
| 外壳更新N2 | Native 1.1.0；Android versionCode示例10100 | 同安装身份/签名的更高版本外壳，复用现有插件；用于验证安装路径，不为测试新增无用JAR/业务能力 |
| 错误资源 | 专用隔离发布空间 | 准备坏hash/坏入口、外壳不兼容、不可达下载地址；不覆盖正常发布产物 |

准备动作：

1. 001交付的Windows与Android手机安装N1，确认终端页实际Native/Web版本及扫码页W1外观。首次安装配置、服务和测试身份可用，不靠mock登录。
2. 固定一次真实条码`000123ABC`，保留扫码成功截图。记录安装包hash、已安装版本、OS/WebView、运行中的Web版本及清单来源。
3. 用**同一前端构建/发布流程**生成W2的Web站点资源和两个容器适用的Web Bundle，先上传不可变资源再发布清单。N1安装包/APK文件保持原hash，不能重新安装App后声称热更新成功。
4. 每个测试前写明起始Native/Web/清单目标；只有对应案例需要时切换隔离发布目标。中断、清理和重装仅操作本任务测试实例，不影响用户IDE/现用服务。
5. 实际构建、发布、安装命令在选定工具链后记在交付记录中；当前未建工程，不预写不存在的package脚本或假地址。

### 4.6 页面热更新的逐步验收

以下W1→W2在Windows和Android手机**分别执行**。U01～U04应连续保留同一已安装N1实例，才能证明更新时机；换安装包不算页面热更新。

| 案例 | 从什么状态开始 / 操作步骤 | 预期结果与检查点 |
| --- | --- | --- |
| U01 使用中发布 | N1/W1打开扫码页并显示000123ABC；发布W2；继续输入/拍照扫描另一条码 | 页面仍为W1标题/布局，旧结果及当前操作不被更新清空；新扫描仍正常；无更新遮罩/强制刷新 |
| U02 普通返回前台 | 延续U01，Android切后台再返回；Windows最小化/托盘隐藏再恢复，确保进程和renderer存活 | 仍是W1，不触发强制更新；设备/媒体依原生命周期恢复，不把后台停止相机误判为更新打断 |
| U03 真正重开 | Windows用“退出”结束实例再开；Android在隔离测试中结束App进程再开，不只按Home | 先显示检查/目标W2/下载校验进度，无稍后/跳过；完成后登录或恢复身份。Native仍N1，Web为W2，标题/说明/边框已变化 |
| U04 更新后原生回归 | 在N1/W2输入000123ABC、拍照扫描、清空，再重开一次 | 页面新样式与原生扫码共存；Label正确且可清空，重复启动不重复下载同包；实际APK/外壳未变 |
| U05 重新登录与Token刷新 | 分别准备N1/W1会话和W2发布；一次保持同用户刷新Token，一次明确退出再登录；另测记住登录的冷启动 | 有效Token刷新不切版本；退出再登录必须更新；自动恢复登录不能绕过冷启动检查 |
| U06 断网及下载中断 | 启动检查前断网；恢复后下载W2过程中断网，再重试 | 检查失败不当无更新；提示原因、重试/退出，不进旧业务；重试不拼坏包，进度与实际状态一致 |
| U07 坏包与空间不足 | 隔离发布坏hash/缺入口资源；另模拟更新暂存空间不足 | 执行前拒绝坏包；启动健康失败在60秒窗口内进入恢复；旧文件保留但不放行旧业务。不得删除用户业务文件腾空间 |
| U08 切换中止与清单变化 | 在暂存/切换/健康确认前分别结束隔离实例再开；下载时发布新的有效目标 | 不加载半包、不循环重启同一坏包；旧健康回报无效；不混合两个版本文件，完成进入前按当前目标检查 |
| U09 桥接兼容 | N1安装实例收到声明需要更高Native/Bridge的页面包 | 不尝试调用不存在的原生接口；有匹配安装包时进入安装包流程，否则明确需新客户端并停留更新页，无旧版业务入口 |

U07的网络故障与包故障分开：本地资源完整但API不通，不应误判Web包损坏；启动版本检查本身失败仍不能进入业务。后台/锁屏原有相机/媒体清理不因“使用中不更新”取消。

### 4.7 完整安装包升级与交付证据

| 案例 | 操作 | 预期结果 |
| --- | --- | --- |
| U10 正常外壳升级 | 在N1实例发布N2，保持业务时观察，再真正重启；按提示下载并走Windows/Android系统安装；安装后重新打开 | 使用中不安装；启动时明确是客户端升级；实际Native=N2、Android versionCode递增且签名连续；重新检查Web版本后进入，扫码/清空回归通过 |
| U11 取消/拒绝安装 | 启动升级后取消系统安装、拒绝安装权限，再返回App | 仍在升级/失败页面，只能重试、设置或退出；不能以OS弹窗已关闭当升级成功，更不能进旧业务 |
| U12 错误安装包 | 隔离环境测试不同安装身份/错误签名/Android低versionCode或损坏包 | 应用/系统拒绝，原文件与受控恢复入口保留；错误原因明确，不承诺自动静默降级 |

每个U案例记录：平台、起始Native/Web、目标releaseNId/hash、实际操作、更新界面截图、完成后的真实Native/Web、扫码Label/清空结果、PASS/FAIL/未测原因。U01必须有“新版已发布而当前页仍W1”的证据；U03必须有“同一N1外壳显示W2”的证据；U10必须有系统安装后读取到N2的证据。

若以后真实变更JAR/AAR/SDK/原生权限/bridge，用同一安装包流程验新增能力。首轮不为验证更新机制制造额外硬件依赖。005完成条件是U01～U12对应支持平台通过且§8终端回归完成；PDA广播不属于本次完成条件，SQL支持另由009交付。

## 5. 方法签名与业务边界

TypeScript命令返回Promise；订阅返回unsubscribe。以下是公共运行时契约，不要求Web具备原生能力。默认交互超时10秒，用户系统授权/扫码流程60秒可取消，连接/打印具体超时按adapter冻结；长下载返回受理快照，通过status/订阅取进度，不挂一个跨几分钟的桥接Promise。超时后必须能关联原requestNId，不能自动重发有副作用操作。

| 方法 | 输入 | 输出/约束 |
| --- | --- | --- |
| capabilities.get | 无 | CapabilitySnapshotV1 |
| scanner.acquire | {ownerPageId,source}；subjectEpoch取请求信封 | {sessionNId}；忙返回DEVICE_BUSY |
| scanner.release | {sessionNId} | {released:boolean}；重复release幂等 |
| scanner.onResult | 当前sessionNId | ScanResultV1流；主体/会话不符丢弃 |
| camera.scanOnce | {sessionNId,formats:string[]} | ScanResultV1或用户取消；不连续偷摄 |
| bluetooth.requestDevice | {profileNId} | {deviceHandle,capabilities:string[]}；OS权限必经合法用户交互 |
| bluetooth.connect / disconnect | {deviceHandle} / {connectionNId}；requestNId取信封 | connect返回{connectionNId,state:'Connected'}；disconnect返回{disconnected:boolean}，不生成新连接ID；connection互斥，断连幂等 |
| bluetooth.exchange | {connectionNId,operation:adapter受控命令,payload:该命令DTO}；requestNId取信封 | {requestNId,result:对应命令DTO}；不是任意字节/UUID写入通道；工艺解析归adapter |
| files.pick / save | {purpose:白名单,mimeTypes:string[],maxBytes:string} / {artifactHandle,displayName} | {fileHandle,name,sizeBytes:string}；不返回任意绝对路径 |
| printer.getCapabilities | {profileNId} | {protocol,receiptLevel,supportedFormats}；未验证返回Unsupported |
| notifications.show | {eventNId,titleKey,target:{kind:'conversation',nId:key}} | {accepted:boolean}，titleKey限内置本地化键，无敏感正文；宿主绑定当前身份，点击后重新鉴权 |
| navigation.open | {kind:'conversation',nId:key} | {opened:boolean}；映射当前布局已有聊天路由，不执行任意deep link/URL |
| updater.check/download/activate | {} / {releaseNId} / {releaseNId} | UpdateStatusV1；activate仅限§4启动/重新登录检查阶段，进度统一为downloadedBytes/totalBytes |
| lifecycle.onChange | 无 | {state:'foreground'或'background'或'resume'或'identityChanged'或'locked'或'suspending'或'terminating',subjectEpoch,lifecycleSequence:string} |

相同requestNId不同payload返回RUNTIME_REQUEST_CONFLICT；桥接至多一次响应不能当设备物理exactly-once。BLE和Classic SPP分别adapter，支持BLE不能声称支持蓝牙打印。PF06A仅冻结打印能力描述与受控设备边界，PF10B拥有打印作业/回执/重打状态。

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aa","method":"scanner.acquire","subjectEpoch":"1803c808b1ae482490905c923e0e9192","payload":{"ownerPageId":"pda.runtime.scanner-test","source":"camera"}}
```

成功与设备忙反例（两种独立结果，不会同时返回；真实系统验证对应会话前置，不用示例替代授权）：

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aa","ok":true,"subjectEpoch":"1803c808b1ae482490905c923e0e9192","data":{"sessionNId":"6e18863084df4b16b2aa6a7ce6c6ffba"}}
```

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aa","ok":false,"subjectEpoch":"1803c808b1ae482490905c923e0e9192","error":{"code":"DEVICE_BUSY","retryable":true,"outcome":"NotExecuted"}}
```

本例只用手机相机。PDA广播adapter不在本次实现；未来另立任务时必须在真实型号上固定action/category/extras/编码/来源验证规则，不能把本例当通用厂商广播协议。IMEs、键盘楔入与业务回车冲突通过scan-session归属解决，不全局拦截所有Enter。

### 5.1 请求校验、取消、事件与迟到结果

1. 接收端顺序：合法主frame及受信页面→DTO字节数/结构/版本→当前身份epoch→方法允许的权限及目标→请求幂等/设备所有权→调用。Origin匹配须URL解析后精确比较scheme/host/port；不能用字符串前缀、后缀或仅hostname。子frame、销毁窗口、外链页、旧renderer请求均拒绝。
2. 请求上下文键为`{containerInstanceNId,subjectEpoch,requestNId,method}`；instance在每次原生进程启动随机生成。主体/请求ID只在信封一处传输，禁止payload覆盖。请求规范化指纹不存秘密；同ID异method/载荷返回RUNTIME_REQUEST_CONFLICT，同ID处理中复用同一结果，不二次调设备。
3. 每个身份最多64个在途命令；完成记录最多256项、保留10分钟，仅用于同进程短期重入隔离。超过在途上限拒绝新动作RUNTIME_BUSY。调用方不跨重启/淘汰周期自动重放副作用请求；超时后经设备/业务权威查结果或由用户明确重新操作。该内存窗口不是打印作业幂等账本。
4. 取消/超时将请求标为不可再交付业务。底层不能取消时，迟到成功必须立即释放本次取得的扫描/摄像头/连接资源；迟到失败只进入脱敏诊断，不改当前页面错误。取消返回不能声称已撤销物理写入，无法确定结果标Unknown。
5. 订阅包装为`{contractVersion:1,eventNId:nid,eventType:'ScanResult'或'CapabilitiesChanged'或'DevicesChanged'或'UpdateChanged'或'LifecycleChanged',subjectEpoch,sequence:string,payload:对应DTO}`；更新事件另带updateEpoch/stateRevision。序号在同实例/事件源内递增；同源旧事件丢弃，不跨源比较。先安装监听再取快照，使用snapshotSequence/更新修订合并；卸载只移除本订阅，不能removeAllListeners误删别人的监听。
6. 扫码等瞬时事件不为下一个页面或身份补发；更新/连接/身份等状态通过get快照恢复，不以事件日志作为唯一事实。原生异步回调必须捕获启动时的上下文；禁止catch/fallback读取“此刻选中的会话”补来源。错误也按能力/原请求归属清理，不能一处成功抹掉其他错误。

### 5.2 页面闭环所需补充方法

以下与§5原方法一起构成白名单。`Promise`结果仍装入BridgeResponseV1；订阅用§5.1事件包装。尚未支持的方法返回稳定Unsupported，不能返回空成功对象。除bootstrap/身份协调内部端口外，业务方法要求有效登录身份。

| 方法 | 输入 → 输出 | 权限/作用及恢复 |
| --- | --- | --- |
| config.get | {} → {configRevision,terminal,channel,scannerMode,suffix,diagnosticConsent,environmentNId,deploymentEnvironment} | runtime.read；无可编辑服务器/信任根/密钥 |
| config.update | {expectedRevision:string,scannerMode,suffix,diagnosticConsent} → config.get同型 | runtime.configure；仅支持枚举且该模式已核验；有扫描会话则DEVICE_BUSY；不同修订RUNTIME_CONFIG_CONFLICT，不能覆盖 |
| devices.list | {} → {items:DeviceStatusV1[]} | runtime.read；最多100个已配置项，首版不建分页/搜索服务 |
| devices.onChange / capabilities.onChange | {} → 对应快照事件 | runtime.read；OS撤权/蓝牙开关变化后刷新；不自动弹授权 |
| permissions.openSettings | {capability:能力名} → {opened:boolean} | 当前动作对应业务权限；仅受控OS设置页，返回应用后重新检测，不假设已授权 |
| runtime.cancel | {targetRequestNId:nid} → {cancelRequested:boolean} | 仅本人本实例请求；幂等；不能取消别人的操作，也不是物理执行撤销保证 |
| updater.status / updater.onChange | {} → UpdateStatusV1 / 同型事件 | 终端页查看要求runtime.read；手动检查要求runtime.update。启动bootstrap可在登录前检查/下载/激活，仅操作内置受信更新目标，不依赖用户业务权限 |
| updater.confirmHealthy（内部bootstrap专用） | {activationNId:nid,releaseNId:nid,webVersion:string,bridgeContractVersion:number} → {accepted:boolean} | 宿主校验当前加载slot及一次性activation，旧页/任意业务页不能确认；不依赖用户登录成功 |
| printer.printTest | {profileNId:nid} → {submission:'SubmittedToSystem'或'Cancelled',receiptLevel:'SystemOnly'} | runtime.configure；仅Electron固定本地非敏感测试页、copies=1、系统打印对话框，拒绝URL/HTML/原始指令；SubmittedToSystem不等于实际出纸 |
| diagnostics.export | {consent:true} → {artifactHandle:nid,displayName:string,sizeBytes:string} | runtime.configure；每次显式导出、先脱敏，交files.save；不后台上传 |

`files.pick`的purpose首版仅`collaborationAttachment`，mimeTypes/maxBytes还须与现有File/PF05限制取更严格值；`files.save`只消费当前身份有效的句柄，来源限已授权业务下载或诊断导出。句柄绑定epoch/purpose，10分钟无使用失效，单次成功保存后释放；上传结束/取消主动释放。文件内容通过内部adapter接入现有上传/下载流，不塞进64KiB桥接JSON，不另建绕过File授权的下载器。

相机、麦克风、选源和系统文件选择必须由当前明确用户动作触发。JavaScript方法存在不是用户已经同意。清理类方法（release/disconnect/cancel）允许在业务动作权限撤销后释放本人已有资源；彻底登出则由宿主主动清理，不要求过期Token请求先获服务端批准。

### 5.3 身份epoch与资源所有权

- 宿主身份协调器只接收现有认证装配层的会话安装/清除通知，经既有Identity可信身份核验后绑定tenant/user/session；epoch是本地隔离令牌，不能当服务端身份或授权。普通业务页面不能自行提供权限列表、tenant或epoch来切人。
- 初始未认证实例有匿名epoch，仅允许本地版本/修复提示及内部bootstrap，不能扫码、设备写入或进入业务导航。登录完成、登出、换用户、换租户、会话失效或权限失效时推进epoch并撤销旧操作。相同主体有效Token刷新可保留epoch，但必须单飞且拒绝旧刷新回包覆盖新身份。
- 换人顺序：先封住旧操作并更换epoch→立即停止本地捕获/扫描、释放连接/监听/句柄、清除通知和旧导航→清除凭据/业务草稿及内存缓存→完成新身份核验→恢复公共配置和新主体业务。失败留登录/错误页，不把旧用户状态暂借新用户使用。
- 路由退出释放page拥有的扫描/文件选择；App媒体资源由PF06 Host拥有，不随聊天视图卸载结束。更新下载/版本状态为安装实例级，不随换人消失；换人清旧订阅，重新登录按§4完成更新检查；激活仅由受信启动协调器执行。device-profile为受控终端配置，可保留非秘密型号，但新主体不自动继承设备会话或业务授权。

### 5.4 扫码、键盘与Android广播的确定行为

| 场景 | 必须执行的行为 |
| --- | --- |
| acquire / release | 单实例只能一个活动会话；先订阅再让设备开始扫描；模式切换先停旧会话。release幂等；离页/页面失去活动所有权/锁屏/换人注销接收并清缓冲。keyboard输入控件失焦按输入所有权释放；broadcast不因文本框失焦或inputDisabled而停止 |
| 键盘文本框 | 仅标记的当前扫描输入控件接收keyboard结果；普通文本录入仍走原逻辑。IME composing和普通聊天Enter沿PF05规则，不能靠全局按键速度猜扫描并拦截发送 |
| 结束符 | Enter提交当前扫描缓冲且阻止这一次默认提交；Tab提交后依页面焦点规则移动一次；None必须显式“确认输入”提交，不能等待一个不存在的结束字符。浏览器普通聊天无扫描会话时行为不变 |
| 内容与重复 | 保留前导零、大小写、合法GS1分隔符，不任意trim/数字转换；按已冻结adapter去除约定终止符。空结果拒绝，最多4096字符且受64KiB信封上限；相同业务码两次真实扫描生成不同eventNId、都交业务处理 |
| 广播字段（范围外） | 本次不定义、不实现。未来PDA独立任务须基于真实设备填写action/category、extras、编码、来源验证、permission及SDK/固件版本；不能从本次手机实现推断 |
| 无厂商事件ID | 每次有效设备回调生成新eventNId，桥接重发沿用此ID；只注册一个receiver和一个输出通道。不用“同码100ms内只算一次”掩盖重复注册；厂商会重复发且无法区分时登记该型号限制并核验配置 |
| 来源保护 | action/category不证明发送者身份。按厂商能力验证显式目标、签名级permission/受控SDK等实际保护；需接收其他App广播时不能盲用NOT_EXPORTED造成收不到。不能验证来源的型号保留风险门禁，不称“已防伪造”；条码永远是待校验业务输入，不是执行命令 |
| 系统弹层/相机 | 临时后台（OS授权/扫描Activity）按关联request保留取消上下文，暂停键盘/广播；非本次外部流程或锁屏立即取消。结果返回先比对epoch/session再交付，取消后回调立即释放相机，不写日志或自动发送消息 |

### 5.5 BLE与基础打印边界

001必须用一台指定设备冻结adapter DTO：`adapterKey`、服务UUID/特征UUID清单、读/写/notify所需属性、编码/分帧/校验、MTU限制、业务命令枚举、应答关联、最大payload、连接和操作超时、重连及物理结果查询方法。字段尚无证据时保持§8.1未关闭，不开放`settings:any`或任意GATT字节写入。

BLE状态固定Disconnected→Connecting→Connected→Disconnecting→Disconnected；异常到Unknown/Disconnected，并保留具体原因。一个connection内有副作用操作串行；超时的写操作标结果未知，不自动补发。重连可恢复连接状态但不自动重做最后一次写入；每次重连生成新connectionNId，旧notify丢弃。OS撤权、蓝牙关闭、换人、进程结束均释放资源并更新能力快照。配对是OS行为，不缓存PIN。

PF06A的测试打印仅证明系统调用及人工观察测试页；不能用系统已接收证明标签完成。业务打印任务/持久幂等/超时未知/回执/重打归PF10B。已有Agent拥有设备时不再建立同机直连执行器；本阶段不提前提供通用printer.submit。PDA BLE读写演示不能作为SPP打印或生产打印验收。

device-profile首版由001冻结的受控部署配置提供；页面只测试/连接已有profile，不新增任意UUID/端口/协议编辑器。Bluetooth requestDevice只把用户选择绑定到该profile批准的adapter，不能把所有扫描到的设备自动授权；设备名与handle均不是认证凭据。

### 5.6 稳定错误与用户动作

| 错误码 | outcome / retryable | 页面处理 |
| --- | --- | --- |
| RUNTIME_UNSUPPORTED / RUNTIME_INVALID_ARGUMENT / RUNTIME_VERSION_UNSUPPORTED / RUNTIME_ORIGIN_DENIED | NotExecuted / false | 未支持或请求无效；不循环重试、不输出底层参数 |
| RUNTIME_IDENTITY_STALE / PERMISSION_DENIED | NotExecuted / false | 丢弃旧上下文或提示重新登录/受控设置；OS授权必须再次点击 |
| RUNTIME_REQUEST_CONFLICT / RUNTIME_CONFIG_CONFLICT | NotExecuted / false | 保留输入，读取当前值后显式修正，不能改ID自动重发 |
| RUNTIME_BUSY / DEVICE_BUSY / UPDATE_BUSY | NotExecuted / true | 显示当前忙原因，允许用户稍后重试，默认不重放设备命令 |
| RUNTIME_CANCELLED | NotExecuted或Unknown / false | 明确取消；底层可能已执行时保留结果未知 |
| RUNTIME_TIMEOUT / DEVICE_DISCONNECTED | NotExecuted、Failed或Unknown，按实际最早失败点 / Unknown时false | 未调用设备才可标NotExecuted；有写入可能则先查结果，不自动再写 |
| UPDATE_INCOMPATIBLE / UPDATE_SIGNATURE_INVALID / UPDATE_ARTIFACT_INVALID / UPDATE_ROLLBACK_FORBIDDEN | NotExecuted / false | 保留当前文件，拒绝本包；启动更新失败页不放行业务，需新包/修复输入 |
| UPDATE_DOWNLOAD_FAILED / UPDATE_STORAGE_FULL / UPDATE_HEALTH_TIMEOUT / UPDATE_CLOCK_INVALID | 下载失败可retryable=true，其余false；激活后的健康失败为Failed | 区分下载重试、释放可清临时空间、回退或维修、校时；不删除用户资料 |
| RUNTIME_SECURE_STORAGE_UNAVAILABLE | NotExecuted / false | 原生凭据不能安全保存；允许明确的本次内存会话策略或重新登录，禁止静默明文降级 |

错误对象不包含原始异常/设备地址/Token。`retryable=true`只表示允许用户在条件变化后重试，不是SDK自动重试指令。

### 5.7 媒体端口与JSON桥接分开

共享Runtime在renderer内提供`media.captureMicrophone({operationNId:nid,subjectEpoch:nid}):Promise<MediaStream>`和`media.captureScreen({operationNId:nid,subjectEpoch:nid}):Promise<MediaStream>`，仅由PF06媒体所有者在既有用户手势内调用。会话取消、离页或停止时须显式释放轨道与Android原生采屏服务；迟到轨道由adapter立即停止，不依赖调用`track.stop()`后再等待`ended`事件。麦克风返回音频轨道，首版屏幕返回视频轨道且不自动附带系统音频；PF06继续拥有邀请、绑定、sender/receiver、协商、播放和停止。

MediaStream/MediaStreamTrack是同renderer内对象，不进入BridgeRequest/Response的JSON；普通业务IPC不承载媒体数据。Web adapter调用已有浏览器采集；Electron由受限选源处理器配合renderer媒体API取得轨道。Android手机发屏使用系统MediaProjection、前台服务和专用`screenFrame`事件，将压缩帧送入WebView Canvas并生成真实`MediaStreamTrack`，再交给PF06现有单PeerConnection；事件仅在用户授权的共享会话中流转，不记录帧、不另开媒体Hub。此路线已在Android 10真机验证，其他系统版本及高负载性能仍需单独验收；不能用一张静态截屏冒充实时视频。端口错误映射保留取消/拒绝/不支持与operation来源，交PF06既有局部错误呈现。

## 6. 运行时数据与逐字段说明

本章直接保留全部运行时对象，不再要求人工跳到另一份字段文档。对象无业务SQL表；SQL实体/列说明见§9.6.5。

### 6.1 公共类型、能力与版本身份

- 本文`nid`/`string32`为随机UUID的32位小写hex；`key`为外部原始业务标识，长度沿现有领域DTO，不强改成32位。`subjectEpoch`为容器身份协调器生成的不透明随机nid，不使用用户名或自增业务版本代替。`configRevision`及递增技术序号使用非负十进制Int64字符串，JS以BigInt比较；桥接版本为正的安全整数。
- 时间序列化必须带`Z`，例如`2026-09-14T01:00:00.000Z`；非法/无时区输入拒绝。单调时间只用于当前进程超时，不跨重启持久化复用。签名有效期依可信UTC；时钟明显异常阻止激活并提示校时，不能反复重试或增加过期宽限。
- `NativeVersion`/`WebVersion`及兼容范围均使用SemVer，不含`v`前缀；stable拒绝预发布版本。Android的`versionCode`为正整数，独立于显示`versionName`/SemVer，不能按名字推算可升级性。生产001需登记appId/packageName、Windows安装身份、架构和版本映射。
- 能力名固定：`scanner.keyboard`、`scanner.broadcast`、`scanner.camera`、`bluetooth.ble`、`files.pick`、`files.save`、`printer.systemTest`、`notifications.local`、`media.microphone`、`media.screenCapture`、`media.screenReceive`、`updates.installer`、`updates.apk`、`updates.webBundle`、`diagnostics.export`。SPP、远程推送、后台持续音视频不是以上能力的隐含子项。未核验实现对外返回Unsupported并附`CAPABILITY_NOT_VERIFIED`，支持矩阵仍登记Unknown，以区别硬件确认不支持。
- 能力快照只表达容器/设备/OS状态，不授予业务权限。`Supported`不表示已连设备、对方已同意、声音已播放或版本可立即安装。权限被拒/永久拒绝使用PermissionRequired及不同reasonCode，禁止每次刷新重新申请系统授权。

`?`表示字段可以省略，不等于接受JSON null；未标默认值的必填字段由可信来源提供，不能自动填空串/0。`sequence`是非负十进制Int64字符串，`UTC`为带Z的RFC3339；`SemVer`不带v且≤64字符。`Source=keyboard/broadcast/camera`，`Availability=Supported/Unsupported/PermissionRequired/Degraded`，`PackageKind=installer/apk/web-bundle`，`Channel=stable/pilot`，`Platform=win-x64/android`。

### 6.2 runtime-config.json：终端运行配置

一份文件表示本安装实例选定的环境及操作偏好。由容器校验后原子写入；普通页面只能通过config.update改扫码模式、结束符和诊断偏好，不能改服务器或信任根。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| schemaVersion | 字面量1 | 文件格式版本；随对象结构定义，不是配置内容修订号 |
| configRevision | sequence；默认'0' | 配置修订号；每次成功保存递增，用expectedRevision防覆盖 |
| environmentNId | string≤64 | 部署环境标识；来自受信安装配置，不是租户ID |
| deploymentEnvironment | DEV/TEST/UAT/PROD | 部署类别；由部署配置指定，不从Git分支推断 |
| apiOrigin | string≤2048 | 远端API的HTTPS Origin，不含路径；从内置允许环境中选取 |
| apiBasePath | string≤256；默认'/' | API基础路径；与apiOrigin构成现有apiBaseUrl |
| allowedOrigins | string[] | API允许访问的精确Origin；只能取内置策略批准值，不是页面/更新白名单 |
| terminal | pc/pda | 页面布局；Windows用pc，首轮Android手机使用pda，不代表设备物理型号 |
| channel | Channel | 所选发布通道；来自内置允许集合，配置变化不能绕过版本下限 |
| scannerMode | Source | 当前默认扫码来源；仅允许已验证支持模式，切换前需释放扫描会话 |
| suffix | Enter/Tab/None | 输入结束方式；None需页面显式确认，不用超时猜完成 |
| diagnosticConsent | boolean；默认false | 本地诊断偏好；不代表允许后台上传，每次导出仍须明确同意 |

### 6.3 release-manifest.json：一个发布产物

一份受信清单绑定一个不可变产物，由发布侧生成及签名，客户端只读。清单≤64KiB；除signature外全部字段进入签名输入。签名算法及规范化字节由001实测冻结，不写占位验签。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| schemaVersion | 字面量1 | 清单格式版本；解析器不接受未知版本冒充兼容 |
| releaseNId | nid | 产物发布标识；发布时生成，同ID不得换文件 |
| appId | string≤200 | 应用安装身份；由001固定，防止同平台其他App包误装 |
| environmentNId | string≤64 | 目标环境；与终端安装配置匹配 |
| platform | Platform | 目标系统/架构；不从文件扩展名猜测 |
| packageKind | PackageKind | 产物类别；win-x64允许installer/web-bundle，android允许apk/web-bundle |
| channel | Channel | 发布通道；客户端精确匹配，stable不接受预发布版本 |
| releaseSequence | sequence | 本发布空间递增序号；用于拒绝清单回放，不能用显示版本代替 |
| version | SemVer | 本产物版本；完整安装包为NativeVersion，Web包为webVersion |
| webVersion | SemVer | 本产物携带的Vue资源版本；安装包也须记录内置页面版本 |
| nativeMinVersion | SemVer | 运行该页面所需最低外壳版本；不满足时先安装外壳 |
| nativeMaxVersion | SemVer?；省略表示无上限 | 可兼容的最高外壳版本；不是要求降级安装 |
| bridgeMinVersion | 正安全整数 | 页面依赖的最低桥接契约版本；构建发布时填写 |
| bridgeMaxVersion | 正安全整数 | 页面已支持的最高桥接契约版本；与min共同校验 |
| apiContractId | string≤64 | 经核验的前后端接口兼容组合标识；不是Git提交号，不新增版本服务 |
| androidVersionCode | 正整数?；apk必填 | Android安装递增号；由APK构建确定，独立于versionName |
| artifactUrl | string | 受信HTTPS下载位置；每次重定向也检查白名单，不接受页面任意输入 |
| artifactSha256 | 64位小写hex | 最终完整文件的SHA256；发布时计算，绑定到签名清单 |
| artifactSizeBytes | 非负十进制Int64字符串 | 下载文件精确字节数；用于进度、完整性及空间检查 |
| issuedOn | UTC | 清单签发时间；由发布侧写入 |
| expiresOn | UTC | 清单有效截止时间；过期拒绝激活，不用本地宽限绕过 |
| keyId | string≤64 | 验签公钥的受控标识；须已在客户端信任策略中存在 |
| signingAlgorithm | string≤32 | 签名算法标识；只能使用001冻结的允许算法 |
| signature | string | 清单签名值；编码/长度及规范化规则由001固定，不是SHA256别名 |

### 6.4 update-state.json：本地更新恢复记录

一条记录只保存本安装实例恢复一次更新所需的最小事实。由更新器原子写入；不是业务审计或历史库，也不能凭旧Healthy状态跳过本次启动检查。当前、lastGood与暂存slot由更新器私有存储关联，不对网页暴露路径。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| schemaVersion | 字面量1 | 状态文件结构版本；未知格式走受控修复 |
| state | UpdateState | 当前阶段，枚举见§4.1；单更新所有者推进 |
| currentNativeVersion | SemVer | 实际已安装外壳版本；从宿主读取，不采信下载目标 |
| currentWebVersion | SemVer | 当前加载的页面版本；切换后与实际slot核对 |
| lastGoodWebVersion | SemVer | 最后完成本地健康确认的资源版本；用于恢复修复入口，不授予旧版业务通行 |
| pendingReleaseNId | nid? | 当前待下载/切换的发布标识；目标确定时写入 |
| pendingArtifactHash | 64位小写hex? | 待处理产物的预期hash；来自受信清单，不能随下载响应更换 |
| lastErrorCode | string? | 最近更新错误分类；无错误省略，不能保存原始异常/秘密 |
| currentReleaseNId | nid? | 当前资源关联发布标识；初装尚无发布关联时可省略 |
| lastGoodReleaseNId | nid? | 最后健康资源的发布标识；与版本/hash/私有slot共同恢复 |
| targetPackageKind | PackageKind? | 当前目标类别；决定Web切换或OS安装流程 |
| activationNId | nid? | 本次切换的一次性标识；激活前生成，旧页面不能确认新激活 |
| healthDeadlineOn | UTC? | 本次健康窗口截止时间；供重启恢复判断，进程内超时仍用单调时钟 |
| highestAcceptedReleaseSequence | sequence；默认'0' | 该发布空间最高已接受序号；防旧签名清单回放，不因换用户清空 |

下载进度、`updateEpoch`和`stateRevision`只存在当前进程的界面快照中，不写持久状态；失败重试从头下载。状态文件不保存版本历史、审计事件、多级回滚链或断点续传元数据。

### 6.5 UpdateStatusV1：界面更新快照

由更新器从本地事实生成只读快照，供启动界面及终端页显示。业务页的canDownload/canActivate必须为false；只有受信启动检查阶段可受理切换。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| contractVersion | 字面量1 | DTO协议版本；不是应用版本 |
| updateEpoch | nid | 快照所属更新器代次；与stateRevision共同判断新旧 |
| stateRevision | sequence | 该代次的状态修订；来自更新器 |
| state | UpdateState | 显示阶段；与状态文件语义一致 |
| currentNativeVersion | SemVer | 实际外壳版本；只读 |
| currentWebVersion | SemVer | 实际当前页面版本；只读 |
| targetReleaseNId | nid? | 本次目标发布标识；检查未成功前省略 |
| targetVersion | SemVer? | 当前目标产物版本；按包种类显示“客户端/页面版本” |
| downloadedBytes | sequence | 当前下载字节数；不超过确认的总大小 |
| totalBytes | sequence? | 总大小未知时省略；界面用不确定进度，不能显示假百分比 |
| canDownload | boolean | 当前阶段能否受理下载；由宿主阶段/目标决定，不是业务权限 |
| canActivate | boolean | 当前阶段能否切换/安装；业务期间始终false |
| lastErrorCode | string? | 可本地化错误码；不暴露临时目录、URL或密钥 |
| recoveryAction | Retry/Reinstall/ContactSupport? | 针对错误的恢复建议；退出始终可用，不提供继续旧版业务 |

### 6.6 scan-session：扫描输入所有权

一条内存记录表示当前页面取得的一次扫描所有权。全容器只允许一个活动会话；离页、换人及对应生命周期结束时释放，不持久保存条码业务值。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| sessionNId | nid | 容器成功授予所有权时生成；新会话用新ID，不能以条码或设备号代替 |
| ownerPageId | string≤128 | 调用方给出的已登记页面ID；宿主验证，用于离页释放和投递 |
| subjectEpoch | nid | 身份协调器当前代次；隔离旧主体结果，不是用户ID/权限凭据 |
| source | Source | 本会话实际输入通道；不从扫描文本猜来源 |
| acquiredOn | UTC | 成功取得所有权时间；诊断用，不是条码业务发生时间 |
| cancelled | boolean；默认false | 取消/失效后设true，禁止继续投递；不代表此前物理操作一定未发生 |

### 6.7 ScanResultV1：一次扫描结果

由已取得所有权的adapter产生，经桥接投递当前会话。真实重复扫描生成不同eventNId，桥接重放沿用原ID；value只交业务/临时显示，不写日志。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| contractVersion | 字面量1 | 扫码结果结构版本 |
| eventNId | nid | 本次真实回调事件ID；用于去除传输重放，不按value去重 |
| sessionNId | nid | 结果所属扫描会话；离页/新会话后旧ID拒绝 |
| value | string；1～4096字符 | 条码原始业务值；保留前导零、大小写及合法GS1分隔符 |
| source | Source | 产生结果的实际adapter通道；必须与会话一致 |
| symbology | string≤64? | 码制；仅adapter可靠提供时填写，不凭内容猜 |
| receivedOn | UTC | 原生/输入adapter收到结果的时间；不是服务端业务入账时间 |
| subjectEpoch | nid | 启动扫描时捕获的主体代次；禁止回调时补成当前用户 |

### 6.8 能力与设备快照

CapabilitySnapshotV1表示当前容器能力，不授予业务权限；DeviceStatusV1表示一个受控设备配置的当前状态。由宿主检测更新，页面只读。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| CapabilitySnapshotV1.contractVersion | 字面量1 | 能力DTO结构版本 |
| CapabilitySnapshotV1.container | web/electron/capacitor | 实际运行容器；不能按UA/屏幕宽度猜 |
| CapabilitySnapshotV1.os | string | 宿主报告的系统名称 |
| CapabilitySnapshotV1.osVersion | string | 宿主报告的系统版本；用于支持矩阵 |
| CapabilitySnapshotV1.nativeVersion | SemVer? | 外壳实际版本；纯Web无原生版本时省略 |
| CapabilitySnapshotV1.webVersion | SemVer | 当前Vue资源版本 |
| CapabilitySnapshotV1.bridgeContractVersion | 正安全整数 | 当前adapter实现的桥接契约版本 |
| CapabilitySnapshotV1.capabilities | CapabilityEntry[] | 逐项能力列表；能力名称沿§6.1 |
| CapabilitySnapshotV1.subjectEpoch | nid | 能力快照所属主体代次 |
| CapabilitySnapshotV1.snapshotSequence | sequence；初始'0' | 同实例同事件源快照序号；与onChange事件合并 |
| CapabilityEntry.name | string枚举 | 稳定能力名，如scanner.camera；不使用界面标题 |
| CapabilityEntry.availability | Availability | 设备/容器支持与OS授权状态，不表示已经执行成功 |
| CapabilityEntry.reasonCode | string? | 不支持/降级/权限原因；本地化展示依据 |
| DeviceStatusV1.profileNId | nid | 关联受控设备配置，也是稳定row-key |
| DeviceStatusV1.displayName | string≤200 | 供操作员识别的名称，不是设备身份凭据 |
| DeviceStatusV1.kind | ble/printer/scanner | 设备类别；来自受控profile |
| DeviceStatusV1.availability | Availability | 当前能力/权限状态 |
| DeviceStatusV1.connectionState | Disconnected/Connecting/Connected/Disconnecting/Busy/Unknown | adapter报告的连接事实；Unknown不能显示为已断开且可安全重写 |
| DeviceStatusV1.reasonCode | string? | 该设备当前状态原因，不覆盖其他行错误 |
| devices.list.items | DeviceStatusV1[]；最多100项 | 受控配置的设备快照集合；不新增分页服务 |
| devices.list.subjectEpoch | nid | 列表所属主体代次 |
| devices.list.snapshotSequence | sequence；初始'0' | 设备事件源序号；不与更新器stateRevision跨源比较 |

### 6.9 BridgeRequestV1 / BridgeResponseV1：受限调用信封

一条请求表示对一个白名单方法的调用；响应保留原上下文。请求≤64KiB；方法payload/data的精确字段沿§5。不暴露任意shell、文件系统、SQL或插件全集。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| BridgeRequestV1.contractVersion | 字面量1 | 调用信封结构版本 |
| BridgeRequestV1.requestNId | nid | 本次操作ID；重入沿用，同ID异载荷拒绝，不等于物理执行账本 |
| BridgeRequestV1.method | 白名单枚举 | 要调用的方法；未知方法拒绝 |
| BridgeRequestV1.payload | 对应方法DTO | 方法参数；不能覆盖信封的身份/请求ID |
| BridgeRequestV1.subjectEpoch | nid | 认证装配层安装的主体代次；不采信普通页传入任意用户/权限 |
| BridgeResponseV1.contractVersion | 字面量1 | 响应信封结构版本 |
| BridgeResponseV1.requestNId | nid | 原请求ID；成功/失败/取消都保留 |
| BridgeResponseV1.ok | boolean | 调用是否成功；不代表设备物理结果一定完成 |
| BridgeResponseV1.data | 对应返回DTO? | ok=true时必有且无error；方法返回内容见§5 |
| BridgeResponseV1.error | 错误对象? | ok=false时必有且无data；不含秘密/原始异常 |
| BridgeResponseV1.subjectEpoch | nid | 原请求的主体代次；迟到响应不能改为新用户 |
| error.code | string | 稳定错误分类；枚举和页面动作见§5.6 |
| error.retryable | boolean | 是否可在条件变化后重试；不是SDK自动重发指令 |
| error.outcome | NotExecuted/Failed/Unknown | 未执行、明确失败或物理结果未知；Unknown不能自动补发 |

### 6.10 device-profile.json：受控设备配置

一条记录表示已批准adapter下的一项设备绑定。由001冻结的部署配置提供，首版不建任意协议编辑器；秘密留安全存储。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| schemaVersion | 字面量1 | 配置对象格式版本 |
| profileNId | nid | 稳定设备配置ID；与连接实例ID不同 |
| kind | ble/printer/scanner | 设备类别；决定使用的能力端口 |
| displayName | string≤200 | 操作员可识别名称；可显示但不作为授权依据 |
| adapterKey | string≤128 | 已验证适配器标识；连接时只能使用其批准协议 |
| deviceHandle | string≤256 | 宿主识别设备的受控句柄；不是认证因素，不写诊断 |
| settings | 已验证adapter DTO | 对应设备协议参数；真实设备核验后逐字段冻结，禁止any或任意字节命令 |

### 6.11 diagnostics：本地脱敏诊断记录

一行表示一个运行时技术事件，由宿主写入滚动文件；最多10MiB×3、默认保留7天，无默认后台上传。每次导出独立同意，不记录业务正文。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| occurredOn | UTC | 事件发生时间；宿主记录 |
| level | info/warn/error | 技术事件严重程度 |
| code | string≤96 | 稳定诊断分类，供支持人员检索 |
| requestNId | nid? | 有关联调用时记录原请求ID；无请求事件省略 |
| adapterKey | string≤128? | 有关联adapter时记录版本化标识，不记录设备地址 |
| durationMs | number? | 操作耗时毫秒；由单调时钟计算，不使用UTC相减做超时 |
| version | string | 生成该诊断的运行组件版本；不拿目标更新版本冒充当前版本 |

### 6.12 内置部署策略：安装时的信任边界

一份策略随完整包或受控签名部署维护，不是config.update对象；Web包不能修改。实际Origin/公钥/环境值由001核验。

| 字段 | 类型 / 默认或空值 | 中文含义、来源与维护时机 |
| --- | --- | --- |
| schemaVersion | 字面量1 | 策略结构版本 |
| appId | string≤200 | 应用安装身份；与发布清单精确匹配 |
| rendererOrigin | string≤2048 | 受信本地页面Origin；精确匹配scheme/host/port |
| allowedEnvironments | 环境对象[] | 部署允许的环境集合；普通配置只能选择其中值 |
| allowedEnvironments[].environmentNId | string≤64 | 该允许环境的稳定标识 |
| allowedEnvironments[].apiOrigins | string[] | 该环境允许的API Origin，不含通配 |
| allowedEnvironments[].apiBasePaths | string[] | 该环境允许的API基础路径 |
| allowedEnvironments[].channels | Channel[] | 该环境允许使用的发布通道 |
| allowedEnvironments[].apiContractIds | string[] | 经核验的API兼容组合 |
| updateOrigins | string[] | 资源与清单下载允许的精确Origin；与API白名单分开 |
| updateManifestUrl | string≤2048 | 固定清单入口；不依赖业务登录，不接受任意网页URL |
| trustKeys | 公钥对象[] | 允许验签的公钥集合；不包含私钥 |
| trustKeys[].keyId | string | 公钥查找标识；清单只可引用已受信项 |
| trustKeys[].algorithm | string | 该公钥允许的算法；与冻结签名方案一致 |
| trustKeys[].publicKey | string | 受控分发的公钥；编码格式由001给固定测试向量 |

### 6.13 发布清单、版本与恢复语义

清单/状态/快照字段见本章§6.3～6.5。清单允许win-x64/installer、win-x64/web-bundle、android/apk、android/web-bundle；按appId/环境/通道/包种类隔离版本空间。

- `version`表示本产物版本：完整包为NativeVersion，Web包为webVersion；`webVersion`始终表示产物携带的页面版本。
- `nativeMinVersion/nativeMaxVersion`与bridge范围只用于判断页面能否使用当前外壳；首期不增加独立安全下限版本字段。
- `releaseNId`识别不可变产物，`releaseSequence`防清单回放，`activationNId`绑定一次切换及健康回报；它们不能互相替代。
- 签名覆盖除signature以外全部字段；拒绝重复JSON键、非规范数字及额外未定义字段。001固定算法、规范化、签名编码、公钥格式及跨语言正反测试向量。产物SHA256被清单签名绑定，平台安装签名另验。
- 最后良好版本须关联已验hash及私有slot；显示版本号不足以恢复。恢复旧文件不能绕过本次启动必更。
- 能力/设备快照用subjectEpoch与同事件源snapshotSequence；更新快照用updateEpoch/stateRevision，不跨源比较。业务中的canDownload/canActivate始终false。

内置部署策略与普通配置分开，字段见§6.12。所有Origin精确匹配，Web包不能扩大API/更新白名单或更换公钥。配置按“校验→归一→发布只读快照”装配，不能混用部分无效文件；未知schema保留原文件并进入修复。首次使用合法内置配置，升级损坏只回同环境合法备份，不退到开发地址或默认租户。普通配置保存校验expectedRevision后原子落盘。

## 7. 工程接入、原生生命周期和发布

### 7.1 当前接入位置与允许修改点

下列接入位置来自2026-09-14代码核对；属于待适配设计。实际开工核对交接差异并保留既有修改，不要求回到历史Git状态。

| 现有文件（相对仓库） | 当前行为 / PF06A最小修改边界 |
| --- | --- |
| `src/frontend/src/main.ts`、`app/createIndustrialApp.ts` | 同一工厂装配Pinia/Router/认证/API/协作；原生先完成异步bootstrap再调用工厂，不另复制一份业务main |
| `src/frontend/src/config/runtimeConfig.ts` | 已有mock/http/embedded与pageOrigin，Web显式根相对API路径经同一parser解析；开发默认localhost:5041。增加宿主只读配置来源并生成唯一快照；原生生产缺配置即失败，不回默认 |
| `src/frontend/src/auth/sessionStore.ts`、`stores/authStore.ts`、`auth/types.ts`、`auth/httpAuthGateway.ts`、`auth/embeddedAuthGateway.ts` | bearer/mock持久化sessionStorage；embedded-cookie跳过Token持久化并通过bootstrapSession/refreshEmbeddedSession恢复。仅对需要持久化的会话抽出load/save/clear异步端口；Native bearer接安全存储，不建第二AuthStore |
| `src/frontend/src/api/httpClient.ts`、`api/collaborationHub.ts`、`api/systemData/notificationHub.ts` | 保留认证分支的装配范围：http走Bearer及原刷新，embedded走withCredentials、无Token的Collaboration API/Hub，不强行启动SystemData Hub或管理API。核对各分支已启用端点均使用有效配置，Bearer Hub重连读最新Token |
| `src/frontend/src/router/index.ts`、`routes.ts`、`routeNames.ts`、`guards.ts`、`meta.ts` | 当前createWebHistory；本地资源服务器须支持已登记页面路由fallback，刷新/冷启动深路由可用。未知资源404不能都返回HTML掩盖JS缺失；若目标方案必须改history，001先定适配方案，Web既有URL不变 |
| `src/frontend/src/App.vue`、`stores/collaborationMediaStore.ts`、`components/collaboration/CollaborationMediaHost.vue`、`ScreenShareFloatingWindow.vue`、`CollaborationChat.vue` | 已有App级媒体Host/页内观看浮窗；只接Runtime媒体采集/生命周期端口，不复制Hub/Store或把媒体搬到子窗口 |
| `src/frontend/src/permissions/catalog.ts`、`catalog.generated.ts`、`locales/zh-CN.ts`、`locales/en-US.ts`、`localization/types.ts`、`systemData/runtime/navigation.ts` | 仅增加终端入口/本地化及原生能力适配；生成文件通过现有生成机制更新，不单改前端字符串假装拥有后端权限 |
| `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs`、`IndustrialPlatform.Identity.Application/Bootstrap/BootstrapSeedCatalog.cs` | 仅新增§3.7三项权限及对应真实种子/测试；注册权限不代表普通用户已授权 |
| `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SystemDataBaselineSeedRunner.cs` | 导航受管条目按现有版本机制增量；允许补相关现有种子/策略测试，不新建Runtime Service Host |
| 新增目录 | `src/frontend/src/platform/runtime/`；共享终端页、AppScanInput及ScannerTestPage置`src/frontend/src/components/runtime/`，路由薄入口在`pages/pc/`和`pages/pda/`；`src/clients/electron/`、`src/clients/pda/`、`tests/Clients/`和对应前端测试 |

表内简写路径沿同单元前缀解析。正式派遣登记新增测试/页面/配置文件和唯一写入者；同一任务内两端分别维护各自原生目录，共享Runtime/包锁/路由/认证/协作/权限种子顺序写。没有业务API需求的Runtime方法保持本地调用，不为了符合菜单标准新增空API Controller。

### 7.2 配置、Origin、资源与HTTP/Hub

启动顺序固定：读取内置发布身份/信任配置→校验容器本地配置→启动强制更新检查/失败恢复→选择本次检查允许的已验Web slot→注册受信本地页面Origin/受限bridge→安装只读有效配置及存储adapter→装配Vue。此后本地资源/bridge健康检查独立报告，不等待网络；业务另按恢复身份→加载权限/菜单/权威数据继续。网络身份恢复失败不等于本地包不健康，健康步骤按§4要求在本地条件具备时完成。

- **页面来源**：Electron优先受控secure/standard自定义协议与私有资源根；Capacitor使用冻结的本地scheme/hostname和打包webDir。实际Origin在001注册并测试；不能把本地页面`null` Origin全局加入CORS或仅依靠apiOrigin允许桥接。远程业务API不是可加载任意JS的页面来源。
- **API来源**：runtime-config的apiOrigin+apiBasePath形成现有apiBaseUrl，域名/路径按URL解析归一；配置来自可信部署输入，不来自query/deep-link/localStorage/网页任意消息。原生环境切换需要清会话/连接并重新bootstrap，不能带旧Token向新环境试探。只在生产包允许的environmentNId/通道集合内使用。
- **统一读取**：任何loadRuntimeConfig调用均返回同一已冻结配置，包括Collaboration Hub；不得登录client已换地址而Hub沿import.meta.env旧地址。不直接修改现有API前缀、authorization envelope或requestNId语义。
- **认证模式与同源路径**：保留Web `http/embedded/mock`的现有装配边界和显式根相对API路径；生产仍禁止mock。原生本地scheme不能被当成远端HTTP API基址，原生部署须给明确的受信HTTP(S)入口。原生首版允许的authMode在§8.1认证输入冻结；embedded是现有Web能力，不因装入WebView自动获得原生支持。若纳入原生，须核验Cookie分区/SameSite/跨源、会话bootstrap/刷新/登出及宿主可信身份绑定；不得制造accessToken/refreshToken、读取HttpOnly Cookie给JS，或将会话失效导航改成平台登录。未纳入的原生模式在配置校验时明确拒绝，不能静默改成http。
- **跨源/证书**：按实际页面Origin和`withCredentials`核对CORS及Cookie策略，允许精确Origin且不返回`*`配凭据；HTTPS/WSS、WebSocket升级、代理与大文件需在交付拓扑验证。内网CA由受控环境信任，不能关闭webSecurity/TLS验证解决连接问题。HTTP调试例外只能在独立开发配置和已批准测试Origin使用，不打进正式包。
- **资源与深路由**：生产从内置或热更新后已校验的版本资源加载，禁止依赖Vite在线；配置Vite base/webDir/协议资源映射。直接冷启聊天深路由、刷新、动态import、字体图标、本地错误页均检查。路径穿越、绝对路径、非登记路由不得读私有文件。普通外链只在无bridge的系统浏览器打开，内部页面禁止任意window.open/webview导航。
- **SSO边界**：现有withCredentials不证明系统浏览器与WebView共享Cookie。首版必须验证现有账号登录/刷新/登出；若分发要求企业SSO，§8.1认证输入补实际回调Origin、受控链接、state/PKCE及会话交接证据，复用现有Identity能力，不临时发明把Token放URL的登录协议。未指定原生SSO不得宣称已支持。

### 7.3 安全存储与权限不能停在界面

Web bearer/mock保留现有sessionStorage策略；embedded-cookie继续以服务端HttpOnly会话为权威，恢复/刷新沿原AuthGateway，不持久化伪造Token或镜像Cookie。原生Bearer Token只允许必要的内存使用和OS受支持的加密存储，不再同时写sessionStorage/localStorage/Capacitor普通Preferences；密码不持久化。存储key绑定appId/环境/tenant/user/服务端会话，日志只记脱敏错误。Windows用户安全存储不等于每个共用业务账号隔离，应用必须仍执行换人清理；Android密钥/加密凭据排除Auto Backup/迁移恢复。安全存储不可用的降级策略在001明确，只允许显式的内存会话，不静默明文落盘。

持久化会话端口只向认证装配模块开放，方法为`loadCurrent():Promise<AuthSession|null>`、`saveCurrent(session):Promise<void>`、`clearCurrent():Promise<void>`；保留现有AuthSession的transport及可选Token语义，embedded-cookie不经此端口保存/恢复身份。原生adapter再经受限内部通道保存，不向普通页面提供通用`secureStorage.get(key)`或任意读取接口。恢复先核验会话和当前用户权限，不能只因解密成功进入业务。一次失败不能留下“UI已登录、宿主仍是旧epoch”的分裂状态。

配置/更新/设备调用既有前端PermissionGate也有宿主接收端检查：从现有Identity可信会话取得权限，不信任页面提交的permissions数组；方法执行前检查当前身份、所需权限和OS授权。如何复用既有me/令牌验证与权限失效通知由001在§8.1认证输入核对并固定；不能用新账号服务代替，也不能在未核验前把UI隐藏当安全验收。服务端业务请求始终继续独立鉴权。危险操作的权限新鲜度不确定时拒绝新操作，清理本人资源不受此阻塞。


### 7.4 Electron Windows

首版一个业务BrowserWindow/renderer，一个业务媒体Host；第二次启动只激活已有实例并处理经校验导航，不打开第二套聊天/扫码资源。托盘菜单仅显示“打开平台”“退出”，无业务敏感正文；不默认开机自启。安装身份、session分区及单实例锁作用域由§8.1 Windows输入固定。

| 用户/OS动作 | 页面及资源结果 |
| --- | --- |
| 最小化 | 保留App和当前媒体，任务栏/托盘可恢复；不能因观看窗隐藏重复播放或切断 |
| 点击窗口X | 托盘可用时隐藏到托盘并保留明确驻留提示，当前语音/共享保持；首次告知“关闭窗口后仍运行”。无托盘时有在途业务则显示受控退出流程，不隐藏成无法重新打开的后台进程 |
| 托盘“退出” | 先检查未保存表单/文件/设备操作，用户保存/放弃/稍后；确认退出后立即按能力停止媒体、扫描、BLE，尽力发End后退出；远端/审计无响应不阻塞本地停止 |
| OS锁屏/休眠 | 主动结束当前媒体并释放扫描/设备会话，持久消息由服务端保存；唤醒恢复身份/权威状态，不自动重启麦克风或共享。锁屏按原安全生命周期处理，不触发页面更新 |
| renderer崩溃/进程被杀 | 能主动清理则清理；不能执行回调不伪造Stopped/Healthy。对端沿PF06租约到期处理；下次启动查询权威状态，旧会话不自动重接采集 |
| 安装升级退出 | 仅§4启动检查阶段允许更新器接管安装退出；普通关闭/托盘恢复不能触发默认自动安装 |

安全配置保持contextIsolation=true、sandbox=true、nodeIntegration=false；默认拒绝非白名单permission、frame、导航和新窗口。preload暴露按方法封装端口，不能暴露ipcRenderer、send任意channel、shell、任意fs、process/env或原生插件全集；诊断导出也不能间接提供任意目录读取。

屏幕采集复用PF06“用户选源→邀请/接受→绑定→传输”顺序。在已核验OS/Electron版本支持时使用系统选择器，否则使用§3.8受控来源选择界面；不复制官方示例的“取sources[0]直接授权”。源句柄只绑定当前request/epoch，源窗口关闭触发该屏幕能力停止。麦克风为独立语音能力，首版不将系统音频loopback默认并入屏幕共享，不改变PF06三槽/单PeerConnection协议。

### 7.5 Capacitor Android（本次仅手机）

- 生命周期同时处理Activity变化、appStateChange、resume、锁屏、配置变化和进程重建；在resume重新检测权限、网络和身份；同一存活实例不切换Web版本，进程/renderer重建按冷启动检查更新，而不是只调用一次Hub.start。重复监听/重复receiver注册可观察且须消除。
- 前台聊天/观看/语音按§8.1媒体输入实际能力验证。Android手机主动共享屏幕时，系统授权后的MediaProjection前台服务允许切到桌面或其他App，画面仍经既有PF06通道发送；用户停止、系统撤销投屏或会话结束时须释放原生服务和视频轨。普通后台语音/BLE及锁屏持续媒体仍需分别验证，不能由屏幕共享通过推定。返回前台恢复权威业务列表。用户正在确认OS权限/文件选择的受控临时Activity按§5.4保留原request上下文。
- 返回键先关闭最上层受控弹窗/扫码会话，再按既有PDA路由返回；未保存表单按原离页规则。根页返回进入后台按上述清理，不直接强制退出覆盖在途物理操作结果。软键盘、安全区、横竖屏及滚动保持真实设备验收；旋转不得重建第二媒体Host或receiver。
- 权限清单按目标OS/targetSdk区分：相机、麦克风、蓝牙扫描/连接和通知只按使用请求；本次不加入厂商广播权限或接收器。Android 12+蓝牙权限与旧系统定位要求、Android 13+通知授权按官方规则及目标手机核验；拒绝/永久拒绝/设置撤销分别处理，不为连接一个设备申请无用权限。
- **屏幕接收≠本机屏幕采集**：Android WebView的`getDisplayMedia`在当前真机不可用；手机发屏须由MediaProjection逐次取得系统同意，前台服务采集帧，WebView `canvas.captureStream()`生成视频轨，并接入PF06既有传输。Android 10真机已验证手机→Windows画面、切后台持续更新、停止后释放；这一结果不等于Android 14+或iOS支持声明。原生能力改变需重打APK，Web页面逻辑改变仍按现有Web更新流程生效。
- 原生插件权限、SDK/ABI变化走APK升级；设备WebView不能更新或版本低于支持下限时显示明确升级/不支持信息，不通过降低安全配置让坏页面勉强运行。

### 7.6 协作恢复、迟到操作与权限异常

恢复顺序：确认当前epoch→恢复/刷新Identity会话与权限→建立或恢复原有Hub→读取当前聊天/未读及媒体权威快照→按PF06版本规则合并→更新呈现。UI“已连接”与“正在共享/通话”分开，不能因为Hub connected就写媒体成功。服务端已经Ended的会话不能被本地缓存或迟到Ready复活，过期TURN配置不能重用。

PF06的session/call ID、mediaContextNId/contextRevision、negotiationNId、operation代次保留原定义；Runtime epoch只增加容器主体隔离，不替换其并发令牌。成功、失败、取消、mediaReady fallback都携带原上下文。退出/切人后迟到选源、getUserMedia、文件选择、设备连接立即清理，不把结果挂到新聊天；当前真实权限/设备错误仍可见，不全局吞掉MEDIA_CONTEXT_STALE。

新增能力失败只清对应能力，停止语音保留屏幕、停止屏幕保留语音；只有锁屏/退出/身份失效等全局生命周期动作才结束全部。多设备抢接与忙线继续由PF06服务端决定，原生不能凭本机单实例锁绕过跨设备约束。恢复后需要新邀请/用户授权的动作明确提供入口，不后台自动接听/自动发屏。

### 7.7 系统通知和消息持久化

首版`notifications.local`表示进程存活且收到已授权事件后显示系统通知；权限已授予不保证系统一定显示，accepted只表示受理。`titleKey`限“有新消息/有协作邀请”等模板，不附正文/条码/人员姓名/附件名。去重键=当前身份+事件ID；默认到达通知不自动标已读。

点击通知要做冷/热启动统一处理：验证内置payload形状及所属app/环境→冷启动先完成更新检查（热恢复不切版本）→等待身份恢复→匹配原通知主体→重新查会话成员权限→进入对应布局既有会话。已换用户/失效会话/被移出会话/过期邀请只提示不可用，不展示旧缓存，不自动邀请/接听。登出清本地主体通知及pending导航。若将来启用远程push，还需平台token绑定/解绑服务端契约与送达证据；本包不先造推送Service。

App被杀、系统省电限制或网络断开时，没有已确认push渠道就不宣称后台通知可达。消息已由PF05保存与系统通知显示分别验收。恢复只同步服务端事实，不为补通知而二次发送消息或重复已读提交。


### 7.8 可复现工具链和三类产物

| 产物 | 001必须登记的构建输入 | 005真实交付证据 |
| --- | --- | --- |
| Windows完整包 | Electron/electron-vite/builder/updater精确版本、Electron内置Chromium/Node版本、Windows/CPU、安装器格式/安装范围、appId/产品名、签名证书责任与时间戳策略 | 无开发服务器机器安装→启动登录→退出/再开→升级→卸载；旧版配置/设备档案保留策略和清理说明；安装后的实际版本与包hash |
| Android APK | Capacitor core/cli/android/plugin版本组合、JDK/Gradle wrapper/AGP、compile/min/target SDK、ABI、applicationId、versionCode/versionName、WebView最低版本、签名alias引用 | 真实Android手机安装和拍照扫码、所需权限、冷启动、旧APK覆盖升级、数据保留、错误签名/低versionCode拒绝、人工重新安装路径；不含PDA广播 |
| Windows / Android Web Bundle | 同一frontend构建产物、webVersion、资源入口/manifest、依赖Native/bridge/API契约范围、更新器插件配置、包限额 | Windows不改安装包、Android不改APK，各完成一次页面资源升级；健康确认、损坏/中断回退、旧页回报无效、内置恢复页可用 |

开发Node与Electron内置Node是两套版本，不因为现有Node24就认为Electron运行兼容。不得使用浮动latest依赖或把插件demo依赖照搬进主工程；冻结锁文件/Gradle wrapper/插件版本、许可和来源，记录实际构建命令与干净构建验证。001未完成前不杜撰`pnpm package:*`等仓库不存在的命令。

首版复用现有Vue源、API和语言资源；原生package只拥有宿主/adapter/打包所需依赖，不复制整个前端代码、业务配置或领域模型。各容器产物对同一WebVersion应能追踪到同一业务构建输入。构建产物不得包含私有Development.local.json、`.env`秘密、签名私钥、测试账号、开发server.url、生产mock开关或自动打开的远程调试端口。

APK安装经系统受控流程，未知来源安装权限和文件授予只在用户发起本次升级时处理，不开放所有文件权限或承诺静默安装。正式包保留安装身份和签名连续性，签名丢失/更换不是“改版本号再安装”即可恢复。安装/签名/分发执行须遵守实际任务授权，本轮文档不代表已经发布。

### 7.9 发布侧与公钥信任

发布路径逻辑为`appId / environmentNId / channel / platform / packageKind / releaseNId`，artifact不可变；先上传并核验全部包，再原子发布签名清单/通道指针。不能先发布清单后让客户端遇到半包或404。下载端点与每级重定向只允许内置信任的HTTPS目标，更新HTTP不携带平台业务Bearer Token去第三方域名。

发布权限与平台runtime.update不同：前者控制产物和签名，后者仅允许业务内手动检查；启动更新由受信bootstrap自动执行，OS安装确认仍由操作者完成。签名私钥留受控发布环境，客户端仅内置信任公钥和keyId；普通runtime-config不含可编辑公钥。TLS证书、清单签名、Windows签名、APK签名分别登记责任/到期/恢复，不互相代替。

首期只内置一个受信公钥集合；未知keyId拒绝，不能从未受信清单下载新公钥后自行信任。密钥轮换或吊销需要通过完整安装包更新信任配置，不在本次建设在线密钥治理。内部网络不可达时保留lastGood供修复界面使用，启动检查失败不放行旧版业务；有效期异常时禁止激活，不能关闭校验“临时救急”。

### 7.10 诊断、安全保存与维护

诊断包内容限定：Native/Web/bridge版本、OS/WebView/已核验adapter版本、环境代号、脱敏能力与权限状态、更新状态/错误码、操作耗时和相关request ID。沿§6最多10MiB×3文件、7天上限，先滚动限制再导出，不把整个userData目录压缩。默认无后台上传；每次导出显式同意。

排除Token/Cookie/Authorization/密码、TURN凭据/SDP/ICE地址、完整API查询字符串、条码/消息/标签/通知正文、蓝牙MAC/配对凭据、绝对用户目录、个人文件和屏幕缩略图。脱敏覆盖嵌套对象、异常链及第三方插件输出；无安全过滤能力的插件日志不得直接收集。原始崩溃dump可能含内存秘密，首版不自动加入。

保存必须经系统选择器，只接受当前有效artifactHandle和经清理的displayName；名称不能含路径、控制字符或设备保留名，覆盖已有文件由系统明确确认。取消不产生成功提示；导出失败不影响当前业务。支持人员收到的是可复制的错误码/版本/相关ID，而不是用户凭据。卸载/重装手册分开说明应用文件、用户配置、加密凭据、更新暂存和本次诊断的位置及保留策略；不擅自删除普通业务资料。

## 8. 开发输入核对与整体验收

### 8.1 开工时一次核对必要输入

下表属于三个任务内的准备工作，不另设八道审批或七个派遣阶段。输入缺失只影响对应能力；PDA广播不在本次范围，也不收集其设备或协议输入。设备/依赖版本须实际核验，不填latest或虚构PASS。

| 范围 | 必须记录的具体输入 | 对应结果 |
| --- | --- | --- |
| Windows/Android手机 | OS、架构、WebView、安装权限、安装身份/签名连续性；目标手机型号 | 可复现安装、冷启动、真实登录及扫码页入口 |
| 工具链 | 项目Node/pnpm及Electron/构建器、Capacitor/JDK/Gradle/AGP/SDK、相机/更新插件精确版本和许可 | 固定锁文件和真实命令，包不依赖Vite；不能把候选库写成已采用 |
| 认证/页面装配 | 原生允许authMode、本地Origin、API/Hub唯一配置、安全存储、可信主体/权限绑定与失效通知 | §7装配和登录/刷新/换人链可验证，Web既有模式不退化 |
| 页面入口 | 终端菜单父项、增量seed ID、权限、命名路由、两端扫码测试子路由 | 实际菜单可达，直接路由不能越权，不新增空API |
| 更新与签名 | 两端Web资源加载、清单/工具元数据映射、签名规范化/公钥、安装签名、实际渠道和包大小/空间阈值 | §4正反场景可执行，启动更新不依赖业务登录 |
| BLE/基础打印 | 实际设备/协议、adapter DTO、超时与结果未知处理；Windows打印测试目标 | 沿§5已定能力接入，不能以BLE demo冒充SPP打印 |
| 原生媒体/通知 | 目标WebView/系统实际采集、接收、播放、本地通知与生命周期 | 沿原媒体Host接入，不建设第二套媒体平台或推送服务 |
| SQL Server | 见§9.6，独立于终端设备核对 | 009真实引擎及逐对象接入输入 |

记录格式：目标/实际版本、确定的契约或配置、实际操作与结果、未验原因及受影响范围。真机未测不等于代码失败，也不以编译或组件模拟代替真实支持。

### 8.2 先跑通最小链，再补已有能力

001先跑Windows/Android“安装→启动检查→真实登录→终端页→扫码测试→输入/相机结果→清空→离页释放”，同时完成§3的共享控件与路由。原生工具链、配置和接口就在这条链中核验，不新建脱离真实菜单/认证的演示应用。

之后接既定文件/基础打印、BLE、协作和诊断能力；媒体仍从原聊天入口，经邀请/同意、授权、实际声音/画面及分项停止验收。005再用同一客户端/扫码页执行§4更新场景，最后完成下表。用户后置项单列，不重复全量验证未受改动影响的旧Web路径。

### 8.3 终端回归矩阵

R1=共享Web行为，R2=Windows外壳，R3=Android/设备，R4=更新，R5=协作生命周期，R6=部署诊断。扫码详测用S01～S05、S07～S12，S06范围外；更新详测用U01～U12。下表只保留跨能力回归要求，不另外新增任务阶段。

| ID / R映射 | Given / When | 必须观察的Then及负例 | 主TASK |
| --- | --- | --- | --- |
| A06A-01 / R1/R2 | 无Vite的已安装包，使用合法部署配置；冷启动聊天深路由，刷新并恢复身份，访问当前认证分支启用的API/Hub；Web另回归显式同源API路径及embedded | 本地资源完整、远端请求使用有效入口；缺配置/跨源/坏证书明确失败，不回localhost、不关闭安全校验；embedded不附Bearer、不装配管理API/Hub，会话失效仍走宿主重新进入页 | 001 |
| A06A-02 / R1/R6 | 已有种子库和最低权限账号；从PC/PDA实际菜单进入，读/配置/手动检查权限各自撤销，再启动必更 | 新旧库入口一致且不覆盖自定义；无权业务按钮/直接调用均拒绝，登录前受信启动更新不依赖这些业务权限；中英/明暗/窄屏/200%/键盘/软键盘通过；用户管理查询、原聊天菜单和PDAEnter不回归 | 001 |
| A06A-03 / R1/R5 | A登录并留草稿、通知、设备会话；刷新并发换B/过期/撤权，重新启动；分别覆盖bearer与现有Web embedded，原生embedded按已冻结支持矩阵 | 旧Token/草稿/句柄/事件不落入B；HTTP与宿主epoch一致；原生无sessionStorage明文Token；embedded不镜像Cookie或恢复缓存身份；旧刷新/通知点击不能复活A；存储不可用无静默降级 | 001 |
| A06A-04 / R2/R3 | 一个bridge命令处理中；同ID同payload、同ID异payload、超限/未知版本/子frame/伪Origin、取消后迟到成功/失败 | 仅一次设备调用；非法输入被真正接收端拒绝；旧回包不影响新状态且已取得资源释放；当前错误保留，物理结果不明标Unknown | 001 |
| A06A-05 / R3 | Android手机有当前输入/相机会话；同码两次、重放同event、Enter/Tab/None/IME、切页/切人后的迟到结果 | 两次真实码均接收、重放一次；不误发聊天/表单、不丢前导零/GS1；失去所有权不投递；不加载PDA广播receiver | 001 |
| A06A-06 / R3 | 真实Android手机相机/实际目标BLE被拒/永久拒绝/设置撤权/断连；write发出后丢响应，后台后返回 | 不循环授权；相机/连接释放；写结果未知不自动重发；重连生成新ID、旧notify无效；实际设备值/回应被核对，demo不冒充SPP | 001 |
| A06A-07 / R2/R6 | 受控测试文件/固定打印测试页；取消选择/保存/打印、非法路径和任意HTML、设备忙 | 不越出句柄/目录/模板边界；实际系统对话框且人工出纸与SystemOnly分开；无标签队列；同机Agent已拥有设备时不抢占 | 001 |
| A06A-08 / R2/R5 | Windows与第二真实端已通话/共享；最小化、X关闭到托盘、恢复、页内浮窗缩放再拖动、退出/锁屏 | 视图操作保持媒体且音频只播一次；退出/锁屏按§7.4结束资源及对端；选源取消/源窗口销毁清对应轨道，不默认共享第一屏 | 001 |
| A06A-09 / R5 | 两种能力已建立；停止一项、同时新增另一项、迟到Ready/Answer/错误；多设备抢接/权限撤销 | 按PF06上下文/协商规则，单项失败不误停另项；观察实际音频/视频及本地轨道；协议PASS不能扩大成真实媒体PASS | 001 |
| A06A-10 / R4 | 发布新页面及外壳；Windows/Android分别冷启动、恢复登录、退出再登录、取消OS安装 | 两端页面热更新均不重打外壳；Windows安装包/APK各一次升级；有明确提示/进度，无稍后/跳过；实际版本/hash匹配，取消安装仍不进业务 | 005 |
| A06A-11 / R4 | 错appId/环境/通道/架构、过期清单/旧sequence/未知keyId/签名hash篡改、bridge/API不兼容 | 在执行前拒绝对应包，旧文件保留供修复，启动失败不进业务；不以签名有效跳过版本/身份，不以HTTPS代替签名 | 005 |
| A06A-12 / R4 | 下载断网、磁盘满、解压穿越/异常大小；在暂存写盘、slot切换、health前分别中止进程再启 | 失败临时文件删除后从头重试，不拼坏包、不写越界、不丢lastGood；同包不循环激活，旧activation健康回包拒绝 | 005 |
| A06A-13 / R4/R5 | 使用中发布新版，保留表单/扫码/文件/媒体；切后台/托盘再恢复；随后终止隔离实例再开；下载时再发布一版 | 使用中及普通恢复不切版本、不触发自动安装；重新启动必须完成当前发布目标更新；旧资源/动态chunk可用；下载不混包，更新失败/断网只可重试或退出 | 001/005 |
| A06A-14 / R4/R6 | Bundle入口/动态chunk坏、bridge错误；断网但本地包正常；诊断注入秘密样本 | 坏包60秒内进入修复界面，离线不误判本地包损坏，但启动检查失败不放行业务；lastGood不能绕过必更；诊断脱敏断言、容量/保留/取消保存均通过 | 005/001 |
| A06A-15 / R2/R3/R6 | 真实分发环境安装、覆盖升级、卸载/重装；Windows受限权限、APK签名不符/安装权限拒绝 | 支持的安装路径真实完成，失败有恢复说明；配置/凭据按边界处理，未安装机器/模拟器不计真机 | 001/005 |
| A06A-16 / R5 | Android手机前后台/锁屏/强杀/旋转/OS授权弹层；有新消息/旧通知，再冷启动 | 清理与受控外部流程区分；无重复receiver/媒体Host；恢复先身份后权威消息，点击重新鉴权；未启用push时杀进程后通知不记送达PASS | 001 |

测试可用可控延迟/故障注入证明竞态，但明确标模拟层；必须保留真实设备及两端业务结果。进程中止只对本任务隔离测试实例，不能结束用户IDE/当前服务。验收数据使用已批准隔离身份/设备测试内容，不扩权、新建账号或清理普通业务数据来绕过前置。

### 8.4 证据、支持声明和命令

每个案例记录`caseId、容器/布局/OS/WebView及版本、Native/Web/bridge/API契约、设备/固件/adapter、权限状态、包来源/签名验证摘要、Given/实际操作/Then、结果、证据路径`。双端媒体另记录发送/接收方、采集/连接/播放/停止各层；分轨时限沿PF06§8.3，无实际测量不补造数字。PF06用户已通过的Web停止基线继续引用原工作包，不再次当成当前缺陷。

结果只用PASS/FAIL/BLOCKED/NOT RUN/用户后置/不适用（写原因）；Unknown能力核验不能改写为Unsupported已通过，未授权和不支持也不能混用。网络/TURN/同条件画质/30分钟专项明确标后续云部署，任务受影响时引用原安排。任务001若决定首版不提供某种原生能力，必须先回写范围/按钮/验收三处，不靠标“不适用”临时缩小交付。

稳定交接写入`docs/evidence/PF-06A.md`，包含实际HEAD、既有WIP、本轮manifest/逐文件hash、修改范围、用例矩阵和剩余项；不得创建虚假执行证据。本轮采用双角色模式：开发完成自测并一次交接后停止写入，主控直接完成后续调整、测试和验收；复用未受影响的通过证据，对主控修改补验受影响路径并执行仓库必需门禁。

当前已有前端命令在`src/frontend`执行：`pnpm typecheck`、`pnpm lint`、`pnpm exec vitest run <明确spec路径>`、`pnpm build`；测试必须核对实际筛选范围和退出码。原生构建/签名/安装命令由001从实际工程工具链登记，不能用前端build替代。若本包修改后端权限/种子源码，最终按仓库先`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`再`dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`；解决锁后必须新鲜重建。纯文档复审只检查引用/结构/契约一致性，不跑业务全套测试。

## 9. SQL Server 2019/2022接入

### 9.1 目标与当前结果

2019／2022 共用 `SqlServer` Provider、同一套业务模型和 SQL Server 迁移，不分别开发两套实现。当前用户提供 SQL Server 2019；本轮完成代码接入，并在 `industrial_platform_sql2019` 隔离库验证全部 109 张现有表和四服务初始化。2022 没有实例，真实测试记 NOT RUN，不把 2019 测试写成 2022 测试。

| 服务 | 表数 | 包含范围 |
| --- | ---: | --- |
| Identity | 23 | 用户、角色、权限、会话、SSO、引导凭据、幂等、审计、种子/迁移/Outbox；含两个 PF05 技术安全表 |
| SystemData | 42 | 组织任职、菜单/功能/主题、数据库编排、文件、通知、审计、技术账本 |
| ReferenceData | 24 | 七个业务模块、缓存代次、Outbox、种子及迁移账本 |
| Collaboration | 20 | 消息/成员/附件、合规、语音/共享持久事实、Inbox/Outbox及迁移账本 |

当前代码可进入 SQL Server 2019 开发联调。现用开发运行库仍为 PostgreSQL；隔离测试不会自动切换正常服务。SQL Server 2022 实机、客户生产部署、备份恢复与完整终端联合验收另外登记，不能由自动化测试代替。各项实际证据见[全表验证报告](../evidence/PF-06A-SQL2019-compatibility-check.md)。

不做 PG→SQL Server 业务数据搬迁，不新增跨库双写或运行中切换，不扩展数据库运维平台。既有备份/审批/权限流程沿原边界。

### 9.2 什么时候使用哪种数据库

| 场景 | 引擎和选择方式 |
| --- | --- |
| 当前日常开发 | PostgreSQL；沿现有云 Docker 和私有配置 |
| 本轮真实兼容验证 | SQL Server 2019 隔离库；只读取 `SqlServerAcceptance:2019:ConnectionString` |
| 用户切换后的 SQL 开发联调 | 启动前明确选择 `RemoteDevelopment:DatabaseProvider=SqlServer`；四个组合服务共用 SQL Server |
| 快速隔离测试 | 显式 `IndustrialPlatform:DevelopmentInfrastructureMode=Sqlite`；不能替代真实 SQL 类型/并发验证 |
| 正式部署 | 部署方固定一个已验收 Provider，并配置明确目标；不发现或加载开发者私有文件 |

Provider 在启动时确定，不按页面或单张表临时选库；连接失败不会退到另一引擎。Shared/PerService 控制物理库拓扑，不代表 PostgreSQL/SQL Server，也不代表进程数。UnifiedHost 只有一套连接，其组合模块使用同一引擎和物理库；分布式服务使用显式映射。切库不复制或合并数据，各库拥有独立用户、菜单与业务事实。

### 9.3 配置与运行入口

普通开发只维护被 Git 忽略的 `src/backend/appsettings.Development.local.json`；同目录 example 是模板。测试连接与正常运行连接放在同一文件的不同节，避免维护第二套中间件配置。

| 配置 | 含义与要求 |
| --- | --- |
| `RemoteDevelopment:DatabaseProvider` | `PostgreSQL`（缺省）或 `SqlServer`；未知值拒绝，不回退 |
| `RemoteDevelopment:SqlServer:Host` | SQL Server 服务器；不从验收连接串自动复制 |
| `Port` | 明确 TCP 端口，本轮为 1433，允许 1～65535 |
| `UserName` / `Password` | 受控 SQL 登录凭据；不得进入 Git、聊天、日志或字段字典 |
| `Encrypt` | Development SQL 合同要求 true |
| `TrustServerCertificate` | 必须显式配置；正式部署 false 并配置可信证书；隔离开发可明确设 true |
| `ConnectTimeoutSeconds` | 默认 15，允许 1～120 |
| `CommandTimeoutSeconds` | 默认 30，允许 1～120 |
| `DatabaseTopology:Mode` | `Shared` 或 `PerService`；普通 Shared 只用于 Development，独立宿主沿原显式 Standalone 语义 |
| `SharedDatabaseName` | Shared 的实际物理库名；本轮隔离目标 `industrial_platform_sql2019` |
| `SharedDatabaseSchema` | SQL Server 固定 `dbo`；非 dbo 拒绝 |
| `SharedSqliteFile` | SQL Shared 时必须显式置为 `""`，覆盖 Host 自带的 SQLite 文件默认值 |
| `ServiceDatabases` | PerService 的显式小写服务键映射；漏映射拒绝，不推导库名 |
| `SqlServerAcceptance:2019:ConnectionString` | 仅受控测试消费；正常 Host 不用此节选运行库 |

连接使用 `Microsoft.Data.SqlClient 5.2.2` 的连接串构造器；SqlSugar Core 保持 5.1.4.216。Development 的库名只由 DatabaseTopology 解析，不在 SqlServer 节增加第二个 DatabaseName。

要用现有 2019 库联调时，在私有文件补齐 SqlServer 参数，再改 Provider、SharedDatabaseName、SharedDatabaseSchema、SharedSqliteFile，最后由用户在 IDE 重启调试。无须手工建 109 张表。已有隔离库已由本轮初始化，含受控验收数据；不把它当现用 PG 库的副本。

Staging/Production 不加载 `appsettings.Development.local.json`，使用部署提供的 `SqlSugar` 与 `DatabaseTopology`。独立 Collaboration 宿主仍使用唯一独立配置：`Standalone:Database:Provider=SqlServer`、ConnectionString、DatabaseName（若填必须等于连接串实际库）、Schema=dbo。它不能回退到平台私有配置。

### 9.4 类型与查询语义

| 项目 | 当前实现合同 |
| --- | --- |
| 主键/并发令牌 | Guid→uniqueidentifier；保留整数 OptimisticVersion 与 GUID ConcurrencyVersion 双版本，不改用 rowversion |
| 时间 | DateTimeOffset→datetimeoffset(7)，应用按 UTC 保存/使用；日期值不附加时区 |
| 布尔/整数 | bool→bit；long→bigint；已实测 Int64 最大值 |
| 小数 | 保留领域精度/scale；动态属性使用 decimal(28,10)，已实测 999999999999999999.9999999999，不经 double |
| 中文文本 | nvarchar；可索引字段使用明确长度，非索引长文本使用 max；具体长度见逐字段字典 |
| 比较/去重 | 业务标识列采用 Latin1_General_100_BIN2；领域继续负责标准化与空白限制，不能依赖实例默认 CI 排序规则 |
| JSON | nvarchar JSON 与 ISJSON CHECK；保持事件/配置序列化合同，不使用 PG jsonb 方言 |
| nullable 唯一范围 | 持久计算 tenant_scope 等既定范围，过滤索引保留软删除及 Draft/Current 规则 |
| 长 SSO URI | normalized_uri 保留长字符串，SHA-256 计算列参与唯一键，不因键宽限制截断 URI |
| 关联软删除 | 普通关系复合 FK ON UPDATE CASCADE；自引用关系用 ID 外键和专用 AFTER 触发器同步/校验快照，避免多重级联路径 |
| 原生 SQL | SQL Server 采用 TOP/OUTPUT/UPDLOCK/HOLDLOCK 等独立路径；PRAGMA、ON CONFLICT、RETURNING、PG cast 不进入 SQL 分支 |

完整物理表、字段、默认值、索引、FK 与 CHECK 在[全表字段字典](details/PF06A-SQLServer全表字段字典.md)，不在本章再复制 1777 行。

### 9.5 迁移、事务与并发

四服务使用各自嵌入的不可变 SQL Server 脚本。Provider 选择已贯通注册、目标解析、凭据、路由/DI、初始化、目标检查/迁移/种子与锁。PG/SQLite 继续走原脚本和原 checksum 输入。

| 位置 | 原子性和锁 |
| --- | --- |
| Identity/SystemData/Collaboration 本地迁移 | 数据库会话锁覆盖账本检查到全部步骤完成；每步 DDL 与记账同事务，失败回滚 |
| UnifiedHost/独立宿主完整初始化 | 同库 `industrial-platform:initialization` 会话锁覆盖四服务初始化；显式释放后关闭持锁连接 |
| ReferenceData 初始化 | 同连接事务级 sp_getapplock，迁移/种子原子提交；其他 Provider 不变 |
| 用户分配 | 租户/用户稳定 SHA-256 资源键，Transaction owner；关键区与仓储共用事务，Commit 或 Dispose 回滚释放 |
| 编码序号 | UPDLOCK/HOLDLOCK 下分配；相同幂等请求重放不再次分配 |
| 缓存代次 | 行/范围锁下插入或更新，尊重外部业务事务；不引入 MERGE |
| 工作器领取 | TOP(1)+UPDLOCK/READPAST/READCOMMITTEDLOCK；租约与双版本更新防止重复领取 |
| 查询预算/文件引用 | 并发不突破既有额度；文件引用/保全与删除竞争通过锁和状态条件互斥 |
| 目标库迁移/SQL种子 | 事务锁、步骤和账本同事务；锁内重查种子幂等事实，避免并发重复副作用 |

锁负返回码拒绝成功；初始化和用户锁等待 10 秒。取消/异常必须释放连接或回滚，不能改成进程锁代替多实例互斥。消息、媒体、Outbox 等仍使用既有业务版本/租约合同，不新增协调平台。

### 9.6 人工接手与维护入口

#### 9.6.1 服务目录

代码在 `src/backend/src/Services/{Identity,SystemData,ReferenceData,Collaboration}`；SQL Server 脚本在各 Infrastructure 的 `Persistence/Migrations/SqlServer` 或 `Persistence/SqlServer`。表名固定 dbo 加所属服务前缀；防重放与再次认证授权两张安全表归 Identity。

#### 9.6.2 配置合同

正常运行用 §9.3；测试用 acceptance 节。唯一私有配置、覆盖顺序及 Shared/PerService 操作见[数据库开发注意事项](../../src/开发注意事项-数据库拓扑与本地配置.md)。配置变化后需由用户重启 IDE 调试，文件已改不表示旧进程已加载。

#### 9.6.3 账本和 checksum

本地三个服务保留 SHA-256(Id+Description) 算法；SQL Server Description 纳入对应脚本内容哈希。ReferenceData 保持其脚本规范化内容 checksum。目标库迁移/种子各自记录内容摘要、版本和执行序号；同版本不同内容拒绝。checksum/SignatureRef 不等于已实现密码学私钥签名，本轮不扩展签名体系。

已应用步骤不改 SQL、checksum 或 applied_on。ReferenceData `2.7-012` 是本轮发现长度 CHECK 后新增的 SQL 修复；`2.7-013` 新增表/列说明。Identity/SystemData 的描述 v2 替代尚未发布的描述入口，保留隔离库已经记录的旧描述 ID，不删账本；新库直接应用当前描述 v2，避免分库时引用其他服务表。描述脚本只改元数据。

本轮还修复两个同名 SchemaMigrationRecord 类的 SqlSugar 映射混淆，SystemData 改名 SystemDataSchemaMigrationRecord 并显式 AS。隔离库首次测试误入 Identity 的 58 条 SystemData 记录已按 ID、内容和 checksum 与正确账本逐条一致的条件移除；正确账本/业务数据未重置。修复仅针对本轮初始空隔离库，不是日常清库办法。

#### 9.6.4 后续改动检查

2026-10-06 命名修订：表、模型和持久化类均按领域命名。当前安全表为 `identity_service_call_nonce`（受信调用防重放）、`identity_step_up_grant`（敏感操作再次认证授权）。SQLite/PostgreSQL 追加 `ID-022-01`，SQL Server 追加 `SQLSERVER-999-TableNames`；SQL Server 在历史说明脚本之后执行改名，表数据、对象 ID、索引和扩展属性保留。已应用脚本、描述和 checksum 不改写。新旧表同时存在时报错并回滚，不自动合并或清空。SystemData 持久化类使用 `SystemDataOperationalStore`、业务异常使用 `SystemDataOperationException`，前端 API 模型使用 `SystemDataOperationsApi`；不改变接口路径或业务字段。

新增或修改数据库路径时，补 SQL Server 独立脚本及受影响业务测试；追加新迁移 ID，不原地改历史。Inspect 保持只读；缺列/缺索引/权限不足分别报告。复核 Shared 与 PerService 的目标身份，避免跨服务账本和字段映射串表。

#### 9.6.5 表实体字段字典

[全表字段字典](details/PF06A-SQLServer全表字段字典.md)覆盖 109 张表／1777 列，包含用途、实体入口、类型/长度/NULL/默认/计算、中文含义与维护、索引、FK/CHECK。所有表和列在 SQL Server 有 MS_Description，可直接从 SSMS 查看。未来改动同时更新字典与追加描述迁移；只读 Inspect 不补注释。

### 9.7 验证结果和待验边界

本轮不为了等 2022 实例阻塞 2019 开发联调，但也不关闭“两引擎完整生产支持”声明。自动化和人工/部署结果分开：

| 编号 | 当前证据 |
| --- | --- |
| SQL-A01 | 2019 major=15、compatibility=150、实际隔离库验证通过；2022/160、生产 TLS 和权限负例待真实环境 |
| SQL-A02 | Provider、Shared/PerService 解析、漏映射和独立宿主配置测试通过；2019 同库四初始化器 Ready；真正四物理库/分布式部署待验 |
| SQL-A03 | 2019 全 109 表创建、增量修复、种子与二次应用通过；并发目标种子只执行一次；多进程全新库同时首次初始化待部署验收 |
| SQL-A04 | SQL catalog 检查已接入，全部表/列说明实际可查；结构缺损/权限隐藏的 SQL 实机负例待专项 |
| SQL-A05 | 中文/Guid/UTC、并发版本、Json、精确 decimal/Int64、编码去重与分页通过；全部边界值不是逐一手工跑完 |
| SQL-A06 | Identity 用户存取/登录名去重、七模块发布与读写、文件引用/保全、Collaboration 可见性/预算通过；IDE 登录/动态菜单/完整聊天与媒体联合链待用户联调 |
| SQL-A07 | 编码并发、相同请求重放、预算上限、工作领取、用户锁、迁移锁争用/释放、失败回滚与 seed 幂等通过；真实断连/死锁/多进程故障、媒体 slot 与 Outbox 重投全矩阵待专项 |
| SQL-A08 | 目标检查、已存在库、迁移幂等/内容漂移/回滚与 SQL 种子通过；创建登录/角色权限、备份恢复与 2019→2022 升级未实机执行 |
| SQL-A09 | 新鲜 Release 编译及全量回归结果见最新证据；PG 历史脚本与 checksum 不改，外部环境门控跳过不计实测 |
| SQL-A10 | 本轮未替用户启动正常服务或重跑手机；SQL 运行库切换后的客户端最小业务链待用户 IDE 联调 |

执行真实 2019 验证：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release -p:PF06A_SQLSERVER_ENABLED=1
$env:PF06A_SQLSERVER_ACCEPTANCE='1'
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build -m:1
```

默认编译跳过真实 SQL 用例，不连接受控库；显式启用时只允许本轮指定隔离库，先核对真实引擎。测试保留随机 sql-check 租户事实，临时探针表只删除本测试生成的名称，不清理普通业务数据。

2022 实例具备后，在同一私有文件增加 `SqlServerAcceptance:2022:ConnectionString`，使用隔离库 `industrial_platform_sql2022`，设置 `PF06A_SQLSERVER_ENGINE_YEAR=2022` 后执行同一命令。测试会核对真实 major=16；未提供实例时不运行、不记 PASS。SqlSugarScope 的超时通过配置回调应用，构造器不能提前获取底层执行上下文；已有八路隔离回归保护。
