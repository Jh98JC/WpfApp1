# 간단한 배포 가이드 (한글 경로 지원)

## 🚀 빠른 시작 - 수동 배포

PowerShell 스크립트가 작동하지 않을 때 수동으로 배포하는 방법입니다.

### 1단계: Release 빌드

Visual Studio에서:
1. 상단 메뉴바에서 `Debug` → `Release`로 변경
2. `Ctrl+Shift+B` 키를 눌러 빌드
3. 출력 창에서 "빌드 성공" 확인

### 2단계: 파일 압축

1. 파일 탐색기 열기
2. 다음 경로로 이동:
   ```
   C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\WpfApp2\bin\Release\net8.0-windows
   ```
3. 폴더 안의 **모든 파일** 선택 (Ctrl+A)
4. 우클릭 → `압축(Zip) 폴더에 추가`
5. 생성된 ZIP 파일 이름을 `WpfApp2_v1.0.0.zip`으로 변경
6. ZIP 파일을 상위 폴더로 이동:
   ```
   C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\
   ```

### 3단계: GitHub 저장소 생성

1. https://github.com 접속
2. 로그인
3. 우측 상단 `+` → `New repository`
4. 설정:
   - Repository name: `WpfApp2`
   - 접근 권한: **Public** (중요!)
   - 나머지는 기본값
5. `Create repository` 클릭

### 4단계: GitHub Release 생성

1. 생성된 저장소 페이지에서
2. 우측 `Releases` 클릭
3. `Create a new release` 클릭
4. 정보 입력:
   - **Choose a tag**: `v1.0.0` 입력 후 `Create new tag: v1.0.0 on publish` 클릭
   - **Release title**: `WpfApp2 v1.0.0 - 첫 배포`
   - **Description**: 
     ```
     ## 주요 기능
     - 초기 릴리스
     - 자동 업데이트 기능

     ## 설치 방법
     1. WpfApp2_v1.0.0.zip 다운로드
     2. 압축 해제
     3. WpfApp2.exe 실행
     ```
5. **Attach binaries**: `WpfApp2_v1.0.0.zip` 파일을 드래그 앤 드롭
6. `Publish release` 클릭

### 5단계: update.xml 파일 준비

1. 다음 위치의 파일을 텍스트 편집기(메모장 등)로 열기:
   ```
   C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\WpfApp2\update.xml
   ```

2. 내용을 다음과 같이 수정:
   ```xml
   <?xml version="1.0" encoding="UTF-8"?>
   <item>
     <version>1.0.0.0</version>
     <url>https://github.com/본인사용자명/WpfApp2/releases/download/v1.0.0/WpfApp2_v1.0.0.zip</url>
     <changelog>https://github.com/본인사용자명/WpfApp2/releases/tag/v1.0.0</changelog>
     <mandatory>true</mandatory>
   </item>
   ```

   **중요:** `본인사용자명`을 실제 GitHub 사용자명으로 변경!

3. 저장

### 6단계: updates 폴더 생성 및 파일 복사

1. 다음 위치에 `updates` 폴더 생성:
   ```
   C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\updates
   ```

2. 수정한 `update.xml` 파일을 `updates` 폴더로 복사

### 7단계: App.xaml.cs 파일 수정

1. Visual Studio에서 `WpfApp2\App.xaml.cs` 파일 열기

2. 24-25번 라인 수정:
   ```csharp
   string githubUser = "본인사용자명";     // 예: "kwonJiHoon"
   string githubRepo = "WpfApp2";
   ```

3. 저장 (`Ctrl+S`)

### 8단계: GitHub에 코드 업로드

#### 방법 1: GitHub Desktop 사용 (추천)

