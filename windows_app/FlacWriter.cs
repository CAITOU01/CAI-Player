using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;

namespace CaiPlayer
{
    internal static class FlacWriter
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr library, string name);
        [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr library);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Delete(IntPtr encoder);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Set(IntPtr encoder, uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init(IntPtr encoder, byte[] path, IntPtr progress, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Write(IntPtr encoder, int[] samples, uint frames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Finish(IntPtr encoder);

        private static T Function<T>(IntPtr library, string name) where T : class
        {
            IntPtr proc = GetProcAddress(library, "FLAC__stream_encoder_" + name);
            if (proc == IntPtr.Zero) throw new Win32Exception();
            return (T)(object)Marshal.GetDelegateForFunctionPointer(proc, typeof(T));
        }

        internal static void Encode(IWaveProvider pcm, string path)
        {
            var fmt = pcm.WaveFormat;
            if (fmt.BitsPerSample != 16 || fmt.Encoding != WaveFormatEncoding.Pcm) throw new IOException("需要 16 位元 PCM 音軌。");
            string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, IntPtr.Size == 8 ? "libFLAC-x64.dll" : "libFLAC-x86.dll");
            IntPtr library = LoadLibraryEx(dll, IntPtr.Zero, 0x900);
            if (library == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var release = Function<Delete>(library, "delete");
                var finish = Function<Finish>(library, "finish");
                var write = Function<Write>(library, "process_interleaved");
                IntPtr encoder = Function<Create>(library, "new")();
                if (encoder == IntPtr.Zero) throw new IOException("無法建立 FLAC 編碼器。");
                try
                {
                    if (Function<Set>(library, "set_channels")(encoder, (uint)fmt.Channels) == 0 ||
                        Function<Set>(library, "set_bits_per_sample")(encoder, 16) == 0 ||
                        Function<Set>(library, "set_sample_rate")(encoder, (uint)fmt.SampleRate) == 0 ||
                        Function<Init>(library, "init_file")(encoder, Encoding.UTF8.GetBytes(path + "\0"), IntPtr.Zero, IntPtr.Zero) != 0)
                        throw new IOException("無法建立 FLAC 檔案。");
                    var bytes = new byte[4096 * fmt.BlockAlign];
                    var samples = new int[bytes.Length / 2];
                    int count;
                    while ((count = pcm.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        if (count % fmt.BlockAlign != 0) throw new IOException("音軌資料不完整。");
                        for (int i = 0; i < count / 2; i++) samples[i] = BitConverter.ToInt16(bytes, 2 * i);
                        if (write(encoder, samples, (uint)(count / fmt.BlockAlign)) == 0) throw new IOException("無法寫入 FLAC 音軌。");
                    }
                    if (finish(encoder) == 0) throw new IOException("FLAC 檔案寫入失敗。");
                }
                finally { release(encoder); }
            }
            finally { FreeLibrary(library); }
        }
    }
}
