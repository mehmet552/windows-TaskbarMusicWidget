<# :
@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Command -ScriptBlock ([ScriptBlock]::Create((Get-Content -LiteralPath '%~f0' -Raw)))"
pause
exit /b
#>

$ErrorActionPreference = "Stop"

$appName = "TaskbarMusicWidget"
$installDir = Join-Path $env:LOCALAPPDATA $appName
$startupFolder = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$shortcutPath = Join-Path $startupFolder "$appName.lnk"
$exePath = Join-Path $installDir "$appName.exe"
$projectDir = Join-Path (Get-Location) "TaskbarMusicWidget"

Write-Host ">>> Eski surum kapatiliyor..." -ForegroundColor Cyan
Stop-Process -Name $appName -Force -ErrorAction SilentlyContinue

Write-Host ">>> Proje derleniyor ve yayinlaniyor (Release Modu)..." -ForegroundColor Cyan
Set-Location $projectDir
dotnet publish -c Release -o $installDir

Write-Host ">>> Baslangic (Startup) klasorune kisayol olusturuluyor..." -ForegroundColor Cyan
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.WorkingDirectory = $installDir
$Shortcut.Description = "Taskbar Music Widget"
$Shortcut.Save()

Write-Host ">>> Kurulum tamamlandi! Uygulama baslatiliyor..." -ForegroundColor Green
Start-Process -FilePath $exePath -WorkingDirectory $installDir
