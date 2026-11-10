// ============================================================================
//  RollRoll - 轻量日志
//
//  自用工具不需要日志框架，但「为什么没滚动」是最常见的求助场景，
//  所以保留一个带上限的文本日志：%APPDATA%\RollRoll\log.txt
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace RollRoll
{
    internal static class Log
    {
        private const long MaxBytes = 256 * 1024;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RollRoll");
                return Path.Combine(dir, "log.txt");
            }
        }

        internal static void Info(string msg) { Write("INFO ", msg); }
        internal static void Warn(string msg) { Write("WARN ", msg); }
        internal static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    if (msg == null) msg = "";

                    // GetWindowText 对 EDIT 控件会返回**全部正文**，所以任何来自
                    // 窗口标题的字符串都可能长到离谱。日志行必须强制压平、截断。
                    msg = msg.Replace("\r", " ").Replace("\n", " ");
                    if (msg.Length > 400) msg = msg.Substring(0, 400) + "...";

                    string path = FilePath;
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    RotateIfNeeded(path);

                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}",
                        DateTime.Now, level, msg);
                    File.AppendAllText(path, line + Environment.NewLine, Utf8NoBom);
                }
            }
            catch
            {
                // 日志写不进去不是业务错误，绝不允许它把程序带崩。
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxBytes) return;

                string old = path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch
            {
            }
        }
    }
}
