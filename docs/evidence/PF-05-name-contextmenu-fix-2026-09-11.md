# PF05 会话人员名称与正文右键：主控局部修复

2026-09-11，用户要求小改动由当前主控直接完成；没有另派开发、改模型、提交或操作数据库。

## 根因与改动

- R14 列表投影使用 ConversationMember.DisplayNameSnapshot，旧数据可能保存 USR 标识；实时消息使用 Identity 人员名称，两处不一致。目录显示统一为 `Name (NormalizedLoginName)`，例如 `测试 (TEST)`；列表、创建会话和详情使用当前目录投影，历史记录/快照不改写。
- UnifiedHost 的 InProcessIdentityDirectory 通过 Identity 自有受控 QueryUsersAsync 按当前页人员 ID 批量读取名称，保留服务端租户/删除范围。空页无查询，不为正常组合宿主逐会话查人。HTTP 目录使用同样显示格式，并沿现有单人目录端点兼容，没有增加公开管理权限或批量 HTTP 接口。
- 右键原绑定于已经没有可见按钮的空 message-actions 区，点击正文不会触发。将右键和键盘菜单绑定到整个消息 article，仅允许本人非撤回消息；仍不显示“更多”，原外点击关闭、Escape、权限与视口定位保持。标题下移除重复技术人员 ID，详情中的技术 ID 仍保留。

## 验证

- 新鲜 Release solution build：exit 0、0 warning、0 error。
- Collaboration 全项目：85 passed；新增用例验证旧成员 ID 快照不会覆盖当前目录名称。
- Identity CollaborationDirectory 定向：4 passed。
- UnifiedHost 用户查询路由及真实登录定向：2 passed；实际 Identity 数据库查询验证批量名称与单人目录一致、含 `(ADMIN)`，不存在人员不返回，跨租户为空。
- CollaborationChat 组件：17 passed；新增正文 contextmenu 触发断言、菜单 visibility 可见以及外部按下关闭。
- 前端 vue-tsc、两个改动文件 ESLint 通过；Prettier 差异已格式化；修改文件 git diff --check 通过。
- 结束前现有 `http://localhost:5041/health` 与 `http://localhost:5173` 均为 200；未停止/重启用户调试或云 Docker，没有清理进程。

本轮没有声称重新通过真实浏览器矩阵。前端需加载修改，后端名称投影需用户调试实例加载新程序集；Release 测试成功不证明已有 Debug 进程已更新。原 PF05 全范围门禁不因本修复关闭。
