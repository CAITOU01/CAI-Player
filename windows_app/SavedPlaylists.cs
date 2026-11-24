using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaiPlayer
{
    internal sealed class SavedList
    {
        public string Name { get; set; }
        public string Source { get; set; }
        public List<Playlist.JsonTrack> Tracks { get; set; }
        [ScriptIgnore] public string Id { get; set; }
        public override string ToString() { return Name + "   ·   " + Tracks.Count + " 首"; }

        internal static SavedList Capture(string id, string name, Playlist queue)
        {
            return new SavedList { Id = id, Name = name, Source = queue.SourcePath,
                Tracks = queue.Tracks.Select(t => new Playlist.JsonTrack { title = t.Title, path = t.Path, duration = t.Duration }).ToList() };
        }

        internal Playlist Open(string[] roots)
        {
            return new Playlist { Name = Name, SourcePath = Source ?? "", MusicRoots = roots,
                Tracks = Tracks.Select(t => new Track(t.title, t.path, t.duration ?? -1)).ToList() };
        }
    }

    internal static class SavedPlaylists
    {
        private const string Key = @"Software\CAI Player\Playlists";
        private static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }; }
        internal static List<SavedList> All()
        {
            var all = new List<SavedList>();
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
            {
                if (key == null) return all;
                foreach (string id in key.GetValueNames())
                {
                    var entry = Json().Deserialize<SavedList>((string)key.GetValue(id));
                    entry.Id = id;
                    all.Add(entry);
                }
            }
            return all.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        internal static void Write(SavedList list)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key)) key.SetValue(list.Id, Json().Serialize(list));
        }
        internal static void Delete(string id)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key, true))
                if (key != null) key.DeleteValue(id, false);
        }
    }

    internal sealed partial class PlayerWindow
    {
        private string savedListId;

        private void SaveNamedQueue()
        {
            if (savedListId == null) return;
            try { SavedPlaylists.Write(SavedList.Capture(savedListId, playlist.Name, playlist)); dirty = false; }
            catch (Exception e) { ShowError("無法儲存播放清單", e); }
        }

        private static string AskName(Form owner, string title, string initial)
        {
            using (var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, Font = owner.Font,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(380, 105) })
            {
                var name = new TextBox { Text = initial, AccessibleName = "清單名稱", Bounds = new Rectangle(12, 12, 356, 30), MaxLength = 120 };
                var ok = new Button { Text = "確定", Bounds = new Rectangle(200, 57, 80, 32), Enabled = name.Text.Trim().Length > 0, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Bounds = new Rectangle(288, 57, 80, 32), DialogResult = DialogResult.Cancel };
                name.TextChanged += delegate { ok.Enabled = name.Text.Trim().Length > 0; };
                dialog.Controls.AddRange(new Control[] { name, ok, cancel });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                name.SelectAll();
                return dialog.ShowDialog(owner) == DialogResult.OK ? name.Text.Trim() : null;
            }
        }

        private void OpenNamed(SavedList entry)
        {
            ResetPlayer();
            order.Reset(null);
            ++folderScan;
            folderLibrary = false;
            savedListId = entry.Id;
            playlist = entry.Open(playlist.MusicRoots);
            dirty = false;
            RefreshList(null);
        }

        private void ManagePlaylists()
        {
            using (var dialog = new Form { Text = "我的播放清單", Font = Font, StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(575, 330), MinimumSize = new Size(450, 300), MinimizeBox = false, MaximizeBox = false })
            {
                var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = "已儲存的播放清單" };
                var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 178, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
                dialog.Padding = new Padding(12);
                dialog.Controls.Add(list); dialog.Controls.Add(actions);
                Action<string> reload = id => {
                    list.Items.Clear();
                    foreach (var entry in SavedPlaylists.All()) {
                        int index = list.Items.Add(entry);
                        if (entry.Id == id) list.SelectedIndex = index;
                    }
                    if (list.SelectedIndex == -1 && list.Items.Count != 0) list.SelectedIndex = 0;
                };
                Func<SavedList> selected = () => list.SelectedItem as SavedList;
                Action<string, Action> button = (label, act) => {
                    var b = new Button { Text = label, Width = 152, Height = 33 };
                    b.Click += delegate { try { act(); } catch (Exception e) { ShowError("無法更新播放清單", e); } };
                    actions.Controls.Add(b);
                };
                Action open = () => {
                    var entry = selected();
                    if (entry == null || !MayReplace()) return;
                    OpenNamed(entry); dialog.Close();
                };
                button("開啟", open);
                list.DoubleClick += delegate { open(); };
                button("新增空白清單…", () => {
                    string name = AskName(dialog, "新增播放清單", "");
                    if (name == null || !MayReplace()) return;
                    var entry = SavedList.Capture(Guid.NewGuid().ToString("N"), name, new Playlist());
                    SavedPlaylists.Write(entry); OpenNamed(entry); dialog.Close();
                });
                button("將目前佇列另存為…", () => {
                    string name = AskName(dialog, "儲存目前的播放清單", playlist.Name);
                    if (name == null) return;
                    string id = Guid.NewGuid().ToString("N");
                    SavedPlaylists.Write(SavedList.Capture(id, name, playlist));
                    playlist.Name = name; savedListId = id; folderLibrary = false; ++folderScan;
                    dirty = false; RefreshList(Selected); dialog.Close();
                });
                button("加入選取的歌曲", () => {
                    var target = selected();
                    var track = Selected;
                    if (target == null || track == null) return;
                    if (target.Id == savedListId) {
                        playlist.Tracks.Add(new Track(track.Title, track.Path, track.Duration)); MarkEdited(); RefreshList(track);
                    } else {
                        target.Tracks.Add(new Playlist.JsonTrack { title = track.Title, path = playlist.Resolve(track), duration = track.Duration });
                        SavedPlaylists.Write(target);
                    }
                    reload(target.Id);
                });
                button("重新命名…", () => {
                    var entry = selected();
                    if (entry == null) return;
                    string name = AskName(dialog, "重新命名播放清單", entry.Name);
                    if (name == null) return;
                    entry.Name = name; SavedPlaylists.Write(entry);
                    if (entry.Id == savedListId) { playlist.Name = name; RefreshList(Selected); }
                    reload(entry.Id);
                });
                button("刪除…", () => {
                    var entry = selected();
                    if (entry == null || MessageBox.Show(dialog, "要刪除播放清單「" + entry.Name + "」嗎？音檔會保留。",
                        "刪除播放清單", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                    SavedPlaylists.Delete(entry.Id); if (entry.Id == savedListId) savedListId = null;
                    reload(null);
                });
                try { reload(savedListId); dialog.ShowDialog(this); }
                catch (Exception e) { ShowError("無法開啟已儲存的播放清單", e); }
            }
        }
    }
}
