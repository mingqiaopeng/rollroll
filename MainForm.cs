// ============================================================================
//  RollRoll - 主面板
//
//  一块固定尺寸的小面板：目标、方向、速度、方式、选项、开始/停止。
//  关窗只隐藏到托盘；点「退出」才真正结束。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace RollRoll
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyToggleId = 0x5A01;
        private const int HotkeyStopId   = 0x5A02;
        private const int VkF8 = 0x77;
        private const int VkF9 = 0x78;

        private readonly ScrollController _controller = new ScrollController();
        private readonly Options _opt = Settings.Current;

        private TrayController _tray;
        private WindowInfo _target;

        private Label _lblTarget;
        private Label _lblTargetMeta;
        private Label _lblSpeedValue;
        private Label _lblStatus;
        private Label _lblHint;
        private Chip _chipDown;
        private Chip _chipUp;
        private Chip _chipHumanizeOff;
        private Chip _chipHumanizeLight;
        private Chip _chipHumanizeNatural;
        private Chip _chipHumanizeStrong;
        private Slider _trackSpeed;
        private NumericUpDown _numStep;
        private ComboBox _cmbEngine;
        private CheckBox _chkPark;
        private CheckBox _chkPause;
        private ActionButton _btnStart;
        private ActionButton _btnPick;
        private Timer _uiTimer;

        private bool _exiting;
        private bool _trayHintShown;
        private bool _hotkeyWarned;
        private string _lastStop = "";

        internal MainForm()
        {
            Text = "RollRoll 滚轮助手";
            Font = UiKit.FontUi;
            BackColor = UiKit.PageBg;
            ClientSize = new Size(400, 480);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            KeyPreview = true;
            DoubleBuffered = true;
            Icon = TrayIconFactory.AppIcon;

            BuildUi();
            LoadFromOptions();
            BindEvents();

            TryRestoreTarget();
            RefreshUi();

            _uiTimer = new Timer();
            _uiTimer.Interval = 400;
            _uiTimer.Tick += OnUiTick;
            _uiTimer.Start();
        }

        // ---- 供托盘调用 ----

        internal bool IsScrolling { get { return _controller.IsRunning; } }

        internal bool CanToggle { get { return true; } }

        internal string StatusLine
        {
            get
            {
                if (_controller.IsRunning)
                    return string.Format("运行中 · 已滚 {0:N0} 格", _controller.Notches);
                if (_target != null && WindowInfo.IsAlive(_target))
                    return "目标：" + _target.ExeName;
                return "空闲 · 未指定目标";
            }
        }

        internal void AttachTray(TrayController tray)
        {
            _tray = tray;
            _tray.SetState(TrayState.Idle);
        }

        internal void ToggleScroll()
        {
            if (_controller.IsRunning)
            {
                _controller.Stop("手动停止");
                return;
            }
            StartScroll();
        }

        internal void PickTargetFromTray()
        {
            ShowPanel();
            PickTarget();
        }

        internal void ShowPanel()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            Show();
            Activate();
            BringToFront();
        }

        internal void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            Settings.Save();
            Close();
        }

        // ---- 界面构建 ----

        private void BuildUi()
        {
            Label title = UiKit.MakeLabel("RollRoll 滚轮助手", UiKit.FontTitle, UiKit.TextMain,
                new Rectangle(12, 10, 376, 26), ContentAlignment.MiddleLeft);
            Controls.Add(title);

            Label sub = UiKit.MakeLabel("向指定窗口连续发送滚轮", UiKit.FontSmall, UiKit.TextSub,
                new Rectangle(13, 36, 376, 18), ContentAlignment.MiddleLeft);
            Controls.Add(sub);

            Card card = new Card();
            card.Bounds = new Rectangle(12, 62, 376, 64);
            Controls.Add(card);

            _lblTarget = UiKit.MakeLabel("尚未指定目标窗口", UiKit.FontUiBold, UiKit.TextMain,
                new Rectangle(12, 10, 248, 20), ContentAlignment.MiddleLeft);
            card.Controls.Add(_lblTarget);

            _lblTargetMeta = UiKit.MakeLabel("点右侧按钮，3 秒内把鼠标移到目标上", UiKit.FontSmall, UiKit.TextSub,
                new Rectangle(12, 32, 248, 18), ContentAlignment.MiddleLeft);
            card.Controls.Add(_lblTargetMeta);

            _btnPick = new ActionButton();
            _btnPick.Primary = false;
            _btnPick.Font = UiKit.FontUiBold;
            _btnPick.Text = "拾取窗口";
            _btnPick.Bounds = new Rectangle(270, 15, 94, 34);
            card.Controls.Add(_btnPick);

            Controls.Add(UiKit.MakeLabel("方向", UiKit.FontUi, UiKit.TextSub,
                new Rectangle(12, 146, 44, 28), ContentAlignment.MiddleLeft));

            _chipDown = new Chip();
            _chipDown.Text = "向下";
            _chipDown.Bounds = new Rectangle(62, 144, 66, 30);
            Controls.Add(_chipDown);

            _chipUp = new Chip();
            _chipUp.Text = "向上";
            _chipUp.Bounds = new Rectangle(136, 144, 66, 30);
            Controls.Add(_chipUp);

            Controls.Add(UiKit.MakeLabel("速度", UiKit.FontUi, UiKit.TextSub,
                new Rectangle(12, 188, 44, 28), ContentAlignment.MiddleLeft));

            _trackSpeed = new Slider();
            _trackSpeed.Minimum = 1;
            _trackSpeed.Maximum = 30;
            _trackSpeed.Bounds = new Rectangle(60, 186, 242, 30);
            Controls.Add(_trackSpeed);

            _lblSpeedValue = UiKit.MakeLabel("8 格/秒", UiKit.FontUi, UiKit.TextMain,
                new Rectangle(306, 188, 70, 28), ContentAlignment.MiddleRight);
            Controls.Add(_lblSpeedValue);

            Controls.Add(UiKit.MakeLabel("每步", UiKit.FontUi, UiKit.TextSub,
                new Rectangle(12, 226, 44, 28), ContentAlignment.MiddleLeft));

            _numStep = new NumericUpDown();
            _numStep.Minimum = 1;
            _numStep.Maximum = 5;
            _numStep.Bounds = new Rectangle(62, 226, 62, 26);
            Controls.Add(_numStep);

            Controls.Add(UiKit.MakeLabel("格", UiKit.FontUi, UiKit.TextSub,
                new Rectangle(130, 226, 34, 28), ContentAlignment.MiddleLeft));

            Controls.Add(UiKit.MakeLabel("方式", UiKit.FontUi, UiKit.TextSub,
                new Rectangle(12, 264, 44, 28), ContentAlignment.MiddleLeft));

            _cmbEngine = new ComboBox();
            _cmbEngine.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbEngine.Font = UiKit.FontUi;
            _cmbEngine.Bounds = new Rectangle(62, 264, 194, 26);
            _cmbEngine.Items.Add("真实输入（兼容性好）");
            _cmbEngine.Items.Add("后台消息（不占鼠标）");
            Controls.Add(_cmbEngine);

            Controls.Add(UiKit.MakeLabel("抖动", UiKit.FontUi, UiKit.TextSub,
                new Rectangle(12, 298, 44, 28), ContentAlignment.MiddleLeft));

            _chipHumanizeOff = MakeHumanizeChip("关", 62);
            _chipHumanizeLight = MakeHumanizeChip("轻", 120);
            _chipHumanizeNatural = MakeHumanizeChip("自然", 178);
            _chipHumanizeStrong = MakeHumanizeChip("强", 236);

            _chkPark = new CheckBox();
            _chkPark.Text = "停靠光标";
            _chkPark.Font = UiKit.FontUi;
            _chkPark.ForeColor = UiKit.TextMain;
            _chkPark.Bounds = new Rectangle(12, 336, 110, 24);
            Controls.Add(_chkPark);

            _chkPause = new CheckBox();
            _chkPause.Text = "鼠标一动就停";
            _chkPause.Font = UiKit.FontUi;
            _chkPause.ForeColor = UiKit.TextMain;
            _chkPause.Bounds = new Rectangle(126, 336, 150, 24);
            Controls.Add(_chkPause);

            _btnStart = new ActionButton();
            _btnStart.Text = "开始  (F8)";
            _btnStart.Bounds = new Rectangle(12, 370, 376, 48);
            Controls.Add(_btnStart);

            _lblStatus = UiKit.MakeLabel("空闲", UiKit.FontUiBold, UiKit.TextSub,
                new Rectangle(12, 428, 376, 22), ContentAlignment.MiddleLeft);
            Controls.Add(_lblStatus);

            _lblHint = UiKit.MakeLabel("请先点「拾取窗口」指定目标 · F8 开始/停止", UiKit.FontSmall, UiKit.TextSub,
                new Rectangle(12, 450, 376, 20), ContentAlignment.MiddleLeft);
            Controls.Add(_lblHint);
        }

        private Chip MakeHumanizeChip(string text, int x)
        {
            Chip c = new Chip();
            c.Text = text;
            c.Bounds = new Rectangle(x, 296, 54, 30);
            Controls.Add(c);
            return c;
        }

        private void LoadFromOptions()
        {
            _trackSpeed.Value = Clamp(_opt.NotchesPerSecond, _trackSpeed.Minimum, _trackSpeed.Maximum);
            _numStep.Value = Clamp(_opt.NotchesPerStep, 1, 5);
            _cmbEngine.SelectedIndex = _opt.Engine == ScrollEngineKind.BackgroundMessage ? 1 : 0;
            _chkPark.Checked = _opt.ParkCursor;
            _chkPause.Checked = _opt.PauseOnMouseMove;
            _lblSpeedValue.Text = _opt.NotchesPerSecond + " 格/秒";
            SetHumanize(_opt.Humanize);
        }

        private void BindEvents()
        {
            _btnStart.Click += delegate { ToggleScroll(); };
            _btnPick.Click += delegate { PickTarget(); };

            _chipDown.Click += delegate { SetDirection(false); };
            _chipUp.Click += delegate { SetDirection(true); };

            _chipHumanizeOff.Click += delegate { SetHumanize(HumanizeLevel.Off); };
            _chipHumanizeLight.Click += delegate { SetHumanize(HumanizeLevel.Light); };
            _chipHumanizeNatural.Click += delegate { SetHumanize(HumanizeLevel.Natural); };
            _chipHumanizeStrong.Click += delegate { SetHumanize(HumanizeLevel.Strong); };

            _trackSpeed.ValueChanged += delegate
            {
                _opt.NotchesPerSecond = _trackSpeed.Value;
                _lblSpeedValue.Text = _opt.NotchesPerSecond + " 格/秒";
            };

            _numStep.ValueChanged += delegate { _opt.NotchesPerStep = (int)_numStep.Value; };

            _cmbEngine.SelectedIndexChanged += delegate
            {
                _opt.Engine = _cmbEngine.SelectedIndex == 1
                    ? ScrollEngineKind.BackgroundMessage
                    : ScrollEngineKind.RealInput;
                SyncOptionEnables();
            };

            _chkPark.CheckedChanged += delegate
            {
                _opt.ParkCursor = _chkPark.Checked;
                SyncOptionEnables();
            };

            _chkPause.CheckedChanged += delegate { _opt.PauseOnMouseMove = _chkPause.Checked; };

            _controller.Changed += OnControllerChanged;

            FormClosing += OnFormClosingHandler;
            Resize += OnResizeHandler;
            Shown += OnShownHandler;
            KeyDown += OnKeyDownHandler;
        }

        private void SyncOptionEnables()
        {
            bool inputMode = _opt.Engine == ScrollEngineKind.RealInput;
            _chkPark.Enabled = inputMode && !_controller.IsRunning;
            _chkPause.Enabled = inputMode && _opt.ParkCursor && !_controller.IsRunning;
        }

        private void SetDirection(bool up)
        {
            _opt.ScrollUp = up;
            _chipUp.Selected = up;
            _chipDown.Selected = !up;
        }

        private void SetHumanize(HumanizeLevel level)
        {
            _opt.Humanize = level;
            _chipHumanizeOff.Selected = level == HumanizeLevel.Off;
            _chipHumanizeLight.Selected = level == HumanizeLevel.Light;
            _chipHumanizeNatural.Selected = level == HumanizeLevel.Natural;
            _chipHumanizeStrong.Selected = level == HumanizeLevel.Strong;
        }

        private static string HumanizeName(HumanizeLevel level)
        {
            switch (level)
            {
                case HumanizeLevel.Light: return "轻";
                case HumanizeLevel.Strong: return "强";
                case HumanizeLevel.Natural: return "自然";
                default: return "关";
            }
        }

        // ---- 目标 ----

        private void TryRestoreTarget()
        {
            WindowInfo w = WindowInfo.FindBySignature(_opt.TargetExe, _opt.TargetClass);
            if (w != null)
            {
                _target = w;
                Log.Info("按签名找回目标: " + w.Caption);
            }
        }

        private void AdoptTarget(WindowInfo w, bool save)
        {
            _target = w;
            if (save)
            {
                _opt.TargetExe = w.ExeName;
                _opt.TargetClass = w.ClassName;
                Settings.Save();
            }
            RefreshTargetCard();
        }

        private void PickTarget()
        {
            if (_controller.IsRunning) _controller.Stop("重新选择目标");

            WindowState = FormWindowState.Normal;
            Hide();
            Application.DoEvents();

            WindowInfo w = null;
            try
            {
                w = TargetPicker.Pick(3);
            }
            catch (Exception ex)
            {
                Log.Warn("拾取失败: " + ex.Message);
            }

            Show();
            Activate();

            if (w == null)
            {
                _lblHint.ForeColor = UiKit.WarnRed;
                _lblHint.Text = "没有拾取到窗口：请把鼠标停在目标窗口内部再试";
                return;
            }

            AdoptTarget(w, true);
            _lastStop = "";
            Log.Info("目标 -> " + w.Caption + " | " + w.Detail);
            RefreshUi();
        }

        private void RefreshTargetCard()
        {
            if (_target == null)
            {
                _lblTarget.Text = "尚未指定目标窗口";
                _lblTargetMeta.Text = "点右侧按钮，3 秒内把鼠标移到目标上";
                return;
            }

            if (!WindowInfo.IsAlive(_target))
            {
                _lblTarget.Text = "目标窗口已关闭";
                _lblTargetMeta.Text = "点「拾取窗口」重新指定";
                return;
            }

            _lblTarget.Text = _target.Caption;
            _lblTargetMeta.Text = _target.Detail;
        }

        // ---- 开始 / 停止 ----

        private void StartScroll()
        {
            if (_target == null || !WindowInfo.IsAlive(_target))
            {
                WindowInfo again = WindowInfo.FindBySignature(_opt.TargetExe, _opt.TargetClass);
                if (again == null)
                {
                    ShowPanel();
                    SystemSounds.Beep.Play();
                    _lblHint.ForeColor = UiKit.WarnRed;
                    _lblHint.Text = "还没有目标窗口，请先点「拾取窗口」";
                    return;
                }
                AdoptTarget(again, false);
            }

            if (_target.Elevated && !WindowInfo.IsCurrentElevated())
            {
                DialogResult r = MessageBox.Show(this,
                    "目标窗口以管理员权限运行，普通权限的 RollRoll 无法向它发送滚轮。\r\n\r\n是否以管理员身份重启 RollRoll？",
                    "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) RestartElevated();
                return;
            }

            string error;
            if (!_controller.Start(_target, _opt, out error))
            {
                _lastStop = "";
                _lblHint.ForeColor = UiKit.WarnRed;
                _lblHint.Text = error;
                Log.Warn("启动失败: " + error);
                SystemSounds.Hand.Play();
                RefreshUi();
                return;
            }

            _lastStop = "";
            RefreshUi();

            if (_tray != null && !Visible)
                _tray.Notify("开始滚动", string.Format("{0} · {1} 格/秒（F8 停止）",
                    _target.ExeName, _opt.NotchesPerSecond));
        }

        private void RestartElevated()
        {
            try
            {
                Settings.Save();
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                Log.Warn("提权重启失败: " + ex.Message);
                MessageBox.Show(this, "以管理员身份重启失败：" + ex.Message,
                    "RollRoll", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnControllerChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(new MethodInvoker(HandleStopped)); }
            catch (InvalidOperationException) { }
        }

        private void HandleStopped()
        {
            if (IsDisposed) return;

            _lastStop = _controller.StopReason;
            RefreshUi();

            if (_tray != null)
            {
                _tray.SetState(_target != null && WindowInfo.IsAlive(_target) ? TrayState.Idle : TrayState.Warning);
                if (!Visible)
                    _tray.Notify("已停止", string.IsNullOrEmpty(_lastStop) ? "滚动结束" : _lastStop);
            }

            Log.Info("会话结束: " + _lastStop);
        }

        // ---- 刷新 ----

        private void RefreshUi()
        {
            bool running = _controller.IsRunning;

            _btnStart.Text = running ? "停止  (F8)" : "开始  (F8)";
            _btnStart.FillColor = running ? UiKit.WarnRed : UiKit.Accent;

            _btnPick.Enabled = !running;
            _trackSpeed.Enabled = !running;
            _numStep.Enabled = !running;
            _cmbEngine.Enabled = !running;
            SyncOptionEnables();

            _chipUp.Selected = _opt.ScrollUp;
            _chipDown.Selected = !_opt.ScrollUp;
            SetHumanize(_opt.Humanize);
            _chipHumanizeOff.Enabled = !running;
            _chipHumanizeLight.Enabled = !running;
            _chipHumanizeNatural.Enabled = !running;
            _chipHumanizeStrong.Enabled = !running;
            _lblSpeedValue.Text = _opt.NotchesPerSecond + " 格/秒";

            RefreshTargetCard();
            UpdateStatusText();

            if (_tray != null)
                _tray.SetState(running ? TrayState.Running : TrayState.Idle);
        }

        private void UpdateStatusText()
        {
            if (_controller.IsRunning)
            {
                TimeSpan t = _controller.Elapsed;
                _lblStatus.ForeColor = UiKit.OkGreen;
                _lblStatus.Text = string.Format("● 运行中 · 已滚 {0:N0} 格", _controller.Notches);

                _lblHint.ForeColor = UiKit.TextSub;
                _lblHint.Text = string.Format("已运行 {0:00}:{1:00}:{2:00} · {3} · 抖动：{4} · F8 停止",
                    (int)t.TotalHours, t.Minutes, t.Seconds,
                    _opt.Engine == ScrollEngineKind.RealInput ? "真实输入" : "后台消息",
                    HumanizeName(_opt.Humanize));
                return;
            }

            if (string.IsNullOrEmpty(_lastStop))
            {
                _lblStatus.ForeColor = UiKit.TextSub;
                _lblStatus.Text = "空闲";
            }
            else
            {
                _lblStatus.ForeColor = UiKit.WarnRed;
                _lblStatus.Text = "已停止：" + _lastStop;
            }

            _lblHint.ForeColor = UiKit.TextSub;
            _lblHint.Text = _target != null && WindowInfo.IsAlive(_target)
                ? string.Format("{0} · {1} 格/秒 · 抖动：{2} · F8 开始/停止",
                    _target.ExeName, _opt.NotchesPerSecond, HumanizeName(_opt.Humanize))
                : "请先点「拾取窗口」指定目标 · F8 开始/停止";
        }

        // ---- 事件 ----

        private void OnUiTick(object sender, EventArgs e)
        {
            if (_controller.IsRunning) UpdateStatusText();
        }

        private void OnShownHandler(object sender, EventArgs e)
        {
            if (Settings.FirstRun && _tray != null)
            {
                Settings.FirstRun = false;
                Settings.Save();
                _tray.Notify("RollRoll 已就绪", "点「拾取窗口」选择目标，F8 开始 / 停止");
            }
        }

        private void OnKeyDownHandler(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _controller.IsRunning)
            {
                _controller.Stop("Esc 停止");
                e.Handled = true;
            }
        }

        private void OnResizeHandler(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                BeginInvoke(new MethodInvoker(HideToTray));
        }

        private void OnFormClosingHandler(object sender, FormClosingEventArgs e)
        {
            if (!_exiting)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _uiTimer.Stop();
            _controller.Dispose();

            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }

            Settings.Save();
        }

        private void HideToTray()
        {
            if (!Visible) return;

            Hide();

            if (!_trayHintShown && _tray != null)
            {
                _trayHintShown = true;
                _tray.Notify("RollRoll 仍在运行", "F8 开始 / 停止，双击托盘图标打开面板");
            }
        }

        // ---- 热键 ----

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            bool okToggle = Native.RegisterHotKey(Handle, HotkeyToggleId, Native.MOD_NOREPEAT, VkF8);
            if (!okToggle)
                okToggle = Native.RegisterHotKey(Handle, HotkeyToggleId, 0, VkF8);

            bool okStop = Native.RegisterHotKey(Handle, HotkeyStopId, Native.MOD_NOREPEAT, VkF9);
            if (!okStop)
                okStop = Native.RegisterHotKey(Handle, HotkeyStopId, 0, VkF9);

            if (!okToggle && !_hotkeyWarned)
            {
                _hotkeyWarned = true;
                Log.Warn("F8 热键注册失败: " + Native.LastWin32Error());
                if (_lblHint != null)
                {
                    _lblHint.ForeColor = UiKit.WarnRed;
                    _lblHint.Text = "F8 被其他程序占用，请用面板按钮或托盘菜单";
                }
            }

            if (!okStop)
                Log.Warn("F9 热键注册失败: " + Native.LastWin32Error());
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, HotkeyToggleId);
            Native.UnregisterHotKey(Handle, HotkeyStopId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();

                if (id == HotkeyToggleId)
                {
                    ToggleScroll();
                    return;
                }

                if (id == HotkeyStopId)
                {
                    if (_controller.IsRunning) _controller.Stop("F9 停止");
                    return;
                }
            }

            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_uiTimer != null) _uiTimer.Dispose();
                _controller.Dispose();
            }
            base.Dispose(disposing);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
