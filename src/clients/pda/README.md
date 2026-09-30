# Android 手机客户端

这是 PF-06A 的 Capacitor Android 外壳边界。首轮只验真实 Android 手机相机扫码；PDA 厂商广播、BLE 和持续后台媒体不在首轮假设支持范围内。`IndustrialRuntime` 已提供 Keystore auth、能力快照、Ed25519 清单/HTTPS 下载/hash 校验、私有 Web Bundle slot 切换和 APK 系统安装入口；两者都必须经过真实签名/设备安装验收。

```powershell
Set-Location ..\..\frontend
pnpm build
Set-Location ..\pda
pnpm install --frozen-lockfile
pnpm cap:add
pnpm cap:sync
pnpm cap:build
```

构建前必须实际固定 Android SDK、JDK、Gradle/AGP、targetSdk、applicationId、versionCode 和签名身份。Keystore/签名文件不进入仓库；没有真实设备或签名输入时，安装/升级/相机验收记录为 `BLOCKED/NOT RUN`。
