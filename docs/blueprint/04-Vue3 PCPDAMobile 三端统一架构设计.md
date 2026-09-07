# Vue3 PC/PDA/Mobile 三端统一架构设计

> 文档版本：V1.0
> 文档类型：前端架构设计文档
> 技术栈：Vue3 + TypeScript + Vite + Pinia + Element Plus + ECharts
> 目标：一套代码，多端适配（PC Web / PDA / Mobile）

---

# 1. 设计目标

## 1.1 建设目标

Industrial Platform 前端采用：

> 多端统一前端架构

实现：

```text
                 Vue3

                  |

        --------------------

        |                  |

     PC Web             Mobile


        |

      PDA


```

目标：

* PC端管理系统
* PDA生产现场操作
* 手机移动审批
* 扫码作业
* 设备操作

尽量：

> 业务代码复用，UI根据终端适配。

---

# 2. 前端整体架构

## 2.1 技术架构

```text
Vue3

 |

TypeScript

 |

Vite

 |

Pinia

 |

Vue Router

 |

Component Framework

 |

API Layer

 |

Backend API

```

---

# 3. 多端设计策略

## 3.1 不采用三套项目

不推荐：

```text
mes-pc

mes-pda

mes-mobile

```

原因：

* 代码重复
* 维护困难
* 业务逻辑无法复用

---

采用：

```text
Industrial.Web


一个项目


根据设备类型适配


```

---

# 3.2 终端识别

启动时判断：

```typescript
DeviceType

{

PC

PDA

MOBILE

}

```

来源：

```text
UserAgent

屏幕宽度

Touch能力

```

---

# 4. 项目目录设计

推荐：

```text
industrial-web


├── src


│
├── api

│
├── assets

│
├── components

│
├── composables

│
├── directives

│
├── layouts

│
├── router

│
├── stores

│
├── utils

│
├── views

│
├── device

│
├── platform

│
└── types


```

---

# 5. 业务模块化设计

采用：

Domain Frontend。

例如：

```text
modules


├── workorder

│
├── material

│
├── weighting

│
├── equipment

│
├── trace

│
├── report

```

---

结构：

```text
workorder


├── api

├── components

├── pages

├── hooks

├── types


```

---

# 6. Layout设计

## 6.1 PC布局

适合：

管理人员。

结构：

```text
+--------------------------------+

| Header                         |

+---------+----------------------+

| Menu    | Content              |

|         |                      |

|         |                      |

+---------+----------------------+


```

---

技术：

```text
LayoutPC.vue

```

---

# 6.2 PDA布局

生产现场。

特点：

* 大按钮
* 少菜单
* 快操作

结构：

```text
+----------------+

任务

----------------

工单:

WO001


物料:

MAT001


状态:

执行中


[扫码]


[称量]


[完成]


+----------------+

```

---

组件：

```text
LayoutPDA.vue

```

---

# 6.3 Mobile布局

手机：

```text
底部Tab


首页

任务

消息

我的

```

组件：

```text
LayoutMobile.vue

```

---

# 7. 响应式设计

采用：

CSS Variable

例如：

```css
--page-padding

--button-size

--font-size

```

根据设备：

PC：

```text
padding:24px

```

PDA：

```text
padding:12px

```

Mobile：

```text
padding:8px

```

---

# 8. 公共组件平台

建立：

MES UI Framework

目录：

```text
components


├── MESForm

├── MESGrid

├── MESScanner

├── MESPrinter

├── MESUpload

├── MESWeight

├── MESStatus

├── MESDashboard


```

---

# 9. MES业务组件设计

## 9.1 扫码组件

MES核心组件。

支持：

* 条码
* QR
* RFID

组件：

```vue
<MESScanner
 @success="scanSuccess"
/>

```

---

能力：

```text
摄像头扫码

扫码枪输入

蓝牙扫描枪


```

---

# 9.2 称量组件

对应：

Weighting Service

组件：

```vue
<MESWeight

 scale="Scale001"

 target="10"

 />

```

功能：

* 读取重量
* 稳定判断
* 超差提示
* 自动保存

---

# 9.3 打印组件

支持：

```text
ZPL

ESC/POS

蓝牙打印

网络打印

```

组件：

```vue
<MESPrinter/>

```

---

# 10. PDA生产应用设计

## 10.1 PDA首页

显示：

```text
今日任务


----------------

待生产

10


待称量

5


异常

2


```

---

## 10.2 工单执行

流程：

```text
扫描工单


↓

确认物料


↓

执行工序


↓

采集数据


↓

完成


```

---

## 10.3 物料上线

流程：

```text
扫描物料


↓

校验批次


↓

绑定容器


↓

上线


```

---

## 10.4 称量操作

流程：

```text
扫描任务


↓

扫描物料


↓

读取电子秤


↓

判断重量


↓

打印标签


↓

生成记录


```

