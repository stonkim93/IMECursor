using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace IMECursor;

// ==========================================
// 1. 프로그램 진입점 (Program)
// ==========================================
internal static partial class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "IMECursorColor_SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("이미 실행 중입니다.", "IME Cursor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) => MainForm.RestoreDefaults();
        AppDomain.CurrentDomain.ProcessExit += (s, e) => MainForm.RestoreDefaults();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new MainForm());
    }
}

// ==========================================
// 2. 메인 폼 및 트레이/표시기 제어 (MainForm)
// ==========================================
internal partial class MainForm : Form
{
    private readonly System.Windows.Forms.Timer _stateTimer;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;

    private readonly ToolStripMenuItem _statusMenuItem;
    private readonly ToolStripMenuItem _toggleIndicatorMenuItem;
    private bool _enableMiniIndicator = true;

    private readonly Dictionary<ImeState.State, Icon> _trayIcons = new()
    {
        { ImeState.State.EnglishLower, CreateTrayIcon("e", Color.FromArgb(70, 70, 70), Color.White) },
        { ImeState.State.EnglishUpper, CreateTrayIcon("E", Color.DeepSkyBlue, Color.Black) },
        { ImeState.State.Hangul,       CreateTrayIcon("K", Color.FromArgb(220, 40, 40), Color.White) },
        { ImeState.State.PaliLower,    CreateTrayIcon("p", Color.FromArgb(255, 175, 0), Color.Black) },
        { ImeState.State.PaliUpper,    CreateTrayIcon("P", Color.Lime, Color.Black) }
    };

    private ImeState.State _lastState = (ImeState.State)(-1);
    private Color _currentDotColor = Color.White;

    private IntPtr _currentArrowCursor = IntPtr.Zero;
    private IntPtr _currentIBeamCursor = IntPtr.Zero;
    private IntPtr _currentCrossCursor = IntPtr.Zero;

