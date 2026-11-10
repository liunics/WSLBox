using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WSLBox
{
    internal sealed class InlineRename
    {
        private readonly Popup popup;
        private readonly TextBox input;
        private readonly TextBlock error;
        private readonly string initial;
        private readonly bool folder;
        private readonly Func<string, Task<string>> commit;
        private readonly Action<bool> finished;
        private bool ended, submitting, cancelled;
        public bool IsCommitting { get { return submitting; } }
        public bool RestoreFocus { get; private set; }
        internal TextBox Input { get { return input; } }
        public InlineRename(FrameworkElement label, string name, bool isFolder, bool icons,
            Func<string, Task<string>> commit, Action<bool> finished)
        {
            initial = name; folder = isFolder; this.commit = commit; this.finished = finished;
            input = new TextBox { Text = name, Background = Brushes.White, Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 140, 225)), BorderThickness = new Thickness(1),
                Padding = new Thickness(2), FontFamily = label.FontFamily(), FontSize = 12,
                TextWrapping = icons ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextAlignment = icons ? TextAlignment.Center : TextAlignment.Left,
                MinWidth = Math.Max(80, label.ActualWidth + 4), MaxWidth = Math.Max(300, label.ActualWidth + 4),
                MinHeight = Math.Max(23, label.ActualHeight + 4), MaxHeight = 150,
                AcceptsReturn = false, AcceptsTab = false };
            error = new TextBlock { Foreground = Brushes.White, Background = Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Padding = new Thickness(5), Visibility = Visibility.Collapsed };
            var body = new StackPanel(); body.Children.Add(input); body.Children.Add(error);
            popup = new Popup { PlacementTarget = label, Placement = PlacementMode.Relative,
                HorizontalOffset = -2, VerticalOffset = -2, AllowsTransparency = true,
                StaysOpen = false, Child = body };
            popup.Closed += async (s,e) => { if (!ended && !submitting) await Submit(); };
            input.PreviewKeyDown += async (s,e) => {
                if (e.Key == Key.Escape) { e.Handled = true; RestoreFocus = true; Cancel(); }
                else if (e.Key == Key.Enter) { e.Handled = true; RestoreFocus = true; await Submit(); }
            };
        }
        public static int SelectionLength(string name, bool folder)
        {
            int dot = folder ? -1 : name.LastIndexOf('.'); return dot > 0 ? dot : name.Length;
        }
        public void Show()
        {
            if (ended) return;
            popup.IsOpen = true;
            input.Dispatcher.BeginInvoke(new Action(() => {
                if (ended || submitting) return;
                input.Focus(); Keyboard.Focus(input); input.Select(0, SelectionLength(initial, folder));
            }), DispatcherPriority.Input);
        }
        public void Cancel()
        {
            cancelled = true; popup.IsOpen = false;
            if (!submitting) Finish(false);
        }
        internal async Task Submit()
        {
            if (ended || submitting) return;
            if (cancelled) { Finish(false); return; }
            string name = input.Text;
            if (name == initial) { Finish(false); return; }
            string message = null;
            if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\0', '\r', '\n' }) >= 0)
                message = L10n.T("ui.a_name_is_required_it_cannot_contain_or_line_breaks");
            else
            {
                submitting = true; input.IsReadOnly = true;
                try { message = await commit(name); }
                catch (Exception ex) { message = ex.Message; }
                finally { submitting = false; input.IsReadOnly = false; }
                if (message == null) { Finish(true); return; }
            }
            if (cancelled) { Finish(false); return; }
            error.Text = message; error.Visibility = Visibility.Visible;
            ScheduleRetry();
        }
        private void ScheduleRetry()
        {
            input.Dispatcher.BeginInvoke(new Action(() => { if (!ended && !cancelled) { popup.IsOpen = true; input.Focus(); Keyboard.Focus(input); } }), DispatcherPriority.Input);
        }
        private void Finish(bool saved)
        {
            if (ended) return; ended = true; popup.IsOpen = false; finished(saved);
        }
    }
    internal static class RenameFont
    {
        public static FontFamily FontFamily(this FrameworkElement label)
        {
            var text = label as TextBlock; return text == null ? new FontFamily("Microsoft YaHei UI") : text.FontFamily;
        }
    }
}
