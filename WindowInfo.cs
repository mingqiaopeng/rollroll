// ============================================================================
//  RollRoll - 目标窗口模型
//
//  句柄只在进程生命周期内有效，所以除了 Hwnd 之外还记录「进程名 + 类名」签名，
//  下次启动时可以按签名把窗口找回来，避免每次都要重新拾取。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RollRoll
{
    internal sealed class WindowInfo
    {
        internal IntPtr Hwnd;
        internal string Title = "";
        internal string ClassName = "";
        internal string ExeName = "";
        internal string ExePath = "";
        internal int ProcessId;
        internal bool Elevated;
        internal Rectangle Bounds;

        /// <summary>标题可能随文档变化，展示时现取。</summary>
        internal string LiveTitle
        {
            get
            {
                if (!IsAlive(this)) return Title;
                string t = Native.TitleOf(Hwnd);
                if (t.Length > 0) Title = t;
                return Title;
            }
        }

        internal string Caption
        {
            get
            {
                string t = LiveTitle;
                if (string.IsNullOrEmpty(t)) t = "（无标题）";
                return t;
            }
        }

        internal string Detail
        {
            get
            {
                return string.Format("{0}  ·  {1}  ·  PID {2}{3}",
                    string.IsNullOrEmpty(ExeName) ? "未知进程" : ExeName,
                    string.IsNullOrEmpty(ClassName) ? "未知类" : ClassName,
                    ProcessId,
                    Elevated ? "  ·  管理员" : "");
            }
        }

        internal static bool IsAlive(WindowInfo w)
        {
            return w != null && w.Hwnd != IntPtr.Zero && Native.IsWindow(w.Hwnd);
        }

        internal static IntPtr Root(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            return Native.GetAncestor(hwnd, Native.GA_ROOT);
        }

        internal static WindowInfo FromHandle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;

            WindowInfo w = new WindowInfo();
            w.Hwnd = hwnd;
            w.Title = Native.TitleOf(hwnd);
            w.ClassName = Native.ClassNameOf(hwnd);

            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            w.ProcessId = (int)pid;

            Native.RECT r;
            if (Native.GetWindowRect(hwnd, out r))
                w.Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);

            FillProcessInfo(w);
            return w;
        }

        /// <summary>目标客户区中心（屏幕坐标），并夹到虚拟桌面范围内。</summary>
        internal Native.POINT CenterScreen()
        {
            Native.RECT cr;
            int cx, cy;

            if (Native.GetClientRect(Hwnd, out cr) && cr.Right - cr.Left >= 8 && cr.Bottom - cr.Top >= 8)
            {
                cx = (cr.Left + cr.Right) / 2;
                cy = (cr.Top + cr.Bottom) / 2;
            }
            else
            {
                Native.RECT wr;
                if (!Native.GetWindowRect(Hwnd, out wr))
                {
                    Rectangle v = SystemInformation.VirtualScreen;
                    return new Native.POINT(v.Left + v.Width / 2, v.Top + v.Height / 2);
                }
                cx = (wr.Left + wr.Right) / 2;
                cy = (wr.Top + wr.Bottom) / 2;
            }

            Native.POINT p = new Native.POINT(cx, cy);
            Native.ClientToScreen(Hwnd, ref p);

            Rectangle vs = SystemInformation.VirtualScreen;
            if (p.X < vs.Left + 1) p.X = vs.Left + 1;
            if (p.Y < vs.Top + 1) p.Y = vs.Top + 1;
            if (p.X > vs.Right - 2) p.X = vs.Right - 2;
            if (p.Y > vs.Bottom - 2) p.Y = vs.Bottom - 2;
            return p;
        }

        /// <summary>按「进程名 + 类名」找回上次的目标窗口。</summary>
        internal static WindowInfo FindBySignature(string exeName, string className)
        {
            if (string.IsNullOrEmpty(exeName) && string.IsNullOrEmpty(className)) return null;

            IntPtr found = IntPtr.Zero;
            string myExe = CurrentExeName();

            Native.EnumWindowsProc cb = delegate (IntPtr h, IntPtr lp)
            {
                try
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    if (Native.IsIconic(h)) return true;

                    string cls = Native.ClassNameOf(h);
                    if (!string.IsNullOrEmpty(className) &&
                        !string.Equals(cls, className, StringComparison.OrdinalIgnoreCase))
                        return true;

                    WindowInfo info = FromHandle(h);
                    if (info == null) return true;

                    if (!string.IsNullOrEmpty(exeName) &&
                        !string.Equals(info.ExeName, exeName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (!string.IsNullOrEmpty(myExe) &&
                        string.Equals(info.ExeName, myExe, StringComparison.OrdinalIgnoreCase))
                        return true;

                    found = h;
                    return false;
                }
                catch
                {
                    return true;
                }
            };

            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);

            if (found == IntPtr.Zero) return null;
            return FromHandle(found);
        }

        /// <summary>拾取阶段过滤掉桌面、任务栏和本程序自己的窗口。</summary>
        internal static bool IsSuitable(WindowInfo w)
        {
            if (!IsAlive(w)) return false;
            if (!Native.IsWindowVisible(w.Hwnd)) return false;
            if (w.ProcessId == 0) return false;

            string cls = w.ClassName;
            if (cls == "Progman" || cls == "WorkerW" ||
                cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                return false;

            if (string.Equals(w.ExeName, CurrentExeName(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (w.Bounds.Width < 40 || w.Bounds.Height < 40) return false;
            return true;
        }

        internal static bool IsCurrentElevated()
        {
            IntPtr token;
            if (!Native.OpenProcessToken(Native.GetCurrentProcess(), Native.TOKEN_QUERY, out token))
                return false;
            try
            {
                Native.TOKEN_ELEVATION e;
                int size;
                if (Native.GetTokenInformation(token, Native.TokenElevation, out e,
                        System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.TOKEN_ELEVATION)), out size))
                    return e.TokenIsElevated != 0;
                return false;
            }
            finally
            {
                Native.CloseHandle(token);
            }
        }

        private static void FillProcessInfo(WindowInfo w)
        {
            w.ExeName = "";
            w.ExePath = "";

            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, w.ProcessId);
            if (h != IntPtr.Zero)
            {
                try
                {
                    int cap = 1024;
                    StringBuilder sb = new StringBuilder(cap);
                    if (Native.QueryFullProcessImageName(h, 0, sb, ref cap))
                    {
                        w.ExePath = sb.ToString();
                        w.ExeName = System.IO.Path.GetFileName(w.ExePath);
                    }

                    IntPtr token;
                    if (Native.OpenProcessToken(h, Native.TOKEN_QUERY, out token))
                    {
                        try
                        {
                            Native.TOKEN_ELEVATION e;
                            int size;
                            if (Native.GetTokenInformation(token, Native.TokenElevation, out e,
                                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.TOKEN_ELEVATION)), out size))
                                w.Elevated = e.TokenIsElevated != 0;
                        }
                        finally
                        {
                            Native.CloseHandle(token);
                        }
                    }
                }
                finally
                {
                    Native.CloseHandle(h);
                }
            }

            if (w.ExeName.Length == 0)
            {
                // 受保护进程拿不到句柄，退回托管 API；这里失败很常见，不当错误。
                try
                {
                    Process p = Process.GetProcessById(w.ProcessId);
                    w.ExeName = p.ProcessName + ".exe";
                }
                catch
                {
                }
            }
        }

        private static string CurrentExeName()
        {
            try
            {
                return Process.GetCurrentProcess().ProcessName + ".exe";
            }
            catch
            {
                return "RollRoll.exe";
            }
        }
    }
}

