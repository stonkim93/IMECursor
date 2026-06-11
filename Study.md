<h1 align="center">🖱️ IMECursor App 완전 분석 노트</h1>

<p align="center">
<img alt="C#" src="https://img.shields.io/badge/Language-C%23-512BD4">
<img alt="Windows11" src="https://img.shields.io/badge/Platform-Windows%2011-0078D4">
<img alt="Win32 API" src="https://img.shields.io/badge/API-Win32%20%2F%20P%2FInvoke-FF8C00">
<img alt="Status" src="https://img.shields.io/badge/Status-Deep%20Dive-2E8B57">
</p>

<p align="center">
<sub>🎨 색상 가이드&nbsp;&nbsp;|&nbsp;&nbsp;
<span style="color:#C0392B"><b>빨강</b></span> = 핵심 경고·주의&nbsp;&nbsp;|&nbsp;&nbsp;
<span style="color:#1F618D"><b>파랑</b></span> = 타이밍·핵심 수치&nbsp;&nbsp;|&nbsp;&nbsp;
<span style="color:#8E44AD"><b>보라</b></span> = 코드 상수값&nbsp;&nbsp;|&nbsp;&nbsp;
<span style="color:#1E8449"><b>초록</b></span> = 안전장치·완료
</sub>
</p>

---

## 📦 1. IMECursor App의 구조

> 📝 **이 장에서 다루는 내용:** 프로그램을 구성하는 4개 클래스의 역할, 15ms 타이머가 굴러가는 핵심 흐름, 그리고 GC에 의존하지 않는 수동 메모리 관리 전략

> 이 프로그램은 겉보기에는 단순한 유틸리티 같지만, 내부적으로는 <span style="color:#C0392B"><b>초당 60회 이상</b></span> 윈도우 커널과 통신하며 <span style="color:#C0392B"><b>메모리 누수 없이</b></span> 그래픽 자원을 관리해야 하는 **고도로 최적화된 코드**입니다.

🔀 이벤트 구동 방식의 **Visual Basic**, 객체 지향 및 메모리 제어가 필수적인 **C++**, 그리고 절차적이고 계산 지향적인 **Fortran**의 패러다임이 **C#**이라는 모던 언어 안에서 어떻게 융합되어 있는지, 내부 구조와 핵심 로직을 분해해 드립니다.

### 🏗️ 1.1 애플리케이션 전체 구조 (Architecture)

코드는 역할에 따라 크게 **4개의 영역**으로 철저하게 분리되어 있습니다.

| 🧩 영역 | 🎯 역할 | 💬 비유 |
|:---|:---|:---|
| 🚪 **`Program`** | 진입점 | Fortran/C·C++의 `main()` |
| 🖼️ **`MainForm`** | 이벤트 루프 & 렌더링 | Visual Basic의 Form |
| 🧠 **`ImeState`** | 상태 감지 엔진 | 입력상태를 판독하는 '두뇌' |
| 🌉 **`NativeMethods`** | Win32 API 브릿지 | C 라이브러리 직접 호출 통로 |

- 🚪 **`Program` 클래스 (진입점):** Fortran이나 C/C++의 `main()` 함수 역할을 합니다. `Mutex`를 사용해 프로그램이 두 개 이상 실행되지 않도록 차단하고, `MainForm`을 백그라운드에서 실행시킵니다.

- 🖼️ **`MainForm` 클래스 (이벤트 루프 및 렌더링):** Visual Basic의 폼(Form)과 같지만, 화면에 보이지 않는 **투명한 상태**(`Size = 16x16`, `Location = -100, -100`)로 존재합니다. <span style="color:#1F618D"><b>15ms 주기</b></span>의 타이머 이벤트를 발생시키며, 시스템 트레이 아이콘과 미니 인디케이터(작은 원)의 그래픽 자원을 생성하고 관리합니다.

- 🧠 **`ImeState` 클래스 (상태 감지 엔진):** 현재 활성화된 윈도우를 추적하고, 윈도우 커널에 직접 질의하여 현재 입력 상태(영어, 한글, 빨리어)를 판독하는 <u>**'두뇌'**</u> 역할을 합니다.

- 🌉 **`NativeMethods` 클래스 (Win32 API 브릿지):** C#은 기본적으로 안전한 관리형(Managed) 환경에서 동작하지만, 이 클래스를 통해 C언어로 작성된 윈도우 OS의 핵심 라이브러리(`user32.dll`, `gdi32.dll` 등) 함수들을 **직접 호출(P/Invoke)**합니다.

### 💓 1.2 핵심 로직 흐름 (The Heartbeat)

프로그램의 생명주기는 다음 구조를 가집니다.

