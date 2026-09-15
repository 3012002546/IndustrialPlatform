# EmbeddedHost 源会话校验收口

2026-09-14，总控接手最后的小范围收口；开发和验收均已确认停止对应源码写入与测试。保留其他已验收入口及前端结果，不重复媒体矩阵或环境检查。

根因：配置键是 cookie 索引，不能作为显式 SessionNId 的替代；仅按 SessionNId 取第一条再检查身份，也会被前置无关记录遮挡。当前实现将显式 SessionNId、source、tenant、subject、安全版本和活动状态放入同一个匹配条件。

测试修正：原多场景复用中间件已撤销的 token，后续拒绝断言无法证明各场景生效。开发改为无撤销副作用的 mapper 校验，各边界使用独立会话标识；总控补充有效记录先通过、清空源记录后拒绝，避免用从未存在的记录冒充删除验证。覆盖键与字段不同、会话轮换、字段缺失、停用、删除、Enabled=false、四项身份/版本不匹配、前置无关同ID记录及后置合法记录。

本次新鲜验证（同一稳定工作树，命令均 exit 0）：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release --no-restore --verbosity quiet
dotnet test tests/Collaboration/IndustrialPlatform.Collaboration.Tests/IndustrialPlatform.Collaboration.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~Security_EmbeddedHost' --logger 'console;verbosity=normal'
```

Release 构建：0 警告、0 错误。定向握手和适配测试：20/20 通过。测试项目在 solution 内，sample 为其项目引用；测试使用本次新鲜构建产物。未运行前端或完整媒体回归；本结果不是实际 MES 接线或浏览器联调证明。

稳定文件 SHA256：

- `src/samples/Collaboration.EmbeddedHost/EmbeddedHandshake.cs`：`818BA73D0530A44F55ED8D96869BFAE7A66DEB90E59A205EBFCF42D606E2C90C`
- `tests/Collaboration/IndustrialPlatform.Collaboration.Tests/Security_EmbeddedHostHandshakeTests.cs`：`66DB0EBA496082ECD94B25B4F515BB46A3F07C18C3FBB86A114D49AA48493158`

真实 MES 当前用户/目录接口由用户后续按入口和中文说明补接；SQL Server、真实部署网络及终端专项按用户安排后置。未提交、部署或调整用户调试进程。
