using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WSLBox
{
    internal static class WslSetup
    {
        public static Task<string[]> Distributions()
        {
            return Task.Run(() => {
                using (var process = Process.Start(new ProcessStartInfo("wsl.exe", "--list --quiet") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                }))
                {
                    var output = Read(process.StandardOutput.BaseStream);
                    var errors = Read(process.StandardError.BaseStream);
                    if (!process.WaitForExit(12000)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new IOException(L10n.T("ui.timed_out_loading_distributions_try_again")); }
                    if (!Task.WaitAll(new Task[] { output, errors }, 3000)) throw new IOException(L10n.T("ui.timed_out_reading_the_distribution_list"));
                    if (process.ExitCode != 0) throw new IOException(L10n.T("ui.could_not_load_wsl_distributions") + Decode(errors.Result));
                    return ParseDistributions(Decode(output.Result));
                }
            });
        }
        private static async Task<byte[]> Read(Stream stream)
        {
            using (var memory = new MemoryStream()) { await stream.CopyToAsync(memory); return memory.ToArray(); }
        }
        private static string Decode(byte[] bytes)
        {
            return (bytes.Take(512).Any(b => b == 0) || (bytes.Length > 1 && bytes[0] == 255 && bytes[1] == 254)
                ? Encoding.Unicode : Encoding.UTF8).GetString(bytes).Trim('\ufeff', '\0');
        }
        internal static string[] ParseDistributions(string value)
        {
            return value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim('\ufeff', '\0', ' ', '\t')).Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static string DefaultDistribution()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss"))
            {
                string id = key == null ? null : key.GetValue("DefaultDistribution") as string;
                using (var distro = String.IsNullOrEmpty(id) ? null : key.OpenSubKey(id))
                    return distro == null ? null : distro.GetValue("DistributionName") as string;
            }
        }
        public static string LinuxPath(string distro, string value)
        {
            string path = (value ?? "").Trim();
            if (Paths.IsWsl(path))
            {
                string[] parts = path.Substring(2).Split(new[] { '\\' }, 3);
                if (parts.Length < 2 || !String.Equals(parts[1], distro, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(L10n.T("ui.this_folder_belongs_to_a_different_distribution_select_that_dist"));
                path = parts.Length == 3 ? "/" + parts[2].Replace('\\', '/') : "/";
            }
            Paths.Unc(distro, path);
            if (path.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new ArgumentException(L10n.T("ui.the_folder_path_contains_invalid_characters"));
            return path;
        }
    }

    // IFileDialog's native vtable order, through GetResult.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFolderDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr types);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }
    internal static class FolderPicker
    {
        public static string Pick(Window owner, string distro, string linuxPath)
        {
            IFolderDialog dialog = null;
            IShellItem initial = null, selected = null;
            try
            {
                dialog = (IFolderDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
                uint options; dialog.GetOptions(out options);
                dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x02000000); // pick folders, filesystem, existing path, don't add to recent
                dialog.SetTitle(L10n.F("settings.picker_title", distro));
                dialog.SetOkButtonLabel(L10n.T("ui.select_folder"));
                try { initial = ShellTransfer.CreateItem(Paths.Unc(distro, linuxPath)); }
                catch (COMException) { initial = ShellTransfer.CreateItem(Paths.Unc(distro, "/")); }
                dialog.SetFolder(initial);
                int result = dialog.Show(new WindowInteropHelper(owner).Handle);
                if (result == unchecked((int)0x800704C7)) return null;
                Marshal.ThrowExceptionForHR(result);
                dialog.GetResult(out selected);
                IntPtr name; selected.GetDisplayName(0x80058000, out name); // SIGDN_FILESYSPATH
                try { return WslSetup.LinuxPath(distro, Marshal.PtrToStringUni(name)); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            finally
            {
                if (selected != null) Marshal.ReleaseComObject(selected);
                if (initial != null) Marshal.ReleaseComObject(initial);
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }
    }
}
