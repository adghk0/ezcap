# EzCap

Windows에서 `Ctrl+Shift+C`를 누르고 마우스로 직사각형을 드래그하면 해당 화면 영역이 이미지로 클립보드에 즉시 복사되고 편집 창이 열립니다. 캡처 중 `Esc` 또는 오른쪽 클릭으로 취소할 수 있습니다. 실행 중에는 알림 영역의 아이콘에서 캡처하거나 종료할 수 있습니다.

편집 창에서 사각형을 드래그하면 그 안에 글자를 바로 입력할 수 있습니다. 글자를 비워 두면 테두리만 그려집니다. `Ctrl+Enter` 또는 다른 곳을 클릭하면 테두리와 글자가 함께 확정됩니다. 색상과 선 굵기를 바꿀 수 있고, `Ctrl+Z`로 마지막 편집을 취소할 수 있습니다. 편집한 결과는 **클립보드 복사** 또는 `Ctrl+C`로 다시 복사하거나 **PNG 저장**으로 저장하세요.

## 실행

```powershell
dotnet run --project EzCap.csproj
```

배포용 파일을 만들려면:

```powershell
dotnet publish EzCap.csproj -c Release -r win-x64 --self-contained false -p:PublishDir=release\v0.1.0\
```

`release/v0.1.0`의 실행 파일과 `.dll`, `.deps.json`, `.runtimeconfig.json`을 같은 폴더에 두어야 합니다. 실행할 컴퓨터에는 .NET 9 Desktop Runtime이 필요합니다. 이 폴더의 `EzCap.exe`를 실행하세요.

현재 사용자 계정에서 Windows 로그인 시 자동 실행하려면 PowerShell에서 다음을 실행합니다.

```powershell
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'EzCap' -Value '"D:\Project\ezcap\release\v0.1.0\EzCap.exe"'
```

다른 프로그램이 `Ctrl+Shift+C`를 이미 등록했다면 트레이 알림이 표시됩니다. 해당 프로그램의 단축키를 해제하거나 트레이 메뉴에서 캡처하세요.