<p align="center"><b>🎨 사전 렌더링 (Pre-rendering) ➔ 🔁 무한 감지 루프</b></p>

| 단계 | 함수 | 핵심 동작 |
|:---:|:---|:---|
| 1️⃣ | `BakeAllAssets` | 5가지 입력 상태의 커서·아이콘을 메모리에 미리 그려둠 |
| 2️⃣ | `StateTimer_Tick` | <span style="color:#1F618D"><b>15ms</b></span>마다 활성 창 핸들을 확인 |
| 3️⃣ | `ImeState.Detect` | 키보드 레이아웃·IME 컨텍스트로 최종 상태 판독 |
| 4️⃣ | `ApplyState` | 상태 변경분만 커서·인디케이터에 반영 |

1️⃣ **초기화 및 렌더링 (`BakeAllAssets`)**
프로그램이 켜지면 **5가지 입력 상태**(영어 대/소문자, 한글, 빨리어 대/소문자)에 해당하는 모든 마우스 커서와 트레이 아이콘 이미지를 메모리에 미리 그려둡니다(Baking).
> ⚠️ <span style="color:#C0392B">루프 안에서 매번 그림을 그리면 CPU 점유율이 치솟기 때문입니다.</span>

2️⃣ **타이머 루프 (`StateTimer_Tick`)** — <span style="color:#1F618D"><b>15ms(약 0.015초)</b></span>마다 실행되는 심장 박동
   - `GetForegroundWindow()`로 현재 사용자가 클릭한 창의 핸들(ID)을 가져옵니다.
   - 해당 창이 '작업표시줄'인지 '일반 앱'인지 구분하여 상태 동기화 및 예외 처리를 수행합니다.

3️⃣ **상태 판독 (`ImeState.Detect`)**
대상 창의 스레드 ID를 추출하고, 키보드 레이아웃(빨리어 여부)과 IME 컨텍스트(한글 여부)를 교차 검증하여 최종 상태를 반환합니다.

4️⃣ **시각적 업데이트 (`ApplyState` & `UpdateLayeredIndicator`)**
상태가 이전과 달라졌을 때만 윈도우 기본 커서를 미리 그려둔 커서로 바꿔치기하고, 엑셀이나 한글 앱인 경우 마우스 포인터 좌표를 추적하여 미니 인디케이터를 투명 윈도우 레이어 위에 겹쳐서 그립니다.

### 🧹 1.3 메모리 관리 (가비지 컬렉터의 맹점 극복)

이 앱의 최적화 핵심은 <span style="color:#C0392B">**가비지 컬렉터(GC)를 맹신하지 않는 것**</span>에 있습니다.

`Bitmap`, `Graphics`, `IntPtr`(핸들) 등의 객체는 윈도우 OS의 GDI+ 자원을 소모합니다. C#의 GC는 Managed 메모리 영역만 감시하므로, OS 내부의 비관리(Unmanaged) 자원이 얼마나 고갈되고 있는지 모릅니다.

> 🛑 따라서 `MainForm.Dispose()`나 `CleanUpIndicatorGdi()` 함수 내부를 보면 `DeleteObject`, `DestroyCursor`, `DeleteDC` 같은 **C/C++ 스타일의 API 호출**을 통해 수동으로 윈도우 자원을 파괴하고 있습니다. 이는 장시간 켜두는 백그라운드 유틸리티에서 프로그램이 뻗는(Crash) 현상을 막기 위한 <span style="color:#1E8449">**필수 방어 로직**</span>입니다.

---

## 🪟 2. Windows 11의 내부 작동 원리

> 📝 **이 장에서 다루는 내용:** 활성 창을 찾는 방법부터 IME 상태 판독, 전역 커서 교체, 레이어드 윈도우로 작은 원을 그리는 방식까지 — OS 레벨에서 일어나는 5단계 파이프라인

이 프로그램은 윈도우 11 환경에서 운영체제의 커널 및 서브시스템(`User32`, `GDI32`, `Imm32`)과 매우 긴밀하게 통신하며 동작합니다. 윈도우 11이 내부적으로 입력 포커스와 그래픽을 처리하는 메커니즘을 **5가지 핵심 단계**로 설명합니다.

| 단계 | 제목 | 핵심 API |
|:---:|:---|:---|
| 2.1 🎯 | 타겟 윈도우 찾기 | `GetForegroundWindow` |
| 2.2 🧵 | 포커스 스레드 추적 | `GetGUIThreadInfo` |
| 2.3 ⌨️ | IME 판독 | `GetKeyboardLayout`, `ImmGetDefaultIMEWnd` |
| 2.4 🖱️ | 시스템 커서 교체 | `SetSystemCursor` |
| 2.5 ⚪ | 작은원 그리기 | `UpdateLayeredWindow` |

