using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WSLBox
{
    // The signatures follow the native vtable order, including unused methods.
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(IntPtr dialog);
        void SetProperties(IntPtr properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem destination);
        void DeleteItem(IShellItem item, IntPtr sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    public static class ShellTransfer
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        private static IShellItem Item(string path)
        {
            Guid iid = typeof(IShellItem).GUID;
            IShellItem item;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item);
            return item;
        }

        public static Task<bool> Run(IntPtr owner, string[] sources, string destination, bool move)
        {
            Paths.ValidateTransfer(sources, destination, move);
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(() => {
                IFileOperation operation = null;
                var items = new List<IShellItem>();
                try
                {
                    operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
                        new Guid("3AD05575-8857-4850-9277-11B85BDB8E09")));
                    // Keep native conflict/error/progress UI. Never automatically
                    // overwrite, and leave cross-volume source deletion to Windows.
                    operation.SetOperationFlags(0x0040 | 0x20000000);
                    operation.SetOwnerWindow(owner);
                    IShellItem target = Item(destination);
                    items.Add(target);
                    foreach (string source in sources)
                    {
                        IShellItem item = Item(source);
                        items.Add(item);
                        if (move) operation.MoveItem(item, target, null, IntPtr.Zero);
                        else operation.CopyItem(item, target, null, IntPtr.Zero);
                    }
                    operation.PerformOperations();
                    bool aborted;
                    operation.GetAnyOperationsAborted(out aborted);
                    completion.SetResult(!aborted);
                }
                catch (Exception ex) { completion.SetException(ex); }
                finally
                {
                    foreach (IShellItem item in items) Marshal.ReleaseComObject(item);
                    if (operation != null) Marshal.ReleaseComObject(operation);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }
    }
}
