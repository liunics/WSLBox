using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace WSLBox
{
    internal static class Branding
    {
        public const string Version = "1.1.0";
        public const string AssemblyVersion = "1.1.0.0";
        private static ImageSource image;
        public static ImageSource Image
        {
            get
            {
                if (image != null) return image;
                var drawing = new DrawingGroup();
                using (DrawingContext context = drawing.Open())
                {
                    context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 256, 256));
                    context.PushTransform(new ScaleTransform(8, 8));
                    IconArtwork.Draw(context);
                    context.Pop();

                }
                drawing.Freeze(); var result = new DrawingImage(drawing); result.Freeze(); image = result;
                return image;
            }
        }
        public static System.Drawing.Icon CreateTrayIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WSLBox.AppIcon"))
            {
                if (stream == null) throw new IOException(L10n.T("ui.the_embedded_application_icon_is_missing_download_the_complete_r"));
                using (var icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32)))
                    return (System.Drawing.Icon)icon.Clone();
            }
        }
    }
}
