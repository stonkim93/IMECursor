<div align="center">

# 🌐 IMECursor

### Windows IME 상태를 마우스 포인터와 트레이 아이콘으로 직관적으로 보여주는 고성능 유틸리티

![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows11&logoColor=white)
![Framework](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-yellow.svg)
![Status](https://img.shields.io/badge/status-Production--Ready-2E8B57)

</div>


**IMECursor**는 윈도우 환경에서 현재 입력 중인 언어와 대소문자 상태(IME)를 **마우스 포인터**와 **트레이 아이콘**의 색상 변화를 이용하여 직관적으로 알려주는 유틸리티 앱입니다. 특히 자체적인 포인터를 사용하는 앱(Excel, 아래한글)에서는 포인터 우측 하단에 작은 원를 추가로 표시하여 입력 상태를 확인합니다.

영어, 한국어 뿐만 아니라 **빨리어(Pāḷi)** 입력 상태까지 완벽하게 지원합니다.

<br>

## ✨ 주요 기능 (Key Features)

### 1️⃣ 5가지 입력 상태별 색상 테마 지원

입력 모드가 변경되면 마우스 포인터(화살표 or I-Beam)와 작업 표시줄의 트레이 아이콘의 색깔이 **즉각적으로** 변합니다.

<div align="center">

| 입력 상태 (IME State) | 포인터/아이콘 색상 | 트레이 글자 |
|:---|:---|:---:|
| **영어 소문자** (English Lower) | $\color{gray}\Large\blacktriangle$ White | $\color{gray}\large\boldsymbol{e}$|
| **영어 대문자** (English Upper) | $\color{DeepSkyBlue}\Large\blacktriangle$ DeepSkyBlue | $\color{deepskyblue}\large\textbf{E}$|
| **한국어** (Hangul) | $\color{red}\Large\blacktriangle$ Red | $\color{red}\large\textbf{K}$|
| **빨리어 소문자** (Pāḷi Lower) | $\color{orange}\Large\blacktriangle$ Orange | $\color{orange}\large\boldsymbol{p}$|
| **빨리어 대문자** (Pāḷi Upper) | $\color{lime}\Large\blacktriangle$ Lime | $\color{lime}\large\textbf{P}$|

</div>

### 2️⃣ 엑셀과 한글에서 포인터 하단에 작은원(mini indicator) 표시하기

- 마우스 포인터를 자체적으로 관리하는 앱(엑셀, 한글)에서는 포인터 우측 하단에 '작은 원'을 생성하고 입력상태에 따라 작은 원의 색상을 변경합니다.

    ⚠️ Microsoft Excel (`excel.exe`)의 셀 위에서는 포인터가 엑셀이 자체 관리하는 CROSS 형태로 바뀌고 색상 변경이 어렵다.

    ⚠️ 한글과컴퓨터 아래한글 (`hwp.exe`)의 텍스트 입력창 안에서는 포인터가 한글이 자체 관리하는 I-BEAM 형태로 바뀌고 색상 변경이 어렵다.

- 시스템 트레이의 아이콘을 **우클릭**(또는 좌클릭)하면, 1) 현재 입력상태 표시, 2) **엑셀/한글 작은원 표시** 활성화 옵션, 3) 종료(Exit) 버튼을 제공합니다.


<div align="center">

![alt text](Indicator.png)

</div>

## 💡 사용팁 (Tips)

### 1️⃣ 빨리어(Pali) 키보드 설치하고 사용하기

* Windows US+Pali(Unicode) 키보드 설치 방법 : `https://www.tipitaka.org/keyboard.html`

* 한국어(MS IME) ↔ Pali어 빠른 전환 : <kbd>Ctrl</kbd> + <kbd>Shift</kbd>

* 자판 목록에서 순환적으로 Pali어 선택하기 : <kbd>WIN</kbd> + <kbd>Space</kbd>

* Pali어 글자배열 및 입력방법 : <kbd>한/영키(Right Alt)</kbd> + <kbd> (a, s, d), (G, H), (l, m, n), (r, t, Y, u, i) </kbd>

<div align="center">

