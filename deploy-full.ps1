# WpfApp2 완전 자동 배포 스크립트
# GitHub CLI(gh) 필요: https://cli.github.com/

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$false)]
    [string]$ChangelogMessage = "• 버그 수정 및 성능 개선"
)

Write-Host "=== WpfApp2 완전 자동 배포 ===" -ForegroundColor Cyan
Write-Host "버전: $Version" -ForegroundColor Green
Write-Host ""

# GitHub CLI 확인
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "오류: GitHub CLI(gh)가 설치되어 있지 않습니다." -ForegroundColor Red
    Write-Host "설치 방법: https://cli.github.com/" -ForegroundColor Yellow
    exit 1
}

# 1. 버전 업데이트
Write-Host "[1/5] 버전 업데이트 중..." -ForegroundColor Yellow
& .\update-version.ps1 -NewVersion $Version -ChangelogMessage $ChangelogMessage
if ($LASTEXITCODE -ne 0) { exit 1 }

# 2. 릴리즈 빌드
Write-Host ""
Write-Host "[2/5] 릴리즈 빌드 중..." -ForegroundColor Yellow
& .\build-release.ps1 -Version $Version
if ($LASTEXITCODE -ne 0) { exit 1 }

# 3. Git 커밋
Write-Host ""
Write-Host "[3/5] Git 커밋 중..." -ForegroundColor Yellow
git add .
git commit -m "Update to v$Version"
git push origin main
Write-Host "  ✓ Git 푸시 완료" -ForegroundColor Green

# 4. GitHub Release 생성
Write-Host ""
Write-Host "[4/5] GitHub Release 생성 중..." -ForegroundColor Yellow
$zipFile = "publish\WpfApp2-v$Version.zip"
$releaseNotes = @"
## 변경 사항
$ChangelogMessage

## 설치 방법
1. WpfApp2-v$Version.zip 다운로드
2. 압축 해제
3. WpfApp2.exe 실행
"@

gh release create "v$Version" $zipFile `
    --title "Version $Version" `
    --notes $releaseNotes

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Release 생성 완료" -ForegroundColor Green
} else {
    Write-Host "  ✗ Release 생성 실패" -ForegroundColor Red
    exit 1
}

# 5. 완료
Write-Host ""
Write-Host "[5/5] update.xml 반영 대기 중..." -ForegroundColor Yellow
Write-Host "  GitHub가 파일을 업데이트하는 중입니다 (약 10-30초 소요)" -ForegroundColor Gray
Start-Sleep -Seconds 15

Write-Host ""
Write-Host "=== 배포 완료 ===" -ForegroundColor Green
Write-Host ""
Write-Host "Release URL: https://github.com/Jh98JC/WpfApp1/releases/tag/v$Version" -ForegroundColor Cyan
Write-Host "Update XML: https://raw.githubusercontent.com/Jh98JC/WpfApp1/main/updates/update.xml" -ForegroundColor Cyan
Write-Host ""
