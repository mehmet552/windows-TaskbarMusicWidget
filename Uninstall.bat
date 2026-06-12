<# :
@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Command -ScriptBlock ([ScriptBlock]::Create((Get-Content -LiteralPath '%~f0' -Raw)))"
pause
exit /b
#>

$appName = "TaskbarMusicWidget"
$installDir = Join-Path $env:LOCALAPPDATA $appName
$startupFolder = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$shortcutPath = Join-Path $startupFolder "$appName.lnk"

Write-Host ">>> Uygulama kapatiliyor..." -ForegroundColor Cyan
Stop-Process -Name $appName -Force -ErrorAction SilentlyContinue

Write-Host ">>> Baslangic (Startup) kisayolu siliniyor..." -ForegroundColor Cyan
if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
}

Write-Host ">>> Uygulama dosyalari siliniyor ($installDir)..." -ForegroundColor Cyan
if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}

Write-Host ">>> Kaldirma islemi basariyla tamamlandi!" -ForegroundColor Green
