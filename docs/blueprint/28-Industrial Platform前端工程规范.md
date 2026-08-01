# 28-Industrial Platform前端工程规范

> Industrial Platform
> 工业数字化执行平台
> Frontend Engineering Standard V1.0

---

# 文档说明

本文档定义：

```text
Industrial Platform
```

统一前端工程体系。

目标：

建立：

* PC Web端
* PDA手持端
* Mobile移动端
* 大屏Dashboard
* AI助手界面

统一技术体系。

最终支持：

```text
Vue3

+

TypeScript

+

Vite

+

Pinia

+

Element Plus

+

ECharts

+

Capacitor

```

---

# 1. 前端总体定位

Industrial Platform前端不是传统后台管理系统。

定位：

> 面向工业现场的人机交互平台。

包含：

```
管理端

生产执行端

设备监控端

移动操作端

AI智能助手端
```

---

# 2. 前端总体架构

```text
                  Industrial Platform UI


                         |

              Frontend Core Framework


                         |

 -------------------------------------------------

 PC Web        PDA         Mobile       Dashboard


                         |

 -------------------------------------------------

 Vue3

 TypeScript

 Pinia

 Router

 API SDK

 Component


```

---

# 3. 技术选型

## 核心框架

| 技术           | 用途    |
| ------------ | ----- |
| Vue3         | UI框架  |
| TypeScript   | 类型系统  |
| Vite         | 构建    |
| Pinia        | 状态管理  |
| Vue Router   | 路由    |
| Axios        | 接口    |
| Element Plus | PC组件  |
| ECharts      | 工业图表  |
| Tailwind CSS | 布局辅助  |
| Vant         | 移动组件  |
| Capacitor    | APP封装 |

---

# 4. 工程目录设计

最终：

```text
src/web


├── apps

│
├── pc

│
├── pda

│
├── mobile


├── components


├── layouts


├── router


├── stores


├── api


├── hooks


├── utils


├── permissions


├── locales


├── assets


└── styles

```

---

# 5. 多端统一架构

核心原则：

> 业务逻辑复用，UI适配。

例如：

称量功能：

共享：

```text
weighing.service.ts

weighing.store.ts

weighing.model.ts
```

不同：

PC:

```text
Table
Form
```

PDA:

```text
扫码

快速录入
```

Mobile:

```text
简化操作
```

---

# 6. Monorepo设计

推荐：

pnpm workspace。

结构：

```text
frontend


├── packages

│
├── shared

│
├── api-client

│
├── ui


├── apps

│
├── pc

├── pda

└── mobile

```

---

# 7. API层设计

禁止：

组件直接调用Axios。

错误：

```typescript
axios.get(...)
```

---

正确：

```text
Page

↓

Service

↓

API Client

↓

Backend
```

---

目录：

```text
api

├── identity.ts

├── material.ts

├── workorder.ts

├── weighting.ts

├── trace.ts

├── ai.ts

```

---

示例：

```typescript
export function getWorkOrders(){

return request.get(
'/api/v1/workorders'
)

}

```

---

# 8. TypeScript规范

必须开启：

```json
{
"strict":true
}
```

---

禁止：

```typescript
any
```

除特殊情况。

---

推荐：

```typescript
interface WorkOrderDto
{

id:string;

orderNo:string;

status:string;

}

```

---

# 9. 状态管理设计

使用：

Pinia。

目录：

```text
stores


├── user.ts

├── tenant.ts

├── permission.ts

├── workorder.ts

├── equipment.ts

├── ai.ts

```

---

# 10. 用户状态模型

user store：

```typescript
{
userId,

username,

roles,

permissions,

tenantId

}
```

---

# 11. 权限设计

采用：

RBAC。

前端：

```text
Route Permission

+

Button Permission

```

---

路由：

```typescript
meta:
{

permission:

"workorder.view"

}

```

---

按钮：

```vue
<Permission
code="workorder.create">

<Button/>

</Permission>

```

---

# 12. Layout设计

统一：

```text
layouts


├── PCLayout.vue


├── MobileLayout.vue


├── PDALayout.vue


```

---

# 13. PC端设计规范

适合：

* 管理
* 配置
* 报表
* 分析

布局：

```text
Header

Sidebar

Content

Footer

```

---

# 14. PDA端设计规范

工业现场特点：

* 戴手套
* 快速操作
* 网络不稳定

设计原则：

## 大按钮

推荐：

48px以上。

---

## 少输入

优先：

* 扫码
* 选择
* 自动带入

---

典型：

称量流程：

```text
扫描任务

↓

扫描物料

↓

扫描容器

↓

称量

↓

确认

```

---

# 15. Mobile端设计规范

