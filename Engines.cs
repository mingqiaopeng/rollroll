// ============================================================================
//  RollRoll - 滚动引擎
//
//  两种发法各有取舍，UI 上可切换：
//    InputEngine   SendInput 注入真实滚轮事件 —— 兼容性最好，需要前台/光标
//    MessageEngine PostMessage 投递 WM_MOUSEWHEEL —— 后台静默，现代应用可能忽略
// ============================================================================

using System;

namespace RollRoll
{
    internal interface IScrollEngine
    {
        void Prepare(WindowInfo target, Options options);
        void Step(int notches);
        void Release();
    }

    /// <summary>真实输入。方向用符号表示：正数向上，负数向下。</summary>
    internal sealed class InputEngine : IScrollEngine
    {
        private readonly Native.INPUT[] _buf = new Native.INPUT[1];

        internal InputEngine()
        {
            _buf[0].type = Native.INPUT_MOUSE;
            _buf[0].mi.dwFlags = Native.MOUSEEVENTF_WHEEL;
        }

        public void Prepare(WindowInfo target, Options options)
        {
        }

        public void Step(int notches)
        {
            // mouseData 是 DWORD，负增量必须走 unchecked 保留补码。
            _buf[0].mi.mouseData = unchecked((uint)(notches * 120));
            _buf[0].mi.dwExtraInfo = IntPtr.Zero;

            uint sent = Native.SendInput(1, _buf, Native.InputSize);
            if (sent != 1)
                throw new InvalidOperationException("SendInput 被系统拒绝：" + Native.LastWin32Error());
        }

        public void Release()
        {
        }
    }

    /// <summary>后台消息。把滚轮投递给目标中心点下最深的子窗口。</summary>
    internal sealed class MessageEngine : IScrollEngine
    {
        private WindowInfo _target;
        private IntPtr _child;
        private int _resolvedAt;

        public void Prepare(WindowInfo target, Options options)
        {
            _target = target;
            _child = IntPtr.Zero;
            Resolve();
        }

        public void Step(int notches)
        {
            // 子窗口可能被应用重建（Chromium 系尤其明显），定期重解析。
            if (_child == IntPtr.Zero || !Native.IsWindow(_child) ||
                unchecked(Environment.TickCount - _resolvedAt) > 2000)
                Resolve();

            Native.POINT p = _target.CenterScreen();

            int wParam = (int)((uint)(ushort)(short)(notches * 120) << 16);
            int lParam = (int)((uint)(ushort)p.X | ((uint)(ushort)p.Y << 16));

            Native.PostMessage(_child, Native.WM_MOUSEWHEEL,
                new IntPtr(wParam), new IntPtr(lParam));
        }

        public void Release()
        {
        }

        private void Resolve()
        {
            IntPtr h = _target.Hwnd;
            Native.POINT p = _target.CenterScreen();

            for (int guard = 0; guard < 32; guard++)
            {
                Native.POINT c = p;
                if (!Native.ScreenToClient(h, ref c)) break;

                IntPtr next = Native.ChildWindowFromPointEx(h, c,
                    Native.CWP_SKIPINVISIBLE | Native.CWP_SKIPDISABLED | Native.CWP_SKIPTRANSPARENT);

                if (next == IntPtr.Zero || next == h) break;
                h = next;
            }

            if (h != _child)
                Log.Info("消息目标 -> " + Native.ClassNameOf(h));

            _child = h;
            _resolvedAt = Environment.TickCount;
        }
    }
}
