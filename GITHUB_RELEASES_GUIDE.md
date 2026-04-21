# GitHub Releases를 이용한 자동 업데이트 가이드

## 📌 준비 단계

### 1. GitHub 저장소 생성

1. [GitHub](https://github.com)에 로그인
2. 우측 상단 `+` → `New repository` 클릭
3. 저장소 설정:
   - Repository name: `WpfApp2` (또는 원하는 이름)
   - Public 또는 Private 선택 (업데이트 파일 접근을 위해 Public 권장)
   - `Create repository` 클릭

### 2. 저장소 구조 생성

로컬에서 Git 저장소 초기화:

```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"

# Git 저장소 초기화 (처음만)
git init

# .gitignore 파일 생성 (불필요한 파일 제외)
@"
bin/
obj/
.vs/
*.user
*.suo
"@ | Out-File -FilePath .gitignore -Encoding UTF8

# updates 폴더 생성
mkdir updates -Force

# update.xml을 updates 폴더로 복사
Copy-Item WpfApp2\update.xml updates\update.xml
```

### 3. App.xaml.cs에 GitHub 정보 입력

`WpfApp2\App.xaml.cs` 파일을 열어서 **24-25번 라인** 수정:

```csharp
string githubUser = "YOUR-USERNAME";        // GitHub 사용자명으로 변경
string githubRepo = "YOUR-REPO-NAME";       // 저장소 이름으로 변경
```

**예시:**
```csharp
string githubUser = "kwonJiHoon";
string githubRepo = "WpfApp2";
```

## 🚀 첫 배포 (v1.0.0)

### 1. Release 빌드

Visual Studio에서:
1. 상단 메뉴: `Debug` → `Release`로 변경
2. `Ctrl+Shift+B` (솔루션 빌드)
3. 빌드된 파일 위치 확인:
   ```
   WpfApp2\bin\Release\net8.0-windows\
   ```

### 2. 배포 파일 압축

PowerShell에서 실행:

```powershell
# 작업 디렉토리 이동
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\WpfApp2\bin\Release\net8.0-windows"

# ZIP 파일 생성 (v1.0.0)
Compress-Archive -Path * -DestinationPath "..\..\..\..\WpfApp2_v1.0.0.zip" -Force

# 생성된 파일 확인
cd "..\..\..\.."
dir WpfApp2_v1.0.0.zip
```

### 3. GitHub에 코드 푸시

```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"

# 모든 파일 추가
git add .

# 커밋
git commit -m "Initial release v1.0.0"

# GitHub 원격 저장소 연결 (본인의 저장소 URL로 변경)
git remote add origin https://github.com/YOUR-USERNAME/YOUR-REPO-NAME.git

# main 브랜치로 푸시
git branch -M main
git push -u origin main
```

### 4. GitHub Release 생성

#### 웹 브라우저에서:

1. GitHub 저장소 페이지 이동
2. 우측의 `Releases` 클릭
3. `Create a new release` 클릭
4. 릴리스 정보 입력:
   - **Tag version**: `v1.0.0` (정확히 입력!)
   - **Release title**: `WpfApp2 v1.0.0 - 첫 배포`
   - **Description**: 변경 사항 작성
     ```
     ## 주요 기능
     - 초기 릴리스
     - 자동 업데이트 기능 포함

     ## 설치 방법
     1. WpfApp2_v1.0.0.zip 다운로드
     2. 압축 해제
     3. WpfApp2.exe 실행
     ```
5. **파일 첨부**:
   - `WpfApp2_v1.0.0.zip` 파일을 드래그 앤 드롭
6. `Publish release` 클릭

### 5. update.xml 파일 업로드

update.xml을 `updates` 폴더에 넣고 GitHub에 푸시:

```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"

# update.xml 파일 수정 (YOUR-USERNAME, YOUR-REPO-NAME 변경 후)
# 텍스트 에디터로 updates\update.xml 열어서 수정

# 변경사항 커밋
git add updates/update.xml
git commit -m "Add update.xml for auto-update"
git push
```

**updates\update.xml 파일 내용 (수정 필요):**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>1.0.0.0</version>
  <url>https://github.com/YOUR-USERNAME/YOUR-REPO-NAME/releases/download/v1.0.0/WpfApp2_v1.0.0.zip</url>
  <changelog>https://github.com/YOUR-USERNAME/YOUR-REPO-NAME/releases/tag/v1.0.0</changelog>
  <mandatory>true</mandatory>
</item>
```

## 🔄 업데이트 배포 (v1.0.1 이상)

### 1. 버전 번호 업데이트

`WpfApp2\WpfApp2.csproj` 파일 수정:

```xml
<PropertyGroup>
  ...
  <Version>1.0.1</Version>
  <AssemblyVersion>1.0.1.0</AssemblyVersion>
  <FileVersion>1.0.1.0</FileVersion>
</PropertyGroup>
```

### 2. 코드 수정 및 빌드

1. 필요한 코드 수정
2. Release 모드로 빌드 (`Ctrl+Shift+B`)

### 3. 배포 파일 생성

```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\WpfApp2\bin\Release\net8.0-windows"

# ZIP 파일 생성
Compress-Archive -Path * -DestinationPath "..\..\..\..\WpfApp2_v1.0.1.zip" -Force

cd "..\..\..\.."
```

### 4. update.xml 파일 업데이트

`updates\update.xml` 파일 수정:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>1.0.1.0</version>
  <url>https://github.com/YOUR-USERNAME/YOUR-REPO-NAME/releases/download/v1.0.1/WpfApp2_v1.0.1.zip</url>
  <changelog>https://github.com/YOUR-USERNAME/YOUR-REPO-NAME/releases/tag/v1.0.1</changelog>
  <mandatory>true</mandatory>
</item>
```

### 5. GitHub에 푸시

```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"

git add .
git commit -m "Update to v1.0.1"
git push
```

### 6. GitHub Release 생성

1. GitHub 저장소 → `Releases` → `Create a new release`
2. 릴리스 정보:
   - Tag: `v1.0.1`
   - Title: `WpfApp2 v1.0.1 - 업데이트`
   - Description: 변경 사항 작성
3. `WpfApp2_v1.0.1.zip` 파일 첨부
4. `Publish release` 클릭

### 7. 테스트

1. 이전 버전(v1.0.0) 실행
2. 자동 업데이트 다이얼로그 확인
3. 업데이트 진행
4. 새 버전으로 재시작 확인

## 📋 빠른 참조 - PowerShell 스크립트

### 전체 배포 자동화 스크립트

`deploy.ps1` 파일을 프로젝트 루트에 생성:

```powershell
# 배포 자동화 스크립트
param(
    [Parameter(Mandatory=$true)]
    [string]$Version  # 예: "1.0.1"
)

$ErrorActionPreference = "Stop"
$RootPath = "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"
$ProjectPath = "$RootPath\WpfApp2"
$BuildPath = "$ProjectPath\bin\Release\net8.0-windows"
$ZipName = "WpfApp2_v$Version.zip"

Write-Host "🚀 WpfApp2 v$Version 배포 시작..." -ForegroundColor Cyan

# 1. Release 빌드
Write-Host "`n📦 Release 빌드 중..." -ForegroundColor Yellow
dotnet build "$ProjectPath\WpfApp2.csproj" -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 빌드 실패!" -ForegroundColor Red
    exit 1
}