![alt text](PaliKeyboard.png)

</div>


### 2️⃣ 한글 2020에서 윈도우 MS IME 사용하기

> 📌 [TIP]
> 한글과컴퓨터의 자체 입력기 대신 MicroSoft IME를 사용하도록 전환하면, 한글에서도 이 프로그램이 입력 상태를 정확히 보여줍니다.

* 한글 2020 실행 후 상단 메뉴에서 `도구 ➔ 글자판 ➔ 글자판 바꾸기`를 클릭합니다. (단축키: <kbd>Alt</kbd> + <kbd>F2</kbd>)
* **글자판 바꾸기** 창에서 현재 글자판 종류를 **한국어** 대신 **윈도우 입력기**로 변경합니다.
* **글자판 자동 변경**을 해제하여 항상 윈도우 설정을 따르도록 저장하고 나옵니다.
* 트레이 아이콘을 클릭하여, **엑셀/한글 작은원 표시**가 체크되어 있으면, 입력상태를 시각적으로 구분하기 쉽습니다.

### 3️⃣ 윈도우 시작프로그램에 추가하기

* 윈도우 실행창(run)을 띄운다 : <kbd>WIN</kbd> + <kbd>R</kbd>
* 윈도우 시작프로그램 폴더를 연다 : `shell:startup`
* IMECursor.exe 바로가기 파일을 생성하여 시작프로그램 폴더에 붙여넣는다.
* IMECursor 실행후 숨겨진 아이콘 박스에 포함된 경우, 작업표시줄로 끄집에 내어 MS IME 옆에 놓으면 시각적으로 도움이 된다.


<br>


## 🏃 초보 개발자를 위한 정보

### ⚙️ 요구 사항

