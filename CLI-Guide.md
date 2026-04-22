# GitHub CLI 사용 가이드

## 설치 완료 ✓
GitHub CLI가 이미 설치되었습니다!

## 사용 방법

### 기본 명령어

```powershell
# 1. 릴리즈 생성 (가장 간단)
& "C:\Program Files\GitHub CLI\gh.exe" release create v1.0.3 publish\WpfApp2-v1.0.3.zip --title "Version 1.0.3" --notes "변경 사항"

# 2. 릴리즈 목록 보기
& "C:\Program Files\GitHub CLI\gh.exe" release list

# 3. 특정 릴리즈 보기
& "C:\Program Files\GitHub CLI\gh.exe" release view v1.0.3
```

### 전체 배포 과정 (수동)

```powershell
# 1. 버전 번호를 수동으로 변경
# - WpfApp2\WpfApp2.csproj (3곳)
# - WpfApp2\Window1.xaml (1곳)
# - updates\update.xml (버전, URL)

# 2. 빌드
dotnet publish WpfApp2\WpfApp2.csproj -c Release -r win-x64 --self-contained false -o publish\v1.0.3

# 3. ZIP 생성
Compress-Archive -Path "publish\v1.0.3\*" -DestinationPath "publish\WpfApp2-v1.0.3.zip" -Force

# 4. Git 커밋
git add .
git commit -m "Release v1.0.3"
git push origin main

# 5. GitHub Release 생성
& "C:\Program Files\GitHub CLI\gh.exe" release create v1.0.3 `
    publish\WpfApp2-v1.0.3.zip `
    --title "Version 1.0.3" `
    --notes "## 변경 사항`n- 버그 수정`n- 성능 개선"
```

### 빠른 릴리즈 생성 (ZIP만 있을 때)

```powershell
# 먼저 build-release.ps1로 빌드 & ZIP 생성
.\build-release.ps1 -Version 1.0.3

# 그 다음 create-release.ps1로 릴리즈 생성
.\create-release.ps1 -Version 1.0.3
```

## 유용한 명령어

```powershell
# 릴리즈 삭제 (실수한 경우)
& "C:\Program Files\GitHub CLI\gh.exe" release delete v1.0.3 --yes

# 릴리즈 수정
& "C:\Program Files\GitHub CLI\gh.exe" release edit v1.0.3 --notes "새로운 설명"

# 릴리즈에 파일 추가
& "C:\Program Files\GitHub CLI\gh.exe" release upload v1.0.3 추가파일.zip
```

## PowerShell 별칭 설정 (선택사항)

매번 전체 경로를 입력하기 귀찮다면:

```powershell
# 현재 세션에만 적용
Set-Alias gh "C:\Program Files\GitHub CLI\gh.exe"

# 영구 적용 (프로필에 추가)
Add-Content $PROFILE 'Set-Alias gh "C:\Program Files\GitHub CLI\gh.exe"'
```

이후 `gh` 명령어만으로 사용 가능:

```powershell
gh release list
gh release create v1.0.3 publish\WpfApp2-v1.0.3.zip --title "v1.0.3" --notes "Changes"
```

## 현재 v1.0.2 테스트

```powershell
# 1. v1.0.2 빌드가 이미 완료됨 (publish\WpfApp2-v1.0.2.zip 존재)

# 2. Git 커밋 & 푸시
git add .
git commit -m "Release v1.0.2"
git push origin main

# 3. 릴리즈 생성
.\create-release.ps1 -Version 1.0.2
```
