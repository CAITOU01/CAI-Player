using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaiPlayer
{
    internal sealed class LibrarySong
    {
        internal string Title, Artist = "", Album = "", Genre = "", Path;
        internal static LibrarySong Read(string path)
        {
            var song = new LibrarySong { Path = path, Title = System.IO.Path.GetFileNameWithoutExtension(path) };
            Uri uri;
            if (Uri.TryCreate(path, UriKind.Absolute, out uri) && !uri.IsFile) return song;
            try
            {
                using (var file = TagLib.File.Create(path, TagLib.ReadStyle.None))
                {
                    var tag = file.Tag;
                    if (!String.IsNullOrWhiteSpace(tag.Title)) song.Title = tag.Title;
                    song.Artist = tag.JoinedPerformers ?? ""; song.Album = tag.Album ?? ""; song.Genre = tag.JoinedGenres ?? "";
                }
            }
            catch (Exception) { /* Keep the file visible if it has no readable tags. */ }
            return song;
        }
    }

    internal sealed partial class PlayerWindow
    {
        private static string[] RecentPaths()
        {
            return Registry.GetValue(MusicFolders.Settings, "RecentTracks", null) as string[] ?? new string[0];
        }

        private void RecordRecent(Uri source)
        {
            if (source == null) return;
            try
            {
                string path = source.IsFile ? source.LocalPath : source.AbsoluteUri;
                var recent = new[] { path }.Concat(RecentPaths().Where(s => !String.Equals(s, path,
                    source.IsFile ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))).Take(50).ToArray();
                Registry.SetValue(MusicFolders.Settings, "RecentTracks", recent, RegistryValueKind.MultiString);
            }
            catch (Exception) { /* Playback works when the history cannot be stored. */ }
        }

        private void BrowseLibrary()
        {
            using (var dialog = new Form { Text = "音樂庫", StartPosition = FormStartPosition.CenterParent, Font = Font,
                ClientSize = new Size(800, 460), MinimumSize = new Size(570, 350) })
            {
                var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); dialog.Controls.Add(page);
                var top = Row(); page.Controls.Add(top, 0, 0);
                var source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108, AccessibleName = "媒體來源" };
                source.Items.AddRange(new[] { "資料夾歌庫", "最近播放", "影片庫" }); source.SelectedIndex = 0;
                var sort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 94, AccessibleName = "排序方式" };
                sort.Items.AddRange(new[] { "原有順序", "歌曲名稱", "演出者", "專輯", "類型" }); sort.SelectedIndex = 0;
                var search = new TextBox { Width = 190, AccessibleName = "搜尋歌曲、演出者或專輯" };
                top.Controls.AddRange(new Control[] { source, sort, Caption("搜尋", 10, false), search });
                var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                    HideSelection = false, AccessibleName = "音樂庫歌曲" };
                foreach (var column in new[] { "歌曲名稱", "演出者", "專輯", "類型", "位置" }) view.Columns.Add(column, column == "歌曲名稱" ? 190 : 135);
                page.Controls.Add(view, 0, 1);
                var buttons = Row(); page.Controls.Add(buttons, 0, 2);
                var songs = new List<LibrarySong>();
                Action render = () => {
                    var query = search.Text.Trim();
                    IEnumerable<LibrarySong> visible = songs.Where(s => new[] { s.Title, s.Artist, s.Album, s.Genre, s.Path }
                        .Any(field => field.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0));
                    if (sort.SelectedIndex > 0) visible = visible.OrderBy(s => sort.SelectedIndex == 1 ? s.Title :
                        sort.SelectedIndex == 2 ? s.Artist : sort.SelectedIndex == 3 ? s.Album : s.Genre).ThenBy(s => s.Title);
                    view.BeginUpdate(); view.Items.Clear();
                    foreach (var s in visible) view.Items.Add(new ListViewItem(new[] { s.Title, s.Artist, s.Album, s.Genre, s.Path }) { Tag = s });
                    view.EndUpdate();
                };
                search.TextChanged += delegate { render(); }; sort.SelectedIndexChanged += delegate { render(); };
                int generation = 0;
                Action load = async delegate {
                    int request = ++generation;
                    bool recent = source.SelectedIndex == 1;
                    bool videos = source.SelectedIndex == 2;
                    string[] roots = videos ? VideoFolders() : playlist.MusicRoots;
                    songs.Clear(); render(); dialog.Text = "音樂庫 · 載入中…";
                    try {
                        var result = await Task.Run(() => {
                            var paths = recent ? (IEnumerable<string>)RecentPaths() : MusicFolders.Scan(roots, videos).Files;
                            var rows = new List<LibrarySong>();
                            foreach (var path in paths) {
                                if (System.Threading.Volatile.Read(ref generation) != request) break;
                                rows.Add(LibrarySong.Read(path));
                            }
                            return rows;
                        });
                        if (request != generation || dialog.IsDisposed) return;
                        songs = result; render(); dialog.Text = "音樂庫 · " + songs.Count + " 首";
                    } catch (Exception e) { if (!dialog.IsDisposed) ShowError("無法讀取音樂庫", e); }
                };
                source.SelectedIndexChanged += delegate { load(); }; dialog.Shown += delegate { load(); };
                var configure = ActionButton("影片資料夾…", delegate {
                    using (var folders = new MusicFoldersDialog(VideoFolders(), true, true)) {
                        if (folders.ShowDialog(dialog) != DialogResult.OK) return;
                        try { Registry.SetValue(MusicFolders.Settings, "VideoFolders", folders.Roots, RegistryValueKind.MultiString); load(); }
                        catch (Exception e) { ShowError("無法儲存影片資料夾", e); }
                    }
                });
                configure.Visible = false;
                source.SelectedIndexChanged += delegate { configure.Visible = source.SelectedIndex == 2; };
                top.Controls.Add(configure);
                dialog.FormClosed += delegate { ++generation; };
                Action<bool, bool> enqueue = (play, next) => {
                    if (view.SelectedItems.Count == 0) return;
                    var selection = (LibrarySong)view.SelectedItems[0].Tag;
                    Track track;
                    if (play)
                    {
                        if (!MayReplace()) return;
                        var queue = new Playlist { MusicRoots = playlist.MusicRoots, Name = source.Text };
                        track = null;
                        foreach (ListViewItem item in view.Items) {
                            var song = (LibrarySong)item.Tag; var t = new Track(song.Title, song.Path);
                            queue.Tracks.Add(t); if (song == selection) track = t;
                        }
                        ResetPlayer(); order.Reset(null); playlist = queue; savedListId = null; folderLibrary = false; ++folderScan;
                        dirty = false; RefreshList(track); dialog.Close(); order.Select(track); Start(track);
                    }
                    else
                    {
                        track = new Track(selection.Title, selection.Path);
                        int index = next ? (active == null ? 0 : playlist.Tracks.IndexOf(active) + 1) : playlist.Tracks.Count;
                        playlist.Tracks.Insert(index, track);
                        if (next) order.PlayNext(track);
                        MarkEdited(); RefreshList(track); dialog.Close();
                    }
                };
                buttons.Controls.Add(ActionButton("立即播放", () => enqueue(true, false), true));
                buttons.Controls.Add(ActionButton("下一首播放", () => enqueue(false, true)));
                buttons.Controls.Add(ActionButton("加入佇列", () => enqueue(false, false)));
                view.ItemActivate += delegate { enqueue(true, false); };
                dialog.ShowDialog(this);
            }
        }

        private static string[] VideoFolders()
        {
            return Registry.GetValue(MusicFolders.Settings, "VideoFolders", null) as string[] ?? new string[0];
        }

    }
}
