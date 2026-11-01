using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;

namespace TaleSpireVR
{
    /// <summary>
    /// A second desktop window ("TaleSpire VR - Stream View") for Discord / OBS. It runs its own Win32 message loop on a
    /// background thread and draws finished frames (bottom-up rows) with GDI, so it never waits on the game.
    /// Unity can only open extra windows on extra monitors, hence a plain Win32 window.
    ///
    /// Frames are triple-buffered: the main thread fills a spare buffer and swaps it in under a short lock; the window
    /// thread paints the newest one. Double-click (or F11) toggles borderless fullscreen; the client area keeps 16:9.
    /// </summary>
    internal sealed class StreamWindow
    {
        /// <summary>Pixels arrive as RGBA (what the GPU can read back); GDI wants BGRA, swapped once on this thread.</summary>
        private sealed class Frame { public byte[] Data = new byte[0]; public int W, H; public bool Bgra; }

        private readonly object _lock = new object();
        private Frame _spare = new Frame(), _ready = new Frame(), _display = new Frame();
        private bool _readyFresh;
        private readonly AutoResetEvent _frameEvent = new AutoResetEvent(false);

        private Thread _thread;
        private volatile IntPtr _hwnd;
        private volatile bool _quit;
        private WndProcFn _wndProc;   // kept alive: the window class holds a pointer to it
        private string _title;
        private int _clientW, _clientH;
        private bool _fullscreen;
        private WINDOWPLACEMENT _savedPlacement;

        /// <summary>The window exists (the user has not closed it).</summary>
        public bool IsOpen => _thread != null && _thread.IsAlive && _hwnd != IntPtr.Zero;

        public void Open(string title, int clientW, int clientH)
        {
            if (_thread != null && _thread.IsAlive) return;
            _title = title;
            _clientW = clientW;
            _clientH = clientH;
            _quit = false;
            _thread = new Thread(Run) { IsBackground = true, Name = "TaleSpireVR stream window" };
            _thread.Start();
        }

        public void Close()
        {
            _quit = true;
            var h = _hwnd;
            if (h != IntPtr.Zero) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _frameEvent.Set();
        }

        /// <summary>Main thread: hand over a finished frame (32-bit RGBA, rows bottom-up as Unity reads them back).</summary>
        public void Submit(NativeArray<byte> data, int w, int h)
        {
            int size = w * h * 4;
            if (data.Length < size || _hwnd == IntPtr.Zero) return;
            var f = _spare;
            if (f.Data.Length != size) f.Data = new byte[size];
            NativeArray<byte>.Copy(data, f.Data, size);
            f.W = w;
            f.H = h;
            f.Bgra = false;
            lock (_lock)
            {
                _spare = _ready;
                _ready = f;
                _readyFresh = true;
            }
            _frameEvent.Set();
        }

