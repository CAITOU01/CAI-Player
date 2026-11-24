using System;
using NAudio.Dsp;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NAudio.Wave;

namespace CaiPlayer
{
    internal static class EmbeddedAudio
    {
        private static readonly Lazy<Assembly> library = new Lazy<Assembly>(delegate {
            using (var stream = typeof(EmbeddedAudio).Assembly.GetManifestResourceStream("NAudio.dll"))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                return Assembly.Load(bytes.ToArray());
            }
        });

        public static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            return new AssemblyName(args.Name).Name == "NAudio" ? library.Value : null;
        }
    }

    internal sealed class TrackSignal : IDisposable
    {
        private const int Bands = 64, FramesPerSecond = 20, FramesPerBlock = 10;
        private const int FftBits = 12, Window = 1 << FftBits;
        private readonly object gate = new object();
        private int requested = -1;
        private readonly Dictionary<int, float[][]> cache = new Dictionary<int, float[][]>();
        private bool stopped;

        public TrackSignal(Uri source)
        {
            if (!source.IsFile) return;
            var worker = new Thread(delegate() {
                try { Analyze(source.LocalPath); }
                catch (Exception) { /* Visualization is optional for unsupported codecs. */ }
            }) { IsBackground = true, Name = "CAI spectrum" };
            worker.Start();
        }

        public bool TryGetLevels(double seconds, float[] output)
        {
            if (!(seconds >= 0 && seconds < int.MaxValue / FramesPerSecond) || output == null || output.Length != Bands) return false;
            int frame = (int)(seconds * FramesPerSecond);
            lock (gate)
            {
                if (stopped) return false;
                int block = frame / FramesPerBlock;
                requested = block;
                Monitor.Pulse(gate);
                float[][] spectra;
                if (!cache.TryGetValue(block, out spectra)) return false;
                int offset = frame - block * FramesPerBlock;
                if (offset < 0 || offset >= spectra.Length) return false;
                Array.Copy(spectra[offset], output, Bands);
                return true;
            }
        }

        private void Analyze(string path)
        {
            using (var reader = new MediaFoundationReader(path,
                new MediaFoundationReader.MediaFoundationReaderSettings { SingleReaderObject = true }))
            {
                var pcm = reader.ToSampleProvider();
                int rate = pcm.WaveFormat.SampleRate, channels = pcm.WaveFormat.Channels;
                // Two overlapping frames keep the display continuous during block changes.
                const int count = FramesPerBlock + 2;
                var buffer = new float[(rate * count / FramesPerSecond + Window + 2) * channels];
                var fft = new Complex[Window];
                var hann = new float[Window];
                for (int i = 0; i < Window; i++)
                    hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1)));
                var bandOf = new int[Window / 2];
                double top = Math.Min(20000, rate * 0.5);
                for (int k = 1; k < bandOf.Length; k++)
                {
                    double hz = (double)k * rate / Window;
                    bandOf[k] = hz < 20 || hz > top ? -1 : Math.Min(Bands - 1,
                        (int)(Bands * Math.Log((400 + hz) / 420) / Math.Log((400 + top) / 420)));
                }
                while (true)
                {
                    int block;
                    lock (gate)
                    {
                        while (!stopped && (requested < 0 ||
                            (cache.ContainsKey(requested) && cache.ContainsKey(requested + 1)))) Monitor.Wait(gate);
                        if (stopped) return;
                        block = cache.ContainsKey(requested) ? requested + 1 : requested;
                    }
                    long first = (long)block * FramesPerBlock * rate / FramesPerSecond;
                    reader.Position = first * reader.WaveFormat.BlockAlign;
                    int received = 0, read;
                    while (received < buffer.Length && (read = pcm.Read(buffer, received, buffer.Length - received)) > 0)
                        received += read;
                    var frames = new float[count][];
                    for (int f = 0; f < count; f++)
                    {
                        lock (gate) { if (stopped) return; }
                        int start = (int)(((long)block * FramesPerBlock + f) * rate / FramesPerSecond - first);
                        var bands = frames[f] = new float[Bands];
                        for (int c = 0; c < channels; c++)
                        {
                            for (int i = 0; i < Window; i++)
                            {
                                int index = (start + i) * channels + c;
                                fft[i].X = index < received ? buffer[index] * hann[i] : 0;
                                fft[i].Y = 0;
                            }
                            FastFourierTransform.FFT(true, FftBits, fft);
                            for (int k = 1; k < bandOf.Length; k++)
                            {
                                int band = bandOf[k];
                                if (band < 0) continue;
                                float power = fft[k].X * fft[k].X + fft[k].Y * fft[k].Y;
                                bands[band] = Math.Max(bands[band], power);
                            }
                        }
                        for (int b = 0; b < Bands; b++)
                            bands[b] = (float)Math.Max(0, Math.Min(1, 1 + 10 * Math.Log10(Math.Max(1e-12, 16 * bands[b])) / 60));
                    }
                    lock (gate)
                    {
                        if (frames[count - 1] != null)
                        {
                            cache[block] = frames;
                            foreach (int old in new List<int>(cache.Keys))
                                if (old < requested - 1 || old > requested + 2) cache.Remove(old);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (gate) { stopped = true; cache.Clear(); Monitor.PulseAll(gate); }
        }
    }
}
