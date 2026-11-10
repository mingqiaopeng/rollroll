// ============================================================================
//  RollRoll - 程序入口
//
//  启动模式:
//    (默认)       显示主面板，托盘常驻
//    --selftest   打印诊断信息（互操作尺寸、前台窗口、子窗口解析）后退出
// ============================================================================

using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RollRoll
{
    internal static class Program
    {
        private const string MutexName = @"Local\RollRoll.SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
            if (HasArg(args, "--selftest")) return SelfTest.Run();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Settings.Load();

            Mutex mtx = AcquireMutex();
            if (mtx == null)
            {
                MessageBox.Show("RollRoll 已经在运行，请查看系统托盘。",
                    "RollRoll", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            try
            {
                MainForm form = new MainForm();
                TrayController tray = new TrayController(form);
                form.AttachTray(tray);

                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("未处理异常: " + ex);
                MessageBox.Show("RollRoll 启动失败：\r\n" + ex.Message,
                    "RollRoll", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                try { mtx.ReleaseMutex(); } catch { }
                mtx.Dispose();
            }
        }

        /// <summary>
        /// 以管理员身份重启时会有一瞬间两个进程并存，所以这里最多等 3 秒，
        /// 而不是一看名字被占用就报「已在运行」。
        /// </summary>
        private static Mutex AcquireMutex()
        {
            for (int i = 0; i < 30; i++)
            {
                bool created;
                Mutex m = new Mutex(true, MutexName, out created);
                if (created) return m;
                m.Dispose();
                Thread.Sleep(100);
            }
            return null;
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal static class SelfTest
    {
        internal static int Run()
        {
            StringBuilder sb = new StringBuilder();
            Action<string> line = delegate (string s) { sb.AppendLine(s); };

            line("RollRoll self-test");
            line("time      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            line("os        : " + Environment.OSVersion.VersionString);
            line("process   : " + (IntPtr.Size == 8 ? "x64" : "x86"));
            line("input size: " + Native.InputSize + " (expect " + (IntPtr.Size == 8 ? 40 : 28) + ")");
            line("settings  : " + Settings.FilePath);

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    line("dpi       : " + g.DpiX);
            }
            catch (Exception ex)
            {
                line("dpi       : <" + ex.Message + ">");
            }

            IntPtr fg = Native.GetForegroundWindow();
            line("foreground: 0x" + fg.ToInt64().ToString("X") +
                 " [" + Native.ClassNameOf(fg) + "] " + Native.TitleOf(fg));

            WindowInfo info = WindowInfo.FromHandle(fg);
            if (info != null)
            {
                line("target    : " + info.Detail);
                Native.POINT c = info.CenterScreen();
                line("center    : " + c.X + "," + c.Y);

                IntPtr h = fg;
                for (int i = 0; i < 32; i++)
                {
                    Native.POINT p = c;
                    if (!Native.ScreenToClient(h, ref p)) break;
                    IntPtr next = Native.ChildWindowFromPointEx(h, p,
                        Native.CWP_SKIPINVISIBLE | Native.CWP_SKIPDISABLED | Native.CWP_SKIPTRANSPARENT);
                    if (next == IntPtr.Zero || next == h) break;
                    h = next;
                }
                line("scroll hwnd: 0x" + h.ToInt64().ToString("X") +
                     " [" + Native.ClassNameOf(h) + "]");
            }

            int count = 0;
            Native.EnumWindowsProc cb = delegate (IntPtr w, IntPtr lp)
            {
                if (Native.IsWindowVisible(w)) count++;
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            line("windows   : " + count + " visible top-level");
            line("elevated  : " + WindowInfo.IsCurrentElevated());

            string text = sb.ToString();

            // 从控制台启动时标准输出可用；双击启动时这里静默失败，交给下面的文件。
            try { Console.Write(text); }
            catch { }

            string outPath = Path.Combine(Path.GetTempPath(), "rollroll-selftest.txt");
            try
            {
                File.WriteAllText(outPath, text, new UTF8Encoding(false));
                Console.WriteLine("written   : " + outPath);
            }
            catch
            {
            }

            return 0;
        }
    }
}