### 🎯 2.1 타겟 윈도우 찾기 (Active Window Capture)

윈도우 11에서 사용자가 어떤 창을 클릭하여 타이핑을 시작하면, 프로그램은 15ms 주기의 타이머 안에서 가장 먼저 현재 최상위에 활성화된 창의 핸들(ID)을 구합니다.

- 🔧 **코드 구현:** `NativeMethods.GetForegroundWindow()` 함수를 호출하여 활성 창의 `IntPtr`(C++의 `HWND` 혹은 `void*` 포인터에 해당)을 가져옵니다.

- 🧹 **윈도우 내 가로채기 및 필터링:**
  - 사용자가 작업 표시줄을 누르거나 바탕화면을 누를 때 발생하는 시스템 포커스 노이즈를 필터링해야 합니다.
  - `IsTaskbarOrSystemWindow` 함수 내부에서 C++ 스타일의 고속 포인터 제어인 `stackalloc char[256]`을 수행하여, OS 커널에 해당 창의 클래스 이름(`GetClassName`)을 질의합니다.
  - 이름이 `Shell_TrayWnd`(작업 표시줄)나 `Progman`(바탕화면)인 경우 **시스템 영역**으로 판정합니다. 🚫

### 🧵 2.2 정확한 포커스 스레드 추적 (Thread & Focus Tracking)

윈도우 11의 오피스 프로그램들은 내부 구조가 복잡한 다중 창(MDI) 구조이거나 브라우저 기반 렌더링을 사용하여, 단순히 '최상위 창의 핸들'만 가지고는 현재 깜빡이는 커서(Caret)의 입력 상태를 알 수 없습니다.

**⚙️ 동작 원리:**

1. `GetWindowThreadProcessId`를 호출하여 최상위 창을 소유한 윈도우 스레드 ID(Thread ID)를 알아냅니다.
2. 이 스레드 ID를 `GetGUIThreadInfo` API에 넘겨줍니다. 이 함수는 윈도우 커널이 관리하는 해당 스레드의 유저 인터페이스 구조체(`GUITHREADINFO`)를 통째로 복사해 줍니다.
3. 구조체 내부의 **`gti.hwndFocus`**(실제 포커스를 가진 자식 창 Handle) 컨트롤을 정밀하게 추출해 냅니다.

> ✅ <span style="color:#1E8449">이 과정을 거쳐야만 엑셀의 수식 입력줄이나 시트 내부 셀 각각의 포커스 변화를 정확히 따라잡을 수 있습니다.</span>

### ⌨️ 2.3 키보드 레이아웃 및 IME 판독 (Input Context Query)

실제 포커스가 맞춰진 윈도우 핸들과 스레드 ID를 찾았다면, 이제 윈도우 11 입력 서브시스템에 현재 상태를 질의합니다.

- 🟢 **빨리어(Pāḷi) 판독:**
  `GetKeyboardLayout(threadId)`를 호출하면 현재 활성화된 키보드 레이아웃 핸들(HKL)이 반환됩니다. C++의 비트 연산과 동일하게 이를 상위 16비트로 밀고(`>> 16 & 0xFFFF`) 잘라내어 디바이스 ID를 구합니다. 이 값이 빨리어 고유 코드인 <code style="color:#8E44AD"><b>0xF0C0</b></code>와 일치하는지 체크합니다.

- 🔴 **한국어(Hangul) 판독:**
  - 한국어는 `imm32.dll`이라는 윈도우 전통의 IME 모듈을 사용합니다.
  - 대상 윈도우의 기본 IME 창 핸들을 `ImmGetDefaultIMEWnd`로 구한 뒤, `SendMessageTimeout`을 통해 `WM_IME_CONTROL` 메시지를 OS에 보냅니다. 파라미터로 `IMC_GETCONVERSIONMODE`를 실어 보내면 현재 입력 상태가 비트 마스크 값으로 리턴됩니다.
  - 응답이 없거나 실패할 경우를 대비해, `ImmGetContext` ➔ `ImmGetConversionStatus` 순서로 Native API를 연속 호출하는 **폴백(Fallback) 안전망**을 거쳐 최종 상태(`IME_CMODE_NATIVE`)를 판별합니다.

### 🖱️ 2.4 시스템 커서 가로채기 및 교체 (Global Cursor Overriding)

일반적으로 커서를 바꿀 때는 내 프로그램 창 위에 마우스가 올라왔을 때만 변경(`SetCursor`)되지만, 이 앱은 백그라운드에서 **윈도우 11 시스템 전체의 커서를 강제로 교체**합니다.

