# WpfApp2 자동화 스크립트 사용 가이드

## 📦 스크립트 목록

### 1️⃣ `update-version.ps1` - 버전 업데이트
모든 파일의 버전 번호를 자동으로 업데이트합니다.

**사용법:**
```powershell
.\update-version.ps1 -NewVersion 1.0.3

# 변경 로그 메시지 추가
.\update-version.ps1 -NewVersion 1.0.3 -ChangelogMessage "• 새 기능 추가`n• 버그 수정"
```

**업데이트되는 파일:**
- `WpfApp2/WpfApp2.csproj` (Version, AssemblyVersion, FileVersion)
- `WpfApp2/Window1.xaml` (버전 텍스트)
- `updates/update.xml` (버전, URL)
- `WpfApp2/update.xml` (버전, URL, changelog)

---

### 2️⃣ `build-release.ps1` - 릴리즈 빌드
Release 빌드를 생성하고 ZIP 파일로 압축합니다.

**사용법:**
```powershell
.\build-release.ps1 -Version 1.0.3
```

**생성 파일:**
- `publish/v1.0.3/` - 빌드 파일들
- `publish/WpfApp2-v1.0.3.zip` - GitHub Release용 ZIP

---

### 3️⃣ `deploy-full.ps1` - 완전 자동 배포 (GitHub CLI 필요)
버전 업데이트 → 빌드 → 커밋 → GitHub Release 생성까지 모두 자동화

**사전 요구사항:**
- GitHub CLI 설치: https://cli.github.com/
- `gh auth login` 명령으로 인증 완료

**사용법:**
```powershell
.\deploy-full.ps1 -Version 1.0.3

# 변경 로그 추가
.\deploy-full.ps1 -Version 1.0.3 -ChangelogMessage "• 새 기능 A`n• 버그 수정 B`n• 성능 개선 C"
```

---

## 🚀 일반적인 배포 워크플로우

### 방법 A: 수동 배포 (GitHub Desktop 사용)
```powershell
# 1. 버전 업데이트
.\update-version.ps1 -NewVersion 1.0.3

# 2. 릴리즈 빌드
.\build-release.ps1 -Version 1.0.3

# 3. GitHub Desktop에서 커밋 & 푸시
# 4. GitHub 웹사이트에서 Release 생성 및 ZIP 업로드
```

### 방법 B: 완전 자동 배포 (GitHub CLI)
```powershell
.\deploy-full.ps1 -Version 1.0.3 -ChangelogMessage "• 새 기능 추가"
```

---

## 📝 버전 번호 규칙

**버전 형식:** `Major.Minor.Patch` (예: 1.0.3)

- **Major** (1.x.x): 대규모 변경, 호환성 깨짐
- **Minor** (x.1.x): 새 기능 추가, 하위 호환성 유지
- **Patch** (x.x.1): 버그 수정, 작은 개선

---

## ⚠️ 주의사항

1. **실행 정책 오류 시:**
   ```powershell
   Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
   ```

2. **Git 미설치 시:**
   - Git 설치: https://git-scm.com/
   - `git config --global user.name "Your Name"`
   - `git config --global user.email "your@email.com"`

3. **GitHub CLI 미설치 시:**
   - `deploy-full.ps1` 대신 방법 A 사용
   - 또는 GitHub CLI 설치: https://cli.github.com/

---

## 🧪 테스트 방법

이전 버전(예: v1.0.1)의 EXE를 실행하여 업데이트가 작동하는지 확인:

```powershell
# 테스트용 이전 버전 빌드
.\build-release.ps1 -Version 1.0.1
.\publish\v1.0.1\WpfApp2.exe  # 이 파일로 업데이트 테스트
```

---

## 📞 문제 해결

### Q: "실행할 수 없습니다" 오류
**A:** PowerShell 실행 정책 변경:
```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### Q: GitHub CLI가 작동하지 않음
**A:** 인증 확인:
```powershell
gh auth status
gh auth login
```

### Q: 빌드가 실패함
**A:** Visual Studio가 설치되어 있고 .NET 8 SDK가 있는지 확인:
```powershell
dotnet --version
```
