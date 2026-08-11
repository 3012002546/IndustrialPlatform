# Tailscale 新设备接入与协作指南

本文说明如何让一台新的 Windows 开发电脑安全加入 Industrial Platform 云端开发环境。macOS/Linux 的总体流程相同，命令行路径需要按操作系统调整。

## 1. 当前连接方式

当前开发环境采用以下方式：

```text
开发电脑
  │
  ├─ Git：同步受版本控制的代码和非敏感配置
  ├─ Tailscale：加入同一 Tailnet，访问云服务器私网
  ├─ OpenSSH：每台电脑使用独立私钥登录服务器
  └─ 本地私有配置：连接 PostgreSQL、Redis、RabbitMQ、Seq
```

重要说明：

- 加入 Tailnet 只代表网络可达，不代表自动获得 SSH 或数据库权限。
- 当前服务器使用普通 OpenSSH 密钥认证，没有启用 Tailscale SSH。
- 服务器已关闭 SSH 密码认证，新设备必须先添加自己的 SSH 公钥。
- PostgreSQL 等基础设施端口只绑定 Tailnet，不允许通过公网访问。
- 每位开发者、每台设备使用独立身份，不共享 Tailscale 账号或 SSH 私钥。

## 2. 接入前需要准备

### 2.1 新设备使用者需要

- 自己的 Tailscale 登录账号。
- 项目 Git 仓库访问权限。
- Windows OpenSSH Client。
- 一名当前仍能登录服务器的管理员协助添加新公钥。
- 用于接收数据库等开发凭据的密码管理器或其他安全渠道。

检查 Windows OpenSSH Client：

```powershell
Get-Command ssh
Get-Command ssh-keygen
```

如果命令不存在，在 Windows“设置 → 系统 → 可选功能”中安装 OpenSSH Client。

### 2.2 Tailnet 管理员需要

- 在 Tailscale 管理后台邀请新成员。
- 在启用设备审批时批准新设备。
- 确认访问控制允许该成员访问开发服务器所需端口。
- 保持一台已验证的管理员电脑和 SSH 会话可用，直到新设备验证完成。

官方资料：

- 安装 Tailscale：<https://tailscale.com/kb/1017/install>
- 设备审批：<https://tailscale.com/kb/1099/device-approval>
- MagicDNS：<https://tailscale.com/kb/1054/dns>
- 访问控制：<https://tailscale.com/kb/1337/acl-syntax>

## 3. 管理员邀请开发者加入 Tailnet

如果新设备属于同一个人，也应使用该人员自己的正常账号登录；不要共享管理员登录状态。

管理员操作：

1. 登录 Tailscale 管理后台。
2. 打开用户或成员管理页面。
3. 邀请开发者自己的账号加入当前 Tailnet。
4. 只授予开发所需权限，不要默认授予 Tailnet 管理员权限。
5. 如果使用用户组和设备标签，将开发者加入开发组，并保持云服务器的开发基础设施标签不变。

推荐的访问边界：

- 开发者可以访问开发服务器的 SSH 和开发基础设施端口。
- 开发者不能访问不相关服务器或管理网络。
- PostgreSQL、Redis、RabbitMQ、Seq 不开放公网防火墙规则。
- 访问策略变更应在 Tailscale 管理后台审阅和记录。

## 4. 新设备安装并登录 Tailscale

### 4.1 安装

从 Tailscale 官方网站下载安装客户端：

<https://tailscale.com/download/windows>

安装完成后，用被邀请的个人账号登录。

### 4.2 等待设备批准

如果 Tailnet 开启了设备审批，新设备可能显示已登录但无法访问其他设备。此时管理员需要：

1. 在管理后台找到待批准设备。
2. 核对设备名称、操作系统和登录用户。
3. 批准设备。
4. 确认设备没有被错误设置为出口节点、子网路由器或服务器标签。

普通开发电脑通常不需要发布子网路由，也不需要成为出口节点。

### 4.3 检查本机状态

在新设备 PowerShell 中运行：

```powershell
tailscale status
```

确认：

- 本机状态为在线。
- 能看到开发服务器设备。
- 开发服务器显示预期的 MagicDNS 名称。

