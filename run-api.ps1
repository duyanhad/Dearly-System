# Chạy API cho dev: đọc .env (nếu có) rồi start DrinkWater.Api
# Dùng:  .\run-api.ps1            (mặc định http://127.0.0.1:5157)
#        .\run-api.ps1 -Port 6000
param([int]$Port = 5157)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $root '.env'

if (Test-Path $envFile) {
  Get-Content $envFile -Encoding UTF8 | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith('#')) { return }
    $i = $line.IndexOf('=')
    if ($i -lt 1) { return }
    $name = $line.Substring(0, $i).Trim()
    $value = $line.Substring($i + 1).Trim()
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
  }
  Write-Host "Đã nạp .env ($envFile)"
}

if (-not $env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS = "http://0.0.0.0:$Port" }
Set-Location (Join-Path $root 'src\DrinkWater.Api')
dotnet run --no-launch-profile
