using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("WSLBox")]
[assembly: System.Reflection.AssemblyProduct("WSLBox")]
// Declare the supported Framework target so networking uses the OS TLS policy.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyVersion(WSLBox.Branding.AssemblyVersion)]
[assembly: System.Reflection.AssemblyFileVersion(WSLBox.Branding.AssemblyVersion)]

namespace WSLBox
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            L10n.Initialize("auto");
#if LOCAL_TESTS
            L10n.Initialize(args.Length > 0 && args[0] == "--preview-en" ? "en-US" : args.Length > 0 && args[0] != "--diagnose" ? "zh-CN" : "auto");
#endif
            if (args.Length == 2 && args[0] == "--diagnose")
            {
                try
                {
                    try { L10n.Initialize(WorkspaceLayout.Load().Language); } catch { }
                    var client = new WslClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend.py"));
                    Reply result = client.RunOneShot("", "doctor", null).GetAwaiter().GetResult();
                    File.WriteAllText(args[1], Json.Write(result), Encoding.UTF8); Environment.ExitCode = result.Ok ? 0 : 2;
                }
                catch (Exception ex) { File.WriteAllText(args[1], Json.Write(new Reply { Error = ex.Message }), Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
#if LOCAL_TESTS
            if (args.Length == 2 && args[0] == "--update-check-test")
            {
                try { File.WriteAllText(args[1], UpdateService.Check(CancellationToken.None).GetAwaiter().GetResult().ToString(), Encoding.UTF8); }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            if (args.Length == 2 && args[0] == "--file-actions-test")
            {
                try { SelfTest.FileActionsSmoke(); File.WriteAllText(args[1], "PASS", Encoding.UTF8); }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
                finally { WslClient.ShutdownOpenWorkers(); }
                return;
            }
            if (args.Length == 2 && args[0] == "--benchmark-open")
            {
                try { SelfTest.BenchmarkOpen(args[1]); }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
                finally { WslClient.ShutdownOpenWorkers(); }
                return;
            }
            if ((args.Length == 2 || args.Length == 3) && (args[0] == "--preview" || args[0] == "--preview-en"))
            {
                try { SelfTest.Preview(args[1], args.Length == 3 ? args[2] : null); }
                catch (Exception ex) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "preview-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                finally { WslClient.ShutdownOpenWorkers(); }
                return;
            }
            if (args.Length == 2 && args[0] == "--self-test")
            {
                try { SelfTest.Run(); File.WriteAllText(args[1], "PASS", Encoding.UTF8); }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
                finally { WslClient.ShutdownOpenWorkers(); }
                return;
            }
#endif
            WorkspaceLayout savedLayout;
            try { savedLayout = WorkspaceLayout.Load(); L10n.Initialize(savedLayout.Language); }
            catch (Exception ex) { MessageBox.Show(ex.Message, L10n.T("ui.wslbox_startup_failed"), MessageBoxButton.OK, MessageBoxImage.Error); return; }
            bool created;
            using (var mutex = new Mutex(true, @"Local\WSLBox", out created))
            {
                if (!created) { MessageBox.Show(L10n.T("ui.wslbox_is_already_running_use_to_create_a_box_or_show_the_boxes_"), "WSLBox"); return; }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
                    WorkspaceLayout layout = savedLayout;
                    using (var workspace = new BoxWorkspace(app, layout))
                    {
                        workspace.OpenSaved(); app.Run();
                    }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, L10n.T("ui.wslbox_startup_failed"), MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
    }

    internal sealed class BoxWorkspace : IDisposable
    {
        private readonly Application app;
        private readonly WorkspaceLayout layout;
        private readonly List<BoxForm> windows = new List<BoxForm>();
        private readonly Forms.NotifyIcon tray;
        private readonly System.Drawing.Icon trayIcon;
        private bool exiting;
        private readonly DesktopToggle desktopToggle;
        private readonly List<BoxForm> desktopHidden = new List<BoxForm>();
        public BoxWorkspace(Application app, WorkspaceLayout layout)
        {
            this.app = app; this.layout = layout;
            desktopToggle = new DesktopToggle(app.Dispatcher, ToggleDesktopBoxes);
            desktopToggle.Enabled = layout.DesktopDoubleClick != false;
            trayIcon = Branding.CreateTrayIcon();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = L10n.T("ui.wslbox_desktop_folder_boxes"), Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(L10n.T("ui.show_all_boxes"), null, (s, e) => ShowAll());
            var desktopOption = new Forms.ToolStripMenuItem(L10n.T("ui.double_click_desktop_to_hide_show")) {
                CheckOnClick = true, Checked = desktopToggle.Enabled,
                Enabled = desktopToggle.Installed,
                ToolTipText = desktopToggle.Installed ? L10n.T("ui.only_blank_windows_desktop_areas_trigger_this_desktop_icons_are_") : L10n.T("ui.desktop_mouse_detection_could_not_start_restart_wslbox")
            };
            desktopOption.CheckedChanged += (s, e) => {
                layout.DesktopDoubleClick = desktopToggle.Enabled = desktopOption.Checked;
                if (!desktopOption.Checked) ShowAll();
                Save();
            };
            menu.Items.Add(desktopOption);
            var startup = new Forms.ToolStripMenuItem(L10n.T("startup.label")) { Checked = StartupRegistration.IsEnabled,
                ToolTipText = L10n.T("startup.hint") };
            startup.Click += (s, e) => { startup.Checked = ToggleStartAtLogin(); };
            menu.Opening += (s, e) => startup.Checked = StartupRegistration.IsEnabled;
            menu.Items.Add(startup);
            menu.Items.Add(L10n.T("ui.new_box_5cc522"), null, (s, e) => CreateBox(windows.FirstOrDefault()));
            var language = new Forms.ToolStripMenuItem(L10n.LanguageMenuLabel);
            Action refreshLanguage = L10n.BindLanguageChoices(() => LanguagePreference, SetLanguage, (label, select) =>
            {
                var option = new Forms.ToolStripMenuItem(label);
                option.Click += (s, e) => select();
                language.DropDownItems.Add(option);
                return value => option.Checked = value;
            });
            language.DropDownOpening += (s, e) => refreshLanguage();
            menu.Items.Add(language);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(L10n.T("ui.help_and_shortcuts"), null, (s, e) => Dialogs.Help(windows.FirstOrDefault(w => w.IsVisible)));
            menu.Items.Add(L10n.T("ui.about"), null, (s, e) => Dialogs.About(windows.FirstOrDefault(w => w.IsVisible)));
            menu.Items.Add(L10n.T("ui.exit"), null, (s, e) => Exit());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ShowAll();
        }
        public string LanguagePreference { get { return L10n.Normalize(layout.Language); } }
        public void SetLanguage(string value)
        {
            value = L10n.Normalize(value);
            if (value == LanguagePreference) return;
            layout.Language = value; Save();
            MessageBox.Show(L10n.T("settings.language_saved"), L10n.T("settings.language_saved_title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        internal Rect[] SnapPeers(BoxForm moving)
        {
            return windows.Where(window => window != moving && window.IsVisible && !window.IsClosing
                && window.WindowState != WindowState.Minimized).Select(window => window.BoundsIn(moving)).ToArray();
        }
        public bool ToggleStartAtLogin()
        {
            SetStartAtLogin(!StartupRegistration.IsEnabled);
            return StartupRegistration.IsEnabled;
        }
        public void SetStartAtLogin(bool enabled)
        {
            try { StartupRegistration.SetEnabled(enabled); }
            catch (Exception ex) { MessageBox.Show(L10n.F("startup.failed", ex.Message), "WSLBox", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        public void OpenSaved() { foreach (Settings box in layout.Boxes.ToArray()) Open(box, true); }
        private void Open(Settings box, bool background = false)
        {
            var window = new BoxForm(box, this);
            windows.Add(window);
            window.Closed += (s, e) => {
                windows.Remove(window); desktopHidden.Remove(window);
                if (!exiting && windows.Count > 0) layout.Boxes.Remove(box);
                Save();
            };
            window.ShowActivated = !background;
            if (background) window.ShowInBackground(); else window.Show();
        }
        public void CreateBox(BoxForm owner)
        {
            if (desktopHidden.Count > 0) ShowAll();
            Settings original = owner == null ? new Settings() : owner.Configuration;
            var dialog = new ConfigForm(original.Distro, owner == null ? original.Folder : owner.CurrentFolder, "", original.ShowTrashTab != false, original.LinuxUser);
            Dialogs.PlaceDialog(dialog, owner);
            if (dialog.ShowDialog() != true) return;
            var box = new Settings {
                Distro = dialog.Distro, LinuxUser = dialog.LinuxUser, Folder = dialog.Folder, Title = dialog.BoxTitle, ShowTrashTab = dialog.ShowTrashTab,
                X = original.X + 36, Y = original.Y + 36, Width = original.Width, Height = original.Height,
                Bounds = original.Bounds == null ? null : new SavedWindowBounds { Left = original.Bounds.Left + 36, Top = original.Bounds.Top + 36, Width = original.Bounds.Width, Height = original.Bounds.Height },
                BackgroundOpacity = original.BackgroundOpacity, ViewMode = original.ViewMode, IconSize = original.IconSize, IconSpacing = original.IconSpacing,
                SortBy = original.SortBy, SortDescending = original.SortDescending, ShowThumbnails = original.ShowThumbnails, ShowHidden = original.ShowHidden, ExpandOnHover = original.ExpandOnHover
            };
            box.Normalize(); layout.Boxes.Add(box); Open(box); Save();
        }
        public void Save()
        {
            try { layout.Save(); }
            catch (Exception ex) { MessageBox.Show(L10n.T("ui.could_not_save_the_box_layout") + ex.Message, "WSLBox"); }
        }
        private void ShowAll()
        {
            desktopHidden.Clear();
            foreach (BoxForm window in windows) window.ShowFromTray();
        }
        private void ToggleDesktopBoxes()
        {
            if (exiting || windows.Any(w => !w.CanClose || w.EditingName || w.OwnedWindows.Cast<Window>().Any(child => child.IsVisible))) return;
            if (desktopHidden.Count > 0)
            {
                foreach (BoxForm window in desktopHidden.ToArray())
                    if (windows.Contains(window)) window.ShowFromDesktop();
                desktopHidden.Clear();
            }
            else
            {
                foreach (BoxForm window in windows.Where(w => w.IsVisible).ToArray())
                {
                    desktopHidden.Add(window); window.Hide();
                }
            }
        }
        public void Exit()
        {
            if (windows.Any(w => !w.CanClose)) { MessageBox.Show(L10n.T("ui.wait_for_the_file_operation_to_finish_before_exiting"), "WSLBox"); return; }
            // Freeze every box before Windows starts activating/destroying siblings.
            foreach (BoxForm window in windows) window.PrepareForClose();
            Save(); exiting = true; app.Shutdown();
        }
        public void Dispose()
        {
            desktopToggle.Dispose();
            tray.Visible = false;
            if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            trayIcon.Dispose();
            WslClient.ShutdownOpenWorkers();
        }
    }
}
