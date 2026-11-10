// ============================================================================
//  RollRoll - 界面公共件
//
//  自绘控件只做三件事：圆角卡片、胶囊选择块、主/次按钮。
//  字体按 (字号, 字形) 缓存，避免反复 new Font 触发字体名解析。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RollRoll
{
    internal static class UiKit
    {
        internal static readonly Color Accent     = Color.FromArgb(0x2F, 0x6F, 0xED);
        internal static readonly Color AccentDark = Color.FromArgb(0x20, 0x55, 0xC8);
        internal static readonly Color TextMain   = Color.FromArgb(0x1F, 0x23, 0x28);
        internal static readonly Color TextSub    = Color.FromArgb(0x6B, 0x72, 0x80);
        internal static readonly Color TextFaint  = Color.FromArgb(0xA0, 0xA6, 0xAE);
        internal static readonly Color CardBg     = Color.FromArgb(0xF7, 0xF8, 0xFA);
        internal static readonly Color CardBorder = Color.FromArgb(0xE3, 0xE6, 0xEB);
        internal static readonly Color PageBg     = Color.White;
        internal static readonly Color OkGreen    = Color.FromArgb(0x16, 0x9B, 0x62);
        internal static readonly Color WarnRed    = Color.FromArgb(0xC0, 0x39, 0x2B);

        private static readonly Dictionary<string, Font> Fonts =
            new Dictionary<string, Font>(StringComparer.Ordinal);

        internal static Font Font(float size, FontStyle style)
        {
            string key = size.ToString("0.##") + "|" + (int)style;
            Font f;
            if (Fonts.TryGetValue(key, out f)) return f;

            f = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            Fonts[key] = f;
            return f;
        }

        internal static Font FontUi      { get { return Font(9f, FontStyle.Regular); } }
        internal static Font FontUiBold  { get { return Font(9f, FontStyle.Bold); } }
        internal static Font FontSmall   { get { return Font(8.25f, FontStyle.Regular); } }
        internal static Font FontButton  { get { return Font(10.5f, FontStyle.Bold); } }
        internal static Font FontTitle   { get { return Font(13f, FontStyle.Bold); } }

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 1 || r.Width <= 2 || r.Height <= 2)
            {
                p.AddRectangle(r);
                return p;
            }

            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        internal static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        internal static Label MakeLabel(string text, Font font, Color color,
            Rectangle bounds, ContentAlignment align)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.Bounds = bounds;
            l.TextAlign = align;
            l.AutoSize = false;
            l.AutoEllipsis = true;
            return l;
        }
    }

    internal sealed class Card : Panel
    {
        internal Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = UiKit.CardBg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color parentBg = Parent != null ? Parent.BackColor : UiKit.PageBg;
            e.Graphics.Clear(parentBg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath p = UiKit.Rounded(r, 10))
            {
                using (SolidBrush b = new SolidBrush(UiKit.CardBg))
                    e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(UiKit.CardBorder))
                    e.Graphics.DrawPath(pen, p);
            }

            base.OnPaint(e);
        }
    }

    /// <summary>胶囊选择块：用于「向下 / 向上」。</summary>
    internal sealed class Chip : Control
    {
        private bool _selected;
        private bool _hover;

        internal bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        internal Chip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);

            Font = UiKit.FontUi;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill;
            Color border;
            Color text;

            if (!Enabled)
            {
                fill = Color.FromArgb(0xF2, 0xF3, 0xF6);
                border = Color.FromArgb(0xE3, 0xE6, 0xEB);
                text = UiKit.TextFaint;
            }
            else
            {
                fill = _selected ? UiKit.Accent : (_hover ? Color.FromArgb(0xF0, 0xF4, 0xFF) : Color.White);
                border = _selected ? UiKit.Accent : UiKit.CardBorder;
                text = _selected ? Color.White : UiKit.TextMain;
            }

            using (GraphicsPath p = UiKit.Rounded(r, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(fill))
                    e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(border))
                    e.Graphics.DrawPath(pen, p);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// 轻量滑杆。原生 TrackBar 的滑块偏上、样式老旧，在 96dpi 下和左侧标签
    /// 明显不齐，所以自绘一条：4px 轨道 + 14px 圆形滑块。
    /// </summary>
    internal sealed class Slider : Control
    {
        private int _min = 1;
        private int _max = 30;
        private int _value = 1;
        private bool _drag;

        internal event EventHandler ValueChanged;

        internal int Minimum
        {
            get { return _min; }
            set { _min = value; ClampValue(); Invalidate(); }
        }

        internal int Maximum
        {
            get { return _max; }
            set { _max = value; ClampValue(); Invalidate(); }
        }

        internal int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v == _value) return;

                _value = v;
                Invalidate();

                EventHandler h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        internal Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);

            Height = 28;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        private void ClampValue()
        {
            if (_value < _min) _value = _min;
            if (_value > _max) _value = _max;
        }

        private const int Pad = 8;

        private int ThumbX
        {
            get
            {
                int span = Width - Pad * 2;
                if (span < 2) span = 2;
                double t = (double)(_value - _min) / Math.Max(1, _max - _min);
                return Pad + (int)Math.Round(t * span);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _drag = true;
                Capture = true;
                SetFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _drag = false;
                Capture = false;
            }
            base.OnMouseUp(e);
        }

        private void SetFromX(int x)
        {
            int span = Width - Pad * 2;
            if (span < 2) span = 2;

            double t = (double)(x - Pad) / span;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            Value = _min + (int)Math.Round(t * (_max - _min));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int cy = Height / 2;
            int x1 = Pad;
            int x2 = Width - Pad;
            if (x2 <= x1 + 2) return;

            int tx = ThumbX;

            Color trackColor = Enabled ? Color.FromArgb(0xE1, 0xE5, 0xEC) : Color.FromArgb(0xEF, 0xF1, 0xF4);
            Color fillColor = Enabled ? UiKit.Accent : Color.FromArgb(0xC8, 0xCE, 0xD8);

            using (GraphicsPath p = UiKit.Rounded(new Rectangle(x1, cy - 2, x2 - x1, 4), 2))
            using (SolidBrush b = new SolidBrush(trackColor))
                e.Graphics.FillPath(b, p);

            if (tx > x1 + 2)
            {
                using (GraphicsPath p = UiKit.Rounded(new Rectangle(x1, cy - 2, tx - x1, 4), 2))
                using (SolidBrush b = new SolidBrush(fillColor))
                    e.Graphics.FillPath(b, p);
            }

            const int r = 7;
            Rectangle thumb = new Rectangle(tx - r, cy - r, r * 2, r * 2);
            using (SolidBrush b = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(b, thumb);
            using (Pen pen = new Pen(fillColor, 2f))
                e.Graphics.DrawEllipse(pen, thumb);
        }
    }

    /// <summary>主按钮（实心accent）与次按钮（白底描边）。</summary>
    internal sealed class ActionButton : Control
    {
        private bool _primary = true;
        private bool _hover;
        private bool _pressed;

        internal bool Primary
        {
            get { return _primary; }
            set { _primary = value; Invalidate(); }
        }

        /// <summary>主按钮底色。运行中会换成红色，提示「再按一次就停」。</summary>
        internal Color FillColor = UiKit.Accent;

        internal ActionButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);

            Font = UiKit.FontButton;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bool fire = _pressed && ClientRectangle.Contains(e.Location);
                _pressed = false;
                Invalidate();
                if (fire) OnClick(EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill;
            Color border;
            Color text;

            if (!Enabled)
            {
                fill = Color.FromArgb(0xEE, 0xF0, 0xF4);
                border = Color.FromArgb(0xE3, 0xE6, 0xEB);
                text = UiKit.TextFaint;
            }
            else if (_primary)
            {
                fill = _pressed ? UiKit.Blend(FillColor, Color.Black, 0.12)
                     : (_hover ? UiKit.Blend(FillColor, Color.White, 0.08) : FillColor);
                border = fill;
                text = Color.White;
            }
            else
            {
                fill = _pressed ? Color.FromArgb(0xE6, 0xEE, 0xFF)
                     : (_hover ? Color.FromArgb(0xF3, 0xF7, 0xFF) : Color.White);
                border = Color.FromArgb(0xC9, 0xD8, 0xF8);
                text = UiKit.Accent;
            }

            using (GraphicsPath p = UiKit.Rounded(r, 8))
            {
                using (SolidBrush b = new SolidBrush(fill))
                    e.Graphics.FillPath(b, p);
                if (!_primary)
                {
                    using (Pen pen = new Pen(border))
                        e.Graphics.DrawPath(pen, p);
                }
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }
    }
}
