# Quick Release Script
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [string]$Changelog = ""
)

$zipFile = "publish\WpfApp2-v$Version.zip"

if (-not (Test-Path $zipFile)) {
    Write-Host "Error: ZIP file not found: $zipFile" -ForegroundColor Red
    Write-Host "Run build-release.ps1 first!" -ForegroundColor Yellow
    exit 1
}

Write-Host "Creating GitHub Release v$Version..." -ForegroundColor Cyan

# 기본 변경사항 또는 사용자 입력
if ([string]::IsNullOrWhiteSpace($Changelog)) {
    $changelogContent = "- Bug fixes and performance improvements"
} else {
    $changelogContent = $Changelog
}

$releaseNotes = @"
## Changes
$changelogContent

## Installation
1. Download WpfApp2-v$Version.zip
2. Extract files
3. Run WpfApp2.exe
"@

gh release create "v$Version" $zipFile --title "Version $Version" --notes $releaseNotes

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Release created successfully!" -ForegroundColor Green
    Write-Host "URL: https://github.com/Jh98JC/WpfApp1/releases/tag/v$Version" -ForegroundColor Cyan
} else {
    Write-Host "Failed to create release" -ForegroundColor Red
}