    private IntPtr _lastForegroundHwnd = IntPtr.Zero;
    private bool _showMiniIndicator = false;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    public MainForm()
    {
        this.Size = new Size(16, 16);
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.Location = new Point(-100, -100);

        _stateTimer = new()
        {
            Interval = 15
        };
        _stateTimer.Tick += StateTimer_Tick;

        _contextMenu = new();

        _statusMenuItem = new ToolStripMenuItem("현재 상태: 확인 중...") { Enabled = false };
        _contextMenu.Items.Add(_statusMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        _toggleIndicatorMenuItem = new ToolStripMenuItem("엑셀/한글 작은원 표시기 활성화")
        {
            CheckOnClick = true,
            Checked = true
        };
        _toggleIndicatorMenuItem.CheckedChanged += (s, e) =>
        {
            _enableMiniIndicator = _toggleIndicatorMenuItem.Checked;
            if (!_enableMiniIndicator)
            {
                UpdateLayeredIndicator(Color.Transparent, -100, -100);
            }
        };
        _contextMenu.Items.Add(_toggleIndicatorMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitMenuItem = new("종료", null, (s, e) => this.Close());
        _contextMenu.Items.Add(exitMenuItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _trayIcons[ImeState.State.EnglishLower],
            Text = "IME Cursor Color Changer",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _trayIcon.MouseClick += TrayIcon_MouseClick;
    }

    protected override void OnPaint(PaintEventArgs e) { /* 잔상 방지 */ }
    protected override void OnPaintBackground(PaintEventArgs e) { /* 배경 지우기 무시 */ }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _ = NativeMethods.SetForegroundWindow(this.Handle);
            _contextMenu.Show(Cursor.Position);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateSystemCursorColor(ImeState.Detect());
        _stateTimer.Start();
    }

    private void StateTimer_Tick(object? sender, EventArgs e)
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd != _lastForegroundHwnd)
        {
            _lastForegroundHwnd = hWnd;
            _showMiniIndicator = IsTargetProcess(hWnd);
        }

        ImeState.State currentState = ImeState.Detect();

        if (currentState != _lastState)
        {
            UpdateSystemCursorColor(currentState);

            if (_trayIcons.TryGetValue(currentState, out var targetIcon))
            {
                _trayIcon.Icon = targetIcon;
            }

            _lastState = currentState;

            string stateDescription = currentState switch
            {
                ImeState.State.EnglishLower => "영어 소문자 [e]",
                ImeState.State.EnglishUpper => "영어 대문자 [E]",
                ImeState.State.Hangul => "한국어 [K]",
                ImeState.State.PaliLower => "팔리어 소문자 [p]",
                ImeState.State.PaliUpper => "팔리어 대문자 [P]",
                _ => currentState.ToString()
            };

            _trayIcon.Text = $"IME Cursor: {stateDescription}";
            _statusMenuItem.Text = $"현재 상태: {stateDescription}";
        }

        if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            if (_showMiniIndicator && _enableMiniIndicator)
            {
                UpdateLayeredIndicator(_currentDotColor, pt.X + 18, pt.Y + 18);
            }
            else
            {
                UpdateLayeredIndicator(Color.Transparent, -100, -100);
            }
        }
    }

    private static bool IsTargetProcess(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return false;

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            string name = proc.ProcessName;

            return string.Equals(name, "excel", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "hwp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSystemCursorColor(ImeState.State state)
    {
        switch (state)
        {
            case ImeState.State.EnglishLower: _currentDotColor = Color.White; break;
            case ImeState.State.EnglishUpper: _currentDotColor = Color.DeepSkyBlue; break;
            case ImeState.State.Hangul: _currentDotColor = Color.Red; break;
            case ImeState.State.PaliLower: _currentDotColor = Color.FromArgb(255, 175, 0); break;
            case ImeState.State.PaliUpper: _currentDotColor = Color.Lime; break;
        }

        IntPtr hNewArrow = CreateDynamicArrowCursor(_currentDotColor);
        IntPtr hNewIBeam = CreateDynamicIBeamCursor(_currentDotColor);
        IntPtr hNewCross = CreateDynamicCrossCursor(_currentDotColor);

        if (hNewArrow != IntPtr.Zero)
        {
            _ = NativeMethods.SetSystemCursor(hNewArrow, NativeMethods.OCR_NORMAL);
            if (_currentArrowCursor != IntPtr.Zero) _ = NativeMethods.DestroyIcon(_currentArrowCursor);
            _currentArrowCursor = hNewArrow;
        }

        if (hNewIBeam != IntPtr.Zero)
        {
            _ = NativeMethods.SetSystemCursor(hNewIBeam, NativeMethods.OCR_IBEAM);
            if (_currentIBeamCursor != IntPtr.Zero) _ = NativeMethods.DestroyIcon(_currentIBeamCursor);
            _currentIBeamCursor = hNewIBeam;
        }

        if (hNewCross != IntPtr.Zero)
        {
            _ = NativeMethods.SetSystemCursor(hNewCross, NativeMethods.OCR_CROSS);
            if (_currentCrossCursor != IntPtr.Zero) _ = NativeMethods.DestroyIcon(_currentCrossCursor);
            _currentCrossCursor = hNewCross;
        }
    }

    private void UpdateLayeredIndicator(Color color, int x, int y)
    {
        using Bitmap bmp = new(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            if (color != Color.Transparent)
            {
                RectangleF rect = new(3f, 3f, 10f, 10f);

                using Brush brush = new SolidBrush(color);
                g.FillEllipse(brush, rect);

                using Pen pen = new(Color.Black, 1.0f);
                g.DrawEllipse(pen, rect);
            }
        }

        SelectBitmap(bmp, x, y);
    }

    private void SelectBitmap(Bitmap bitmap, int x, int y)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = CreateAlphaHBitmap(bitmap, screenDc);
            if (hBitmap == IntPtr.Zero) return;

            oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

            NativeMethods.SIZE size = new() { cx = bitmap.Width, cy = bitmap.Height };
            NativeMethods.POINT pointSource = new() { X = 0, Y = 0 };
            NativeMethods.POINT pointDest = new() { X = x, Y = y };
            NativeMethods.BLENDFUNCTION blend = new()
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA
            };

            _ = NativeMethods.UpdateLayeredWindow(
                this.Handle, screenDc, ref pointDest, ref size,
                memDc, ref pointSource, 0, ref blend, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBitmap != IntPtr.Zero)
            {
                _ = NativeMethods.SelectObject(memDc, oldBitmap);
                _ = NativeMethods.DeleteObject(hBitmap);
            }
            _ = NativeMethods.DeleteDC(memDc);
        }
    }

    private static IntPtr CreateAlphaHBitmap(Bitmap bitmap, IntPtr hdcScreen)
    {
        NativeMethods.BITMAPINFO bmi = new()
        {
            biSize = Marshal.SizeOf(typeof(NativeMethods.BITMAPINFO)),
            biWidth = bitmap.Width,
            biHeight = -bitmap.Height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        IntPtr hBitmap = NativeMethods.CreateDIBSection(hdcScreen, ref bmi, 0, out IntPtr pBits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero) return IntPtr.Zero;

        System.Drawing.Imaging.BitmapData bmpData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        int bytes = bmpData.Stride * bitmap.Height;
        byte[] rgbValues = new byte[bytes];
        Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
        Marshal.Copy(rgbValues, 0, pBits, bytes);

        bitmap.UnlockBits(bmpData);
        return hBitmap;
    }

    private static IntPtr CreateDynamicArrowCursor(Color color)
    {
        using Bitmap bmp = new(32, 32);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Point[] arrowPoints = [
            new Point(0, 0), new Point(0, 20), new Point(5, 15), new Point(9, 24),
            new Point(12, 23), new Point(8, 14), new Point(15, 14)
        ];

        using Brush brush = new SolidBrush(color);
        g.FillPolygon(brush, arrowPoints);

        using Pen pen = new(Color.Black, 1.2f) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, arrowPoints);

        IntPtr hbitmap = bmp.GetHbitmap();
        NativeMethods.ICONINFO ii = new() { fIcon = 0, xHotspot = 0, yHotspot = 0, hbmMask = hbitmap, hbmColor = hbitmap };
        IntPtr hCursor = NativeMethods.CreateIconIndirect(ref ii);
        _ = NativeMethods.DeleteObject(hbitmap);
        return hCursor;
    }

    private static IntPtr CreateDynamicIBeamCursor(Color color)
    {
        using Bitmap bmp = new(32, 32);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using Pen outPen = new(Color.Black, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(outPen, 11, 6, 21, 6);
        g.DrawLine(outPen, 11, 26, 21, 26);
        g.DrawLine(outPen, 16, 6, 16, 26);

        using Pen inPen = new(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(inPen, 11, 6, 21, 6);
        g.DrawLine(inPen, 11, 26, 21, 26);
        g.DrawLine(inPen, 16, 6, 16, 26);

        IntPtr hbitmap = bmp.GetHbitmap();
        NativeMethods.ICONINFO ii = new() { fIcon = 0, xHotspot = 16, yHotspot = 16, hbmMask = hbitmap, hbmColor = hbitmap };
        IntPtr hCursor = NativeMethods.CreateIconIndirect(ref ii);
        _ = NativeMethods.DeleteObject(hbitmap);
        return hCursor;
    }

    private static IntPtr CreateDynamicCrossCursor(Color color)
    {
        using Bitmap bmp = new(32, 32);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using Pen outPen = new(Color.Black, 7.0f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(outPen, 7, 16, 25, 16);
        g.DrawLine(outPen, 16, 7, 16, 25);

        using Pen inPen = new(color, 3.0f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        g.DrawLine(inPen, 7, 16, 25, 16);
        g.DrawLine(inPen, 16, 7, 16, 25);

        IntPtr hbitmap = bmp.GetHbitmap();
        NativeMethods.ICONINFO ii = new() { fIcon = 0, xHotspot = 16, yHotspot = 16, hbmMask = hbitmap, hbmColor = hbitmap };
        IntPtr hCursor = NativeMethods.CreateIconIndirect(ref ii);
        _ = NativeMethods.DeleteObject(hbitmap);
        return hCursor;
    }

    private static Icon CreateTrayIcon(string text, Color bgColor, Color textColor)
    {
        using Bitmap bmp = new(32, 32);
        using Graphics g = Graphics.FromImage(bmp);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using Brush bgBrush = new SolidBrush(bgColor);
        g.FillRectangle(bgBrush, 0, 0, 32, 32);

        // 자판 상태가 'p'(팔리어 소문자)일 경우 Segoe Script 적용 및 크기를 조금 더 확장(25F -> 27F)
        string fontName = (text == "p") ? "Segoe Script" : "Segoe UI Black";
        float fontSize = (text == "p") ? 27F : 29F;

        using Font font = new(fontName, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using Brush textBrush = new SolidBrush(textColor);

        StringFormat sf = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        float xOffset = -2.0f;
        float yOffset = -3.0f;

        if (text == "p")
        {
            xOffset = -1.0f;
            yOffset = -7.0f;

            // [핵심 변경] 필기체 'p'를 더 두껍고 명확하게 만들기 위해 
            // 가로 및 세로 방향으로 미세 오프셋(0.6~0.8 픽셀)을 주어 중첩하여 렌더링 (Extra Bold 처리)
            g.DrawString(text, font, textBrush, new RectangleF(xOffset, yOffset, 36f, 36f), sf);
            g.DrawString(text, font, textBrush, new RectangleF(xOffset + 0.8f, yOffset, 36f, 36f), sf);
            g.DrawString(text, font, textBrush, new RectangleF(xOffset, yOffset + 0.6f, 36f, 36f), sf);
        }
        else
        {
            if (text == "e") yOffset = -4.5f;
            g.DrawString(text, font, textBrush, new RectangleF(xOffset, yOffset, 36f, 36f), sf);
        }

        IntPtr hIcon = bmp.GetHicon();
        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        _ = NativeMethods.DestroyIcon(hIcon);

        return icon;
    }

    public static void RestoreDefaults()
    {
        _ = NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETCURSORS, 0, IntPtr.Zero, NativeMethods.SPIF_SENDCHANGE);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _stateTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _contextMenu.Dispose();

        RestoreDefaults();

        if (_currentArrowCursor != IntPtr.Zero) _ = NativeMethods.DestroyIcon(_currentArrowCursor);
        if (_currentIBeamCursor != IntPtr.Zero) _ = NativeMethods.DestroyIcon(_currentIBeamCursor);
        if (_currentCrossCursor != IntPtr.Zero) _ = NativeMethods.DestroyIcon(_currentCrossCursor);

        foreach (var icon in _trayIcons.Values) icon.Dispose();

        base.OnFormClosing(e);
    }
}

// ==========================================
// 3. 입력기 상태 감지 엔진 (ImeState)
// ==========================================
internal static partial class ImeState
{
    public enum State { EnglishLower, EnglishUpper, Hangul, PaliLower, PaliUpper }

    private const ushort LANG_KOREAN = 0x0412;
    private const ushort LANG_ENGLISH = 0x0409;
    private const ushort PALI_DEVICE_ID = 0xF0C0;

    public static State Detect()
    {
        bool capsOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x0001) != 0;

        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return capsOn ? State.EnglishUpper : State.EnglishLower;

        uint fgThread = NativeMethods.GetWindowThreadProcessId(hWnd, out _);

        IntPtr hTargetWnd = hWnd;
        uint targetThread = fgThread;

        NativeMethods.GUITHREADINFO gti = new();
        gti.cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));

        if (NativeMethods.GetGUIThreadInfo(fgThread, ref gti))
        {
            IntPtr referenceWnd = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : gti.hwndActive;
            if (referenceWnd != IntPtr.Zero)
            {
                hTargetWnd = referenceWnd;
                targetThread = NativeMethods.GetWindowThreadProcessId(hTargetWnd, out _);
            }
        }

        IntPtr hkl = NativeMethods.GetKeyboardLayout(targetThread);
        long hklVal = hkl.ToInt64();

        ushort fgLangId = (ushort)(hklVal & 0xFFFF);
        ushort deviceId = (ushort)((hklVal >> 16) & 0xFFFF);

        if (fgLangId == LANG_ENGLISH)
        {
            if (deviceId == PALI_DEVICE_ID)
            {
                return capsOn ? State.PaliUpper : State.PaliLower;
            }
            return capsOn ? State.EnglishUpper : State.EnglishLower;
        }

        if (fgLangId == LANG_KOREAN)
        {
            if (IsHangulModeAdvanced(hTargetWnd)) return State.Hangul;
            return capsOn ? State.EnglishUpper : State.EnglishLower;
        }

        return capsOn ? State.PaliUpper : State.PaliLower;
    }

    private static bool IsHangulModeAdvanced(IntPtr hWnd)
    {
        IntPtr hIMC = NativeMethods.ImmGetContext(hWnd);
        if (hIMC != IntPtr.Zero)
        {
            bool success = NativeMethods.ImmGetConversionStatus(hIMC, out uint conversion, out uint _);
            _ = NativeMethods.ImmReleaseContext(hWnd, hIMC);
            if (success)
            {
                return (conversion & NativeMethods.IME_CMODE_NATIVE) != 0;
            }
        }

        IntPtr hImeWnd = NativeMethods.ImmGetDefaultIMEWnd(hWnd);
        if (hImeWnd != IntPtr.Zero)
        {
            IntPtr conversion = NativeMethods.SendMessage(hImeWnd, NativeMethods.WM_IME_CONTROL, (IntPtr)NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero);
            uint convStatus = (uint)conversion.ToInt64();
            return (convStatus & NativeMethods.IME_CMODE_NATIVE) != 0;
        }

        return false;
    }
}

