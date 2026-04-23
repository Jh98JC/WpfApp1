param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$ReleaseNotes = "- Bug fixes and improvements"
)

Write-Host "=== WpfApp2 Release Build & Deploy ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Green
Write-Host ""

$publishDir = "publish\v$Version"
$zipFile = "publish\WpfApp2-v$Version.zip"
$updateXml = "updates\update.xml"

# 1. Update version in update.xml
Write-Host "[1/5] Updating update.xml..." -ForegroundColor Yellow
$xmlContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$Version.0</version>
  <url>https://github.com/Jh98JC/WpfApp1/releases/download/v$Version/WpfApp2-v$Version.zip</url>
  <changelog>https://github.com/Jh98JC/WpfApp1/releases/tag/v$Version</changelog>
  <mandatory>true</mandatory>
</item>
"@
[IO.File]::WriteAllText($updateXml, $xmlContent)
Write-Host "  OK update.xml updated to v$Version" -ForegroundColor Green

# 2. Release Build
Write-Host "[2/5] Building Release..." -ForegroundColor Yellow
dotnet publish WpfApp2\WpfApp2.csproj -c Release -r win-x64 --self-contained false -o $publishDir -p:PublishSingleFile=false -p:PublishReadyToRun=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "  X Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "  OK Build complete: $publishDir" -ForegroundColor Green

# 3. Create ZIP
Write-Host "[3/5] Creating ZIP..." -ForegroundColor Yellow
if (Test-Path $zipFile) {
    Remove-Item $zipFile -Force
}
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipFile -CompressionLevel Optimal
$zipSize = (Get-Item $zipFile).Length / 1MB
Write-Host "  OK ZIP created: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green

# 4. Git commit & push
Write-Host "[4/5] Committing to Git..." -ForegroundColor Yellow
git add $updateXml
$commitMsg = "Release v$Version"
git commit -m $commitMsg
if ($LASTEXITCODE -eq 0) {
    Write-Host "  OK Committed: $commitMsg" -ForegroundColor Green
    git push origin main
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  OK Pushed to GitHub" -ForegroundColor Green
    } else {
        Write-Host "  ! Push failed (continuing anyway...)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ! Nothing to commit (update.xml unchanged)" -ForegroundColor Yellow
}

# 5. Create GitHub Release
Write-Host "[5/5] Creating GitHub Release..." -ForegroundColor Yellow
Start-Sleep -Seconds 2  # GitHub에 커밋 반영 대기
gh release delete "v$Version" --yes 2>$null  # 기존 릴리즈 삭제 (있으면)
gh release create "v$Version" $zipFile --title "v$Version" --notes $ReleaseNotes
if ($LASTEXITCODE -eq 0) {
    Write-Host "  OK Release created!" -ForegroundColor Green
} else {
    Write-Host "  X Release creation failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Deployment Complete! ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Release URL: https://github.com/Jh98JC/WpfApp1/releases/tag/v$Version" -ForegroundColor White
Write-Host "ZIP file: $zipFile" -ForegroundColor White
Write-Host ""

# Open folder
explorer.exe publish