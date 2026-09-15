# PF06A 运行时字段、更新与页面规格

版本V1.4；2026-09-14；对应实施09A。派遣状态待派遣；就绪度待实际目标核验。遵循[派遣前规则](../STANDARD-派遣前详细设计与页面验收.md)。终端Runtime本身**无业务SQL表**，不得为了“表名和字段”建立空Schema；以下定义JSON、TypeScript桥接DTO和本地状态结构。2026-09-14补充为待实施设计；G06A-1内的设备、依赖、签名及外部输入仍待核验，不能把文档细化写成实现/真机PASS。

## 0. 复审依据、范围与防返工映射

2026-09-14用户追加SQL Server 2019/2022支持：作为主方案TASK-PF06A-009，详见[SQL Server接入补充规格](PF06A-SQLServer2019-2022接入补充规格.md)。本文件“无业务SQL表”只指终端Runtime自身，009适配现有平台服务的数据库持久化；不让客户端直连数据库。G06A-SQL单独核验，原16组终端案例/001～008映射保留，SQL-A01～10见补充规格§7。

已读取[共同协作约定](../../agents/开发协作与页面统一约定.md)、[工程笔记](../../agents/ENGINEERING-NOTES.md)、[PF06复盘](../../agents/PF06开发注意事项-减少联调返工.md)、[PF06当前工作包](../../tasks/active/PF-06.md)及现有前端装配/认证/路由/聊天接入。PF05/06业务权威仍为各自规格；本文件只补原生容器相关契约。

| 已证实教训/本次发现 | 本阶段具体预防要求 | 落点/案例 |
| --- | --- | --- |
| PF05附件先授权/先绑定循环、调用端mock掩盖action不一致 | 每个原生动作写产生前置的主体；先真实登录→业务会话→设备授权→实际结果，使用实际接收adapter | §2/7；A06A-03/05/09 |
| PF05版本用途混用、UTC落库回读偏移 | 明确epoch、请求ID、配置修订、包版本、安装versionCode和媒体版本；持久文件重新读回、重启后再判时间 | §1/2/3；A06A-04/12/13 |
| PF06旧成功/旧错误/fallback覆盖新上下文 | 原操作完整上下文随成功、失败、取消、事件保留；没有来源不能猜当前主体 | §2.1/8.3；A06A-04/09 |
| PF06状态成功不等于真实声音/画面、视图变化误停媒体 | 单一媒体Host；观察实际轨道、播放和对端结果，沿PF06标准取证 | §8；A06A-08/09 |
| PF06页内浮窗被理解成新业务窗 | 原生只保留一个业务renderer；选源弹层与远端观看浮窗分开 | §4.4/8.1；A06A-08 |
| PF05扫码Enter、菜单/普通查询回归 | 显式扫码输入归属；公共路由/权限/种子增量同时回归旧入口 | §2.4/4.3/7；A06A-02/05 |
| 当前bearer/mock写sessionStorage，embedded使用HttpOnly会话；API和Hub各读配置 | 分认证模式适配容器存储，保留embedded恢复/刷新及Web同源路径；启动前冻结唯一有效配置，不增加第二套身份协议 | §7.2/7.3；A06A-01/03 |
| 旧规格打印仅有能力查询，但003要求真实基础打印 | 仅增加固定非业务测试页+系统打印对话框；业务作业/回执仍归PF10B | §2.2/2.5；A06A-07 |
| 更新流程缺桥接查询/健康回报、busy原子边界及插件映射 | 补齐状态DTO、单更新所有者、激活锁、持久恢复和实际插件适配证据 | §1.2/2.2/3；A06A-10～14 |

PF06已确认的双向发起、语音/共享共存、实际媒体、浮窗和停止现场结果保持有效。其网络/TURN/质量/30分钟测试在云部署阶段，权限/多端/异常及三端支持在本阶段目标容器可用后接续。PF05真实ClamAV仍按原main前安排，不因此扩张本阶段范围。不得把这些历史延期恢复为001禁止开工的条件。

## 1. 文件与字段类型

生产配置由容器受控写入，不由普通网页任意修改。普通配置不含AccessToken、刷新Token、签名私钥、Bluetooth配对凭据；安全凭据由OS安全存储管理。文件原子替换、旧文件在新文件完整校验前保持有效；路径在容器私有目录，不接受远端任意路径。时间点为UTC RFC3339字符串，超时用单调时钟。

| 对象/归属 | 精确字段（JSON/TS类型） | 默认/限制/生命周期 |
| --- | --- | --- |
| runtime-config.json；本地容器 | schemaVersion:1,configRevision:string='0',environmentNId:string<=64,deploymentEnvironment:'DEV'或'TEST'或'UAT'或'PROD',apiOrigin:string<=2048,apiBasePath:string<=256='/',allowedOrigins:string[],terminal:'pc'或'pda',channel:'stable'或'pilot',scannerMode:'keyboard'或'broadcast'或'camera',suffix:'Enter'或'Tab'或'None',diagnosticConsent:boolean=false | apiOrigin为不含路径的受信HTTPS Origin；与apiBasePath组合为现有apiBaseUrl。allowedOrigins仅为API允许目标，不兼作页面/更新白名单；页面Origin、更新信任见§7.2/9。环境字段必填；本地开发显式例外，不接受通配；升级保留合法用户配置 |
| release-manifest.json；发布侧签名 | schemaVersion:1,releaseNId:string32,platform:'win-x64'或'android',packageKind:'installer'或'apk'或'web-bundle',channel,version:string<=64,nativeMinVersion:string<=64,bridgeMinVersion:number,bridgeMaxVersion:number,securityFloorVersion:string<=64,artifactUrl:string,artifactSha256:string64,artifactSizeBytes:string,issuedOn:string,expiresOn:string,keyId:string<=64,signature:string | max/min按版本比较而非字符串字典序；manifest≤64KiB；SHA256小写hex；artifact大小非负十进制Int64；下载来源白名单；签名算法/公钥分发由G06A-1选型后冻结 |
| update-state.json；本地更新器 | schemaVersion:1,state:UpdateState,currentNativeVersion:string,currentWebVersion:string,lastGoodWebVersion:string,pendingReleaseNId?:string,pendingArtifactHash?:string,downloadedBytes:string='0',activationAttempt:number=0,lastErrorCode?:string,updatedOn:string；补充恢复字段见§1.2 | UpdateState见§3；不是业务审计；包状态不可与私钥混存；每次切换原子写入 |
| scan-session；仅内存 | sessionNId:string32,ownerPageId:string<=128,subjectEpoch:string,source:'keyboard'或'broadcast'或'camera',acquiredOn:string,cancelled:boolean=false | 全容器唯一活动扫描会话；离页/换人release；不得持久缓存条码业务值 |
| ScanResultV1；桥接消息 | contractVersion:1,eventNId:string32,sessionNId:string32,value:string(1～4096),source,symbology?:string<=64,receivedOn:string,subjectEpoch:string | 不以条码value去重；按eventNId去重，连续两次相同业务码可各自上报；桥接重放不重复处理 |
| CapabilitySnapshotV1 | contractVersion:1,container:'web'或'electron'或'capacitor',os:string,osVersion:string,nativeVersion?:string,webVersion:string,bridgeContractVersion:number,capabilities:CapabilityEntry[] | Entry={name:§1.1枚举,availability:'Supported'或'Unsupported'或'PermissionRequired'或'Degraded',reasonCode?:string}；设备支持与用户权限分开；Unknown核验结果不得伪装为Supported |
| BridgeRequestV1 | contractVersion:1,requestNId:string32,method:白名单枚举,payload:该方法DTO,subjectEpoch:string | 请求≤64KiB；reject未知method/version/origin；不提供eval/shell/sql/fs任意路径 |
| BridgeResponseV1 | contractVersion:1,requestNId:string32,ok:boolean,data?:对应DTO,error?:{code:string,retryable:boolean,outcome:'NotExecuted'或'Failed'或'Unknown'},subjectEpoch:string | ok=true必须有对应data且无error；ok=false必须有error且无data；取消不是设备成功。不同结果按§2.1/2.6处理 |
| device-profile.json；容器配置 | schemaVersion:1,profileNId:string32,kind:'ble'或'printer'或'scanner',displayName:string<=200,adapterKey:string<=128,deviceHandle:string<=256,settings:已验证adapter DTO | deviceHandle不是认证因素；只保存受权绑定，秘密走安全存储；adapter尚未定型不开放任意settings |
| diagnostics；滚动本地文件 | occurredOn:string,level:'info'或'warn'或'error',code:string<=96,requestNId?:string32,adapterKey?:string<=128,durationMs?:number,version:string | 不记条码/消息/标签/地址/Token；最多10MiB×3文件，默认7天；导出前脱敏 |

备份/恢复不复制原机凭据；重装重新绑定。变更插件、Android权限、AAR/JAR或原生bridge必须完整APK/安装包升级，不由web-bundle掩盖。

