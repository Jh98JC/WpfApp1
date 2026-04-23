param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$ReleaseNotes = "- 새로운 기능 및 개선 사항`n- 버그 수정 및 성능 개선"
)

$ErrorActionPreference = "Stop"

$updateXml = "updates\update.xml"
$projectDir = "WpfApp2"
$publishDir = "publish\v$Version"
$zipFile = "publish\WpfApp2-v$Version.zip"

Write-Host ""
Write-Host "=== WpfApp2 Release Build & Deploy ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host ""

# 1. Update update.xml
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

$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($updateXml, $xmlContent, $encoding)
Write-Host "  OK update.xml updated to v$Version" -ForegroundColor Green

# 2. Build Release
Write-Host "[2/5] Building Release..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
dotnet publish $projectDir -c Release -o $publishDir -p:PublishSingleFile=false -p:PublishReadyToRun=false
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

# 4. Git commit & push (모든 변경사항 커밋)
Write-Host "[4/5] Committing to Git..." -ForegroundColor Yellow
git add -A
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
    Write-Host "  ! Nothing to commit (all files unchanged)" -ForegroundColor Yellow
}

# 5. Create GitHub Release
Write-Host "[5/5] Creating GitHub Release..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
gh release delete "v$Version" --yes 2>$null
gh release create "v$Version" $zipFile --title "v$Version" --notes $ReleaseNotes
if ($LASTEXITCODE -eq 0) {
    Write-Host "  OK Release created!" -ForegroundColor Green
} else {
    Write-Host "  X Release creation failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Deployment Complete! ===" -ForegroundColor Cyan
Write-Host "Release URL: https://github.com/Jh98JC/WpfApp1/releases/tag/v$Version" -ForegroundColor White
Write-Host "ZIP file: $zipFile" -ForegroundColor White
Write-Host ""