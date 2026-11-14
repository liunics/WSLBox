using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WSLBox
{
    internal sealed class ConfigForm : Window
    {
        private readonly ComboBox distro = new ComboBox { IsEditable = false, MinWidth = 0 };
        private readonly ComboBox linuxUser = new ComboBox { IsEditable = true, MinWidth = 0 };
        private readonly string defaultUser = L10n.T("ui.default_linux_user");
        private static readonly string backendPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend.py");
        private readonly CheckBox showTrashTab = new CheckBox();
        private readonly TextBox folder = new TextBox(), title = new TextBox();
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.PanelMuted };
        private readonly TextBlock titleHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.PanelMuted };
        private readonly Button browse = new Button { Content = L10n.T("ui.browse") }, home = new Button { Content = L10n.T("ui.home") };
        private readonly Button reload = new Button { Content = L10n.T("ui.refresh") }, ok = new Button { Content = L10n.T("ui.ok"), IsDefault = true };
        private readonly WslClient client = new WslClient(backendPath);
        private bool loading, closed;
        private int homeGeneration;
        public string Distro { get { return distro.SelectedItem as string ?? ""; } }
        public string LinuxUser { get { return linuxUser.Text == defaultUser ? "" : linuxUser.Text.Trim(); } }
        public string Folder { get { return folder.Text.Trim(); } }
        public string BoxTitle { get { return title.Text.Trim(); } }
        public bool ShowTrashTab { get { return showTrashTab.IsChecked == true; } }
        public ConfigForm(string distribution, string path, string boxTitle = "", bool showTrash = true, string user = "")
        {
            Title = L10n.T("ui.folder_settings"); Width = 600; MinWidth = 480;
            Icon = Branding.Image; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Theme.ApplyPanelTheme(this);
            var layout = new StackPanel { Margin = new Thickness(22) };
            layout.Children.Add(new TextBlock { Text = L10n.T("ui.choose_a_distribution_and_a_folder_to_display"), FontSize = 15,
                Margin = new Thickness(0, 0, 0, 16) });
            var fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            var distributions = new DockPanel { LastChildFill = true };
            reload.Margin = new Thickness(8, 0, 0, 0); reload.Padding = new Thickness(12, 5, 12, 5);
            DockPanel.SetDock(reload, Dock.Right); distributions.Children.Add(reload); distributions.Children.Add(distro);
            AddField(fields, L10n.T("ui.distribution"), distributions, 0);
            AddField(fields, L10n.T("ui.linux_user"), linuxUser, 1);
            linuxUser.ItemsSource = new[] { defaultUser };
            linuxUser.Text = String.IsNullOrEmpty(user) ? defaultUser : user;
            var folders = new DockPanel { LastChildFill = true };
            foreach (Button button in new[] { home, browse })
            {
                button.Padding = new Thickness(10, 5, 10, 5); button.Margin = new Thickness(7, 0, 0, 0);
                DockPanel.SetDock(button, Dock.Right); folders.Children.Add(button);
            }
            folders.Children.Add(folder); AddField(fields, L10n.T("ui.folder"), folders, 2);
            AddField(fields, L10n.T("ui.box_name"), title, 3);
            foreach (Control input in new Control[] { distro, linuxUser, folder, title }) input.Padding = new Thickness(7, 6, 7, 6);
            if (!String.IsNullOrWhiteSpace(distribution)) { distro.Items.Add(distribution); distro.SelectedIndex = 0; }
            folder.Text = path ?? ""; title.Text = boxTitle ?? "";
            layout.Children.Add(fields);
            layout.Children.Add(new TextBlock { Text = L10n.T("ui.accepts_linux_paths_or_wsl_unc_paths_such_as_wsl_localhost_and_w"),
                Foreground = Theme.PanelMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 5) });
            titleHint.Margin = new Thickness(0, 0, 0, 12); layout.Children.Add(titleHint);
            layout.Children.Add(new TextBlock { Text = L10n.T("ui.windows_access_as_default_user"),
                Foreground = Theme.PanelMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            showTrashTab.Content = L10n.T("ui.show_trash_tab");
            showTrashTab.IsChecked = showTrash;
            showTrashTab.Margin = new Thickness(0, 0, 0, 12);
            layout.Children.Add(showTrashTab);
            status.Margin = new Thickness(0, 0, 0, 16); layout.Children.Add(status);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            ok.MinWidth = 80; ok.Padding = new Thickness(14, 7, 14, 7); ok.IsEnabled = false;
            var cancel = new Button { Content = L10n.T("ui.cancel"), MinWidth = 80, Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            right.Children.Add(ok); right.Children.Add(cancel); layout.Children.Add(right);
            Content = layout;
            folder.TextChanged += (sender, e) => UpdateTitleHint();
            title.TextChanged += (sender, e) => UpdateTitleHint();
            distro.SelectionChanged += async (sender, e) => {
                if (loading || closed) return;
                folder.Text = "";
                linuxUser.Text = defaultUser;
                SetWorking(true);
                try { await LoadLinuxUsers(); }
                finally { if (!closed) SetWorking(false); }
                if (!closed) await HomeFolder();
            };
            reload.Click += async (sender, e) => await LoadDistributions(Distro);
            home.Click += async (sender, e) => await HomeFolder();
            browse.Click += async (sender, e) => {
                if (String.IsNullOrEmpty(Distro)) return;
                try
                {
                    if (String.IsNullOrWhiteSpace(folder.Text)) await HomeFolder();
                    if (closed) return;
                    string selected = FolderPicker.Pick(this, Distro, WslSetup.LinuxPath(Distro, folder.Text));
                    if (selected != null) { folder.Text = selected; Message(L10n.T("ui.folder_selected_access_permissions_will_be_checked_when_you_conf"), false); }
                }
                catch (Exception ex) { Message(ex.Message, true); }
            };
            ok.Click += async (sender, e) => {
                try
                {
                    string selected = WslSetup.LinuxPath(Distro, Folder);
                    SetWorking(true); Message(L10n.T("ui.checking_the_folder"), false);
                    Reply reply = await new WslClient(backendPath, LinuxUser).Run(Distro, "folder-info", selected);
                    if (closed) return;
                    folder.Text = reply.Path; DialogResult = true;
                }
                catch (Exception ex) { if (!closed) Message(ex.Message, true); }
                finally { if (!closed) SetWorking(false); }
            };
            Loaded += async (sender, e) => await LoadDistributions(distribution);
            Closed += (sender, e) => { closed = true; homeGeneration++; };
            UpdateTitleHint();
        }
        private void Message(string text, bool error)
        {
            status.Text = text; status.Foreground = error ? Theme.PanelError : Theme.PanelMuted;
        }
        private void SetWorking(bool working)
        {
            loading = working;
            distro.IsEnabled = reload.IsEnabled = title.IsEnabled = !working;
            linuxUser.IsEnabled = browse.IsEnabled = home.IsEnabled = folder.IsEnabled = ok.IsEnabled = !working && !String.IsNullOrEmpty(Distro);
        }
        private async System.Threading.Tasks.Task LoadDistributions(string preferred)
        {
            SetWorking(true); Message(L10n.T("ui.loading_installed_wsl_distributions"), false);
            try
            {
                string[] names = await WslSetup.Distributions();
                if (closed) return;
                distro.ItemsSource = null; distro.Items.Clear(); distro.ItemsSource = names;
                string choice = !String.IsNullOrWhiteSpace(preferred) ? preferred : WslSetup.DefaultDistribution();
                distro.SelectedItem = System.Linq.Enumerable.FirstOrDefault(names, name => String.Equals(name, choice, StringComparison.OrdinalIgnoreCase));
                if (distro.SelectedItem == null && String.IsNullOrWhiteSpace(preferred) && names.Length > 0) distro.SelectedIndex = 0;
                if (!String.IsNullOrEmpty(Distro)) await LoadLinuxUsers();
                if (closed) return;
                Message(names.Length == 0 ? L10n.T("ui.no_distribution_found_install_a_wsl_distribution_then_refresh") :
                    distro.SelectedItem == null ? L10n.T("ui.the_previous_distribution_is_unavailable_choose_another_one") : L10n.T("ui.distribution_list_updated"), names.Length == 0 || distro.SelectedItem == null);
            }
            catch (Exception ex) { if (!closed) { distro.SelectedIndex = -1; Message(ex.Message, true); } }
            finally { if (!closed) SetWorking(false); }
            if (!closed && !String.IsNullOrEmpty(Distro) && String.IsNullOrWhiteSpace(folder.Text)) await HomeFolder();
        }
        private async System.Threading.Tasks.Task LoadLinuxUsers()
        {
            string selected = LinuxUser;
            string[] names = new string[0];
            try { names = (await client.Run(Distro, "users", null)).Users ?? names; }
            catch { /* The editable field still accepts an account name. */ }
            if (closed) return;
            linuxUser.ItemsSource = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(new[] { defaultUser }, names));
            linuxUser.Text = String.IsNullOrEmpty(selected) ? defaultUser : selected;
        }
        private async System.Threading.Tasks.Task HomeFolder()
        {
            if (String.IsNullOrEmpty(Distro)) return;
            int generation = ++homeGeneration; string selected = Distro;
            SetWorking(true); Message(L10n.T("ui.loading_the_home_directory"), false);
            try
            {
                Reply info = await new WslClient(backendPath, LinuxUser).Run(selected, "info", null);
                if (closed || generation != homeGeneration || selected != Distro) return;
                folder.Text = info.Home; Message(L10n.T("ui.selected_this_distribution_s_home_directory"), false);
            }
            catch (Exception ex) { if (!closed && generation == homeGeneration) Message(ex.Message, true); }
            finally { if (!closed && generation == homeGeneration) SetWorking(false); }
        }
        private void UpdateTitleHint()
        {
            string name = folder.Text.Trim().TrimEnd('/', '\\');
            int slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            name = slash >= 0 ? name.Substring(slash + 1) : name;
            titleHint.Text = String.IsNullOrWhiteSpace(title.Text)
                ? L10n.T("ui.leave_the_name_blank_to_use") + (String.IsNullOrEmpty(name) ? L10n.T("ui.folder_name") : name)
                : L10n.T("ui.display_name") + title.Text.Trim();
        }
        private static void AddField(Grid fields, string label, FrameworkElement input, int row)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 7, 14, 7) };
            Grid.SetRow(text, row); fields.Children.Add(text);
            input.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(input, row); Grid.SetColumn(input, 1); fields.Children.Add(input);
        }
    }

    internal static class Dialogs
    {
        internal static void PlaceDialog(Window dialog, Window owner)
        {
            var box = owner as BoxForm;
            // Windows can raise the owner when a modal child is shown.
            if (owner != null && (box == null || !box.IsBottomLayer))
                dialog.Owner = owner;
            else
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        public static string FileName(Window owner, string heading, string initial, bool folder)
        {
            var dialog = new Window { Title = heading, Width = 440, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Icon = Branding.Image,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            PlaceDialog(dialog, owner);
            Theme.ApplyPanelTheme(dialog);
            var layout = new StackPanel { Margin = new Thickness(20) };
            layout.Children.Add(new TextBlock { Text = folder ? L10n.T("ui.folder_name") : L10n.T("ui.new_name_keep_the_extension_you_need"), Margin = new Thickness(0, 0, 0, 8) });
            var input = new TextBox { Text = initial, Padding = new Thickness(7) }; layout.Children.Add(input);
            var error = new TextBlock { Foreground = Theme.PanelError, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; layout.Children.Add(error);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = L10n.T("ui.ok"), IsDefault = true, MinWidth = 80, Padding = new Thickness(12, 7, 12, 7) };
            var cancel = new Button { Content = L10n.T("ui.cancel"), IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (sender, e) => {
                string name = input.Text;
                if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\0', '\r', '\n' }) >= 0)
                { error.Text = L10n.T("ui.a_name_is_required_it_cannot_contain_or_line_breaks"); return; }
                dialog.DialogResult = true;
            };
            actions.Children.Add(ok); actions.Children.Add(cancel); layout.Children.Add(actions); dialog.Content = layout;
            dialog.Loaded += (sender, e) => {
                input.Focus(); int dot = folder ? -1 : initial.LastIndexOf('.');
                input.Select(0, dot > 0 ? dot : initial.Length);
            };
            return dialog.ShowDialog() == true ? input.Text : null;
        }
        internal static Window CreateAbout(Window owner)
        {
            var window = new Window { Title = L10n.T("ui.about"), Icon = Branding.Image, Width = 510, MinWidth = 360,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            Theme.ApplyPanelTheme(window);
            PlaceDialog(window, owner);
            var layout = new StackPanel { Margin = new Thickness(24) };
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            heading.Children.Add(new Image { Source = Branding.Image, Width = 68, Height = 68, Margin = new Thickness(0, 0, 16, 0) });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = "WSLBox", FontSize = 27, FontWeight = FontWeights.SemiBold });
            names.Children.Add(new TextBlock { Text = Branding.Version, Foreground = Theme.PanelMuted, Margin = new Thickness(0, 4, 0, 0) });
            heading.Children.Add(names); layout.Children.Add(heading);
            layout.Children.Add(new TextBlock { Text = L10n.T("ui.browse_wsl_folders_on_the_windows_desktop_transfer_files_across_"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 23, Margin = new Thickness(0, 0, 0, 14) });
            layout.Children.Add(new TextBlock { Text = L10n.T("ui.requires_windows_and_wsl2_wslg_license_gnu_gpl_v3"), LineHeight = 23 });
            layout.Children.Add(new TextBlock { Text = L10n.T("ui.configuration_folder"), Foreground = Theme.PanelMuted, Margin = new Thickness(0, 16, 0, 5) });
            layout.Children.Add(new TextBox { Text = System.IO.Path.GetDirectoryName(Settings.FilePath), IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Padding = new Thickness(0), FontSize = 11 });
            var project = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(UpdateService.ProjectUrl)) {
                NavigateUri = new Uri(UpdateService.ProjectUrl) };
            var projectLine = new TextBlock { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
            projectLine.Inlines.Add(L10n.T("about.project") + " "); projectLine.Inlines.Add(project);
            var updateStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.PanelMuted,
                Margin = new Thickness(0, 9, 0, 0) };
            Action<string> openLink = url => {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { updateStatus.Text = L10n.T("about.link_failed") + " " + ex.Message; }
            };
            project.RequestNavigate += (s, e) => { openLink(e.Uri.AbsoluteUri); e.Handled = true; };
            layout.Children.Add(projectLine);
            var updateActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
            var check = new Button { Content = L10n.T("about.check_updates"), Padding = new Thickness(12, 7, 12, 7) };
            var download = new Button { Content = L10n.T("about.download_update"), Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
            updateActions.Children.Add(check); updateActions.Children.Add(download);
            layout.Children.Add(updateActions); layout.Children.Add(updateStatus);
            var cancellation = new System.Threading.CancellationTokenSource();
            bool closed = false;
            window.Closed += (s, e) => { closed = true; cancellation.Cancel(); cancellation.Dispose(); };
            download.Click += (s, e) => openLink(UpdateService.ReleasesUrl);
            check.Click += async (s, e) => {
                check.IsEnabled = false; download.Visibility = Visibility.Collapsed;
                updateStatus.Text = L10n.T("about.checking");
                try
                {
                    Version latest = await UpdateService.Check(cancellation.Token);
                    if (closed) return;
                    bool newer = latest > new Version(Branding.Version);
                    updateStatus.Text = newer ? L10n.F("about.new_version", latest) : L10n.F("about.up_to_date", Branding.Version);
                    download.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
                }
                catch (Exception ex) { if (!closed) { updateStatus.Text = L10n.F("about.check_failed", ex.GetBaseException().Message); download.Visibility = Visibility.Visible; } }
                finally { if (!closed) check.IsEnabled = true; }
            };
            window.PreviewKeyDown += (sender, e) => { if (e.Key == System.Windows.Input.Key.Escape) { window.Close(); e.Handled = true; } };
            window.Content = layout; return window;
        }
        public static void About(Window owner) { CreateAbout(owner).ShowDialog(); }
        internal static Window CreateHelp(Window owner)
        {
            var window = new Window { Title = L10n.T("ui.wslbox_help"), Width = 690,
                Height = Math.Min(760, Math.Max(380, SystemParameters.WorkArea.Height * 0.88)),
                Icon = Branding.Image, MinWidth = 520, MinHeight = 380, ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen };
            Theme.ApplyPanelTheme(window);
            var content = new StackPanel { Margin = new Thickness(24) };
            content.Children.Add(new TextBlock { Text = L10n.T("ui.help_and_shortcuts"), FontSize = 23, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = L10n.T("help.intro"), TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.PanelMuted, Margin = new Thickness(0, 8, 0, 2), LineHeight = 21 });
            HelpTable(content, "help.windows", new[,] {
                { "help.desktop", "help.desktop_gesture" }, { "help.move", "help.move_gesture" },
                { "help.maximize", "help.maximize_gesture" }, { "help.resize", "help.resize_gesture" },
                { "help.collapse", "help.collapse_gesture" }, { "help.lock", "help.lock_gesture" },
                { "help.restore", "Ctrl+Shift+R" }, { "help.new_box", "Ctrl+N / +" } });
            HelpTable(content, "help.files", new[,] {
                { "help.open", "help.open_gesture" }, { "help.trash", "Delete" },
                { "help.permanent", "Shift+Delete" }, { "help.rename", "F2" },
                { "help.new_folder", "Ctrl+Shift+N" }, { "help.select_all", "Ctrl+A" },
                { "help.copy", "Ctrl+C" }, { "help.cut", "Ctrl+X" }, { "help.paste", "Ctrl+V" },
                { "help.clear", "help.clear_gesture" }, { "help.menu", "Shift+F10 / Menu" } });
            HelpTable(content, "help.navigation", new[,] {
                { "help.back", "help.back_gesture" }, { "help.forward", "help.forward_gesture" },
                { "help.parent", "Backspace / Alt+↑" }, { "help.home", "Alt+Home" },
                { "help.refresh", "F5 / Ctrl+R" }, { "help.icons", "Ctrl+1" }, { "help.list", "Ctrl+2" }, { "ui.show_hidden", "Ctrl+H" },
                { "help.folder_settings", "Ctrl+L" }, { "help.explorer", "help.explorer_gesture" } });
            HelpTable(content, "help.transfer", new[,] {
                { "help.drop_copy", "help.drop_gesture" }, { "help.drop_move", "help.drop_move_gesture" },
                { "help.drop_force_copy", "help.drop_copy_gesture" }, { "help.drop_subfolder", "help.drop_subfolder_gesture" } });
            content.Children.Add(new TextBlock { Text = L10n.T("help.notes_title"), FontSize = 17,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) });
            content.Children.Add(new TextBlock { Text = L10n.T("help.notes"), TextWrapping = TextWrapping.Wrap, LineHeight = 23 });
            window.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
            window.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { window.Close(); e.Handled = true; } };
            return window;
        }
        private static void HelpTable(Panel parent, string title, string[,] rows)
        {
            parent.Children.Add(new TextBlock { Text = L10n.T(title), FontSize = 17, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 22, 0, 9) });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            HelpRow(grid, 0, L10n.T("help.action"), L10n.T("help.shortcut"), true);
            for (int i = 0; i < rows.GetLength(0); i++)
                HelpRow(grid, i + 1, L10n.T(rows[i, 0]), rows[i, 1].StartsWith("help.") ? L10n.T(rows[i, 1]) : rows[i, 1], false);
            parent.Children.Add(grid);
        }
        private static void HelpRow(Grid grid, int row, string action, string shortcut, bool heading)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int column = 0; column < 2; column++)
            {
                var text = new TextBlock { Text = column == 0 ? action : shortcut, TextWrapping = TextWrapping.Wrap,
                    FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal, LineHeight = 21 };
                var cell = new Border { Padding = new Thickness(10, 7, 10, 7), Child = text,
                    BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(Border.BorderBrushProperty, SystemColors.ControlDarkBrushKey);
                if (heading || row % 2 == 0) cell.SetResourceReference(Border.BackgroundProperty, SystemColors.ControlBrushKey);
                Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
            }
        }
        private static Window helpWindow;
        public static void Help(Window owner)
        {
            if (helpWindow == null)
            {
                helpWindow = CreateHelp(owner);
                helpWindow.Closed += (sender, e) => helpWindow = null;
                helpWindow.Show();
            }
            else if (helpWindow.WindowState == WindowState.Minimized)
                helpWindow.WindowState = WindowState.Normal;
            helpWindow.Activate();
        }
    }
}
