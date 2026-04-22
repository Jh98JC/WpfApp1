# Add Git to System PATH permanently
Write-Host "Adding GitHub Desktop's Git to system PATH..." -ForegroundColor Cyan

$gitPath = "C:\Users\jc941\AppData\Local\GitHubDesktop\app-3.5.8\resources\app\git\cmd"

$currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")

if ($currentUserPath -like "*$gitPath*") {
    Write-Host "Git path already exists in system PATH" -ForegroundColor Yellow
} else {
    $newPath = "$currentUserPath;$gitPath"
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Git path added successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "IMPORTANT: Restart PowerShell (or Visual Studio) for changes to take effect" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Testing Git..." -ForegroundColor Cyan
$env:Path += ";$gitPath"
git --version
Write-Host "Done!" -ForegroundColor Green