1. [GitHub Desktop](https://desktop.github.com/) 다운로드 및 설치
2. GitHub Desktop 실행
3. `File` → `Add local repository`
4. 경로 선택: `C:\권지훈\1. 개인자료\Visual Studio\WpfApp1`
5. "Create a repository" 클릭
6. 좌측 하단 Summary에 `Initial commit` 입력
7. `Commit to main` 클릭
8. 상단 `Publish repository` 클릭
9. Repository name: `WpfApp2`
10. **Keep this code private** 체크 해제 (Public으로!)
11. `Publish repository` 클릭

#### 방법 2: Git 명령어 사용

PowerShell 열기:
```powershell
# 작업 폴더로 이동
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"

# Git 초기화
git init

# .gitignore 파일 생성
@"
bin/
obj/
.vs/
*.user
*.suo
*.zip
"@ | Out-File -FilePath .gitignore -Encoding UTF8

# 모든 파일 추가
git add .

# 커밋
git commit -m "Initial commit"

# GitHub 저장소 연결 (본인사용자명 변경!)
git remote add origin https://github.com/본인사용자명/WpfApp2.git

# 푸시
git branch -M main
git push -u origin main
```

### 9단계: 테스트

1. 빌드된 프로그램 실행
2. 업데이트 체크 확인
3. (현재는 같은 버전이므로 업데이트 알림 없음)

## 🔄 다음 업데이트 배포 시 (v1.0.1 등)

### 1. 버전 번호 변경

`WpfApp2\WpfApp2.csproj` 파일 열어서:
```xml
<Version>1.0.1</Version>
<AssemblyVersion>1.0.1.0</AssemblyVersion>
<FileVersion>1.0.1.0</FileVersion>
```

### 2. Release 빌드

Visual Studio에서 `Ctrl+Shift+B`

### 3. ZIP 파일 생성

위의 2단계와 동일 (파일명: `WpfApp2_v1.0.1.zip`)

### 4. GitHub Release 생성

위의 4단계와 동일 (Tag: `v1.0.1`)

### 5. update.xml 업데이트

`updates\update.xml` 파일을 열어서:
```xml
<version>1.0.1.0</version>
<url>https://github.com/본인사용자명/WpfApp2/releases/download/v1.0.1/WpfApp2_v1.0.1.zip</url>
<changelog>https://github.com/본인사용자명/WpfApp2/releases/tag/v1.0.1</changelog>
```

### 6. GitHub에 푸시

#### GitHub Desktop 사용:
1. GitHub Desktop 열기
2. 변경된 파일 확인
3. Summary: `Update to v1.0.1`
4. `Commit to main` 클릭
5. `Push origin` 클릭

#### Git 명령어 사용:
```powershell
cd "C:\권지훈\1. 개인자료\Visual Studio\WpfApp1"
git add .
git commit -m "Update to v1.0.1"
git push
```

### 7. 이전 버전으로 테스트

이전 버전(v1.0.0) 실행 → 자동 업데이트 확인!

## ⚠️ 주의사항

1. **GitHub 저장소는 반드시 Public으로!**
   - Private 저장소는 인증 필요

2. **파일명 정확히!**
   - Tag: `v1.0.0` (v 붙이기!)
   - ZIP: `WpfApp2_v1.0.0.zip`

3. **update.xml 위치**
   - 반드시 `updates` 폴더 안에!
   - GitHub 저장소의 main 브랜치에 푸시!

4. **버전 번호 일치**
   - WpfApp2.csproj
   - update.xml
   - GitHub Release Tag
   - 모두 같은 버전!

## 🆘 문제 해결

### 업데이트가 안 될 때

1. GitHub 저장소가 Public인지 확인
2. update.xml이 GitHub에 업로드 되었는지 확인:
   ```
   https://github.com/본인사용자명/WpfApp2/blob/main/updates/update.xml
   ```
3. App.xaml.cs의 GitHub 사용자명 확인

### ZIP 파일 다운로드 안 될 때

1. Release에 ZIP 파일이 첨부되었는지 확인
2. URL이 정확한지 확인:
   ```
   https://github.com/본인사용자명/WpfApp2/releases/download/v1.0.0/WpfApp2_v1.0.0.zip
   ```

## 📞 도움이 필요하면

- GitHub Docs: https://docs.github.com/ko
- Git 설치: https://git-scm.com/download/win
- GitHub Desktop: https://desktop.github.com/
