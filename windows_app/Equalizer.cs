using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CaiPlayer
{
    internal sealed class EqualizerSource : ISampleProvider, IDisposable
    {
        internal static readonly int[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
        private readonly AudioFileReader reader;
        private readonly object gate = new object();
        private ISampleProvider source;
        private BiQuadFilter[][] filters;
        private double speed = 1;
        private int[] gains;
        public WaveFormat WaveFormat { get { return reader.WaveFormat; } }
        internal TimeSpan Duration { get { return reader.TotalTime; } }
        internal TimeSpan Position {
            get { lock (gate) return reader.CurrentTime; }
            set { lock (gate) { reader.CurrentTime = value; SetSpeed(speed); SetGains(gains); } }
        }
        internal EqualizerSource(string path, int[] gains, double speed)
        {
            reader = new AudioFileReader(path);
            try { SetGains(gains); SetSpeed(speed); } catch { reader.Dispose(); throw; }
        }
        internal void SetGains(int[] values)
        {
            lock (gate)
            {
                gains = (int[])values.Clone();
                filters = Enumerable.Range(0, WaveFormat.Channels).Select(channel =>
                    Frequencies.Select((hz, i) => hz < WaveFormat.SampleRate / 2 && gains[i] != 0
                        ? BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, hz, 1.4f, gains[i]) : null)
                    .Where(filter => filter != null).ToArray()).ToArray();
            }
        }
        internal void SetSpeed(double value)
        {
            lock (gate)
            {
                speed = value;
                // Output stays at the device sample rate. Resampling changes how much source time each block consumes.
                source = value == 1 ? (ISampleProvider)reader : new WdlResamplingSampleProvider(reader, (int)(WaveFormat.SampleRate / value));
            }
        }
        public int Read(float[] samples, int offset, int count)
        {
            lock (gate)
            {
                int read = source.Read(samples, offset, count);
                for (int i = 0; i < read; i++)
                {
                    float sample = samples[offset + i];
                    foreach (var filter in filters[i % filters.Length]) sample = filter.Transform(sample);
                    samples[offset + i] = Math.Min(1, Math.Max(-1, sample));
                }
                return read;
            }
        }
        public void Dispose() { lock (gate) reader.Dispose(); }
    }

    internal sealed partial class PlayerWindow
    {
        private int[] gains = new int[10];
        private EqualizerSource equalized;
        private WaveOutEvent equalizerOutput;
        private double openedPosition;

        private double Position {
            get { return equalized != null ? equalized.Position.TotalSeconds : media == null ? 0 : media.Position.TotalSeconds; }
            set { var time = TimeSpan.FromSeconds(value); if (equalized != null) equalized.Position = time; else if (media != null) media.Position = time; }
        }

        private void EqualizerDialog()
        {
            using (var d = new Form { Text = "等化器 · 本機音樂", Font = Font, StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(565, 275), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                var levels = new TrackBar[10];
                var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 10, RowCount = 4 };
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                d.Controls.Add(table);
                for (int i = 0; i < levels.Length; i++) {
                    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));
                    string hz = EqualizerSource.Frequencies[i] >= 1000 ? (EqualizerSource.Frequencies[i] / 1000) + "k" : EqualizerSource.Frequencies[i].ToString();
                    var value = new Label { AutoSize = true, Text = gains[i] + " dB" };
                    var bar = levels[i] = new TrackBar { AccessibleName = hz + " Hz", Orientation = Orientation.Vertical, Minimum = -12,
                        Maximum = 12, TickFrequency = 3, Value = gains[i], Dock = DockStyle.Fill };
                    bar.ValueChanged += delegate { value.Text = bar.Value + " dB"; };
                    table.Controls.Add(Caption(hz + " Hz", 9, false), i, 0); table.Controls.Add(bar, i, 1); table.Controls.Add(value, i, 2);
                }
                var row = Row(); table.Controls.Add(row, 0, 3); table.SetColumnSpan(row, 10);
                row.Controls.Add(ActionButton("平坦", () => { foreach (var level in levels) level.Value = 0; }));
                row.Controls.Add(ActionButton("套用", () => { d.DialogResult = DialogResult.OK; }, true));
                row.Controls.Add(ActionButton("取消", () => { d.DialogResult = DialogResult.Cancel; }));
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var selected = levels.Select(bar => bar.Value).ToArray();
                try { Registry.SetValue(MusicFolders.Settings, "Equalizer", selected.Select(v => (byte)(v + 12)).ToArray(), RegistryValueKind.Binary); }
                catch (Exception e) { ShowError("無法儲存等化器", e); return; }
                gains = selected;
                if (equalized != null && gains.Any(v => v != 0)) equalized.SetGains(gains);
                else if (active != null && CanEqualize(LocalSource(active)) && (equalized != null || gains.Any(v => v != 0)))
                    StartAt(active, Position, playing);
            }
        }

        private static bool CanEqualize(string path)
        {
            return !String.IsNullOrEmpty(path) && MusicFolders.Extensions.Split(';')
                .Any(ext => String.Equals(ext.Substring(1), Path.GetExtension(path), StringComparison.OrdinalIgnoreCase));
        }

        private void StartEqualized(Uri uri, double position, bool play)
        {
            equalized = new EqualizerSource(uri.LocalPath, gains, playbackSpeed);
            equalized.Position = TimeSpan.FromSeconds(position);
            equalizerOutput = new WaveOutEvent { DesiredLatency = 100, Volume = volume.Value / 100F };
            var output = equalizerOutput;
            output.PlaybackStopped += (sender, args) => {
                if (equalizerOutput != output || IsDisposed) return;
                if (args.Exception != null) PlaybackError(args.Exception); else Advance();
            };
            output.Init(equalized);
            duration = equalized.Duration.TotalSeconds; ready = true; playing = play;
            progress.Enabled = duration > 0; UpdateKnownDuration(active, duration); RecordRecent(uri);
            state.Text = play ? "播放中" : "已暫停";
            if (play) output.Play();
        }
    }
}
