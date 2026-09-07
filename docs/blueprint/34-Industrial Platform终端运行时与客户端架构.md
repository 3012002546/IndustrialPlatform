# Industrial Platform 终端运行时与客户端架构

版本：V1.0；日期：2026-09-07；状态：路线已定，实施规格待设备/更新渠道核验，未开发。

## 1. 定位与阶段

采用共享 Vue3 业务层，Web 浏览器、Electron Windows PC、Capacitor Android PDA 分别实现宿主能力。新增 **PF-06A**，在 PF-05/PF-06 Web 功能和验收完成后执行，原 PF-07 Scheduler/Platform Health 编号保持不变。实施与唯一任务清单见[实施 09A](../implementation/09A-Industrial%20Platform终端运行时与客户端打包开发实施方案.md)。

PC/PDA/Mobile 是布局维度；Web/Electron/Capacitor 是运行容器维度，不凭 UA 或页面尺寸猜原生能力。PDA 与 Mobile 在 PF-05 就拥有完整 Web 聊天。Mobile 是否包含 iOS 原生包、分发渠道和最低系统版本在专项 001 冻结，不隐含承诺全平台原生包。

## 2. 复用与所有权

共享 UI 组件、API、权限、locale、状态合并与领域规则。业务模块通过 Platform Runtime 使用扫码、摄像头、文件、打印、设备信息、生命周期、提醒导航和更新能力，禁止直接引用 window.electronAPI、Capacitor.Plugins、厂商 JAR 类名或广播 Action。

Runtime 不新增核心 Service Host，不拥有聊天、标签任务或称量业务。Web 适配只实现真正用到的浏览器能力；Native Adapter 负责权限和设备调用。跨窗口/进程/设备共享代码与协议，不假设共享内存 Store。PF-05/06 不提前搭建空插件体系。

## 3. 扫码与工业设备

统一扫码结果至少包含 value、source、可选 symbology、receivedOn、sessionNId；来源为 keyboard-wedge、broadcast、vendor-sdk、camera。输入所有权由当前业务会话显式取得/释放；结束符、去重窗口、连续输入与失焦策略可配置并在真机验证。聊天输入不能把全局扫码回车当发送。

Android 广播接收与 Kotlin/Java 插件隔离厂商协议，按实际 SDK 版本确认导出权限、来源验证与生命周期。后置相机支持一次扫码兜底。HID 是键盘输入，BLE GATT 与 Bluetooth Classic SPP 是不同适配；验证一种 BLE 设备不代表已支持 SPP 蓝牙打印机。

首版验收一台真实 PDA、一种广播协议、一次相机扫码、一种 BLE 设备连接/读写/断连。多厂商 SDK、RFID/NFC、永久后台连接、静默安装、完整 MDM 不作为首版完成条件。标签专项的 PDA 蓝牙打印必须另验实际打印协议，不能以 BLE demo 替代。

## 4. 更新与业务保护

| 容器 | 首版更新路线 | 边界 |
| --- | --- | --- |
| PC Electron | 签名完整安装包更新 | 不做 PC 独立 Web Bundle 热更新 |
| PDA Capacitor | Web Bundle 更新 + APK 完整升级 | 原生插件变化必须升级 APK |
| Web | 原有 Web 发布机制 | 不冒充原生升级 |

维护 NativeVersion、WebVersion、BridgeContractVersion、MinNativeVersion 和发布通道。更新清单/产物需受信签名与 hash 校验；先下载到暂存区，校验兼容性后切换，首次启动健康确认失败回退最后良好版本。禁止降级到不满足安全下限的包；离线保留当前可用版本并明确状态。

更新不能中断打印、称量确认、文件传输或屏幕共享；繁忙状态由能力所有者报告，安全点才激活。不得将“等待安全点”变成无限强制重启。后台通知、锁屏与恢复、托盘和进程退出分别定义，不承诺 WebView 长连接永久在线。

## 5. 安全与设备代理衔接

Electron 保持 contextIsolation、sandbox，关闭页面 Node 集成；preload/contextBridge 仅暴露受限命名能力，校验 IPC sender、参数和目标。文件路径、外链、设备选择都有限制，不开放任意命令/端口写入。

Capacitor 按需请求相机/蓝牙权限，处理拒绝、撤销、锁屏、进程结束；共用设备换人清理旧用户授权、缓存、连接与推送绑定。安全存储与令牌生命周期独立于普通业务 Store。

标签 PF-10B 定义 Device Agent 的执行队列与互斥；浏览器经服务端派发，Electron 优先复用同一 Agent。PF-06A 仅提供打印能力端口与受控基础验证，不复制标签服务或建立另一条抢占设备的执行队列。称量读数质量/稳定性/确认仍归未来 Weighting。

## 6. 核验来源与待决策

路线来自参考对话《寻找PDA打包方案》的最终采纳结论；本轮只编排文档。技术边界核验于 2026-09-07：[Capacitor Plugins](https://capacitorjs.com/docs/plugins) 提供 Web 到原生 API 的桥接；[Electron 安全指南](https://www.electronjs.org/docs/latest/tutorial/security)用于约束上下文隔离和受限 IPC。具体插件、版本、许可、更新服务或自托管方案在 001/005 用设备与部署证据固定，候选不能当作现成依赖。

未确定：实际 PDA 厂家/系统/SDK、BLE 与 SPP 设备、内网证书和分发权限、APK 安装权限、Mobile 原生范围、推送渠道与后台能力。任务可以设计和按端口实施；缺失真实设备或发布条件时相应项保持待验收。

组件核验优先沿已采纳讨论路线：Electron + electron-vite + electron-builder/electron-updater；PDA Capacitor + Kotlin/Java 薄桥接，Bundle 更新先评估 Cap-go/capacitor-updater。DataWedge 插件/demo、官方相机扫码、Headwind MDM、XUpdate 仅按适用范围参考，MDM 不作为首版门禁。
