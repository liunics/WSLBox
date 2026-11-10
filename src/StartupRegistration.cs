using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WSLBox
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class StartupShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IStartupShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short key);
        void SetHotkey(short key);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr owner, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    internal static class StartupRegistration
    {
        internal static string DirectoryPath { get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); } }
        internal static bool IsEnabled { get { return File.Exists(Path.Combine(DirectoryPath, "WSLBox.lnk")); } }
        internal static void SetEnabled(bool enabled)
        {
            SetEnabled(enabled, DirectoryPath, Assembly.GetExecutingAssembly().Location);
        }
        internal static void SetEnabled(bool enabled, string directory, string executable)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new IOException(L10n.T("startup.folder_unavailable"));
            string path = Path.Combine(directory, "WSLBox.lnk");
            if (!enabled) { File.Delete(path); return; }
            executable = Path.GetFullPath(executable);
            if (!File.Exists(executable)) throw new FileNotFoundException(L10n.T("startup.executable_unavailable"));
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "WSLBox-" + Guid.NewGuid().ToString("N") + ".lnk");
            object instance = null;
            try
            {
                instance = new StartupShellLink();
                var link = (IStartupShellLink)instance;
                link.SetPath(executable); link.SetWorkingDirectory(Path.GetDirectoryName(executable));
                link.SetDescription("WSLBox"); link.SetArguments(""); link.SetIconLocation(executable, 0); link.SetShowCmd(1);
                ((IPersistFile)instance).Save(temporary, true);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (instance != null) Marshal.FinalReleaseComObject(instance);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
