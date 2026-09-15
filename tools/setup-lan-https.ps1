#requires -Version 7.5
<#!
生成局域网调试证书；文件集中在项目父目录的 开发辅助文件/HTTPS证书，配置写入前端 .env.lan.local。
用法：pwsh -File tools/setup-lan-https.ps1 -IpAddress 10.13.49.141 -TrustCurrentUser
手机只安装导出的 industrial-platform-lan-ca.cer（公钥）；不要复制任何私钥。
不启动/停止服务，不修改防火墙。-TrustCurrentUser 只信任本次专用CA，不修改机器级信任。
!#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]] $IpAddress,
    [switch] $TrustCurrentUser
)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
$httpsDir = Join-Path (Split-Path $repoDir -Parent) '开发辅助文件/HTTPS证书'
$certDir = Join-Path $httpsDir '私钥'
$exportDir = Join-Path $httpsDir '导出'
$envPath = Join-Path $repoDir 'src/frontend/.env.lan.local'
$ips = @($IpAddress | ForEach-Object { [System.Net.IPAddress]::Parse($_) })
$rootPem = Join-Path $certDir 'ca.pem'
$rootKey = Join-Path $certDir 'ca-key.pem'
$serverPem = Join-Path $certDir 'server.pem'
$serverKey = Join-Path $certDir 'server-key.pem'
$publicCa = Join-Path $exportDir 'industrial-platform-lan-ca.cer'
$envText = if (Test-Path -LiteralPath $envPath) { [IO.File]::ReadAllText($envPath) } else { '' }
# 保留手工配置；不悄悄覆盖其他证书或代理目标。
foreach ($entry in @{ DEV_HTTPS_CERT = $serverPem; DEV_HTTPS_KEY = $serverKey }.GetEnumerator()) {
    if ($envText -match "(?m)^$($entry.Key)=(.+)$") {
        $configured = $Matches[1].Trim().Trim('"').Replace('\', '/')
        if ($configured -ne $entry.Value.Replace('\', '/')) {
            throw "$($entry.Key) 已指向其他证书，请保留现有配置或明确移除该项后再运行。"
        }
    }
}
[IO.Directory]::CreateDirectory($certDir) | Out-Null
[IO.Directory]::CreateDirectory($exportDir) | Out-Null
# 私钥目录只允许当前用户与SYSTEM访问；位于源码仓库及前端root之外。
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
}
Set-Acl -LiteralPath $certDir -AclObject $acl
$hash = [Security.Cryptography.HashAlgorithmName]::SHA256
$padding = [Security.Cryptography.RSASignaturePadding]::Pkcs1
$now = [DateTimeOffset]::UtcNow
$ca = $null
$caRsa = $null
$leaf = $null
$leafRsa = $null
try {
    if ((Test-Path $rootPem) -and (Test-Path $rootKey)) {
        $ca = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($rootPem, $rootKey)
        if ($ca.NotAfter.ToUniversalTime() -lt $now.AddDays(100).UtcDateTime) {
            throw '开发CA即将过期，请明确安排更换CA和各测试设备信任，未自动更换。'
        }
    } elseif ((Test-Path $rootPem) -or (Test-Path $rootKey)) {
        throw '开发CA文件不完整，未覆盖。请先核对 开发辅助文件/HTTPS证书/私钥。'
    } else {
        $caRsa = [Security.Cryptography.RSA]::Create(3072)
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Industrial Platform LAN Development CA', $caRsa, $hash, $padding)
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 0, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign -bor [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::CrlSign, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
        $ca = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddYears(2))
        [IO.File]::WriteAllText($rootPem, $ca.ExportCertificatePem())
        [IO.File]::WriteAllText($rootKey, $caRsa.ExportPkcs8PrivateKeyPem())
    }
    $leafRsa = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Industrial Platform LAN Debug', $leafRsa, $hash, $padding)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment, $true))
    $purposes = [Security.Cryptography.OidCollection]::new()
    $purposes.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1')) | Out-Null
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($purposes, $false))
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName('localhost')
    $san.AddIpAddress([Net.IPAddress]::Loopback)
    $san.AddIpAddress([Net.IPAddress]::IPv6Loopback)
    foreach ($ip in $ips) { $san.AddIpAddress($ip) }
    $request.CertificateExtensions.Add($san.Build())
    $serial = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $serial[0] = $serial[0] -band 0x7f
    $leaf = $request.Create($ca, $now.AddMinutes(-5), $now.AddDays(90), $serial)
    [IO.File]::WriteAllText($serverPem, $leaf.ExportCertificatePem())
    [IO.File]::WriteAllText($serverKey, $leafRsa.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllBytes($publicCa, $ca.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    foreach ($entry in @{ DEV_HTTPS_CERT = $serverPem; DEV_HTTPS_KEY = $serverKey }.GetEnumerator()) {
        $line = "$($entry.Key)=$($entry.Value.Replace('\', '/'))"
        if ($envText -match "(?m)^$($entry.Key)=.*$") {
            $envText = [regex]::Replace($envText, "(?m)^$($entry.Key)=.*$", [Text.RegularExpressions.MatchEvaluator]{ param($match) $line })
        } else { $envText = $envText.TrimEnd() + "`n$line`n" }
    }
    [IO.File]::WriteAllText($envPath, $envText.TrimStart())
    if ($TrustCurrentUser) {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new('Root', 'CurrentUser')
        try {
            $store.Open('ReadWrite')
            # 只把公钥导入信任库，不导入CA私钥。
            $publicCert = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificateFromFile($publicCa)
            try { $store.Add($publicCert) } finally { $publicCert.Dispose() }
        } finally { $store.Dispose() }
    }
    Write-Output "HTTPS配置完成；服务器证书有效至 $($leaf.NotAfter.ToString('yyyy-MM-dd'))"
    Write-Output "手机安装此CA公钥：$publicCa"
    Write-Output "CA SHA256：$($ca.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256))"
    Write-Output "启动前端：pnpm dev:lan:https；手机地址：https://$($ips[0]):5173/mobile"
} finally {
    if ($leaf) { $leaf.Dispose() }; if ($leafRsa) { $leafRsa.Dispose() }
    if ($ca) { $ca.Dispose() }; if ($caRsa) { $caRsa.Dispose() }
}
