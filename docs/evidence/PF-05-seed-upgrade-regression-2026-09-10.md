# PF05 旧种子升级导致 UnifiedHost 启动失败：局部修复证据

日期：2026-09-10。按用户要求由当前主控直接修复；保留其他 PF05 未提交改动。

## 根因与修复

- `SystemDataBaselineSeedRunner` 新增 4 个协作合规页面资源及导航，但 `collaboration.baseline`、`collaboration.navigation` 仍沿用 `1.0.0`。已有账本使 Apply 跳过，Inspect 又要求新增资源与导航，因此初始化最终返回“SystemData 控制面种子或引导事实尚未完成”。
- 将这两个种子的共同版本升级为 `1.1.0`，复用现有增量合并与发布逻辑；不改写 `1.0.0` 账本，不放宽 readiness。
- 新增导航的草稿/发布名称改为对应资源名称，避免 4 个管理入口均显示“聊天”。

## 回归证据

- 新增 `Real_sql_store_upgrades_applied_chat_only_seeds_without_rewriting_history`：在真实 SQLite 控制面存储中写入旧版 3 个聊天资源、两个已执行的 `1.0.0` 种子账本、自定义菜单和已发布快照；关闭再打开数据库，执行 Inspect → Apply → Verify。
- 修复前：该用例在 Apply 后复现用户同样的“SystemData 控制面种子或引导事实尚未完成”错误。
- 修复后：全部 7 个协作资源对应的已发布入口及名称正确；两个旧账本仍存在并追加新版本；历史快照 checksum、自定义菜单保留；重复 Apply 不增加修订，Verify 为 Ready。种子测试类 9 项全部通过。
- 新鲜 `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release --verbosity quiet`：退出码 0，0 警告、0 错误。
- 随后 `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build --verbosity quiet`：退出码 0，1,795 通过、0 失败、7 跳过。分项：BuildingBlocks 168、Gateway 14、Collaboration 76、Identity 619、IntegrationTests 12（跳过 7）、SystemData 626、ReferenceData 258、UnifiedHost 22。
- 修改文件 `git diff --check` 通过。教训已补入 `src/开发注意事项-数据库拓扑与本地配置.md` §6.4.4。

## 验证边界

本轮旧库升级复现使用独立临时 SQLite 数据库，没有手工清理或改写用户云数据库，也没有停止/重启用户 IDE 调试进程。上述通过表示修复构建及自动化回归通过，不表示当前调试进程已加载新代码；重新生成并启动调试后才会执行新版本种子。此记录不替代 PF05 全范围验收结论。
