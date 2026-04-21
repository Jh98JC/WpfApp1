# WpfApp2 배포 자동화 스크립트
param(
    [Parameter(Mandatory=$true)]
    [string]$Version  # 예: "1.0.1"
)

$ErrorActionPreference = "Stop"

# UTF-8 인코딩 설정 (한글 경로 지원)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# 현재 스크립트 위치를 루트 경로로 사용
$RootPath = $PSScriptRoot
if ([string]::IsNullOrEmpty($RootPath)) {
    $RootPath = Get-Location
}

$ProjectPath = Join-Path $RootPath "WpfApp2"
$BuildPath = Join-Path $ProjectPath "bin\Release\net8.0-windows"
$ZipName = "WpfApp2_v$Version.zip"

Write-Host "🚀 WpfApp2 v$Version 배포 시작..." -ForegroundColor Cyan
Write-Host "   경로: $RootPath" -ForegroundColor Gray

# 1. Release 빌드
Write-Host "`n📦 Release 빌드 중..." -ForegroundColor Yellow
Set-Location $RootPath
dotnet build (Join-Path $ProjectPath "WpfApp2.csproj") -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 빌드 실패!" -ForegroundColor Red
    exit 1
}

# 2. ZIP 파일 생성
Write-Host "`n📚 ZIP 파일 생성 중..." -ForegroundColor Yellow
$ZipPath = Join-Path $RootPath $ZipName
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
Compress-Archive -Path (Join-Path $BuildPath "*") -DestinationPath $ZipPath

Write-Host "✅ $ZipName 생성 완료!" -ForegroundColor Green
Write-Host "   위치: $ZipPath" -ForegroundColor Gray

# 3. Git 상태 확인
Write-Host "`n📝 Git 상태 확인..." -ForegroundColor Yellow
Set-Location $RootPath
git status

# 4. Git 커밋 여부 확인
$commit = Read-Host "`nGit에 커밋하시겠습니까? (y/n)"
if ($commit -eq 'y') {
    git add .
    git commit -m "Release v$Version"

    $push = Read-Host "GitHub에 푸시하시겠습니까? (y/n)"
    if ($push -eq 'y') {
        git push
        Write-Host "✅ GitHub에 푸시 완료!" -ForegroundColor Green
    }
}

Write-Host "`n✨ 배포 파일 준비 완료!" -ForegroundColor Green
Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "다음 단계:" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. GitHub 저장소로 이동" -ForegroundColor White
Write-Host "   → Releases → 'Create a new release' 클릭" -ForegroundColor Gray
Write-Host ""
Write-Host "2. 릴리스 정보 입력" -ForegroundColor White
Write-Host "   - Tag: v$Version" -ForegroundColor Gray
Write-Host "   - Title: WpfApp2 v$Version" -ForegroundColor Gray
Write-Host "   - Description: 변경 사항 작성" -ForegroundColor Gray
Write-Host ""
Write-Host "3. 파일 첨부" -ForegroundColor White
Write-Host "   - $ZipName 드래그 앤 드롭" -ForegroundColor Gray
Write-Host ""
Write-Host "4. 'Publish release' 클릭" -ForegroundColor White
Write-Host ""
Write-Host "5. updates\update.xml 파일 업데이트" -ForegroundColor White
Write-Host "   - <version>$Version.0</version>" -ForegroundColor Gray
Write-Host "   - <url>...releases/download/v$Version/$ZipName</url>" -ForegroundColor Gray
Write-Host ""
Write-Host "6. Git 커밋 & 푸시" -ForegroundColor White
Write-Host "   git add updates/update.xml" -ForegroundColor Gray
Write-Host "   git commit -m `"Update to v$Version`"" -ForegroundColor Gray
Write-Host "   git push" -ForegroundColor Gray
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

# ZIP 파일 위치 열기
Start-Process explorer.exe -ArgumentList $RootPath
