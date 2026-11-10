using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WSLBox
{
    internal sealed class SnapPreview : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        public SnapPreview(Window owner)
        {
            Owner = owner; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; ShowActivated = false; Focusable = false;
            IsHitTestVisible = false;
            Content = CreateVisual();
            SourceInitialized += (s, e) => {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x80 | 0x08000000);
                WindowEffects.DisableTransitions(this);
            };
        }
        internal static Border CreateVisual()
        {
            return new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 132, 191, 255)),
                Background = new SolidColorBrush(Color.FromArgb(37, 92, 157, 246)),
                IsHitTestVisible = false };
        }
        public void ShowTarget(Rect target)
        {
            const double inset = 5;
            Rect area = new Rect(target.Left + inset, target.Top + inset,
                Math.Max(1, target.Width - inset * 2), Math.Max(1, target.Height - inset * 2));
            bool changed = !IsVisible || Math.Abs(Left - area.Left) > 1 || Math.Abs(Top - area.Top) > 1
                || Math.Abs(Width - area.Width) > 1 || Math.Abs(Height - area.Height) > 1;
            Left = area.Left; Top = area.Top; Width = area.Width; Height = area.Height;
            if (!IsVisible) Show();
            if (changed) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));
        }
        public void Clear()
        {
            BeginAnimation(OpacityProperty, null);
            if (IsVisible) Hide();
        }
    }
}