不要把 `tailscale status` 的完整输出粘贴到公开 Issue、聊天记录或 Git 文件，因为它可能包含内部设备名称和地址。

## 5. 使用 MagicDNS 验证 Tailnet 网络

从 Tailscale 管理后台或现有管理员处获取开发服务器的 MagicDNS 名称。下文统一使用占位符：

```text
<服务器MagicDNS名称>
```

验证 Tailscale 路径：

```powershell
tailscale ping <服务器MagicDNS名称>
```

再验证名称解析：

```powershell
Resolve-DnsName <服务器MagicDNS名称>
```

如果 `tailscale ping` 成功而普通网络命令失败，应检查 Tailnet ACL、目标端口和服务器服务状态，不要改用公网地址绕过问题。

## 6. 为新设备创建独立 SSH 密钥

每台设备创建自己的密钥。不要从旧电脑复制私钥。

在新设备 PowerShell 中执行：

```powershell
$keyPath = Join-Path $env:USERPROFILE '.ssh/industrial-platform-dev'
ssh-keygen -t ed25519 -a 64 -f $keyPath -C 'industrial-platform-new-device'
```

建议为私钥设置口令，并使用 Windows OpenSSH Agent 管理口令。

生成结果：

- `$keyPath`：私钥，只能留在新设备。
- `$keyPath.pub`：公钥，可以交给管理员。

查看公钥指纹：

```powershell
ssh-keygen -lf "$keyPath.pub"
```

只发送 `.pub` 文件。不要发送没有 `.pub` 后缀的文件，也不要发送以 `PRIVATE KEY` 开头的内容。

## 7. 管理员把新公钥加入服务器

此步骤必须在现有可登录的管理员电脑上操作。不要关闭原有 SSH 会话，直到新设备成功登录。

### 7.1 核对收到的是公钥

公钥通常是一行文本，以 `ssh-ed25519` 开头。管理员应再次检查指纹：

```powershell
ssh-keygen -lf '<新设备公钥文件>.pub'
```

让新设备使用者通过另一个可信渠道确认指纹一致。

### 7.2 安全追加公钥

管理员使用现有连接将公钥复制到服务器临时目录，再追加到目标用户的 `authorized_keys`。命令中的值均为占位符：

```powershell
scp '<新设备公钥文件>.pub' `
  '<服务器用户>@<服务器MagicDNS名称>:/tmp/industrial-platform-new-device.pub'

ssh '<服务器用户>@<服务器MagicDNS名称>'
```

进入服务器后执行：

```bash
set -eu
target_user='<服务器用户>'
target_home="$(getent passwd "$target_user" | cut -d: -f6)"
target_group="$(id -gn "$target_user")"

sudo install -d -o "$target_user" -g "$target_group" -m 0700 "$target_home/.ssh"
sudo touch "$target_home/.ssh/authorized_keys"
sudo chown "$target_user:$target_group" "$target_home/.ssh/authorized_keys"
sudo chmod 0600 "$target_home/.ssh/authorized_keys"

if ! sudo grep -Fqx -f /tmp/industrial-platform-new-device.pub "$target_home/.ssh/authorized_keys"; then
  sudo sh -c "cat /tmp/industrial-platform-new-device.pub >> '$target_home/.ssh/authorized_keys'"
fi

