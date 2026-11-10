using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WSLBox
{
    internal static class IconBuilder
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length != 1) throw new ArgumentException("Expected an output ICO path");
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                var visual = new DrawingVisual();
                using (DrawingContext context = visual.RenderOpen())
                    context.DrawImage(Branding.Image, new Rect(0, 0, size, size));
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var memory = new MemoryStream()) { png.Save(memory); images.Add(memory.ToArray()); }
            }
            var preview = new DrawingVisual();
            using (DrawingContext context = preview.RenderOpen()) context.DrawImage(Branding.Image, new Rect(0, 0, 256, 256));
            var previewBitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32); previewBitmap.Render(preview);
            var previewPng = new PngBitmapEncoder(); previewPng.Frames.Add(BitmapFrame.Create(previewBitmap));
            using (var output = File.Create(Path.ChangeExtension(args[0], ".png"))) previewPng.Save(output);
            using (var stream = File.Create(args[0]))
            using (var output = new BinaryWriter(stream))
            {
                output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
                uint offset = (uint)(6 + sizes.Length * 16);
                for (int index = 0; index < sizes.Length; index++)
                {
                    output.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    output.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    output.Write((byte)0); output.Write((byte)0);
                    output.Write((ushort)1); output.Write((ushort)32);
                    output.Write((uint)images[index].Length); output.Write(offset);
                    offset += (uint)images[index].Length;
                }
                foreach (byte[] data in images) output.Write(data);
            }
        }
    }
}
