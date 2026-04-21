# WpfApp2 자동 업데이트 시스템 사용 가이드

## 개요
이 애플리케이션은 AutoUpdater.NET을 사용하여 자동 업데이트 기능을 지원합니다.

## 설정 방법

### 1. 업데이트 서버 준비
업데이트 파일을 호스팅할 서버가 필요합니다:
- 웹 서버 (IIS, Apache, Nginx 등)
- 클라우드 스토리지 (Azure Blob, AWS S3, Google Drive 등)
- 로컬 네트워크 공유 폴더

### 2. 업데이트 URL 설정
`App.xaml.cs` 파일의 23번 라인을 수정하세요:

```csharp
// 예시 1: 웹 서버
AutoUpdater.Start("https://yourdomain.com/updates/update.xml");

// 예시 2: 로컬 네트워크
AutoUpdater.Start("http://192.168.1.100/updates/update.xml");

// 예시 3: 파일 공유
AutoUpdater.Start("\\\\SERVER\\SharedFolder\\updates\\update.xml");
```

### 3. 업데이트 파일 준비

#### 3.1 새 버전 빌드
1. `WpfApp2.csproj`에서 버전 번호 업데이트:
   ```xml
   <Version>1.0.1</Version>
   <AssemblyVersion>1.0.1.0</AssemblyVersion>
   <FileVersion>1.0.1.0</FileVersion>
   ```

2. Release 모드로 빌드:
   - Visual Studio 상단에서 `Debug` → `Release`로 변경
   - 빌드 → 솔루션 빌드 (Ctrl+Shift+B)

3. 빌드된 파일 위치:
   ```
   WpfApp2\bin\Release\net8.0-windows\
   ```

#### 3.2 업데이트 패키지 생성
빌드된 파일들을 ZIP으로 압축:
- WpfApp2.exe
- WpfApp2.dll
- 모든 .dll 파일들
- 리소스 파일들 (1.jpg, 1.png 등)

파일명 예시: `WpfApp2_v1.0.1.zip`

#### 3.3 update.xml 파일 수정
```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
  <version>1.0.1.0</version>
  <url>https://yourdomain.com/updates/WpfApp2_v1.0.1.zip</url>
  <changelog>https://yourdomain.com/updates/changelog.html</changelog>
  <mandatory>true</mandatory>
</item>
```

### 4. 서버에 파일 업로드
다음 파일들을 서버에 업로드하세요:
```
updates/
├── update.xml
├── WpfApp2_v1.0.1.zip
└── changelog.html (선택사항)
```

## 업데이트 프로세스

### 사용자 측
1. 애플리케이션 실행
2. 자동으로 업데이트 체크
3. 새 버전 발견 시 업데이트 다이얼로그 표시
4. "업데이트" 클릭 → 자동 다운로드 및 설치
5. 애플리케이션 자동 재시작

### 개발자 측
1. 코드 수정
2. 버전 번호 증가 (WpfApp2.csproj)
3. Release 빌드
4. ZIP 파일 생성
5. update.xml 업데이트
6. 서버에 업로드

## 선택적 기능

### 수동 업데이트 체크 버튼 추가
MainWindow.xaml.cs에 다음 코드 추가:

```csharp
private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
{
    AutoUpdater.Start("https://yourdomain.com/updates/update.xml");
}
```

### 업데이트 알림 커스터마이징
App.xaml.cs에 다음 코드 추가:

```csharp
AutoUpdater.ApplicationExitEvent += AutoUpdater_ApplicationExitEvent;
AutoUpdater.CheckForUpdateEvent += AutoUpdater_CheckForUpdateEvent;

private void AutoUpdater_ApplicationExitEvent()
{
    System.Windows.Application.Current.Shutdown();
}

private void AutoUpdater_CheckForUpdateEvent(UpdateInfoEventArgs args)
{
    if (args.IsUpdateAvailable)
    {
        MessageBox.Show($"새 버전 {args.CurrentVersion}이 있습니다!\n현재 버전: {args.InstalledVersion}",
            "업데이트 알림", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
```

## 주의사항

1. **첫 배포**: 반드시 버전 1.0.0으로 배포하세요
2. **URL 접근성**: 사용자 PC에서 update.xml에 접근 가능해야 합니다
3. **방화벽**: 필요시 방화벽 설정 확인
4. **HTTPS 권장**: 보안을 위해 HTTPS 사용 권장
5. **백업**: 업데이트 전 현재 버전 백업 권장

## 테스트 방법

### 로컬 테스트
1. 로컬 웹 서버 실행 (IIS Express 등)
2. update.xml을 로컬 서버에 배치
3. App.xaml.cs에서 로컬 URL 사용
4. 애플리케이션 실행 및 테스트

### 버전 다운그레이드 테스트
1. WpfApp2.csproj 버전을 1.0.0으로 설정
2. 빌드 후 실행
3. update.xml의 버전을 1.0.1로 설정
4. 업데이트 동작 확인

## 트러블슈팅

### 업데이트가 체크되지 않을 때
- update.xml URL 접근 가능 여부 확인
- 방화벽/백신 프로그램 확인
- XML 형식 오류 확인

### 업데이트 다운로드 실패
- ZIP 파일 URL 확인
- 파일 권한 확인
- 네트워크 연결 확인

### 업데이트 후 실행 안됨
- ZIP 파일에 모든 필요한 파일 포함 확인
- .NET 8 런타임 설치 확인
- 압축 해제 경로 확인

## 고급 설정

### 선택적 업데이트
```csharp
AutoUpdater.Mandatory = false;
AutoUpdater.ShowSkipButton = true;
AutoUpdater.ShowRemindLaterButton = true;
```

### 업데이트 체크 주기 설정
```csharp
System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
timer.Interval = TimeSpan.FromHours(2);
timer.Tick += (s, args) => 
{
    AutoUpdater.Start("https://yourdomain.com/updates/update.xml");
};
timer.Start();
```

### 베타 버전 관리
```xml
<!-- update.xml -->
<item>
  <version>1.1.0.0</version>
  <url>https://yourdomain.com/updates/WpfApp2_v1.1.0_beta.zip</url>
  <mandatory>false</mandatory>
</item>
```