应用：

* 管理人员
* 主管
* 现场查询

功能：

```text
我的任务

异常通知

生产状态

审批

AI助手

```

---

# 16. 工业大屏设计

Dashboard。

技术：

ECharts。

---

目录：

```text
dashboard


├── OEE.vue

├── Production.vue

├── Energy.vue

├── Quality.vue

```

---

# 17. ECharts规范

统一封装：

```text
components/chart


├── LineChart

├── BarChart

├── GaugeChart

├── MapChart

```

---

示例：

设备OEE：

```text
Gauge

↓

86%

```

---

# 18. 实时数据设计

使用：

SignalR。

架构：

```text
Backend

 |

SignalR Hub

 |

Frontend

 |

Pinia Store

 |

Component

```

---

例如：

设备状态：

```typescript
equipmentStore.updateStatus()

```

---

# 19. 离线能力设计

工业现场网络不稳定。

支持：

```text
Local Cache

+

Sync Queue
```

---

技术：

IndexedDB。

保存：

```text
未上传称量记录

扫码记录

异常记录

```

---

# 20. 文件上传设计

统一：

```text
Upload Component
```

支持：

* SOP
* 图片
* 报告
* 批记录附件

---

# 21. AI助手前端设计

AI入口：

全平台。

布局：

```text
--------------------------------

                 AI Assistant


 用户问题

 AI回答

 数据来源

 操作建议


--------------------------------

```

---

# 22. AI Chat组件

目录：

```text
components/ai


├── ChatPanel.vue

├── Message.vue

├── AgentSelector.vue

├── SourceViewer.vue

```

---

# 23. AI流式输出

支持：

SSE。

效果：

```text
AI正在分析...

查询工单数据...

查询设备状态...

生成结果...
```

---

# 24. 工业异常提醒

支持：

Notification。

例如：

设备报警：

```text
设备Mixer01

温度异常

立即查看
```

---

# 25. 国际化设计

使用：

vue-i18n。

目录：

```text
locales


├── zh-CN.json

├── en-US.json

```

---

# 26. 主题设计

支持：

```text
Light

Dark

Industrial Dark
```

---

工业大屏：

默认：

Dark。

---

# 27. 组件规范

目录：

```text
components


├── base

├── business

├── industrial

├── ai

```

---

业务组件：

例如：

```text
WorkOrderCard

EquipmentStatus

BatchTraceGraph

```

---

# 28. 表单规范

工业系统：

大量配置。

统一：

```text
Form Schema
```

例如：

```typescript
{
field:"material",

type:"select",

options:[]
}

```

---

未来结合：

Low Code。

---

# 29. 动态页面设计

低代码支持：

```text
Page JSON

↓

Renderer

↓

Vue Component

```

---

# 30. 前端安全规范

必须：

* HTTPS
* XSS防护
* Token刷新
* 权限校验
* 文件类型检查

---

# 31. 性能规范

## 首屏

目标：

<3秒。

---

优化：

* 路由懒加载
* 组件按需加载
* 图片压缩
* 数据分页

---

# 32. 工业大数据展示优化

禁止：

一次加载：

```text
100万条数据
```

采用：

```text
分页

聚合

时间窗口

```

---

# 33. 前端测试

工具：

```text
Vitest

Playwright

Cypress

```

---

测试：

* 组件
* 页面
* 流程

---

# 34. 前端CI/CD

流程：

```text
Git Push

↓

npm install

↓

lint

↓

test

↓

build

↓

Docker

↓

Deploy

```

---

# 35. Docker部署

Nginx：

```text
Frontend Container

        |

       Nginx

        |

      Backend API
```

---

# 36. Codex开发规范

Codex生成前端：

输入：

```text
docs/frontend/*.md

+
API规范

+
数据库模型

```

输出：

```text
Vue Page

Component

Store

API

Type

```

---

# 37. Codex任务拆分

## Task01

创建Vue3基础工程

生成：

```text
Vite

Vue3

TS

Pinia

Router
```

---

## Task02

创建PC端框架

包括：

```text
Layout

Menu

Permission

```

---

## Task03

创建PDA框架

包括：

```text
扫码

任务

离线

```

---

## Task04

创建Mobile框架

包括：

```text
首页

消息

AI

```

---

## Task05

创建工业组件库

包括：

```text
Chart

Status

TraceGraph

```

---

## Task06

接入AI助手

包括：

```text
Chat

Stream

Agent

```

---

# 38. 最终前端架构

```text
Industrial Platform Frontend


                 Vue3


                  |


        Shared Business Layer


                  |


----------------------------------


 PC Web

 PDA

 Mobile

 Dashboard

 AI Assistant


```

---
