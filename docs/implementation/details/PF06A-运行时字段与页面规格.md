# PF06A 运行时字段、更新与页面规格

版本V1.0；2026-09-07；对应实施09A。派遣状态待派遣；就绪度待实际目标核验。遵循[派遣前规则](../STANDARD-派遣前详细设计与页面验收.md)。本阶段**无业务SQL表**，不得为了“表名和字段”建立空Schema；以下冻结JSON、TypeScript桥接DTO和本地状态结构。

## 1. 文件与字段类型

生产配置由容器受控写入，不由普通网页任意修改。普通配置不含AccessToken、刷新Token、签名私钥、Bluetooth配对凭据；安全凭据由OS安全存储管理。文件原子替换、旧文件在新文件完整校验前保持有效；路径在容器私有目录，不接受远端任意路径。数值时间点为UTC RFC3339字符串，超时用单调时钟。

| 对象/归属 | 精确字段（JSON/TS类型） | 默认/限制/生命周期 |
| --- | --- | --- |
| runtime-config.json；本地容器 | schemaVersion:number=1,environmentNId:string<=64,apiOrigin:string<=2048,allowedOrigins:string[],terminal:'pc'或'pda',channel:'stable'或'pilot',scannerMode:'keyboard'或'broadcast'或'camera',suffix:'Enter'或'Tab'或'None',diagnosticConsent:boolean=false | Origin只允许受信HTTPS清单；本地开发显式例外，不接受通配；首版仅Windows PC/Android PDA；升级保留用户配置 |
| release-manifest.json；发布侧签名 | schemaVersion:1,releaseNId:string32,platform:'win-x64'或'android',packageKind:'installer'或'apk'或'web-bundle',channel,version:string<=64,nativeMinVersion:string<=64,bridgeMinVersion:number,bridgeMaxVersion:number,securityFloorVersion:string<=64,artifactUrl:string,artifactSha256:string64,artifactSizeBytes:string,issuedOn:string,expiresOn:string,keyId:string<=64,signature:string | max/min按版本比较而非字符串字典序；manifest≤64KiB；SHA256小写hex；artifact大小非负十进制Int64；下载来源白名单；签名算法/公钥分发由G06A-1选型后冻结 |
| update-state.json；本地更新器 | schemaVersion:1,state:UpdateState,currentNativeVersion:string,currentWebVersion:string,lastGoodWebVersion:string,pendingReleaseNId?:string,pendingArtifactHash?:string,downloadedBytes:string='0',activationAttempt:number=0,lastErrorCode?:string,updatedOn:string | UpdateState见§3；不是业务审计；包状态不可与私钥混存；每次切换原子写入 |
| scan-session；仅内存 | sessionNId:string32,ownerPageId:string<=128,subjectEpoch:string,source:'keyboard'或'broadcast'或'camera',acquiredOn:string,cancelled:boolean=false | 全容器唯一活动扫描会话；离页/换人release；不得持久缓存条码业务值 |
| ScanResultV1；桥接消息 | contractVersion:1,eventNId:string32,sessionNId:string32,value:string(1～4096),source,symbology?:string<=64,receivedOn:string,subjectEpoch:string | 不以条码value去重；按eventNId去重，连续两次相同业务码可各自上报；桥接重放不重复处理 |
| CapabilitySnapshotV1 | contractVersion:1,container:'web'或'electron'或'capacitor',os:string,osVersion:string,nativeVersion?:string,webVersion:string,bridgeContractVersion:number,capabilities:CapabilityEntry[] | Entry={name:string,availability:'Supported'或'Unsupported'或'PermissionRequired'或'Degraded',reasonCode?:string}；设备支持与用户权限分开 |
| BridgeRequestV1 | contractVersion:1,requestNId:string32,method:白名单枚举,payload:该方法DTO,subjectEpoch:string | 请求≤64KiB；reject未知method/version/origin；不提供eval/shell/sql/fs任意路径 |
| BridgeResponseV1 | contractVersion:1,requestNId:string32,ok:boolean,data?:对应DTO,error?:{code:string,retryable:boolean},subjectEpoch:string | 结果与请求方法一一映射，ok与error互斥；取消不是设备成功 |
| device-profile.json；容器配置 | schemaVersion:1,profileNId:string32,kind:'ble'或'printer'或'scanner',displayName:string<=200,adapterKey:string<=128,deviceHandle:string<=256,settings:已验证adapter DTO | deviceHandle不是认证因素；只保存受权绑定，秘密走安全存储；adapter尚未定型不开放任意settings |
| diagnostics；滚动本地文件 | occurredOn:string,level:'info'或'warn'或'error',code:string<=96,requestNId?:string32,adapterKey?:string<=128,durationMs?:number,version:string | 不记条码/消息/标签/地址/Token；最多10MiB×3文件，默认7天；导出前脱敏 |

备份/恢复不复制原机凭据；重装重新绑定。变更插件、Android权限、AAR/JAR或原生bridge必须完整APK/安装包升级，不由web-bundle掩盖。

## 2. 方法签名与业务边界

TypeScript所有方法返回Promise；订阅返回unsubscribe。以下是公共运行时契约，不要求Web具备原生能力。默认交互超时10秒，用户系统授权/扫码流程60秒可取消，连接/打印具体超时按adapter冻结；超时后必须能关联原requestNId，不能自动重发有副作用操作。

