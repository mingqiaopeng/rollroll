// ============================================================================
//  RollRoll - 目标窗口拾取
//
//  交互：点「拾取」后主面板隐藏，屏幕底部出现 3 秒倒计时，倒计时结束时
//  取鼠标下的窗口（顶层）。覆盖层带 WS_EX_TRANSPARENT，不会被
//  WindowFromPoint 命中，也不会抢焦点。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RollRoll
{
    internal static class TargetPicker
    {
        internal static WindowInfo Pick(int seconds)
        {
            using (CountdownForm f = new CountdownForm(seconds))
            {
                if (f.ShowDialog() != DialogResult.OK) return null;
                return f.Result;
            }
        }

        private sealed class CountdownForm : Form
        {
            private readonly Timer _timer = new Timer();
            private readonly int _total;
            private int _left;
            private Label _big;
            private Label _hint;

            internal WindowInfo Result;

            internal CountdownForm(int seconds)
            {
                _total = Math.Max(1, seconds);
                _left = _total;

                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                BackColor = Color.FromArgb(24, 28, 36);
                Opacity = 0.94;
                Size = new Size(320, 92);
                DoubleBuffered = true;

                _big = new Label();
                _big.AutoSize = false;
                _big.Bounds = new Rectangle(8, 6, 76, 80);
                _big.TextAlign = ContentAlignment.MiddleCenter;
                _big.ForeColor = Color.White;
                _big.BackColor = Color.Transparent;
                _big.Font = UiKit.Font(30f, FontStyle.Bold);
                _big.Text = _left.ToString();
                Controls.Add(_big);

                _hint = new Label();
                _hint.AutoSize = false;
                _hint.Bounds = new Rectangle(88, 6, 224, 80);
                _hint.TextAlign = ContentAlignment.MiddleLeft;
                _hint.ForeColor = Color.FromArgb(210, 216, 226);
                _hint.BackColor = Color.Transparent;
                _hint.Font = UiKit.Font(9.5f, FontStyle.Regular);
                _hint.Text = "把鼠标移到目标窗口上\r\n倒计时结束后自动拾取";
                Controls.Add(_hint);

                _timer.Interval = 1000;
                _timer.Tick += OnTick;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                    cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT：命中测试穿透
                    cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                    return cp;
                }
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);

                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - 96);

                using (GraphicsPath p = UiKit.Rounded(new Rectangle(0, 0, Width, Height), 14))
                    Region = new Region(p);

                _timer.Start();
            }

            private void OnTick(object sender, EventArgs e)
            {
                _left--;
                if (_left > 0)
                {
                    _big.Text = _left.ToString();
                    return;
                }

                _timer.Stop();
                Result = CaptureWindow();
                DialogResult = Result != null ? DialogResult.OK : DialogResult.Cancel;
                Close();
            }

            private static WindowInfo CaptureWindow()
            {
                Native.POINT p = new Native.POINT(Cursor.Position.X, Cursor.Position.Y);
                IntPtr h = Native.WindowFromPoint(p);
                if (h == IntPtr.Zero) return null;

                IntPtr root = WindowInfo.Root(h);
                if (root == IntPtr.Zero) return null;

                WindowInfo info = WindowInfo.FromHandle(root);
                if (!WindowInfo.IsSuitable(info)) return null;
                return info;
            }
        }
    }
}