**⚙️ 동작 원리:**
- 프로그램 초기화 시 5가지 상태에 맞는 커서 비트맵을 메모리에 구워둡니다(`_assetCache`).
- 상태가 바뀌면 `NativeMethods.SetSystemCursor(hNew, id)`를 호출합니다. 이 API는 윈도우 OS의 **전역 시스템 커서 레지스트리** 핸들을 메모리 상에서 직접 교체합니다.
- ⚠️ <span style="color:#C0392B">`SetSystemCursor`는 전달된 커서 핸들의 소유권을 가져간 뒤 사용 후 **파괴(Destroy)**합니다.</span> 따라서 캐시 자원을 보존하기 위해 반드시 `CopyIcon(assets.Arrow)`처럼 **복사본 핸들**을 실시간으로 생성하여 넘겨줍니다.

### ⚪ 2.5 작은원 그리기 (Layered Window Ghost Layer Rendering)

타겟 프로세스(Excel, Hwp)가 활성화되면 마우스 커서 옆에 작은 원이 출력됩니다. 화면의 다른 클릭을 방해하지 않으려면 윈도우 11의 **레이어드 윈도우(Layered Window)** 기술이 필수적입니다.

- 👻 **유령 윈도우 생성:** `MainForm`에 `WS_EX_LAYERED(0x00080000)`와 `WS_EX_TRANSPARENT(0x00000020)` 속성을 부여합니다. 시각적으로는 보이지만 마우스 클릭은 모두 통과하여 뒤에 있는 앱으로 전달됩니다.

- ⚡ **초고속 GDI 비트블릿(BitBlt):**
  - 메모리 공간에 가상 디바이스 컨텍스트(`CreateCompatibleDC`)를 만들고 GPU/CPU 공유 버퍼(`CreateDIBSection`)를 개설합니다.
  - GDI+로 버퍼에 안티앨리어싱 원을 그린 후, `UpdateLayeredWindow` API를 호출합니다. 이는 메모리 비트맵을 바탕화면 윈도우 관리자(DWM)에 밀어 넣어 오차 없이 실시간으로 출력시킵니다.

> 💡 이 일련의 과정이 <span style="color:#1F618D"><b>15ms(초당 약 66회)</b></span>라는 극도로 짧은 타이머 주기마다 유기적으로 맞물려 돌아가기 때문에, 사용자는 윈도우 환경에서 아무런 딜레이나 버벅임 없이 **즉각적인 IME 피드백**을 받을 수 있습니다.

---

## 🔄 3. C++ / VB / Fortran 개발자를 위한 핵심 C# 문법 매핑

> 📝 **이 장에서 다루는 내용:** 기존 C++·VB·Fortran 경험자가 빠르게 적응할 수 있도록, 코드에 쓰인 7가지 모던 C# 문법을 익숙한 언어 개념에 대응시켜 설명

C#은 **C++의 강력함**과 **VB의 생산성**을 합쳐놓은 형태를 띱니다. 코드에 적용된 주요 최적화 문법을 비교해 드립니다.

| # | C# 문법 | 대응 개념 (C++/VB/Fortran) |
|:---:|:---|:---|
| 3.1 | `using` | RAII / `malloc`-`free` / `ALLOCATE`-`DEALLOCATE` |
| 3.2 | `=>` | 단일 반환문 함수의 축약형 |
| 3.3 | `[LibraryImport]` | `extern "C"` / `BIND(C)` |
| 3.4 | `unsafe`, `*` | 포인터 직접 연산 |
| 3.5 | `out`, `ref` | 포인터·참조자(`&`) 전달 |
| 3.6 | `stackalloc` | 지역 배열 `char[256]` / `_alloca` |
| 3.7 | `InvokeRequired` | 크리티컬 섹션(Critical Section) |

### 3.1 🗑️ `using` 키워드 (RAII 패턴과 메모리 해제)

Fortran의 `ALLOCATE`/`DEALLOCATE`나 C의 `malloc`/`free`처럼, 메모리를 할당했다면 반드시 명시적 해제가 필요합니다. C++의 **RAII**(Resource Acquisition Is Initialization) 패턴과 유사하게, C#에서는 `using` 블록을 활용합니다.

```csharp
using Bitmap bmp = new Bitmap(32, 32);
// ... bmp 사용 ...
// 블록이 끝나는 순간 자동으로 bmp.Dispose()가 호출되어 C++의 delete처럼 자원이 즉시 환원됩니다.
```

### 3.2 ➡️ `=>` (람다 식 / Expression-bodied Members)

함수 내용이 단일 반환문일 때 중괄호와 `return`을 생략하는 **모던 C# 문법**입니다.

```csharp
// 기존 방식
public static bool IsHangul(State state) {
    return state == State.Hangul;
}

// 람다 식 활용 (코드 간결화) ✨
public static bool IsHangul(State state) => state == State.Hangul;
```

