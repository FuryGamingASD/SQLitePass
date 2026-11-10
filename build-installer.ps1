param(
    [string]$Version = "1.0.0"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building SQLitePass v$Version Installer " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Clean previous build
if (Test-Path "./publish") { Remove-Item -Recurse -Force "./publish" }
if (Test-Path "./releases") { Remove-Item -Recurse -Force "./releases" }

# 2. Publish self-contained app
Write-Host "`n[1/3] Publishing self-contained Win-x64 app..." -ForegroundColor Yellow
dotnet publish SQLitePass.UI -c Release -r win-x64 --self-contained true -o ./publish /p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# 3. Create Velopack Setup & Portable packages
Write-Host "`n[2/3] Packing installer with Velopack..." -ForegroundColor Yellow
vpk pack -u SQLitePass -v $Version -p ./publish -e SQLitePass.exe -o ./releases -r win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Host "Packaging failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n[3/3] Success! Installer files generated in ./releases/:" -ForegroundColor Green
Get-ChildItem "./releases" | Select-Object Name, @{Name="Size (MB)";Expression={[math]::Round($_.Length / 1MB, 2)}}
Write-Host "`nTo publish an update: upload all files in ./releases to GitHub Releases on tag v$Version" -ForegroundColor Cyan