| 항목 | 내용 |
|:---|:---|
| 🖥️ **OS** | Windows 10 / Windows 11 (64-bit) |
| 🧩 **Runtime** | [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 이상 |
| ⌨️ **Language** | C# 12 / 13 |
| 🛠️ **IDE** | Visual Studio 2022 / 2026 |


### 1️⃣ 레포지토리 클론

```bash
git clone https://github.com/stonkim93/IMECursor.git
```

### 2️⃣ 빌드 & 배포판 만들기

Visual Studio에서 `IMECursor.csproj`를 열고 빌드합니다.

* **프레임워크 의존형 (Framework-dependent, 소용량)**

```bash
dotnet publish -c Release --self-contained false /p:PublishSingleFile=true
```

* **.NET 런타임 전체 포함형 (Self-contained, 대용량)**

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

### 3️⃣ 2가지 실행 파일 다운로드

오른쪽의 **[Releases]** 탭에서 최신 버전의 `.zip` 파일을 다운로드 하고 압축을 해제합니다.

| 파일명 | 용도 | 파일 크기 |
|:---|:---|:---:|
| [`IMECursor.zip`](https://github.com/stonkim93/IMECursor/releases/download/v1.0.0/IMECursor.zip) | .NET10 기설치 PC | Small ( 0.25 MB ) |
| [`IMECursor_with_dotnet10.zip`](https://github.com/stonkim93/IMECursor/releases/download/v1.0.0/IMECursor_with_dotnet10.zip) | .NET10 미설치 PC | Big ( 43.8 MB ) |

### 4️⃣ 실행하기

`IMECursor.exe`를 실행하면 시스템 트레이에서 즉시 작동합니다.

> 📌 [IMPORTANT]
> 중복 실행 방지(`Mutex`)가 내장되어 있어 안전하게 백그라운드에서 상주합니다.

<br>


## ⚡ 기술적 특징 및 최적화 (Technical Highlights)

> 📌 [NOTE]
> 이 앱은 백그라운드에서 365일 실행되어도 시스템에 전혀 무리를 주지 않도록, 초경량·고성능을 목표로 가혹하게 최적화되었습니다.

### 🎨 1. 해상도 및 배율에 반응하는 반응형 시각화 (Dynamic Scaling)

* **마우스 포인터 크기 동기화:** 사용자가 윈도우 설정에서 마우스 포인터 크기를 키우거나 모니터 해상도(DPI)를 변경하면 이를 즉시 감지합니다. 

* **이미지 깨짐 방지:** 커서가 커지더라도 단순히 이미지를 늘리지 않고, 변경된 배율에 맞춰 기하학적 형태와 외곽선 두께를 **고해상도로 다시 계산하여 생성**하므로 언제나 선명한 커서를 제공합니다. 작은 원의 위치도 비례하여 정확히 이동합니다.

### 🧠 2. 문맥을 놓치지 않는 스마트 입력 감지 (Smart Context Tracking)

* **어떤 환경도 뚫어내는 3중 감지 엔진:** 메모장, 엑셀, 게임, 금융 보안 프로그램 등은 글자를 입력받는 방식이 모두 다릅니다. 이를 해결하기 위해 
① 하드웨어 키보드 신호 직접 가로채기, 
② 입력창에 직접 상태 질의하기, 
③ 구형 윈도우 API 우회 호출이라는 
**3단계 안전장치**를 거쳐 어떤 악조건 속에서도 한글/영어/빨리어 상태를 정확히 짚어냅니다.

* **작업표시줄 포커스 기억 (Context Sync):** 바탕화면이나 작업표시줄을 클릭했다가 다시 문서로 돌아오면 언어가 꼬이는 윈도우 고유의 버그를 차단합니다. **방금 전까지 쓰던 창과 언어 상태를 프로그램이 기억**해두었다가, 문서로 돌아오는 즉시 원래 상태를 강제로 주입하여 끊김 없는 타이핑 환경을 보장합니다.

### 🚀 3. 메모리 낭비 제로, 극한의 성능 최적화 (Zero GC & Resource Management)

* **마우스 끊김 원천 차단 (Zero GC):** 0.015초마다 마우스를 감지하면서 메모리 쓰레기를 만들면 화면이 뚝뚝 끊기게 됩니다. 이를 막기 위해 임시 공간(Stack)만 사용하고 즉시 비워버리는 특수 설계를 적용하여, 시스템 청소부(가비지 컬렉터)가 개입할 여지를 없앴습니다. 덕분에 **마우스가 단 1ms도 끊기지 않고 부드럽게 움직입니다.**

* **철저한 자원 누수 차단:** 수없이 색상이 바뀌며 생성되는 마우스 커서 이미지를 사용 직후 완벽하게 파괴(`DeleteObject`)하도록 설계하여, 몇 달을 켜놓아도 컴퓨터 가상 메모리를 갉아먹거나 PC가 느려지지 않습니다.

### 🛡️ 4. 외부 충돌 및 오류에 대비한 철벽 안전망 (Bulletproof Safety)

* **모니터 변경 시 튕김 방지 (Thread-Safe):** 듀얼 모니터를 연결하거나 화면 구조가 바뀔 때, 마우스 상태를 감지하는 작업자와 이미지를 새로 그리는 작업자가 충돌하여 프로그램이 꺼지는 현상(레이스 컨디션)을 차단하는 **교통정리 로직**이 탑재되어 있습니다.

* **강제 종료 시 자동 복구:** 예기치 못한 에러나 업데이트로 프로그램이 강제 종료되더라도, 시스템 깊숙이 심어둔 안전망이 작동하여 찰나의 순간에 **윈도우 원래의 하얀색 마우스 커서로 자동 복구**해 놓고 장렬히 전사합니다. 마우스 모양이 먹통이 되는 최악의 낭패를 막아줍니다.

<br>


## 📜 라이선스 (License)

이 프로젝트는 **MIT License**에 따라 자유롭게 수정 및 배포할 수 있습니다.

Made with ❤️ for multilingual writers, researchers, and Pāḷi scholars

<br>

❤️🌐✨⚡🚀💡🎯❌⚠️🏆🏃🛠️⚙️🎨🧩🐛📐➡️🆕🖥️💻⌨️🔤🖱️🔗🔍✅📜📝📌1️⃣2️⃣3️⃣4️⃣⚪🟡🟢🔵🛡️🧠📦🔹🔸🔺🔻