### 3.3 🔗 `[LibraryImport]`와 P/Invoke

Fortran에서 외부 C 라이브러리를 연결할 때 `BIND(C)`를 사용하거나, C++에서 `extern "C"`로 심볼을 링킹하는 것과 같은 원리입니다. C# 컴파일러에게 <u>**"이 함수는 윈도우 커널(`user32.dll`)에 있는 네이티브 C 함수임"**</u>을 명시합니다.

```csharp
[LibraryImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool SetSystemCursor(IntPtr hcur, uint id);
```

> 💡 <span style="color:#1F618D">**참고:** `[LibraryImport]`는 기존 `[DllImport]`보다 빌드 타임에 마샬링 코드를 생성하여 런타임 리플렉션 오버헤드가 없는 최신 방식입니다.</span>

### 3.4 ⚠️ `unsafe`와 Pointers (`*`)

C#은 보안상 메모리 주소 직접 제어를 피하지만, `unsafe` 블록 안에서는 C/C++처럼 **포인터 연산**을 허용하여 극강의 퍼포먼스를 낼 수 있습니다.

> 🛑 <span style="color:#C0392B">주의: `unsafe` 블록은 컴파일러의 메모리 안전성 검사를 우회하므로, 포인터 범위를 직접 책임지고 관리해야 합니다.</span>

### 3.5 📤 `out`과 `ref` 키워드

C/C++에서 포인터나 참조자(`&`)를 넘겨서 반환값을 받아오던 방식입니다.
- **`out`**: 초기화되지 않은 변수를 넘겨 함수 내부에서 값을 채워오도록 강제
- **`ref`**: 이미 값이 있는 변수의 메모리 주소를 참조로 넘김

```csharp
NativeMethods.GetCursorPos(out NativeMethods.POINT pt); // 주소를 넘겨 pt에 좌표를 받아옴
```

### 3.6 ⚡ `Span<char>`와 `stackalloc` (초고속 스택 메모리)

C/C++ 내부에서 함수 지역 변수로 `char className[256];`을 선언하거나 `_alloca`를 사용하는 것과 동일합니다. 힙(Heap) 메모리를 사용하지 않고 **스택(Stack) 영역**에 즉시 메모리를 잡았다가 함수 종료 시 바로 휘발시키므로, 15ms마다 호출되어도 가비지 컬렉터에 찌꺼기를 남기지 않습니다.

```csharp
Span<char> className = stackalloc char[256];
```

### 3.7 🛡️ 방어적 프로그래밍과 멀티스레딩 제어 (Race Condition 방지)

OS의 디스플레이 설정이 변경될 때 발생하는 이벤트(`SystemEvents.DisplaySettingsChanged`)는 UI 메인 스레드가 아닌 **시스템 백그라운드 스레드**에서 유입될 수 있습니다.

- 🔒 **`InvokeRequired`:** VB의 이벤트 처리나 C++ 멀티스레드 환경의 **크리티컬 섹션(Critical Section)**처럼, 다른 스레드에서 UI 자원에 접근하려 할 때 이를 안전하게 메인 스레드로 전달(마샬링)합니다.

- 🚧 **`try-catch (ObjectDisposedException)`:** 자원을 폐기하고 새로 생성하는 찰나의 순간에 OS 윈도우 프로시저가 아이콘 핸들을 요구할 경우 프로그램이 크래시되는 것을 막기 위한 <span style="color:#1E8449">**필수적인 방어 로직**</span>입니다.

```csharp
try {
    if (_trayIcon.Icon == null || _trayIcon.Icon.Handle != assets.TrayIcon.Handle)
        _trayIcon.Icon = assets.TrayIcon;
} catch (ObjectDisposedException) {
    _trayIcon.Icon = assets.TrayIcon; // 예외 발생 시 안전하게 롤백/덮어쓰기
}
```

> ✅ <span style="color:#1E8449">이 7가지 문법만 익히면 C++/VB/Fortran 배경의 개발자도 이 코드베이스 전체를 무리 없이 읽고 수정할 수 있습니다.</span>

---

## ⚙️ 4. APP 수정하기 (Customization Guide)

> 📝 **이 장에서 다루는 내용:** 색상·트레이 텍스트 변경부터 폴링 주기 튜닝, 인디케이터 위치 조정, 특정 앱 예외 처리, 그리고 새로운 언어 자판을 추가하는 방법까지 — 소스코드를 직접 커스터마이징하는 5가지 실전 가이드

프로그램을 사용자의 개발 환경이나 작업 스타일, 디스플레이 취향에 맞춰 변경하고 싶다면 아래의 핵심 로직 가이드를 참고하여 소스코드를 커스텀 빌드할 수 있습니다.

