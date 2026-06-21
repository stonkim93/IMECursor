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
    // =========================================================================
    internal static class AppConfig
    {
        // 1) 타이머 감지 주기 (Polling Interval)
        public const int PollingInterval = 15;

        // 2) 미니 인디케이터를 띄울 타겟 프로그램 지정 (반드시 소문자로 입력)
        public static readonly string[] IndicatorTargetApps = { "excel", "hwp" };

        // 3) 미니 인디케이터 디자인 및 위치 (67.5도 중심축 수학적 정렬 완료)
        public const float IndicatorSize = 8.0f;       // 저해상도 비대화 방지를 위해 8.0으로 최적화
        public const float IndicatorOffsetX = 9.5f;    // 중심축 X좌표 (Tip으로부터의 거리)
        public const float IndicatorOffsetY = 22.9f;   // 중심축 Y좌표 (OffsetX * 2.4142 기울기 완벽 적용)

        // 4) 확장 언어 (Custom Language) 식별 ID
        public const ushort CustomLang_DeviceId = 0xF0C0;
        public const ushort CustomLang_LangId = 0x0000;

        // 5) 상태별 색상 및 트레이 텍스트 테마 설정 구조체
        public struct Theme
        {
            public Color PointerColor;
            public Color TrayBgColor;
            public Color TrayTextColor;
            public string TrayText;
            public string Description;
        }

        // 🎨 테마 딕셔너리
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

        // 🌟 통합 배율 제어 변수
        private float _currentScaleRatio = 1.0f;
        private int _indicatorCanvasSize = 16;

        private static readonly unsafe int s_bmiSize = sizeof(NativeMethods.BITMAPINFO);

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

            float dpiScale = 1.0f;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) { dpiScale = g.DpiX / 96f; }

            int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR);
            float sysScale = cx > 0 ? (float)cx / 32f : 1.0f;

            // 배율의 기준을 명확히 통일하여 모든 크기/거리 연산의 베이스로 사용합니다.
            _currentScaleRatio = Math.Max(dpiScale, sysScale);

            int cursorWidth = (int)Math.Ceiling(32 * _currentScaleRatio);
            int cursorHeight = (int)Math.Ceiling(32 * _currentScaleRatio);
            if (cursorWidth < 32) cursorWidth = 32;
            if (cursorHeight < 32) cursorHeight = 32;

            foreach (ImeState.State state in Enum.GetValues(typeof(ImeState.State)))
            {
                if (!AppConfig.Themes.TryGetValue(state, out AppConfig.Theme theme)) continue;

                StateAssets assets = new StateAssets
                {
                    DotColor = theme.PointerColor,
                    Description = theme.Description,
                    Arrow = CreateDynamicArrowCursor(theme.PointerColor, cursorWidth, cursorHeight, _currentScaleRatio),
                    IBeam = CreateDynamicIBeamCursor(theme.PointerColor, cursorWidth, cursorHeight, _currentScaleRatio),
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
                    // 🌟 [핵심 수정] 타겟 중심점을 먼저 구한 뒤, 캔버스 크기의 절반을 빼서 Top-Left 좌표로 환산합니다.
                    // 이렇게 해야 캔버스 크기가 배율에 따라 커지더라도 '원의 중심'이 절대 위치를 이탈하지 않습니다.
                    float targetX = pt.X + (AppConfig.IndicatorOffsetX * _currentScaleRatio);
                    float targetY = pt.Y + (AppConfig.IndicatorOffsetY * _currentScaleRatio);

                    int destX = (int)Math.Round(targetX - (_indicatorCanvasSize / 2.0f));
                    int destY = (int)Math.Round(targetY - (_indicatorCanvasSize / 2.0f));

                    UpdateLayeredIndicator(_currentDotColor, destX, destY);
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

            NativeMethods.SIZE sz = new() { cx = _indicatorCanvasSize, cy = _indicatorCanvasSize };
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

            _indicatorCanvasSize = (int)(16 * _currentScaleRatio);
            if (_indicatorCanvasSize % 2 != 0) _indicatorCanvasSize++; // 나눗셈 오차를 막기 위해 무조건 짝수로 맞춥니다.
            if (_indicatorCanvasSize < 16) _indicatorCanvasSize = 16;

            using Bitmap bmp = new(_indicatorCanvasSize, _indicatorCanvasSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float size = AppConfig.IndicatorSize * _currentScaleRatio;
                float offset = (_indicatorCanvasSize - size) / 2.0f;
                RectangleF rect = new(offset, offset, size, size);

                using SolidBrush brush = new(color); g.FillEllipse(brush, rect);
                Color penColor = (color == Color.White) ? Color.Black : (color == Color.Black ? Color.White : Color.Black);
                float penWidth = Math.Max(1.0f, 1.0f * _currentScaleRatio);
                using Pen pen = new(penColor, penWidth) { Alignment = PenAlignment.Inset }; g.DrawEllipse(pen, rect);
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
            NativeMethods.BITMAPINFO bmi = new NativeMethods.BITMAPINFO
            {
                biSize = s_bmiSize,
                biWidth = bitmap.Width,
                biHeight = -bitmap.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            IntPtr pBits = IntPtr.Zero;
            IntPtr hBitmap = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, 0, out pBits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero) return IntPtr.Zero;
            System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
            Buffer.MemoryCopy((void*)bmpData.Scan0, (void*)pBits, bytes, bytes);
            bitmap.UnlockBits(bmpData);
            return hBitmap;
        }

        private static IntPtr CreateDynamicArrowCursor(Color color, int w, int h, float scale)
        {
            using Bitmap bmp = new(w, h);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 🌟 [수학적 완전 대칭 좌표계] 67.5도 중심축(Y = 2.4142X) 기준 반사(Reflection) 행렬 적용
            // 길이 15.0px 윈도우 순정 크기 동기화 및 꼬리 평행선(Parallel Stem) 알고리즘 적용
            PointF[] pts = [
                new PointF(0.00f * scale, 0.00f * scale),    // 1. 최상단 꼭지점 (Tip)
                new PointF(0.00f * scale, 15.00f * scale),   // 2. 왼쪽 날개 (순정 크기 15.0px 일치)
                new PointF(4.00f * scale, 12.00f * scale),   // 3. 왼쪽 내부 꺾임점
                new PointF(6.30f * scale, 17.54f * scale),   // 4. 꼬리(Stem) 왼쪽 하단 끝점
                new PointF(7.95f * scale, 16.86f * scale),   // 5. 꼬리(Stem) 오른쪽 하단 끝점
                new PointF(5.66f * scale, 11.31f * scale),   // 6. 오른쪽 내부 꺾임점 (대칭 완벽 매핑)
                new PointF(10.61f * scale, 10.61f * scale)   // 7. 오른쪽 날개 (정밀 45도 매핑, 길이 15.0px 일치)
            ];

            using Brush brush = new SolidBrush(color);
            g.FillPolygon(brush, pts);

            // 테두리가 바깥으로 번지는 현상을 막기 위해 100% 렌더링 시 굵기 1.0f로 가장 날렵하게 고정
            using Pen pen = new(Color.Black, Math.Max(1.0f, 1.0f * scale)) { LineJoin = LineJoin.Round };
            g.DrawPolygon(pen, pts);

            return BitmapToCursor(bmp, 0, 0);
        }

        private static IntPtr CreateDynamicIBeamCursor(Color color, int w, int h, float scale)
        {
            using Bitmap bmp = new(w, h);
            using Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float lx = 12f * scale, rx = 20f * scale, cx = 16f * scale, ty = 8f * scale, by = 24f * scale;

            using Pen outPen = new(Color.Black, 3.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(outPen, lx, ty, rx, ty); g.DrawLine(outPen, lx, by, rx, by); g.DrawLine(outPen, cx, ty, cx, by);

            using Pen inPen = new(color, 1.5f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(inPen, lx, ty, rx, ty); g.DrawLine(inPen, lx, by, rx, by); g.DrawLine(inPen, cx, ty, cx, by);

            return BitmapToCursor(bmp, (int)cx, (int)(16f * scale));
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

        public const uint OCR_NORMAL = 32512, OCR_IBEAM = 32513;
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