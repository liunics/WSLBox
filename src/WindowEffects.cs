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
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal static void DisableTransitions(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return; // Never create a window merely to close it.
            int disabled = 1;
            DwmSetWindowAttribute(handle, 3, ref disabled, sizeof(int));
        }
        internal static void ShowInBackground(Window window)
        {
            IntPtr foreground = GetForegroundWindow();
            // WPF/Win32 cannot show a maximized window without activation. Keep
            // the maximized state valid, place it below apps, and return focus
            // to the previous foreground window in the same synchronous operation.
            window.ShowActivated = window.WindowState == WindowState.Maximized;
            window.Show();
            IntPtr handle = new WindowInteropHelper(window).Handle;
            SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, 0x0013);
            if (foreground != IntPtr.Zero && foreground != handle && GetForegroundWindow() == handle)
                SetForegroundWindow(foreground);
        }
    }
}