| 단계 | 제목 | 수정 위치 |
|:---:|:---|:---|
| 4.1 🎨 | 상태별 색상 및 트레이 텍스트 변경 | `MainForm.cs` 테마 정의 영역 |
| 4.2 ⏱️ | 타이머 감지 주기(Polling) 조정 | `MainForm.cs` 생성자 (`_timer.Interval`) |
| 4.3 🔵 | 미니 인디케이터 디자인·위치 조정 | `MainForm.cs` 렌더링 엔진 메서드 |
| 4.4 🚀 | 특정 프로그램 예외 처리 확장 | `ImeState.cs` / `MainForm.cs` 폴링 메서드 |
| 4.5 🌍 | 다른 언어 자판 추가하기 | `ImeState.cs`의 `Detect` 메서드 |

### 🎨 4.1 상태별 색상 및 트레이 텍스트 변경

각 입력 모드(영어 대/소문자, 한글, 빨리어 등)에 매핑된 고유 색상 코드와 작업 표시줄 트레이에 표시될 문자를 직관적으로 수정할 수 있습니다.

> 📍 **수정 위치:** `MainForm.cs` 내부의 테마 정의 또는 초기화 영역
>
> 🔍 **핵심 체크포인트:** GDI+의 `Color.FromArgb(Alpha, Red, Green, Blue)` 형식을 사용하여 투명도와 색상 조정을 자유롭게 제어할 수 있습니다.

```csharp
// [예시] 한국어(Hangul) 상태일 때의 인디케이터 색상을 정밀 변경하는 방법
// 기존 값에서 다른 ARGB 값 또는 시스템 정의 색상으로 대체 가능
Color hangulColor = Color.FromArgb(255, 30, 144, 255); // DodgerBlue 색상 조합
string trayTextHangul = "ㄱ"; // 트레이 아이콘에 각인될 단어 변경
```

`System.Drawing.Color` 구조체를 사용하거나, 트레이·인디케이터에 활용하기 좋은 **직관적이고 대비가 명확한 색상 코드표**입니다. 윈도우 다크 모드와 라이트 모드 모두에서 시인성이 높은 색상 위주로 구성했습니다.

| Color | 색상명 | Hex Code | RGB 값 (`System.Drawing.Color`) | 추천 매칭 |
|:---:|:---|:---:|:---|:---|
| ⚪ White | 흰색 | `#FFFFFF` | (255, 255, 255) | 영어 소문자 (현행) |
| 🔴 Red | 빨간색 | `#FF3B30` | (255, 59, 48) | 한국어 (현행) |
| 🔵 Blue | 파란색 | `#007AFF` | (0, 122, 255) | 영어 대문자 (현행) |
| 🟠 Orange | 주황색 | `#FF9500` | (255, 149, 0) | 빨리어(Pāḷi) 소문자 |
| 🟢 Green | 녹색 | `#34C759` | (52, 199, 89) | 빨리어(Pāḷi) 대문자 |
| 🟡 Yellow | 노란색 | `#FFCC00` | (255, 204, 0) | 일본어 / 중국어 등 아시아권 언어 |
| 🟣 Purple | 보라색 | `#AF52DE` | (175, 82, 222) | 프랑스어 / 독일어 등 유럽권 언어 |
| 🔘 Cyan | 청록색 | `#32D7C2` | (50, 215, 194) | 기타 다국어 확장용 |

### ⏱️ 4.2 타이머 감지 주기 (Polling Interval) 조정

현재 프로그램은 인간이 인지하기 힘든 미세한 입력 전환까지 놓치지 않기 위해 <span style="color:#1F618D"><b>15ms(약 60Hz)</b></span> 주기로 커널을 확인합니다. 고사양 연산(FEA 시뮬레이션 등)을 돌리거나 배터리 효율이 중요한 노트북 환경에서는 이 주기를 조절하여 CPU 점유율을 더욱 극적으로 낮출 수 있습니다.

> 📍 **수정 위치:** `MainForm.cs` 생성자 구조 내 타이머 세팅 영역 (`_timer.Interval`)

> ⚠️ <span style="color:#C0392B">**주의사항:** 주기를 너무 늘리면(예: 100ms 이상) 한/영 키를 누른 뒤 마우스 포인터 색상이 한 박자 늦게 바뀌는 역체감이 발생할 수 있습니다.</span> **20ms ~ 33ms(30Hz)** 사이를 권장합니다.

```csharp
// [예시] CPU 자원 소모 극소화를 위해 폴링 주기를 대폭 최적화
public MainForm()
{
    // ... 기존 초기화 코드 ...
    _timer.Interval = 25; // 15ms에서 25ms로 변경 (초당 커널 조회 횟수 최적화)
}
```

