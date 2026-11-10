// ============================================================================
//  RollRoll - 滚动控制器
//
//  职责：把「一次滚动会话」的生命周期收进一个后台线程。
//  状态只从外部看两件事：IsRunning 与 StopReason；计数与耗时由 UI 轮询。
//
//  线程模型：
//    Start/Stop 在 UI 线程调用；
//    Cleanup 只在工作线程执行（Stop 只负责置取消事件），
//    因此光标/前台还原不会和滚轮发送并发。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace RollRoll
{
    internal sealed class ScrollController : IDisposable
    {
        private readonly object _gate = new object();

        private Thread _thread;
        private IScrollEngine _engine;
        private WindowInfo _target;
        private Options _opt;
        private ManualResetEvent _cancel;
        private Stopwatch _clock;

        private long _notches;
        private volatile bool _running;
        private volatile string _stopReason = "";
        private volatile string _manualReason;

        private bool _parked;
        private Point _parkedAt;
        private Point _originCursor;
        private IntPtr _originForeground;
        private bool _disposed;

        /// <summary>可能在任意线程触发；订阅方自行 BeginInvoke 回 UI 线程。</summary>
        internal event EventHandler Changed;

        internal bool IsRunning { get { return _running; } }
        internal long Notches { get { return Interlocked.Read(ref _notches); } }
        internal string StopReason { get { return _stopReason; } }

        internal TimeSpan Elapsed
        {
            get
            {
                Stopwatch s = _clock;
                return s == null ? TimeSpan.Zero : s.Elapsed;
            }
        }

        internal bool Start(WindowInfo target, Options options, out string error)
        {
            error = null;

            lock (_gate)
            {
                if (_running) { error = "已经在滚动中"; return false; }

                Thread old = _thread;
                if (old != null && old.IsAlive && !old.Join(600))
                {
                    error = "上一轮还在停止，请稍候";
                    return false;
                }

                if (!WindowInfo.IsAlive(target))
                {
                    error = "目标窗口已不存在，请重新拾取";
                    return false;
                }

                Options o = options.Clone();
                o.Clamp();

                if (Native.IsIconic(target.Hwnd))
                {
                    Native.ShowWindow(target.Hwnd, Native.SW_RESTORE);
                    Thread.Sleep(150);
                }

                IScrollEngine engine = o.Engine == ScrollEngineKind.BackgroundMessage
                    ? (IScrollEngine)new MessageEngine()
                    : new InputEngine();

                _target = target;
                _opt = o;
                _originCursor = Native.CursorPosition;
                _originForeground = Native.GetForegroundWindow();
                _parked = false;
                _parkedAt = Point.Empty;

                if (o.Engine == ScrollEngineKind.RealInput)
                {
                    if (!EnsureForeground(target.Hwnd))
                    {
                        error = "无法把目标窗口切到前台。请先手动点一下目标窗口，再按 F8。";
                        UndoVisuals(64);
                        return false;
                    }

                    if (o.ParkCursor)
                    {
                        Native.POINT c = target.CenterScreen();
                        Native.SetCursorPos(c.X, c.Y);
                        Thread.Sleep(20);

                        Point now = Native.CursorPosition;
                        _parkedAt = new Point(c.X, c.Y);
                        _parked = Math.Abs(now.X - c.X) <= 3 && Math.Abs(now.Y - c.Y) <= 3;

                        if (!_parked)
                        {
                            error = "光标没有落到目标窗口上。请改用「后台消息」模式，或检查是否有程序锁定了光标。";
                            UndoVisuals(64);
                            return false;
                        }
                    }
                    else
                    {
                        Native.POINT cur = new Native.POINT(_originCursor.X, _originCursor.Y);
                        IntPtr under = WindowInfo.Root(Native.WindowFromPoint(cur));
                        if (under != target.Hwnd)
                        {
                            error = "「真实输入」要求鼠标停在目标窗口上：请把鼠标移过去，或勾选「停靠光标」。";
                            UndoVisuals(64);
                            return false;
                        }
                    }
                }

                try
                {
                    engine.Prepare(target, o);
                }
                catch (Exception ex)
                {
                    engine.Release();
                    error = ex.Message;
                    UndoVisuals(64);
                    return false;
                }

                _engine = engine;
                _manualReason = null;
                _stopReason = "";
                Interlocked.Exchange(ref _notches, 0);
                _clock = Stopwatch.StartNew();
                _cancel = new ManualResetEvent(false);
                _running = true;

                Native.timeBeginPeriod(1);

                _thread = new Thread(Run);
                _thread.IsBackground = true;
                _thread.Name = "RollRoll.Scroll";
                _thread.Start();

                Log.Info("开始 | " + o.Describe() + " | " + target.Caption);
                return true;
            }
        }

        internal void Stop(string reason)
        {
            ManualResetEvent ev = _cancel;
            if (!_running || ev == null) return;

            _manualReason = reason;
            try { ev.Set(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop("退出");

            Thread t = _thread;
            if (t != null && t.IsAlive) t.Join(1500);
        }

        // ---- 工作线程 ----

        private void Run()
        {
            string failure = null;
            try
            {
                Loop();
            }
            catch (Exception ex)
            {
                failure = "滚动失败：" + ex.Message;
                Log.Error(failure);
            }
            finally
            {
                Cleanup(failure);
            }
        }

        private void Loop()
        {
            // 速度是「格/秒」，而每一拍可以发多格，所以间隔要乘上每步格数。
            // 旧算法漏了这一步，8 格/秒 × 每步 2 格 实际跑成了 16 格/秒。
            double baseInterval = 1000.0 * _opt.NotchesPerStep / _opt.NotchesPerSecond;
            if (baseInterval < 15.0) baseInterval = 15.0;

            int sign = _opt.ScrollUp ? 1 : -1;
            int stepSize = _opt.NotchesPerStep;

            // 拟人参数：interval 抖动（相对标准差）、步长抖动、停顿频率（次/秒）与毫秒范围。
            // 停顿按「每秒几次」而不是「每拍几成」来计算，否则速度调得越快停顿越密，
            // 实际平均速度会被拖得明显低于设定值。
            double jitter = 0;
            double stepJitter = 0;
            double pausesPerSecond = 0;
            int pauseMin = 0;
            int pauseMax = 0;

            switch (_opt.Humanize)
            {
                case HumanizeLevel.Light:
                    jitter = 0.10; stepJitter = 0.15; pausesPerSecond = 0.10;
                    pauseMin = 60; pauseMax = 150;
                    break;
                case HumanizeLevel.Strong:
                    jitter = 0.40; stepJitter = 0.45; pausesPerSecond = 0.60;
                    pauseMin = 120; pauseMax = 420;
                    break;
                case HumanizeLevel.Natural:
                    jitter = 0.22; stepJitter = 0.30; pausesPerSecond = 0.30;
                    pauseMin = 80; pauseMax = 260;
                    break;
            }

            bool humanized = _opt.Humanize != HumanizeLevel.Off;
            Random rng = new Random(unchecked(Environment.TickCount * 31 + (int)DateTime.UtcNow.Ticks));

            // 起步时慢一拍：前 4 拍把间隔放长，避免「一按就开始匀速狂奔」。
            int rampLeft = humanized ? 4 : 0;

            // 小数步长累加器：随机步长靠它把小数部分留到下一拍，长跑平均速度不变。
            double carry = 0;

            // 计划时刻用小数毫秒累计，避免每拍取整带来长期速度漂移。
            double due = 0;

            while (!_cancel.WaitOne(0))
            {
                if (!Native.IsWindow(_target.Hwnd))
                {
                    _manualReason = "目标窗口已关闭";
                    return;
                }

                if (Native.IsIconic(_target.Hwnd))
                {
                    _manualReason = "目标窗口被最小化";
                    return;
                }

                if (_parked && _opt.PauseOnMouseMove && CursorMovedAway())
                {
                    _manualReason = "检测到鼠标移动，已自动停止";
                    return;
                }

                if (_opt.MaxMinutes > 0 && _clock.Elapsed.TotalMinutes >= _opt.MaxMinutes)
                {
                    _manualReason = "已达到最长运行时间";
                    return;
                }

                double interval = baseInterval;
                if (jitter > 0)
                {
                    interval *= 1.0 + jitter * Gaussian(rng);
                    if (interval < 5.0) interval = 5.0;
                }

                int n;
                if (stepJitter > 0)
                {
                    double raw = stepSize * (1.0 + stepJitter * Gaussian(rng));
                    // 步长随机到负值没有意义，还会让累加器「欠账」后连着几拍不发。
                    if (raw < 0.05) raw = 0.05;

                    carry += raw;
                    n = (int)Math.Round(carry);
                    carry -= n;
                }
                else
                {
                    n = stepSize;
                }

                if (n > 0)
                {
                    _engine.Step(sign * n);
                    Interlocked.Add(ref _notches, n);
                }

                double pauseProb = pausesPerSecond * interval / 1000.0;
                if (pauseProb > 0 && rng.NextDouble() < pauseProb)
                    interval += rng.Next(pauseMin, pauseMax);

                if (rampLeft > 0)
                {
                    interval *= 1.0 + 0.10 * rampLeft;
                    rampLeft--;
                }

                due += interval;
                long wait = (long)Math.Round(due) - _clock.ElapsedMilliseconds;
                if (wait < 1)
                {
                    // 落后于计划就重置基准，不要试图补帧 —— 补帧只会把消息队列冲爆。
                    due = _clock.ElapsedMilliseconds;
                    wait = 1;
                }

                if (_cancel.WaitOne((int)wait)) return;
            }
        }

        /// <summary>
        /// 三个均匀分布之和近似正态：均值 1.5、标准差 0.5，标准化后大致落在 [-3, 3]。
        /// 比 Box-Muller 便宜，且不会出现极端值打乱节奏。
        /// </summary>
        private static double Gaussian(Random rng)
        {
            return 2.0 * (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5);
        }

        private bool CursorMovedAway()
        {
            Point now = Native.CursorPosition;
            return Math.Abs(now.X - _parkedAt.X) > 6 || Math.Abs(now.Y - _parkedAt.Y) > 6;
        }

        private void Cleanup(string failure)
        {
            string reason = failure;
            if (reason == null) reason = _manualReason;
            if (reason == null) reason = "已停止";

            try { if (_engine != null) _engine.Release(); }
            catch { }

            UndoVisuals(8);

            try { Native.timeEndPeriod(1); }
            catch { }

            _running = false;
            _engine = null;
            _stopReason = reason;

            Log.Info("停止 | " + reason + " | notches=" + _notches + " | " + _clock.Elapsed);

            ManualResetEvent ev = _cancel;
            _cancel = null;
            if (ev != null) ev.Close();

            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>
        /// 还原光标与前台窗口。threshold 是「光标还算停在原处」的容差：
        /// 停止时用 8px，启动失败回滚时用 64px（此时用户可能已经手动挪开）。
        /// </summary>
        private void UndoVisuals(int threshold)
        {
            try
            {
                if (_parked && _opt != null && _opt.RestoreCursor)
                {
                    Point now = Native.CursorPosition;
                    if (Math.Abs(now.X - _parkedAt.X) <= threshold &&
                        Math.Abs(now.Y - _parkedAt.Y) <= threshold)
                        Native.SetCursorPos(_originCursor.X, _originCursor.Y);
                }
            }
            catch { }

            try
            {
                if (_opt != null && _opt.RestoreFocus && _originForeground != IntPtr.Zero &&
                    _target != null &&
                    WindowInfo.Root(Native.GetForegroundWindow()) == _target.Hwnd)
                    Native.SetForegroundWindow(_originForeground);
            }
            catch { }

            _parked = false;
        }

        // ---- 前台 ----

        private static bool EnsureForeground(IntPtr hwnd)
        {
            if (WindowInfo.Root(Native.GetForegroundWindow()) == hwnd) return true;

            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);

            IntPtr fg = Native.GetForegroundWindow();
            uint pid;
            uint fgThread = fg == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(fg, out pid);
            uint myThread = Native.GetCurrentThreadId();

            bool attached = false;
            if (fgThread != 0 && fgThread != myThread)
                attached = Native.AttachThreadInput(myThread, fgThread, true);

            Native.SetForegroundWindow(hwnd);

            if (attached)
                Native.AttachThreadInput(myThread, fgThread, false);

            Thread.Sleep(50);
            if (WindowInfo.Root(Native.GetForegroundWindow()) == hwnd) return true;

            // 兜底：短暂置顶再取消置顶，可以绕过系统的前台锁定。
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE);

            Native.SetForegroundWindow(hwnd);
            Thread.Sleep(80);
            return WindowInfo.Root(Native.GetForegroundWindow()) == hwnd;
        }
    }
}
