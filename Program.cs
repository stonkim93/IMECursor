using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// [필수 최적화] .NET 런타임의 자동 마샬링 오버헤드를 제거하여 Native C++ 수준의 커널 호출 속도를 확보합니다.
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

namespace IMECursor
{
    // =========================================================================
    // 1. 프로그램 진입점 (Main)
    // =========================================================================
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Mutex를 활용하여 프로세스 중복 실행 차단
            using Mutex mutex = new Mutex(true, "IMECursorColor_SingleInstance", out bool first);

            if (!first)
            {
                MessageBox.Show("이미 실행 중입니다.", "IME Cursor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 프로그램 예기치 않은 종료 시 OS 기본 마우스 커서로 복구하는 안전망
            AppDomain.CurrentDomain.UnhandledException += (s, e) => MainForm.RestoreDefaults();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => MainForm.RestoreDefaults();

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }

    // =========================================================================
    // 2. 메인 폼 클래스 (백그라운드 루프, 그래픽 렌더링 및 캐싱 담당)
    // =========================================================================
    internal class MainForm : Form
    {
        // 입력 상태별 마우스 커서 및 트레이 아이콘 자원(GDI 핸들)을 보관하는 구조체
        private class StateAssets : IDisposable
        {
            public IntPtr Arrow, IBeam;
            //public IntPtr Cross, SizeWE, SizeNS, SizeNWSE, SizeNESW, SizeAll;
            public Icon TrayIcon = null!;
            public Color DotColor;

            public void Dispose()
            {
                // 관리되지 않는 Win32 자원의 명시적 메모리 해제
                if (Arrow != IntPtr.Zero) NativeMethods.DestroyCursor(Arrow);
                if (IBeam != IntPtr.Zero) NativeMethods.DestroyCursor(IBeam);
                //if (Cross != IntPtr.Zero) NativeMethods.DestroyCursor(Cross);
                //if (SizeWE != IntPtr.Zero) NativeMethods.DestroyCursor(SizeWE);
                //if (SizeNS != IntPtr.Zero) NativeMethods.DestroyCursor(SizeNS);
                //if (SizeNWSE != IntPtr.Zero) NativeMethods.DestroyCursor(SizeNWSE);
                //if (SizeNESW != IntPtr.Zero) NativeMethods.DestroyCursor(SizeNESW);
                //if (SizeAll != IntPtr.Zero) NativeMethods.DestroyCursor(SizeAll);
                TrayIcon?.Dispose();
            }
        }

        private readonly Dictionary<ImeState.State, StateAssets> _assetCache = new();
        private readonly System.Windows.Forms.Timer _stateTimer;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _statusMenuItem;
        private readonly ToolStripMenuItem _toggleIndicatorMenuItem;

        private bool _enableMiniIndicator = true;
        private ImeState.State _lastState = (ImeState.State)(-1);
        private Color _currentDotColor = Color.White;

        // 동기화 추적을 위한 변수
        private bool _showMiniIndicator = false;
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        private IntPtr _currentHwnd = IntPtr.Zero;

        // 인디케이터(작은 원) 렌더링용 GDI 변수
        private IntPtr _indicatorScreenDc = IntPtr.Zero;
        private IntPtr _indicatorMemDc = IntPtr.Zero;
        private IntPtr _indicatorHBitmap = IntPtr.Zero;
        private IntPtr _indicatorOldBitmap = IntPtr.Zero;
        private bool _isIndicatorBaked = false;
        private Color _lastIndicatorColor = Color.Empty;
        private int _lastIndicatorX = int.MinValue;
        private int _lastIndicatorY = int.MinValue;

        private static readonly unsafe int s_bmiSize = sizeof(NativeMethods.BITMAPINFO);
        private static readonly NativeMethods.BITMAPINFO s_bmi16 = new()
        {
            biSize = s_bmiSize,
            biWidth = 16,
            biHeight = -16, // 음수 값은 Top-Down DIB를 의미하여 렌더링 속도 최적화
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        // Layered 윈도우(클릭 투과 및 투명화) 속성 적용
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000008 | 0x00000020 | 0x00000080 | 0x08000000 | 0x00080000;
                return cp;
            }
        }

        public MainForm()
        {
            // 폼을 화면 밖으로 숨김 처리
            this.Size = new Size(16, 16);
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(-100, -100);

            _contextMenu = new();
            _statusMenuItem = new("현재상태: 확인중...") { Enabled = false };
            _toggleIndicatorMenuItem = new("엑셀/한글 작은원 표시") { CheckOnClick = true, Checked = true };
            _toggleIndicatorMenuItem.CheckedChanged += (s, e) =>
            {
                _enableMiniIndicator = _toggleIndicatorMenuItem.Checked;
                if (!_enableMiniIndicator) UpdateLayeredIndicator(Color.Transparent, -10000, -10000);
            };

            _contextMenu.Items.Add(_statusMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_toggleIndicatorMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(new ToolStripMenuItem("종료(Exit)", null, (s, e) => this.Close()));

            _trayIcon = new()
            {
                Text = "IME Cursor Color Changer",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };
            _trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { NativeMethods.SetForegroundWindow(this.Handle); _contextMenu.Show(Cursor.Position); } };

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // 프로그램 시작 시 5가지 테마의 그래픽 자원을 1회 선행 렌더링(캐싱)
            BakeAllAssets();

            // 💡 [커스텀 가이드 3: 타이머 감지 주기 (Polling Interval) 조정]
            // Interval = 15는 15ms마다(초당 약 66회) 입력 상태를 검사함을 의미합니다.
            // 노트북 배터리나 CPU 점유율이 우려된다면 이 값을 25~30으로 늘리시면 됩니다.
            _stateTimer = new() { Interval = 15 };
            _stateTimer.Tick += StateTimer_Tick;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => OnDisplaySettingsChanged(sender, e)));
                return;
            }

            _stateTimer.Stop(); // 자원을 리셋하는 동안 스레드 충돌 방지를 위해 타이머 정지
            RestoreDefaults();
            BakeAllAssets();
            _stateTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            _currentHwnd = hWnd;
            _lastForegroundHwnd = hWnd;
            _showMiniIndicator = IsTargetProcess(hWnd);

            ApplyState(ImeState.Detect(hWnd));
            _stateTimer.Start();
        }

        private void BakeAllAssets()
        {
            ClearCaches();
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR) is int cx and > 0 ? cx : 32;
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYCURSOR) is int cy and > 0 ? cy : 32;

            foreach (ImeState.State state in Enum.GetValues(typeof(ImeState.State)))
            {
                Color dotColor = GetColorForState(state);
                StateAssets assets = new StateAssets
                {
                    DotColor = dotColor,
                    Arrow = CreateDynamicArrowCursor(dotColor, w, h),
                    IBeam = CreateDynamicIBeamCursor(dotColor, w, h),
                    //Cross = CreateDynamicCrossCursor(dotColor, w, h),
                    //SizeWE = CreateDynamicResizeCursor(dotColor, w, h, 0f),
                    //SizeNS = CreateDynamicResizeCursor(dotColor, w, h, 90f),
                    //SizeNWSE = CreateDynamicResizeCursor(dotColor, w, h, 45f),
                    //SizeNESW = CreateDynamicResizeCursor(dotColor, w, h, 135f),
                    //SizeAll = CreateDynamicSizeAllCursor(dotColor, w, h)
                };

                // 💡 [커스텀 가이드 2-2: 트레이 텍스트 및 배경/글자색 변경]
                // iconText: 트레이 아이콘에 각인되는 문자열 (E, K, p, P 등)
                // bgColor: 트레이 아이콘 배경색 (일반적으로 포인터 색상과 맞춤)
                // txtColor: 트레이 글자색 (배경색이 밝으면 Black, 어두우면 White)
                string iconText = state switch { ImeState.State.EnglishUpper => "E", ImeState.State.Hangul => "K", ImeState.State.PaliLower => "p", ImeState.State.PaliUpper => "P", _ => "e" };
                Color bgColor = state switch { ImeState.State.EnglishUpper => Color.Black, ImeState.State.Hangul => Color.Red, ImeState.State.PaliLower => Color.Black, ImeState.State.PaliUpper => Color.Black, _ => Color.Black };
                Color txtColor = state switch { ImeState.State.EnglishUpper => Color.DeepSkyBlue, ImeState.State.Hangul => Color.White, ImeState.State.PaliLower => Color.Orange, ImeState.State.PaliUpper => Color.Lime, _ => Color.White };
                //Color bgColor = state switch { ImeState.State.EnglishUpper => Color.DeepSkyBlue, ImeState.State.Hangul => Color.Red, ImeState.State.PaliLower => Color.Orange, ImeState.State.PaliUpper => Color.Lime, _ => Color.Black };
                //Color txtColor = state switch { ImeState.State.PaliLower or ImeState.State.PaliUpper => Color.Black, _ => Color.White };

                assets.TrayIcon = CreateTrayIcon(iconText, bgColor, txtColor);
                _assetCache[state] = assets;
            }

            if (_lastState != (ImeState.State)(-1)) ApplyState(_lastState);

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // 💡 [커스텀 가이드 2-1: 입력 상태별 마우스 포인터 및 작은 원 색상 변경]
        // 원하는 Color로 리턴값을 수정하세요. (예: Color.Blue, Color.FromArgb(255, 175, 0))
        private Color GetColorForState(ImeState.State state) => state switch
        {
            ImeState.State.EnglishLower => Color.White,
            ImeState.State.EnglishUpper => Color.DeepSkyBlue,
            ImeState.State.Hangul => Color.Red,
            ImeState.State.PaliLower => Color.Orange,
            ImeState.State.PaliUpper => Color.Lime,
            _ => Color.White
        };

        private void StateTimer_Tick(object? sender, EventArgs e)
        {
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            if (hWnd == this.Handle || hWnd == IntPtr.Zero) return;

            bool isTaskbar = IsTaskbarOrSystemWindow(hWnd);

            // 포커스가 시스템(작업표시줄)과 일반 앱 간에 전환될 때 IME 상태 동기화 처리
            if (hWnd != _currentHwnd)
            {
                bool wasTaskbar = IsTaskbarOrSystemWindow(_currentHwnd);

                if (isTaskbar && !wasTaskbar && _lastState != (ImeState.State)(-1))
                {
                    ImeState.SetHangulState(hWnd, ImeState.IsHangul(_lastState));
                }
                else if (!isTaskbar && wasTaskbar && hWnd == _lastForegroundHwnd && _lastState != (ImeState.State)(-1))
                {
                    ImeState.SetHangulState(hWnd, ImeState.IsHangul(_lastState));
                }

                if (!isTaskbar)
                {
                    _lastForegroundHwnd = hWnd;
                    _showMiniIndicator = IsTargetProcess(hWnd); // 특정 앱(엑셀,한글) 활성화 여부 확인
                }

                _currentHwnd = hWnd;
            }

            ImeState.State currentState = ImeState.Detect(hWnd);

            if (currentState != _lastState)
            {
                _lastState = currentState;
                ApplyState(currentState);

                if (isTaskbar && _lastForegroundHwnd != IntPtr.Zero)
                {
                    ImeState.SetHangulState(_lastForegroundHwnd, ImeState.IsHangul(currentState));
                }
            }

            // 미니 인디케이터(작은 원)의 실시간 위치 추적 및 출력
            if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
            {
                if (_showMiniIndicator && _enableMiniIndicator)
                {
                    int cursorH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYCURSOR) is int cy and > 0 ? cy : 32;
                    float ratio = cursorH / 32f;

                    // 💡 [커스텀 가이드 4-2: 미니 인디케이터 위치 조정]
                    // 커서 좌표(pt.X, pt.Y)로부터 얼마나 떨어져서 원을 그릴지 오프셋(offset)을 결정합니다.
                    // (6 * ratio), (24 * ratio) 값을 늘리거나 줄여서 위치를 이동시킬 수 있습니다.
                    UpdateLayeredIndicator(_currentDotColor, pt.X + (int)(6 * ratio), pt.Y + (int)(24 * ratio));
                }
                else
                {
                    UpdateLayeredIndicator(Color.Transparent, -10000, -10000); // 화면 밖으로 치움
                }
            }
        }

        private unsafe bool IsTaskbarOrSystemWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || hWnd == this.Handle) return true;
            Span<char> className = stackalloc char[256];
            fixed (char* pName = className)
            {
                int length = NativeMethods.GetClassName(hWnd, pName, 256);
                if (length > 0)
                {
                    ReadOnlySpan<char> nameSpan = className.Slice(0, length);
                    return nameSpan.IndexOf("Shell_TrayWnd".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                           nameSpan.IndexOf("Progman".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                           nameSpan.IndexOf("WorkerW".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            return false;
        }

        private void ApplyState(ImeState.State state)
        {
            if (!_assetCache.TryGetValue(state, out StateAssets? assets)) return;

            _currentDotColor = assets.DotColor;

            try
            {
                if (_trayIcon.Icon == null || _trayIcon.Icon.Handle != assets.TrayIcon.Handle)
                {
                    _trayIcon.Icon = assets.TrayIcon;
                }
            }
            catch (ObjectDisposedException)
            {
                _trayIcon.Icon = assets.TrayIcon;
            }

            // 전역 시스템 커서 슬롯(OCR_NORMAL 등)을 캐싱된 이미지 핸들로 강제 교체
            ReplaceSystemCursor(NativeMethods.CopyIcon(assets.Arrow), NativeMethods.OCR_NORMAL);
            ReplaceSystemCursor(NativeMethods.CopyIcon(assets.IBeam), NativeMethods.OCR_IBEAM);
            //ReplaceSystemCursor(NativeMethods.CopyIcon(assets.Cross), NativeMethods.OCR_CROSS);
            //ReplaceSystemCursor(NativeMethods.CopyIcon(assets.SizeWE), NativeMethods.OCR_SIZEWE);
            //ReplaceSystemCursor(NativeMethods.CopyIcon(assets.SizeNS), NativeMethods.OCR_SIZENS);
            //ReplaceSystemCursor(NativeMethods.CopyIcon(assets.SizeNWSE), NativeMethods.OCR_SIZENWSE);
            //ReplaceSystemCursor(NativeMethods.CopyIcon(assets.SizeNESW), NativeMethods.OCR_SIZENESW);
            //ReplaceSystemCursor(NativeMethods.CopyIcon(assets.SizeAll), NativeMethods.OCR_SIZEALL);

            string desc = state switch
            {
                ImeState.State.EnglishLower => "영어 소문자 [e]",
                ImeState.State.EnglishUpper => "영어 대문자 [E]",
                ImeState.State.Hangul => "한국어 [K]",
                ImeState.State.PaliLower => "빨리어 소문자 [p]",
                ImeState.State.PaliUpper => "빨리어 대문자 [P]",
                _ => state.ToString()
            };
            _trayIcon.Text = $"IME Cursor: {desc}";
            _statusMenuItem.Text = $"현재상태: {desc}";
        }

        private static void ReplaceSystemCursor(IntPtr hNew, uint cursorId)
        {
            if (hNew == IntPtr.Zero) return;
            if (!NativeMethods.SetSystemCursor(hNew, cursorId)) NativeMethods.DestroyCursor(hNew);
        }

        private void UpdateLayeredIndicator(Color color, int x, int y)
        {
            bool needsUpdate = false;

            if (color != _lastIndicatorColor)
            {
                _lastIndicatorColor = color;
                if (color != Color.Transparent) BakeIndicatorBuffer(color);
                needsUpdate = true;
            }

            if (x != _lastIndicatorX || y != _lastIndicatorY) { _lastIndicatorX = x; _lastIndicatorY = y; needsUpdate = true; }
            if (!needsUpdate) return;

            NativeMethods.SIZE sz = new() { cx = 16, cy = 16 };
            NativeMethods.POINT srcPt = new() { X = 0, Y = 0 };
            NativeMethods.POINT destPt = new() { X = x, Y = y };
            NativeMethods.BLENDFUNCTION bf = new() { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };

            if (color == Color.Transparent || !_isIndicatorBaked)
            {
                if (_indicatorMemDc != IntPtr.Zero)
                {
                    destPt.X = -10000; destPt.Y = -10000;
                    bf.SourceConstantAlpha = 0;
                    IntPtr sDc = NativeMethods.GetDC(IntPtr.Zero);
                    _ = NativeMethods.UpdateLayeredWindow(this.Handle, sDc, ref destPt, ref sz, _indicatorMemDc, ref srcPt, 0, ref bf, 2);
                    _ = NativeMethods.ReleaseDC(IntPtr.Zero, sDc);
                }
                return;
            }

            IntPtr curScreenDc = NativeMethods.GetDC(IntPtr.Zero);
            _ = NativeMethods.UpdateLayeredWindow(this.Handle, curScreenDc, ref destPt, ref sz, _indicatorMemDc, ref srcPt, 0, ref bf, 2);
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, curScreenDc);
        }

        private void BakeIndicatorBuffer(Color color)
        {
            CleanUpIndicatorGdi();
            if (color == Color.Transparent) return;

            using Bitmap bmp = new(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // 💡 [커스텀 가이드 4-1: 미니 인디케이터 디자인(크기/모양) 조정]
                // RectangleF 파라미터 (X좌표, Y좌표, 너비, 높이)를 조정하여 원의 크기를 키우거나 줄일 수 있습니다.
                // 펜 두께(penColor, 1.0f) 부분도 조정 가능합니다.
                RectangleF rect = new(3.0f, 3.0f, 10.0f, 10.0f);
                using SolidBrush brush = new(color); g.FillEllipse(brush, rect);
                Color penColor = (color == Color.White) ? Color.Black : (color == Color.Black ? Color.White : Color.Black);
                using Pen pen = new(penColor, 1.0f) { Alignment = PenAlignment.Inset }; g.DrawEllipse(pen, rect);
            }

            _indicatorScreenDc = NativeMethods.GetDC(IntPtr.Zero);
            _indicatorMemDc = NativeMethods.CreateCompatibleDC(_indicatorScreenDc);
            _indicatorHBitmap = CreateAlphaHBitmap(bmp, _indicatorScreenDc);
            _indicatorOldBitmap = NativeMethods.SelectObject(_indicatorMemDc, _indicatorHBitmap);
            _isIndicatorBaked = true;
        }

        private void CleanUpIndicatorGdi()
        {
            if (_indicatorMemDc != IntPtr.Zero) { if (_indicatorOldBitmap != IntPtr.Zero) { _ = NativeMethods.SelectObject(_indicatorMemDc, _indicatorOldBitmap); _indicatorOldBitmap = IntPtr.Zero; } _ = NativeMethods.DeleteDC(_indicatorMemDc); _indicatorMemDc = IntPtr.Zero; }
            if (_indicatorHBitmap != IntPtr.Zero) { _ = NativeMethods.DeleteObject(_indicatorHBitmap); _indicatorHBitmap = IntPtr.Zero; }
            if (_indicatorScreenDc != IntPtr.Zero) { _ = NativeMethods.ReleaseDC(IntPtr.Zero, _indicatorScreenDc); _indicatorScreenDc = IntPtr.Zero; }
            _isIndicatorBaked = false;
        }

        private static unsafe IntPtr CreateAlphaHBitmap(Bitmap bitmap, IntPtr hdcScreen)
        {
            NativeMethods.BITMAPINFO bmi = (bitmap.Width == 16 && bitmap.Height == 16) ? s_bmi16 : new NativeMethods.BITMAPINFO { biSize = s_bmiSize, biWidth = bitmap.Width, biHeight = -bitmap.Height, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            IntPtr pBits = IntPtr.Zero;
            IntPtr hBitmap = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, 0, out pBits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero) return IntPtr.Zero;
            System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
            Buffer.MemoryCopy((void*)bmpData.Scan0, (void*)pBits, bytes, bytes);
            bitmap.UnlockBits(bmpData);
            return hBitmap;
        }

        private static IntPtr CreateDynamicArrowCursor(Color color, int w, int h)
        {
            using Bitmap bmp = new(w, h); using Graphics g = Graphics.FromImage(bmp); g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
            float sx = w / 32f, sy = h / 32f;
            PointF[] pts = [new(0f * sx, 0f * sy), new(0f * sx, 20f * sy), new(5f * sx, 15f * sy), new(9f * sx, 24f * sy), new(12f * sx, 23f * sy), new(8f * sx, 14f * sy), new(15f * sx, 14f * sy)];
            using Brush brush = new SolidBrush(color); g.FillPolygon(brush, pts);
            using Pen pen = new(Color.Black, Math.Max(1.0f, 1.2f * sx)) { LineJoin = LineJoin.Round }; g.DrawPolygon(pen, pts);
            return BitmapToCursor(bmp, 0, 0);
        }

        private static IntPtr CreateDynamicIBeamCursor(Color color, int w, int h)
        {
            using Bitmap bmp = new(w, h); using Graphics g = Graphics.FromImage(bmp); g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
            float sx = w / 32f, sy = h / 32f; float lx = 11f * sx, rx = 21f * sx, cx = 16f * sx, ty = 6f * sy, by = 26f * sy;
            using Pen outPen = new(Color.Black, 3.5f * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round }; g.DrawLine(outPen, lx, ty, rx, ty); g.DrawLine(outPen, lx, by, rx, by); g.DrawLine(outPen, cx, ty, cx, by);
            using Pen inPen = new(color, 1.5f * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round }; g.DrawLine(inPen, lx, ty, rx, ty); g.DrawLine(inPen, lx, by, rx, by); g.DrawLine(inPen, cx, ty, cx, by);
            return BitmapToCursor(bmp, (int)cx, (int)(16f * sy));
        }


        /* private static IntPtr CreateDynamicCrossCursor(Color color, int w, int h)
        {
            using Bitmap bmp = new(w, h); using Graphics g = Graphics.FromImage(bmp); g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
            float sx = w / 32f, sy = h / 32f; float lx = 7f * sx, rx = 25f * sx, cx = 16f * sx, ty = 7f * sy, by = 25f * sy, cy = 16f * sy;
            using Pen outPen = new(Color.Black, 7.0f * sx) { StartCap = LineCap.Flat, EndCap = LineCap.Flat }; g.DrawLine(outPen, lx, cy, rx, cy); g.DrawLine(outPen, cx, ty, cx, by);
            using Pen inPen = new(color, 3.0f * sx) { StartCap = LineCap.Flat, EndCap = LineCap.Flat }; g.DrawLine(inPen, lx, cy, rx, cy); g.DrawLine(inPen, cx, ty, cx, by);
            return BitmapToCursor(bmp, (int)cx, (int)cy);
        }

        private static IntPtr CreateDynamicResizeCursor(Color color, int w, int h, float angle)
        {
            using Bitmap bmp = new(w, h); using Graphics g = Graphics.FromImage(bmp); g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(w / 2f, h / 2f); g.RotateTransform(angle); g.TranslateTransform(-w / 2f, -h / 2f);
            float sx = w / 32f, sy = h / 32f;
            using Pen outPen = new(Color.Black, 3.5f * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using Pen inPen = new(color, 1.5f * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(outPen, 8 * sx, 16 * sy, 24 * sx, 16 * sy); g.DrawLine(inPen, 8 * sx, 16 * sy, 24 * sx, 16 * sy);
            PointF[] leftArrow = [new(2 * sx, 16 * sy), new(8 * sx, 12 * sy), new(8 * sx, 20 * sy)]; PointF[] rightArrow = [new(30 * sx, 16 * sy), new(24 * sx, 12 * sy), new(24 * sx, 20 * sy)];
            using Brush brush = new SolidBrush(color); using Pen polyOutPen = new(Color.Black, 1.5f * sx) { LineJoin = LineJoin.Round };
            g.FillPolygon(brush, leftArrow); g.DrawPolygon(polyOutPen, leftArrow); g.FillPolygon(brush, rightArrow); g.DrawPolygon(polyOutPen, rightArrow);
            g.ResetTransform(); return BitmapToCursor(bmp, (int)(w / 2f), (int)(h / 2f));
        }

        private static IntPtr CreateDynamicSizeAllCursor(Color color, int w, int h)
        {
            using Bitmap bmp = new(w, h); using Graphics g = Graphics.FromImage(bmp); g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
            float sx = w / 32f, sy = h / 32f;
            void DrawDoubleArrow(float angle)
            {
                g.TranslateTransform(w / 2f, h / 2f); g.RotateTransform(angle); g.TranslateTransform(-w / 2f, -h / 2f);
                using Pen outPen = new(Color.Black, 3.5f * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round }; using Pen inPen = new(color, 1.5f * sx) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(outPen, 8 * sx, 16 * sy, 24 * sx, 16 * sy); g.DrawLine(inPen, 8 * sx, 16 * sy, 24 * sx, 16 * sy);
                PointF[] leftArrow = [new(2 * sx, 16 * sy), new(8 * sx, 12 * sy), new(8 * sx, 20 * sy)]; PointF[] rightArrow = [new(30 * sx, 16 * sy), new(24 * sx, 12 * sy), new(24 * sx, 20 * sy)];
                using Brush brush = new SolidBrush(color); using Pen polyOutPen = new(Color.Black, 1.5f * sx) { LineJoin = LineJoin.Round };
                g.FillPolygon(brush, leftArrow); g.DrawPolygon(polyOutPen, leftArrow); g.FillPolygon(brush, rightArrow); g.DrawPolygon(polyOutPen, rightArrow);
                g.ResetTransform();
            }
            DrawDoubleArrow(0f); DrawDoubleArrow(90f);
            return BitmapToCursor(bmp, (int)(w / 2f), (int)(h / 2f));
        } */
        private static IntPtr BitmapToCursor(Bitmap bmp, int hotX, int hotY)
        {
            IntPtr hBmpColor = bmp.GetHbitmap(); IntPtr hBmpMask = NativeMethods.CreateBitmap(bmp.Width, bmp.Height, 1, 1, IntPtr.Zero);
            NativeMethods.ICONINFO ii = new() { fIcon = 0, xHotspot = hotX, yHotspot = hotY, hbmMask = hBmpMask, hbmColor = hBmpColor };
            IntPtr hCursor = NativeMethods.CreateIconIndirect(ref ii);
            NativeMethods.DeleteObject(hBmpColor); NativeMethods.DeleteObject(hBmpMask);
            return hCursor;
        }

        private static Icon CreateTrayIcon(string text, Color bgColor, Color textColor)
        {
            using Bitmap bmp = new(32, 32); using Graphics g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using SolidBrush bgBrush = new(bgColor); g.FillRectangle(bgBrush, 0, 0, 32, 32);
            string fontName = (text is "p" or "e") ? "Segoe Print" : "Segoe UI Black";
            float fontSize = text switch { "K" => 29F, "E" or "P" => 32F, "e" or "p" => 31F, _ => 29F };
            using Font font = new(fontName, fontSize, FontStyle.Bold, GraphicsUnit.Pixel); using SolidBrush textBrush = new(textColor);
            StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float xOff = -2.0f, yOff = -3.5f;
            if (text is "p" or "e")
            {
                yOff = text == "p" ? -6.5f : -4.0f; xOff = -1.5f;
                g.DrawString(text, font, textBrush, new RectangleF(xOff, yOff, 36f, 36f), sf); g.DrawString(text, font, textBrush, new RectangleF(xOff + 1.0f, yOff, 36f, 36f), sf); g.DrawString(text, font, textBrush, new RectangleF(xOff, yOff + 1.0f, 36f, 36f), sf); g.DrawString(text, font, textBrush, new RectangleF(xOff + 1.0f, yOff + 1.0f, 36f, 36f), sf); g.DrawString(text, font, textBrush, new RectangleF(xOff + 0.5f, yOff + 0.5f, 36f, 36f), sf);
            }
            else { g.DrawString(text, font, textBrush, new RectangleF(xOff, yOff, 36f, 36f), sf); }
            IntPtr hIcon = bmp.GetHicon(); Icon icon = (Icon)Icon.FromHandle(hIcon).Clone(); NativeMethods.DestroyIcon(hIcon); return icon;
        }

        // 💡 [커스텀 가이드 5: 특정 프로그램(Excel, 한글) 예외 처리 및 로직 확장]
        // 미니 인디케이터를 띄울 타겟 프로세스의 이름을 지정합니다. (대소문자 무관)
        // 예: name.Equals("winword", StringComparison.OrdinalIgnoreCase) 를 추가하면 MS Word에서도 작동합니다.
        private static bool IsTargetProcess(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            uint pid = 0; NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
            if (pid == 0) return false;
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid); string name = proc.ProcessName;
                return name.Equals("excel", StringComparison.OrdinalIgnoreCase) || name.Equals("hwp", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void RestoreDefaults() => NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero, NativeMethods.SPIF_SENDCHANGE);

        private void ClearCaches()
        {
            if (_trayIcon != null) _trayIcon.Icon = null;

            foreach (var asset in _assetCache.Values) asset.Dispose();
            _assetCache.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

                if (_trayIcon != null) _trayIcon.Visible = false;
                _stateTimer?.Stop();
                _stateTimer?.Dispose();
                _trayIcon?.Dispose();
                _contextMenu?.Dispose();
            }

            ClearCaches();
            RestoreDefaults();
            CleanUpIndicatorGdi();
            base.Dispose(disposing);
        }
    }

    // =========================================================================
    // 3. 입력기 상태 감지 엔진 (ImeState)
    // =========================================================================
    internal static class ImeState
    {
        public enum State { EnglishLower, EnglishUpper, Hangul, PaliLower, PaliUpper }
        private const ushort PALI_DEVICE_ID = 0xF0C0;

        public static bool IsHangul(State state) => state == State.Hangul;

        public static State Detect(IntPtr foregroundHwnd)
        {
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;

            if (foregroundHwnd == IntPtr.Zero)
                return capsOn ? State.EnglishUpper : State.EnglishLower;

            uint threadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            IntPtr focusWnd = foregroundHwnd;

            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(threadId, ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero)
                {
                    focusWnd = gti.hwndFocus;
                    threadId = NativeMethods.GetWindowThreadProcessId(focusWnd, out _);
                }
                else if (gti.hwndActive != IntPtr.Zero)
                {
                    focusWnd = gti.hwndActive;
                    threadId = NativeMethods.GetWindowThreadProcessId(focusWnd, out _);
                }
            }

            // 💡 [커스텀 가이드 1: Pali어 이외의 다른 언어 자판 추가]
            // 현재는 devId == PALI_DEVICE_ID (0xF0C0) 만을 Pali 상태로 취급합니다.
            // 중국어(예: langId 0x0804) 등도 같은 색상/로직을 태우려면 아래처럼 확장하세요.
            // 
            // ushort langId = (ushort)(NativeMethods.GetKeyboardLayout(threadId).ToInt64() & 0xFFFF);
            // if (devId == PALI_DEVICE_ID || langId == 0x0804) 
            //     return capsOn ? State.PaliUpper : State.PaliLower;

            ushort devId = (ushort)((NativeMethods.GetKeyboardLayout(threadId).ToInt64() >> 16) & 0xFFFF);
            if (devId == PALI_DEVICE_ID)
                return capsOn ? State.PaliUpper : State.PaliLower;

            bool isHangul = CheckHangul(focusWnd);
            if (!isHangul && focusWnd != foregroundHwnd)
            {
                isHangul = CheckHangul(foregroundHwnd);
            }

            return isHangul ? State.Hangul : (capsOn ? State.EnglishUpper : State.EnglishLower);
        }

        private static bool CheckHangul(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            IntPtr hImeWnd = NativeMethods.ImmGetDefaultIMEWnd(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                IntPtr result = IntPtr.Zero;
                NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL,
                    (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 20, out result);
                if (result != IntPtr.Zero) return ((uint)result.ToInt64() & NativeMethods.IME_CMODE_NATIVE) != 0;
            }

            IntPtr hIMC = NativeMethods.ImmGetContext(hWnd);
            if (hIMC != IntPtr.Zero)
            {
                bool success = NativeMethods.ImmGetConversionStatus(hIMC, out uint conv, out _);
                NativeMethods.ImmReleaseContext(hWnd, hIMC);
                if (success) return (conv & NativeMethods.IME_CMODE_NATIVE) != 0;
            }
            return false;
        }

        public static void SetHangulState(IntPtr hWnd, bool setHangul)
        {
            if (hWnd == IntPtr.Zero) return;

            uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
            IntPtr targetWnd = hWnd;

            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(threadId, ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero) targetWnd = gti.hwndFocus;
                else if (gti.hwndActive != IntPtr.Zero) targetWnd = gti.hwndActive;
            }

            IntPtr hImeWnd = NativeMethods.ImmGetDefaultIMEWnd(targetWnd);
            if (hImeWnd == IntPtr.Zero) hImeWnd = NativeMethods.ImmGetDefaultIMEWnd(hWnd);

            if (hImeWnd != IntPtr.Zero)
            {
                NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL,
                    (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 20, out IntPtr result);

                uint mode = (uint)result.ToInt64();
                bool isHangul = (mode & NativeMethods.IME_CMODE_NATIVE) != 0;

                if (isHangul != setHangul)
                {
                    if (setHangul) mode |= NativeMethods.IME_CMODE_NATIVE;
                    else mode &= ~NativeMethods.IME_CMODE_NATIVE;

                    NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL,
                        (IntPtr)NativeMethods.IMC_SETCONVERSIONMODE, (IntPtr)mode, NativeMethods.SMTO_ABORTIFHUNG, 20, out _);
                }
            }
        }
    }

    // =========================================================================
    // 4. Win32 API (NativeMethods)
    // =========================================================================
    internal static unsafe partial class NativeMethods
    {
        public const int VK_CAPITAL = 0x14;
        public const int WM_IME_CONTROL = 0x0283;
        public const int IMC_GETCONVERSIONMODE = 0x0001;
        public const int IMC_SETCONVERSIONMODE = 0x0002;
        public const uint IME_CMODE_NATIVE = 0x0001;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        public const uint OCR_NORMAL = 32512, OCR_IBEAM = 32513, OCR_CROSS = 32515, OCR_SIZENWSE = 32642, OCR_SIZENESW = 32643, OCR_SIZEWE = 32644, OCR_SIZENS = 32645, OCR_SIZEALL = 32646;
        public const uint SPI_SETCURSORS = 0x0057, SPIF_SENDCHANGE = 0x0002;
        public const int SM_CXCURSOR = 13, SM_CYCURSOR = 14;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public int fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
        [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public int rectLeft, rectTop, rectRight, rectBottom; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }

        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetForegroundWindow();
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial IntPtr GetKeyboardLayout(uint idThread);
        [LibraryImport("user32.dll")][SuppressGCTransition] public static partial short GetKeyState(int keyCode);
        [LibraryImport("user32.dll")][SuppressGCTransition][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT lpPoint);
        [LibraryImport("user32.dll")][SuppressGCTransition][return: MarshalAs(UnmanagedType.Bool)] public static partial bool IsWindow(IntPtr hWnd);

        [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        public static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetSystemCursor(IntPtr hcur, uint id);
        [LibraryImport("user32.dll")] public static partial IntPtr CopyIcon(IntPtr hIcon);
        [LibraryImport("user32.dll")] public static partial IntPtr CreateIconIndirect(ref ICONINFO iconinfo);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(IntPtr hIcon);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyCursor(IntPtr hCursor);
        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgti);
        [LibraryImport("user32.dll")] public static partial IntPtr GetDC(IntPtr hWnd);
        [LibraryImport("user32.dll")] public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [LibraryImport("user32.dll", EntryPoint = "UpdateLayeredWindow")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(IntPtr hWnd);
        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)] public static partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);

        [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(IntPtr hObject);
        [LibraryImport("gdi32.dll")] public static partial IntPtr CreateCompatibleDC(IntPtr hDC);
        [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteDC(IntPtr hDC);
        [LibraryImport("gdi32.dll")] public static partial IntPtr SelectObject(IntPtr hDC, IntPtr hGdiObj);
        [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")] public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [LibraryImport("gdi32.dll")] public static partial IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

        [LibraryImport("imm32.dll")] public static partial IntPtr ImmGetContext(IntPtr hWnd);
        [LibraryImport("imm32.dll")] public static partial int ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);
        [LibraryImport("imm32.dll")] public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
    }
}