### 🔵 4.3 미니 인디케이터 디자인 및 위치 조정

문서 작업 시 마우스 화살표나 I-Beam(텍스트 입력선) 하단에 따라다니는 작은 원(Indicator)의 반경 크기 및 중심점 오프셋 좌표를 모니터 해상도(DPI)에 맞게 튜닝할 수 있습니다.

> 📍 **수정 위치:** `MainForm.cs` 내부의 렌더링 엔진 메서드 (`DrawIndicator` 또는 커서 생성 루틴)

> 💡 **팁:** 해상도가 높은 4K 모니터를 사용 중이라면 원이 너무 작아 보일 수 있으므로 반경(radius)을 조금 키워주는 것이 시인성 확보에 좋습니다.

```csharp
// [예시] 미니 인디케이터 원의 크기와 마우스 포인트로부터의 거리 제어
int indicatorRadius = 5;  // 원의 반지름 (픽셀 단위 조정)
int offsetX = 12;         // 마우스 중심점 기준 가로 격차
int offsetY = 20;         // 마우스 중심점 기준 세로 격차

// GDI+ 그리기 명령 인터페이스
g.FillEllipse(currentBrush, offsetX, offsetY, indicatorRadius * 2, indicatorRadius * 2);
```

### 🚀 4.4 특정 프로그램(Excel, 한글) 예외 처리 및 로직 확장

특정 오피스 프로그램이나 전체 화면 게임, 가상 머신 환경 등에서 마우스 포인터 색상 변경 기능이 충돌을 일으키거나 불필요할 때, 특정 프로세스 창 클래스명을 필터링하여 기능을 일시 정지하거나 고정하는 예외 로직을 추가할 수 있습니다.

> 📍 **수정 위치:** `ImeState.cs` 혹은 `MainForm.cs` 내부의 타이머 폴링 처리 메서드 최상단
>
> 🛠️ **구현 방식:** 고속 스택 메모리에 잡힌 윈도우 클래스 네임(`className`) 대조 구문을 확장합니다.

```csharp
// [예시] 현재 활성화된 창이 엑셀(XLMAIN)이거나 특정 편집기일 때 오작동 방지 필터 예시
// NativeMethods.GetClassName을 통해 가져온 문자열 데이터 비교

if (currentClassName.SequenceEqual("XLMAIN"))
{
    // 엑셀 작업 창에서는 특정 테마 색상으로 고정하거나 로직을 우회 처리
    SetDefaultCursorTheme();
    return;
}
```

### 🌍 4.5 다른 언어 자판 추가하기

중국어나 다른 외국어 글자판을 추가하여 기존의 빨리어(Pāḷi)와 동일하게 소문자는 <span style="color:#FF9500">**주황색(Orange)**</span>, 대문자는 <span style="color:#1E8449">**라임색(Lime)**</span>으로 마우스 포인터와 트레이 아이콘을 동기화하고 싶다면, 프로그램의 구조상 `ImeState` 클래스의 입력 상태 판독(`Detect`) 로직만 수정하면 됩니다.

> ✅ <span style="color:#1E8449">기존 에셋 캐시(`PaliLower`, `PaliUpper`)가 이미 주황색과 라임색으로 구워져(Baking) 있으므로, 다른 언어들도 이 상태 코드를 공유하도록 **판별 조건문만 확장**하는 것이 가장 안전하고 효율적인 방법입니다.</span>

C++의 `switch-case`문이나 Fortran의 `IF` 조건문을 확장하는 것과 같은 개념입니다. 수정 단계를 순서대로 안내해 드립니다.

#### 🛠️ 코드 수정 및 추가 순서

**1️⃣ 추가할 언어의 ID(LANGID) 파악하기**

윈도우 OS는 키보드 레이아웃(HKL)의 하위 16비트에 언어 식별자(LANGID)를 담아 리턴합니다. 윈도우 API(`GetKeyboardLayout`)를 통해 반환되는 자판 핸들(HKL)의 하위 16비트는 언어 식별자(LANGID)를 나타냅니다. 향후 새로운 언어 감지 로직을 추가할 때 조건문(`switch-case` 또는 `if`)에서 식별자로 활용할 수 있는 주요 국가별 자판 코드입니다.

