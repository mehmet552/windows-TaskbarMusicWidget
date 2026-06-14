<# :
@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Command -ScriptBlock ([ScriptBlock]::Create((Get-Content -LiteralPath '%~f0' -Raw)))"
exit /b
#>

$ErrorActionPreference = "Stop"

$appName = "TaskbarMusicWidget"
$projectDir = Join-Path (Get-Location) "TaskbarMusicWidget"

Write-Host ">>> Eski surum (varsa) kapatiliyor..." -ForegroundColor Cyan
Stop-Process -Name $appName -Force -ErrorAction SilentlyContinue

Write-Host ">>> Proje derleniyor..." -ForegroundColor Cyan
Set-Location $projectDir
dotnet build -c Debug

Write-Host ">>> Uygulama baslatiliyor..." -ForegroundColor Green
$exePath = Join-Path $projectDir "bin\Debug\net10.0-windows10.0.19041.0\$appName.exe"
Start-Process -FilePath $exePath

Write-Host ">>> Baslatma basarili. Pencere kapatilabilir." -ForegroundColor Green
Start-Sleep -Seconds 2
