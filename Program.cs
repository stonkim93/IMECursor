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
    // ⚙️ [사용자 커스텀 환경 설정] (AppConfig)
    // 아래의 값들만 변경하면 프로그램 전체의 디자인, 속도, 타겟 프로그램이 변경됩니다.
    // =========================================================================
    internal static class AppConfig
    {
        // 1) 타이머 감지 주기 (Polling Interval)
        // 기본값: 15 (15ms, 초당 약 66회). 배터리 절약이 필요하면 25~30으로 늘리세요.
        public const int PollingInterval = 15;

        // 2) 미니 인디케이터를 띄울 타겟 프로그램 지정 (반드시 소문자로 입력)
        // 예: MS Word를 추가하려면 "winword"를 배열에 추가합니다.
        public static readonly string[] IndicatorTargetApps = { "excel", "hwp" };

        // 3) 미니 인디케이터 디자인 및 위치
        public const float IndicatorSize = 10.0f; // 작은 원의 크기 (기본 10.0, 최대 14.0)
        public const int IndicatorOffsetX = 6;    // 마우스 포인터 끝점에서 X축 떨어진 거리
        public const int IndicatorOffsetY = 24;   // 마우스 포인터 끝점에서 Y축 떨어진 거리

        // 4) 확장 언어 (Custom Language) 식별 ID (기존 Pali어 대체용)
        // - 상위 16비트(Device ID)를 쓰는 특수 자판 (예: Pali어 = 0xF0C0)
        public const ushort CustomLang_DeviceId = 0xF0C0;
        // - 하위 16비트(LANGID)를 쓰는 일반 국가 자판 (예: 중국어 간체 = 0x0804, 프랑스어 = 0x040C)
        // 일반 자판을 감지하려면 0x0000 대신 해당 코드를 입력하세요.
        public const ushort CustomLang_LangId = 0x0000;

        // 5) 상태별 색상 및 트레이 텍스트 테마 설정 구조체
        public struct Theme
        {
            public Color PointerColor;   // 마우스 포인터 및 작은 원의 색상
            public Color TrayBgColor;    // 트레이 아이콘의 배경 사각형 색상
            public Color TrayTextColor;  // 트레이 아이콘의 텍스트 색상
            public string TrayText;      // 트레이 아이콘에 표시될 1글자
            public string Description;   // 우클릭 메뉴 및 마우스 오버 시 표시될 설명
        }

        // 🎨 테마 딕셔너리 (여기서 모든 색상과 텍스트를 일괄 제어합니다)
        public static readonly Dictionary<ImeState.State, Theme> Themes = new()
        {
            [ImeState.State.EnglishLower] = new Theme
            {
                PointerColor = Color.White,
                TrayBgColor = Color.Black,
                TrayTextColor = Color.DeepSkyBlue,
                TrayText = "e",
                Description = "영어 소문자 [e]"
            },
            [ImeState.State.EnglishUpper] = new Theme
            {
                PointerColor = Color.DeepSkyBlue,
                TrayBgColor = Color.Black,
                TrayTextColor = Color.DeepSkyBlue,
                TrayText = "E",
                Description = "영어 대문자 [E]"
            },
            [ImeState.State.Hangul] = new Theme
            {
                PointerColor = Color.Red,
                TrayBgColor = Color.Red,
                TrayTextColor = Color.White,
                TrayText = "K",
                Description = "한국어 [K]"
            },
            [ImeState.State.CustomLangLower] = new Theme
            {
                PointerColor = Color.Orange,
                TrayBgColor = Color.Black,
                TrayTextColor = Color.Orange,
                TrayText = "p",
                Description = "확장언어 소문자 [p]"
            },
            [ImeState.State.CustomLangUpper] = new Theme
            {
                PointerColor = Color.Lime,
                TrayBgColor = Color.Black,
                TrayTextColor = Color.Lime,
                TrayText = "P",
                Description = "확장언어 대문자 [P]"
            }
        };
    }

    // =========================================================================
    // 1. 프로그램 진입점 (Main)
    // =========================================================================
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            using Mutex mutex = new Mutex(true, "IMECursorColor_SingleInstance", out bool first);
            if (!first)
            {
                MessageBox.Show("이미 실행 중입니다.", "IME Cursor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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
        private class StateAssets : IDisposable
        {
            public IntPtr Arrow, IBeam;
            public Icon TrayIcon = null!;
            public Color DotColor;
            public string Description = "";

            public void Dispose()
            {
                if (Arrow != IntPtr.Zero) NativeMethods.DestroyCursor(Arrow);
                if (IBeam != IntPtr.Zero) NativeMethods.DestroyCursor(IBeam);
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

        private bool _showMiniIndicator = false;
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        private IntPtr _currentHwnd = IntPtr.Zero;

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
            biHeight = -16,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x00000008 | 0x00000020 | 0x00000080 | 0x08000000 | 0x00080000; return cp; }
        }

        public MainForm()
        {
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

            _trayIcon = new() { Text = "IME Cursor", ContextMenuStrip = _contextMenu, Visible = true };
            _trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) { NativeMethods.SetForegroundWindow(this.Handle); _contextMenu.Show(Cursor.Position); } };

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            BakeAllAssets();

            // 설정 클래스(AppConfig)에서 타이머 주기를 가져와 동적으로 적용합니다.
            _stateTimer = new() { Interval = AppConfig.PollingInterval };
            _stateTimer.Tick += StateTimer_Tick;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => OnDisplaySettingsChanged(sender, e))); return; }
            _stateTimer.Stop();
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
                // AppConfig.Themes에서 해당 상태의 테마 데이터를 가져옵니다.
                if (!AppConfig.Themes.TryGetValue(state, out AppConfig.Theme theme)) continue;

                StateAssets assets = new StateAssets
                {
                    DotColor = theme.PointerColor,
                    Description = theme.Description,
                    Arrow = CreateDynamicArrowCursor(theme.PointerColor, w, h),
                    IBeam = CreateDynamicIBeamCursor(theme.PointerColor, w, h),
                    TrayIcon = CreateTrayIcon(theme.TrayText, theme.TrayBgColor, theme.TrayTextColor)
                };
                _assetCache[state] = assets;
            }

            if (_lastState != (ImeState.State)(-1)) ApplyState(_lastState);

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        private void StateTimer_Tick(object? sender, EventArgs e)
        {
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            if (hWnd == this.Handle || hWnd == IntPtr.Zero) return;

            bool isTaskbar = IsTaskbarOrSystemWindow(hWnd);

            if (hWnd != _currentHwnd)
            {
                bool wasTaskbar = IsTaskbarOrSystemWindow(_currentHwnd);

                if (isTaskbar && !wasTaskbar && _lastState != (ImeState.State)(-1)) { ImeState.SetHangulState(hWnd, ImeState.IsHangul(_lastState)); }
                else if (!isTaskbar && wasTaskbar && hWnd == _lastForegroundHwnd && _lastState != (ImeState.State)(-1)) { ImeState.SetHangulState(hWnd, ImeState.IsHangul(_lastState)); }

                if (!isTaskbar) { _lastForegroundHwnd = hWnd; _showMiniIndicator = IsTargetProcess(hWnd); }
                _currentHwnd = hWnd;
            }

            ImeState.State currentState = ImeState.Detect(hWnd);

            if (currentState != _lastState)
            {
                _lastState = currentState;
                ApplyState(currentState);
                if (isTaskbar && _lastForegroundHwnd != IntPtr.Zero) { ImeState.SetHangulState(_lastForegroundHwnd, ImeState.IsHangul(currentState)); }
            }

            if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
            {
                if (_showMiniIndicator && _enableMiniIndicator)
                {
                    float ratio = (NativeMethods.GetSystemMetrics(NativeMethods.SM_CYCURSOR) is int cy and > 0 ? cy : 32) / 32f;
                    // AppConfig에서 지정한 오프셋 위치를 사용합니다.
                    UpdateLayeredIndicator(_currentDotColor, pt.X + (int)(AppConfig.IndicatorOffsetX * ratio), pt.Y + (int)(AppConfig.IndicatorOffsetY * ratio));
                }
                else { UpdateLayeredIndicator(Color.Transparent, -10000, -10000); }
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

            try { if (_trayIcon.Icon == null || _trayIcon.Icon.Handle != assets.TrayIcon.Handle) _trayIcon.Icon = assets.TrayIcon; }
            catch (ObjectDisposedException) { _trayIcon.Icon = assets.TrayIcon; }

            ReplaceSystemCursor(NativeMethods.CopyIcon(assets.Arrow), NativeMethods.OCR_NORMAL);
            ReplaceSystemCursor(NativeMethods.CopyIcon(assets.IBeam), NativeMethods.OCR_IBEAM);

            _trayIcon.Text = $"IME Cursor: {assets.Description}";
            _statusMenuItem.Text = $"현재상태: {assets.Description}";
        }

        private static void ReplaceSystemCursor(IntPtr hNew, uint cursorId)
        {
            if (hNew == IntPtr.Zero) return;
            if (!NativeMethods.SetSystemCursor(hNew, cursorId)) NativeMethods.DestroyCursor(hNew);
        }

        private void UpdateLayeredIndicator(Color color, int x, int y)
        {
            bool needsUpdate = false;

            if (color != _lastIndicatorColor) { _lastIndicatorColor = color; if (color != Color.Transparent) BakeIndicatorBuffer(color); needsUpdate = true; }
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
                    destPt.X = -10000; destPt.Y = -10000; bf.SourceConstantAlpha = 0;
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

                // AppConfig에서 지정한 원의 크기를 적용하여 16x16 중앙에 정렬합니다.
                float size = AppConfig.IndicatorSize;
                float offset = (16.0f - size) / 2.0f;
                RectangleF rect = new(offset, offset, size, size);

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

            // 특정 글자(p, e)에 하드코딩 되어있던 로직을, 대소문자 판별을 통한 범용 로직으로 개선했습니다.
            bool isLower = !string.IsNullOrEmpty(text) && char.IsLower(text[0]);
            string fontName = isLower ? "Segoe Print" : "Segoe UI Black";
            float fontSize = isLower ? 31F : 32F;
            float xOff = -2.0f;
            float yOff = isLower ? -5.0f : -3.5f;

            using Font font = new(fontName, fontSize, FontStyle.Bold, GraphicsUnit.Pixel); using SolidBrush textBrush = new(textColor);
            StringFormat sf = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            if (isLower)
            {
                g.DrawString(text, font, textBrush, new RectangleF(xOff, yOff, 36f, 36f), sf); g.DrawString(text, font, textBrush, new RectangleF(xOff + 1.0f, yOff, 36f, 36f), sf);
                g.DrawString(text, font, textBrush, new RectangleF(xOff, yOff + 1.0f, 36f, 36f), sf); g.DrawString(text, font, textBrush, new RectangleF(xOff + 1.0f, yOff + 1.0f, 36f, 36f), sf);
                g.DrawString(text, font, textBrush, new RectangleF(xOff + 0.5f, yOff + 0.5f, 36f, 36f), sf);
            }
            else { g.DrawString(text, font, textBrush, new RectangleF(xOff, yOff, 36f, 36f), sf); }
            IntPtr hIcon = bmp.GetHicon(); Icon icon = (Icon)Icon.FromHandle(hIcon).Clone(); NativeMethods.DestroyIcon(hIcon); return icon;
        }

        private static bool IsTargetProcess(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            uint pid = 0; NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
            if (pid == 0) return false;
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid); string name = proc.ProcessName;
                // AppConfig의 타겟 배열을 순회하며 일치 여부를 검사합니다.
                foreach (string targetApp in AppConfig.IndicatorTargetApps)
                {
                    if (name.Equals(targetApp, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
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
                _stateTimer?.Stop(); _stateTimer?.Dispose(); _trayIcon?.Dispose(); _contextMenu?.Dispose();
            }
            ClearCaches(); RestoreDefaults(); CleanUpIndicatorGdi(); base.Dispose(disposing);
        }
    }

    // =========================================================================
    // 3. 입력기 상태 감지 엔진 (ImeState)
    // =========================================================================
    internal static class ImeState
    {
        // 범용적 사용을 위해 PaliLower/Upper 명칭을 CustomLangLower/Upper 로 일반화했습니다.
        public enum State { EnglishLower, EnglishUpper, Hangul, CustomLangLower, CustomLangUpper }

        public static bool IsHangul(State state) => state == State.Hangul;

        public static State Detect(IntPtr foregroundHwnd)
        {
            bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;

            if (foregroundHwnd == IntPtr.Zero) return capsOn ? State.EnglishUpper : State.EnglishLower;

            uint threadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);
            IntPtr focusWnd = foregroundHwnd;

            NativeMethods.GUITHREADINFO gti = new() { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(threadId, ref gti))
            {
                if (gti.hwndFocus != IntPtr.Zero) { focusWnd = gti.hwndFocus; threadId = NativeMethods.GetWindowThreadProcessId(focusWnd, out _); }
                else if (gti.hwndActive != IntPtr.Zero) { focusWnd = gti.hwndActive; threadId = NativeMethods.GetWindowThreadProcessId(focusWnd, out _); }
            }

            // AppConfig에 정의된 Device ID(상위 16비트) 또는 Lang ID(하위 16비트) 중 하나라도 일치하면 Custom 언어로 처리
            long hklValue = NativeMethods.GetKeyboardLayout(threadId).ToInt64();
            ushort devId = (ushort)((hklValue >> 16) & 0xFFFF);
            ushort langId = (ushort)(hklValue & 0xFFFF);

            if (devId == AppConfig.CustomLang_DeviceId || langId == AppConfig.CustomLang_LangId)
                return capsOn ? State.CustomLangUpper : State.CustomLangLower;

            bool isHangul = CheckHangul(focusWnd);
            if (!isHangul && focusWnd != foregroundHwnd) isHangul = CheckHangul(foregroundHwnd);

            return isHangul ? State.Hangul : (capsOn ? State.EnglishUpper : State.EnglishLower);
        }

        private static bool CheckHangul(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            IntPtr hImeWnd = NativeMethods.ImmGetDefaultIMEWnd(hWnd);
            if (hImeWnd != IntPtr.Zero)
            {
                IntPtr result = IntPtr.Zero;
                NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 20, out result);
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
                NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 20, out IntPtr result);
                uint mode = (uint)result.ToInt64();
                bool isHangul = (mode & NativeMethods.IME_CMODE_NATIVE) != 0;

                if (isHangul != setHangul)
                {
                    if (setHangul) mode |= NativeMethods.IME_CMODE_NATIVE; else mode &= ~NativeMethods.IME_CMODE_NATIVE;
                    NativeMethods.SendMessageTimeout(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_SETCONVERSIONMODE, (IntPtr)mode, NativeMethods.SMTO_ABORTIFHUNG, 20, out _);
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

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] public static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] public static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
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