param(
    [string]$ProjectRoot = (Get-Location).Path,
    [int]$Port = 56365
)

$ErrorActionPreference = "Stop"
$hostProject = Join-Path $ProjectRoot "src/backend/src/Hosts/IndustrialPlatform.Collaboration.EmbeddedHost"
$configPath = Join-Path $hostProject "appsettings.example.json"
$dllPath = Join-Path $hostProject "bin/Release/net10.0/IndustrialPlatform.Collaboration.EmbeddedHost.dll"
$dbPath = Join-Path ([System.IO.Path]::GetTempPath()) ("industrial-platform-fixed-demo-" + $PID + ".db")
$dbConfigPath = $dbPath.Replace('\', '/')
$config = Get-Content $configPath -Raw | ConvertFrom-Json

function Set-ConfigEnvironment([object]$value, [string]$prefix) {
    if ($null -eq $value) { return }
    if ($value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $value.PSObject.Properties) {
            Set-ConfigEnvironment $property.Value ("{0}__{1}" -f $prefix, $property.Name)
        }
        return
    }
    if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        $index = 0
        foreach ($item in $value) {
            Set-ConfigEnvironment $item ("{0}__{1}" -f $prefix, $index)
            $index++
        }
        return
    }
    Set-Item "Env:$prefix" ([string]$value)
}

foreach ($property in $config.PSObject.Properties) {
    Set-ConfigEnvironment $property.Value $property.Name
}
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:SqlSugar__ConnectionString = "Data Source=$dbConfigPath"
$env:DatabaseTopology__SharedSqliteFile = $dbConfigPath
$env:EmbeddedCollaboration__AllowedParentOrigins__0 = "http://localhost:$Port"
$env:HostOptions__BackgroundServiceExceptionBehavior = "Ignore"
$env:Logging__EventLog__LogLevel__Default = "None"

Remove-Item -LiteralPath $dbPath -Force -ErrorAction SilentlyContinue
$process = $null
try {
    $process = Start-Process dotnet -WorkingDirectory $ProjectRoot -ArgumentList @(
        "`"$dllPath`"",
        "--urls",
        "http://localhost:$Port"
    ) -PassThru

    $ready = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        Start-Sleep -Milliseconds 500
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $ready = $client.ConnectAsync("127.0.0.1", $Port).Wait(1000)
        }
        finally {
            $client.Dispose()
        }
        if ($ready) { break }
    }
    if (-not $ready) { throw "FixedDemo 未在验证窗口内监听端口。" }

    $demoResponse = Invoke-WebRequest -Uri "http://localhost:$Port/embedded/demo/session" -Method Post -TimeoutSec 20
    $cookieHeader = (($demoResponse.Headers["Set-Cookie"] | ForEach-Object { ($_ -split ";", 2)[0] }) -join "; ")
    $headers = @{ Cookie = $cookieHeader }
    $demo = $demoResponse.Content | ConvertFrom-Json

    $users = Invoke-RestMethod -Uri "http://localhost:$Port/collaboration/api/v1/users?keyword=系统" -Headers $headers -TimeoutSec 20
    $userItems = if ($users.data) { $users.data.items } else { $users.items }
    if (@($userItems).Count -lt 2) { throw "FixedDemo 目录未返回 B/C。" }
    $peer = @($userItems | Where-Object { $_.displayName -eq "系统 B" })[0]
    if (-not $peer) { $peer = @($userItems)[0] }

    $conversation = Invoke-RestMethod `
        -Uri "http://localhost:$Port/collaboration/api/v1/conversations" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{ peerUserNId = $peer.userNId } | ConvertTo-Json) `
        -Headers $headers `
        -TimeoutSec 20
    $conversationData = if ($conversation.data) { $conversation.data } else { $conversation }

    $message = Invoke-RestMethod `
        -Uri "http://localhost:$Port/collaboration/api/v1/conversations/$($conversationData.conversationNId)/messages" `
        -Method Post `
        -ContentType "application/json" `
        -Body (@{ clientMessageNId = "fixed-demo-1"; messageType = "Text"; textContent = "来自系统 A 的演示消息" } | ConvertTo-Json) `
        -Headers $headers `
        -TimeoutSec 20
    $history = Invoke-RestMethod `
        -Uri "http://localhost:$Port/collaboration/api/v1/conversations/$($conversationData.conversationNId)/messages" `
        -Headers $headers `
        -TimeoutSec 20
    $historyData = if ($history.data) { $history.data } else { $history }

    [pscustomobject]@{
        DemoUser = $demo.currentUser
        DirectoryUsers = @($userItems | ForEach-Object displayName)
        ConversationNId = $conversationData.conversationNId
        SentMessageNId = if ($message.data) { $message.data.messageNId } else { $message.messageNId }
        ReadBackCount = @($historyData.items).Count
        Port = $Port
        DatabasePath = $dbPath
    } | ConvertTo-Json -Depth 5
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000)
    }
    Remove-Item -LiteralPath $dbPath -Force -ErrorAction SilentlyContinue
}
