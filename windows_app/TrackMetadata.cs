using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
                        return Decode(bytes);
                    }
                }
                finally { Marshal.ReleaseComObject(source); }
            }
            finally { PropVariantClear(ref value); Marshal.ReleaseComObject(store); }
        }

        public static Bitmap ReadCover(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var cover = EmbeddedCover(path);
                if (cover != null) return cover;
            }
            catch (Exception) { /* Some optional property handlers cannot decode a particular tag. */ }
            string folder = Path.GetDirectoryName(path);
            foreach (var stem in new[] { Path.GetFileNameWithoutExtension(path), "cover", "folder", "front" })
            {
                foreach (var extension in new[] { ".jpg", ".jpeg", ".png" })
                {
                    var candidate = Path.Combine(folder, stem + extension);
                    if (!File.Exists(candidate)) continue;
                    try
                    {
                        using (var input = File.Open(candidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            if (input.Length <= MaxArtBytes) { var art = Decode(input); if (art != null) return art; }
                    }
                    catch (Exception) { /* Try the next supported local cover. */ }
                }
            }
            return null;
        }
    }
}
