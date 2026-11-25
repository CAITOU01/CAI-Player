using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CaiPlayer
{
    internal sealed partial class PlayerWindow
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ThumbButton
        {
            internal uint Mask, Id, BitmapIndex;
            internal IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Tip;
            internal uint Flags;
        }

        // Include inherited methods to preserve the native COM vtable order.
        [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, uint state);
            void RegisterTab(IntPtr tab, IntPtr owner);
            void UnregisterTab(IntPtr tab);
            void SetTabOrder(IntPtr tab, IntPtr before);
            void SetTabActive(IntPtr tab, IntPtr owner, uint reserved);
            void ThumbBarAddButtons(IntPtr hwnd, uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ThumbButton[] buttons);
            void ThumbBarUpdateButtons(IntPtr hwnd, uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ThumbButton[] buttons);
        }

        private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");
        private const int PreviousId = 3101, ToggleId = 3102, NextId = 3103;
        private ITaskbarList3 taskbar;
        private readonly IntPtr[] thumbIcons = new IntPtr[4];
        private int thumbnailState = -1;

        private static IntPtr MakeThumbIcon(int kind)
        {
            // A light fill with a dark outline stays visible on either Windows taskbar theme.
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var shape = new GraphicsPath())
            using (var outline = new Pen(Color.FromArgb(210, 32, 32, 32), 2))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (kind == 2) {
                    shape.AddRectangle(new Rectangle(7, 5, 6, 22));
                    shape.AddRectangle(new Rectangle(19, 5, 6, 22));
                } else {
                    bool left = kind == 0;
                    shape.AddPolygon(left ? new[] { new Point(24, 5), new Point(8, 16), new Point(24, 27) } :
                        new[] { new Point(8, 5), new Point(24, 16), new Point(8, 27) });
                    if (kind != 1) shape.AddRectangle(new Rectangle(left ? 4 : 25, 5, 4, 22));
                }
                graphics.DrawPath(outline, shape);
                graphics.FillPath(Brushes.White, shape);
                return bitmap.GetHicon();
            }
        }

        private ThumbButton ThumbnailButton(uint id, int icon, string tip, bool enabled)
        {
            if (thumbIcons[icon] == IntPtr.Zero) thumbIcons[icon] = MakeThumbIcon(icon);
            return new ThumbButton { Mask = 2 | 4 | 8, Id = id, Icon = thumbIcons[icon], Tip = tip,
                Flags = enabled ? 0u : 1u };
        }

        private void UpdateThumbnail()
        {
            if (taskbar == null || !IsHandleCreated) return;
            bool enabled = playlist.Tracks.Count != 0;
            int stateCode = (enabled ? 1 : 0) | (playing ? 2 : 0);
            if (thumbnailState == stateCode) return;
            var buttons = new[] { ThumbnailButton(PreviousId, 0, "上一首", enabled),
                ThumbnailButton(ToggleId, playing ? 2 : 1, playing ? "暫停" : "播放", enabled),
                ThumbnailButton(NextId, 3, "下一首", enabled) };
            try
            {
                if (thumbnailState < 0) taskbar.ThumbBarAddButtons(Handle, 3, buttons);
                else taskbar.ThumbBarUpdateButtons(Handle, 3, buttons);
                thumbnailState = stateCode;
            }
            catch (COMException) { ReleaseTaskbar(); }
        }

        private void ReleaseTaskbar()
        {
            if (taskbar != null) Marshal.ReleaseComObject(taskbar);
            taskbar = null;
            thumbnailState = -1;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            ReleaseTaskbar();
            if (thumbIcons != null)
                for (int i = 0; i < thumbIcons.Length; i++)
                    if (thumbIcons[i] != IntPtr.Zero) { DestroyIcon(thumbIcons[i]); thumbIcons[i] = IntPtr.Zero; }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == TaskbarCreated && TaskbarCreated != 0)
            {
                ReleaseTaskbar();
                try {
                    var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"));
                    taskbar = (ITaskbarList3)Activator.CreateInstance(type);
                    taskbar.HrInit();
                    UpdateThumbnail();
                } catch (COMException) { ReleaseTaskbar(); }
                catch (InvalidCastException) { ReleaseTaskbar(); }
            }
            if (message.Msg == 0x0111 && ((message.WParam.ToInt64() >> 16) & 0xffff) == 0x1800)
            {
                switch ((int)(message.WParam.ToInt64() & 0xffff))
                {
                    case PreviousId: Skip(-1); return;
                    case ToggleId: Toggle(); return;
                    case NextId: Skip(1); return;
                }
            }
            base.WndProc(ref message);
        }
    }
}
