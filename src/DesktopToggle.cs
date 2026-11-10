using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Accessibility;

namespace WSLBox
{
    internal sealed class DesktopClickGesture
    {
        private bool pending;
        private uint time;
        private int x, y;
        public void Reset() { pending = false; }
        public bool Click(uint now, int px, int py, bool desktop, uint interval, int width, int height)
        {
            if (!desktop) { pending = false; return false; }
            bool twice = pending && unchecked(now - time) <= interval
                && Math.Abs((long)px - x) <= Math.Max(1, width / 2)
                && Math.Abs((long)py - y) <= Math.Max(1, height / 2);
            pending = !twice; time = now; x = px; y = py;
            return twice;
        }
    }
    internal sealed class DesktopToggle : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseData
        {
            public NativePoint Point;
            public uint MouseInfo, Flags, Time;
            public UIntPtr Extra;
        }
        private delegate IntPtr MouseProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        private sealed class PhysicalScreenScope : IDisposable
        {
            private readonly IntPtr previous;
            public bool Active { get { return previous != IntPtr.Zero; } }
            public PhysicalScreenScope() { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            public void Dispose() { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flag);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromPoint(NativePoint point,
            out IAccessible accessible, [MarshalAs(UnmanagedType.Struct)] out object child);
        private readonly MouseProc callback;
        private readonly Dispatcher dispatcher;
        private readonly Action toggle;
        private readonly DesktopClickGesture gesture = new DesktopClickGesture();
        private IntPtr hook;
        private NativePoint first;
        private bool enabled = true, disposed;
        public bool Enabled { get { return enabled; } set { enabled = value; gesture.Reset(); } }
        public bool Installed { get { return hook != IntPtr.Zero; } }
        public DesktopToggle(Dispatcher dispatcher, Action toggle)
        {
            this.dispatcher = dispatcher; this.toggle = toggle; callback = OnMouse;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        }
        private static string ClassName(IntPtr handle)
        {
            var name = new StringBuilder(128); GetClassName(handle, name, name.Capacity); return name.ToString();
        }
        private static bool DesktopSurface(NativePoint point)
        {
            // MSLLHOOKSTRUCT.pt is in per-monitor screen coordinates. WPF's
            // system-DPI thread otherwise virtualizes hit testing on another
            // monitor: a point over our box can incorrectly hit the wallpaper.
            using (var dpi = new PhysicalScreenScope())
            {
                return dpi.Active && DesktopWindow(point);
            }
        }
        private static bool DesktopWindow(NativePoint point)
        {
            IntPtr window = WindowFromPoint(point);
            string name = ClassName(window), root = ClassName(GetAncestor(window, 2));
            if (root != "Progman" && root != "WorkerW") return false;
            return name == "Progman" || name == "WorkerW" || name == "SHELLDLL_DefView"
                || (name == "SysListView32" && ClassName(GetParent(window)) == "SHELLDLL_DefView");
        }
        internal static bool BlankDesktop(NativePoint point)
        {
            // Accessibility hit testing must use the very same physical point
            // and DPI context as the native desktop-window test.
            using (var dpi = new PhysicalScreenScope())
            {
                if (!dpi.Active || !DesktopWindow(point)) return false;
                IAccessible accessible = null;
                try
                {
                    object child;
                    if (AccessibleObjectFromPoint(point, out accessible, out child) != 0 || accessible == null) return false;
                    int role = Convert.ToInt32(accessible.get_accRole(child));
                    // Only the desktop list/client background qualifies.
                    // LISTITEM/TEXT icons and arbitrary window roles do not.
                    if (role == 0x21 || role == 0x0a) return true;
                    // When Explorer hides its desktop icons, only the real
                    // wallpaper window remains; it can expose WINDOW role.
                    string surface = ClassName(WindowFromPoint(point));
                    return role == 0x09 && (surface == "Progman" || surface == "WorkerW");
                }
                catch (Exception) { return false; }
                finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
            }
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr pointer)
        {
            try
            {
                if (code >= 0 && enabled && !disposed && message.ToInt32() == 0x0201)
                {
                    MouseData data = (MouseData)Marshal.PtrToStructure(pointer, typeof(MouseData));
                    bool desktop = (data.Flags & 1) == 0 && GetAsyncKeyState(0x10) >= 0
                        && GetAsyncKeyState(0x11) >= 0 && GetAsyncKeyState(0x12) >= 0 && DesktopSurface(data.Point);
                    NativePoint previous = first; first = data.Point;
                    if (gesture.Click(data.Time, data.Point.X, data.Point.Y, desktop,
                        GetDoubleClickTime(), GetSystemMetrics(36), GetSystemMetrics(37)))
                    {
                        NativePoint current = data.Point;
                        // Accessibility is cross-process COM. Keep it outside
                        // the low-level hook, which must always return quickly.
                        dispatcher.BeginInvoke(new Action(() => {
                            if (!disposed && enabled && BlankDesktop(previous) && BlankDesktop(current)) toggle();
                        }), DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception) { gesture.Reset(); }
            return CallNextHookEx(hook, code, message, pointer);
        }
        public void Dispose()
        {
            disposed = true;
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }
}