        private void Run()
        {
            IntPtr hInstance = GetModuleHandle(null);
            const string cls = "TaleSpireVRStreamView";
            try
            {
                _wndProc = WndProc;
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                    style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = hInstance,
                    hCursor = LoadCursor(IntPtr.Zero, (IntPtr)IDC_ARROW),
                    hbrBackground = GetStockObject(BLACK_BRUSH),
                    lpszClassName = cls,
                };
                RegisterClassEx(ref wc);

                var r = new RECT { Left = 0, Top = 0, Right = _clientW, Bottom = _clientH };
                AdjustWindowRectEx(ref r, WS_OVERLAPPEDWINDOW, false, 0);
                _hwnd = CreateWindowEx(0, cls, _title, WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT,
                    r.Right - r.Left, r.Bottom - r.Top, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                {
                    Plugin.Log.LogError("Stream window: CreateWindowEx failed, error " + Marshal.GetLastWin32Error());
                    return;
                }
                // Don't take focus from the game window.
                ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
                Plugin.Log.LogInfo($"Stream window opened ({_clientW}x{_clientH} client)");

                var handles = new[] { _frameEvent.SafeWaitHandle.DangerousGetHandle() };
                while (!_quit && _hwnd != IntPtr.Zero)
                {
                    uint w = MsgWaitForMultipleObjects(1, handles, false, 100, QS_ALLINPUT);
                    while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    if (w == WAIT_OBJECT_0 && _hwnd != IntPtr.Zero) PaintNow();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Stream window thread failed: " + e);
            }
            finally
            {
                if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
                UnregisterClass(cls, hInstance);
                Plugin.Log.LogInfo("Stream window closed");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (msg)
                {
                    case WM_ERASEBKGND:
                        return (IntPtr)1;   // we paint every pixel ourselves (no flicker)
                    case WM_PAINT:
                    {
                        IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);
                        Paint(hwnd, hdc, true);
                        EndPaint(hwnd, ref ps);
                        return IntPtr.Zero;
                    }
                    case WM_SIZING:
                        KeepAspect(hwnd, (int)wParam, lParam);
                        return (IntPtr)1;
                    case WM_LBUTTONDBLCLK:
                        ToggleFullscreen(hwnd);
                        return IntPtr.Zero;
                    case WM_KEYDOWN:
                        if ((int)wParam == VK_F11 || ((int)wParam == VK_ESCAPE && _fullscreen)) ToggleFullscreen(hwnd);
                        return IntPtr.Zero;
                    case WM_CLOSE:
                        DestroyWindow(hwnd);
                        return IntPtr.Zero;
                    case WM_DESTROY:
                        _hwnd = IntPtr.Zero;
                        return IntPtr.Zero;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Stream window message " + msg + " failed: " + e.Message);
            }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void PaintNow()
        {
            IntPtr hdc = GetDC(_hwnd);
            if (hdc == IntPtr.Zero) return;
            Paint(_hwnd, hdc, false);
            ReleaseDC(_hwnd, hdc);
        }

        private void Paint(IntPtr hwnd, IntPtr hdc, bool fullRepaint)
        {
            lock (_lock)
            {
                if (_readyFresh)
                {
                    var t = _display; _display = _ready; _ready = t;
                    _readyFresh = false;
                }
            }
            GetClientRect(hwnd, out RECT rc);
            int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
            if (cw <= 0 || ch <= 0) return;
            var f = _display;
            if (f.W <= 0 || f.H <= 0)
            {
                FillRect(hdc, ref rc, GetStockObject(BLACK_BRUSH));
                return;
            }
            if (!f.Bgra) { SwapRedBlue(f.Data); f.Bgra = true; }

            // Fit the frame inside the client area, keeping its aspect ratio (black bars if the window is off 16:9).
            float scale = Math.Min((float)cw / f.W, (float)ch / f.H);
            int dw = Math.Max(1, (int)Math.Round(f.W * scale)), dh = Math.Max(1, (int)Math.Round(f.H * scale));
            int dx = (cw - dw) / 2, dy = (ch - dh) / 2;
            if (dx > 0 || dy > 0 || fullRepaint)
            {
                IntPtr black = GetStockObject(BLACK_BRUSH);
                var top = new RECT { Left = 0, Top = 0, Right = cw, Bottom = dy };
                var bottom = new RECT { Left = 0, Top = dy + dh, Right = cw, Bottom = ch };
                var left = new RECT { Left = 0, Top = dy, Right = dx, Bottom = dy + dh };
                var right = new RECT { Left = dx + dw, Top = dy, Right = cw, Bottom = dy + dh };
                if (dy > 0) { FillRect(hdc, ref top, black); FillRect(hdc, ref bottom, black); }
                if (dx > 0) { FillRect(hdc, ref left, black); FillRect(hdc, ref right, black); }
            }

            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = f.W,
                biHeight = f.H,          // positive = bottom-up rows, which is how Unity reads textures back
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,       // BI_RGB (BGRX)
            };
            // Shrinking: HALFTONE averages pixels (clean); same size or larger: plain copy.
            if (dw < f.W) { SetStretchBltMode(hdc, HALFTONE); SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero); }
            else SetStretchBltMode(hdc, COLORONCOLOR);
            StretchDIBits(hdc, dx, dy, dw, dh, 0, 0, f.W, f.H, f.Data, ref bmi, DIB_RGB_COLORS, SRCCOPY);
        }

