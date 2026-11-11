using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace WSLBox
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr bindContext, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out IntPtr pidl, ref uint attributes);
        void EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumerator);
        void BindToObject(IntPtr pidl, IntPtr context, ref Guid iid, out IntPtr result);
        void BindToStorage(IntPtr pidl, IntPtr context, ref Guid iid, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr hint, IntPtr first, IntPtr second);
        void CreateViewObject(IntPtr hwnd, ref Guid iid, out IntPtr result);
        void GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] children, ref uint attributes);
        void GetUIObjectOf(IntPtr hwnd, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] children, ref Guid iid, IntPtr reserved, out IntPtr result);
        void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr strret);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr result);
    }
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        void InvokeCommand(ref InvokeCommandInfo info);
        void GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint length);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        void InvokeCommand(ref InvokeCommandInfo info);
        void GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wparam, IntPtr lparam);
    }
    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
        void InvokeCommand(ref InvokeCommandInfo info);
        void GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr name, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wparam, IntPtr lparam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wparam, IntPtr lparam, out IntPtr result);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InvokeCommandInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Owner, Verb, Parameters, Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon, Title, VerbW, ParametersW, DirectoryW, TitleW;
        public int X, Y;
    }

    public enum ShellMenuAction
    {
        None = 0, OpenLinux = 1, Configure = 2, NewBox = 3, Help = 4, Restore = 5, Home = 6, Trash = 7, Rename = 8, PermanentDelete = 9,
        LargeIcons = 10, MediumIcons = 11, SmallIcons = 12, List = 13, ToggleThumbnails = 14, ToggleHidden = 15,
        SortName = 20, SortSize = 21, SortType = 22, SortModified = 23,
        Ascending = 24, Descending = 25, Refresh = 30, NewFolder = 31, OpenTrash = 32, OpenWithLinux = 33, CloseBox = 34, ExitProgram = 35
    }

    public sealed class ShellMenuInvocationException : IOException
    {
        public ShellMenuInvocationException(Exception error)
            : base(L10n.F("shell.command_failed", error.HResult.ToString("X8"), error.Message), error) { }
    }

    public sealed class ShellContextMenu : IDisposable
    {
        private const uint First = 1, Last = 0x6fff, Custom = 0x8000;
        private readonly List<IntPtr> pidls = new List<IntPtr>();
        private IShellFolder folder;
        private IContextMenu context;
        private IContextMenu2 context2;
        private IContextMenu3 context3;
        private IntPtr menu;
        private HwndSource source;
        private HwndSourceHook hook;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint mask, out uint attributes);
        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHBindToParent(IntPtr pidl, ref Guid iid, out IShellFolder folder, out IntPtr child);
        [DllImport("shell32.dll")]
        private static extern IntPtr ILFindLastID(IntPtr pidl);
        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr menu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMenuString(IntPtr menu, uint position, StringBuilder text, int maximum, uint flags);
        [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool ModifyMenu(IntPtr menu, uint item, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")]
        private static extern bool CheckMenuRadioItem(IntPtr menu, uint first, uint last, uint selected, uint flags);
        [DllImport("user32.dll")]
        private static extern uint CheckMenuItem(IntPtr menu, uint command, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InsertMenuNative(IntPtr menu, uint position, uint flags, UIntPtr id, string label);
        private static bool InsertMenu(IntPtr menu, uint position, uint flags, UIntPtr id, string label)
        {
            return InsertMenuNative(menu, position, flags, id, MenuText.Clean(label));
        }
        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool RemoveMenu(IntPtr menu, uint position, uint flags);
        [DllImport("user32.dll")] private static extern uint EnableMenuItem(IntPtr menu, uint id, uint flags);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr owner, uint message, IntPtr wparam, IntPtr lparam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MenuItemText
        {
            public uint Size, Mask, Type, State, Id;
            public IntPtr Submenu, CheckedBitmap, UncheckedBitmap;
            public UIntPtr Data;
            public IntPtr Text;
            public uint Length;
            public IntPtr Bitmap;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetMenuItemInfoW", ExactSpelling = true)]
        private static extern bool SetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MenuItemText info);
        [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
        private static void CleanMenuLabels(IntPtr popup, HashSet<IntPtr> visited)
        {
            if (popup == IntPtr.Zero || !visited.Add(popup)) return;
            int count = GetMenuItemCount(popup);
            for (int index = 0; index < count; index++)
            {
                int size = GetMenuString(popup, (uint)index, null, 0, 0x400);
                var text = new StringBuilder(Math.Max(512, size + 1));
                GetMenuString(popup, (uint)index, text, text.Capacity, 0x400);
                string original = text.ToString(), clean = MenuText.Clean(original);
                if (original != clean)
                {
                    IntPtr value = Marshal.StringToHGlobalUni(clean);
                    try
                    {
                        // MIIM_STRING changes text only: preserve command IDs,
                        // icons, checked/disabled states and submenu handles.
                        var info = new MenuItemText { Size = (uint)Marshal.SizeOf(typeof(MenuItemText)), Mask = 0x40,
                            Text = value, Length = (uint)clean.Length };
                        SetMenuItemInfo(popup, (uint)index, true, ref info);
                    }
                    finally { Marshal.FreeHGlobal(value); }
                }
                CleanMenuLabels(GetSubMenu(popup, index), visited);
            }
        }
        private IntPtr Parse(string path)
        {
            IntPtr pidl; uint attributes;
            SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attributes);
            if (pidl == IntPtr.Zero) throw new IOException(L10n.T("ui.windows_cannot_resolve_this_path"));
            pidls.Add(pidl); return pidl;
        }
        private int Build(IntPtr owner, string[] paths, string directory)
        {
            Guid folderId = typeof(IShellFolder).GUID, menuId = typeof(IContextMenu).GUID;
            IntPtr raw;
            if (paths.Length > 0)
            {
                IntPtr first = Parse(paths[0]), child;
                SHBindToParent(first, ref folderId, out folder, out child);
                var children = new IntPtr[paths.Length]; children[0] = child;
                string parent = Path.GetDirectoryName(paths[0]);
                for (int i = 1; i < paths.Length; i++)
                {
                    if (!Paths.Same(parent, Path.GetDirectoryName(paths[i]))) throw new IOException(L10n.T("ui.select_items_from_the_same_folder_to_use_the_native_menu"));
                    children[i] = ILFindLastID(Parse(paths[i]));
                }
                folder.GetUIObjectOf(owner, (uint)children.Length, children, ref menuId, IntPtr.Zero, out raw);
            }
            else
            {
                IntPtr full = Parse(directory), child;
                IShellFolder parent;
                SHBindToParent(full, ref folderId, out parent, out child);
                try
                {
                    IntPtr bound; parent.BindToObject(child, IntPtr.Zero, ref folderId, out bound);
                    try { folder = (IShellFolder)Marshal.GetObjectForIUnknown(bound); }
                    finally { Marshal.Release(bound); }
                }
                finally { Marshal.ReleaseComObject(parent); }
                folder.CreateViewObject(owner, ref menuId, out raw);
            }
            try { context = (IContextMenu)Marshal.GetObjectForIUnknown(raw); }
            finally { Marshal.Release(raw); }
            context3 = context as IContextMenu3; context2 = context as IContextMenu2;
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new IOException(L10n.T("ui.windows_could_not_create_the_context_menu"));
            Marshal.ThrowExceptionForHR(context.QueryContextMenu(menu, 0, First, Last, paths.Length > 0 ? 0x10u : 0u));
            if (paths.Length > 0)
                for (int index = 0; index < GetMenuItemCount(menu); index++)
                {
                    uint id = GetMenuItemID(menu, index);
                    if (id >= First && id <= Last && CanonicalVerb(id) == "delete")
                        ModifyMenu(menu, id, 0, (UIntPtr)id, L10n.T("ui.move_to_linux_trash_d_delete"));
                }
            return GetMenuItemCount(menu);
        }
        private string CanonicalVerb(uint command)
        {
            IntPtr buffer = Marshal.AllocCoTaskMem(1024);
            try
            {
                foreach (uint type in new uint[] { 4, 0 })
                {
                    try
                    {
                        Marshal.Copy(new byte[1024], 0, buffer, 1024);
                        context.GetCommandString((UIntPtr)(command - First), type, IntPtr.Zero, buffer, 511);
                        string verb = type == 4 ? Marshal.PtrToStringUni(buffer) : Marshal.PtrToStringAnsi(buffer);
                        if (!String.IsNullOrEmpty(verb)) return verb.ToLowerInvariant();
                    }
                    catch (Exception ex) { if (!(ex is COMException) && !(ex is ArgumentException) && !(ex is InvalidCastException)
                            && !(ex is NotImplementedException) && !(ex is UnauthorizedAccessException)) throw; }
                }
                return "";
            }
            finally { Marshal.FreeCoTaskMem(buffer); }
        }
        public static string[] InspectVerbs(string[] paths, string directory)
        {
            using (var native = new ShellContextMenu())
            {
                native.Build(IntPtr.Zero, paths, directory);
                var verbs = new List<string>();
                for (int i = 0; i < GetMenuItemCount(native.menu); i++)
                {
                    uint id = GetMenuItemID(native.menu, i);
                    if (id >= First && id <= Last) verbs.Add(native.CanonicalVerb(id));
                }
                return verbs.ToArray();
            }
        }
        public static int Inspect(string[] paths, string directory)
        {
            // Read-only validation: query native items, never invoke a verb.
            using (var native = new ShellContextMenu()) return native.Build(IntPtr.Zero, paths, directory);
        }
        public static string[] InspectLabels(string[] paths, string directory)
        {
            using (var native = new ShellContextMenu())
            {
                int count = native.Build(IntPtr.Zero, paths, directory);
                var labels = new List<string>();
                for (uint index = 0; index < count; index++)
                {
                    var text = new StringBuilder(512);
                    GetMenuString(native.menu, index, text, text.Capacity, 0x400);
                    if (text.Length > 0) labels.Add(text.ToString());
                }
                return labels.ToArray();
            }
        }
        public static ShellMenuAction Show(Window window, Point screen, string[] paths, string directory, Settings settings)
        {
            using (var native = new ShellContextMenu()) return native.ShowCore(window, screen, paths, directory, settings);
        }
        private void AddOption(IntPtr popup, ShellMenuAction action, string text)
        {
            if (!InsertMenu(popup, (uint)GetMenuItemCount(popup), 0x400, (UIntPtr)(Custom + (uint)action), text))
                throw new IOException(L10n.T("ui.could_not_create_the_view_or_sort_menu"));
        }
        private void AddPopup(uint position, string name, IntPtr popup)
        {
            UIntPtr handle = IntPtr.Size == 8 ? new UIntPtr((ulong)popup.ToInt64()) : new UIntPtr((uint)popup.ToInt32());
            if (!InsertMenu(menu, position, 0x400 | 0x10, handle, name))
            { DestroyMenu(popup); throw new IOException(L10n.T("ui.could_not_insert_a_menu_item")); }
        }
        private void AddViewMenus(Settings settings)
        {
            IntPtr view = CreatePopupMenu();
            AddPopup(0, L10n.T("ui.view_v"), view);
            AddOption(view, ShellMenuAction.LargeIcons, L10n.T("ui.large_icons_r"));
            AddOption(view, ShellMenuAction.MediumIcons, L10n.T("ui.medium_icons_m"));
            AddOption(view, ShellMenuAction.SmallIcons, L10n.T("ui.small_icons_n"));
            AddOption(view, ShellMenuAction.List, L10n.T("ui.list_l"));
            InsertMenu(view, 4, 0x400 | 0x800, UIntPtr.Zero, null);
            AddOption(view, ShellMenuAction.ToggleThumbnails, L10n.T("ui.show_thumbnails"));
            CheckMenuItem(view, Custom + 14, settings.ShowThumbnails != false ? 8u : 0u);
            AddOption(view, ShellMenuAction.ToggleHidden, L10n.T("ui.show_hidden"));
            CheckMenuItem(view, Custom + 15, settings.ShowHidden ? 8u : 0u);
            uint currentView = (uint)(settings.ViewMode == "list" ? ShellMenuAction.List :
                settings.IconSize == 64 ? ShellMenuAction.LargeIcons : settings.IconSize == 24 ? ShellMenuAction.SmallIcons : ShellMenuAction.MediumIcons);
            CheckMenuRadioItem(view, Custom + 10, Custom + 13, Custom + currentView, 0);
            IntPtr sort = CreatePopupMenu(); AddPopup(1, L10n.T("ui.sort_by_o"), sort);
            AddOption(sort, ShellMenuAction.SortName, L10n.T("ui.name")); AddOption(sort, ShellMenuAction.SortSize, L10n.T("ui.size"));
            AddOption(sort, ShellMenuAction.SortType, L10n.T("ui.type")); AddOption(sort, ShellMenuAction.SortModified, L10n.T("ui.date_modified"));
            InsertMenu(sort, 4, 0x400 | 0x800, UIntPtr.Zero, null);
            AddOption(sort, ShellMenuAction.Ascending, L10n.T("ui.ascending")); AddOption(sort, ShellMenuAction.Descending, L10n.T("ui.descending"));
            uint currentSort = (uint)(settings.SortBy == "size" ? ShellMenuAction.SortSize : settings.SortBy == "type" ? ShellMenuAction.SortType :
                settings.SortBy == "modified" ? ShellMenuAction.SortModified : ShellMenuAction.SortName);
            CheckMenuRadioItem(sort, Custom + 20, Custom + 23, Custom + currentSort, 0);
            CheckMenuRadioItem(sort, Custom + 24, Custom + 25, Custom + (settings.SortDescending ? 25u : 24u), 0);
            InsertMenu(menu, 2, 0x400, (UIntPtr)(Custom + 30), L10n.T("ui.refresh_e"));
            InsertMenu(menu, 3, 0x400 | 0x800, UIntPtr.Zero, null);
        }
        public static int InspectOptions(string directory, Settings settings)
        {
            using (var native = new ShellContextMenu())
            {
                native.Build(IntPtr.Zero, new string[0], directory);
                int before = GetMenuItemCount(native.menu); native.AddViewMenus(settings);
                return GetMenuItemCount(native.menu) - before;
            }
        }
        private void OrganizeMenus(int selected, Settings settings)
        {
            if (selected > 0)
            {
                // Keep Shell IDs unchanged while removing duplicate native
                // entries: Linux rename/trash are presented in one top block.
                for (int index = GetMenuItemCount(menu) - 1; index >= 0; index--)
                {
                    uint id = GetMenuItemID(menu, index);
                    if (id < First || id > Last) continue;
                    string verb = CanonicalVerb(id);
                    if (verb == "delete" || verb == "rename") RemoveMenu(menu, (uint)index, 0x400);
                }
                InsertMenu(menu, 0, 0x400, (UIntPtr)(Custom + 1), L10n.T("ui.open_with_linux_default_app"));
                InsertMenu(menu, 1, 0x400, (UIntPtr)(Custom + 33), L10n.T("ui.linux_open_with"));
                InsertMenu(menu, 2, 0x400, (UIntPtr)(Custom + 8), L10n.T("ui.rename_f2"));
                InsertMenu(menu, 3, 0x400, (UIntPtr)(Custom + 7), L10n.T("ui.move_to_linux_trash_delete"));
                InsertMenu(menu, 4, 0x400, (UIntPtr)(Custom + 32), L10n.T("ui.open_linux_trash"));
                InsertMenu(menu, 5, 0x400 | 0x800, UIntPtr.Zero, null);
                if (selected != 1)
                {
                    EnableMenuItem(menu, Custom + 33, 1);
                    EnableMenuItem(menu, Custom + 8, 1);
                }
            }
            else
            {
                AddViewMenus(settings);
                InsertMenu(menu, 0, 0x400, (UIntPtr)(Custom + 2), L10n.T("ui.folder_settings_ctrl_l"));
                InsertMenu(menu, 1, 0x400, (UIntPtr)(Custom + 6), L10n.T("ui.bound_folder_alt_home"));
                InsertMenu(menu, 2, 0x400, (UIntPtr)(Custom + 31), L10n.T("ui.new_folder_ctrl_shift_n"));
                InsertMenu(menu, 3, 0x400, (UIntPtr)(Custom + 32), L10n.T("ui.open_linux_trash"));
                InsertMenu(menu, 4, 0x400 | 0x800, UIntPtr.Zero, null);
                uint end = (uint)GetMenuItemCount(menu);
                InsertMenu(menu, end++, 0x400 | 0x800, UIntPtr.Zero, null);
                InsertMenu(menu, end++, 0x400, (UIntPtr)(Custom + 5), L10n.T("ui.restore_window_ctrl_shift_r"));
                InsertMenu(menu, end++, 0x400, (UIntPtr)(Custom + 3), L10n.T("ui.new_box_ctrl_n"));
                InsertMenu(menu, end++, 0x400 | 0x800, UIntPtr.Zero, null);
                InsertMenu(menu, end++, 0x400, (UIntPtr)(Custom + 4), L10n.T("ui.help_and_shortcuts"));
                InsertMenu(menu, end++, 0x400 | 0x800, UIntPtr.Zero, null);
                InsertMenu(menu, end++, 0x400, (UIntPtr)(Custom + 34), L10n.T("ui.close_this_box"));
                InsertMenu(menu, end, 0x400, (UIntPtr)(Custom + 35), L10n.T("ui.exit"));
            }
            CleanMenuLabels(menu, new HashSet<IntPtr>());
        }
        public static string[] InspectOrganizedLabels(string[] paths, string directory, Settings settings)
        {
            using (var native = new ShellContextMenu())
            {
                native.Build(IntPtr.Zero, paths, directory);
                native.OrganizeMenus(paths.Length, settings);
                var labels = new List<string>();
                for (uint index = 0; index < GetMenuItemCount(native.menu); index++)
                {
                    var text = new StringBuilder(512);
                    GetMenuString(native.menu, index, text, text.Capacity, 0x400);
                    labels.Add(text.Length == 0 ? "---" : text.ToString());
                }
                return labels.ToArray();
            }
        }
        private ShellMenuAction ShowCore(Window window, Point screen, string[] paths, string directory, Settings settings)
        {
            IntPtr owner = new WindowInteropHelper(window).Handle;
            Build(owner, paths, directory);
            OrganizeMenus(paths.Length, settings);
            source = HwndSource.FromHwnd(owner); hook = HandleMenuMessage;
            if (source != null) source.AddHook(hook);
            SetForegroundWindow(owner);
            uint command = TrackPopupMenuEx(menu, 0x100 | 0x2, (int)screen.X, (int)screen.Y, owner, IntPtr.Zero);
            PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
            if (command > Custom && Enum.IsDefined(typeof(ShellMenuAction), (int)(command - Custom)))
            {
                ShellMenuAction action = (ShellMenuAction)(command - Custom);
                return action == ShellMenuAction.Trash && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
                    ? ShellMenuAction.PermanentDelete : action;
            }
            if (command >= First && command <= Last)
            {
                // Native commands can be invoked directly by ordinal.
                // Some archive extensions do not implement GetCommandString;
                // our Linux rename/trash commands already have custom IDs.
                var info = new InvokeCommandInfo {
                    Size = Marshal.SizeOf(typeof(InvokeCommandInfo)), Mask = 0x4000 | 0x20000000,
                    Owner = owner, Verb = (IntPtr)(command - First), VerbW = (IntPtr)(command - First),
                    Show = 1, X = (int)screen.X, Y = (int)screen.Y
                };
                try { context.InvokeCommand(ref info); }
                catch (Exception ex) { throw new ShellMenuInvocationException(ex); }
            }
            return ShellMenuAction.None;
        }
        private IntPtr HandleMenuMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message != 0x117 && message != 0x2b && message != 0x2c && message != 0x120) return IntPtr.Zero;
            if ((message == 0x2b || message == 0x2c) && wparam != IntPtr.Zero) return IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            if (context3 != null)
                handled = context3.HandleMenuMsg2((uint)message, wparam, lparam, out result) >= 0;
            else if (context2 != null) handled = context2.HandleMenuMsg((uint)message, wparam, lparam) >= 0;
            // Shell extensions can populate a submenu only when it opens.
            if (message == 0x117) CleanMenuLabels(wparam, new HashSet<IntPtr>());
            return handled ? result : IntPtr.Zero;
        }
        public void Dispose()
        {
            if (source != null && hook != null) source.RemoveHook(hook);
            if (menu != IntPtr.Zero) { DestroyMenu(menu); menu = IntPtr.Zero; }
            context2 = null; context3 = null;
            if (context != null) { Marshal.ReleaseComObject(context); context = null; }
            if (folder != null) { Marshal.ReleaseComObject(folder); folder = null; }
            foreach (IntPtr pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            pidls.Clear();
        }
    }
}
