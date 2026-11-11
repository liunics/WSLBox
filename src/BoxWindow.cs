using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WSLBox
{
    internal sealed class FileModel : INotifyPropertyChanged
    {
        public Entry Entry { get; set; }
        public string Label { get { return String.IsNullOrEmpty(Entry.DisplayName) ? Entry.Name : Entry.DisplayName; } }
        public string Path { get { return Entry.Path; } }
        private ImageSource icon;
        public bool ThumbnailPending { get; set; }
        private bool dropTarget;
        public bool IsDropTarget
        {
            get { return dropTarget; }
            set { if (dropTarget != value) { dropTarget = value; Changed("IsDropTarget"); } }
        }
        public ImageSource Icon { get { return icon; } set { icon = value; Changed("Icon"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name)); }
        public void Update(Entry entry, ImageSource image)
        {
            string label = Entry == null ? null : Label, path = Entry == null ? null : Path;
            if (Entry == null || Entry.Path != entry.Path || Entry.Modified != entry.Modified || Entry.Size != entry.Size
                || Entry.IconKey != entry.IconKey || Entry.CanThumbnail != entry.CanThumbnail) ThumbnailPending = entry.CanThumbnail;
            Entry = entry;
            if (!Object.ReferenceEquals(Icon, image)) Icon = image;
            if (label != Label) Changed("Label");
            if (path != Path) Changed("Path");
        }
    }

    internal sealed class BoxForm : Window
    {
        private readonly Settings settings;
        private readonly BoxWorkspace workspace;
        private readonly WslClient wsl;
        private readonly FolderHistory history = new FolderHistory();
        private readonly Border plate = new Border();
        private readonly Grid body = new Grid();
        private readonly TextBlock title = new TextBlock();
        private readonly TextBlock status = new TextBlock();
        private readonly ListBox list = new ListBox();
        private readonly WrapPanel filters = new WrapPanel();
        private readonly ResizeGrip grip = new ResizeGrip();
        private readonly Button viewButton;
        private readonly Button lockButton;
        private readonly Button collapseButton;
        private readonly DispatcherTimer refreshTimer = new DispatcherTimer();
        private readonly DispatcherTimer noticeTimer = new DispatcherTimer();
        private readonly DispatcherTimer hoverTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
        private bool hoverExpanded;
        private bool Collapsed
        {
            get { return settings.Collapsed && !hoverExpanded; }
            set { hoverTimer.Stop(); hoverExpanded = false; settings.Collapsed = value; }
        }
        private readonly ContextMenu menu = new ContextMenu();
        private readonly ContextMenu fileMenu = new ContextMenu();
        private bool IsContextMenuOpen { get { return contextOpen || fileMenu.IsOpen; } }
        private List<FileModel> models = new List<FileModel>();
        private bool busy, listing, initialized, opening, dragging, closing, applyingBounds, contextOpen;
        private Entry[] listedEntries;
        private string lastError = "";
        private readonly Dictionary<string, ImageSource> decodedImages = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        private Point dragStart;
        private FileModel dragOrigin;
        private FileModel highlightedDropFolder;
        private bool showingDropPreview, dropInProgress;
        private static int clipboardWriteVersion;
        private const int ClipboardCannotOpen = unchecked((int)0x800401D0);
        private Rect normalBounds;
        private Rect sizingStart = Rect.Empty;
        private bool captionPending, captionMoving, restoringCaption;
        private Point captionDownScreen, captionDownLocal, captionOrigin, captionPointer;
        private double captionRatio, captionOffset;
        private PointerSnap captionSnap;
        private Rect[] captionPeers = new Rect[0];
        private Matrix captionTransform;
        private Point captionDevice;
        private string currentFolder;
        private SnapPreview snapPreview;
        private InlineRename renameEditor;
        private int thumbnailGeneration, thumbnailRunning = -1;
        private string ListCommand { get { return settings.ShowThumbnails != false ? "list" : "list-icons"; } }
        public Settings Configuration { get { return settings; } }
        public string CurrentFolder { get { return currentFolder; } }
        internal bool IsClosing { get { return closing; } }
        public bool EditingName { get { return renameEditor != null; } }
        public bool CanClose { get { return !closing && !busy && !opening && !dragging && !IsContextMenuOpen && !captionMoving && sizingStart.IsEmpty; } }

        public BoxForm(Settings settings, BoxWorkspace workspace = null)
        {
            this.settings = settings; this.workspace = workspace; settings.Normalize();
            currentFolder = settings.Folder;
            wsl = new WslClient(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend.py"));
            Title = "WSLBox"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Icon = Branding.Image;
            Background = Brushes.Transparent; Foreground = Theme.Text;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
            ShowInTaskbar = false; ResizeMode = ResizeMode.CanResizeWithGrip;
            Resources = Theme.Resources(); MinWidth = 280;
            Width = settings.Bounds.Width; Height = settings.Bounds.Height; Left = settings.Bounds.Left; Top = settings.Bounds.Top;
            // Clamp to the desktop in WPF device-independent coordinates.
            double left = SystemParameters.VirtualScreenLeft, top = SystemParameters.VirtualScreenTop;
            if (Left + Width < left + 30 || Left > left + SystemParameters.VirtualScreenWidth - 30
                || Top + 36 < top || Top > top + SystemParameters.VirtualScreenHeight - 30)
            { Left = left + 40; Top = top + 40; }
            normalBounds = new Rect(Left, Top, Width, Height);
            StoreNormalBounds();

            var outer = new Grid();
            plate.CornerRadius = new CornerRadius(10);
            plate.BorderThickness = new Thickness(1);
            plate.BorderBrush = new SolidColorBrush(Color.FromArgb(65, 200, 214, 233));
            plate.Padding = new Thickness(7, 3, 7, 5);
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            plate.Child = layout; outer.Children.Add(plate); Content = outer;

            var header = new Grid { Height = 34, Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            title.VerticalAlignment = VerticalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.Margin = new Thickness(4, 0, 8, 0); header.Children.Add(title);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(actions, 1); header.Children.Add(actions);
            var copyLinuxPath = HeaderButton(actions, "\uE8C8", L10n.T("ui.copy_current_folder_s_linux_path"), async () => {
                string path = currentFolder;
                if (!String.IsNullOrEmpty(path))
                    await WriteClipboard(() => Clipboard.SetText(path), L10n.T("ui.linux_path_copied"));
            });
            copyLinuxPath.FontFamily = new FontFamily("Segoe MDL2 Assets"); copyLinuxPath.FontSize = 16;
            viewButton = HeaderButton(actions, "", L10n.T("ui.switch_icons_list"), () => { SetView(settings.ViewMode == "icons" ? "list" : "icons"); return Done(); });
            lockButton = HeaderButton(actions, "◇", L10n.T("ui.lock_position_and_size"), () => { settings.Locked = !settings.Locked; ApplyAppearance(); Persist(); return Done(); });
            HeaderButton(actions, "+", L10n.T("ui.new_box"), () => { NewBox(); return Done(); });
            Button more = null;
            more = HeaderButton(actions, "☰", L10n.T("ui.menu_and_help"), () => { ShowBoxMenu(more); return Done(); });
            menu.PlacementTarget = more; menu.Placement = PlacementMode.Bottom;
            collapseButton = HeaderButton(actions, "⌃", L10n.T("ui.collapse_expand"), () => { ToggleCollapsed(); return Done(); });
            header.MouseLeftButtonDown += (s, e) => {
                if (Ancestor<Button>(e.OriginalSource as DependencyObject) != null) return;
                if (e.ClickCount == 2)
                {
                    captionPending = false; header.ReleaseMouseCapture(); ToggleMaximized();
                    e.Handled = true; return;
                }
                if (!settings.Locked && e.LeftButton == MouseButtonState.Pressed)
                {
                    captionPending = true;
                    captionSnap = PointerSnap.None; captionPeers = null;
                    captionDownLocal = e.GetPosition(this);
                    captionDownScreen = ToDip(PointToScreen(captionDownLocal));
                    captionOrigin = new Point(Left, Top);
                    captionRatio = captionDownLocal.X / Math.Max(1, ActualWidth > 0 ? ActualWidth : Width);
                    captionOffset = captionDownLocal.Y;
                    header.CaptureMouse(); e.Handled = true;
                }
            };
            header.MouseMove += (s, e) => {
                if (!captionPending || e.LeftButton != MouseButtonState.Pressed) return;
                Point device = PointToScreen(e.GetPosition(this)), pointer = ToDip(device);
                if (!captionMoving)
                {
                    Vector distance = pointer - captionDownScreen;
                    if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance
                        && Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    captionMoving = true;
                    if (WindowState == WindowState.Maximized || settings.Maximized || settings.EdgeLayout != "" || IsShellSized())
                    {
                        restoringCaption = true;
                        try { RestoreForDrag(pointer, WorkAreaAt(device)); }
                        finally { restoringCaption = false; }
                        if (!header.IsMouseCaptured) header.CaptureMouse();
                        captionOrigin = new Point(Left, Top); captionPointer = pointer;
                    }
                    else captionPointer = captionDownScreen;
                }
                Point origin = captionOrigin + (pointer - captionPointer);
                if (settings.SnapToEdges != false)
                {
                    var source = PresentationSource.FromVisual(this) as HwndSource;
                    Matrix transform = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
                    if (captionPeers == null || transform != captionTransform)
                    {
                        captionTransform = transform;
                        captionPeers = workspace == null ? new Rect[0] : workspace.SnapPeers(this);
                    }
                    origin = WindowGeometry.Magnet(origin, BoundsIn(this).Size, WorkAreaAt(device), captionPeers);
                }
                bool previous = applyingBounds; applyingBounds = true;
                try { Left = origin.X; Top = origin.Y; }
                finally { applyingBounds = previous; }
                captionDevice = device;
                PointerSnap target = WindowGeometry.DetectPointerSnap(pointer, ScreenAreaAt(device));
                if (target != captionSnap)
                {
                    captionSnap = target;
                }
                ShowSnapPreview(target, WorkAreaAt(device));
                e.Handled = true;
            };
            header.MouseLeftButtonUp += (s, e) => {
                bool moved = captionMoving;
                PointerSnap target = captionSnap;
                captionPending = captionMoving = false;
                captionSnap = PointerSnap.None;
                ClearSnapPreview();
                header.ReleaseMouseCapture();
                if (moved)
                {
                    if (target == PointerSnap.None) Persist();
                    else ApplyPointerSnap(target, WorkAreaAt(captionDevice));
                    if (!busy && !opening) status.Visibility = Visibility.Collapsed;
                }
            };
            header.LostMouseCapture += (s, e) => {
                if (restoringCaption) return;
                bool moved = captionMoving; captionPending = captionMoving = false;
                captionSnap = PointerSnap.None;
                ClearSnapPreview();
                if (moved) Persist();
            };
            layout.Children.Add(header);

            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition());
            filters.Margin = new Thickness(0, 3, 0, 7);
            body.Children.Add(filters);
            foreach (string[] filter in new[] {
                new[] { "all", L10n.T("ui.all") }, new[] { "folders", L10n.T("ui.folders") }, new[] { "documents", L10n.T("ui.documents") },
                new[] { "images", L10n.T("ui.images") }, new[] { "archives", L10n.T("ui.archives") }, new[] { "other", L10n.T("ui.other") } })
            {
                string key = filter[0];
                var button = new Button { Content = filter[1], Tag = key, Style = (Style)Resources["HeaderButton"],
                    Width = Double.NaN, Height = 28, FontSize = 12, MinWidth = 38, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(1, 0, 1, 0) };
                button.Click += (s, e) => { settings.Filter = key; ApplyFilter(); Persist(); };
                filters.Children.Add(button);
            }
            list.Background = Brushes.Transparent; list.BorderThickness = new Thickness(0); list.Padding = new Thickness(0);
            list.SelectionMode = SelectionMode.Extended; list.Foreground = Theme.Text;
            list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((sender, e) => {
                // Recycling must never move an active name editor onto a
                // different file's label when its old row scrolls out of view.
                if (renameEditor != null && (e.VerticalChange != 0 || e.HorizontalChange != 0)) renameEditor.Cancel();
            }));
            list.ItemContainerStyle = (Style)Resources["FileItem"];
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            list.Style = (Style)Resources["FileList"];
            list.MouseDoubleClick += async (s, e) => {
                if (busy || renameEditor != null) { e.Handled = true; return; }
                var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (item != null)
                {
                    var model = item.DataContext as FileModel;
                    if (model != null) await ActivateItem(model);
                }
                else if (Ancestor<ScrollBar>(e.OriginalSource as DependencyObject) == null) Reveal();
                e.Handled = true;
            };
            list.PreviewMouseLeftButtonDown += (s, e) => {
                dragStart = e.GetPosition(list);
                ClearSelectionOnBlank(e.OriginalSource as DependencyObject);
                var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                dragOrigin = item == null ? null : item.DataContext as FileModel;
            };
            list.MouseLeftButtonUp += (s, e) => ClearSelectionOnBlank(e.OriginalSource as DependencyObject);
            list.PreviewMouseMove += async (s, e) => {
                if (busy || dragging || dragOrigin == null || e.LeftButton != MouseButtonState.Pressed) return;
                Point current = e.GetPosition(list);
                if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                try
                {
                    if (!list.SelectedItems.Contains(dragOrigin)) list.SelectedItem = dragOrigin;
                    string[] paths = SelectedPaths();
                    if (paths.Length == 0) return;
                    dragging = true;
                    DragDrop.DoDragDrop(list, FileData(paths, false), DragDropEffects.Copy | DragDropEffects.Move);
                }
                catch (Exception ex) { Report(ex); }
                finally { dragging = false; dragOrigin = null; if (!busy) ClearDropPreview(); }
                await RefreshFolder(false);
            };
            list.AllowDrop = true;
            DragEventHandler previewDrop = (s, e) =>
            {
                string[] paths;
                string target;
                FileModel folder;
                e.Effects = ResolveDrop(e, out paths, out target, out folder);
                if (e.Effects == DragDropEffects.None) ClearDropPreview();
                else
                {
                    HighlightDropFolder(folder);
                    string message = (e.Effects == DragDropEffects.Move ? L10n.T("ui.move_to") : L10n.T("ui.copy_to")) + target;
                    if (!showingDropPreview || status.Text != message) Notify(message, false);
                    showingDropPreview = true;
                }
                e.Handled = true;
            };
            list.PreviewDragEnter += previewDrop;
            list.PreviewDragOver += previewDrop;
            list.PreviewDrop += async (s, e) =>
            {
                string[] paths;
                string target;
                FileModel folder;
                DragDropEffects effect = ResolveDrop(e, out paths, out target, out folder);
                e.Effects = effect;
                e.Handled = true;
                if (effect == DragDropEffects.None) { ClearDropPreview(); return; }
                showingDropPreview = false;
                HighlightDropFolder(folder);
                dropInProgress = true;
                try { await Transfer(paths, target, effect == DragDropEffects.Move); }
                finally
                {
                    dropInProgress = false;
                    HighlightDropFolder(null);
                }
            };
            list.DragLeave += (s, e) => { if (!busy) ClearDropPreview(); };
            Grid.SetRow(list, 1); body.Children.Add(list);
            Grid.SetRow(body, 1); layout.Children.Add(body);
            status.Foreground = Theme.Muted; status.FontSize = 11; status.TextWrapping = TextWrapping.NoWrap;
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.Margin = new Thickness(0); status.Visibility = Visibility.Collapsed;
            status.Padding = new Thickness(3, 2, 3, 2);
            status.MaxHeight = 24; status.ClipToBounds = true;
            // Canvas contributes zero desired size: messages float over the files
            // without reserving a footer or changing the viewport.
            status.Background = Brushes.Transparent;
            status.Foreground = Theme.Text;
            status.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.7 };
            status.HorizontalAlignment = HorizontalAlignment.Left;
            var noticeLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            noticeLayer.SizeChanged += (sender, e) => status.MaxWidth = Math.Max(0, noticeLayer.ActualWidth - 24);
            Canvas.SetLeft(status, 4); Canvas.SetBottom(status, 1); noticeLayer.Children.Add(status);
            Grid.SetRow(noticeLayer, 1); Panel.SetZIndex(noticeLayer, 10); layout.Children.Add(noticeLayer);
            grip.Width = grip.Height = 15; grip.HorizontalAlignment = HorizontalAlignment.Right;
            grip.VerticalAlignment = VerticalAlignment.Bottom; grip.Opacity = 0.4; outer.Children.Add(grip);
            BuildMenus();

            PreviewMouseDown += async (s, e) => {
                if (e.ChangedButton == MouseButton.XButton1 || e.ChangedButton == MouseButton.XButton2)
                { e.Handled = true; await HistoryNavigate(e.ChangedButton == MouseButton.XButton2); }
            };
            PreviewKeyDown += OnKey;
            Loaded += async (s, e) => await Initialize();
            MouseEnter += (s, e) => HoverEnter();
            MouseLeave += (s, e) => { hoverTimer.Stop(); if (hoverExpanded) ScheduleHover(120); };
            hoverTimer.Tick += (s, e) => HoverTick();
            IsVisibleChanged += async (s, e) => {
                ++thumbnailGeneration;
                if (!IsVisible) { hoverTimer.Stop(); SetHoverExpanded(false); ClearDropPreview(); }
                if (IsVisible && initialized && !closing)
                {
                    LoadThumbnails(thumbnailGeneration, settings.Distro, currentFolder);
                    await RefreshFolder(false);
                }
            };
            SourceInitialized += (s, e) =>
            {
                var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null) source.AddHook(ResizeWindowMessage);
            };
            Closing += OnClosing;
            Closed += (s, e) => {
                // Normal close, native close, and application shutdown share cleanup.
                PrepareForClose();
            };
            LocationChanged += (s, e) => RememberBounds();
            SizeChanged += (s, e) => RememberBounds();
            StateChanged += OnWindowStateChanged;
            refreshTimer.Interval = TimeSpan.FromSeconds(5);
            refreshTimer.Tick += async (s, e) => {
                if (!busy && !opening && !listing && !dragging && !captionPending && !IsContextMenuOpen && !Collapsed && WindowState != WindowState.Minimized)
                    await RefreshFolder(false);
            };
            noticeTimer.Interval = TimeSpan.FromSeconds(4);
            noticeTimer.Tick += (s, e) => { if (!busy && !opening) { status.Visibility = Visibility.Collapsed; noticeTimer.Stop(); } };
            SetView(settings.ViewMode, false); ApplyWindowLayout(null); UpdateTitle();
        }

        private IntPtr ResizeWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int hitTest = 0x0084, enterSizeMove = 0x0231, exitSizeMove = 0x0232;
            if (message == enterSizeMove)
            {
                WindowEffects.TryGetBounds(this, out sizingStart);
            }
            else if (message == exitSizeMove)
            {
                Rect original = sizingStart;
                sizingStart = Rect.Empty;
                Rect current;
                if (!original.IsEmpty && WindowEffects.TryGetBounds(this, out current)
                    && current.Size != original.Size && WindowState == WindowState.Normal)
                {
                    settings.EdgeLayout = "";
                    Persist();
                }
            }
            else if (message == hitTest && !closing && ResizeMode != ResizeMode.NoResize
                && WindowState == WindowState.Normal && !settings.Maximized)
            {
                Rect bounds;
                if (!WindowEffects.TryGetBounds(this, out bounds)) return IntPtr.Zero;
                long packed = lParam.ToInt64();
                var point = new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
                var dpi = VisualTreeHelper.GetDpi(this);
                int edge = WindowEffects.ResizeHit(bounds, point, dpi);
                if (edge != 0)
                {
                    handled = true;
                    return new IntPtr(edge);
                }
            }
            return IntPtr.Zero;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized) { ++thumbnailGeneration; return; }
            if (applyingBounds || settings.Collapsed || closing) return;
            if (initialized) LoadThumbnails(thumbnailGeneration, settings.Distro, currentFolder);
            if (WindowState == WindowState.Maximized)
            {
                // Resuming a minimized maximized window must not overwrite its
                // pre-maximize layout with an empty current edge layout.
                if (!settings.Maximized)
                {
                    settings.MaximizeReturnEdge = settings.EdgeLayout;
                    settings.EdgeLayout = "";
                }
                settings.Maximized = true;
            }
            else if (WindowState == WindowState.Normal)
            {
                settings.Maximized = false;
                string returnEdge = settings.MaximizeReturnEdge;
                settings.MaximizeReturnEdge = "";
                if (returnEdge != "") { settings.EdgeLayout = returnEdge; ApplyWindowLayout(null); }
            }
        }

        private static Task Done() { return Task.FromResult(0); }
        private void ShowSnapPreview(PointerSnap target, Rect work)
        {
            if (closing) return;
            if (target == PointerSnap.None) { ClearSnapPreview(); return; }
            if (snapPreview == null) snapPreview = new SnapPreview(this);
            snapPreview.ShowTarget(target == PointerSnap.Maximize ? work : WindowGeometry.Half(work, target == PointerSnap.Right));
        }
        private void ClearSnapPreview() { if (snapPreview != null) snapPreview.Clear(); }
        private Button HeaderButton(Panel parent, string glyph, string tooltip, Func<Task> action)
        {
            var button = new Button { Content = glyph, ToolTip = tooltip, Style = (Style)Resources["HeaderButton"] };
            button.Click += async (s, e) => { try { await action(); } catch (Exception ex) { Report(ex); } };
            parent.Children.Add(button); return button;
        }
        private static T Ancestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null)
            {
                T match = node as T; if (match != null) return match;
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D) node = VisualTreeHelper.GetParent(node);
                else node = LogicalTreeHelper.GetParent(node);
            }
            return null;
        }
        private void ClearSelectionOnBlank(DependencyObject source)
        {
            if (Ancestor<ListBoxItem>(source) == null && Ancestor<ScrollBar>(source) == null)
            { list.SelectedItems.Clear(); dragOrigin = null; }
        }
        private void BuildMenus()
        {
            AddMenu(menu, L10n.T("ui.folder_settings_ctrl_l_2db86b"), async () => await Configure());
            AddMenu(menu, L10n.T("ui.bound_folder_alt_home_9134c1"), async () => await Home());
            AddMenu(menu, L10n.T("ui.new_folder_ctrl_shift_n_1157ef"), async () => await NewFolder());
            AddMenu(menu, L10n.T("ui.open_linux_trash"), async () => await OpenTrash());
            menu.Items.Add(new Separator());
            var views = new MenuItem { Header = L10n.T("ui.view") };
            var sorts = new MenuItem { Header = L10n.T("ui.sort_by") };
            var sizes = new MenuItem { Header = L10n.T("ui.icon_size") };
            views.Items.Add(sizes);
            var choices = new List<MenuItem>();
            foreach (var choice in new[] {
                Tuple.Create(ShellMenuAction.LargeIcons, L10n.T("ui.large_icons")), Tuple.Create(ShellMenuAction.MediumIcons, L10n.T("ui.medium_icons")),
                Tuple.Create(ShellMenuAction.SmallIcons, L10n.T("ui.small_icons")), Tuple.Create(ShellMenuAction.List, L10n.T("ui.list")),
                Tuple.Create(ShellMenuAction.SortName, L10n.T("ui.name")), Tuple.Create(ShellMenuAction.SortSize, L10n.T("ui.size")),
                Tuple.Create(ShellMenuAction.SortType, L10n.T("ui.type")), Tuple.Create(ShellMenuAction.SortModified, L10n.T("ui.date_modified")),
                Tuple.Create(ShellMenuAction.Ascending, L10n.T("ui.ascending")), Tuple.Create(ShellMenuAction.Descending, L10n.T("ui.descending")) })
            {
                ShellMenuAction action = choice.Item1;
                var item = new MenuItem { Header = choice.Item2, Tag = action, IsCheckable = true };
                item.Click += (s, e) => ApplyViewSort(action);
                if (action == ShellMenuAction.List) views.Items.Add(item);
                else if ((int)action < 20) sizes.Items.Add(item);
                else sorts.Items.Add(item);
                choices.Add(item);
            }
            var spacing = new MenuItem { Header = L10n.T("ui.icon_spacing") };
            foreach (var choice in new[] {
                Tuple.Create("compact", L10n.T("ui.icon_spacing_compact")),
                Tuple.Create("standard", L10n.T("ui.icon_spacing_standard")),
                Tuple.Create("roomy", L10n.T("ui.icon_spacing_roomy")) })
            {
                string preset = choice.Item1;
                var option = new MenuItem { Header = choice.Item2, Tag = preset, IsCheckable = true };
                option.Click += (sender, e) =>
                {
                    settings.IconSpacing = preset;
                    SetView("icons");
                };
                spacing.Items.Add(option);
            }
            views.Items.Add(spacing);
            menu.Items.Add(views); menu.Items.Add(sorts);
            var thumbs = new MenuItem { Header = L10n.T("ui.show_thumbnails"), IsCheckable = true, IsChecked = settings.ShowThumbnails != false };
            thumbs.Click += async (s, e) => await ToggleThumbnails();
            var hidden = new MenuItem { Header = L10n.T("ui.show_hidden"), InputGestureText = "Ctrl+H", IsCheckable = true, IsChecked = settings.ShowHidden };
            hidden.Click += async (sender, e) => await ToggleHidden();
            views.Items.Add(new Separator()); views.Items.Add(thumbs); views.Items.Add(hidden);
            menu.Opened += (s, e) => {
                thumbs.IsChecked = settings.ShowThumbnails != false; hidden.IsChecked = settings.ShowHidden;
                spacing.IsEnabled = settings.ViewMode == "icons";
                foreach (MenuItem option in spacing.Items)
                    option.IsChecked = (string)option.Tag == settings.IconSpacing;
                foreach (MenuItem item in choices)
                {
                    ShellMenuAction action = (ShellMenuAction)item.Tag;
                    item.IsChecked = action == ShellMenuAction.List ? settings.ViewMode == "list" :
                        action == ShellMenuAction.LargeIcons ? settings.ViewMode == "icons" && settings.IconSize == 64 :
                        action == ShellMenuAction.MediumIcons ? settings.ViewMode == "icons" && settings.IconSize == 42 :
                        action == ShellMenuAction.SmallIcons ? settings.ViewMode == "icons" && settings.IconSize == 24 :
                        action == ShellMenuAction.SortName ? settings.SortBy == "name" : action == ShellMenuAction.SortSize ? settings.SortBy == "size" :
                        action == ShellMenuAction.SortType ? settings.SortBy == "type" : action == ShellMenuAction.SortModified ? settings.SortBy == "modified" :
                        action == ShellMenuAction.Descending ? settings.SortDescending : !settings.SortDescending;
                }
            };
            var opacity = new MenuItem { Header = L10n.T("ui.background_opacity") };
            foreach (int percent in new[] { 25, 45, 65, 85, 95 })
            {
                int value = percent;
                var item = new MenuItem { Header = L10n.T("ui.background_opacity_3bc015") + percent + "%" };
                item.Click += (s, e) => { settings.BackgroundOpacity = value / 100.0; ApplyAppearance(); Persist(); };
                opacity.Items.Add(item);
            }
            menu.Items.Add(opacity);
            menu.Items.Add(new Separator());
            AddMenu(menu, L10n.T("ui.restore_window_ctrl_shift_r_2d0a09"), () => { RestoreWindow(); return Done(); });
            var snap = new MenuItem { Header = L10n.T("ui.edge_magnetism"), IsCheckable = true, IsChecked = settings.SnapToEdges != false };
            snap.Click += (s, e) => { settings.SnapToEdges = snap.IsChecked; Persist(); };
            menu.Items.Add(snap);
            var hover = new MenuItem { Header = L10n.T("ui.expand_on_hover"), IsCheckable = true, IsChecked = settings.ExpandOnHover };
            hover.Click += (sender, e) => {
                settings.ExpandOnHover = hover.IsChecked;
                if (!settings.ExpandOnHover) SetHoverExpanded(false);
                Persist();
            };
            menu.Items.Add(hover);
            menu.Opened += (sender, e) => hover.IsChecked = settings.ExpandOnHover;
            AddMenu(menu, L10n.T("ui.new_box_ctrl_n_ff9436"), () => { NewBox(); return Done(); });
            menu.Items.Add(new Separator());
            var startup = new MenuItem { Header = L10n.T("startup.label"), ToolTip = L10n.T("startup.hint"),
                IsCheckable = true, IsEnabled = workspace != null, IsChecked = StartupRegistration.IsEnabled };
            startup.Click += (sender, e) => { if (workspace != null) workspace.SetStartAtLogin(startup.IsChecked); startup.IsChecked = StartupRegistration.IsEnabled; };
            menu.Opened += (sender, e) => startup.IsChecked = StartupRegistration.IsEnabled;
            menu.Items.Add(startup);
            menu.Items.Add(L10n.LanguageMenu(() => workspace == null ? L10n.Preference : workspace.LanguagePreference, value => {
                if (workspace != null) workspace.SetLanguage(value);
            }));
            menu.Items.Add(new Separator());
            AddMenu(menu, L10n.T("ui.close_this_box"), () => { Close(); return Done(); });
            menu.Items.Add(new Separator());
            AddMenu(menu, L10n.T("ui.help_and_shortcuts"), () => { Dialogs.Help(this); return Done(); });
            AddMenu(menu, L10n.T("ui.about"), () => { Dialogs.About(this); return Done(); });
            AddMenu(menu, L10n.T("ui.exit"), () => { if (workspace != null) workspace.Exit(); else Close(); return Done(); });

            var files = fileMenu;
            MenuItem open = AddMenu(files, L10n.T("ui.open_with_linux_default_app"), async () => await OpenSelected());
            MenuItem openWith = AddMenu(files, L10n.T("ui.linux_open_with"), async () => await OpenWithSelected());
            MenuItem rename = AddMenu(files, L10n.T("ui.rename_f2_0b5144"), async () => await RenameSelected());
            MenuItem trash = AddMenu(files, L10n.T("ui.move_to_linux_trash_delete_725a71"), async () => await DeleteSelected(false));
            AddMenu(files, L10n.T("ui.open_linux_trash"), async () => await OpenTrash());
            AddMenu(files, L10n.T("ui.new_folder_ctrl_shift_n_1157ef"), async () => await NewFolder());
            files.Items.Add(new Separator());
            MenuItem copy = AddMenu(files, L10n.T("ui.copy_ctrl_c"), () => CopySelected(false));
            MenuItem cut = AddMenu(files, L10n.T("ui.cut_ctrl_x"), () => CopySelected(true));
            MenuItem paste = AddMenu(files, L10n.T("ui.paste_ctrl_v"), async () => await Paste());
            files.Opened += (s, e) => {
                open.IsEnabled = copy.IsEnabled = cut.IsEnabled = initialized && !busy && list.SelectedItems.Count > 0;
                openWith.IsEnabled = initialized && !busy && list.SelectedItems.Count == 1;
                trash.IsEnabled = initialized && !busy && list.SelectedItems.Count > 0;
                rename.IsEnabled = initialized && !busy && list.SelectedItems.Count == 1;
                try { paste.IsEnabled = initialized && !busy && Clipboard.ContainsFileDropList(); }
                catch { paste.IsEnabled = false; }
            };
            list.PreviewMouseRightButtonDown += (s, e) => {
                var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                FileModel model = item == null ? null : item.DataContext as FileModel;
                if (model == null) list.SelectedItems.Clear();
                else if (!list.SelectedItems.Contains(model)) list.SelectedItem = model;
            };
            list.PreviewMouseRightButtonUp += async (s, e) => {
                e.Handled = true;
                await NativeContextMenu(PointToScreen(e.GetPosition(this)));
            };
        }
        private void ShowBoxMenu(FrameworkElement target, Point? point = null)
        {
            menu.PlacementTarget = target;
            menu.Placement = point.HasValue ? PlacementMode.RelativePoint : PlacementMode.Bottom;
            menu.HorizontalOffset = point.HasValue ? point.Value.X : 0;
            menu.VerticalOffset = point.HasValue ? point.Value.Y : 0;
            menu.IsOpen = true;
        }
        private async Task NativeContextMenu(Point screen)
        {
            if (closing || IsContextMenuOpen) return;
            FileModel[] selected = list.SelectedItems.Cast<FileModel>().ToArray();
            if (selected.Length == 0)
            {
                // Background and header use exactly the same menu instance,
                // including view, opacity, snapping and bottom close actions.
                ShowBoxMenu(list, list.PointFromScreen(screen));
                return;
            }
            if (busy || !initialized) return;
            string[] paths = selected.Select(m => Paths.Unc(settings.Distro, m.Path)).ToArray();
            ShellMenuAction action = ShellMenuAction.None;
            bool failed = false;
            contextOpen = true;
            try
            {
                action = ShellContextMenu.Show(this, screen, paths, Paths.Unc(settings.Distro, currentFolder), settings);
            }
            catch (ShellMenuInvocationException ex)
            {
                // The menu was already shown and a command was chosen. A
                // second menu would be unrelated to that command's failure.
                Report(ex);
                return;
            }
            catch (Exception ex)
            {
                Notify(L10n.T("ui.windows_context_menu_unavailable") + ex.Message);
                failed = true;
            }
            finally { contextOpen = false; }
            if (failed)
            {
                fileMenu.PlacementTarget = list;
                fileMenu.Placement = PlacementMode.RelativePoint;
                Point point = list.PointFromScreen(screen);
                fileMenu.HorizontalOffset = point.X;
                fileMenu.VerticalOffset = point.Y;
                fileMenu.IsOpen = true;
                return;
            }
            if (action == ShellMenuAction.OpenWithLinux) await OpenWithSelected();
            else if (action == ShellMenuAction.Trash || action == ShellMenuAction.PermanentDelete) await DeleteSelected(action == ShellMenuAction.PermanentDelete);
            else if (action == ShellMenuAction.Rename) await RenameSelected();
            else if (action == ShellMenuAction.NewFolder) await NewFolder();
            else if (action == ShellMenuAction.OpenTrash) await OpenTrash();
            else if (action == ShellMenuAction.OpenLinux) await OpenItems(selected);
            else if (action == ShellMenuAction.CloseBox) { Close(); return; }
            else if (action == ShellMenuAction.ExitProgram) { if (workspace != null) workspace.Exit(); else Close(); return; }
            else if (action == ShellMenuAction.Configure) await Configure();
            else if (action == ShellMenuAction.NewBox) NewBox();
            else if (action == ShellMenuAction.Help) Dialogs.Help(this);
            else if (action == ShellMenuAction.Restore) RestoreWindow();
            else if (action == ShellMenuAction.Home) await Home();
            else if (action == ShellMenuAction.Refresh) await RefreshFolder(true);
            else if (action == ShellMenuAction.ToggleThumbnails) await ToggleThumbnails();
            else if (action == ShellMenuAction.ToggleHidden) await ToggleHidden();
            else ApplyViewSort(action);
            await RefreshFolder(false);
        }
        private MenuItem AddMenu(ContextMenu target, string label, Func<Task> action)
        {
            var item = new MenuItem { Header = MenuText.Clean(label) };
            item.Click += async (s, e) => { try { target.IsOpen = false; await action(); } catch (Exception ex) { Report(ex); } };
            target.Items.Add(item); return item;
        }
        private void NewBox() { if (workspace != null) workspace.CreateBox(this); }
        private void Persist() { if (closing) return; RememberBounds(); if (workspace != null) workspace.Save(); }
        public void RememberBounds() { RememberBoundsCore(false); }
        private void RememberBoundsCore(bool beforeMaximize)
        {
            if (closing || applyingBounds || WindowState != WindowState.Normal || settings.EdgeLayout != "" || (settings.Collapsed && settings.Maximized)
                || (!beforeMaximize && IsShellSized())) return;
            normalBounds = new Rect(Left, Top, Width, settings.Collapsed ? normalBounds.Height : Height);
            StoreNormalBounds();
        }
        private void StoreNormalBounds()
        {
            // The live rectangle is authoritative. Integers are retained only
            // for old releases reading the same layout file.
            settings.Bounds = SavedWindowBounds.From(normalBounds);
            settings.X = (int)normalBounds.Left; settings.Y = (int)normalBounds.Top;
            settings.Width = (int)normalBounds.Width; settings.Height = (int)normalBounds.Height;
        }
        internal Rect BoundsIn(BoxForm reference)
        {
            Rect native;
            if (WindowEffects.TryGetBounds(this, out native))
                return new Rect(reference.ToDip(native.TopLeft), reference.ToDip(native.BottomRight));
            Point start = reference.ToDip(PointToScreen(new Point(0, 0)));
            Point end = reference.ToDip(PointToScreen(new Point(ActualWidth, ActualHeight)));
            return new Rect(start, end);
        }
        private Point ToDip(Point device)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            return source == null ? device : source.CompositionTarget.TransformFromDevice.Transform(device);
        }
        private Rect WorkAreaAt(Point device)
        {
            var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)device.X, (int)device.Y)).WorkingArea;
            return new Rect(ToDip(new Point(work.Left, work.Top)), ToDip(new Point(work.Right, work.Bottom)));
        }
        private Rect ScreenAreaAt(Point device)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)device.X, (int)device.Y)).Bounds;
            return new Rect(ToDip(new Point(screen.Left, screen.Top)), ToDip(new Point(screen.Right, screen.Bottom)));
        }
        private Rect CurrentWorkArea()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            var source = PresentationSource.FromVisual(this) as HwndSource;
            var dpi = VisualTreeHelper.GetDpi(this);
            Matrix transform = source == null ? new Matrix(1 / dpi.DpiScaleX, 0, 0, 1 / dpi.DpiScaleY, 0, 0) : source.CompositionTarget.TransformFromDevice;
            System.Drawing.Rectangle work;
            if (handle == IntPtr.Zero || source == null)
            {
                Rect saved = normalBounds;
                var device = new System.Drawing.Rectangle((int)Math.Round(saved.Left * dpi.DpiScaleX), (int)Math.Round(saved.Top * dpi.DpiScaleY),
                    Math.Max(1, (int)Math.Round(saved.Width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Round(saved.Height * dpi.DpiScaleY)));
                var screen = System.Windows.Forms.Screen.FromRectangle(device);
                if (screen.Primary) return SystemParameters.WorkArea;
                work = screen.WorkingArea;
            }
            else work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            Point origin = transform.Transform(new Point(work.Left, work.Top));
            Point end = transform.Transform(new Point(work.Right, work.Bottom));
            return new Rect(origin, end);
        }
        private bool IsShellSized()
        {
            Rect work = CurrentWorkArea();
            double width = ActualWidth > 0 ? ActualWidth : Width, height = ActualHeight > 0 ? ActualHeight : Height;
            const double tolerance = 1.5;
            bool top = Math.Abs(Top - work.Top) <= tolerance, fullHeight = Math.Abs(height - work.Height) <= tolerance;
            bool left = Math.Abs(Left - work.Left) <= tolerance;
            bool fullWidth = Math.Abs(width - work.Width) <= tolerance;
            bool halfWidth = Math.Abs(width - work.Width / 2) <= tolerance;
            bool right = Math.Abs(Left - (work.Left + work.Width / 2)) <= tolerance;
            return top && fullHeight && ((left && fullWidth) || (halfWidth && (left || right)));
        }
        private void RestoreWindow()
        {
            RestoreBoundsAt(null, CurrentWorkArea(), false, true);
            Persist();
        }
        private void RestoreForDrag(Point pointer, Rect work)
        {
            RestoreBoundsAt(pointer, work, Collapsed);
        }
        private void RestoreBoundsAt(Point? pointer, Rect work, bool keepCollapsed, bool unlock = false)
        {
            Rect restore = WindowGeometry.Restore(normalBounds, work, pointer, captionRatio, captionOffset);
            bool previous = applyingBounds; applyingBounds = true;
            try
            {
                settings.Maximized = false; settings.EdgeLayout = ""; settings.MaximizeReturnEdge = ""; Collapsed = keepCollapsed;
                if (unlock) settings.Locked = false;
                WindowState = WindowState.Normal;
                MinHeight = keepCollapsed ? 42 : 180;
                Width = restore.Width; Height = keepCollapsed ? 44 : restore.Height;
                Left = restore.Left; Top = restore.Top;
                settings.X = (int)Left; settings.Y = (int)Top; settings.Width = (int)restore.Width; settings.Height = (int)restore.Height;
                ApplyAppearance();
                // Appearance uses integer persisted bounds; restore the exact
                // live rectangle last so fractional sizes cannot lose a row.
                Width = restore.Width; Height = keepCollapsed ? 44 : restore.Height;
                Left = restore.Left; Top = restore.Top;
                normalBounds = restore; StoreNormalBounds();
            }
            finally { applyingBounds = previous; }
        }
        private void ToggleMaximized()
        {
            if (WindowState == WindowState.Maximized || settings.Maximized)
            {
                if (settings.MaximizeReturnEdge != "")
                {
                    settings.Maximized = false; Collapsed = false; settings.EdgeLayout = settings.MaximizeReturnEdge;
                    settings.MaximizeReturnEdge = ""; ApplyWindowLayout(null); Persist();
                }
                else { RestoreBoundsAt(null, CurrentWorkArea(), false); Persist(); }
                return;
            }
            settings.MaximizeReturnEdge = settings.EdgeLayout;
            RememberBoundsCore(true);
            Collapsed = false; settings.EdgeLayout = ""; settings.Maximized = true;
            ApplyWindowLayout(null); Persist();
        }
        private void ApplyPointerSnap(PointerSnap target, Rect work)
        {
            if (target == PointerSnap.Maximize) settings.MaximizeReturnEdge = settings.EdgeLayout;
            RememberBoundsCore(target == PointerSnap.Maximize);
            Collapsed = false;
            settings.Maximized = target == PointerSnap.Maximize;
            settings.EdgeLayout = target == PointerSnap.Left ? "left" : target == PointerSnap.Right ? "right" : "";
            ApplyWindowLayout(work); Persist();
        }
        public void ShowInBackground()
        {
            if (!closing) WindowEffects.ShowInBackground(this);
        }
        public void ShowFromDesktop() { ShowInBackground(); }
        public void ShowFromTray()
        {
            if (closing) return;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = settings.Maximized ? WindowState.Maximized : WindowState.Normal;
            Activate();
        }
        private void UpdateTitle()
        {
            string path = (currentFolder ?? "").TrimEnd('/');
            title.Text = !String.IsNullOrWhiteSpace(settings.Title) ? settings.Title :
                path.Length == 0 ? (String.IsNullOrEmpty(settings.Distro) ? L10n.T("ui.folder_box") : settings.Distro) : path.Split('/').Last();
            title.ToolTip = settings.Distro + L10n.T("ui.current_folder") + currentFolder + L10n.T("ui.bound_folder") + settings.Folder + "\n" + models.Count + L10n.T("ui.items");
            if (status.Visibility == Visibility.Visible && !String.IsNullOrEmpty(status.Text)
                && status.Text != models.Count + L10n.T("ui.items"))
                title.ToolTip = Convert.ToString(title.ToolTip) + "\n\n" + status.Text;
            Title = "WSLBox · " + title.Text;
        }
        private void ApplyAppearance()
        {
            plate.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * settings.BackgroundOpacity), 15, 19, 26));
            lockButton.FontFamily = new FontFamily("Segoe MDL2 Assets");
            lockButton.FontSize = 16;
            lockButton.Content = settings.Locked ? "\uE72E" : "\uE785";
            lockButton.ToolTip = settings.Locked ? L10n.T("ui.locked_click_to_unlock_position_and_size") : L10n.T("ui.lock_position_and_size");
            collapseButton.Content = Collapsed ? "⌄" : "⌃";
            body.Visibility = Collapsed ? Visibility.Collapsed : Visibility.Visible;
            grip.Visibility = Collapsed || settings.Locked ? Visibility.Collapsed : Visibility.Visible;
            ResizeMode = settings.Locked || Collapsed ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            if (Collapsed) status.Visibility = Visibility.Collapsed;
        }
        private void ApplyWindowLayout(Rect? requestedWork)
        {
            if (closing) return;
            bool previous = applyingBounds; applyingBounds = true;
            try
            {
                ApplyAppearance();
                if (Collapsed)
                {
                    Rect work = requestedWork ?? CurrentWorkArea();
                    Rect edge = WindowGeometry.Half(work, settings.EdgeLayout == "right");
                    WindowState = WindowState.Normal; MinHeight = 42;
                    Width = settings.Maximized ? work.Width : settings.EdgeLayout != "" ? edge.Width : normalBounds.Width;
                    Height = 44;
                    if (settings.Maximized) { Left = work.Left; Top = work.Top; }
                    else if (settings.EdgeLayout != "") { Left = edge.Left; Top = edge.Top; }
                }
                else if (hoverExpanded && settings.Maximized)
                {
                    Rect work = requestedWork ?? CurrentWorkArea();
                    WindowState = WindowState.Normal; MinHeight = 180;
                    Left = work.Left; Top = work.Top; Width = work.Width; Height = work.Height;
                }
                else
                {
                    MinHeight = 180; Width = normalBounds.Width; Height = normalBounds.Height;
                    if (requestedWork.HasValue && settings.Maximized)
                    {
                        WindowState = WindowState.Normal;
                        Left = requestedWork.Value.Left + Math.Max(0, (requestedWork.Value.Width - Width) / 2);
                        Top = requestedWork.Value.Top + 20;
                    }
                    WindowState = settings.Maximized ? WindowState.Maximized : WindowState.Normal;
                    if (!settings.Maximized && settings.EdgeLayout != "")
                    {
                        Rect edge = WindowGeometry.Half(requestedWork ?? CurrentWorkArea(), settings.EdgeLayout == "right");
                        Width = edge.Width; Height = edge.Height; Left = edge.Left; Top = edge.Top;
                    }
                }
            }
            finally { applyingBounds = previous; }
        }
        private void ScheduleHover(int milliseconds)
        {
            hoverTimer.Stop(); hoverTimer.Interval = TimeSpan.FromMilliseconds(milliseconds); hoverTimer.Start();
        }
        private bool HoverBlocked(bool expanding)
        {
            return dragging || captionPending || !sizingStart.IsEmpty || IsContextMenuOpen || menu.IsOpen || renameEditor != null
                || OwnedWindows.Cast<Window>().Any(window => window.IsVisible)
                || (!expanding && (busy || opening));
        }
        private void HoverEnter()
        {
            hoverTimer.Stop();
            if (closing || !IsVisible || !settings.ExpandOnHover || !settings.Collapsed || hoverExpanded) return;
            if (HoverBlocked(true)) { ScheduleHover(80); return; }
            SetHoverExpanded(true);
        }
        private void HoverTick()
        {
            hoverTimer.Stop();
            if (closing || !IsVisible || !settings.ExpandOnHover) return;
            if (IsMouseOver == hoverExpanded) return;
            if (HoverBlocked(IsMouseOver)) { ScheduleHover(80); return; }
            SetHoverExpanded(IsMouseOver && settings.Collapsed);
        }
        private void SetHoverExpanded(bool expanded)
        {
            expanded = expanded && settings.ExpandOnHover && settings.Collapsed && !closing;
            if (hoverExpanded == expanded) return;
            hoverExpanded = expanded; ++thumbnailGeneration;
            ApplyWindowLayout(null);
            if (expanded && initialized && IsVisible) LoadThumbnails(thumbnailGeneration, settings.Distro, currentFolder);
        }
        private void ToggleCollapsed()
        {
            if (!Collapsed)
            {
                if (WindowState == WindowState.Maximized) settings.Maximized = true;
                else RememberBounds();
            }
            Collapsed = !Collapsed; ++thumbnailGeneration; ApplyWindowLayout(null); Persist();
            if (!Collapsed) LoadThumbnails(thumbnailGeneration, settings.Distro, currentFolder);
        }
        private void SetView(string mode, bool save = true)
        {
            if (renameEditor != null) renameEditor.Cancel();
            settings.ViewMode = mode == "list" ? "list" : "icons";
            bool icons = settings.ViewMode == "icons";
            viewButton.Content = Theme.HeaderIcon(icons ? "list" : "grid");
            viewButton.ToolTip = icons ? L10n.T("ui.switch_to_list") : L10n.T("ui.switch_to_icons");
            list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse(icons
                ? "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapPanel/></ItemsPanelTemplate>"
                : "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><VirtualizingStackPanel/></ItemsPanelTemplate>");
            VirtualizingStackPanel.SetIsVirtualizing(list, true);
            VirtualizingStackPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            int iconSize = settings.IconSize;
            int itemWidth = Math.Max(74, iconSize + 44);
            int itemHeight = iconSize + 52;
            string iconMargin = "0,5,0,6";
            if (settings.IconSpacing == "compact")
            {
                itemWidth -= 12;
                itemHeight -= 9;
                iconMargin = "0,3,0,3";
            }
            else if (settings.IconSpacing == "roomy")
            {
                itemWidth += 16;
                itemHeight += 12;
            }
            list.ItemTemplate = (DataTemplate)XamlReader.Parse(icons
                ? "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='" + itemWidth + "' Height='" + itemHeight + "' ToolTip='{Binding Path}'><Image Source='{Binding Icon}' Width='" + iconSize + "' Height='" + iconSize + "' Margin='" + iconMargin + "'/><TextBlock Name='FileName' Text='{Binding Label}' TextAlignment='Center' TextWrapping='Wrap' TextTrimming='CharacterEllipsis' MaxHeight='36' FontSize='12'/></StackPanel></DataTemplate>"
                : @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><DockPanel Height='29' ToolTip='{Binding Path}'><Image Source='{Binding Icon}' Width='20' Height='20' Margin='3,0,9,0' DockPanel.Dock='Left'/><TextBlock Name='FileName' Text='{Binding Label}' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' Margin='0,0,10,0'/></DockPanel></DataTemplate>");
            if (save) Persist();
        }
        private static string Category(Entry entry)
        {
            if (entry.IsDirectory) return "folders";
            string type = entry.ContentType ?? "", ext = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
            if (type.StartsWith("image/")) return "images";
            if (new[] { ".zip", ".7z", ".tar", ".gz", ".xz", ".bz2", ".rar", ".deb", ".rpm" }.Contains(ext)) return "archives";
            if (type.StartsWith("text/") || new[] { ".pdf", ".doc", ".docx", ".odt", ".xls", ".xlsx", ".ppt", ".pptx" }.Contains(ext)) return "documents";
            return "other";
        }
        private void ApplyFilter()
        {
            if (renameEditor != null) renameEditor.Cancel();
            var selected = new HashSet<string>(list.SelectedItems.Cast<FileModel>().Select(m => m.Path), StringComparer.Ordinal);
            List<FileModel> shown = models.Where(m => settings.Filter == "all" || Category(m.Entry) == settings.Filter).ToList();
            shown.Sort((left, right) => FileOrdering.Compare(left.Entry, right.Entry, settings.SortBy, settings.SortDescending));
            var current = list.ItemsSource as List<FileModel>;
            if (current == null || !current.SequenceEqual(shown))
            {
                list.ItemsSource = shown;
                foreach (FileModel model in shown) if (selected.Contains(model.Path)) list.SelectedItems.Add(model);
            }
            foreach (Button button in filters.Children)
                button.Foreground = (string)button.Tag == settings.Filter ? new SolidColorBrush(Color.FromRgb(153, 229, 103)) : Theme.Text;
        }
        private bool ApplyViewSort(ShellMenuAction action)
        {
            if (action == ShellMenuAction.List) { SetView("list"); return true; }
            if (action == ShellMenuAction.LargeIcons || action == ShellMenuAction.MediumIcons || action == ShellMenuAction.SmallIcons)
            {
                settings.IconSize = action == ShellMenuAction.LargeIcons ? 64 : action == ShellMenuAction.SmallIcons ? 24 : 42;
                SetView("icons"); return true;
            }
            if (action == ShellMenuAction.SortName) settings.SortBy = "name";
            else if (action == ShellMenuAction.SortSize) settings.SortBy = "size";
            else if (action == ShellMenuAction.SortType) settings.SortBy = "type";
            else if (action == ShellMenuAction.SortModified) settings.SortBy = "modified";
            else if (action == ShellMenuAction.Ascending) settings.SortDescending = false;
            else if (action == ShellMenuAction.Descending) settings.SortDescending = true;
            else return false;
            ApplyFilter(); Persist(); return true;
        }
        private void ShowEntries(Reply reply)
        {
            currentFolder = reply.Path;
            if (String.IsNullOrEmpty(settings.Folder)) settings.Folder = reply.Path;
            Entry[] entries = reply.Entries ?? new Entry[0];
            if (!FileOrdering.SameEntries(listedEntries, entries))
            {
                ++thumbnailGeneration;
                var icons = DecodeImages(reply.Icons);
                ImageSource folder = Theme.Fallback(true), file = Theme.Fallback(false);
                var previous = models.ToDictionary(m => m.Path, StringComparer.Ordinal);
                models = entries.Select(e => {
                    FileModel model;
                    if (!previous.TryGetValue(e.Path, out model)) model = new FileModel();
                    model.Update(e, e.IconKey != null && icons.ContainsKey(e.IconKey) ? icons[e.IconKey] : e.IsDirectory ? folder : file);
                    return model;
                }).ToList();
                listedEntries = entries; ApplyFilter();
            }
            if (settings.ShowThumbnails != false) LoadThumbnails(thumbnailGeneration, settings.Distro, currentFolder);
            UpdateTitle();
        }
        private Dictionary<string, ImageSource> DecodeImages(LinuxIcon[] source)
        {
            var icons = new Dictionary<string, ImageSource>();
            foreach (LinuxIcon icon in source ?? new LinuxIcon[0])
            {
                try
                {
                    ImageSource cached;
                    if (decodedImages.TryGetValue(icon.Key, out cached)) { icons[icon.Key] = cached; continue; }
                    using (var stream = new MemoryStream(Convert.FromBase64String(icon.Png)))
                    {
                        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream; image.EndInit(); image.Freeze(); icons[icon.Key] = image;
                        if (decodedImages.Count >= 512) decodedImages.Clear();
                        decodedImages[icon.Key] = image;
                    }
                }
                catch (Exception ex) { if (!(ex is FormatException) && !(ex is NotSupportedException) && !(ex is ArgumentException)) throw; }
            }
            return icons;
        }
        private bool CanLoadThumbnails
        { get { return !closing && IsVisible && !Collapsed && WindowState != WindowState.Minimized && settings.ShowThumbnails != false; } }
        private async void LoadThumbnails(int generation, string distro, string folder)
        {
            if (!CanLoadThumbnails || thumbnailRunning == generation) return;
            string[] paths = models.Where(m => m.ThumbnailPending).Select(m => m.Path).ToArray();
            if (paths.Length == 0) return;
            thumbnailRunning = generation;
            var byPath = models.ToDictionary(m => m.Path, StringComparer.Ordinal);
            try
            {
                for (int offset = 0; offset < paths.Length; offset += 4)
                {
                    if (!CanLoadThumbnails || generation != thumbnailGeneration) return;
                    string[] batch = paths.Skip(offset).Take(4).ToArray();
                    Reply reply = await wsl.Run(distro, "thumbnails", Json.Write(batch));
                    if (!CanLoadThumbnails || generation != thumbnailGeneration || distro != settings.Distro || folder != currentFolder) return;
                    var icons = DecodeImages(reply.Icons);
                    foreach (ThumbnailResult result in reply.Thumbnails ?? new ThumbnailResult[0])
                    {
                        FileModel model; ImageSource image;
                        if (byPath.TryGetValue(result.Path, out model) && result.IconKey != null && icons.TryGetValue(result.IconKey, out image)) model.Icon = image;
                    }
                    foreach (string path in batch) { FileModel model; if (byPath.TryGetValue(path, out model)) model.ThumbnailPending = false; }
                }
            }
            catch { } // Keep type icons; metadata refresh controls retry/backoff.
            finally { if (thumbnailRunning == generation) thumbnailRunning = -1; }
        }
        private Task ToggleThumbnails() { return ToggleListing(false); }
        private Task ToggleHidden() { return ToggleListing(true); }
        private async Task ToggleListing(bool hidden)
        {
            if (busy || !initialized || closing) return;
            if (hidden) settings.ShowHidden = !settings.ShowHidden;
            else settings.ShowThumbnails = settings.ShowThumbnails == false;
            ++thumbnailGeneration; listedEntries = null; Persist(); busy = true;
            try
            {
                Reply reply = await wsl.Run(settings.Distro, ListCommand, currentFolder, settings.ShowHidden);
                if (!closing) ShowEntries(reply);
            }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; }
        }
        private void Notify(string message, bool temporary = true)
        {
            if (closing) return;
            status.Text = message; status.ToolTip = message; status.Visibility = Collapsed ? Visibility.Collapsed : Visibility.Visible;
            UpdateTitle();
            noticeTimer.Stop(); if (temporary) noticeTimer.Start();
        }
        private void Report(Exception ex)
        {
            if (closing) return;
            Notify(L10n.T("ui.operation_failed") + ex.Message);
            MessageBox.Show(this, ex.Message, "WSLBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        private async Task Initialize()
        {
            if (initialized) return;
            busy = true; Notify(L10n.T("ui.connecting_to_wsl"), false);
            try
            {
                Reply info = await wsl.Run(settings.Distro, "info", null);
                if (closing) return;
                if (String.IsNullOrEmpty(settings.Distro)) settings.Distro = info.Distro;
                if (String.IsNullOrEmpty(settings.Folder)) settings.Folder = info.Home;
                currentFolder = settings.Folder;
                initialized = true;
                wsl.PrepareOpen(settings.Distro);
            }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; }
            if (closing) return;
            if (initialized) { await RefreshFolder(true); history.Reset(currentFolder); }
            refreshTimer.Start();
        }
        private async Task RefreshFolder(bool explicitRequest)
        {
            if (busy || listing || !initialized || closing || renameEditor != null || (!explicitRequest && !IsVisible)) return;
            listing = true;
            string requested = currentFolder, distro = settings.Distro;
            if (explicitRequest) Notify(L10n.T("ui.reading_the_folder"), false);
            string command = ListCommand; bool showHidden = settings.ShowHidden;
            try
            {
                Reply reply = await wsl.Run(distro, command, requested, showHidden);
                if (command != ListCommand || showHidden != settings.ShowHidden) return;
                if (closing || busy || dragging || IsContextMenuOpen || renameEditor != null || (!explicitRequest && !IsVisible)
                    || requested != currentFolder || distro != settings.Distro) return;
                ShowEntries(reply); lastError = "";
                if (explicitRequest) Notify(models.Count + L10n.T("ui.items"));
            }
            catch (Exception ex)
            {
                if (closing || command != ListCommand || showHidden != settings.ShowHidden || renameEditor != null || requested != currentFolder || distro != settings.Distro) return;
                models.Clear(); list.ItemsSource = null; listedEntries = null;
                Notify(L10n.T("ui.read_failed") + ex.Message);
                if (explicitRequest && lastError != ex.Message) Report(ex);
                lastError = ex.Message;
            }
            finally { listing = false; }
        }
        private async Task<bool> Navigate(string path, int historyStep = 0)
        {
            if (busy || !initialized || closing) return false;
            busy = true; Notify(L10n.T("ui.opening_the_folder"), false);
            try
            {
                Reply reply = await wsl.Run(settings.Distro, ListCommand, path, settings.ShowHidden);
                if (closing) return false;
                ShowEntries(reply);
                if (historyStep < 0) history.CommitBack();
                else if (historyStep > 0) history.CommitForward();
                else history.Record(reply.Path);
                Persist(); Notify(models.Count + L10n.T("ui.items")); return true;
            }
            catch (Exception ex) { Report(ex); return false; }
            finally { busy = false; }
        }
        private async Task HistoryNavigate(bool forward)
        {
            string target = forward ? history.ForwardTarget : history.BackTarget;
            if (target != null) await Navigate(target, forward ? 1 : -1);
        }
        private async Task Up()
        {
            string path = (currentFolder ?? "/").TrimEnd('/'); int slash = path.LastIndexOf('/');
            await Navigate(slash <= 0 ? "/" : path.Substring(0, slash));
        }
        private Task<bool> Home() { return Navigate(settings.Folder); }
        private async Task Configure()
        {
            if (busy) return;
            var dialog = new ConfigForm(settings.Distro, settings.Folder, settings.Title) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            busy = true; Notify(L10n.T("ui.checking_the_folder"), false);
            try
            {
                Reply reply = await wsl.Run(dialog.Distro, ListCommand, dialog.Folder, settings.ShowHidden);
                if (closing) return;
                settings.Distro = dialog.Distro; settings.Title = dialog.BoxTitle; settings.Folder = reply.Path;
                wsl.PrepareOpen(settings.Distro);
                initialized = true; ShowEntries(reply); history.Reset(reply.Path); Persist(); Notify(models.Count + L10n.T("ui.items"));
            }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; }
        }
        private async Task ActivateItem(FileModel model)
        {
            if (busy) return;
            if (model.Entry.IsDirectory) await Navigate(model.Path);
            else await OpenItems(new[] { model });
        }
        private Task OpenSelected() { return OpenItems(list.SelectedItems.Cast<FileModel>().ToArray()); }
        private async Task OpenItems(FileModel[] selected)
        {
            if (busy || opening || selected.Length == 0) return;
            opening = true; Notify(L10n.T("ui.waiting_for_the_linux_default_app"), false);
            try
            {
                string application = L10n.T("ui.linux_default_application"), inputWarning = null;
                foreach (FileModel model in selected)
                {
                    Reply reply = await wsl.Run(settings.Distro, "open", model.Path);
                    if (!reply.Ok) throw new IOException(L10n.T("ui.the_linux_default_app_did_not_acknowledge_the_request"));
                    if (!String.IsNullOrEmpty(reply.Application)) application = reply.Application;
                    if (!String.IsNullOrEmpty(reply.InputMethodWarning)) inputWarning = reply.InputMethodWarning;
                }
                Notify(application + L10n.T("ui.accepted_the_open_request") + (String.IsNullOrEmpty(inputWarning) ? "" : " " + inputWarning));
            }
            catch (Exception ex) { Report(ex); }
            finally { opening = false; }
        }
        private async void OnKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (FileKeyRouting.DeferToFocusedControl(Keyboard.FocusedElement is TextBoxBase,
                Keyboard.FocusedElement is ButtonBase, Collapsed, key, Keyboard.Modifiers)) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            if (ctrl && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && key == Key.R) RestoreWindow();
            else if (key == Key.Delete && !ctrl && !alt) await DeleteSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            else if (key == Key.F2 && !ctrl && !alt) await RenameSelected();
            else if (ctrl && (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && key == Key.N) await NewFolder();
            else if (ctrl && key == Key.H) await ToggleHidden();
            else if (ctrl && key == Key.A) list.SelectAll();
            else if (ctrl && key == Key.C) { e.Handled = true; await CopySelected(false); }
            else if (ctrl && key == Key.X) { e.Handled = true; await CopySelected(true); }
            else if (ctrl && key == Key.V) await Paste();
            else if (ctrl && key == Key.N) NewBox();
            else if (ctrl && key == Key.L) await Configure();
            else if (ctrl && key == Key.D1) SetView("icons");
            else if (ctrl && key == Key.D2) SetView("list");
            else if (alt && key == Key.Left) await HistoryNavigate(false);
            else if (alt && key == Key.Right) await HistoryNavigate(true);
            else if (alt && key == Key.Home) await Home();
            else if ((alt && key == Key.Up) || key == Key.Back) await Up();
            else if (key == Key.F5 || (ctrl && key == Key.R)) await RefreshFolder(true);
            else if (key == Key.Apps || ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && key == Key.F10))
            {
                if (Collapsed) ShowBoxMenu(this);
                else await NativeContextMenu(list.PointToScreen(new Point(20, 20)));
            }
            else if (key == Key.Escape) list.SelectedItems.Clear();
            else if (key == Key.Enter && list.SelectedItems.Count == 1) await ActivateItem((FileModel)list.SelectedItem);
            else return;
            e.Handled = true;
        }
        private async Task OpenWithSelected()
        {
            if (busy || opening || !initialized || IsContextMenuOpen || list.SelectedItems.Count != 1) return;
            FileModel model = (FileModel)list.SelectedItem;
            opening = true;
            try
            {
                var dialog = new OpenWithDialog(this, wsl, settings.Distro, model.Path);
                if (dialog.ShowDialog() != true) return;
                Notify(L10n.T("ui.opening_with_the_selected_linux_app"), false);
                Reply reply = await wsl.OpenWith(settings.Distro, model.Path, dialog.ApplicationId, dialog.SetDefault);
                if (!reply.Ok) throw new IOException(L10n.T("ui.the_linux_app_did_not_acknowledge_the_request"));
                Notify((reply.Application ?? L10n.T("ui.linux_application")) + L10n.T("ui.accepted_the_open_request") + (String.IsNullOrEmpty(reply.InputMethodWarning) ? "" : " " + reply.InputMethodWarning));
            }
            catch (Exception ex) { Report(ex); }
            finally { opening = false; }
        }
        private async Task DeleteSelected(bool permanent)
        {
            if (busy || opening || !initialized || IsContextMenuOpen) return;
            FileModel[] selected = list.SelectedItems.Cast<FileModel>().ToArray();
            if (selected.Length == 0) return;
            string action = permanent ? L10n.T("ui.permanently_delete") : L10n.T("ui.move_to_linux_trash");
            string question = permanent
                ? L10n.T("ui.permanently_delete_b7df60") + selected.Length + L10n.T("ui.selected_item_s_these_items_will_not_go_to_trash")
                : L10n.T("ui.move") + selected.Length + L10n.T("ui.selected_item_s_to_linux_trash_you_can_restore_them_with_a_linux");
            if (MessageBox.Show(this, question, action, MessageBoxButton.YesNo,
                permanent ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            busy = true; int completed = 0; var failures = new List<FileFailure>();
            Notify(L10n.T("ui.working") + action + "…", false);
            try
            {
                foreach (string[] batch in FileActions.Batches(selected.Select(m => m.Path).ToArray()))
                {
                    Reply reply = await wsl.Run(settings.Distro, permanent ? "delete-permanently" : "trash", Json.Write(new FileRequest { Paths = batch }));
                    completed += reply.Completed;
                    if (reply.Failures != null) failures.AddRange(reply.Failures);
                }
                if (failures.Count > 0)
                    throw new IOException(completed + L10n.T("ui.item_s_completed") + failures.Count + L10n.T("ui.item_s_failed") +
                        String.Join("\n", failures.Take(6).Select(f => f.Path + "：" + f.Error)) +
                        (permanent ? "" : L10n.T("ui.items_that_could_not_be_trashed_will_not_be_permanently_deleted_")));
                Notify(L10n.F(permanent ? "files.deleted_count" : "files.trashed_count", completed));
            }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; }
            await RefreshFolder(false);
        }
        private Task RenameSelected()
        {
            if (busy || opening || !initialized || IsContextMenuOpen || list.SelectedItems.Count != 1 || renameEditor != null) return Done();
            FileModel model = (FileModel)list.SelectedItem;
            list.ScrollIntoView(model); list.UpdateLayout();
            var container = list.ItemContainerGenerator.ContainerFromItem(model) as ListBoxItem;
            TextBlock label = FindFileLabel(container);
            if (label == null) return Done();
            string renamed = null;
            InlineRename editor = null;
            editor = new InlineRename(label, model.Entry.Name, model.Entry.IsDirectory, settings.ViewMode == "icons", async name => {
                if (busy || closing) return L10n.T("ui.another_operation_is_in_progress_try_confirming_again_shortly");
                busy = true;
                try
                {
                    Reply reply = await wsl.Run(settings.Distro, "rename", Json.Write(new FileRequest { Path = model.Path, Name = name }));
                    if (!reply.Ok) return L10n.T("ui.rename_did_not_complete");
                    renamed = reply.Path; return null;
                }
                catch (Exception ex) { return ex.Message; }
                finally { busy = false; }
            }, async saved => {
                if (renameEditor == editor) renameEditor = null;
                if (closing) return;
                if (saved)
                {
                    bool selected = list.SelectedItem == model;
                    await RefreshFolder(true);
                    FileModel result = selected ? models.FirstOrDefault(m => m.Path == renamed) : null;
                    if (result != null) { list.SelectedItem = result; list.ScrollIntoView(result); }
                }
                if (editor.RestoreFocus && IsActive) list.Focus();
            });
            renameEditor = editor; dragOrigin = null; editor.Show(); return Done();
        }
        private static TextBlock FindFileLabel(DependencyObject root)
        {
            if (root == null) return null;
            var text = root as TextBlock;
            if (text != null && text.Name == "FileName") return text;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                TextBlock found = FindFileLabel(VisualTreeHelper.GetChild(root, index));
                if (found != null) return found;
            }
            return null;
        }
        private async Task NewFolder()
        {
            if (busy || opening || !initialized || IsContextMenuOpen) return;
            string name = Dialogs.FileName(this, L10n.T("ui.new_folder"), L10n.T("ui.new_folder"), true);
            if (name != null) await NamedFileAction("mkdir", currentFolder, name);
        }
        private async Task NamedFileAction(string command, string path, string name)
        {
            busy = true;
            try
            {
                Reply reply = await wsl.Run(settings.Distro, command, Json.Write(new FileRequest { Path = path, Name = name }));
                if (!reply.Ok) throw new IOException(L10n.T("ui.the_file_operation_did_not_complete"));
                busy = false; await RefreshFolder(true);
                FileModel result = models.FirstOrDefault(m => m.Path == reply.Path);
                if (result != null) { list.SelectedItem = result; list.ScrollIntoView(result); }
            }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; }
        }
        private async Task OpenTrash()
        {
            if (busy || opening || !initialized) return;
            opening = true;
            try { await wsl.Run(settings.Distro, "open-trash", null); }
            catch (Exception ex) { Report(ex); }
            finally { opening = false; }
        }
        private string[] SelectedPaths()
        {
            FileModel[] selected = list.SelectedItems.Cast<FileModel>().ToArray();
            if (selected.Any(m => m.Entry.IsLink)) throw new IOException(L10n.T("ui.transfer_symbolic_links_within_linux_to_preserve_their_semantics"));
            return selected.Select(m => Paths.Unc(settings.Distro, m.Path)).ToArray();
        }
        private static DataObject FileData(string[] paths, bool move)
        {
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)));
            return data;
        }
        private static DragDropEffects DropEffect(DragDropKeyStates keys, DragDropEffects allowed)
        {
            bool shift = (keys & DragDropKeyStates.ShiftKey) != 0, ctrl = (keys & DragDropKeyStates.ControlKey) != 0;
            if (shift && ctrl) return DragDropEffects.None;
            DragDropEffects effect = shift ? DragDropEffects.Move : DragDropEffects.Copy;
            return (allowed & effect) != 0 ? effect : DragDropEffects.None;
        }
        private DragDropEffects ResolveDrop(DragEventArgs e, out string[] paths, out string target, out FileModel folder)
        {
            paths = null;
            target = currentFolder;
            folder = null;
            if (busy || dropInProgress || closing || !initialized) return DragDropEffects.None;
            DragDropEffects effect = DropEffect(e.KeyStates, e.AllowedEffects);
            if (effect == DragDropEffects.None) return effect;
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
                paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            }
            catch (System.Runtime.InteropServices.ExternalException) { return DragDropEffects.None; }

            target = DropTarget(e.GetPosition(list), out folder);
            return Paths.CanDrop(paths, Paths.Unc(settings.Distro, target), effect == DragDropEffects.Move)
                ? effect : DragDropEffects.None;
        }
        private string DropTarget(Point position, out FileModel folder)
        {
            var item = Ancestor<ListBoxItem>(list.InputHitTest(position) as DependencyObject);
            FileModel model = item == null ? null : item.DataContext as FileModel;
            folder = model != null && model.Entry.IsDirectory && !model.Entry.IsLink ? model : null;
            return folder == null ? currentFolder : folder.Path;
        }
        private void HighlightDropFolder(FileModel folder)
        {
            if (ReferenceEquals(highlightedDropFolder, folder)) return;
            if (highlightedDropFolder != null) highlightedDropFolder.IsDropTarget = false;
            highlightedDropFolder = folder;
            if (folder != null) folder.IsDropTarget = true;
        }
        private void ClearDropPreview()
        {
            if (dropInProgress) return;
            HighlightDropFolder(null);
            if (showingDropPreview) status.Visibility = Visibility.Collapsed;
            showingDropPreview = false;
        }
        private async Task<bool> Transfer(string[] sources, string target, bool move)
        {
            if (busy || !initialized) return false;
            busy = true; Notify(move ? L10n.T("ui.moving_check_the_windows_file_operation_dialog") : L10n.T("ui.copying_check_the_windows_file_operation_dialog"), false);
            bool complete = false;
            try { complete = await ShellTransfer.Run(new WindowInteropHelper(this).Handle, sources, Paths.Unc(settings.Distro, target), move); }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; }
            await RefreshFolder(false);
            Notify(complete ? L10n.T("ui.file_operation_complete") : L10n.T("ui.some_operations_did_not_complete_check_the_source_and_destinatio")); return complete;
        }
        private async Task CopySelected(bool move)
        {
            if (busy || !initialized) return;
            try
            {
                string[] paths = SelectedPaths();
                if (paths.Length == 0) return;
                DataObject data = FileData(paths, move);
                data.SetData("WSLBox.ClipboardId", Guid.NewGuid().ToString());
                await WriteClipboard(() => Clipboard.SetDataObject(data, true),
                    (move ? L10n.T("ui.cut") : L10n.T("ui.copied")) + paths.Length + L10n.T("ui.items_95de7c"));
            }
            catch (Exception ex) { Report(ex); }
        }
        private async Task WriteClipboard(Action write, string success)
        {
            int version = ++clipboardWriteVersion;
            const int attempts = 4;
            for (int attempt = 1; attempt <= attempts && version == clipboardWriteVersion && !closing; attempt++)
            {
                try
                {
                    write();
                    Notify(success);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException ex)
                {
                    if (ex.HResult != ClipboardCannotOpen) throw;
                    if (attempt == attempts)
                    {
                        Notify(L10n.T("ui.clipboard_busy"));
                        return;
                    }
                }
                await Task.Delay(60);
            }
        }
        private async Task Paste()
        {
            if (busy || !initialized) return;
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] paths = data.GetData(DataFormats.FileDrop) as string[]; if (paths == null || paths.Length == 0) return;
                var stream = data.GetData("Preferred DropEffect") as MemoryStream;
                byte[] effect = stream == null ? new byte[0] : stream.ToArray();
                bool move = effect.Length >= 4 && (BitConverter.ToInt32(effect, 0) & 2) != 0;
                string token = data.GetData("WSLBox.ClipboardId") as string;
                bool complete = await Transfer(paths, currentFolder, move);
                if (complete && move && token != null)
                {
                    IDataObject current = Clipboard.GetDataObject();
                    if (current != null && (current.GetData("WSLBox.ClipboardId") as string) == token) Clipboard.Clear();
                }
            }
            catch (Exception ex) { Report(ex); }
        }
        private void Reveal()
        {
            if (!initialized) return;
            try { Process.Start("explorer.exe", Paths.Quote(Paths.Unc(settings.Distro, currentFolder))); }
            catch (Exception ex) { Report(ex); }
        }
        internal void PrepareForClose()
        {
            if (closing) return;
            RememberBounds();
            closing = true;
            ++thumbnailGeneration;
            refreshTimer.Stop(); noticeTimer.Stop(); hoverTimer.Stop();
            captionPending = captionMoving = false; captionSnap = PointerSnap.None;
            WindowEffects.DisableTransitions(this);
            // Dispose the overlay before the owner is destroyed. A cached full-screen
            // preview must not remain visible during the owner's teardown/animation.
            if (snapPreview != null)
            {
                SnapPreview preview = snapPreview; snapPreview = null;
                preview.Clear(); preview.Close();
            }
            if (renameEditor != null) renameEditor.Cancel();
        }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (closing) return;
            if (!CanClose) { e.Cancel = true; Notify(L10n.T("ui.wait_for_the_file_operation_to_finish_before_closing")); return; }
            PrepareForClose();
        }
    }
}
