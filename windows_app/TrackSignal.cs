using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using NAudio.CoreAudioApi;
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

    // The chart shows peaks in consecutive 40 ms intervals, oldest on the left.
    // The file reader stays separate from playback so scrubbing and volume changes
    // cannot interrupt the audio. Streams use only this process's output session.
    internal sealed class TrackSignal : IDisposable
    {
        private const int BinsPerSecond = 25, History = 64, Block = 100;
        private readonly object gate = new object();
        private readonly Uri source;
        private int requested = -1, start;
        private float[] cache;
        private bool stopped, liveMode;

        public TrackSignal(Uri source)
        {
            this.source = source;
            var worker = new Thread(Run) { IsBackground = true, Name = "CAI audio levels" };
            worker.Start();
        }

        public bool TryGetLevels(double seconds, float[] output)
        {
            if (output == null || output.Length == 0 || !(seconds >= 0 && seconds < int.MaxValue / BinsPerSecond)) return false;
            int last = (int)(seconds * BinsPerSecond);
            lock (gate)
            {
                if (stopped) return false;
                if (last != requested) { requested = last; Monitor.Pulse(gate); }
                // The session meter is sampled asynchronously after this UI tick.
                if (liveMode && cache != null && last >= start + cache.Length && last < start + cache.Length + 6)
                    last = start + cache.Length - 1;
                if (cache == null || last < start || last >= start + cache.Length) return false;
                int first = last + 1 - output.Length;
                for (int i = 0; i < output.Length; i++)
                {
                    int j = first + i - start;
                    output[i] = j < 0 ? 0 : cache[j];
                }
                return true;
            }
        }

        private void Run()
        {
            if (source.IsFile)
            {
                try { AnalyzeFile(); return; }
                catch (Exception) { /* The playback engine may support additional codecs. */ }
            }
            try { MonitorOutput(); } catch (Exception) { /* Meter availability never affects playback. */ }
        }

        private void AnalyzeFile()
        {
            using (var reader = new MediaFoundationReader(source.LocalPath,
                new MediaFoundationReader.MediaFoundationReaderSettings { SingleReaderObject = true }))
            {
                var pcm = reader.ToSampleProvider();
                int frames = Math.Max(1, reader.WaveFormat.SampleRate / BinsPerSecond);
                var samples = new float[checked(frames * reader.WaveFormat.Channels)];
                int loadedBlock = -1;
                while (true)
                {
                    int block;
                    lock (gate)
                    {
                        while (!stopped && (requested < 0 || requested / Block == loadedBlock)) Monitor.Wait(gate);
                        if (stopped) return;
                        block = requested / Block;
                    }
                    int first = Math.Max(0, block * Block - History);
                    var peaks = new float[(block + 1) * Block + 12 - first];
                    reader.Position = (long)first * frames * reader.WaveFormat.BlockAlign;
                    for (int i = 0; i < peaks.Length; i++)
                    {
                        lock (gate) { if (stopped) return; if (requested / Block != block) break; }
                        int count = pcm.Read(samples, 0, samples.Length);
                        if (count == 0) break;
                        float peak = 0;
                        for (int j = 0; j < count; j++) peak = Math.Max(peak, Math.Abs(samples[j]));
                        peaks[i] = Math.Min(1, peak);
                    }
                    lock (gate)
                    {
                        if (requested / Block == block) { start = first; cache = peaks; loadedBlock = block; }
                        else loadedBlock = -1;
                    }
                }
            }
        }

        private static AudioSessionControl FindSession(MMDeviceEnumerator devices, int pid)
        {
            var endpoints = devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in endpoints)
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == pid && session.AudioMeterInformation != null &&
                        session.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) return session;
                    session.Dispose();
                }
            }
            return null;
        }

        private void MonitorOutput()
        {
            lock (gate) { liveMode = true; cache = null; }
            using (var devices = new MMDeviceEnumerator())
            using (var process = Process.GetCurrentProcess())
            {
                AudioSessionControl session = null;
                int last = -1;
                var retry = Stopwatch.StartNew();
                try
                {
                    while (true)
                    {
                        int point;
                        lock (gate)
                        {
                            while (!stopped && requested == last) Monitor.Wait(gate);
                            if (stopped) return;
                            point = requested;
                        }
                        try
                        {
                            if (session == null && (last < 0 || retry.ElapsedMilliseconds >= 500))
                            {
                                retry.Restart();
                                session = FindSession(devices, process.Id);
                            }
                            if (session != null && session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                            {
                                session.Dispose(); session = null;
                            }
                            if (session != null)
                            {
                                float peak = session.AudioMeterInformation.MasterPeakValue;
                                lock (gate)
                                {
                                    int step = point - last;
                                    if (cache == null || step <= 0 || step >= History) cache = new float[History];
                                    else
                                    {
                                        Array.Copy(cache, step, cache, 0, History - step);
                                        Array.Clear(cache, History - step, step);
                                    }
                                    start = point - History + 1;
                                    cache[History - 1] = peak;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            if (session != null) session.Dispose();
                            session = null;
                        }
                        last = point;
                    }
                }
                finally { if (session != null) session.Dispose(); }
            }
        }

        public void Dispose()
        {
            lock (gate) { stopped = true; cache = null; Monitor.PulseAll(gate); }
        }
    }
}