        private static unsafe void SwapRedBlue(byte[] data)
        {
            fixed (byte* p = data)
            {
                uint* px = (uint*)p;
                int n = data.Length / 4;
                for (int i = 0; i < n; i++)
                {
                    uint v = px[i];   // little-endian RGBA = 0xAABBGGRR -> BGRA = 0xAARRGGBB
                    px[i] = (v & 0xFF00FF00u) | ((v & 0xFFu) << 16) | ((v >> 16) & 0xFFu);
                }
            }
        }

        /// <summary>WM_SIZING: keep the client area 16:9 while the user drags a border.</summary>
        private void KeepAspect(IntPtr hwnd, int edge, IntPtr lParam)
        {
            var r = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));
            var frame = new RECT();
            AdjustWindowRectEx(ref frame, (uint)GetWindowLong(hwnd, GWL_STYLE), false, 0);
            int fw = frame.Right - frame.Left, fh = frame.Bottom - frame.Top;
            int cw = Math.Max(160, r.Right - r.Left - fw), chh = Math.Max(90, r.Bottom - r.Top - fh);
            // Dragging the top or bottom edge sets the height; any other edge or corner sets the width.
            if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM) cw = (int)Math.Round(chh * 16.0 / 9.0);
            else chh = (int)Math.Round(cw * 9.0 / 16.0);
            if (edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT)
                r.Top = r.Bottom - chh - fh;
            else
                r.Bottom = r.Top + chh + fh;
            if (edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT)
                r.Left = r.Right - cw - fw;
            else
                r.Right = r.Left + cw + fw;
            Marshal.StructureToPtr(r, lParam, false);
        }

        private void ToggleFullscreen(IntPtr hwnd)
        {
            if (!_fullscreen)
            {
                _savedPlacement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
                GetWindowPlacement(hwnd, ref _savedPlacement);
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
                GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi);
                SetWindowLong(hwnd, GWL_STYLE, unchecked((int)(WS_POPUP | WS_VISIBLE)));
                var m = mi.rcMonitor;
                SetWindowPos(hwnd, HWND_TOP, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, SWP_FRAMECHANGED | SWP_NOOWNERZORDER);
                _fullscreen = true;
            }
            else
            {
                SetWindowLong(hwnd, GWL_STYLE, (int)(WS_OVERLAPPEDWINDOW | WS_VISIBLE));
                SetWindowPlacement(hwnd, ref _savedPlacement);
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
                _fullscreen = false;
            }
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }

        // ---- Win32 ----
        private delegate IntPtr WndProcFn(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
        private const uint CS_VREDRAW = 1, CS_HREDRAW = 2, CS_DBLCLKS = 8;
        private const int CW_USEDEFAULT = unchecked((int)0x80000000);
        private const int SW_SHOWNOACTIVATE = 4, IDC_ARROW = 32512, BLACK_BRUSH = 4, GWL_STYLE = -16;
        private const uint WM_DESTROY = 0x0002, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014,
            WM_KEYDOWN = 0x0100, WM_SIZING = 0x0214, WM_LBUTTONDBLCLK = 0x0203;
        private const int VK_ESCAPE = 0x1B, VK_F11 = 0x7A;
        private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6,
            WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
        private const uint PM_REMOVE = 1, QS_ALLINPUT = 0x04FF, WAIT_OBJECT_0 = 0;
        private const int HALFTONE = 4, COLORONCOLOR = 3;
        private const uint DIB_RGB_COLORS = 0, SRCCOPY = 0x00CC0020;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20, SWP_NOOWNERZORDER = 0x200;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT { public uint length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string cls, IntPtr hInstance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint ms, uint wakeMask);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr name);
        [DllImport("user32.dll")] private static extern bool AdjustWindowRectEx(ref RECT r, uint style, bool menu, uint exStyle);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr r, bool erase);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO mi);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
        [DllImport("gdi32.dll")]
        private static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int wDest, int hDest, int xSrc, int ySrc, int wSrc, int hSrc,
            byte[] bits, ref BITMAPINFOHEADER bmi, uint usage, uint rop);

        /// <summary>Primary monitor size in physical pixels.</summary>
        public static void PrimaryMonitorSize(out int w, out int h)
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
            if (GetMonitorInfo(MonitorFromWindow(IntPtr.Zero, 1 /* PRIMARY */), ref mi))
            {
                w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            }
            else { w = 1920; h = 1080; }
        }
    }
}
