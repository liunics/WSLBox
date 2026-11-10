using System;
using System.Windows;
using System.Runtime.Serialization;

namespace WSLBox
{
    [DataContract]
    public sealed class SavedWindowBounds
    {
        [DataMember] public double Left;
        [DataMember] public double Top;
        [DataMember] public double Width;
        [DataMember] public double Height;
        public bool IsValid { get { return Finite(Left) && Finite(Top) && Finite(Width) && Finite(Height) && Width >= 280 && Height >= 180; } }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public Rect Rectangle { get { return new Rect(Left, Top, Width, Height); } }
        public static SavedWindowBounds From(Rect value)
        { return new SavedWindowBounds { Left = value.Left, Top = value.Top, Width = value.Width, Height = value.Height }; }
    }
    internal enum PointerSnap { None, Maximize, Left, Right }
    internal static class WindowGeometry
    {
        // All inputs are WPF device-independent coordinates. During a drag the
        // pointer stays attached to the same part of the restored caption.
        public static Rect Restore(Rect remembered, Rect work, Point? pointer, double ratio, double captionOffset)
        {
            // A valid saved window must retain its exact dimensions. Only
            // recover a smaller default when it no longer fits this monitor.
            double width = remembered.Width > work.Width ? Math.Min(460, work.Width) : remembered.Width;
            double height = remembered.Height > work.Height ? Math.Min(560, work.Height) : remembered.Height;
            width = Math.Max(280, width);
            height = Math.Max(180, height);
            double x = remembered.Left, y = remembered.Top;
            if (pointer.HasValue)
            {
                x = pointer.Value.X - width * Math.Max(0, Math.Min(1, ratio));
                y = pointer.Value.Y - Math.Max(0, Math.Min(37, captionOffset));
                // Keep the caption reachable, but don't push the window up just
                // because its bottom temporarily extends off-screen while moving.
                x = Math.Max(work.Left - width + 60, Math.Min(x, work.Right - 60));
                y = Math.Max(work.Top, Math.Min(y, work.Bottom - 34));
            }
            else
            {
                x = Math.Max(work.Left, Math.Min(x, work.Right - width));
                y = Math.Max(work.Top, Math.Min(y, work.Bottom - height));
            }
            return new Rect(x, y, width, height);
        }
        public static Point Magnet(Point origin, Size size, Rect work, double threshold = 12)
        {
            double x = origin.X, y = origin.Y;
            double left = Math.Abs(x - work.Left), right = Math.Abs(x + size.Width - work.Right);
            double top = Math.Abs(y - work.Top), bottom = Math.Abs(y + size.Height - work.Bottom);
            if (Math.Min(left, right) <= threshold) x = left <= right ? work.Left : work.Right - size.Width;
            if (Math.Min(top, bottom) <= threshold) y = top <= bottom ? work.Top : work.Bottom - size.Height;
            return new Point(x, y);
        }
        public static Point Magnet(Point origin, Size size, Rect work, Rect[] peers, double threshold = 12)
        {
            Point edge = Magnet(origin, size, work, threshold);
            double x = edge.X, y = edge.Y;
            double bestX = Math.Min(Math.Abs(origin.X - work.Left), Math.Abs(origin.X + size.Width - work.Right));
            double bestY = Math.Min(Math.Abs(origin.Y - work.Top), Math.Abs(origin.Y + size.Height - work.Bottom));
            if (bestX > threshold) bestX = threshold + 1;
            if (bestY > threshold) bestY = threshold + 1;
            foreach (Rect peer in peers)
            {
                if (peer.IsEmpty || peer.Width <= 0 || peer.Height <= 0) continue;
                bool verticalOverlap = origin.Y <= peer.Bottom + threshold && origin.Y + size.Height >= peer.Top - threshold;
                if (verticalOverlap)
                {
                    Consider(origin.X, peer.Left - size.Width, threshold, ref x, ref bestX);
                    Consider(origin.X, peer.Right, threshold, ref x, ref bestX);
                }
                bool verticalContact = Math.Abs(origin.Y + size.Height - peer.Top) <= threshold || Math.Abs(origin.Y - peer.Bottom) <= threshold;
                if (verticalContact)
                {
                    Consider(origin.X, peer.Left, threshold, ref x, ref bestX);
                    Consider(origin.X, peer.Right - size.Width, threshold, ref x, ref bestX);
                }
            }
            // Resolve Y against the final X, so another peer cannot invalidate
            // an earlier choice merely by appearing later in the array.
            foreach (Rect peer in peers)
            {
                if (peer.IsEmpty || peer.Width <= 0 || peer.Height <= 0) continue;
                bool horizontalOverlap = x <= peer.Right + threshold && x + size.Width >= peer.Left - threshold;
                if (horizontalOverlap)
                {
                    Consider(origin.Y, peer.Top - size.Height, threshold, ref y, ref bestY);
                    Consider(origin.Y, peer.Bottom, threshold, ref y, ref bestY);
                }
                bool horizontalContact = Math.Abs(x + size.Width - peer.Left) <= threshold || Math.Abs(x - peer.Right) <= threshold;
                if (horizontalContact)
                {
                    Consider(origin.Y, peer.Top, threshold, ref y, ref bestY);
                    Consider(origin.Y, peer.Bottom - size.Height, threshold, ref y, ref bestY);
                }
            }
            return new Point(x, y);
        }
        private static void Consider(double original, double target, double threshold, ref double value, ref double nearest)
        {
            double distance = Math.Abs(original - target);
            if (distance <= threshold && distance < nearest) { value = target; nearest = distance; }
        }
        public static PointerSnap DetectPointerSnap(Point pointer, Rect screen, double threshold = 1)
        {
            if (pointer.Y <= screen.Top + threshold) return PointerSnap.Maximize;
            if (pointer.X <= screen.Left + threshold) return PointerSnap.Left;
            if (pointer.X >= screen.Right - threshold) return PointerSnap.Right;
            return PointerSnap.None;
        }
        public static Rect Half(Rect work, bool right)
        {
            double half = work.Width / 2;
            return new Rect(right ? work.Left + half : work.Left, work.Top, half, work.Height);
        }
    }
}
