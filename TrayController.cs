// ============================================================================
//  RollRoll - 托盘控制器
//
//  托盘是后台唯一入口：开始/停止、重新拾取、显示面板、退出。
//  图标配色直接反映运行状态，避免「它到底还在不在滚」的疑问。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RollRoll
{
    internal sealed class TrayController : IDisposable
    {
        private readonly MainForm _form;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _miStatus;
        private readonly ToolStripMenuItem _miToggle;
        private TrayState _state = TrayState.Idle;

        internal TrayController(MainForm form)
        {
            _form = form;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            _miStatus = new ToolStripMenuItem("空闲");
            _miStatus.Enabled = false;

            _miToggle = new ToolStripMenuItem("开始 (F8)");
            _miToggle.Click += delegate { _form.ToggleScroll(); };

            ToolStripMenuItem miPick = new ToolStripMenuItem("重新拾取窗口");
            miPick.Click += delegate { _form.PickTargetFromTray(); };

            ToolStripMenuItem miShow = new ToolStripMenuItem("显示面板");
            miShow.Click += delegate { _form.ShowPanel(); };

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { _form.ExitApp(); };

            _menu.Items.Add(_miStatus);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miToggle);
            _menu.Items.Add(miPick);
            _menu.Items.Add(miShow);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miExit);

            _menu.Opening += OnMenuOpening;

            _tray = new NotifyIcon();
            _tray.Icon = TrayIconFactory.Get(TrayState.Idle);
            _tray.Text = "RollRoll · 空闲";
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { _form.ShowPanel(); };
        }

        internal void SetState(TrayState state)
        {
            _state = state;
            try
            {
                _tray.Icon = TrayIconFactory.Get(state);
            }
            catch
            {
            }

            switch (state)
            {
                case TrayState.Running: SetTooltip("RollRoll · 滚动中（F8 停止）"); break;
                case TrayState.Warning: SetTooltip("RollRoll · 需要处理"); break;
                default: SetTooltip("RollRoll · 空闲（F8 开始）"); break;
            }
        }

        internal void SetTooltip(string text)
        {
            if (text == null) text = "";
            // Shell_NotifyIcon 的 szTip 上限 63 个字符（含结尾）。
            if (text.Length > 62) text = text.Substring(0, 59) + "...";
            try { _tray.Text = text; }
            catch { }
        }

        internal void Notify(string title, string text)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = ToolTipIcon.None;
                _tray.ShowBalloonTip(2500);
            }
            catch
            {
            }
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _miToggle.Text = _form.IsScrolling ? "停止 (F8)" : "开始 (F8)";
            _miStatus.Text = _form.StatusLine;
            _miToggle.Enabled = _form.CanToggle;
        }

        public void Dispose()
        {
            try
            {
                _tray.Visible = false;
                _tray.Dispose();
                _menu.Dispose();
            }
            catch
            {
            }
        }
    }
}
