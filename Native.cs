// ============================================================================
//  RollRoll - Win32 互操作
//
//  只放声明与两个小工具方法，不放业务逻辑。所有 SetLastError 的调用失败时
//  都通过 LastWin32Error() 翻译，错误码不会被 CLR 的还原机制吃掉。
// ============================================================================

using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace RollRoll
{
    internal static class Native
    {
        // ---- 消息 ----
        internal const int WM_MOUSEWHEEL = 0x020A;
        internal const int WM_HOTKEY     = 0x0312;

        // ---- SendInput ----
        internal const uint INPUT_MOUSE       = 0;
        internal const uint MOUSEEVENTF_WHEEL = 0x0800;

        // ---- 热键 ----
        internal const int MOD_ALT      = 0x0001;
        internal const int MOD_CONTROL  = 0x0002;
        internal const int MOD_SHIFT    = 0x0004;
        internal const int MOD_WIN      = 0x0008;
        internal const int MOD_NOREPEAT = 0x4000;

        // ---- 窗口 ----
        internal const int  SW_RESTORE = 9;
        internal const uint GA_ROOT    = 2;

        internal const uint CWP_SKIPINVISIBLE   = 0x0001;
        internal const uint CWP_SKIPDISABLED    = 0x0002;
        internal const uint CWP_SKIPTRANSPARENT = 0x0004;

        internal static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        internal static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        internal const uint SWP_NOSIZE     = 0x0001;
        internal const uint SWP_NOMOVE     = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;

        // ---- 进程 / 令牌 ----
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        internal const uint TOKEN_QUERY = 0x0008;
        internal const int  TokenElevation = 20;

        // ---- 系统度量 ----
        internal const int SM_CXSMICON = 49;
        internal const int SM_CYSMICON = 50;

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;

            internal POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx;
            internal int dy;
            internal uint mouseData;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        // 只用到鼠标事件，不需要显式 union：x64 下 MOUSEINPUT 自带 8 字节对齐，
        // INPUT 为 40 字节；x86 下为 28 字节。尺寸在 SelfTest 里会打印出来核对。
        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            internal uint type;
            internal MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TOKEN_ELEVATION
        {
            internal int TokenIsElevated;
        }

        internal static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // ---- user32 ----
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo,
            [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        // ---- kernel32 ----
        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags,
            StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachConsole(int dwProcessId);
        // ---- advapi32 ----
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess,
            out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
            out TOKEN_ELEVATION TokenInformation, int TokenInformationLength, out int ReturnLength);

        // ---- winmm ----
        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint uPeriod);

        // ---- 小工具 ----

        /// <summary>当前光标位置（屏幕坐标）。</summary>
        internal static Point CursorPosition
        {
            get
            {
                POINT p;
                if (!GetCursorPos(out p)) return Point.Empty;
                return new Point(p.X, p.Y);
            }
        }

        /// <summary>把 Win32 错误码翻成人话。</summary>
        internal static string LastWin32Error()
        {
            int code = Marshal.GetLastWin32Error();
            if (code == 0) return "无错误信息";
            return string.Format("{0} (0x{1:X})", new Win32Exception(code).Message, code);
        }

        /// <summary>取窗口类名，失败返回空串。</summary>
        internal static string ClassNameOf(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(256);
            int n = GetClassName(hWnd, sb, sb.Capacity);
            if (n <= 0) return string.Empty;
            return sb.ToString(0, n);
        }

        /// <summary>取窗口标题，失败返回空串。</summary>
        internal static string TitleOf(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return string.Empty;
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return string.Empty;
            StringBuilder sb = new StringBuilder(len + 1);
            int n = GetWindowText(hWnd, sb, sb.Capacity);
            if (n <= 0) return string.Empty;
            return sb.ToString(0, n);
        }
    }
}
