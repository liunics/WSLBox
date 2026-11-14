using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WSLBox
{
    [DataContract] public sealed class LinuxApplication
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Command;
        [DataMember] public string IconKey;
        [DataMember] public bool Recommended;
        [DataMember] public bool IsDefault;
        public string Label { get { return Name + (IsDefault ? L10n.T("ui.current_default") : ""); } }
        public ImageSource Image { get; set; }
        public string Description { get { return Command; } }
    }
    internal sealed class OpenWithDialog : Window
    {
        private readonly ListBox applications = new ListBox { DisplayMemberPath = "Label" };
        private readonly TextBox search = new TextBox { Padding = new Thickness(7), Margin = new Thickness(0, 8, 0, 8) };
        private readonly CheckBox others = new CheckBox { Content = L10n.T("ui.show_other_linux_applications"), Margin = new Thickness(0, 8, 0, 8) };
        private readonly CheckBox always = new CheckBox { Content = L10n.T("ui.use_as_the_linux_default_for_this_file_type"), Margin = new Thickness(0, 12, 0, 12) };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.PanelMuted };
        private readonly Button ok = new Button { Content = L10n.T("ui.open"), IsDefault = true, IsEnabled = false, MinWidth = 80, Padding = new Thickness(12, 7, 12, 7) };
        private bool closed;
        private LinuxApplication[] apps = new LinuxApplication[0];
        public string ApplicationId { get { var app = applications.SelectedItem as LinuxApplication; return app == null ? null : app.Id; } }
        public bool SetDefault { get { return always.IsChecked == true; } }
        public OpenWithDialog(Window owner, WslClient client, string distro, string path)
        {
            Title = L10n.T("ui.linux_open_with"); Icon = Branding.Image;
            Width = 540; Height = 540; MinWidth = 420; MinHeight = 380;
            ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Dialogs.PlaceDialog(this, owner);
            SourceInitialized += (s, e) => WindowEffects.DisableMinimize(this);
            Theme.ApplyPanelTheme(this);
            var layout = new DockPanel { Margin = new Thickness(20) };
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = L10n.T("ui.choose_a_linux_app_for") + System.IO.Path.GetFileName(path), TextWrapping = TextWrapping.Wrap, FontSize = 15 });
            heading.Children.Add(new TextBlock { Text = L10n.T("ui.search_application_names_or_commands"), Foreground = Theme.PanelMuted, Margin = new Thickness(0, 8, 0, 0) });
            search.ToolTip = L10n.T("ui.search_application_names_or_commands"); heading.Children.Add(search); heading.Children.Add(others);
            DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
            var footer = new StackPanel(); footer.Children.Add(status); footer.Children.Add(always);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = L10n.T("ui.cancel"), IsCancel = true, MinWidth = 80, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
            actions.Children.Add(ok); actions.Children.Add(cancel); footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
            applications.DisplayMemberPath = "";
            applications.ItemTemplate = (DataTemplate)XamlReader.Parse(@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><DockPanel Margin='6' ToolTip='{Binding Description}'><Image Source='{Binding Image}' Width='32' Height='32' Margin='0,0,12,0' DockPanel.Dock='Left'/><TextBlock Text='{Binding Label}' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/></DockPanel></DataTemplate>");
            applications.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            ScrollViewer.SetHorizontalScrollBarVisibility(applications, ScrollBarVisibility.Disabled);
            layout.Children.Add(applications); Content = layout;
            search.TextChanged += (s,e) => Filter();
            others.Checked += (s,e) => Filter(); others.Unchecked += (s,e) => Filter();
            applications.SelectionChanged += (s,e) => ok.IsEnabled = applications.SelectedItem != null;
            ok.Click += (s,e) => { if (ApplicationId != null) DialogResult = true; };
            applications.MouseDoubleClick += (s,e) => {
                if (ApplicationId != null && FindItem(e.OriginalSource as DependencyObject) != null) DialogResult = true;
            };
            Closed += (s,e) => closed = true;
            Loaded += async (s,e) => {
                status.Text = L10n.T("ui.loading_linux_applications");
                try
                {
                    Reply reply = await client.Run(distro, "applications", path);
                    if (closed) return;
                    var images = new Dictionary<string, ImageSource>();
                    foreach (LinuxIcon icon in reply.Icons ?? new LinuxIcon[0])
                    {
                        try
                        {
                            using (var data = new MemoryStream(Convert.FromBase64String(icon.Png)))
                            {
                                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                                image.StreamSource = data; image.EndInit(); image.Freeze(); images[icon.Key] = image;
                            }
                        }
                        catch (Exception) { }
                    }
                    apps = reply.Applications ?? new LinuxApplication[0];
                    foreach (LinuxApplication app in apps) app.Image = app.IconKey != null && images.ContainsKey(app.IconKey) ? images[app.IconKey] : Branding.Image;
                    if (!apps.Any(app => app.Recommended)) others.IsChecked = true;
                    status.Text = L10n.T("ui.file_type") + reply.ContentType;
                    Filter(); search.Focus();
                }
                catch (Exception ex) { if (!closed) { status.Text = ex.Message; status.Foreground = Theme.PanelError; } }
            };
        }
        private static ListBoxItem FindItem(DependencyObject child)
        {
            while (child != null)
            {
                var item = child as ListBoxItem; if (item != null) return item;
                child = child is Visual ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
            }
            return null;
        }
        private void Filter()
        {
            string term = search.Text.Trim(); string selected = ApplicationId;
            var visible = apps.Where(app => (others.IsChecked == true || app.Recommended)
                && ((app.Name ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || (app.Command ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            applications.ItemsSource = visible;
            applications.SelectedItem = visible.FirstOrDefault(app => app.Id == selected)
                ?? visible.FirstOrDefault(app => app.IsDefault) ?? visible.FirstOrDefault();
            ok.IsEnabled = applications.SelectedItem != null;
        }
    }
}