sudo rm -f /tmp/industrial-platform-new-device.pub
```

不要使用覆盖写入方式替换整个 `authorized_keys`，否则可能删除现有管理员公钥并导致失联。

## 8. 核对服务器 SSH 主机指纹

新设备第一次连接前，应通过现有管理员会话取得服务器主机指纹：

```bash
sudo ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub
```

管理员通过可信渠道把指纹发给新设备使用者。

新设备获取候选主机密钥：

```powershell
$serverName = '<服务器MagicDNS名称>'
$candidateKnownHosts = Join-Path $env:TEMP 'industrial-platform-known-hosts'
ssh-keyscan -t ed25519 $serverName 2>$null | Set-Content -Encoding ascii $candidateKnownHosts
ssh-keygen -lf $candidateKnownHosts
```

人工比较指纹。只有完全一致时，才把记录保存到项目已忽略目录：

```powershell
$repositoryRoot = '<IndustrialPlatform仓库绝对路径>'
$projectSshDirectory = Join-Path $repositoryRoot 'src/backend/.ssh'
New-Item -ItemType Directory -Force $projectSshDirectory | Out-Null
Copy-Item $candidateKnownHosts (Join-Path $projectSshDirectory 'known_hosts')
Remove-Item $candidateKnownHosts
```

`ssh-keyscan` 本身不能证明目标服务器可信，必须通过管理员提供的独立指纹进行比较。

## 9. 从新设备验证 SSH 登录

保持旧设备管理员会话在线，在新设备运行：

```powershell
$repositoryRoot = '<IndustrialPlatform仓库绝对路径>'
$keyPath = Join-Path $env:USERPROFILE '.ssh/industrial-platform-dev'
$knownHosts = Join-Path $repositoryRoot 'src/backend/.ssh/known_hosts'

ssh `
  -i $keyPath `
  -o BatchMode=yes `
  -o StrictHostKeyChecking=yes `
  -o "UserKnownHostsFile=$knownHosts" `
  '<服务器用户>@<服务器MagicDNS名称>'
```

成功进入服务器后执行只读检查：

```bash
whoami
sudo -n true && echo sudo-ready
sudo docker info >/dev/null && echo docker-ready
```

验证成功后退出：

```bash
exit
```

如果新设备登录失败，不要删除旧设备公钥，也不要重启 SSH。先检查用户名、公钥是否追加正确、文件权限以及 Tailnet ACL。

## 10. 获取项目代码

在新设备使用自己的 Git 凭据克隆仓库：

```powershell
git clone <项目仓库地址>
Set-Location '<IndustrialPlatform仓库路径>'
git status
```

确认以下私有路径继续被忽略：

```powershell
git check-ignore -v `
  'src/backend/appsettings.Development.local.json' `
  'src/backend/.ssh/known_hosts'
```

不要把新设备的 SSH 私钥放进仓库。项目的 `src/backend/.ssh/` 只应保存本地使用且已忽略的项目相关文件。

## 11. 创建新设备的本地私有配置

从受版本控制的示例创建：

```powershell
Copy-Item `
  'src/backend/appsettings.Development.local.example.json' `
  'src/backend/appsettings.Development.local.json'
```

通过密码管理器或其他安全渠道取得真实值，然后填写本地文件：

```json
{
  "RemoteDevelopment": {
    "Enabled": true,
    "Host": "<服务器MagicDNS名称>",
    "Ssh": {
      "Host": "<服务器MagicDNS名称>",
      "Port": 22,
      "UserName": "<服务器用户>",
      "IdentityFile": "<新设备私钥路径>"
    },
    "PostgreSql": {
      "Port": 5432,
      "UserName": "<数据库用户>",
      "Password": "<数据库密码>",
      "IdentityDatabase": "identity_db",
      "ReferenceDataDatabase": "industrial_platform"
    }
  }
}
```

实际文件应保留示例中的 Redis、RabbitMQ 和 Seq 节点。RabbitMQ 与 Seq 是否启用应与服务器当前 profile 一致。

再次确认文件被忽略：

```powershell
git check-ignore -v 'src/backend/appsettings.Development.local.json'
```

## 12. 验证 PostgreSQL 网络连接

从本地私有配置读取参数，避免在命令历史中硬编码地址：

```powershell
$config = Get-Content -Raw 'src/backend/appsettings.Development.local.json' | ConvertFrom-Json
$remote = $config.RemoteDevelopment

Test-NetConnection `
  -ComputerName $remote.Host `
  -Port $remote.PostgreSql.Port
```

`TcpTestSucceeded=True` 只代表 Tailnet 网络和 PostgreSQL 端口可达，不代表数据库账号认证成功。

数据库客户端使用：

| 参数 | 配置来源 |
| --- | --- |
| Host | `RemoteDevelopment.Host` |
| Port | `RemoteDevelopment.PostgreSql.Port` |
| Database | `IdentityDatabase` 或 `ReferenceDataDatabase` |
| Username | `RemoteDevelopment.PostgreSql.UserName` |
| Password | `RemoteDevelopment.PostgreSql.Password` |
| SSL mode | `Prefer` |