# 2. ZIP 파일 생성
Write-Host "`n📚 ZIP 파일 생성 중..." -ForegroundColor Yellow
if (Test-Path "$RootPath\$ZipName") {
    Remove-Item "$RootPath\$ZipName" -Force
}
Compress-Archive -Path "$BuildPath\*" -DestinationPath "$RootPath\$ZipName"

Write-Host "✅ $ZipName 생성 완료!" -ForegroundColor Green

# 3. Git 커밋
Write-Host "`n📝 Git 커밋 중..." -ForegroundColor Yellow
cd $RootPath
git add .
git commit -m "Release v$Version"

# 4. Git 푸시
Write-Host "`n⬆️ GitHub에 푸시 중..." -ForegroundColor Yellow
git push

Write-Host "`n✨ 배포 파일 준비 완료!" -ForegroundColor Green
Write-Host "`n다음 단계:" -ForegroundColor Cyan
Write-Host "1. GitHub 저장소 → Releases → Create a new release" -ForegroundColor White
Write-Host "2. Tag: v$Version" -ForegroundColor White
Write-Host "3. 파일 첨부: $ZipName" -ForegroundColor White
Write-Host "4. Publish release" -ForegroundColor White
Write-Host "`n5. updates\update.xml 파일의 버전을 $Version.0으로 업데이트" -ForegroundColor White
Write-Host "6. Git 커밋 & 푸시" -ForegroundColor White

# ZIP 파일 위치 열기
explorer.exe $RootPath
```

**사용 방법:**

```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"
.\deploy.ps1 -Version "1.0.1"
```

## 🎯 체크리스트

### 첫 배포 시
- [ ] GitHub 저장소 생성
- [ ] App.xaml.cs에 GitHub 정보 입력
- [ ] Release 빌드
- [ ] ZIP 파일 생성
- [ ] GitHub에 코드 푸시
- [ ] GitHub Release v1.0.0 생성 및 ZIP 첨부
- [ ] updates\update.xml 수정 및 푸시
- [ ] 테스트 실행

### 업데이트 배포 시
- [ ] WpfApp2.csproj 버전 번호 증가
- [ ] 코드 수정 및 테스트
- [ ] Release 빌드
- [ ] ZIP 파일 생성
- [ ] updates\update.xml 버전 업데이트
- [ ] GitHub에 푸시
- [ ] GitHub Release 생성 및 ZIP 첨부
- [ ] 이전 버전으로 자동 업데이트 테스트

## ⚠️ 주의사항

1. **Tag 버전 형식**: 반드시 `v1.0.0` 형식 사용 (v 접두사 필수)
2. **Public 저장소**: Private 저장소는 인증이 필요하므로 Public 권장
3. **파일명 일치**: ZIP 파일명이 update.xml의 URL과 정확히 일치해야 함
4. **버전 번호**: WpfApp2.csproj와 update.xml의 버전이 일치해야 함
5. **첫 배포**: 반드시 v1.0.0부터 시작

## 🔍 트러블슈팅

### 업데이트가 체크되지 않음
- GitHub 저장소가 Public인지 확인
- update.xml의 URL이 올바른지 확인
- App.xaml.cs의 GitHub 사용자명/저장소명 확인

### 404 에러
- Release Tag가 정확한지 확인 (v1.0.0)
- ZIP 파일이 Release에 첨부되었는지 확인
- URL의 버전 번호가 일치하는지 확인

### update.xml을 찾을 수 없음
- updates 폴더가 main 브랜치에 있는지 확인
- Git 푸시가 완료되었는지 확인
- GitHub에서 파일 확인: `https://github.com/사용자명/저장소명/blob/main/updates/update.xml`

## 📚 참고 링크

- [AutoUpdater.NET GitHub](https://github.com/ravibpatel/AutoUpdater.NET)
- [GitHub Releases 문서](https://docs.github.com/en/repositories/releasing-projects-on-github)
- [Git 기본 명령어](https://git-scm.com/docs)
