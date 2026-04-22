# WpfApp2 릴리즈 빌드 및 배포 스크립트
param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

Write-Host "=== WpfApp2 릴리즈 빌드 스크립트 ===" -ForegroundColor Cyan
Write-Host "버전: $Version" -ForegroundColor Green
Write-Host ""

$publishDir = "publish\v$Version"
$zipFile = "publish\WpfApp2-v$Version.zip"

# 1. 릴리즈 빌드
Write-Host "[1/3] 릴리즈 빌드 중..." -ForegroundColor Yellow
dotnet publish WpfApp2\WpfApp2.csproj -c Release -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ 빌드 실패" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ 완료: $publishDir" -ForegroundColor Green

# 2. ZIP 파일 생성
Write-Host "[2/3] ZIP 파일 생성 중..." -ForegroundColor Yellow
if (Test-Path $zipFile) {
    Remove-Item $zipFile -Force
}
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipFile -Force
Write-Host "  ✓ 완료: $zipFile" -ForegroundColor Green

# 3. 파일 크기 확인
$zipSize = (Get-Item $zipFile).Length / 1MB
Write-Host "[3/3] 파일 정보" -ForegroundColor Yellow
Write-Host "  파일명: WpfApp2-v$Version.zip" -ForegroundColor White
Write-Host "  크기: $([math]::Round($zipSize, 2)) MB" -ForegroundColor White

Write-Host ""
Write-Host "=== 빌드 완료 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "다음 단계:" -ForegroundColor Yellow
Write-Host "  1. GitHub Desktop에서 변경사항 커밋 & 푸시" -ForegroundColor White
Write-Host "  2. GitHub에서 v$Version Release 생성" -ForegroundColor White
Write-Host "  3. WpfApp2-v$Version.zip 업로드" -ForegroundColor White
Write-Host "  4. Publish release 클릭" -ForegroundColor White
Write-Host ""
Write-Host "ZIP 파일 위치: $zipFile" -ForegroundColor Cyan

# 폴더 열기
explorer "publish"
