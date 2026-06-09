POWERSHELL 실행후 입력하여 배포판 만들기 (.net runtime 전체 포함된, self-contained)
D:\FORTRAN\IMECursor> dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

POWERSHELL 실행후 입력하여 배포판 만들기 (framework-dependent, Windows 10/11에는 .NET 8이 기본 내장됨)
D:\FORTRAN\IMECursor> dotnet publish -c Release --self-contained false /p:PublishSingleFile=true


US+Pali Unicode 입력기에서 한글-Pali 변경 : control + shift

한글2020에서 윈도우 MS IME 사용하기
한글 2020 실행 후 상단 메뉴에서 도구 ➔ 글자판 ➔ 글자판 바꾸기를 클릭합니다. (단축키: Alt + F2)
'글자판 바꾸기' 창에서 현재 글자판을 한국어 대신 윈도우 입력기로 변경합니다.
설정을 저장하고 나오면, 이제 HWP에서도 커서 프로그램이 MS IME의 한/영 상태를 정확하게 감지하여 색상을 실시간으로 변경합니다.