---

# 11. 离线能力设计

工业现场：

网络不稳定。

离线能力按业务阶段独立设计，下面是后续MES离线作业的架构方向。PF05/06、PF06A与PF10B首版不承诺Offline First或离线创建生产任务：聊天用服务端持久事实和联网补拉；标签离线只保留执行账本/待回执，恢复先对账。

---

架构：

```text
PDA


 |

Local Storage


 |

Sync Service


 |

MES API


```

---

# 11.1 后续离线业务的本地存储候选

仅在已明确离线业务、加密/保留/冲突与恢复规则的后续阶段选用；不是当前聊天或Runtime新增本地数据库的要求。候选：

SQLite

或者：

IndexedDB

保存：

```text
任务

扫码记录

称量记录

临时数据

```

---

# 11.2 后续离线业务同步机制

以下为离线业务候选状态，不能替代当前聊天发送状态、更新状态或标签PrintItem状态。实际启用前由所属阶段完成详细设计。

数据状态：

```text
Local

 |

Pending

 |

Syncing

 |

Completed

```

---

# 12. 移动设备能力

## 12.1 摄像头

支持：

```text
Camera API

```

---

## 12.2 蓝牙

支持：

```text
蓝牙扫描枪

蓝牙打印机

蓝牙电子秤

```

---

## 12.3 NFC

未来：

```text
人员识别

物料识别

设备识别

```

---

# 13. APP封装方案

推荐：

Capacitor

架构：

```text
Vue3


 |

Capacitor


 |

Android/iOS


```

---

优势：

* 复用Web代码
* 调用原生能力
* 支持工业PDA

---

# 14. API层设计

统一：

```text
api


 |

service


 |

request


```

例如：

```typescript
workOrderApi.ts


export function getTask()

export function completeTask()

```

---

# 15. 状态管理

Pinia。

模块：

```text
stores


├── user

├── permission

├── device

├── terminal

├── cache


```

---

# 16. 权限设计

支持：

按钮级权限。

例如：

```vue
<Button

 v-permission="'WO.COMPLETE'"

>

完成

</Button>

```

---

# 17. 实时通信

SignalR。

用途：

## PC

实时：

* 设备状态
* 告警
* 生产数量

---

## PDA

推送：

* 新任务
* 异常

---

示例：

```text
Server

 |

SignalR Hub

 |

Client

```

---

# 18. 看板设计

ECharts。

组件：

```text
MESChart


MESGauge


MESAlarmPanel


MESKanban


```

支持：

拖拽布局：

```text
Dashboard Designer

```

---

# 19. 前端工程规范

## TypeScript规范

禁止：

```typescript
any

```

统一：

```typescript
interface

type

```

---

## API规范

所有接口：

```typescript
async

Promise<T>

```

---

## 组件规范

一个组件：

一个职责。

---

# 20. 与后端微服务对应

前端模块：

| 前端        | 后端                |
| --------- | ----------------- |
| WorkOrder | WorkOrder Service |
| Inventory | OperationalData Service |
| Weighting | Weighting Service |
| Equipment | Equipment Service |
| Trace     | Trace Service     |
| Monitor   | Server Monitor    |

---

# 21. 开发建议

## 第一阶段

先完成：

PC + PDA Web

原因：

MES现场主要：

* 工控机
* 平板
* PDA浏览器

---

## 第二阶段

增加：

Capacitor APP

---

## 第三阶段

增加：

工业PDA深度能力：

* 扫码枪SDK
* 打印SDK
* NFC

---

# 22. 总结

最终前端架构：

```text
              Vue3


                |

        Unified Frontend


                |

--------------------------------


PC Web

PDA

Mobile APP


--------------------------------


                |

            MES API


```

核心原则：

```text
业务代码复用

UI适配终端

能力插件化

离线能力按业务阶段设计，当前首版以在线权威与恢复对账为准

```

## 三端协作与运行容器增量（2026-09-07）

PF-05 交付 PC/PDA/Mobile 的完整 Web 聊天，PF-06 交付 Web 共享/观看；PDA/Mobile 不是仅缩放 PC 页面。登录后的应用级连接、未读、抽屉/全屏导航、主题/密度/语言、软键盘、安全区、扫码焦点及换人隔离按实施 08/09 验收。

PF-06A 才接 Electron Windows 与 Capacitor Android PDA。布局维度与容器能力分离，共享业务核心经最小 Runtime 调用 Web/原生能力，不在页面直接依赖厂商 SDK。只共享代码/协议，不假设跨窗口共享 Store。终端蓝图与版本/更新/设备规则见[蓝图 34](34-Industrial%20Platform终端运行时与客户端架构.md)。标签 PDA 直连打印与 Windows Agent 接入见[蓝图 35](35-Industrial%20Platform标签管理平台设计.md)，不强制两端相同底层实现。
