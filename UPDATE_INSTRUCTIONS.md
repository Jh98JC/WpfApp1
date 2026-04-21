# WpfApp1 업데이트 가이드

## 현재 상태

✅ AutoUpdater.NET 설치 완료
✅ GitHub 저장소 연결 완료 (https://github.com/Jh98JC/WpfApp1)
✅ update.xml 파일 생성 완료

## 다음 단계

### 1. update.xml을 GitHub에 푸시

GitHub Desktop에서:
1. `updates/update.xml` 파일이 Changes 탭에 나타날 것입니다
2. Summary에 "Add update.xml for AutoUpdater" 입력
3. "Commit to main" 클릭
4. "Push origin" 클릭

### 2. 첫 번째 릴리스 만들기

프로그램을 빌드하고 GitHub Release를 만들어야 합니다:

1. **Visual Studio에서 Release 모드로 빌드**
   - 상단 메뉴: Build → Configuration Manager
   - Active solution configuration: **Release** 선택
   - Build → Build Solution (Ctrl+Shift+B)

2. **빌드된 파일 찾기**
   - 경로: `WpfApp2\bin\Release\net8.0-windows\`
   - 필요한 파일들:
     - WpfApp2.exe
     - WpfApp2.dll
     - WpfApp2.pdb
     - 기타 모든 DLL 파일들

3. **ZIP 파일 생성**
   - 위 폴더의 모든 파일을 선택
   - 마우스 우클릭 → 보내기 → 압축(ZIP) 폴더
   - 파일명: `WpfApp2.zip`

4. **GitHub에서 Release 생성**
   - https://github.com/Jh98JC/WpfApp1/releases 접속
   - "Create a new release" 클릭
   - Tag version: `v1.0.0` 입력
   - Release title: `WpfApp2 v1.0.0` 입력
   - Description:
     ```
     ## WpfApp2 첫 번째 릴리스

     ### 주요 기능
     - 탭 기반 인터페이스
     - 자동 업데이트 지원
     - Material Design UI
     ```
   - `WpfApp2.zip` 파일을 드래그하여 업로드
   - "Publish release" 클릭

### 3. 테스트

Release를 만든 후:
1. 프로그램을 실행
2. 업데이트 버튼 클릭
3. "현재 최신 버전을 사용하고 있습니다" 메시지가 나타나야 함

### 4. 새 버전 업데이트 방법

나중에 새 버전을 배포할 때:

1. **프로젝트 파일에서 버전 번호 증가**
   ```xml
   <Version>1.0.1</Version>
   <AssemblyVersion>1.0.1.0</AssemblyVersion>
   <FileVersion>1.0.1.0</FileVersion>
   ```

2. **update.xml 수정**
   ```xml
   <version>1.0.1.0</version>
   <url>https://github.com/Jh98JC/WpfApp1/releases/download/v1.0.1/WpfApp2.zip</url>
   ```

3. **Release 모드로 빌드 및 ZIP 생성**

4. **GitHub에서 새 Release 생성** (태그: v1.0.1)

5. **update.xml을 커밋 & 푸시**

## 문제 해결

### "업데이트 서버에 연결할 수 없습니다" 에러
- update.xml 파일이 GitHub main 브랜치에 있는지 확인
- URL이 정확한지 확인: https://raw.githubusercontent.com/Jh98JC/WpfApp1/main/updates/update.xml
- 인터넷 연결 확인

### "404 Not Found" 에러
- update.xml이 푸시되었는지 확인
- 브라우저에서 직접 URL 접속 테스트

## 참고 사항

- **반드시 Release 모드로 빌드**해야 합니다 (Debug 모드 X)
- ZIP 파일에는 **모든 DLL 파일**이 포함되어야 합니다
- 버전 번호는 **반드시 증가**해야 합니다 (1.0.0 → 1.0.1)
- update.xml 변경 후 반드시 **커밋 & 푸시**해야 합니다
