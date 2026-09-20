# Industrial Platform 终端运行时与客户端架构

版本：V1.2；日期：2026-09-20；状态：路线已定，实际工具链/设备/更新渠道待核验，未开发。

## 1. 定位与阶段

采用共享 Vue3 业务层，Web 浏览器、Electron Windows PC、Capacitor Android（手机首验，后续PDA） 分别实现宿主能力。新增 **PF-06A**，复用PF-05/PF-06已确认Web基线，原后置验收沿既定安排，原 PF-07 Scheduler/Platform Health 编号保持不变。实施与唯一任务清单见[实施 09A](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md)。

PC/PDA/Mobile 是布局维度；Web/Electron/Capacitor 是运行容器维度，不凭 UA 或页面尺寸猜原生能力。PDA 与 Mobile 在 PF-05 就拥有完整 Web 聊天。首轮手机使用同一Android APK及PDA布局，不另建Mobile包或iOS包；实际Android/Windows最低系统与分发条件由001核验。

## 2. 复用与所有权

共享 UI 组件、API、权限、locale、状态合并与领域规则。业务模块通过 Platform Runtime 使用扫码、摄像头、文件、打印、设备信息、生命周期、提醒导航和更新能力，禁止直接引用 window.electronAPI、Capacitor.Plugins、厂商 JAR 类名或广播 Action。

Runtime 不新增核心 Service Host，不拥有聊天、标签任务或称量业务。Web 适配只实现真正用到的浏览器能力；Native Adapter 负责权限和设备调用。跨窗口/进程/设备共享代码与协议，不假设共享内存 Store。PF-05/06 不提前搭建空插件体系。

## 3. 扫码与工业设备

新增共享AppScanInput（标题、文本框、拍照图标）和扫码测试页（控件、结果Label、清空）。mode/inputDisabled/showCamera控制表现；广播模式隐藏拍照入口，禁用输入不停止广播。具体线框、字段和S01～S12见09A§3。当前开发只分001客户端与扫码、005更新与终端交付、009 SQL Server三个任务。

统一扫码结果采用 ScanResultV1：contractVersion、eventNId、value、source、可选 symbology、receivedOn、sessionNId、subjectEpoch。首版 source 为 keyboard、broadcast、camera；键盘楔入（keyboard-wedge）映射为 keyboard，vendor-sdk 是后续厂商适配路线，不是首版公开枚举。输入所有权由当前业务会话显式取得/释放；结束符、连续输入与失焦策略在真机验证。按 eventNId 排除同一事件重放，不按条码值或全局时间窗口丢弃连续相同条码。聊天输入不能把全局扫码回车当发送。字段和方法见[09A统一开发文档](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md)§3/5/6。

Android 广播接收与 Kotlin/Java 插件隔离厂商协议，按实际 SDK 版本确认导出权限、来源验证与生命周期。后置相机支持一次扫码兜底。HID 是键盘输入，BLE GATT 与 Bluetooth Classic SPP 是不同适配；验证一种 BLE 设备不代表已支持 SPP 蓝牙打印机。

首轮在真实Android手机验证安装、输入/相机扫码和生命周期；PDA厂商广播实现及验收后置，保留桥接接口。既定一种BLE设备连接/读写/断连单独验证，手机可用不等于外设已验。多厂商 SDK、RFID/NFC、永久后台连接、静默安装、完整 MDM 不作为首版完成条件。标签专项的 PDA 蓝牙打印必须另验实际打印协议，不能以 BLE demo 替代。

## 4. 页面热更新与外壳升级

| 变更 / 场景 | Windows Electron | Android Capacitor |
| --- | --- | --- |
| Vue/HTML/CSS/JS等页面资源 | Web资源包热更新，无需重打安装包 | Web资源包热更新，无需重打APK |
| 原生SDK、插件、权限或外壳 | 完整Windows安装包升级 | 完整APK升级，含JAR/AAR变化 |
| 正在使用 / 普通后台或托盘恢复 | 保持本次页面版本，不因发布新版刷新或安装 | 同左 |
| 重新启动 / 系统回收后启动 / 重新登录 | 先检查更新；有更新显示进度并完成后进入，无稍后/跳过 | 同左 |
| 检查或更新失败 | 只给重试/退出；保留文件供恢复，不放行旧版业务 | 同左 |

共享Vue源通过Web发布流程产出不可变的版本资源及客户端资源包。先上传完整资源再发布清单，保留使用中的旧文件；客户端只在启动检查阶段下载/校验/暂存/原子切换。页面由Runtime桥接调用原生能力，不能仅靠网页发布新增JAR或原生接口。

NativeVersion、WebVersion、BridgeContractVersion及兼容范围按[09A统一开发文档](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md)§4/6维护。两端Web资源启动健康窗口均为60秒；同包最多自动激活一次，失败恢复修复界面，不绕过必更进入旧版业务。安装包按系统授权与签名规则执行，不承诺静默安装或任意降级。

本阶段不建设灰度平台、班次管理、服务端业务版本准入系统或跨业务busy lease。后台媒体、锁屏清理和托盘仍按既有生命周期处理，不因热更新改变。

## 5. 安全与设备代理衔接

Electron 保持 contextIsolation、sandbox，关闭页面 Node 集成；preload/contextBridge 仅暴露受限命名能力，校验 IPC sender、参数和目标。文件路径、外链、设备选择都有限制，不开放任意命令/端口写入。

Capacitor 按需请求相机/蓝牙权限，处理拒绝、撤销、锁屏、进程结束；共用设备换人清理旧用户授权、缓存、连接与推送绑定。安全存储与令牌生命周期独立于普通业务 Store。

标签 PF-10B 定义 Device Agent 的执行队列与互斥；浏览器经服务端派发，Electron 优先复用同一 Agent。PF-06A 仅提供打印能力端口与受控基础验证，不复制标签服务或建立另一条抢占设备的执行队列。称量读数质量/稳定性/确认仍归未来 Weighting。

## 6. 核验来源与待决策

路线来自参考对话《寻找PDA打包方案》的最终采纳结论；本轮只调整文档。技术边界核验于 2026-09-07：[Capacitor Plugins](https://capacitorjs.com/docs/plugins) 提供 Web 到原生 API 的桥接；[Electron 安全指南](https://www.electronjs.org/docs/latest/tutorial/security)用于约束上下文隔离和受限 IPC。具体插件、版本、许可、更新服务或自托管方案在 PF06A-001 用设备与部署证据核验并补齐规格，005实现已明确的更新路径；候选不能当作现成依赖。

待核验：Android手机/Windows实际系统、既定BLE设备、内网证书和分发/安装权限、更新工具及原生媒体能力。PDA厂家/SDK/广播明确后置；SPP、iOS和远程推送不自动纳入本轮。可先派001受控核验；会影响接口、设备字段或更新路线的输入未明确前，相关生产实现保持待派遣。实现完成后仍缺实机验收证据的项保持待验收，两类状态不得混用。

组件核验优先沿已采纳讨论路线：Electron + electron-vite + electron-builder/electron-updater；PDA Capacitor + Kotlin/Java 薄桥接，Bundle 更新先评估 Cap-go/capacitor-updater。DataWedge 插件/demo、官方相机扫码、Headwind MDM、XUpdate 仅按适用范围参考，MDM 不作为首版门禁。