| 方法 | 输入 | 输出/约束 |
| --- | --- | --- |
| capabilities.get | 无 | CapabilitySnapshotV1 |
| scanner.acquire | {ownerPageId,source,subjectEpoch} | {sessionNId}；忙返回DEVICE_BUSY |
| scanner.release | {sessionNId} | {released:boolean}；重复release幂等 |
| scanner.onResult | 当前sessionNId | ScanResultV1流；主体/会话不符丢弃 |
| camera.scanOnce | {sessionNId,formats:string[]} | ScanResultV1或用户取消；不连续偷摄 |
| bluetooth.requestDevice | {profileNId} | {deviceHandle,capabilities:string[]}；OS权限必经合法用户交互 |
| bluetooth.connect / disconnect | {deviceHandle,requestNId} | {connectionNId,state}；connection互斥，断连幂等 |
| bluetooth.exchange | {connectionNId,operation:adapter受控命令,requestNId,payload:该命令DTO} | {requestNId,result}；不是任意字节/UUID写入通道；工艺解析归adapter |
| files.pick / save | {purpose:白名单,mimeTypes:string[],maxBytes:string} / {artifactHandle,displayName} | {fileHandle,name,sizeBytes:string}；不返回任意绝对路径 |
| printer.getCapabilities | {profileNId} | {protocol,receiptLevel,supportedFormats}；未验证返回Unsupported |
| notifications.show | {eventNId,titleKey,target:{kind:'conversation',nId}} | {accepted:boolean}，默认无敏感正文；点击后重新鉴权 |
| navigation.open | {kind:受控路由枚举,nId} | {opened:boolean}；不执行任意deep link/URL |
| updater.check/download/activate | 无 / {releaseNId} / {releaseNId} | {state,progressBytes:string,errorCode?}；activate须§3安全点 |
| lifecycle.onChange | 无 | {state:'foreground'或'background'或'resume'或'identityChanged',subjectEpoch} |

相同requestNId不同payload返回RUNTIME_REQUEST_CONFLICT；桥接至多一次响应不能当设备物理exactly-once。BLE和Classic SPP分别adapter，支持BLE不能声称支持蓝牙打印。PF06A仅冻结打印能力描述与受控设备边界，PF10B拥有打印作业/回执/重打状态。

```json
{"contractVersion":1,"requestNId":"3771e632cc794c1dacd315e2f50dc3aae","method":"scanner.acquire","subjectEpoch":"user-session-3","payload":{"ownerPageId":"pda.label.print","source":"broadcast"}}
```

广播adapter必须在真实型号上固定action/category/extras/编码/来源验证规则，不把上例当通用厂商广播协议。IMEs、键盘楔入与业务回车冲突通过scan-session归属解决，不全局拦截所有Enter。

## 3. 更新状态机与恢复

`Idle → Checking → Available → Downloading → Verifying → Staged → WaitingForIdle → Activating → Healthy`；失败到`Failed`，激活失败到`RollingBack → Healthy/RecoveryRequired`。用户暂缓回到Staged；下载可恢复必须同时验证range/ETag/hash，不能只拼旧文件。错误码固定主文六类并补UPDATE_BUSY、UPDATE_DOWNLOAD_FAILED、UPDATE_HEALTH_TIMEOUT。

下载/校验可后台进行；激活前由各业务所有者汇总busy lease（扫码、未提交表单、打印在途、屏幕共享），未知busy按忙处理，不能仅按没有打开页面推断空闲。锁定激活意图后复查busy与当前版本，防检查后又开始打印。强制安全更新也不能静默中断在途打印，展示受控结束/维护流程。

WebBundle启动健康窗口60秒，未收到可信健康确认回最后良好包；最多自动激活1次，不循环重启。健康确认包括Vue启动/bridge兼容/本地关键资源可用，不能把客户网络断开当包损坏。回退不能低于securityFloorVersion；没有安全可回版本进入RecoveryRequired并给维修入口。PC installer/APK回退能力受系统安装规则约束，不能保证静默降级；记录操作员介入步骤。

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

## 5. 任务与验收映射

| TASK | 固定输入/实施步骤 | 关键可观察反例 |
| --- | --- | --- |
| PF06A-001 | §1字段+G06A-1设备/OS/SDK/签名/更新矩阵；只完成受控技术核验，先回写版本与adapter DTO再派生产 | 没实机不能声称广播/BLE支持；Mobile原生范围未定不创建工程 |
| PF06A-002 | §1/2 TS DTO→Web Unsupported/支持适配→主体epoch/scan-session | 旧主体响应丢弃；同请求异载荷拒绝；Web业务不回归 |
| PF06A-003 | §1/2 Electron main/preload边界、导航/文件/通知/窗口生命周期 | 非白名单Origin/method拒绝，关闭窗口与退出进程分开，无任意shell |
| PF06A-004 | §1/2 Android薄插件、型号固定广播/相机、键盘归属 | 两次相同条码都可接收，重放同Event只一次；IME/扫码不误触发送 |
| PF06A-005 | §1manifest/state+§3，PC/Bundle/APK三路径 | 篡改/hash错误/版本不兼容阻止激活；断电恢复；busy拒绝；损坏包回退 |
| PF06A-006 | 生命周期回接PF05/06权威状态 | 锁屏醒来先授权后共享；换人不继承草稿/旧事件；后台通知与持久消息分开证据 |
| PF06A-007 | W06A-01/02+§2打印能力边界/诊断 | 能力Unsupported清楚；BLE不冒充SPP；输出脱敏 |
| PF06A-008 | 主文R1～R6与本规格全部负例 | 真Windows+真PDA，安装包/清单/版本/设备矩阵与截图，缺项显式待验收 |

## 6. 派遣前设备与发布门禁

G06A-1由总控提供首台PDA型号/Android版本、厂商广播或SDK样本、BLE目标、Windows版本/架构、分发环境与签名责任方；技术核验输出固定adapter配置字段、依赖精确版本/许可、签名/公钥轮换与回退方式。未取得这些输入，生产001以后的具体实现不具备就绪条件；不可把任意设备支持留给开发自由扩张。文档中的共用字段已落定，设备字段只在真实adapter核验后补齐。
