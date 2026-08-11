# 云端开发容器与 PostgreSQL 连接

本文用于在 Windows 本地管理云端开发基础设施，以及配置本地后端连接云端 PostgreSQL。

## 1. 安全约束

- 私有配置：`src/backend/appsettings.Development.local.json`
- SSH 私钥目录：`src/backend/.ssh/`
- 上述路径均已被 Git 忽略，禁止提交服务器地址、密码或私钥。
- PostgreSQL、Redis、RabbitMQ、Seq 只绑定 Tailnet，不要改为公网监听。
- 服务器只运行发布镜像，不在服务器上构建应用镜像。

## 2. 在本地 PowerShell 初始化 SSH 参数

在仓库根目录 `IndustrialPlatform` 打开 PowerShell，执行以下代码。代码只把配置保存在当前 PowerShell 进程中，不会打印凭据：

```powershell
$configPath = 'src/backend/appsettings.Development.local.json'
$config = Get-Content -Raw $configPath | ConvertFrom-Json
$sshConfig = $config.RemoteDevelopment.Ssh

$backendRoot = (Resolve-Path 'src/backend').Path
$identityFile = if ([IO.Path]::IsPathRooted($sshConfig.IdentityFile)) {
    $sshConfig.IdentityFile
} else {
    Join-Path $backendRoot $sshConfig.IdentityFile
}

$knownHosts = Join-Path $backendRoot '.ssh/known_hosts'
$sshTarget = "$($sshConfig.UserName)@$($sshConfig.Host)"
$sshArguments = @(
    '-i', $identityFile,
    '-p', $sshConfig.Port,
    '-o', 'BatchMode=yes',
    '-o', 'StrictHostKeyChecking=yes',
    '-o', "UserKnownHostsFile=$knownHosts",
    '-o', 'LogLevel=ERROR'
)

function Invoke-CloudInfrastructure([string] $Command) {
    & ssh @sshArguments $sshTarget $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Cloud infrastructure command failed with exit code $LASTEXITCODE."
    }
}
```

全新电脑若没有 `known_hosts` 记录，应先由管理员核对服务器 SSH 主机指纹，再写入 `src/backend/.ssh/known_hosts`；不要关闭主机密钥检查。

## 3. 检查容器状态

以下命令只显示容器名称、运行状态和健康状态，不显示服务器地址或端口：

```powershell
$statusCommand = @'
set -eu
for container in industrial-platform-postgres industrial-platform-redis industrial-platform-rabbitmq industrial-platform-seq
do
  state=$(sudo docker inspect --format={{.State.Status}} "$container")
  if [ "$state" = "running" ]; then
    health=$(sudo docker inspect --format={{.State.Health.Status}} "$container")
  else
    health=not-running
  fi
  echo "$container state=$state health=$health"
done
'@

Invoke-CloudInfrastructure $statusCommand
```

正常的完整启动结果应为四个容器全部 `state=running health=healthy`。

## 4. 启动容器

当前尚未部署应用时，可以启动全部开发基础设施：

```powershell
Invoke-CloudInfrastructure 'sudo /opt/industrial-platform-dev/manage.sh start all'
```

按需启动：

```powershell
# PostgreSQL + Redis
Invoke-CloudInfrastructure 'sudo /opt/industrial-platform-dev/manage.sh start core'

# PostgreSQL + Redis + RabbitMQ
Invoke-CloudInfrastructure 'sudo /opt/industrial-platform-dev/manage.sh start messaging'

# PostgreSQL + Redis + Seq
Invoke-CloudInfrastructure 'sudo /opt/industrial-platform-dev/manage.sh start observability'
```

服务器内存为 4 GB。应用部署后建议默认只保留 `core`，按需开启 RabbitMQ，并默认停止 Seq。

## 5. 停止容器

停止全部基础设施容器但保留数据库和其他数据卷：

```powershell
Invoke-CloudInfrastructure 'sudo /opt/industrial-platform-dev/manage.sh stop'
```

该命令不会删除 Docker volume。不要执行 `docker compose down -v`，除非明确要永久删除开发数据。

## 6. 本地后端连接云端 PostgreSQL

推荐让应用通过已经接线的私有配置自动切换，不要把 PostgreSQL 连接串写入受版本控制的 `appsettings.Development.json`。

首次配置：

```powershell
Copy-Item `
  'src/backend/appsettings.Development.local.example.json' `
  'src/backend/appsettings.Development.local.json'
```

然后只在被 Git 忽略的 `appsettings.Development.local.json` 中填写真实值：

```json
{
  "RemoteDevelopment": {
    "Enabled": true,
    "Host": "<Tailnet 主机名或地址>",
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

实际文件还应保留示例中的 Redis、RabbitMQ 和 Seq 节点。不要把占位符当作真实值。

启动 Identity 或 ReferenceData API 时：

- 私有文件存在且 `RemoteDevelopment.Enabled=true`：使用云端 PostgreSQL 和 Redis。
- 私有文件不存在或 `RemoteDevelopment.Enabled=false`：自动使用本地 SQLite。
- `RabbitMq.Enabled` 与 `Seq.Enabled` 分别控制可选依赖；不需要时保持 `false`。

项目的 `launchSettings.json` 已设置私有配置文件路径，使用 IDE 的 Development profile 启动时无需另外传入连接串。

## 7. 使用数据库客户端连接

DBeaver、DataGrip、pgAdmin 或 `psql` 使用以下参数：

| 参数 | 值 |
| --- | --- |
| Host | 私有配置中的 `RemoteDevelopment.Host` |
| Port | 私有配置中的 `PostgreSql.Port` |
| Database | Identity 使用 `IdentityDatabase`；ReferenceData 使用 `ReferenceDataDatabase` |
| Username | 私有配置中的 `PostgreSql.UserName` |
| Password | 私有配置中的 `PostgreSql.Password` |
| SSL mode | `Prefer`；传输链路已由 Tailscale 加密 |

本机必须已登录同一 Tailnet。当前 PostgreSQL 端口直接绑定 Tailnet，因此通常不需要 SSH 隧道。连接失败时依次检查：

1. 本机 Tailscale 是否在线。
2. `Invoke-CloudInfrastructure $statusCommand` 中 PostgreSQL 是否为 `healthy`。
3. 私有配置中的 Host、Port、数据库名和用户名是否正确。
4. 云服务器端口是否仍只绑定 Tailnet；不要通过开放公网端口解决连接问题。

可以在不打印密码的情况下检查 PostgreSQL 端口连通性：

```powershell
$remote = $config.RemoteDevelopment
Test-NetConnection -ComputerName $remote.Host -Port $remote.PostgreSql.Port
```

`TcpTestSucceeded=True` 只表示网络端口可达；数据库账号、密码和数据库名仍需由实际连接验证。