// ==========================================
// 4. Win32 API 핵심 선언부 (NativeMethods)
// ==========================================
internal static partial class NativeMethods
{
    public const int VK_CAPITAL = 0x14;
    public const int WM_IME_CONTROL = 0x0283;
    public const int IMC_GETCONVERSIONMODE = 0x0001;
    public const uint IME_CMODE_NATIVE = 0x0001;

    public const uint OCR_NORMAL = 32512;
    public const uint OCR_IBEAM = 32513;
    public const uint OCR_CROSS = 32515;
    public const uint SPI_SETCURSORS = 0x0057;
    public const uint SPIF_SENDCHANGE = 0x0002;

    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const uint ULW_ALPHA = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO { public int fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO { public int cbSize; public int flags; public IntPtr hwndActive; public IntPtr hwndFocus; public IntPtr hwndCapture; public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize; public IntPtr hwndCaret; public int rectLeft; public int rectTop; public int rectRight; public int rectBottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [LibraryImport("user32.dll")] public static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [LibraryImport("user32.dll")] public static partial IntPtr GetKeyboardLayout(uint idThread);
    [LibraryImport("user32.dll")] public static partial short GetKeyState(int keyCode);

    [LibraryImport("imm32.dll")] public static partial IntPtr ImmGetContext(IntPtr hWnd);
    [LibraryImport("imm32.dll")] public static partial int ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [LibraryImport("imm32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);
    [LibraryImport("imm32.dll")] public static partial IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] public static partial IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetSystemCursor(IntPtr hcur, uint id);
    [LibraryImport("user32.dll")] public static partial IntPtr CreateIconIndirect(ref ICONINFO iconinfo);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DestroyIcon(IntPtr hIcon);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteObject(IntPtr hObject);
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgti);
    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")] public static partial IntPtr GetDC(IntPtr hWnd);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [LibraryImport("gdi32.dll")] public static partial IntPtr CreateCompatibleDC(IntPtr hDC);
    [LibraryImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool DeleteDC(IntPtr hDC);
    [LibraryImport("gdi32.dll")] public static partial IntPtr SelectObject(IntPtr hDC, IntPtr hGdiObj);
    [LibraryImport("user32.dll", EntryPoint = "UpdateLayeredWindow")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(IntPtr hWnd);
}