| 국가 및 언어 | 자판 레이아웃 명칭 | LANGID (Hex) | LANGID (Decimal) | 비고 및 특징 |
|:---|:---|:---:|:---:|:---|
| 🇰🇷 한국어 | Korean Input System | `0x0412` | 1042 | 두벌식/세벌식 공통 (기본 한글) |
| 🇺🇸 영어 | United States (US) | `0x0409` | 1033 | 표준 쿼티(QWERTY) 자판 |
| 🇯🇵 일본어 | Japanese Input Method | `0x0411` | 1041 | MS IME (Kana/Romaji 입력형식) |
| 🇨🇳 중국어 (간체) | Chinese (PRC) | `0x0804` | 2052 | 핀인(Pinyin) 입력기 중심 |
| 🇹🇼 중국어 (번체) | Chinese (Taiwan) | `0x0404` | 1028 | 주음부호(Bopomofo) / 창힐 입력기 |
| 🇩🇪 독일어 | German (Standard) | `0x0407` | 1031 | QWERTZ 레이아웃, 움라우트(ä·ö·ü) 포함 |
| 🇫🇷 프랑스어 | French (Standard) | `0x040C` | 1036 | AZERTY 레이아웃, 악센트 기호 포함 |
| 🇪🇸 스페인어 | Spanish (Traditional) | `0x040A` | 1034 | 쿼티 기반, 물결표(ñ) 자판 배치 |
| 🇷🇺 러시아어 | Russian | `0x0419` | 1049 | 키릴 문자(Cyrillic) 레이아웃 |
| 🇮🇳 힌디어 | Hindi | `0x0439` | 1081 | 데바나가리(Devanagari) 문자 레이아웃 |
| 🇻🇳 베트남어 | Vietnamese | `0x02A1` / `0x042A` | 673 / 1066 | 성조 기호 입력용 Telex/VNI 레이아웃 |
| 🪷 빨리어 (Pāḷi) | Pali Keyboard Layout | <code style="color:#8E44AD"><b>0xF0C0</b></code> | 상위 16비트 | 로마자 확장 입력 자판 (현행 감지 기준) |

> 💡 **코드 검토 시 참고 사항**
> 기존 소스 코드에서 빨리어와 타 다국어를 동시에 판별할 때는 아래와 같이 **상위 비트(Device ID)**와 **하위 비트(LANGID)**를 모두 추출하여 비교하는 구조를 취하게 됩니다.

```csharp
// 입력기 핸들 가져오기
long hklValue = NativeMethods.GetKeyboardLayout(threadId).ToInt64();

// 1. 상위 16비트 추출 (빨리어 자판 등 특수 입력장치 식별용)
ushort devId = (ushort)((hklValue >> 16) & 0xFFFF);

// 2. 하위 16비트 추출 (일반 국가별 언어 식별용)
ushort langId = (ushort)(hklValue & 0xFFFF);

// 판별 로직 예시
if (devId == 0xF0C0)
{
    // 빨리어(Pāḷi) 처리
}
else if (langId == 0x0412)
{
    // 한국어 처리
}
else if (langId == 0x0804)
{
    // 중국어 간체 처리
}
```

**2️⃣ `ImeState.Detect` 내부의 조건식 확장**

기존 코드에서 `deviceId == 0xF0C0`만 체크하던 부분에 OR(`||`) 연산자를 사용하여 중국어 등의 언어 ID 체크 로직을 추가합니다.

**3️⃣ 대소문자(Caps Lock) 판별 공유**

조건문을 통과한 언어들은 자동으로 하단의 Caps Lock 및 Shift 키 검사 로직을 거쳐 소문자(Orange) 상태와 대문자(Lime) 상태로 매핑됩니다.

> 💡 **구조 분석 및 팁 (C++/VB 관점)**
>
> - 🧩 **결합도 최소화:** Graphic Asset 구조를 변경하지 않고 `ImeState.Detect` 내부의 판별식(`isTargetSpecialLang`) 하나만 확장했습니다. 이는 결합도를 매우 낮게 유지하는 객체 지향 및 절차적 최적화 설계 기법입니다.
> - 🎨 **비트 마스크 확장:** 향후 베트남어나 아랍어 등 또 다른 언어별로 각기 다른 색상(예: 노란색, 보라색 등)을 넣고 싶다면, `State` 열거형에 `VietnamLower`, `VietnamUpper`를 신설하고 `BakeAllAssets`에서 브러시 색상을 다르게 구워준 뒤, `Detect`문에서 `switch-case` 혹은 `if-else if`문 구조로 변경해주면 됩니다.

> ✅ <span style="color:#1E8449">**핵심 요약:** 새 언어를 추가할 때 색상·아이콘 렌더링 코드는 건드릴 필요가 없습니다. `Detect` 메서드의 조건식만 확장하면, 기존에 구워둔 빨리어용 에셋(주황·라임)을 그대로 재사용할 수 있습니다.</span>

---

<p align="center">📌 <i>이 문서는 IMECursor 앱의 내부 구조와 Win32 API 활용 방식을 정리한 학습 노트입니다.</i></p>