链路由 Tailscale 加密，通常不需要 SSH 隧道。不要通过开放公网 PostgreSQL 端口解决连接问题。

## 13. 验证云端容器状态

先完成本文第 9 节的 SSH 配置，再参考：

[`云端开发容器与PostgreSQL连接.md`](./云端开发容器与PostgreSQL连接.md)

完整开发基础设施应包含：

- PostgreSQL
- Redis
- RabbitMQ
- Seq

应用部署后，为节省 4 GB 服务器内存，通常只保留 `core`，按需开启 RabbitMQ，并默认停止 Seq。

## 14. 验证本地后端配置切换

使用 IDE 的 Development profile 启动 Identity 或 ReferenceData API：

- 本地私有文件存在且 `RemoteDevelopment.Enabled=true`：连接云端 PostgreSQL/Redis。
- 文件不存在或 `RemoteDevelopment.Enabled=false`：回退本地 SQLite。
- `RabbitMq.Enabled=true`：ReferenceData 使用云端 RabbitMQ。
- `Seq.Enabled=true`：日志发送到云端 Seq。

不要把真实连接串复制到受版本控制的 `appsettings.Development.json`。

## 15. 常见故障排查

### 15.1 Tailscale 看不到服务器

依次检查：

1. 是否使用正确的个人账号登录。
2. 是否加入正确 Tailnet。
3. 新设备是否等待管理员批准。
4. ACL 是否允许该用户访问开发服务器。
5. 设备是否过期、被禁用或被移除。

### 15.2 `tailscale ping` 成功，但 SSH 失败

检查：

1. SSH 用户名是否正确。
2. 使用的是否为新设备自己的私钥。
3. 对应公钥是否已追加到服务器 `authorized_keys`。
4. 私钥是否与公钥指纹匹配。
5. `known_hosts` 是否包含已核对的正确服务器主机密钥。
6. Tailnet ACL 是否允许 SSH 端口。

服务器已关闭密码认证；密码登录失败是预期行为。

### 15.3 SSH 成功，但 PostgreSQL 失败

检查：

1. PostgreSQL 容器是否 `running/healthy`。
2. Tailnet ACL 是否允许 PostgreSQL 端口。
3. 私有配置是否使用 MagicDNS 名称而非公网地址。
4. 数据库名、用户名和密码是否正确。
5. 本地安全软件是否阻止 Tailscale 网络。

### 15.4 更换网络后连接变慢

运行：

```powershell
tailscale ping <服务器MagicDNS名称>
tailscale netcheck
```

如果连接通过 DERP 中继，功能仍可正常使用，但延迟可能较高。不要因此开放公网服务端口。

## 16. 新设备验收清单

- [ ] 使用个人账号加入正确 Tailnet。
- [ ] 管理员已批准设备。
- [ ] 能看到并解析服务器 MagicDNS 名称。
- [ ] `tailscale ping` 成功。
- [ ] 新设备已生成独立 SSH 密钥。
- [ ] 管理员已核对并追加新公钥。
- [ ] 已通过独立渠道核对 SSH 主机指纹。
- [ ] 新设备可以使用密钥登录服务器。
- [ ] `sudo -n true` 与只读 Docker 检查成功。
- [ ] 已从示例创建被忽略的本地私有配置。
- [ ] PostgreSQL TCP 连接检查成功。
- [ ] IDE Development profile 能读取预期配置。
- [ ] `git status` 不包含私有配置、私钥或 known-hosts 文件。

## 17. 设备丢失、离职或不再使用

管理员应及时执行：

1. 在 Tailscale 管理后台禁用或移除该设备。
2. 从服务器 `authorized_keys` 删除该设备对应的单独公钥行。
3. 撤销该用户不再需要的 Tailnet、Git 和密码管理器权限。
4. 如果凭据曾泄漏，轮换数据库、Redis、RabbitMQ、Seq 等密码。
5. 不要删除其他设备的公钥。

由于每台设备使用独立密钥，撤销单台设备不会影响其他开发者。
