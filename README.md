# EzCap

Windows에서 `Ctrl+Shift+C`를 누르고 마우스로 직사각형을 드래그하면 해당 화면 영역이 이미지로 클립보드에 즉시 복사되고 편집 창이 열립니다. 캡처 중 `Esc` 또는 오른쪽 클릭으로 취소할 수 있습니다. 실행 중에는 알림 영역의 아이콘에서 캡처하거나 종료할 수 있습니다.

편집 창이 열린 상태에서 새 캡처를 시작하면 기존 창을 닫고 새 캡처를 진행합니다. 입력 중이던 글자는 닫기 전에 이력에 반영됩니다.

편집 창에서 사각형을 드래그한 뒤 바로 글자를 입력하면 사각형 안에 투명 배경으로 미리 보입니다. 글자를 비워 두면 테두리만 그려집니다. `Ctrl+Enter` 또는 캔버스를 클릭하면 테두리와 글자가 함께 확정됩니다. 배경은 기본적으로 투명하며 **배경: 투명** 메뉴에서 색을 선택하거나 다시 투명으로 돌릴 수 있습니다. 글자·테두리 색과 선 굵기도 바꿀 수 있고, `Ctrl+Z`로 마지막 편집을 취소할 수 있습니다. 편집한 결과는 **클립보드 복사** 또는 `Ctrl+C`로 다시 복사하거나 **PNG 저장**으로 저장하세요.

캡처 이력은 편집 창 아래쪽에 표시됩니다. 항목을 선택하면 이전 캡처를 다시 열 수 있습니다. 원본 및 편집 결과는 `%ProgramData%\EzCap`에 PNG로 저장되며, 프로그램을 다시 시작할 때 이전 세션 파일을 지웁니다. 이력 개수 제한은 없습니다. 이 폴더는 현재 사용자와 Windows SYSTEM 계정만 접근하도록 설정됩니다.

이력에서 큰 이미지를 열면 화면에 맞는 범위까지 편집 창이 커집니다. 작은 이미지는 보기 영역의 가운데에 표시되고 나머지 영역은 비워 둡니다. 화면보다 큰 이미지는 스크롤해 볼 수 있습니다.

## 실행

```powershell
dotnet run --project EzCap.csproj
```

배포용 파일을 만들려면:

```powershell
dotnet publish EzCap.csproj -c Release -r win-x64 --self-contained false -p:PublishDir=release\v0.1.6\
```

`release/v0.1.6`의 실행 파일과 `.dll`, `.deps.json`, `.runtimeconfig.json`을 같은 폴더에 두어야 합니다. 실행할 컴퓨터에는 .NET 9 Desktop Runtime이 필요합니다. 이 폴더의 `EzCap.exe`를 실행하세요.

현재 사용자 계정에서 Windows 로그인 시 자동 실행하려면 PowerShell에서 다음을 실행합니다.

```powershell
$exe = (Resolve-Path 'release\v0.1.6\EzCap.exe').Path
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'EzCap' -Value ('"{0}"' -f $exe)
```

다른 프로그램이 `Ctrl+Shift+C`를 이미 등록했다면 트레이 알림이 표시됩니다. 해당 프로그램의 단축키를 해제하거나 트레이 메뉴에서 캡처하세요.

