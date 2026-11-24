using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using NAudio.Wave;

namespace CaiPlayer
{
    internal sealed class DiscTrack
    {
        internal int Number, FirstSector, Sectors;
        public override string ToString() { return String.Format("曲目 {0:00}    {1}:{2:00}", Number, Sectors / 4500, Sectors / 75 % 60); }
    }

    // Audio CDs contain 75 sectors per second, each holding 2352 bytes of stereo PCM.
    internal sealed class AudioCdStream : WaveStream
    {
        private const int BytesPerSector = 2352;
        private readonly WaveFormat format = new WaveFormat(44100, 16, 2);
        private readonly SafeFileHandle drive;
        private readonly DiscTrack track;
        private long position;
        private readonly byte[] raw = new byte[16 * BytesPerSector], info = new byte[16];

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle drive, uint code, byte[] input, int inputSize,
            [Out] byte[] output, int outputSize, out int returned, IntPtr overlapped);

        private static SafeFileHandle OpenDrive(string root)
        {
            var device = new DriveInfo(root);
            if (device.DriveType != DriveType.CDRom) throw new IOException("請選擇光碟機。");
            var handle = CreateFile(@"\\.\" + device.Name.Substring(0, 2), 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) { var error = new Win32Exception(); handle.Dispose(); throw error; }
            return handle;
        }

        internal static List<DiscTrack> ReadTracks(string root)
        {
            using (var handle = OpenDrive(root))
            {
                var toc = new byte[804]; int length;
                if (!DeviceIoControl(handle, 0x24000, null, 0, toc, toc.Length, out length, IntPtr.Zero)) throw new Win32Exception();
                return ParseTracks(toc, length);
            }
        }

        internal static List<DiscTrack> ParseTracks(byte[] toc, int length)
        {
            if (length < 4) throw new IOException("光碟目錄無法讀取。");
            int count = toc[3] - toc[2] + 1;
            if (count < 1 || count > 99 || 4 + (count + 1) * 8 > length) throw new IOException("光碟目錄不完整。");
            var tracks = new List<DiscTrack>();
            for (int i = 0; i < count; i++)
            {
                int p = 4 + i * 8;
                if ((toc[p + 1] & 4) != 0) continue;
                int first = Sector(toc, p), end = Sector(toc, p + 8);
                if (first >= 0 && end > first) tracks.Add(new DiscTrack { Number = toc[p + 2], FirstSector = first, Sectors = end - first });
            }
            return tracks;
        }

        private static int Sector(byte[] toc, int p) { return (toc[p + 5] * 60 + toc[p + 6]) * 75 + toc[p + 7] - 150; }
        internal AudioCdStream(string root, DiscTrack track)
        {
            this.track = track; drive = OpenDrive(root);
            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, info, 12, 4); // TRACK_MODE_TYPE.CDDA
        }
        public override WaveFormat WaveFormat { get { return format; } }
        public override long Length { get { return (long)track.Sectors * BytesPerSector; } }
        public override long Position { get { return position; } set { position = Math.Max(0, Math.Min(Length, value)); } }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = (int)Math.Min(count, Length - position), copied = 0;
            while (copied < total)
            {
                int within = (int)(position % BytesPerSector);
                int take = Math.Min(raw.Length - within, total - copied);
                int sectors = (within + take + BytesPerSector - 1) / BytesPerSector;
                long diskOffset = (track.FirstSector + position / BytesPerSector) * 2048L;
                Buffer.BlockCopy(BitConverter.GetBytes(diskOffset), 0, info, 0, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(sectors), 0, info, 8, 4);
                int returned;
                if (!DeviceIoControl(drive, 0x2403E, info, info.Length, raw, sectors * BytesPerSector, out returned, IntPtr.Zero)) throw new Win32Exception();
                if (returned < within + take) throw new EndOfStreamException("光碟音軌讀取不完整。");
                Buffer.BlockCopy(raw, within, buffer, offset + copied, take); copied += take; position += take;
            }
            return total;
        }
        protected override void Dispose(bool disposing) { if (disposing) drive.Dispose(); base.Dispose(disposing); }
    }

    internal sealed partial class PlayerWindow
    {
        private static void WriteDiscAudio(IWaveProvider pcm, string path, int format)
        {
            if (format == 1) { WaveFileWriter.CreateWaveFile(path, pcm); return; }
            if (format == 5) { FlacWriter.Encode(pcm, path); return; }
            NAudio.MediaFoundation.MediaFoundationApi.Startup();
            switch (format)
            {
                case 2: MediaFoundationEncoder.EncodeToMp3(pcm, path, 192000); return;
                case 3: MediaFoundationEncoder.EncodeToAac(pcm, path, 192000); return;
                case 4: MediaFoundationEncoder.EncodeToWma(pcm, path, 192000); return;
            }
            var subtype = new Guid("00006c61-0000-0010-8000-00aa00389b71"); // MFAudioFormat_ALAC
            var type = MediaFoundationEncoder.SelectMediaType(subtype, pcm.WaveFormat, pcm.WaveFormat.AverageBytesPerSecond * 8);
            if (type == null) throw new IOException("此電腦缺少所選格式的編碼器。");
            using (var encoder = new MediaFoundationEncoder(type)) encoder.Encode(path, pcm);
        }

        private void OpenDisc()
        {
            using (var dialog = new Form { Text = "音樂 CD", Font = Font, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(470, 340) })
            {
                var drives = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "光碟機" };
                var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "音軌" };
                var status = new Label { Dock = DockStyle.Bottom, Height = 38 };
                var row = Row(); row.Dock = DockStyle.Bottom; row.Height = 46;
                dialog.Padding = new Padding(12);
                dialog.Controls.Add(list); dialog.Controls.Add(drives); dialog.Controls.Add(row); dialog.Controls.Add(status);
                WaveOutEvent output = null; AudioCdStream stream = null; DiscTrack activeDisc = null;
                bool ripping = false;
                Action stop = () => { if (output != null) { output.Stop(); output.Dispose(); output = null; }
                    if (stream != null) { stream.Dispose(); stream = null; } activeDisc = null; };
                Action read = () => {
                    stop(); list.Items.Clear();
                    if (drives.SelectedItem == null) { status.Text = "找不到光碟機。接上光碟機後重新開啟此視窗。"; return; }
                    try {
                        list.Items.AddRange(AudioCdStream.ReadTracks((string)drives.SelectedItem).ToArray());
                        if (list.Items.Count > 0) list.SelectedIndex = 0;
                        status.Text = list.Items.Count + " 個音軌";
                    } catch (Exception e) { status.Text = "請放入音樂 CD：" + e.Message; }
                };
                Action play = () => {
                    var selected = list.SelectedItem as DiscTrack; if (selected == null || ripping) return;
                    try {
                        if (playing) Toggle();
                        if (activeDisc != selected || output == null || output.PlaybackState == PlaybackState.Stopped) {
                            stop(); stream = new AudioCdStream((string)drives.SelectedItem, selected);
                            output = new WaveOutEvent { Volume = volume.Value / 100F };
                            output.Init(stream); activeDisc = selected;
                        }
                        output.Play(); status.Text = "播放中 · " + selected;
                    } catch (Exception e) { stop(); status.Text = e.Message; }
                };
                row.Controls.Add(IconButton("\uE768", "播放音軌", play));
                row.Controls.Add(IconButton("\uE769", "暫停音軌", () => { if (output != null) { output.Pause(); status.Text = "已暫停"; } }));
                row.Controls.Add(IconButton("\uE71A", "停止音軌", () => { stop(); status.Text = "已停止"; }));
                row.Controls.Add(ActionButton("重新讀取", read));
                row.Controls.Add(ActionButton("擷取音軌…", async delegate {
                    var selected = list.SelectedItem as DiscTrack; if (selected == null || ripping) return;
                    using (var save = new SaveFileDialog { Title = "儲存音軌", FileName = "Track " + selected.Number.ToString("00"),
                        Filter = "無損 WAV|*.wav|MP3|*.mp3|AAC|*.m4a|WMA|*.wma|FLAC|*.flac|Apple Lossless|*.m4a", OverwritePrompt = true }) {
                        if (save.ShowDialog(dialog) != DialogResult.OK) return;
                        stop(); string root = (string)drives.SelectedItem, path = save.FileName;
                        bool replace = File.Exists(path);
                        int format = save.FilterIndex;
                        string extension = new[] { ".wav", ".mp3", ".m4a", ".wma", ".flac", ".m4a" }[format - 1];
                        if (!String.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) { status.Text = "所選格式的副檔名是 " + extension; return; }
                        var temp = Path.Combine(Path.GetDirectoryName(path), ".cai-" + Guid.NewGuid().ToString("N") + extension);
                        ripping = true; drives.Enabled = row.Enabled = list.Enabled = false; status.Text = "正在擷取音軌…";
                        try {
                            await Task.Run(() => {
                                using (var audio = new AudioCdStream(root, selected)) WriteDiscAudio(audio, temp, format);
                                if (replace) File.Replace(temp, path, null); else File.Move(temp, path);
                            });
                            status.Text = "已儲存 " + path;
                        } catch (Exception e) { status.Text = e.Message; }
                        finally { ripping = false; drives.Enabled = row.Enabled = list.Enabled = true; if (File.Exists(temp)) File.Delete(temp); }
                    }
                }));
                drives.SelectedIndexChanged += delegate { read(); };
                list.DoubleClick += delegate { play(); };
                dialog.FormClosing += delegate(object s, FormClosingEventArgs e) { if (ripping) e.Cancel = true; };
                try {
                    drives.Items.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom).Select(d => d.Name).ToArray());
                    if (drives.Items.Count > 0) drives.SelectedIndex = 0; else read();
                    dialog.ShowDialog(this);
                } finally { stop(); }
            }
        }
    }
}
