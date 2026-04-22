# Release Script with Changelog File Support
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [string]$Changelog = "",

    [Parameter(Mandatory=$false)]
    [string]$ChangelogFile = ""
)

$zipFile = "publish\WpfApp2-v$Version.zip"

if (-not (Test-Path $zipFile)) {
    Write-Host "Error: ZIP file not found: $zipFile" -ForegroundColor Red
    Write-Host "Run build-release.ps1 first!" -ForegroundColor Yellow
    exit 1
}

Write-Host "Creating GitHub Release v$Version..." -ForegroundColor Cyan

# 변경사항 결정
if (![string]::IsNullOrWhiteSpace($ChangelogFile) -and (Test-Path $ChangelogFile)) {
    Write-Host "Reading changelog from file: $ChangelogFile" -ForegroundColor Yellow
    $changelogContent = Get-Content $ChangelogFile -Raw
} elseif (![string]::IsNullOrWhiteSpace($Changelog)) {
    $changelogContent = $Changelog
} else {
    $changelogContent = "- Bug fixes and performance improvements"
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
