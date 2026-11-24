using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CaiPlayer
{
    internal static class TrackMetadata
    {
        // Windows property handlers expose the embedded album art as an image
        // stream. This avoids mistaking Explorer's generic file icon for art.
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
            public PropertyKey(string format, uint id) { Format = new Guid(format); Id = id; }
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public ulong Number;
            [FieldOffset(8)] public IntPtr Pointer;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext,
            uint flags, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        private const int MaxArtBytes = 24 * 1024 * 1024;

        private static IPropertyStore Open(string path)
        {
            IPropertyStore store;
            var id = typeof(IPropertyStore).GUID;
            return SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 1, ref id, out store) >= 0 ? store : null;
        }

        public static double ReadSeconds(string path)
        {
            if (!File.Exists(path)) return 0;
            try
            {
                double tagged = PropertySeconds(path);
                if (tagged > 0) return tagged;
            }
            catch (COMException) { /* Try the audio decoder when the shell has no usable handler. */ }
            using (var reader = new NAudio.Wave.MediaFoundationReader(path))
                return reader.TotalTime.TotalSeconds;
        }

        private static double PropertySeconds(string path)
        {
            var store = Open(path);
            if (store == null) return 0;
            var value = new PropVariant();
            try
            {
                var key = new PropertyKey("64440490-4C8B-11D1-8B70-080036B11A03", 3);
                if (store.GetValue(ref key, out value) < 0 || value.Type != 21) return 0;
                return value.Number / (double)TimeSpan.TicksPerSecond;
            }
            finally { PropVariantClear(ref value); Marshal.ReleaseComObject(store); }
        }

        private static Bitmap Decode(Stream stream)
        {
            using (var original = Image.FromStream(stream, false, false))
            {
                if ((long)original.Width * original.Height > 64L * 1024 * 1024) return null;
                double scale = Math.Min(1, 800.0 / Math.Max(original.Width, original.Height));
                var image = new Bitmap(Math.Max(1, (int)(original.Width * scale)),
                    Math.Max(1, (int)(original.Height * scale)), PixelFormat.Format32bppPArgb);
                try
                {
                    using (var drawing = Graphics.FromImage(image))
                    {
                        drawing.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        drawing.DrawImage(original, new Rectangle(Point.Empty, image.Size));
                    }
                    return image;
                }
                catch { image.Dispose(); throw; }
            }
        }

        private static Bitmap EmbeddedCover(string path)
        {
            var store = Open(path);
            if (store == null) return null;
            var value = new PropVariant();
            try
            {
                var key = new PropertyKey("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 27);
                if (store.GetValue(ref key, out value) < 0 || value.Type != 66 || value.Pointer == IntPtr.Zero) return null;
                var source = (IStream)Marshal.GetObjectForIUnknown(value.Pointer);
                try
                {
                    System.Runtime.InteropServices.ComTypes.STATSTG stat;
                    source.Stat(out stat, 1);
                    if (stat.cbSize <= 0 || stat.cbSize > MaxArtBytes) return null;
                    using (var bytes = new MemoryStream((int)stat.cbSize))
                    {
                        var buffer = new byte[32768];
                        var count = Marshal.AllocHGlobal(4);
                        try
                        {
                            source.Seek(0, 0, IntPtr.Zero);
                            while (bytes.Length < stat.cbSize)
                            {
                                int wanted = (int)Math.Min(buffer.Length, stat.cbSize - bytes.Length);
                                source.Read(buffer, wanted, count);
                                int read = Marshal.ReadInt32(count);
                                if (read <= 0 || read > wanted) return null;
                                bytes.Write(buffer, 0, read);
                            }
                        }
                        finally { Marshal.FreeHGlobal(count); }
                        bytes.Position = 0;
                        return DecodeCover(bytes);
                    }
                }
                finally { Marshal.ReleaseComObject(source); }
            }
            finally { PropVariantClear(ref value); Marshal.ReleaseComObject(store); }
        }

        private static class WebP32
        {
            [DllImport("webpdecoder-x86.dll", CallingConvention = CallingConvention.Cdecl)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
            internal static extern int WebPGetInfo(byte[] data, UIntPtr count, out int width, out int height);
            [DllImport("webpdecoder-x86.dll", CallingConvention = CallingConvention.Cdecl)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
            internal static extern IntPtr WebPDecodeBGRAInto(byte[] data, UIntPtr count, IntPtr dest, UIntPtr size, int stride);
        }

        private static class WebP64
        {
            [DllImport("webpdecoder-x64.dll", CallingConvention = CallingConvention.Cdecl)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
            internal static extern int WebPGetInfo(byte[] data, UIntPtr count, out int width, out int height);
            [DllImport("webpdecoder-x64.dll", CallingConvention = CallingConvention.Cdecl)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
            internal static extern IntPtr WebPDecodeBGRAInto(byte[] data, UIntPtr count, IntPtr dest, UIntPtr size, int stride);
        }

        private static Bitmap ReadWebP(Stream input)
        {
            byte[] bytes;
            using (var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true))
                bytes = reader.ReadBytes(checked((int)input.Length));
            var count = (UIntPtr)(uint)bytes.Length;
            int width, height;
            int valid = IntPtr.Size == 8 ? WebP64.WebPGetInfo(bytes, count, out width, out height)
                : WebP32.WebPGetInfo(bytes, count, out width, out height);
            if (valid == 0 || width <= 0 || height <= 0 || (long)width * height > 32L * 1024 * 1024) return null;
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                try
                {
                    var size = (UIntPtr)(uint)(data.Stride * height);
                    var result = IntPtr.Size == 8 ? WebP64.WebPDecodeBGRAInto(bytes, count, data.Scan0, size, data.Stride)
                        : WebP32.WebPDecodeBGRAInto(bytes, count, data.Scan0, size, data.Stride);
                    if (result == IntPtr.Zero) return null;
                }
                finally { bitmap.UnlockBits(data); }
                double scale = Math.Min(1, 800.0 / Math.Max(width, height));
                return new Bitmap(bitmap, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
            }
        }

        private static Bitmap DecodeCover(Stream input)
        {
            var header = new byte[12];
            int read = input.Read(header, 0, header.Length);
            input.Position = 0;
            if (read == 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" &&
                System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP")
            {
                var image = ReadWebP(input);
                if (image != null) return image;
                input.Position = 0;
            }
            try { return Decode(input); }
            catch (Exception)
            {
                // Try additional image decoders installed with Windows.
                input.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 800;
                bitmap.StreamSource = input;
                bitmap.EndInit();
                if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 64L * 1024 * 1024) return null;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var png = new MemoryStream()) { encoder.Save(png); png.Position = 0; return Decode(png); }
            }
        }

        private static Bitmap TaggedCover(string path, out string album)
        {
            album = null;
            using (var file = TagLib.File.Create(path, TagLib.ReadStyle.None))
            {
                album = file.Tag.Album;
                foreach (var picture in file.Tag.Pictures.OrderByDescending(p => p.Type == TagLib.PictureType.FrontCover))
                {
                    try
                    {
                        var data = picture.Data;
                        if (data == null || data.Count == 0 || data.Count > MaxArtBytes) continue;
                        using (var input = new MemoryStream(data.Data, false))
                        {
                            var decoded = DecodeCover(input);
                            if (decoded != null) return decoded;
                        }
                    }
                    catch (Exception) { /* Try other pictures, even if the front cover is damaged. */ }
                }
            }
            return null;
        }

        public static Bitmap ReadCover(string path)
        {
            if (!File.Exists(path)) return null;
            string album = null;
            try { var tagged = TaggedCover(path, out album); if (tagged != null) return tagged; }
            catch (Exception) { /* An untagged or unsupported container may still have a shell thumbnail. */ }
            try
            {
                var cover = EmbeddedCover(path);
                if (cover != null) return cover;
            }
            catch (Exception) { /* Some optional property handlers cannot decode a particular tag. */ }
            string folder = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            foreach (var stem in new[] { name, Path.GetFileName(path), name + ".cover", album, "cover", "folder", "front", "album", "albumart", "albumartsmall" })
            {
                if (String.IsNullOrWhiteSpace(stem) || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp", ".jfif", ".bmp", ".gif" })
                {
                    var candidate = Path.Combine(folder, stem + extension);
                    if (!File.Exists(candidate)) continue;
                    try
                    {
                        using (var input = File.Open(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            if (input.Length > 0 && input.Length <= MaxArtBytes) { var art = DecodeCover(input); if (art != null) return art; }
                    }
                    catch (Exception) { /* Try the next supported local cover. */ }
                }
            }
            return null;
        }
    }
}
