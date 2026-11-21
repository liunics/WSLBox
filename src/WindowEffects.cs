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
        [DllImport("user32.dll", EntryPoint = "IsIconic")]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        internal static bool IsMinimized(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            return handle != IntPtr.Zero && IsIconic(handle);
        }

        internal static void RestoreIfMinimized(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero && IsIconic(handle)) ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
        }

        private static Rect DeviceBounds(Window window, Rect logical)
        {
            var source = PresentationSource.FromVisual(window) as HwndSource;
            var transform = source == null ? System.Windows.Media.Matrix.Identity : source.CompositionTarget.TransformToDevice;
            return new Rect(transform.Transform(logical.TopLeft), transform.Transform(logical.BottomRight));
        }

        internal static bool BoundsMatch(Window window, Rect logical)
        {
            Rect actual;
            if (!TryGetBounds(window, out actual)) return false;
            Rect expected = DeviceBounds(window, logical);
            return Math.Abs(actual.Left - expected.Left) <= 1 && Math.Abs(actual.Top - expected.Top) <= 1
                && Math.Abs(actual.Width - expected.Width) <= 1 && Math.Abs(actual.Height - expected.Height) <= 1;
        }

        internal static void ApplyBounds(Window window, Rect logical)
        {
            if (BoundsMatch(window, logical)) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            Rect target = DeviceBounds(window, logical);
            const uint noZOrderOrActivation = 0x0014;
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(target.Left), (int)Math.Round(target.Top),
                (int)Math.Round(target.Width), (int)Math.Round(target.Height), noZOrderOrActivation);
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
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int style);
        internal static void DisableMinimize(Window window)
        {
            const int styleIndex = -16, minimizeBox = 0x00020000;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            SetWindowLong(handle, styleIndex, GetWindowLong(handle, styleIndex) & ~minimizeBox);
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037); // Refresh the non-client frame.
        }

        internal static void HideForClose(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            const uint hideWithoutMovingOrActivating = 0x0097; // HIDEWINDOW | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, hideWithoutMovingOrActivating);
        }

        internal static void DisableTransitions(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return; // Never create a window merely to close it.
            int disabled = 1;
            DwmSetWindowAttribute(handle, 3, ref disabled, sizeof(int));
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            public IntPtr Window, InsertAfter;
            public int X, Y, Width, Height;
            public uint Flags;
        }

        internal static void KeepBottomPlacement(IntPtr position)
        {
            const uint noZOrder = 0x0004;
            var change = (WindowPosition)Marshal.PtrToStructure(position, typeof(WindowPosition));
            if ((change.Flags & noZOrder) != 0) return;
            change.InsertAfter = new IntPtr(1); // HWND_BOTTOM
            Marshal.StructureToPtr(change, position, false);
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
