# Windows Electron 客户端

这是 PF-06A 的 Windows 外壳边界。它只暴露白名单 Runtime bridge：能力快照、OS 安全存储、设备快照、文件/诊断/系统打印/通知端口、屏幕源查询和受信更新入口；renderer 不获得 `ipcRenderer`、`shell`、文件系统或任意 IPC channel。

开发前先生成前端资源：

```powershell
Set-Location ..\..\frontend
pnpm build
Set-Location ..\electron
pnpm install --frozen-lockfile
pnpm build
```

`pnpm start` 仅用于开发外壳检查。正式包需把受控的 `runtime-policy.json` 和不可变、Ed25519 签名的 `runtime-manifest.json` 放入 `resources/`；清单的 artifact URL 必须命中策略中的 HTTPS origin。Web Bundle 会下载、SHA-256 校验、解压检查、写入私有 slot 并在重新加载前切换；Windows native 包会在 hash 验证后打开系统安装器并退出旧实例。发布域名、公钥、Windows/Android 签名身份仍必须由发布环境注入，示例策略不能直接发布。
