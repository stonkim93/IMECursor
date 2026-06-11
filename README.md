# 🌐 IMECursor Color Changer

**IMECursor**는 윈도우 환경에서 현재 입력 중인 언어(영어, 한글, Pali어)와 대소문자 상태(IME)를 마우스 포인터와 트레이 아이콘의 색상으로 직관적으로 알려주는 고성능 유틸리티입니다. 특히 문서 작업이 많은 환경(Excel, 아래한글)에서는 포인터 하단에 미니 인디케이터(작은 원)를 추가로 표시하여 작업 효율을 극대화합니다.

\---

## ✨ 주요 기능 (Key Features)

### 1\. 5가지 입력 상태별 맞춤형 테마 지원

입력 모드가 변경되면 마우스 포인터(화살표, I-Beam, Cross)와 작업 표시줄 트레이 아이콘이 즉각적으로 변환됩니다.

|입력 상태 (IME State)|포인터/아이콘 색상|트레이 글자|
|-|:-:|:-:|
|**영어 소문자 (English Lower)**|⚪ White|**e**|
|**영어 대문자 (English Upper)**|🔹 DeepSkyBlue|**E**|
|**한국어 (Hangul)**|🔴 Red|**K**|
|**팔리어 소문자 (Pāḷi Lower)**|🔸 Orange|**p**|
|**팔리어 대문자 (Pāḷi Upper)**|🟢 Lime|**P**|

### 2\. 특정 오피스 앱 타겟 '미니 인디케이터'

* **대상 프로세스:** Microsoft Excel (`excel.exe`), 한글과컴퓨터 아래한글 (`hwp.exe`)
* **동작:** 해당 프로그램이 활성화되면 마우스 커서 우측 하단에 정밀한 레이어드(Layered) 구조의 \*\*작은 원(Dot)\*\*이 나타나 현재 입력 상태를 동적으로 추적합니다.

### 3\. 시스템 설정 동기화 (Dynamic Scaling)

* 사용자의 윈도우 '마우스 포인터 크기' 변경 및 디스플레이 DPI 배율을 실시간으로 감지합니다.
* 커서가 커지더라도 기하학적 형태나 외곽선 두께가 깨지지 않고 고해상도로 자동 렌더링되며, 미니 인디케이터의 오프셋 위치도 비례하여 조정됩니다.

\---

## ⚡ 기술적 특징 및 최적화 (Technical Highlights)

> \*\*Production-Ready 이 앱은 초경량, 고성능 작동을 목표로 가혹하게 최적화되었습니다.\*\*

* **Zero GC Pressure (가비지 컬렉션 압박 전면 제거)**
마우스 이동 시(15ms 주기) 비트맵을 반복 생성하던 레거시 방식을 탈피했습니다. 입력 상태가 **'변경될 때만'** GDI+ 리소스를 단 한 번 메모리 DC에 프리렌더링(Pre-rendering)한 후, 이동 시에는 순수 Win32 API 비트 블릿(`UpdateLayeredWindow`) 처리만 수행하여 **CPU 및 메모리 점유율을 0%에 수렴**시켰습니다.
* **정밀한 다단계 IME 폴백(Fallback) 엔진**
단순한 API 호출로는 감지하기 어려운 아래한글(HWP) 고유의 내장 IME 상태 및 복잡한 MDI(다중 문서 인터페이스) 포커스 전환을 `GetGUIThreadInfo` 및 `WM\_IME\_CONTROL` 메시지 크롤링을 통해 100% 정확하게 판별합니다.
* **Native Win32 결합 \& 자원 누수 방지**
.NET 8의 최신 `\[LibraryImport]`를 활용하여 마샬링 오버헤드를 줄였으며, 프로그램 종료 시 예외가 발생하더라도 윈도우 원래의 기본 커서를 복구(`SPI\_SETCURSORS`)하고 커널 객체를 확실하게 해제하도록 설계되었습니다.

\---

## 🚀 시작하기 (Getting Started)

### ⚙️요구 사항

* **OS:** Windows 10 / Windows 11 (64-bit)
* **Runtime:** [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 이상
* **Language:**\*\* C# 12
* **IDE:** \*\* Visual Studio 2022

### ⚙️실행 방법

1. 본 레포지토리를 클론합니다.

```bash
git clone https://github.com/stonkim93/IMECursor.git

2\. Visual Studio 2022에서 IMECursor.csproj를 열고 빌드(Release 모드 추천)합니다.

3\. 빌드된 IMECursor.exe를 실행하면 시스템 트레이에서 즉시 작동합니다.

\*\* 중복 실행 방지(Mutex)가 적용되어 있어 안전하게 백그라운드에서 상주합니다.

\# POWERSHELL 실행후 입력하여 배포판 만들기 (.net runtime 전체 포함된, self-contained)

D:\\FORTRAN\\IMECursor> dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

\# POWERSHELL 실행후 입력하여 배포판 만들기 (framework-dependent, Windows 10/11에는 .NET 8이 기본 내장됨)

D:\\FORTRAN\\IMECursor> dotnet publish -c Release --self-contained false /p:PublishSingleFile=true

\### 실행 파일 다운로드

오른쪽의 \*\*\[Releases]\*\* 탭에서 최신 버전의 `.zip` 파일을 다운로드한 뒤, 압축을 풀고 `IMECursor.exe`를 실행하시면 즉시 시스템 트레이에 상주하며 작동합니다.

IMECursor.zip --> .net8 설치된 윈도우용 (파일 사이즈 작음)

IMECursor\_with\_dot\_net8.zip  --> .net8 미설치 윈도우용 (파일 사이즈 큼)

### ⚙️사용 팁

트레이 메뉴 기능: 시스템 트레이의 아이콘을 우클릭(또는 좌클릭)하면 현재 상태 브리핑 및 엑셀/한글 화면에서 '작은 원 표시 여부'를 실시간으로 끄고 켤 수 있는 토글 옵션을 제공합니다.

\### Windows US+Pali(unicode) 키보드 설치방법

https://www.tipitaka.org/keyboard.html

US+Pali Unicode 입력기에서 한글-Pali 변경 : control + shift

\### 한글2020에서 윈도우 MS IME 사용하기

한글 2020 실행 후 상단 메뉴에서 도구 ➔ 글자판 ➔ 글자판 바꾸기를 클릭합니다. (단축키: Alt + F2)

'글자판 바꾸기' 창에서 현재 글자판을 한국어 대신 윈도우 입력기로 변경합니다.

설정을 저장하고 나오면, 이제 HWP에서도 커서 프로그램이 MS IME의 한/영 상태를 정확하게 감지하여 색상을 실시간으로 변경합니다.
