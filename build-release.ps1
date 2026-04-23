param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [Parameter(Mandatory=$false)]
    [string]$ReleaseNotes = "- "
)

$ErrorActionPreference = "Stop"

$updateXml = "updates\update.xml"
$projectDir = "WpfApp2"
$projectFile = "$projectDir\WpfApp2.csproj"
$publishDir = "publish\v$Version"
$zipFile = "publish\WpfApp2-v$Version.zip"

Write-Host ""
Write-Host "=== WpfApp2 Release Build & Deploy ===" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host ""

# 1. Update WpfApp2.csproj version
Write-Host "[1/6] Updating WpfApp2.csproj version..." -ForegroundColor Yellow
$csprojContent = Get-Content $projectFile -Raw -Encoding UTF8
$csprojContent = $csprojContent -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
$csprojContent = $csprojContent -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
$csprojContent = $csprojContent -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$Version</FileVersion>"
$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($projectFile, $csprojContent, $encoding)
Write-Host "  OK WpfApp2.csproj updated to v$Version" -ForegroundColor Green

# 2. Update update.xml
Write-Host "[2/6] Updating update.xml..." -ForegroundColor Yellow
$xmlContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>$Version</version>
  <url>https://github.com/Jh98JC/WpfApp1/releases/download/v$Version/WpfApp2-v$Version.zip</url>
  <changelog>https://github.com/Jh98JC/WpfApp1/releases/tag/v$Version</changelog>
  <mandatory>true</mandatory>
</item>
"@

$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($updateXml, $xmlContent, $encoding)
Write-Host "  OK update.xml updated to v$Version" -ForegroundColor Green

# 3. Build Release
Write-Host "[3/6] Building Release..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
dotnet publish $projectDir -c Release -o $publishDir -p:PublishSingleFile=false -p:PublishReadyToRun=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "  X Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "  OK Build complete: $publishDir" -ForegroundColor Green

# 4. Create ZIP
Write-Host "[4/6] Creating ZIP..." -ForegroundColor Yellow
if (Test-Path $zipFile) {
    Remove-Item $zipFile -Force
}
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipFile -CompressionLevel Optimal
$zipSize = (Get-Item $zipFile).Length / 1MB
Write-Host "  OK ZIP created: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green

# 5. Git commit & push (모든 변경사항 커밋)
Write-Host "[5/6] Committing to Git..." -ForegroundColor Yellow
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

# 6. Create GitHub Release
Write-Host "[6/6] Creating GitHub Release..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

# 기존 릴리즈 삭제 시도 (에러 무시)
try {
    gh release delete "v$Version" --yes 2>$null | Out-Null
} catch {
    # 릴리즈가 없으면 무시
}

# 새 릴리즈 생성
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
