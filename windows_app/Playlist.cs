using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CaiPlayer
{
    public sealed class Track
    {
        public string Title, Path;
        public int Duration;

        public Track(string title, string path, int duration = -1)
        {
            Title = title;
            Path = path;
            Duration = duration;
        }

        public override string ToString() { return Title; }
    }

    public enum PlaylistFormat { M3U, Json, Template }
    public enum PathStyle { Relative, Original, Absolute }

    public sealed class Playlist
    {
        public string Name = "我的播放清單";
        public string SourcePath = "";
        public string MusicRoot = "";
        public List<Track> Tracks = new List<Track>();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Regex Tokens = new Regex(@"\{(index|title|path|duration|sep)\}");

        public static Playlist Load(string filename)
        {
            string full = Path.GetFullPath(filename);
            var p = new Playlist { SourcePath = full, Name = Path.GetFileNameWithoutExtension(full), MusicRoot = Path.GetDirectoryName(full) };
            string content = File.ReadAllText(full, Utf8).TrimStart('\uFEFF');
            if (content.TrimStart().StartsWith("{", StringComparison.Ordinal))
                p.ParseJson(content);
            else
                p.ParseM3u(content);
            return p;
        }

        private void ParseM3u(string content)
        {
            string pendingTitle = null;
            int duration = -1;
            bool nameSeen = false;
            using (var reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("這是 HLS 串流索引。請匯入含歌曲路徑的 M3U 播放清單。");
                    if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    {
                        int comma = line.IndexOf(',');
                        pendingTitle = comma < 0 ? null : line.Substring(comma + 1);
                        string raw = comma < 0 ? line.Substring(8) : line.Substring(8, comma - 8);
                        double value;
                        duration = double.TryParse(raw.Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                            && value >= 0 && value <= int.MaxValue ? (int)value : -1;
                        continue;
                    }
                    if (line.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
                    {
                        Name = line.Substring(10).Trim();
                        nameSeen = true;
                        continue;
                    }
                    if (line[0] == '#')
                    {
                        if (!nameSeen && Tracks.Count == 0 && pendingTitle == null && line.Length > 1 &&
                            !line.StartsWith("#EXT", StringComparison.OrdinalIgnoreCase))
                        {
                            Name = line.Substring(1).Trim();
                            nameSeen = true;
                        }
                        continue;
                    }
                    Tracks.Add(new Track(string.IsNullOrEmpty(pendingTitle) ? DefaultTitle(line) : pendingTitle, line, duration));
                    pendingTitle = null;
                    duration = -1;
                }
            }
        }

        private void ParseJson(string content)
        {
            var data = Serializer().Deserialize<JsonPlaylist>(content);
            if (data == null || data.tracks == null) throw new FormatException("JSON 播放清單需包含 tracks 陣列。");
            if (!string.IsNullOrWhiteSpace(data.name)) Name = data.name;
            foreach (var item in data.tracks)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.path)) throw new FormatException("每首歌曲都需要 path 欄位。");
                Tracks.Add(new Track(string.IsNullOrWhiteSpace(item.title) ? DefaultTitle(item.path) : item.title, item.path, item.duration ?? -1));
            }
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        private static string DefaultTitle(string location)
        {
            Uri uri;
            string path = location;
            if (TryStream(location, out uri)) path = Uri.UnescapeDataString(uri.AbsolutePath);
            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            string title = path.Substring(slash + 1);
            return title.Length == 0 ? location : title;
        }

        private static bool TryStream(string path, out Uri uri)
        {
            return Uri.TryCreate(path, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https");
        }

        public string Resolve(Track track)
        {
            string path = track.Path;
            Uri uri;
            if (string.IsNullOrWhiteSpace(path)) throw new FormatException("歌曲路徑是空白。");
            if (TryStream(path, out uri)) return path;
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out uri) && uri.IsFile)
                path = uri.LocalPath;
            else if (!Path.IsPathRooted(path) && Regex.IsMatch(path, @"^[A-Za-z][A-Za-z0-9+.\-]*:"))
                throw new FormatException("歌曲來源需使用本機檔案或 HTTP(S) 網址。");
            string root = !string.IsNullOrEmpty(MusicRoot) ? MusicRoot :
                (!string.IsNullOrEmpty(SourcePath) ? Path.GetDirectoryName(SourcePath) : Environment.CurrentDirectory);
            return Path.GetFullPath(Path.Combine(root, path));
        }

        internal string ExportPath(Track track, string outputDir, PathStyle style)
        {
            if (style == PathStyle.Original) return track.Path;
            string resolved = Resolve(track);
            Uri stream;
            if (style == PathStyle.Absolute || TryStream(resolved, out stream)) return resolved;
            string dir = Path.GetFullPath(outputDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!string.Equals(Path.GetPathRoot(dir), Path.GetPathRoot(resolved), StringComparison.OrdinalIgnoreCase)) return resolved;
            // Compare filesystem components directly so literal percent escapes, hashes and Unicode remain intact.
            string[] from = dir.TrimEnd('\\').Split('\\');
            string[] to = resolved.Split('\\');
            int common = 0;
            while (common < from.Length && common < to.Length && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase)) common++;
            var parts = new List<string>();
            for (int i = common; i < from.Length; i++) parts.Add("..");
            for (int i = common; i < to.Length; i++) parts.Add(to[i]);
            return string.Join("/", parts.ToArray());
        }

        internal static string FormatRow(string template, string separator, int index, string title, string path, int duration)
        {
            return Tokens.Replace(template, delegate(Match token) {
                switch (token.Groups[1].Value)
                {
                    case "index": return index.ToString(CultureInfo.InvariantCulture);
                    case "title": return title;
                    case "path": return path;
                    case "sep": return separator;
                    default: return duration.ToString(CultureInfo.InvariantCulture);
                }
            });
        }

        private static string OneLine(string text)
        {
            if (text == null || text.IndexOfAny(new char[] { '\r', '\n', '\0' }) >= 0)
                throw new FormatException("M3U 的名稱與路徑必須是單行文字。");
            return text;
        }

        public void Save(string filename, PlaylistFormat format, PathStyle style, string template, string separator)
        {
            string full = Path.GetFullPath(filename);
            if (!string.IsNullOrEmpty(SourcePath) && string.Equals(full, Path.GetFullPath(SourcePath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("請選擇新的檔名，以保留原始播放清單。");
            if (format == PlaylistFormat.Template && string.IsNullOrWhiteSpace(template)) throw new FormatException("請輸入匯出範本。");
            string dir = Path.GetDirectoryName(full);
            var output = new StringBuilder();
            var data = new JsonPlaylist { name = Name, tracks = new List<JsonTrack>() };
            if (format == PlaylistFormat.M3U)
            {
                output.AppendLine("#EXTM3U");
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    string prefix = Name.StartsWith("EXT", StringComparison.OrdinalIgnoreCase) ||
                        Name.StartsWith("PLAYLIST:", StringComparison.OrdinalIgnoreCase) ? "#PLAYLIST:" : "#";
                    output.Append(prefix).AppendLine(OneLine(Name));
                }
            }
            for (int i = 0; i < Tracks.Count; i++)
            {
                Track track = Tracks[i];
                string path = ExportPath(track, dir, style);
                switch (format)
                {
                    case PlaylistFormat.M3U:
                        output.Append("#EXTINF:").Append(track.Duration.ToString(CultureInfo.InvariantCulture)).Append(',').AppendLine(OneLine(track.Title));
                        output.AppendLine(OneLine(path.StartsWith("#", StringComparison.Ordinal) ? "./" + path : path));
                        break;
                    case PlaylistFormat.Json:
                        data.tracks.Add(new JsonTrack { title = track.Title, path = path, duration = track.Duration });
                        break;
                    case PlaylistFormat.Template:
                        output.AppendLine(FormatRow(template, separator, i + 1, track.Title, path, track.Duration));
                        break;
                }
            }
            string text = format == PlaylistFormat.Json ? Serializer().Serialize(data) : output.ToString();
            string temp = Path.Combine(dir, ".cai-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, text, Utf8);
                if (File.Exists(full)) File.Replace(temp, full, null);
                else File.Move(temp, full);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public sealed class JsonPlaylist { public string name { get; set; } public List<JsonTrack> tracks { get; set; } }
        public sealed class JsonTrack { public string title { get; set; } public string path { get; set; } public int? duration { get; set; } }
    }
}
