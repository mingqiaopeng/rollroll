// ============================================================================
//  RollRoll - 配置对象与持久化
//
//  配置写在 %APPDATA%\RollRoll\settings.ini（UTF-8）。
//  写盘走「临时文件 + 原子替换」，避免断电/被杀时留下半截文件。
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RollRoll
{
    internal enum ScrollEngineKind
    {
        /// <summary>SendInput 注入真实滚轮事件。兼容性最好，但会占用光标/前台。</summary>
        RealInput = 0,

        /// <summary>PostMessage 投递 WM_MOUSEWHEEL。后台静默，现代应用可能忽略。</summary>
        BackgroundMessage = 1
    }

    /// <summary>
    /// 拟人扰动强度：随机间隔 + 随机步长 + 偶发停顿。
    /// 不是均匀噪声 —— 目标是让滚动像人手的节奏，而不是精确的节拍器。
    /// </summary>
    internal enum HumanizeLevel
    {
        Off = 0,
        Light = 1,
        Natural = 2,
        Strong = 3
    }

    internal sealed class Options
    {
        internal bool ScrollUp;
        internal int NotchesPerSecond = 8;
        internal int NotchesPerStep = 1;
        internal ScrollEngineKind Engine = ScrollEngineKind.RealInput;
        internal HumanizeLevel Humanize = HumanizeLevel.Natural;

        /// <summary>真实输入模式下把光标停靠到目标中心，否则要求用户自己把鼠标放上去。</summary>
        internal bool ParkCursor = true;

        /// <summary>停靠期间用户动鼠标就自动停止，避免和人抢鼠标。</summary>
        internal bool PauseOnMouseMove = true;

        internal bool RestoreCursor = true;
        internal bool RestoreFocus = true;

        /// <summary>0 = 不限时。非 0 时达到分钟数自动停止。</summary>
        internal int MaxMinutes;

        /// <summary>目标窗口签名：进程名 + 类名。句柄跨进程不通用，重开时靠这个找回。</summary>
        internal string TargetExe = "";
        internal string TargetClass = "";

        internal Options Clone()
        {
            Options o = new Options();
            o.ScrollUp = ScrollUp;
            o.NotchesPerSecond = NotchesPerSecond;
            o.NotchesPerStep = NotchesPerStep;
            o.Engine = Engine;
            o.Humanize = Humanize;
            o.ParkCursor = ParkCursor;
            o.PauseOnMouseMove = PauseOnMouseMove;
            o.RestoreCursor = RestoreCursor;
            o.RestoreFocus = RestoreFocus;
            o.MaxMinutes = MaxMinutes;
            o.TargetExe = TargetExe;
            o.TargetClass = TargetClass;
            return o;
        }

        internal void Clamp()
        {
            if (NotchesPerSecond < 1) NotchesPerSecond = 1;
            if (NotchesPerSecond > 30) NotchesPerSecond = 30;
            if (NotchesPerStep < 1) NotchesPerStep = 1;
            if (NotchesPerStep > 5) NotchesPerStep = 5;
            if (MaxMinutes < 0) MaxMinutes = 0;
            if (Engine != ScrollEngineKind.BackgroundMessage) Engine = ScrollEngineKind.RealInput;

            int h = (int)Humanize;
            if (h < 0) h = 0;
            if (h > 3) h = 3;
            Humanize = (HumanizeLevel)h;

            if (TargetExe == null) TargetExe = "";
            if (TargetClass == null) TargetClass = "";
        }

        internal string Describe()
        {
            return string.Format(
                "dir={0} speed={1}/s step={2} engine={3} humanize={4} park={5} pauseOnMove={6} target={7}|{8}",
                ScrollUp ? "up" : "down", NotchesPerSecond, NotchesPerStep,
                Engine == ScrollEngineKind.RealInput ? "input" : "message",
                Humanize, ParkCursor, PauseOnMouseMove, TargetExe, TargetClass);
        }
    }

    internal static class Settings
    {
        internal static readonly Options Current = new Options();

        /// <summary>本次启动前没有配置文件 —— 用来决定是否给一次新手提示。</summary>
        internal static bool FirstRun;

        internal static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RollRoll");
                return Path.Combine(dir, "settings.ini");
            }
        }

        internal static void Load()
        {
            Options o = new Options();
            bool exists = false;

            try
            {
                string path = FilePath;
                exists = File.Exists(path);
                if (exists)
                {
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;

                        int i = line.IndexOf('=');
                        if (i <= 0) continue;

                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();

                        switch (k)
                        {
                            case "Direction":    o.ScrollUp = v == "up"; break;
                            case "Speed":        o.NotchesPerSecond = ParseInt(v, o.NotchesPerSecond); break;
                            case "Step":         o.NotchesPerStep = ParseInt(v, o.NotchesPerStep); break;
                            case "Engine":       o.Engine = v == "message" ? ScrollEngineKind.BackgroundMessage : ScrollEngineKind.RealInput; break;
                            case "Humanize":     o.Humanize = (HumanizeLevel)ParseInt(v, (int)o.Humanize); break;
                            case "Park":         o.ParkCursor = v == "1"; break;
                            case "PauseOnMove":  o.PauseOnMouseMove = v == "1"; break;
                            case "RestoreCursor": o.RestoreCursor = v == "1"; break;
                            case "RestoreFocus": o.RestoreFocus = v == "1"; break;
                            case "MaxMinutes":   o.MaxMinutes = ParseInt(v, o.MaxMinutes); break;
                            case "TargetExe":    o.TargetExe = v; break;
                            case "TargetClass":  o.TargetClass = v; break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取配置失败（改用默认值）: " + ex.Message);
                o = new Options();
            }

            o.Clamp();
            Copy(o, Current);
            FirstRun = !exists;

            Log.Info("配置载入 | " + Current.Describe() + " | firstRun=" + FirstRun);
        }

        /// <summary>把 UI 上的当前值落盘。失败只记日志。</summary>
        internal static void Save()
        {
            Current.Clamp();

            string path = FilePath;
            string tmp = path + ".tmp";
            string bak = path + ".bak";

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder(512);
                sb.Append("# RollRoll 配置").Append(Environment.NewLine);
                sb.Append("Direction=").Append(Current.ScrollUp ? "up" : "down").Append(Environment.NewLine);
                sb.Append("Speed=").Append(Current.NotchesPerSecond.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                sb.Append("Step=").Append(Current.NotchesPerStep.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                sb.Append("Engine=").Append(Current.Engine == ScrollEngineKind.BackgroundMessage ? "message" : "input").Append(Environment.NewLine);
                sb.Append("Humanize=").Append(((int)Current.Humanize).ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                sb.Append("Park=").Append(Current.ParkCursor ? "1" : "0").Append(Environment.NewLine);
                sb.Append("PauseOnMove=").Append(Current.PauseOnMouseMove ? "1" : "0").Append(Environment.NewLine);
                sb.Append("RestoreCursor=").Append(Current.RestoreCursor ? "1" : "0").Append(Environment.NewLine);
                sb.Append("RestoreFocus=").Append(Current.RestoreFocus ? "1" : "0").Append(Environment.NewLine);
                sb.Append("MaxMinutes=").Append(Current.MaxMinutes.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                sb.Append("TargetExe=").Append(Current.TargetExe).Append(Environment.NewLine);
                sb.Append("TargetClass=").Append(Current.TargetClass).Append(Environment.NewLine);

                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Replace(tmp, path, bak);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("保存配置失败: " + ex.Message);
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }

        private static void Copy(Options from, Options to)
        {
            to.ScrollUp = from.ScrollUp;
            to.NotchesPerSecond = from.NotchesPerSecond;
            to.NotchesPerStep = from.NotchesPerStep;
            to.Engine = from.Engine;
            to.Humanize = from.Humanize;
            to.ParkCursor = from.ParkCursor;
            to.PauseOnMouseMove = from.PauseOnMouseMove;
            to.RestoreCursor = from.RestoreCursor;
            to.RestoreFocus = from.RestoreFocus;
            to.MaxMinutes = from.MaxMinutes;
            to.TargetExe = from.TargetExe;
            to.TargetClass = from.TargetClass;
        }
    }
}