人工接手说明同样适用于本节JSON/TS、本地状态和内存记录，格式沿[共同规则§3.1](../STANDARD-派遣前详细设计与页面验收.md#31-面向人工接手的表实体与字段说明)。这些对象无业务SQL表，物理列标为不适用，仍须解释每个字段的中文含义、来源/写入方、空值/默认、变化时机、敏感性和恢复用途，并在后续TS/原生DTO中同步注释；不能只列压缩类型串。

`scan-session`说明示例：一条记录表示当前页面获得的一次扫描输入所有权，仅存在容器内存，按离页/换人/生命周期规则释放，不是持久业务单据或硬件身份。以下六字段与上表定义对应，具体权限/取消语义继续沿§2，不新增接口。

| 字段 / 中文名 | 含义、来源与维护注意事项 |
| --- | --- |
| sessionNId / 扫描会话标识 | 成功取得所有权时由容器分配的nid；关联本次扫描结果与release，重新取得时用新值；不能拿设备编号或条码内容代替 |
| ownerPageId / 所属页面标识 | 调用方提供已登记PageId，宿主按当前受信页面与权限检查；用于路由退出释放和结果投递；显示标题、翻译文案不是稳定PageId |
| subjectEpoch / 主体隔离代次 | 来自容器身份协调器的当前代次；识别结果是否属于原会话主体，换人后旧结果拒绝；不授予业务权限，不当用户ID保存 |
| source / 扫码来源 | 本会话已选定的keyboard/broadcast/camera路径；用于适配器及重复通道控制；不根据扫描到的文本猜来源，也不能表示设备型号 |
| acquiredOn / 取得时间 | 成功取得扫描所有权时记录的UTC时间点，用于诊断；不是条码业务发生时间，不能替代单调时钟进行进程内超时计算 |
| cancelled / 已取消标记 | 初始false；取消/失效后阻止继续向该会话投递有效结果，仍按§2清理迟到资源；true不表示物理设备此前操作一定未发生 |

其余配置、发布清单、更新状态、设备profile和bridge DTO按同一粒度在对应对象旁补说明；本例不能替代完整字段字典。特别说明`configRevision`与`schemaVersion`、`requestNId`与`eventNId`、`releaseNId`与版本号、`securityFloorVersion`与最后良好版本的不同用途，避免人工接手时误合并字段。

### 1.1 公共类型、能力与版本身份

- 本文`nid`/`string32`为随机UUID的32位小写hex；`key`为外部原始业务标识，长度沿现有领域DTO，不强改成32位。`subjectEpoch`为容器身份协调器生成的不透明随机nid，不使用用户名或自增业务版本代替。`configRevision`及递增技术序号使用非负十进制Int64字符串，JS以BigInt比较；桥接版本为正的安全整数。
- 时间序列化必须带`Z`，例如`2026-09-14T01:00:00.000Z`；非法/无时区输入拒绝。单调时间只用于当前进程超时，不跨重启持久化复用。签名有效期依可信UTC；时钟明显异常阻止激活并提示校时，不能反复重试或增加过期宽限。
- `NativeVersion`/`WebVersion`/安全下限/兼容范围均使用SemVer，不含`v`前缀；stable拒绝预发布版本。Android的`versionCode`为正整数，独立于显示`versionName`/SemVer，不能按名字推算可升级性。生产001需登记appId/packageName、Windows安装身份、架构和版本映射。
- 能力名固定：`scanner.keyboard`、`scanner.broadcast`、`scanner.camera`、`bluetooth.ble`、`files.pick`、`files.save`、`printer.systemTest`、`notifications.local`、`media.microphone`、`media.screenCapture`、`media.screenReceive`、`updates.installer`、`updates.apk`、`updates.webBundle`、`diagnostics.export`。SPP、远程推送、后台持续音视频不是以上能力的隐含子项。未核验实现对外返回Unsupported并附`CAPABILITY_NOT_VERIFIED`，支持矩阵仍登记Unknown，以区别硬件确认不支持。
- 能力快照只表达容器/设备/OS状态，不授予业务权限。`Supported`不表示已连设备、对方已同意、声音已播放或版本可立即安装。权限被拒/永久拒绝使用PermissionRequired及不同reasonCode，禁止每次刷新重新申请系统授权。

### 1.2 发布清单、恢复和页面快照补充字段

以下字段并入§1同名对象，是本次待实现契约的一部分；仓库尚无这些生产文件，不需要为本轮文档版本新增虚构迁移。

| 对象 | 必填补充字段/明确语义 |
| --- | --- |
| release-manifest.json | `appId:string<=200`、`environmentNId:string<=64`、`releaseSequence:string`、`webVersion:string`、`apiContractId:string<=64`、`nativeMaxVersion?:string`、`androidVersionCode?:number`（仅apk必填）、`signingAlgorithm:string<=32`；platform/packageKind组合只允许win-x64/installer、android/apk、android/web-bundle。appId是冻结应用身份，不能只按platform匹配 |
| 同一清单版本含义 | `version`为本产物版本；installer/apk时为NativeVersion，web-bundle时等于webVersion。`webVersion`始终为本产物装入的页面版本。`nativeMinVersion`/可选max为该页面所需宿主范围；完整包还须校验它实际包含的宿主符合范围。`securityFloorVersion`只属于该appId/环境/通道/packageKind的版本空间，不将APK版本与Bundle版本比较 |
| 签名覆盖 | 除signature本身外全部字段进入确定的签名输入，禁止接收重复JSON键、非规范数字或额外未定义字段。algorithm、canonicalization、signature编码/长度、公钥格式由G06A-1F给出跨语言固定测试向量；未冻结不能实现“校验成功”占位。artifactSha256作为被签字段绑定包内容；平台安装签名另行验证 |
| update-state.json | `updateEpoch:nid`、`stateRevision:string`、`currentReleaseNId?:nid`、`lastGoodReleaseNId?:nid`、`targetPackageKind?:enum`、`previousWebVersion?:string`、`activationNId?:nid`、`healthDeadlineOn?:UTC`、`resumeETag?:string<=512`、`verifiedManifestHash?:string64`、`highestAcceptedReleaseSequence:string='0'`、`effectiveSecurityFloorVersion:string`；按appId/环境/通道/包种类隔离。最后良好版本还须关联已验包hash和私有slot，不仅保存显示版本号 |
| UpdateStatusV1；只读页面快照 | `{contractVersion:1,updateEpoch,stateRevision,state,currentNativeVersion,currentWebVersion,targetReleaseNId?:nid,targetVersion?:string,downloadedBytes:string,totalBytes?:string,canDownload:boolean,canActivate:boolean,busyReasons:BusyReason[],lastErrorCode?:string,recoveryAction?:'Retry'或'Reinstall'或'ContactSupport'}`；不向页面暴露下载URL、临时目录或签名秘密 |
| BusyReason；显示用 | `{ownerId:string<=128,kind:'scan'或'form'或'fileTransfer'或'print'或'media'或'device',reasonKey:string<=128}`；本地化描述，不带条码/会话正文/文件名；lease只在内存由真实所有者管理，见§3.2 |
| DeviceStatusV1 | `{profileNId:nid,displayName:string<=200,kind:'ble'或'printer'或'scanner',availability:能力状态,connectionState:'Disconnected'或'Connecting'或'Connected'或'Disconnecting'或'Busy'或'Unknown',reasonCode?:string}`；row-key=profileNId，未知不能展示“已断开可重试写入” |

CapabilitySnapshotV1及devices.list返回体补充`subjectEpoch`、`snapshotSequence:string`；序号属于同实例同事件源，初始为'0'，对应onChange事件sequence。更新快照使用自己的updateEpoch/stateRevision，不与设备或能力序号比较。

内置部署策略（只随完整包/受控签名部署变更，非config.update对象）字段固定为`schemaVersion:1,appId:string<=200,rendererOrigin:string<=2048,allowedEnvironments:{environmentNId,apiOrigins:string[],apiBasePaths:string[],channels:('stable'或'pilot')[],apiContractIds:string[]}[],updateOrigins:string[],updateManifestUrl:string<=2048,trustKeys:{keyId,algorithm,publicKey}[]`。所有Origin精确匹配；公网/内网地址及公钥实际值由001核验。Bundle不修改该策略，runtime-config只能在策略允许集合内选择，不能自己扩大allowedOrigins。

配置读取实行“校验→归一→发布只读快照”，不能把部分合法字段与无效新文件混用。未知schema保留原文件并进入配置修复页；首次安装使用已校验内置配置，损坏升级配置只允许回到同环境合法备份。禁止静默退回开发地址或默认租户。普通参数更新需expectedRevision比较成功后原子落盘，失败保留原配置。

## 2. 方法签名与业务边界

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
| updater.check/download/activate | {} / {releaseNId} / {releaseNId} | UpdateStatusV1；activate须§3安全点，进度统一为downloadedBytes/totalBytes |
| lifecycle.onChange | 无 | {state:'foreground'或'background'或'resume'或'identityChanged'或'locked'或'suspending'或'terminating',subjectEpoch,lifecycleSequence:string} |

相同requestNId不同payload返回RUNTIME_REQUEST_CONFLICT；桥接至多一次响应不能当设备物理exactly-once。BLE和Classic SPP分别adapter，支持BLE不能声称支持蓝牙打印。PF06A仅冻结打印能力描述与受控设备边界，PF10B拥有打印作业/回执/重打状态。

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aa","method":"scanner.acquire","subjectEpoch":"1803c808b1ae482490905c923e0e9192","payload":{"ownerPageId":"runtime.scanner.test","source":"broadcast"}}
```

成功与设备忙反例（两种独立结果，不会同时返回；真实系统验证对应会话前置，不用示例替代授权）：

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aa","ok":true,"subjectEpoch":"1803c808b1ae482490905c923e0e9192","data":{"sessionNId":"6e18863084df4b16b2aa6a7ce6c6ffba"}}
```

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aa","ok":false,"subjectEpoch":"1803c808b1ae482490905c923e0e9192","error":{"code":"DEVICE_BUSY","retryable":true,"outcome":"NotExecuted"}}
```

广播adapter必须在真实型号上固定action/category/extras/编码/来源验证规则，不把上例当通用厂商广播协议。IMEs、键盘楔入与业务回车冲突通过scan-session归属解决，不全局拦截所有Enter。

### 2.1 请求校验、取消、事件与迟到结果

1. 接收端顺序：合法主frame及受信页面→DTO字节数/结构/版本→当前身份epoch→方法允许的权限及目标→请求幂等/设备所有权→调用。Origin匹配须URL解析后精确比较scheme/host/port；不能用字符串前缀、后缀或仅hostname。子frame、销毁窗口、外链页、旧renderer请求均拒绝。
2. 请求上下文键为`{containerInstanceNId,subjectEpoch,requestNId,method}`；instance在每次原生进程启动随机生成。主体/请求ID只在信封一处传输，禁止payload覆盖。请求规范化指纹不存秘密；同ID异method/载荷返回RUNTIME_REQUEST_CONFLICT，同ID处理中复用同一结果，不二次调设备。
3. 每个身份最多64个在途命令；完成记录最多256项、保留10分钟，仅用于同进程短期重入隔离。超过在途上限拒绝新动作RUNTIME_BUSY。调用方不跨重启/淘汰周期自动重放副作用请求；超时后经设备/业务权威查结果或由用户明确重新操作。该内存窗口不是打印作业幂等账本。
4. 取消/超时将请求标为不可再交付业务。底层不能取消时，迟到成功必须立即释放本次取得的扫描/摄像头/连接资源；迟到失败只进入脱敏诊断，不改当前页面错误。取消返回不能声称已撤销物理写入，无法确定结果标Unknown。
5. 订阅包装为`{contractVersion:1,eventNId:nid,eventType:'ScanResult'或'CapabilitiesChanged'或'DevicesChanged'或'UpdateChanged'或'LifecycleChanged',subjectEpoch,sequence:string,payload:对应DTO}`；更新事件另带updateEpoch/stateRevision。序号在同实例/事件源内递增；同源旧事件丢弃，不跨源比较。先安装监听再取快照，使用snapshotSequence/更新修订合并；卸载只移除本订阅，不能removeAllListeners误删别人的监听。
6. 扫码等瞬时事件不为下一个页面或身份补发；更新/连接/身份等状态通过get快照恢复，不以事件日志作为唯一事实。原生异步回调必须捕获启动时的上下文；禁止catch/fallback读取“此刻选中的会话”补来源。错误也按能力/原请求归属清理，不能一处成功抹掉其他错误。

### 2.2 页面闭环所需补充方法

以下与§2原方法一起构成白名单。`Promise`结果仍装入BridgeResponseV1；订阅用§2.1事件包装。尚未支持的方法返回稳定Unsupported，不能返回空成功对象。除bootstrap/身份协调内部端口外，业务方法要求有效登录身份。

| 方法 | 输入 → 输出 | 权限/作用及恢复 |
| --- | --- | --- |
| config.get | {} → {configRevision,terminal,channel,scannerMode,suffix,diagnosticConsent,environmentNId,deploymentEnvironment} | runtime.read；无可编辑服务器/信任根/密钥 |
| config.update | {expectedRevision:string,scannerMode,suffix,diagnosticConsent} → config.get同型 | runtime.configure；仅支持枚举且该模式已核验；有扫描会话则DEVICE_BUSY；不同修订RUNTIME_CONFIG_CONFLICT，不能覆盖 |
| devices.list | {} → {items:DeviceStatusV1[]} | runtime.read；最多100个已配置项，首版不建分页/搜索服务 |
| devices.onChange / capabilities.onChange | {} → 对应快照事件 | runtime.read；OS撤权/蓝牙开关变化后刷新；不自动弹授权 |
| permissions.openSettings | {capability:能力名} → {opened:boolean} | 当前动作对应业务权限；仅受控OS设置页，返回应用后重新检测，不假设已授权 |
| runtime.cancel | {targetRequestNId:nid} → {cancelRequested:boolean} | 仅本人本实例请求；幂等；不能取消别人的操作，也不是物理执行撤销保证 |
| updater.status / updater.onChange | {} → UpdateStatusV1 / 同型事件 | runtime.read；快照恢复进度；check/download/activate要求runtime.update |
| updater.defer | {releaseNId:nid} → UpdateStatusV1 | runtime.update；只把WaitingForIdle退回Staged，不能取消已经交给OS的安装或更换目标包 |
| updater.confirmHealthy（内部bootstrap专用） | {activationNId:nid,releaseNId:nid,webVersion:string,bridgeContractVersion:number} → {accepted:boolean} | 宿主校验当前加载slot及一次性activation，旧页/任意业务页不能确认；不依赖用户登录成功 |
| printer.printTest | {profileNId:nid} → {submission:'SubmittedToSystem'或'Cancelled',receiptLevel:'SystemOnly'} | runtime.configure；仅Electron固定本地非敏感测试页、copies=1、系统打印对话框，拒绝URL/HTML/原始指令；SubmittedToSystem不等于实际出纸 |
| diagnostics.export | {consent:true} → {artifactHandle:nid,displayName:string,sizeBytes:string} | runtime.configure；每次显式导出、先脱敏，交files.save；不后台上传 |

`files.pick`的purpose首版仅`collaborationAttachment`，mimeTypes/maxBytes还须与现有File/PF05限制取更严格值；`files.save`只消费当前身份有效的句柄，来源限已授权业务下载或诊断导出。句柄绑定epoch/purpose，10分钟无使用失效，单次成功保存后释放；上传结束/取消主动释放。文件内容通过内部adapter接入现有上传/下载流，不塞进64KiB桥接JSON，不另建绕过File授权的下载器。

相机、麦克风、选源和系统文件选择必须由当前明确用户动作触发。JavaScript方法存在不是用户已经同意。清理类方法（release/disconnect/cancel）允许在业务动作权限撤销后释放本人已有资源；彻底登出则由宿主主动清理，不要求过期Token请求先获服务端批准。

### 2.3 身份epoch与资源所有权

- 宿主身份协调器只接收现有认证装配层的会话安装/清除通知，经既有Identity可信身份核验后绑定tenant/user/session；epoch是本地隔离令牌，不能当服务端身份或授权。普通业务页面不能自行提供权限列表、tenant或epoch来切人。
- 初始未认证实例有匿名epoch，仅允许本地版本/修复提示及内部bootstrap，不能扫码、设备写入或进入业务导航。登录完成、登出、换用户、换租户、会话失效或权限失效时推进epoch并撤销旧操作。相同主体有效Token刷新可保留epoch，但必须单飞且拒绝旧刷新回包覆盖新身份。
- 换人顺序：先封住旧操作并更换epoch→立即停止本地捕获/扫描、释放连接/监听/句柄、清除通知和旧导航→清除凭据/业务草稿及内存缓存→完成新身份核验→恢复公共配置和新主体业务。失败留登录/错误页，不把旧用户状态暂借新用户使用。
- 路由退出释放page拥有的扫描/文件选择；App媒体资源由PF06 Host拥有，不随聊天视图卸载结束。更新下载/版本状态为安装实例级，不随换人消失；换人清旧订阅并重新检查runtime.update后才允许激活。device-profile为受控终端配置，可保留非秘密型号，但新主体不自动继承设备会话或业务授权。

### 2.4 扫码、键盘与Android广播的确定行为

| 场景 | 必须执行的行为 |
| --- | --- |
| acquire / release | 单实例只能一个活动会话；先订阅再让设备开始扫描；模式切换先停旧会话。release幂等，离页/失焦失去扫描所有权/锁屏/换人注销接收与清缓冲 |
| 键盘文本框 | 仅标记的当前扫描输入控件接收keyboard结果；普通文本录入仍走原逻辑。IME composing和普通聊天Enter沿PF05规则，不能靠全局按键速度猜扫描并拦截发送 |
| 结束符 | Enter提交当前扫描缓冲且阻止这一次默认提交；Tab提交后依页面焦点规则移动一次；None必须显式“确认输入”提交，不能等待一个不存在的结束字符。浏览器普通聊天无扫描会话时行为不变 |
| 内容与重复 | 保留前导零、大小写、合法GS1分隔符，不任意trim/数字转换；按已冻结adapter去除约定终止符。空结果拒绝，最多4096字符且受64KiB信封上限；相同业务码两次真实扫描生成不同eventNId、都交业务处理 |
| 广播字段 | 001填action/category、extras键及类型、symbology映射、字符编码、结束符、target package/component、所需permission、SDK/固件版本、是否厂家稳定事件ID。不允许把任意Intent extras透传页面 |
| 无厂商事件ID | 每次有效设备回调生成新eventNId，桥接重发沿用此ID；只注册一个receiver和一个输出通道。不用“同码100ms内只算一次”掩盖重复注册；厂商会重复发且无法区分时登记该型号限制并核验配置 |
| 来源保护 | action/category不证明发送者身份。按厂商能力验证显式目标、签名级permission/受控SDK等实际保护；需接收其他App广播时不能盲用NOT_EXPORTED造成收不到。不能验证来源的型号保留风险门禁，不称“已防伪造”；条码永远是待校验业务输入，不是执行命令 |
| 系统弹层/相机 | 临时后台（OS授权/扫描Activity）按关联request保留取消上下文，暂停键盘/广播；非本次外部流程或锁屏立即取消。结果返回先比对epoch/session再交付，取消后回调立即释放相机，不写日志或自动发送消息 |

### 2.5 BLE与基础打印边界

001必须用一台指定设备冻结adapter DTO：`adapterKey`、服务UUID/特征UUID清单、读/写/notify所需属性、编码/分帧/校验、MTU限制、业务命令枚举、应答关联、最大payload、连接和操作超时、重连及物理结果查询方法。字段尚无证据时保持§6未关闭，不开放`settings:any`或任意GATT字节写入。

BLE状态固定Disconnected→Connecting→Connected→Disconnecting→Disconnected；异常到Unknown/Disconnected，并保留具体原因。一个connection内有副作用操作串行；超时的写操作标结果未知，不自动补发。重连可恢复连接状态但不自动重做最后一次写入；每次重连生成新connectionNId，旧notify丢弃。OS撤权、蓝牙关闭、换人、进程结束均释放资源并更新能力快照。配对是OS行为，不缓存PIN。

PF06A的测试打印仅证明系统调用及人工观察测试页；不能用系统已接收证明标签完成。业务打印任务/持久幂等/超时未知/回执/重打归PF10B。已有Agent拥有设备时不再建立同机直连执行器；本阶段不提前提供通用printer.submit。PDA BLE读写演示不能作为SPP打印或生产打印验收。

device-profile首版由001冻结的受控部署配置提供；页面只测试/连接已有profile，不新增任意UUID/端口/协议编辑器。Bluetooth requestDevice只把用户选择绑定到该profile批准的adapter，不能把所有扫描到的设备自动授权；设备名与handle均不是认证凭据。

### 2.6 稳定错误与用户动作

| 错误码 | outcome / retryable | 页面处理 |
| --- | --- | --- |
| RUNTIME_UNSUPPORTED / RUNTIME_INVALID_ARGUMENT / RUNTIME_VERSION_UNSUPPORTED / RUNTIME_ORIGIN_DENIED | NotExecuted / false | 未支持或请求无效；不循环重试、不输出底层参数 |
| RUNTIME_IDENTITY_STALE / PERMISSION_DENIED | NotExecuted / false | 丢弃旧上下文或提示重新登录/受控设置；OS授权必须再次点击 |
| RUNTIME_REQUEST_CONFLICT / RUNTIME_CONFIG_CONFLICT | NotExecuted / false | 保留输入，读取当前值后显式修正，不能改ID自动重发 |
| RUNTIME_BUSY / DEVICE_BUSY / UPDATE_BUSY | NotExecuted / true | 显示当前忙原因，允许用户稍后重试，默认不重放设备命令 |
| RUNTIME_CANCELLED | NotExecuted或Unknown / false | 明确取消；底层可能已执行时保留结果未知 |
| RUNTIME_TIMEOUT / DEVICE_DISCONNECTED | NotExecuted、Failed或Unknown，按实际最早失败点 / Unknown时false | 未调用设备才可标NotExecuted；有写入可能则先查结果，不自动再写 |
| UPDATE_INCOMPATIBLE / UPDATE_SIGNATURE_INVALID / UPDATE_ARTIFACT_INVALID / UPDATE_ROLLBACK_FORBIDDEN | NotExecuted / false | 保留当前可用版本，拒绝本包；需新包/修复输入 |
| UPDATE_DOWNLOAD_FAILED / UPDATE_STORAGE_FULL / UPDATE_HEALTH_TIMEOUT / UPDATE_CLOCK_INVALID | 下载失败可retryable=true，其余false；激活后的健康失败为Failed | 区分下载重试、释放可清临时空间、回退或维修、校时；不删除用户资料 |
| RUNTIME_SECURE_STORAGE_UNAVAILABLE | NotExecuted / false | 原生凭据不能安全保存；允许明确的本次内存会话策略或重新登录，禁止静默明文降级 |

错误对象不包含原始异常/设备地址/Token。`retryable=true`只表示允许用户在条件变化后重试，不是SDK自动重试指令。

### 2.7 媒体端口与JSON桥接分开

共享Runtime在renderer内提供`media.captureMicrophone({operationNId:nid,subjectEpoch:nid}):Promise<MediaStream>`和`media.captureScreen({operationNId:nid,subjectEpoch:nid}):Promise<MediaStream>`，仅由PF06媒体所有者在既有用户手势内调用。取消沿operationNId撤销；返回轨道由PF06接管，迟到轨道由adapter立即停止。麦克风返回音频轨道，首版屏幕返回视频轨道且不自动附带系统音频；PF06继续拥有邀请、绑定、sender/receiver、协商、播放和停止。

MediaStream/MediaStreamTrack是同renderer内对象，不进入BridgeRequest/Response的JSON，不把视频帧/PCM/base64塞进普通IPC。Web adapter调用已有浏览器采集；Electron由受限选源处理器配合renderer媒体API取得轨道；Android adapter只有在001验证可交付现有传输使用的真实轨道后才实现发屏，否则按明确支持矩阵处理并先回写范围。Native不能另开第二个媒体Hub或用一张截屏冒充实时视频。端口错误映射保留取消/拒绝/不支持与operation来源，交PF06既有局部错误呈现。

## 3. 更新状态机与恢复

`Idle → Checking → Available → Downloading → Verifying → Staged → WaitingForIdle → Activating → Healthy`；无更新回Idle，失败到Failed，WebBundle激活失败到`RollingBack → Healthy/RecoveryRequired`。用户暂缓回到Staged；下载可恢复必须同时验证range/ETag/hash，不能只拼旧文件。错误码见§2.6。

下载/校验可后台进行；激活前由各业务所有者汇总busy lease（扫码、未提交表单、打印在途、屏幕共享），未知busy按忙处理，不能仅按没有打开页面推断空闲。锁定激活意图后复查busy与当前版本，防检查后又开始打印。强制安全更新也不能静默中断在途打印，展示受控结束/维护流程。

WebBundle启动健康窗口60秒，未收到可信健康确认回最后良好包；最多自动激活1次，不循环重启。健康确认包括Vue启动/bridge兼容/本地关键资源可用，不能把客户网络断开当包损坏。回退不能低于securityFloorVersion；没有安全可回版本进入RecoveryRequired并给维修入口。PC installer/APK回退能力受系统安装规则约束，不能保证静默降级；记录操作员介入步骤。

### 3.1 每步守卫与故障后的实际结果

| 当前状态/动作 | 守卫与可持久事实 | 失败/重复/恢复 |
| --- | --- | --- |
| Idle/Healthy/Failed→Checking | 单更新所有者；检查请求合并；读取当前appId/环境/通道/架构与版本 | 断网保留当前可用版本；未查到更新不是当前包不健康 |
| Checking→Available | HTTPS目标及每次重定向在白名单，清单签名/有效期/包身份/兼容/安全下限全部通过 | 篡改/过期/错通道拒绝；不能仅凭version更大显示“可安装” |
| Available→Downloading | releaseNId绑定已验证清单hash；磁盘预算校验；只写本次暂存目录 | 相同包双击共用下载，不并行覆盖；新release不得偷换进行中的目标 |
| Downloading→Verifying | 长度完整；检查最终SHA256/签名绑定，禁止在下载未完时试运行 | Range服务改成200时从头下载，不拼接；ETag变化丢弃旧分片；断网保留可验证暂存 |
| Verifying→Staged | 包身份/解压目录/入口资源/兼容再次通过；更新slot和日志原子持久化 | 校验失败清本次临时包，当前包/最后良好包不删 |
| Staged→WaitingForIdle | 用户activate受理；固定releaseNId和updateEpoch；冻结新busy准入意图 | 显示所有有效busy原因；稍后取消意图退Staged，继续业务 |
| WaitingForIdle→Activating | §3.2原子确认全体所有者、最终版本/签名/有效期/空间仍有效；记录activationNId及尝试次数 | 检查与切换之间出现新作业则不激活；页面关闭不等于安全点 |
| Activating→Healthy | Bundle由本次启动健康回报确认；installer/apk只有重启实际版本匹配及启动检查通过才Healthy | OS安装已弹出/已受理仍为Activating；用户取消安装退Staged，可继续当前客户端 |
| Activating→RollingBack | 仅Bundle自动切回仍满足安全下限且有可信hash的lastGood slot | 启动失败/health超时均只尝试一次；无安全lastGood则RecoveryRequired |
| 崩溃后重启 | 从持久状态与实际已安装Native/活动Web slot核对；重建updateEpoch、丢旧页面回报 | Verifying未完重新验；Activating不得清attempt后重试同包；状态文件损坏用同环境合法备份或维修 |

### 3.2 busy所有者与激活原子区

busy是本地安全协调，不新增业务表或远端调度服务。每个正在使用的所有者注册`ownerId`、`subjectEpoch`及`collectBusy():BusyReason[]`；操作开始前获取内存lease `{leaseNId,ownerId,subjectEpoch,kind}`，完成/取消在finally释放。显式扫描会话、未提交表单、上传/下载/导出、系统打印提交、语音与屏幕（含授权等待）、BLE连接中的在途命令都纳入。单纯设备Connected且无在途命令可在安全区断连，不永久阻止升级。

更新协调器先设置activationPending，所有者从此拒绝新业务lease并返回UPDATE_BUSY；随后在同一串行调度区复查所有已注册所有者、现存lease和包版本。存量未保存表单让用户保存/放弃/稍后，不能替用户丢弃；其他业务有明确取消路径才可取消。全部空闲才持久化激活意图并切换。取消/激活失败需恢复准入，不能永久锁死业务。

准入关闭只禁止新业务，已有lease的保存/完成/取消/断连/停止及必要身份刷新继续允许，并归属原lease；否则会出现“更新等待保存、保存又被更新禁止”的前置循环。UI忙时禁用立即激活按钮；状态WaitingForIdle用于调用时发现新busy或用户已经提交激活意图的竞态，不要求用户强行点禁用按钮进入等待。

owner意外消失、renderer崩溃或查询超时视为Unknown/忙，不靠lease自然过期推断物理作业完成；先停止可确认的媒体/扫描，核对未决设备/文件操作。进程重启后未决物理写入仍未知，由该设备/后续Agent事实或操作员确认处理。PF06媒体busy由唯一媒体Store提供；不按是否显示浮窗判断。强制安全更新沿同一规则，不能设定到时强退绕过在途作业。

### 3.3 版本、防回放、包解压与回退

- 版本校验同时包括appId、环境/通道、platform/架构、packageKind、Native兼容区间、bridge区间和apiContractId。API契约标识为本轮已核验的前后端公共契约组合，不是Git提交；首版通过发布兼容矩阵约束服务端版本，未知组合拒绝激活，不为此先造通用版本服务。
- 清单releaseSequence在同发布空间严格递增；本地持久最高已接受值，同sequence仅允许同release/hash。有效且未过期的旧签名也不能回放降低安全下限。同一releaseNId不得替换产物。已Staged到激活之间再次核验到期时间和本地最高下限。
- 安全下限单调提高；只有可信签名策略能提高，普通配置/换通道不能降低。提高下限后无可回退旧包时，预先展示维修风险并保证有受信恢复路径；激活失败只能RecoveryRequired，不能为“自动回退成功”偷偷运行已禁止包。
- Bundle解压拒绝绝对路径、`..`穿越、符号/硬链接、重名归一后覆盖、大小写冲突及非预期原生可执行文件；允许文件列表由构建产物生成。001冻结包体/解压总字节/文件数上限及余量；下载前与解压前都检查实际空间，不能只信压缩大小。
- 只维护内置恢复资源、当前slot、lastGood和一个暂存slot；健康确认前不删lastGood。不清业务文件腾空间；清理仅限已归属本更新器、已验证绝对路径位于私有更新根内的废弃暂存。
- WebBundle禁止修改签名根、生产API白名单、原生权限/插件/安装身份和安全存储格式；遇到这些变更走完整包。业务本地配置的升级应保持前后两个受支持版本可读；无法兼容先走完整包迁移，不在网页启动时破坏旧配置后再回退网页。

### 3.4 更新库映射必须在001验证

electron-updater元数据不是本JSON的同义词；G06A-1F/G须给“本清单字段→实际feed字段/签名检查→下载文件”的逐项映射，证明从检查到安装始终同一个artifact。禁止一边通过自定义check，另一边由库自动拉未受约束的latest安装。按锁定版本设置手动激活/禁用退出自动安装；当前官方文档与旧版的自动安装选项存在差异，不能照旧教程硬编码属性。安装前的busy检查须发生在quitAndInstall之前，不能只依赖窗口已经关闭之后的退出回调。

候选Capgo的`notifyAppReady`与健康超时配置、自动更新/后台切换、默认内置包回退都要逐项映射到本规格。当前官方文档默认健康确认10秒，本项目要求60秒，使用时必须显式一致配置，并只由一个协调器决定健康与切换；每次相应启动在规定时点回报，不能刚进入JS立即报成功。插件内置回退若可能低于securityFloor，须选可受控模式或判此路线不满足要求。未核验插件不写成已采用，详见§11来源。

## 4. 页面线框图与路由

### W06A-01 终端与版本（PC/PDA共享信息）

```text
┌ 终端与版本                                         刷新 ┐
│ 当前客户端版本 / 页面版本 / 运行状态                    │
│ 更新：可下载→校验→等待作业结束→可重启                   │
│ [检查更新] [下载] [稍后] [重启更新：忙时禁用+原因]        │
├ 设备名称 / 能力 / 权限 / 状态                  配置 更多 ┤
│ 扫码器   支持/未授权         [测试扫码]                 │
│ 蓝牙设备  已连接/断开        [连接/断开]                │
│ 系统打印  支持/未配置       [打印测试页]                │
└ 诊断导出入口（脱敏）                                    ┘
配置抽屉：支持模式 / 结束符 / 受控设备 → 取消 / 保存
```

路由`/pc/terminal`、`/pda/terminal`，菜单“终端 / 终端与版本”；runtime.read控制查看，runtime.configure/runtime.update控制配置/更新，系统授权另行校验。Web模式仍显示当前Web版本与Unsupported能力，不展示假安装包更新。设备列表selection=none,compact，状态主页面不机械提供导出/分组。参考1440×900/360×800，窄屏上下排列，PDA48px触控。

### W06A-02 扫码测试与更新异常

```text
┌ 测试扫码                               结束扫描 ┐
│ 模式 / 当前归属 / 已取得系统权限                 │
│ 等待扫描→收到结果（仅当前临时显示）              │
│ 权限拒绝：说明+设置入口；断连：重连              │
└ 返回即释放会话，不将数据写日志                 ┘
更新失败：原因 / 当前仍可用版本 / 重试或维修；不只转圈
```

页面先查capabilities后展示动作。保存双击只一个请求；权限拒绝不能循环弹授权；下载进度与业务作业loading不共用全页遮罩。主题/locale、keyboard/safearea、切后台/锁屏/换人、系统通知点击重新鉴权逐项验收。错误文案不暴露IPC/JAR等内部术语。

### 4.3 PageId、字段、权限与入口闭合

| PageId / 命名路由 | 路径/入口 | 数据和动作 |
| --- | --- | --- |
| `pc.runtime.terminal` / `pc-runtime-terminal` | `/pc/terminal`，终端→终端与版本 | W06A-01；1440×900、1280×720及200%缩放；复用AppPage/AppDataTable compact，selection=none |
| `pda.runtime.terminal` / `pda-runtime-terminal` | `/pda/terminal`，PDA功能入口→终端与版本 | 同W06A-01，360×800；纵向分区、48px触控；不套PC多列工具栏 |
| `runtime.scanner.settings` / 无独立路由 | W06A-01“扫码配置”抽屉 | AppFormDrawer居中/侧滑偏好；scannerMode下拉只含已核验能力、suffix枚举、diagnosticConsent开关；expectedRevision隐含随快照提交 |
| `runtime.scanner.test` / 无独立路由 | W06A-01“测试扫码”→W06A-02 | acquire成功才显示等待；Keyboard专用输入/确认、Camera扫码一次、Broadcast接收；结果仅内存显示，关闭清空 |
| `runtime.update.recovery` / 内置恢复入口 | W06A-01失败区或Vue无法加载时原生恢复界面 | 当前/目标版本、错误说明、重试/重新安装/联系支持；不暴露任意shell/下载URL输入 |
| `runtime.screen.source` / 无业务路由 | 仅Electron用户触发分享、系统选源不可用时W06A-03 | 见§4.4；不注册为SystemData菜单，不独立运行聊天/媒体Host |

Mobile Web继续已有三端业务入口；首版不新增`/mobile/terminal`及Mobile原生包。PC/PDA浏览器打开终端页可看Web版本、实际浏览器能力和不支持原因，不展示原生更新成功假数据。

`runtime.read`为Page权限，`runtime.configure`、`runtime.update`为Action权限，精确登记进Identity PermissionCatalog/BootstrapSeedCatalog、前端生成目录和权限常量。业务扫码/BLE仍按所属业务动作权限，不要求业务操作者拥有终端配置权限；终端页的测试扫码、测试连接、打印测试页和诊断导出要求runtime.configure。能看runtime.read不等于可以使用设备或升级。

SystemData导航资源稳定NId分别为`systemdata.navigation.pc-runtime-terminal`与`systemdata.navigation.pda-runtime-terminal`，绑定相应route name/runtime.read/终端类别；菜单分组只创建一次。用新增幂等种子版本upsert本阶段受管条目，不重放/覆盖已有自定义菜单，不改历史checksum。实际增量ID、顺序、父菜单及已有种子调用点在001核对后写入§6H；不得以当前文档的资源NId冒充已应用种子版本。

| 页面区域/字段 | 唯一数据来源 | 加载、错误与动作规则 |
| --- | --- | --- |
| 容器/OS/Native/Web/bridge版本 | capabilities.get | 页面打开局部骨架；未知显示“尚未取得”，不以package.json前端版本代替宿主实际版本 |
| 当前环境/通道/扫码设置 | config.get | 环境/通道只读，服务器/证书由部署配置；配置冲突保留草稿，提示重读比较 |
| 下载/校验/目标/等待原因 | updater.status + onChange | 进度局部更新，total未知用不确定进度；重复按钮同步guard；状态退回只允许当前updateEpoch/stateRevision |
| 设备行状态 | devices.list + onChange | row-key稳定，不预选；连接错误只影响本行；刷新列表不能中断进行中的业务操作 |
| 检查/下载/稍后/重启更新 | updater方法 | read可看、update才可操作；无支持隐藏原生动作；忙时禁用激活并显示真实busy原因 |
| 扫码配置/测试/打印/导出 | §2.2方法 | configure权限；OS授权另外必需；返回后检查新状态而非先改UI为成功 |
| 权限拒绝/永久拒绝 | 能力reasonCode | 提示恢复步骤与受控设置入口，刷新不弹授权；无权限不发原生副作用调用 |

首次启动“有包无可用配置/服务不可达”显示本地版本、环境标识及“联系管理员配置/重试连接”，不默默选择localhost、不允许未登录网页改apiOrigin。UI文案只显示操作员需要的环境名与处理建议，技术错误详情进入脱敏诊断。首次配置由受控安装配置提供，首版不增加任意服务器编辑表单。

### 4.4 W06A-03选源与W06A-04恢复

```text
W06A-03：选择要共享的内容（仅系统选择器不可用时）
┌ [屏幕] [窗口]                           取消 ┐
│ 来源缩略图 / 可辨认标题；默认不选择           │
│ 本次共享仅发送选中画面，语音沿原聊天控制       │
└ 来源失效：请重新选择            [开始共享] ┘
W06A-04：客户端需要恢复（内置资源，不依赖故障Bundle）
┌ 当前客户端版本 / 页面版本 / 当前仍可用状态    ┐
│ 更新失败原因 / 已回退版本或没有安全可回版本    │
│ [重试] / [重新安装说明] / [导出诊断]          │
└ 不要求先加载坏页面；不开放任意链接和命令      ┘
```

选源只有用户确认才返回当前请求有效source句柄；关闭弹层/源窗口消失/超时/邀请结束即取消并清缩略图。来源列表不写日志，不能默认选第一屏。远端观看继续PF06页内浮窗：缩放后拖动保持DOM实际尺寸，最小化有重新展开入口；多显示器/DPI变化后窗口边界可达。恢复页是宿主安全操作入口，离线也可看版本和说明；不因此绕过签名/安全下限。普通业务诊断要求权限；无法登录的恢复界面仅允许本机显式同意导出宿主启动/更新脱敏记录，不读取旧用户聊天、Token或业务文件。

## 5. 任务与验收映射

| TASK | 固定输入/实施步骤 | 关键可观察反例 |
| --- | --- | --- |
| PF06A-001 | §6A～H输入→§7当前接入核对→§9工具链/发布/插件核验→回写精确adapter/安装身份/签名向量；只核验，不新建完整媒体PoC | 逐项记录决定设计的输入和后续真机证据；未实际核验不写Supported |
| PF06A-002 | §1/2 DTO、Web适配→§7配置装配/会话存储端口→§2.1/2.3身份隔离→§3.2忙闲协调→§10.1页面权限/导航基础 | A06A-01/03/04；不能只建接口而保留原生sessionStorage明文持久化 |
| PF06A-003 | §7.1/7.2/7.3接入→§8.1单窗口/IPC/选源→§9Windows包→§4/10.1 PC页面已接能力→实际安装最小登录/文件/打印测试链 | A06A-01/07/08/15；打包后路径/CORS/权限，不仅pnpm dev |
| PF06A-004 | §2.4广播/键盘/相机→§2.5 BLE实际DTO→§8.2 Android生命周期→§9 APK→§4/10.1 PDA真实终端/扫码入口 | A06A-05/06/15；真实广播来源/软键盘/撤权；不把模拟器作为设备支持 |
| PF06A-005 | §1.2清单/状态→§3原子更新→§9分发/签名→三条更新实际升级和失败恢复 | A06A-10～14；插件默认策略不能越过项目门禁 |
| PF06A-006 | §8生命周期/通知→现有PF05/06全链→目标容器异常/多端组合 | A06A-03/08/09/16；真实业务效果与信令分别记录，网络质量后置按原安排 |
| PF06A-007 | §4全部页面整合（基础入口随002～004交付，见§10.1）→§2.2受控配置/测试/诊断→§7权限导航完整回归→PF10B边界交接 | A06A-02/07/14；同一设备不建第二执行队列 |
| PF06A-008 | §10逐项场景/支持矩阵→独立稳定交验→主文R1～R6交接 | 必需范围FAIL/BLOCKED未关闭不写整包通过；已确认旧路径无变化不机械重跑 |

## 6. 派遣前设备与发布门禁

G06A-1由总控提供首台PDA型号/Android版本、厂商广播或SDK样本、BLE目标、Windows版本/架构、分发环境与签名责任方；技术核验输出固定adapter配置字段、依赖精确版本/许可、签名/公钥轮换与回退方式。未取得这些输入，生产001以后的具体实现不具备就绪条件；不可把任意设备支持留给开发自由扩张。文档中的共用字段已落定，设备字段只在真实adapter核验后补齐。

G06A-1A～H是原门禁的核验清单，不是新增八道派遣/审批卡。只影响对应范围；001可先核对现有代码、依赖资料和可得输入。需要改变首版承诺的结论由主控回写权威规格后再派相关生产范围；已经决定契约但仅缺真机证据的条目记待验收，不反复退为待设计。

| 子项 | 本轮已知/仍缺输入 | 核验责任及关闭输出 | 影响 |
| --- | --- | --- | --- |
| G06A-1A PDA/广播 | 已定Android PDA；缺型号、OS/API level、固件、WebView版本/更新能力、厂商文档和有效广播样本 | 主控归集目标，001逐字段填写§2.4，给真实接收/取消/来源拒绝证据及不支持边界 | 004广播实现/设备支持声明 |
| G06A-1B BLE | 已定一种BLE；缺设备型号、GATT/业务协议、读写样本和结果查询方式 | 主控提供目标，001冻结§2.5全部adapter DTO、权限、超时/Unknown处理 | 004/007设备实现；不扩成SPP |
| G06A-1C Windows | 已定Windows Electron、清单首版win-x64；缺实际OS/架构、安装权限、证书信任/代理、打印机及终端限制 | 001登记最低/实测OS、签名身份、per-user或per-machine安装范围、系统选源能力和打印测试目标 | 003/005实际包/交付 |
| G06A-1D 原生媒体/通知/Mobile | Mobile仅Web基线；缺目标WebView语音/接收/采集支持、后台要求及通知渠道 | 001按§8逐能力核验。Android发屏如需独立原生WebRTC/MediaProjection链，先主控收敛；不默认为薄桥接已覆盖。推送未定不建服务 | 006与原生支持声明，不阻挡已确认Web业务 |
| G06A-1E 工具链 | 现有frontend要求Node>=24.18.0 <25、pnpm11.16.0；原生工程未检出 | 001登记Electron/Chromium/内置Node、electron-vite/builder/updater、Capacitor core/cli/android与插件、JDK/Gradle/AGP/SDK精确版本和许可；编译装配可复现 | 002～005依赖锁定 |
| G06A-1F 签名/更新 | 首版三种更新路线已定；缺算法/规范化输入、公钥分发、安装签名责任和实际渠道 | 001给§1.2签名成功/篡改/未知keyId固定向量、签名与公钥轮换/吊销/离线恢复过程、插件feed映射；生产密钥只记录托管引用 | 005生产更新/发布 |
| G06A-1G 分发/兼容 | 缺appId/packageName/安装标识、环境和通道允许表、apiContractId及兼容矩阵、包限额/磁盘余量 | 001填写§9产物矩阵、三种升级的实际来源/操作身份、离线安装步骤和恢复路径；签名/渠道未具备只做明确的测试核验 | 003～005/008交付范围 |
| G06A-1H 平台接入 | §7已核对装配入口；runtime权限、导航增量与原生可信身份桥接尚无实现 | 001核对当前Identity鉴权/me与刷新契约、SSO需求、Origin/CORS、导航父节点/顺序/新seed ID及schema、共享文件写入范围，记录精确接入结果 | 002/003/004/007公共改动 |

每条输出统一记录：`itemId / designState（待输入、待核验、已冻结）/ validationState（未测、PASS、FAIL、BLOCKED、用户后置）/ 目标平台及版本 / 决定或限制 / 文档落点 / 证据路径 / 缺项责任方`。当前A～H均未整体关闭；本次代码阅读不能替代设备/安装/升级实验。不填虚构设备、签名责任人、依赖版本或验收结果。

### 6.1 001受控核验的可执行边界

001沿主文已采纳路线核验：Electron/electron-vite/builder/updater，Capacitor Android薄插件，PDA更新先评估Capgo；不重开UI框架选型、不建设另一套媒体业务。核验装置为指定Windows、真实PDA/广播样本/BLE设备、隔离测试安装包与分发目录、既有已批准测试身份及服务入口；不存在的装置/身份先记录缺项，不能自行替换为“默认管理员+mock服务”。

核验顺序为代码与版本/许可→最小打包并本地启动→配置/登录/原生能力实际接入→签名/兼容失败向量与一次受控更新恢复。每一步产出字段映射/实际命令/版本/观察证据，后一步缺设备时继续可独立完成的文档/编译检查。成功判据：真实调用契约与§1/2一致、设备/权限结果可观察、60秒健康与回退/安全下限无冲突、包不依赖开发服务器；不使用“能编译”代替全部判据。

停止当前候选/能力核验的条件：目标输入仍缺失、依赖或渠道许可不满足、候选无法执行受信更新/受控激活、Android采集无法接入现有轨道、或需扩为另一套业务/媒体平台。集中给主控已排除原因和最小可行范围，由主控回写再继续相关生产；不在原契约外无限扩建demo。没有新证据的同一问题两轮不重复探测环境。001的实际报告只证明已测目标，不等于002～008已实施。

### 6.2 001核验工作的逐步执行输入

2026-09-14用户再次明确：启动开发前任务必须细化。下表细化001的内部顺序，不新增派遣卡。主控先完成只读调查和确定输入；涉及编译/设备的核验仅在目标与隔离工作线明确后进行。每步分别登记“设计是否确定”和“实验是否通过”，不以步骤表齐全宣布G06A-1关闭。

| 顺序 | 精确输入 / 要做的动作 | 必交产物与可观察判据 | 缺项时处理 / 影响 |
| --- | --- | --- | --- |
| 1 当前工程与配置 | 阅读§7所列装配/认证/路由文件、`src/frontend/package.json`及现有锁文件；登记http/embedded/mock三条装配链，列所有HTTP/Hub配置读取点 | 一张“配置来源→parser→应用装配→API/Hub”的实际调用表，列Bearer与Cookie会话的恢复/刷新/失效入口；旧Web行为逐项保留 | 共享文件有新WIP则只复核相关契约，不修改PF06；影响002/003/004 |
| 2 目标与工具链 | 按§6A～G记录Windows/PDA/设备/安装条件；核对项目Node/pnpm要求与本机实际版本；再按目标核验Electron、Capacitor、JDK/Gradle/AGP/SDK及插件的版本/许可/原生依赖 | 每个依赖给精确版本、用途、许可来源、运行/构建最低条件与锁定位置；说明packaged renderer/local scheme和原生权限如何兼容该组合 | 不写latest或仅主版本；PATH查不到只记未发现，不据此断言未安装；设备未知仅阻挡相应adapter |
| 3 安全bootstrap与主体桥接 | 消费§1/2/7，明确宿主如何获取受信主体/权限，谁安装/清除会话，何时推进epoch；确定原生允许的authMode、本地Origin和深路由策略 | 固定内部方法/调用者/字段/错误/超时，画清冷启动、同主体刷新、换人、撤权顺序；成功及迟到旧回包样例各一组。原生缺部署配置不得落到localhost | 不能把普通页面传入tenant/user/permissions视为已验证；未确定桥接前不让002自由实现另一套认证 |
| 4 最小打包验证 | 在明确的隔离客户端工作线核验原定Electron/Capacitor路线；消费现有Vue产物，按§9登记native/web/bridge身份；实际命令从所选版本生成的脚本核对 | Windows/PDA分别记录工作目录、命令、退出码、产物路径和版本；无Vite时冷启/深路由/动态资源可用，走§10.1最小身份和能力链。测试身份沿现有平台 | 原生工程尚未创建时不得编造可运行的package脚本；未登录或设备缺失记具体未到达步骤，打包成功不等于业务链通过 |
| 5 扫码/BLE与媒体边界 | 广播逐字段填§2.4；BLE逐字段填§2.5；按§8分别核验采集、接收、音频播放、后台/锁屏、通知，沿PF06既有传输端口 | adapterKey/settings DTO、真实输入/返回样本、权限/取消/去重/超时/Unknown映射齐全；采集与接收分别写Supported/Unsupported/Unknown及证据 | 缺厂家样本不能填写通用伪广播；Android需另一套媒体传输时由主控收敛范围，不让004/006边做边扩张 |
| 6 更新与信任 | 三种产物逐项匹配§1.2/3/9；核验库feed、签名规范化、公钥/轮换、安全下限、busy准入、60秒健康和恢复语义 | 精确清单/库元数据映射；固定签名正例及篡改/未知keyId/版本回放反例；隔离包一次升级和恢复记录。清单签名、Windows签名、APK签名分别说明 | 未具备正式证书可使用明确标识的隔离测试信任根验证机制；不据此支持生产发布。候选无法实现要求则记录不适配原因并停止该候选 |
| 7 回写生产输入 | 汇总以上结果，按G06A-1A～H分别审查影响002～008的设计缺项；核对§4页面、种子增量与共享文件唯一写入顺序 | 更新同一权威规格、支持矩阵、命令表和未验项；已确定的任务引用精确章节，执行者不再决定字段/权限/布局/协议。记录允许进入编码的具体范围 | 不强求全部设备实测后才开放无关实现；也不将Unknown改成已确定。技术核验与生产交付证据分别记录 |

001每步结果至少包含：`step / target / observedSource / designDecision / exactContractLocation / commandAndWorkingDirectory（未执行写无）/ expected / observed / designState / validationState / evidence / missingInputOwner / affectedTasks`。版本、设备和命令只填实际观察；同一步有多个平台分行记录。证据归主文已指定的`docs/evidence/PF-06A.md`，真实核验发生后再创建或追加；本节不是PASS报告。

现有Web定向回归入口已核对：在`src/frontend`使用`pnpm test:unit tests/unit/runtimeConfig.spec.ts tests/unit/authStore.spec.ts tests/unit/embeddedAuthGateway.spec.ts tests/unit/routerGuards.spec.ts`，只在涉及这些代码变更或真实接入验证时执行；它证明对应Web行为，不证明原生OS安全存储、Cookie或设备支持。新原生命令和测试入口随实际工具链固定后写§9/10.3，不预写不存在的脚本。

### 6.3 002～008生产输入的交付粒度

下表是在§5九字段主卡与§1～4详细契约之外，明确“细化到什么程度”的核对表；正式编码前由主控检查本次范围对应行，不把空白转交开发。

| TASK | 必须已有的具体输入 | 开发的首个可核验结果 / 主要负例 |
| --- | --- | --- |
| 002 | 已确定的bootstrap/身份内部接口、安全存储adapter、Web/Native配置来源；§1/2完整DTO、超时、权限、epoch、§3 busy所有者；§4页面/导航增量写入位置 | 同一Web入口保持可用，原生接口经过接收端校验；并发刷新/换人不能恢复旧主体，embedded不生成Bearer |
| 003 | Electron/构建器精确版本、appId与安装范围、Origin/history映射、安全preload方法表、系统文件/打印/选源及单实例行为；按§10.1分配PC页面 | 安装后从真实入口登录、打开终端页并调用文件/固定打印测试；非法frame/路径/导航不能访问宿主能力 |
| 004 | Capacitor/Android工具链与权限清单、PDA型号和广播完整字段、相机插件、BLE服务/特征/读写结果与取消规则；按§10.1分配PDA页面 | 从真实PDA入口取得扫描会话并交业务消费；双通道/旧广播/离页后结果不会误投，BLE结果未知不自动重写 |
| 005 | 三种更新的包身份/版本映射、feed与manifest逐字段对应、签名固定向量/公钥托管引用、空间阈值、busy/健康/安全下限及恢复步骤 | 实际一次升级且重开版本相符；篡改、低版本、磁盘满和断电不使未验包启动或绕过忙闲锁 |
| 006 | 各平台采集/接收/后台/通知支持矩阵、PF06现有Host接入位置、OS授权与生命周期事件映射、恢复顺序及busy lease所有者 | 两端真实语音/画面与停止效果可观察；锁屏/换人后不自动恢复采集，迟到回包不复活旧上下文 |
| 007 | §4各PageId/路由/权限/字段/线框、实际导航父项/增量ID、已接设备能力、诊断脱敏字段/容量、PF10B受控交接端口 | 通过实际菜单进入完整页面；无权直接调用被拒，诊断导出无Token，同一设备不出现第二执行队列 |
| 008 | 已交付支持矩阵、原生/页面/Bridge版本和包来源，§10逐案例目标/身份/设备/预期/清理范围，开发稳定交接证据 | 按实际支持范围独立复核；所有未测/阻塞/用户后置明确保留，001实验成功不抵算生产包验收 |

## 7. 当前工程接入与认证配置

### 7.1 2026-09-14代码核对与允许修改点

下列是本次实际读到的接入点，不是已完成PF06A适配。工作树含PF05/06既有未提交修改，生产开工以实际稳定交接重新核对必要差异，不要求回到本次Git状态。

| 现有文件（相对仓库） | 当前行为 / PF06A最小修改边界 |
| --- | --- |
| `src/frontend/src/main.ts`、`app/createIndustrialApp.ts` | 同一工厂装配Pinia/Router/认证/API/协作；原生先完成异步bootstrap再调用工厂，不另复制一份业务main |
| `src/frontend/src/config/runtimeConfig.ts` | 已有mock/http/embedded与pageOrigin，Web显式根相对API路径经同一parser解析；开发默认localhost:5041。增加宿主只读配置来源并生成唯一快照；原生生产缺配置即失败，不回默认 |
| `src/frontend/src/auth/sessionStore.ts`、`stores/authStore.ts`、`auth/types.ts`、`auth/httpAuthGateway.ts`、`auth/embeddedAuthGateway.ts` | bearer/mock持久化sessionStorage；embedded-cookie跳过Token持久化并通过bootstrapSession/refreshEmbeddedSession恢复。仅对需要持久化的会话抽出load/save/clear异步端口；Native bearer接安全存储，不建第二AuthStore |
| `src/frontend/src/api/httpClient.ts`、`api/collaborationHub.ts`、`api/systemData/notificationHub.ts` | 保留认证分支的装配范围：http走Bearer及原刷新，embedded走withCredentials、无Token的Collaboration API/Hub，不强行启动SystemData Hub或管理API。核对各分支已启用端点均使用有效配置，Bearer Hub重连读最新Token |
| `src/frontend/src/router/index.ts`、`routes.ts`、`routeNames.ts`、`guards.ts`、`meta.ts` | 当前createWebHistory；本地资源服务器须支持已登记页面路由fallback，刷新/冷启动深路由可用。未知资源404不能都返回HTML掩盖JS缺失；若目标方案必须改history，001先定适配方案，Web既有URL不变 |
| `src/frontend/src/App.vue`、`stores/collaborationMediaStore.ts`、`components/collaboration/CollaborationMediaHost.vue`、`ScreenShareFloatingWindow.vue`、`CollaborationChat.vue` | 已有App级媒体Host/页内观看浮窗；只接Runtime媒体采集/生命周期和busy端口，不复制Hub/Store或把媒体搬到子窗口 |
| `src/frontend/src/permissions/catalog.ts`、`catalog.generated.ts`、`locales/zh-CN.ts`、`locales/en-US.ts`、`localization/types.ts`、`systemData/runtime/navigation.ts` | 仅增加终端入口/本地化及原生能力适配；生成文件通过现有生成机制更新，不单改前端字符串假装拥有后端权限 |
| `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs`、`IndustrialPlatform.Identity.Application/Bootstrap/BootstrapSeedCatalog.cs` | 仅新增§4.3三项权限及对应真实种子/测试；注册权限不代表普通用户已授权 |
| `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SystemDataBaselineSeedRunner.cs` | 导航受管条目按现有版本机制增量；允许补相关现有种子/策略测试，不新建Runtime Service Host |
| 新增目录 | `src/frontend/src/platform/runtime/`；共享终端页可置`src/frontend/src/components/runtime/`，路由薄入口在`pages/pc/`和`pages/pda/`；`src/clients/electron/`、`src/clients/pda/`、`tests/Clients/`和对应前端测试 |

表内简写路径沿同单元前缀解析。正式派遣登记新增测试/页面/配置文件和唯一写入者；003/004只能并行改各自原生目录，共享Runtime/包锁/路由/认证/协作/权限种子顺序写。没有业务API需求的Runtime方法保持本地调用，不为了符合菜单标准新增空API Controller。

### 7.2 配置、Origin、资源与HTTP/Hub

启动顺序固定：读取内置发布身份/信任配置→校验容器本地配置和更新恢复→选择已验Web slot→注册受信本地页面Origin/受限bridge→安装只读有效配置及存储adapter→装配Vue。此后本地资源/bridge健康检查独立报告，不等待网络；业务另按恢复身份→加载权限/菜单/权威数据继续。网络身份恢复失败不等于本地包不健康，健康步骤按§3要求在本地条件具备时完成。

- **页面来源**：Electron优先受控secure/standard自定义协议与私有资源根；Capacitor使用冻结的本地scheme/hostname和打包webDir。实际Origin在001注册并测试；不能把本地页面`null` Origin全局加入CORS或仅依靠apiOrigin允许桥接。远程业务API不是可加载任意JS的页面来源。
- **API来源**：runtime-config的apiOrigin+apiBasePath形成现有apiBaseUrl，域名/路径按URL解析归一；配置来自可信部署输入，不来自query/deep-link/localStorage/网页任意消息。原生环境切换需要清会话/连接并重新bootstrap，不能带旧Token向新环境试探。只在生产包允许的environmentNId/通道集合内使用。
- **统一读取**：任何loadRuntimeConfig调用均返回同一已冻结配置，包括Collaboration Hub；不得登录client已换地址而Hub沿import.meta.env旧地址。不直接修改现有API前缀、authorization envelope或requestNId语义。
- **认证模式与同源路径**：保留Web `http/embedded/mock`的现有装配边界和显式根相对API路径；生产仍禁止mock。原生本地scheme不能被当成远端HTTP API基址，原生部署须给明确的受信HTTP(S)入口。原生首版允许的authMode在G06A-1H冻结；embedded是现有Web能力，不因装入WebView自动获得原生支持。若纳入原生，须核验Cookie分区/SameSite/跨源、会话bootstrap/刷新/登出及宿主可信身份绑定；不得制造accessToken/refreshToken、读取HttpOnly Cookie给JS，或将会话失效导航改成平台登录。未纳入的原生模式在配置校验时明确拒绝，不能静默改成http。
- **跨源/证书**：按实际页面Origin和`withCredentials`核对CORS及Cookie策略，允许精确Origin且不返回`*`配凭据；HTTPS/WSS、WebSocket升级、代理与大文件需在交付拓扑验证。内网CA由受控环境信任，不能关闭webSecurity/TLS验证解决连接问题。HTTP调试例外只能在独立开发配置和已批准测试Origin使用，不打进正式包。
- **资源与深路由**：生产从打包资源加载，禁止依赖Vite在线；配置Vite base/webDir/协议资源映射。直接冷启聊天深路由、刷新、动态import、字体图标、本地错误页均检查。路径穿越、绝对路径、非登记路由不得读私有文件。普通外链只在无bridge的系统浏览器打开，内部页面禁止任意window.open/webview导航。
- **SSO边界**：现有withCredentials不证明系统浏览器与WebView共享Cookie。首版必须验证现有账号登录/刷新/登出；若分发要求企业SSO，G06A-1H补实际回调Origin、受控链接、state/PKCE及会话交接证据，复用现有Identity能力，不临时发明把Token放URL的登录协议。未指定原生SSO不得宣称已支持。

### 7.3 安全存储与权限不能停在界面

Web bearer/mock保留现有sessionStorage策略；embedded-cookie继续以服务端HttpOnly会话为权威，恢复/刷新沿原AuthGateway，不持久化伪造Token或镜像Cookie。原生Bearer Token只允许必要的内存使用和OS受支持的加密存储，不再同时写sessionStorage/localStorage/Capacitor普通Preferences；密码不持久化。存储key绑定appId/环境/tenant/user/服务端会话，日志只记脱敏错误。Windows用户安全存储不等于每个共用业务账号隔离，应用必须仍执行换人清理；Android密钥/加密凭据排除Auto Backup/迁移恢复。安全存储不可用的降级策略在001明确，只允许显式的内存会话，不静默明文落盘。

持久化会话端口只向认证装配模块开放，方法为`loadCurrent():Promise<AuthSession|null>`、`saveCurrent(session):Promise<void>`、`clearCurrent():Promise<void>`；保留现有AuthSession的transport及可选Token语义，embedded-cookie不经此端口保存/恢复身份。原生adapter再经受限内部通道保存，不向普通页面提供通用`secureStorage.get(key)`或任意读取接口。恢复先核验会话和当前用户权限，不能只因解密成功进入业务。一次失败不能留下“UI已登录、宿主仍是旧epoch”的分裂状态。

配置/更新/设备调用既有前端PermissionGate也有宿主接收端检查：从现有Identity可信会话取得权限，不信任页面提交的permissions数组；方法执行前检查当前身份、所需权限和OS授权。如何复用既有me/令牌验证与权限失效通知由001在G06A-1H核对并固定；不能用新账号服务代替，也不能在未核验前把UI隐藏当安全验收。服务端业务请求始终继续独立鉴权。危险操作的权限新鲜度不确定时拒绝新操作，清理本人资源不受此阻塞。

## 8. 原生容器与协作生命周期

### 8.1 Electron Windows

首版一个业务BrowserWindow/renderer，一个业务媒体Host；第二次启动只激活已有实例并处理经校验导航，不打开第二套聊天/扫码资源。托盘菜单仅显示“打开平台”“退出”，无业务敏感正文；不默认开机自启。安装身份、session分区及单实例锁作用域由G06A-1C固定。

| 用户/OS动作 | 页面及资源结果 |
| --- | --- |
| 最小化 | 保留App和当前媒体，任务栏/托盘可恢复；不能因观看窗隐藏重复播放或切断 |
| 点击窗口X | 托盘可用时隐藏到托盘并保留明确驻留提示，当前语音/共享保持；首次告知“关闭窗口后仍运行”。无托盘时有在途业务则显示受控退出流程，不隐藏成无法重新打开的后台进程 |
| 托盘“退出” | 先检查未保存表单/文件/设备操作，用户保存/放弃/稍后；确认退出后立即按能力停止媒体、扫描、BLE，尽力发End后退出；远端/审计无响应不阻塞本地停止 |
| OS锁屏/休眠 | 主动结束当前媒体并释放扫描/设备会话，持久消息由服务端保存；唤醒恢复身份/权威状态，不自动重启麦克风或共享。锁屏是安全动作，不等待更新busy流程 |
| renderer崩溃/进程被杀 | 能主动清理则清理；不能执行回调不伪造Stopped/Healthy。对端沿PF06租约到期处理；下次启动查询权威状态，旧会话不自动重接采集 |
| 安装升级退出 | 仅§3安全点持有激活权时允许更新器接管退出；普通关闭不能触发库默认自动安装绕过busy |

安全配置保持contextIsolation=true、sandbox=true、nodeIntegration=false；默认拒绝非白名单permission、frame、导航和新窗口。preload暴露按方法封装端口，不能暴露ipcRenderer、send任意channel、shell、任意fs、process/env或原生插件全集；诊断导出也不能间接提供任意目录读取。

屏幕采集复用PF06“用户选源→邀请/接受→绑定→传输”顺序。在已核验OS/Electron版本支持时使用系统选择器，否则使用§4.4受控来源选择界面；不复制官方示例的“取sources[0]直接授权”。源句柄只绑定当前request/epoch，源窗口关闭触发该屏幕能力停止。麦克风为独立语音能力，首版不将系统音频loopback默认并入屏幕共享，不改变PF06三槽/单PeerConnection协议。

### 8.2 Capacitor Android PDA

- 生命周期同时处理Activity变化、appStateChange、resume、锁屏、配置变化和进程重建；在resume重新检测权限、网络、版本和身份，而不是只调用一次Hub.start。重复监听/重复receiver注册可观察且须消除。
- 前台聊天/观看/语音按G06A-1D实际能力验证；进入普通后台或锁屏时首版不承诺继续音视频/BLE，主动停止媒体与设备会话，返回前台恢复权威业务列表。用户正在确认OS权限/文件选择的受控临时Activity按§2.4保留原request上下文；不能把这种短暂离开当退出登录，也不能用于绕过锁屏停止。
- 返回键先关闭最上层受控弹窗/扫码会话，再按既有PDA路由返回；未保存表单按原离页规则。根页返回进入后台按上述清理，不直接强制退出覆盖在途物理操作结果。软键盘、安全区、横竖屏及滚动保持真实设备验收；旋转不得重建第二媒体Host或receiver。
- 权限清单按目标OS/targetSdk区分：相机、麦克风、蓝牙扫描/连接、通知及厂商广播权限只按使用请求。Android 12+蓝牙权限与旧系统定位要求、Android 13+通知授权按官方规则及目标设备核验；拒绝/永久拒绝/设置撤销分别处理，不为连接一个设备申请无用权限。
- **屏幕接收≠本机屏幕采集**：WebView中可以播放远端画面，不能据此声称getDisplayMedia可用。若Android本机发屏需要MediaProjection，还须证明捕获如何作为轨道接入PF06现有传输、OS授权/前台服务/系统停止如何映射、是否引入原生WebRTC栈；仅拿到授权Intent/Bitmap不算完成。该路线由001先核验并回写，不能让004/006临场重写媒体平台或以Unsupported静默缩减已经批准的首版范围。
- 原生插件权限、SDK/ABI变化走APK升级；设备WebView不能更新或版本低于支持下限时显示明确升级/不支持信息，不通过降低安全配置让坏页面勉强运行。

### 8.3 PF05/06恢复顺序、迟到操作与权限异常

恢复顺序：确认当前epoch→恢复/刷新Identity会话与权限→建立或恢复原有Hub→读取当前聊天/未读及媒体权威快照→按PF06版本规则合并→更新呈现。UI“已连接”与“正在共享/通话”分开，不能因为Hub connected就写媒体成功。服务端已经Ended的会话不能被本地缓存或迟到Ready复活，过期TURN配置不能重用。

PF06的session/call ID、mediaContextNId/contextRevision、negotiationNId、operation代次保留原定义；Runtime epoch只增加容器主体隔离，不替换其并发令牌。成功、失败、取消、mediaReady fallback都携带原上下文。退出/切人后迟到选源、getUserMedia、文件选择、设备连接立即清理，不把结果挂到新聊天；当前真实权限/设备错误仍可见，不全局吞掉MEDIA_CONTEXT_STALE。

新增能力失败只清对应能力，停止语音保留屏幕、停止屏幕保留语音；只有锁屏/退出/身份失效等全局生命周期动作才结束全部。多设备抢接与忙线继续由PF06服务端决定，原生不能凭本机单实例锁绕过跨设备约束。恢复后需要新邀请/用户授权的动作明确提供入口，不后台自动接听/自动发屏。

### 8.4 系统通知和消息持久化

首版`notifications.local`表示进程存活且收到已授权事件后显示系统通知；权限已授予不保证系统一定显示，accepted只表示受理。`titleKey`限“有新消息/有协作邀请”等模板，不附正文/条码/人员姓名/附件名。去重键=当前身份+事件ID；默认到达通知不自动标已读。

点击通知要做冷/热启动统一处理：验证内置payload形状及所属app/环境→等待身份恢复→匹配原通知主体→重新查会话成员权限→进入对应布局既有会话。已换用户/失效会话/被移出会话/过期邀请只提示不可用，不展示旧缓存，不自动邀请/接听。登出清本地主体通知及pending导航。若将来启用远程push，还需平台token绑定/解绑服务端契约与送达证据；本包不先造推送Service。

App被杀、系统省电限制或网络断开时，没有已确认push渠道就不宣称后台通知可达。消息已由PF05保存与系统通知显示分别验收。恢复只同步服务端事实，不为补通知而二次发送消息或重复已读提交。

## 9. 构建、安装、分发与诊断交付

### 9.1 可复现工具链和三类产物

| 产物 | 001必须登记的构建输入 | 005/008真实交付证据 |
| --- | --- | --- |
| Windows完整包 | Electron/electron-vite/builder/updater精确版本、Electron内置Chromium/Node版本、Windows/CPU、安装器格式/安装范围、appId/产品名、签名证书责任与时间戳策略 | 无开发服务器机器安装→启动登录→退出/再开→升级→卸载；旧版配置/设备档案保留策略和清理说明；安装后的实际版本与包hash |
| Android APK | Capacitor core/cli/android/plugin版本组合、JDK/Gradle wrapper/AGP、compile/min/target SDK、ABI、applicationId、versionCode/versionName、WebView最低版本、签名alias引用 | 真PDA安装、所需权限、冷启动、旧APK覆盖升级、数据保留、错误签名/低versionCode拒绝、人工重新安装路径 |
| PDA Web Bundle | 同一frontend构建产物、webVersion、资源入口/manifest、依赖Native/bridge/API契约范围、更新器插件配置、包限额 | 不改APK完成一次Bundle升级；健康确认、损坏/中断回退、旧页回报无效、内置恢复页可用 |

开发Node与Electron内置Node是两套版本，不因为现有Node24就认为Electron运行兼容。不得使用浮动latest依赖或把插件demo依赖照搬进主工程；冻结锁文件/Gradle wrapper/插件版本、许可和来源，记录实际构建命令与干净构建验证。001未完成前不杜撰`pnpm package:*`等仓库不存在的命令。

首版复用现有Vue源、API和语言资源；原生package只拥有宿主/adapter/打包所需依赖，不复制整个前端代码、业务配置或领域模型。各容器产物对同一WebVersion应能追踪到同一业务构建输入。构建产物不得包含私有Development.local.json、`.env`秘密、签名私钥、测试账号、开发server.url、生产mock开关或自动打开的远程调试端口。

APK安装经系统受控流程，未知来源安装权限和文件授予只在用户发起本次升级时处理，不开放所有文件权限或承诺静默安装。正式包保留安装身份和签名连续性，签名丢失/更换不是“改版本号再安装”即可恢复。安装/签名/分发执行须遵守实际任务授权，本轮文档不代表已经发布。

### 9.2 发布侧与公钥信任

发布路径逻辑为`appId / environmentNId / channel / platform / packageKind / releaseNId`，artifact不可变；先上传并核验全部包，再原子发布签名清单/通道指针。不能先发布清单后让客户端遇到半包或404。下载端点与每级重定向只允许内置信任的HTTPS目标，更新HTTP不携带平台业务Bearer Token去第三方域名。

发布权限与平台runtime.update不同：前者控制产物和签名，后者允许本机用户请求安装。签名私钥留受控发布环境，客户端仅内置信任公钥和keyId；普通runtime-config不含可编辑公钥。TLS证书、清单签名、Windows签名、APK签名分别登记责任/到期/恢复，不互相代替。

公钥轮换须先通过仍受信的完整包/签名策略部署新公钥，再发布新keyId清单；未知keyId拒绝。密钥泄露/吊销和长时间离线设备如何取得新信任根由001列出具体步骤，不能从当前未受信清单下载一个公钥后自行信任。内部网络不可达时保留最后合法版本；有效期异常时禁止激活，不能关闭校验“临时救急”。渠道切换不能降低安全下限或把pilot预发布伪装stable，跨渠道版本策略必须在G06A-1G冻结。

### 9.3 诊断、安全保存与维护

诊断包内容限定：Native/Web/bridge版本、OS/WebView/已核验adapter版本、环境代号、脱敏能力与权限状态、更新状态/错误码、操作耗时和相关request ID。沿§1最多10MiB×3文件、7天上限，先滚动限制再导出，不把整个userData目录压缩。默认无后台上传；每次导出显式同意。

排除Token/Cookie/Authorization/密码、TURN凭据/SDP/ICE地址、完整API查询字符串、条码/消息/标签/通知正文、蓝牙MAC/配对凭据、绝对用户目录、个人文件和屏幕缩略图。脱敏覆盖嵌套对象、异常链及第三方插件输出；无安全过滤能力的插件日志不得直接收集。原始崩溃dump可能含内存秘密，首版不自动加入。

保存必须经系统选择器，只接受当前有效artifactHandle和经清理的displayName；名称不能含路径、控制字符或设备保留名，覆盖已有文件由系统明确确认。取消不产生成功提示；导出失败不影响当前业务。支持人员收到的是可复制的错误码/版本/相关ID，而不是用户凭据。卸载/重装手册分开说明应用文件、用户配置、加密凭据、更新暂存和本次诊断的位置及保留策略；不擅自删除普通业务资料。

## 10. 可执行验收、故障注入与交接

### 10.1 最小纵向链先跑通

页面按同一§4设计随能力逐步交付：002先接公共Runtime端口及页面所需权限/导航基础，003交付PC终端页中的版本/文件/测试打印，004交付PDA终端/扫码入口。共享页面、导航与种子由唯一写入者按顺序补齐；功能未接入时按真实能力显示未支持/不可用。007整合完整设备/诊断/更新展示及页面验收，不是首次创建所有入口；不得另建绕过真实菜单/权限的临时演示页来满足早期链路。此顺序属于原任务内部分工，不增加TASK或提前声明007完成。

003首次可安装后立即跑“Windows安装→真实身份登录→菜单打开终端页→一次文件选择/受控测试打印→退出再开”；004首次可安装后跑“PDA安装→真实身份登录→取得扫描会话→真实广播/相机→业务接收→离页释放”。006接入时在实际两端跑“原聊天入口→邀请/对方同意→绑定→OS授权/轨道→协商→实际收听/画面→分项结束→另一能力保留”。采集与邀请先后沿PF06既有定义，不能让fixture提前赋好权限、绑定和媒体状态。

这些链是实现过程中的自测，不新增逐卡派遣/验收门。失败定位到最早环节，先修原因再走到直接上下游；接口200、系统受理和真实设备结果单列。已通过且未受变更影响的Web业务不反复取证。

### 10.2 Given / When / Then矩阵

| ID / R映射 | Given / When | 必须观察的Then及负例 | 主TASK |
| --- | --- | --- | --- |
| A06A-01 / R1/R2 | 无Vite的已安装包，使用合法部署配置；冷启动聊天深路由，刷新并恢复身份，访问当前认证分支启用的API/Hub；Web另回归显式同源API路径及embedded | 本地资源完整、远端请求使用有效入口；缺配置/跨源/坏证书明确失败，不回localhost、不关闭安全校验；embedded不附Bearer、不装配管理API/Hub，会话失效仍走宿主重新进入页 | 002/003/004 |
| A06A-02 / R1/R6 | 已有种子库和最低权限账号；从PC/PDA实际菜单进入，读/配置/更新权限各自撤销 | 新旧库入口一致且不覆盖自定义；无权按钮/直接调用均拒绝；中英/明暗/窄屏/200%/键盘/软键盘通过；用户管理查询、原聊天菜单和PDAEnter不回归 | 007 |
| A06A-03 / R1/R5 | A登录并留草稿、通知、设备会话；刷新并发换B/过期/撤权，重新启动；分别覆盖bearer与现有Web embedded，原生embedded按已冻结支持矩阵 | 旧Token/草稿/句柄/事件不落入B；HTTP与宿主epoch一致；原生无sessionStorage明文Token；embedded不镜像Cookie或恢复缓存身份；旧刷新/通知点击不能复活A；存储不可用无静默降级 | 002/006 |
| A06A-04 / R2/R3 | 一个bridge命令处理中；同ID同payload、同ID异payload、超限/未知版本/子frame/伪Origin、取消后迟到成功/失败 | 仅一次设备调用；非法输入被真正接收端拒绝；旧回包不影响新状态且已取得资源释放；当前错误保留，物理结果不明标Unknown | 002/003/004 |
| A06A-05 / R3 | 真PDA有当前扫描输入；连续扫同码两次、桥接重放同event、Enter/Tab/None/IME、切页/切人后再发广播 | 两次真实码均接收、重放一次；不误发聊天/表单、不丢前导零/GS1；失去所有权不投递；广播伪造/双通道/重复receiver按目标协议可观察 | 004 |
| A06A-06 / R3 | 真PDA相机/BLE被拒/永久拒绝/设置撤权/断连；write发出后丢响应，后台后返回 | 不循环授权；相机/连接释放；写结果未知不自动重发；重连生成新ID、旧notify无效；实际设备值/回应被核对，demo不冒充SPP | 004 |
| A06A-07 / R2/R6 | 受控测试文件/固定打印测试页；取消选择/保存/打印、非法路径和任意HTML、设备忙 | 不越出句柄/目录/模板边界；实际系统对话框且人工出纸与SystemOnly分开；无标签队列；同机Agent已拥有设备时不抢占 | 003/007 |
| A06A-08 / R2/R5 | Windows与第二真实端已通话/共享；最小化、X关闭到托盘、恢复、页内浮窗缩放再拖动、退出/锁屏 | 视图操作保持媒体且音频只播一次；退出/锁屏按§8结束资源及对端；选源取消/源窗口销毁清对应轨道，不默认共享第一屏 | 003/006 |
| A06A-09 / R5 | 两种能力已建立；停止一项、同时新增另一项、迟到Ready/Answer/错误；多设备抢接/权限撤销 | 按PF06上下文/协商规则，单项失败不误停另项；观察实际音频/视频及本地轨道；协议PASS不能扩大成真实媒体PASS | 006 |
| A06A-10 / R4 | 已安装当前合法版本；三种发布各完成下载和升级，重开/查询实际版本 | installer/apk/Bundle均至少一次真实升级；状态、实际版本、清单hash匹配；不是仅生成文件或模拟调用 | 005 |
| A06A-11 / R4 | 错appId/环境/通道/架构、过期清单/旧sequence/未知keyId/签名hash篡改、bridge/API不兼容 | 在执行前拒绝对应包，当前版本仍可用；不以签名有效跳过版本/身份，不以HTTPS代替签名 | 005 |
| A06A-12 / R4 | 下载断网、ETag变/Range回200、磁盘满；解压穿越/异常大小；在暂存写盘/slot切换/health前分别中止进程再启 | 不拼坏包、不写越界、不丢lastGood；状态文件重新读回及UTC一致；同包最多自动激活一次，旧activation健康回包拒绝 | 005 |
| A06A-13 / R4/R5 | 有未保存表单、文件传输、语音/共享/设备在途；检查空闲瞬间再开始新操作，owner崩溃 | 激活锁与lease准入原子互斥；完整busy原因可见，Unknown不被当Idle；稍后/取消恢复业务准入，更新器默认退出安装不能绕过 | 002/005/006 |
| A06A-14 / R4/R6 | Bundle入口/动态chunk坏、bridge错误；断网但本地包正常；安全floor高于lastGood；诊断注入秘密样本 | 坏包60秒内触发既定恢复，离线正常壳不误回退；禁止回到不安全包，内置维修页可用；诊断脱敏断言、容量/保留/取消保存均通过 | 005/007 |
| A06A-15 / R2/R3/R6 | 真实分发环境安装、覆盖升级、卸载/重装；Windows受限权限、APK签名不符/安装权限拒绝 | 支持的安装路径真实完成，失败有恢复说明；配置/凭据按边界处理，未安装机器/模拟器不计真机 | 003/004/005/008 |
| A06A-16 / R5 | PDA前后台/锁屏/强杀/旋转/OS授权弹层；有新消息/旧通知，再冷启动 | 清理与受控外部流程区分；无重复receiver/媒体Host；恢复先身份后权威消息，点击重新鉴权；未启用push时杀进程后通知不记送达PASS | 004/006 |

测试可用可控延迟/故障注入证明竞态，但明确标模拟层；必须保留真实设备及两端业务结果。进程中止只对本任务隔离测试实例，不能结束用户IDE/当前服务。验收数据使用已批准隔离身份/设备测试内容，不扩权、新建账号或清理普通业务数据来绕过前置。

### 10.3 证据、支持声明和命令

每个案例记录`caseId、容器/布局/OS/WebView及版本、Native/Web/bridge/API契约、设备/固件/adapter、权限状态、包来源/签名验证摘要、Given/实际操作/Then、结果、证据路径`。双端媒体另记录发送/接收方、采集/连接/播放/停止各层；分轨时限沿PF06§8.3，无实际测量不补造数字。PF06用户已通过的Web停止基线继续引用原工作包，不再次当成当前缺陷。

结果只用PASS/FAIL/BLOCKED/NOT RUN/用户后置/不适用（写原因）；Unknown能力核验不能改写为Unsupported已通过，未授权和不支持也不能混用。网络/TURN/同条件画质/30分钟专项明确标后续云部署，任务受影响时引用原安排。001若决定首版不提供某种原生能力，必须先回写范围/按钮/验收三处，不靠标“不适用”临时缩小交付。

稳定交接写入`docs/evidence/PF-06A.md`，包含实际HEAD、既有WIP、本轮manifest/逐文件hash、修改范围、用例矩阵和剩余项；本次不创建虚假执行证据。开发完成自测再交独立验收，返修只验缺陷与受影响路径，主控核对完整性及最终门禁，不第三次重跑全套。

当前已有前端命令在`src/frontend`执行：`pnpm typecheck`、`pnpm lint`、`pnpm exec vitest run <明确spec路径>`、`pnpm build`；测试必须核对实际筛选范围和退出码。原生构建/签名/安装命令由001从实际工程工具链登记，不能用前端build替代。若本包修改后端权限/种子源码，最终按仓库先`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`再`dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`；解决锁后必须新鲜重建。纯文档复审只检查引用/结构/契约一致性，不跑业务全套测试。

## 11. 官方技术依据与使用限制

2026-09-14复核下列官方资料用于确认平台限制；它们不替代G06A-1的目标版本/设备/许可核验。本文件的超时、字段、busy和首版范围是项目设计要求，不能表述为所有平台的默认行为。

- [Electron安全指南](https://www.electronjs.org/docs/latest/tutorial/security)：隔离、sandbox、IPC sender校验、导航/权限和自定义协议；应用按§8.1实现受限入口。
- [Electron desktopCapturer](https://www.electronjs.org/docs/latest/api/desktop-capturer)：捕获请求与可用时的system picker；不能把示例默认选源当用户同意。
- [Electron safeStorage](https://www.electronjs.org/docs/latest/api/safe-storage)：OS安全存储能力及平台限制；业务主体隔离与会话清除仍由应用实现。
- [Capacitor配置](https://capacitorjs.com/docs/config)、[App生命周期](https://capacitorjs.com/docs/apis/app)：本地Web资源/Origin及App事件；生产配置和目标WebView需实际核验。
- [Android蓝牙权限](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions)、[广播规则](https://developer.android.com/develop/background-work/background-tasks/broadcasts)、[通知权限](https://developer.android.com/develop/ui/compose/notifications/notification-permission)：权限/来源/注册不能按浏览器经验推断。
- [Android MediaProjection](https://developer.android.com/media/grow/media-projection)：每次捕获同意、目标版本前台服务及token使用限制；本项目仍须验证接入现有WebRTC传输，不因API存在承诺已支持。
- [Capgo Updater API](https://capgo.app/docs/plugins/updater/api/)：notifyAppReady默认10秒及appReadyTimeout；本项目60秒与安全floor需单独映射验证。候选插件仍未选定。
- [electron-builder更新文档](https://www.electron.build/docs/features/auto-update/)、[NSIS updater API](https://www.electron.build/docs/api/electron-updater.class.nsisupdater/)：独立发布元数据、自动安装时机和quitAndInstall窗口关闭顺序；具体配置以001锁定版本的文档/API为准，不混用旧版与当前属性。
