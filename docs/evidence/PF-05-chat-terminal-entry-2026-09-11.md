# PF05 消息展示与 PDA / Mobile 聊天入口小修

## 本次范围

- 共用 CollaborationChat 消息气泡移除重复发送者姓名/账号，保留消息内容、时间、已读状态及右键操作。页面、顶部弹窗、PDA、Mobile 共用该组件。
- PDA / Mobile 首页 TerminalFeatureMenu 增加聊天入口，分别指向 pda-collaboration-chat / mobile-collaboration-chat，仅 collaboration.messaging.read 授权后展示。
- 仅有聊天权限时不显示业务空状态；文件分组和文件入口补上原本遗漏的文件权限判断，避免聊天入口使无权限文件功能露出。
- 两端菜单入库定义已存在：collaboration.nav.pda-chat、collaboration.nav.mobile-chat，以及各自的 page resource。沿用 SystemData collaboration.baseline / collaboration.navigation 1.1.0 种子，无需新增重复种子或改写已应用校验和。

## 验证

- 新鲜整套后端 Release build：退出码 0，0 警告、0 错误。
- SystemDataBaselineSeedRunnerTests：9 / 9 通过。真实 SQLite 旧库升级测试增加 PDA / Mobile 运行时菜单断言：聊天名称、各自路由、聊天读取权限及 PC 菜单隔离；旧发布快照与种子历史保留，重复执行幂等。
- CollaborationChat、PdaHomePage、MobileHomePage：3 文件 38 / 38 通过。两端首页增加仅聊天权限下入口导航、权限撤除隐藏和无文件入口验证；Mobile 覆盖英文标签。
- vue-tsc、此次前端文件 ESLint、git diff --check 均通过，修改文件已用 Prettier 格式化。
- 首次后端筛选使用了文件名而非类名，未匹配测试；已改为 SystemDataBaselineSeedRunnerTests 并确认上述 9 项实际执行通过。

## 调试边界

未停止或重启用户服务，未直接修改云数据库或本地连接配置，未提交。此次产品代码仅前端变更；数据库入库路径通过隔离 SQLite 验证，不代表直接查询过当前云数据库。运行中的前端加载新代码后即可显示新入口。
