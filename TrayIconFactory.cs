// ============================================================================
//  RollRoll - 托盘图标
//
//  没有外部图片资源：矢量画一个圆 + 上下箭头，再 4 倍超采样降采样到 32px。
//  任何一个环节失败都退回系统默认图标，绝不让程序起不来。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RollRoll
{
    internal enum TrayState
    {
        Idle = 0,
        Running = 1,
        Warning = 2
    }

    internal static class TrayIconFactory
    {
        private static readonly Icon[] Cache = new Icon[3];
        private static Icon _appIcon;

        internal static Color ColorOf(TrayState s)
        {
            switch (s)
            {
                case TrayState.Running: return Color.FromArgb(0x2F, 0x6F, 0xED);
                case TrayState.Warning: return Color.FromArgb(0xC0, 0x39, 0x2B);
                default: return Color.FromArgb(0x8A, 0x90, 0x99);
            }
        }

        internal static Icon Get(TrayState state)
        {
            int i = (int)state;
            if (i < 0 || i >= Cache.Length) i = 0;
            if (Cache[i] == null) Cache[i] = Build(ColorOf(state), 32);
            return Cache[i];
        }

        internal static Icon AppIcon
        {
            get
            {
                if (_appIcon == null) _appIcon = Build(Color.FromArgb(0x2F, 0x6F, 0xED), 32);
                return _appIcon;
            }
        }

        private static Icon Build(Color bg, int size)
        {
            try
            {
                const int big = 64;

                using (Bitmap bmp = new Bitmap(big, big, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        using (SolidBrush b = new SolidBrush(bg))
                            g.FillEllipse(b, 2, 2, big - 4, big - 4);

                        using (Pen pen = new Pen(Color.White, 6.5f))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            pen.LineJoin = LineJoin.Round;

                            g.DrawLines(pen, new PointF[]
                            {
                                new PointF(20, 29), new PointF(32, 17), new PointF(44, 29)
                            });
                            g.DrawLines(pen, new PointF[]
                            {
                                new PointF(20, 45), new PointF(32, 57), new PointF(44, 45)
                            });
                        }
                    }

                    using (Bitmap small = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(bmp, new Rectangle(0, 0, size, size));
                        }

                        IntPtr h = small.GetHicon();
                        try
                        {
                            using (Icon tmp = Icon.FromHandle(h))
                                return (Icon)tmp.Clone();
                        }
                        finally
                        {
                            Native.DestroyIcon(h);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("生成托盘图标失败: " + ex.Message);
                return (Icon)SystemIcons.Application.Clone();
            }
        }
    }
}
