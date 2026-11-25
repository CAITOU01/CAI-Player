using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaiPlayer
{
    internal static class RecentSongs
    {
        private const int Limit = 20;
        private static readonly HashSet<string> Audio = Extensions(MusicFolders.Extensions);
        private static readonly HashSet<string> Video = Extensions(MusicFolders.VideoExtensions);

        private static HashSet<string> Extensions(string patterns)
        {
            return new HashSet<string>(patterns.Split(';').Select(s => s.Substring(1)), StringComparer.OrdinalIgnoreCase);
        }

        internal static bool IsSong(Uri uri)
        {
            return uri.IsFile ? Audio.Contains(Path.GetExtension(uri.LocalPath)) :
                (uri.Scheme == "http" || uri.Scheme == "https") && !Video.Contains(Path.GetExtension(uri.AbsolutePath));
        }

        private static IEnumerable<Uri> Songs(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                Uri uri;
                if (Uri.TryCreate(path, UriKind.Absolute, out uri) && IsSong(uri)) yield return uri;
            }
        }

        private static string Source(Uri uri) { return uri.IsFile ? uri.LocalPath : uri.AbsoluteUri; }

        internal static string[] Played(string[] legacy)
        {
            var history = Registry.GetValue(MusicFolders.Settings, "RecentSongs", null) as string[] ?? legacy;
            return Songs(history).Distinct().Take(Limit).Select(Source).ToArray();
        }

        internal static void RememberPlayed(Uri uri, string[] legacy)
        {
            var old = Played(legacy);
            string[] list = new[] { uri }.Concat(Songs(old)).Distinct().Take(Limit).Select(Source).ToArray();
            if (!old.SequenceEqual(list) || Registry.GetValue(MusicFolders.Settings, "RecentSongs", null) == null)
                Registry.SetValue(MusicFolders.Settings, "RecentSongs", list, RegistryValueKind.MultiString);
        }

        private static Dictionary<Uri, long> ManualAdditions()
        {
            var entries = Registry.GetValue(MusicFolders.Settings, "AddedSongs", null) as string[] ?? new string[0];
            var result = new Dictionary<Uri, long>();
            foreach (string entry in entries)
            {
                int split = entry.IndexOf('|');
                long ticks; Uri uri;
                if (split > 0 && Int64.TryParse(entry.Substring(0, split), out ticks) &&
                    Uri.TryCreate(entry.Substring(split + 1), UriKind.Absolute, out uri)) result[uri] = ticks;
            }
            return result;
        }

        internal static void RememberAdded(IEnumerable<string> paths)
        {
            try
            {
                var known = ManualAdditions();
                long ticks = DateTime.UtcNow.Ticks;
                foreach (var uri in Songs(paths))
                    if (!known.ContainsKey(uri)) known.Add(uri, ticks--);
                var entries = known.OrderByDescending(pair => pair.Value).Take(Limit)
                    .Select(pair => pair.Value + "|" + Source(pair.Key)).ToArray();
                Registry.SetValue(MusicFolders.Settings, "AddedSongs", entries, RegistryValueKind.MultiString);
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (IOException) { }
        }

        internal static string[] Added(string[] roots)
        {
            var dates = ManualAdditions();
            foreach (string root in roots)
            {
                try
                {
                    foreach (var file in new DirectoryInfo(root).EnumerateFiles())
                    {
                        if (!Audio.Contains(file.Extension)) continue;
                        var uri = new Uri(file.FullName);
                        if (!dates.ContainsKey(uri)) dates[uri] = file.CreationTimeUtc.Ticks;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
            return dates.OrderByDescending(p => p.Value).ThenBy(p => Source(p.Key), StringComparer.OrdinalIgnoreCase)
                .Take(Limit).Select(p => Source(p.Key)).ToArray();
        }
    }

    internal sealed partial class PlayerWindow
    {
        private readonly TreeView sidebar = new TreeView();
        private TreeNode foldersNode, addedNode, playedNode, savedNode, smartNode;
        private Playlist smartQueue;

        private void BuildSidebar(Panel browser)
        {
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 170;
            sidebar.AccessibleName = "播放清單側邊欄";
            sidebar.FullRowSelect = true;
            sidebar.ShowLines = sidebar.ShowPlusMinus = sidebar.ShowRootLines = false;
            sidebar.HideSelection = false;
            sidebar.ItemHeight = 28;
            foldersNode = sidebar.Nodes.Add("資料夾歌庫");
            addedNode = sidebar.Nodes.Add("近期新增");
            playedNode = sidebar.Nodes.Add("近期播放");
            savedNode = sidebar.Nodes.Add("已儲存的播放清單");
            sidebar.BeforeCollapse += (s, e) => e.Cancel = true;
            sidebar.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSidebar(e.Node); };
            sidebar.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) {
                    e.SuppressKeyPress = true; OpenSidebar(sidebar.SelectedNode);
                }
            };
            browser.Controls.Add(sidebar);
        }

        private void ReloadSidebar()
        {
            try
            {
                var lists = SavedPlaylists.All();
                sidebar.BeginUpdate();
                try {
                    savedNode.Nodes.Clear();
                    foreach (var entry in lists) savedNode.Nodes.Add(entry.Id, entry.Name);
                    savedNode.Expand();
                    SyncSidebar();
                } finally { sidebar.EndUpdate(); }
            }
            catch (Exception e) { ShowError("無法讀取播放清單", e); }
        }

        private void SyncSidebar()
        {
            if (playlist != smartQueue || savedListId != null) smartQueue = null;
            sidebar.SelectedNode = savedListId != null ? savedNode.Nodes[savedListId] :
                folderLibrary ? foldersNode : smartQueue != null ? smartNode : null;
        }

        private async void OpenSidebar(TreeNode node)
        {
            if (node == null) return;
            if (node == savedNode) { ManagePlaylists(); return; }
            if (node == foldersNode && folderLibrary) { RefreshFolderLibrary(); return; }
            if (node.Parent == savedNode && node.Name == savedListId) return;
            if (!MayReplace()) { SyncSidebar(); return; }
            try
            {
                if (node.Parent == savedNode) {
                    var entry = SavedPlaylists.Read(node.Name);
                    if (entry == null) ReloadSidebar(); else OpenNamed(entry);
                    return;
                }
                ResetPlayer(); order.Reset(null);
                int request = ++folderScan;
                savedListId = null; dirty = false; folderLibrary = node == foldersNode;
                string[] roots = playlist.MusicRoots;
                playlist = new Playlist { MusicRoots = roots, Name = node.Text };
                smartQueue = folderLibrary ? null : playlist;
                smartNode = node;
                RefreshList(null);
                if (folderLibrary) { RefreshFolderLibrary(); return; }
                state.Text = "正在讀取" + node.Text + "…";
                emptyHint.Text = state.Text;
                bool added = node == addedNode;
                var songs = await Task.Run(() => {
                    string[] paths = added ? RecentSongs.Added(roots) : RecentSongs.Played(RecentPaths());
                    return paths.Select(path => new Track(LibrarySong.Read(path).Title, path)).ToList();
                });
                if (IsDisposed || Disposing || request != folderScan || smartQueue != playlist) return;
                playlist.Tracks = songs;
                RefreshList(null);
                if (songs.Count == 0) emptyHint.Text = added ? "加入音檔或設定音樂資料夾後，歌曲會顯示在這裡" : "播放歌曲後，紀錄會顯示在這裡";
                state.Text = node.Text + " · " + songs.Count + " 首";
            }
            catch (Exception e) { if (!IsDisposed) ShowError("無法開啟播放清單", e); }
        }
    }
}
