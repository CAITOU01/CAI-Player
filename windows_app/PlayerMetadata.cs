using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CaiPlayer
{
    internal sealed partial class PlayerWindow
    {
        private Image currentCover;
        private int coverRequest, scanRequest;
        private readonly Dictionary<Track, string> detectedAt = new Dictionary<Track, string>();

        private string LocalSource(Track track)
        {
            return LocalSource(playlist, track.Path);
        }

        private static string LocalSource(Playlist source, string path)
        {
            try
            {
                Uri uri;
                return Uri.TryCreate(source.ResolvePath(path), UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : null;
            }
            catch (Exception) { return null; }
        }

        private void UpdateKnownDuration(Track track, double seconds)
        {
            if (track == null || !(seconds > 0 && seconds < int.MaxValue)) return;
            int index = playlist.Tracks.IndexOf(track);
            if (index < 0 || index >= tracks.Items.Count) return;
            RecordDuration(track, LocalSource(track), seconds, tracks.Items[index]);
        }

        private void RecordDuration(Track track, string source, double seconds, ListViewItem row)
        {
            track.Duration = (int)seconds;
            if (source != null) detectedAt[track] = source;
            row.SubItems[2].Text = Stamp(seconds);
            row.ToolTipText = "";
        }

        private sealed class DurationProbe
        {
            public Track Track;
            public ListViewItem Row;
            public string OriginalPath, LastPath, Path;
            public bool HasDuration;
            public double Seconds;
        }

        private async void QueueDurationScan()
        {
            int request = Interlocked.Increment(ref scanRequest);
            var source = new Playlist { SourcePath = playlist.SourcePath, MusicRoots = playlist.MusicRoots };
            var live = new HashSet<Track>(playlist.Tracks);
            foreach (var old in new List<Track>(detectedAt.Keys))
                if (!live.Contains(old)) detectedAt.Remove(old);
            var pending = new List<DurationProbe>();
            foreach (ListViewItem row in tracks.Items)
            {
                var track = (Track)row.Tag;
                string last;
                if (!detectedAt.TryGetValue(track, out last) && track.Duration > 0) continue;
                // A previously resolved absolute path is independent of folder search order.
                if (track.Duration > 0 && String.Equals(track.Path, last, StringComparison.OrdinalIgnoreCase)) continue;
                pending.Add(new DurationProbe { Track = track, Row = row, OriginalPath = track.Path,
                    LastPath = last, HasDuration = track.Duration > 0 });
            }
            for (int start = 0; start < pending.Count; start += 8)
            {
                var batch = pending.GetRange(start, Math.Min(8, pending.Count - start));
                await Task.Run(delegate {
                    foreach (var job in batch)
                    {
                        if (request != Volatile.Read(ref scanRequest)) break;
                        job.Path = LocalSource(source, job.OriginalPath);
                        if (job.Path == null || (job.HasDuration && String.Equals(job.Path, job.LastPath, StringComparison.OrdinalIgnoreCase))) continue;
                        try { job.Seconds = TrackMetadata.ReadSeconds(job.Path); }
                        catch (Exception) { /* An unreadable or unsupported file retains its unknown duration. */ }
                    }
                });
                if (request != scanRequest || IsDisposed || Disposing) return;
                foreach (var job in batch)
                {
                    string known;
                    if (detectedAt.TryGetValue(job.Track, out known) && !String.Equals(known, job.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        detectedAt.Remove(job.Track);
                        job.Track.Duration = -1;
                        job.Row.SubItems[2].Text = "—";
                    }
                    if (job.Track.Duration > 0) continue;
                    if (job.Seconds > 0 && job.Seconds < int.MaxValue)
                        RecordDuration(job.Track, job.Path, job.Seconds, job.Row);
                    else job.Row.ToolTipText = "無法讀取音檔時長，請確認歌曲檔案或選擇「音樂資料夾」。";
                }
            }
        }

        private void ClearTrackArt()
        {
            coverRequest++;
            var old = currentCover;
            currentCover = null;
            RefreshCoverDisplay();
            if (old != null) old.Dispose();
        }

        private async void LoadTrackArt(Track track, Uri uri)
        {
            int request = ++coverRequest;
            if (!uri.IsFile) return;
            var image = await Task.Run(delegate {
                try { return TrackMetadata.ReadCover(uri.LocalPath); }
                catch (Exception) { return null; }
            });
            if (request != coverRequest || IsDisposed || Disposing || track != active)
            {
                if (image != null) image.Dispose();
                return;
            }
            var old = currentCover;
            currentCover = image;
            RefreshCoverDisplay();
            if (old != null) old.Dispose();
        }

        private void StopMetadataScan()
        {
            Interlocked.Increment(ref scanRequest);
            coverRequest++;
        }
    }
}
