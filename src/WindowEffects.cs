using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WSLBox
{
    internal static class WindowEffects
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);
        internal static bool TryGetBounds(Window window, out Rect rectangle)
        {
            NativeRectangle native;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out native))
            {
                rectangle = new Rect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
                return true;
            }
            rectangle = Rect.Empty; return false;
        }
        internal static int ResizeHit(Rect bounds, Point point, DpiScale dpi)
        {
            if (!bounds.Contains(point)) return 0;
            double right = (bounds.Right - point.X) / dpi.DpiScaleX;
            double bottom = (bounds.Bottom - point.Y) / dpi.DpiScaleY;
            // Keep the full bottom-right grip usable for diagonal sizing.
            if (right <= 15 && bottom <= 15) return 17; // HTBOTTOMRIGHT
            if (right <= 5) return 11; // HTRIGHT
            if (bottom <= 5) return 15; // HTBOTTOM
            return 0;
        }
        internal static void DisableTransitions(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return; // Never create a window merely to close it.
            int disabled = 1;
            DwmSetWindowAttribute(handle, 3, ref disabled, sizeof(int));
        }
        internal static void SetMouseActivation(Window window, bool enabled)
        {
            const int extendedStyle = -20, noActivate = 0x08000000;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, extendedStyle);
            int updated = enabled ? style & ~noActivate : style | noActivate;
            if (updated != style) SetWindowLong(handle, extendedStyle, updated);
        }

        internal static void SendToBottom(Window window)
        {
            const uint keepGeometryAndFocus = 0x0013; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, keepGeometryAndFocus);
        }

        internal static void ShowInBackground(Window window)
        {
            IntPtr foreground = GetForegroundWindow();
            // WPF/Win32 cannot show a maximized window without activation. Keep
            // the maximized state valid, respect the layer, and return focus
            // to the previous foreground window in the same synchronous operation.
            window.ShowActivated = window.WindowState == WindowState.Maximized;
            window.Show();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (!window.Topmost) SendToBottom(window);
            if (foreground != IntPtr.Zero && foreground != handle && GetForegroundWindow() == handle)
                SetForegroundWindow(foreground);
        }
    }
}
