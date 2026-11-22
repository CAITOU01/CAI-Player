using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using MediaPlayer = System.Windows.Media.MediaPlayer;

namespace CaiPlayer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += EmbeddedAudio.Resolve;
            Run(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PlayerWindow(args));
        }
    }

    internal sealed partial class PlayerWindow : Form
    {
        private Playlist playlist = new Playlist();
        private readonly ListView tracks = new ListView();
        private readonly Label emptyHint = new Label();
        private readonly Label queueTitle = new Label(), nowTitle = new Label(), state = new Label(), time = new Label();
        private readonly PictureBox brand = new PictureBox();
        private readonly TrackBar progress = new TrackBar(), volume = new TrackBar();
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer timer = new Timer();
        private readonly TableLayoutPanel page = new TableLayoutPanel(), deck = new TableLayoutPanel();
        private readonly NowPlayingView banner = new NowPlayingView(false), stage = new NowPlayingView(true);
        private readonly float[] levels = new float[64];
        private TrackSignal signal;
        private bool expanded;
        private FormWindowState previousSize;
        private FlowLayoutPanel editActions;
        private Button themeButton, playButton, viewButton;
        private readonly PlaybackOrder order = new PlaybackOrder();
        private readonly CheckBox shuffle = new CheckBox();
        private Button repeat;
        private readonly ContextMenuStrip repeatMenu = new ContextMenuStrip();
        private Button editButton, removeButton, upButton, downButton;
        private MediaPlayer media;
        private Track active, dragItem;
        private bool dark, playing, ready, dirty, seeking;
        private double duration;
        private const string Product = "CAI Player";
        private const string Version = "V0.03 Alpha";

        public PlayerWindow(string[] args)
        {
            Text = Product + "  " + Version;
            Font = new Font("Microsoft JhengHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 700);
            MinimumSize = new Size(760, 580);
            KeyPreview = true;
            try
            {
                var system = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                dark = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\CAI Player", "DarkMode", Convert.ToInt32(system) == 0 ? 1 : 0)) != 0;
            }
            catch { dark = false; }
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            page.Dock = DockStyle.Fill;
            page.Padding = new Padding(22, 14, 22, 16);
            page.ColumnCount = 1;
            page.RowCount = 6;
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
            Controls.Add(page);
            BuildHeader();
            BuildQueue();
            BuildDeck();
            SetTheme();
            RefreshList(null);
            timer.Interval = 55;
            timer.Tick += delegate { UpdateTime(); UpdateWaveform(); };
            timer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !MayReplace()) e.Cancel = true; };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); StopMetadataScan(); ResetPlayer(); if (brand.Image != null) brand.Image.Dispose(); tips.Dispose(); repeatMenu.Dispose(); };
            Shown += delegate
            {
                if (args.Length == 0) return;
                OpenPaths(args);
            };
        }

        private static Label Caption(string text, float points, bool bold)
        {
            return new Label { Text = text, Font = new Font("Microsoft JhengHei UI", points, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        }

        private Button ActionButton(string text, Action action, bool accent = false)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 32),
                Padding = new Padding(8, 2, 8, 2), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 6, 3), Tag = accent ? "accent" : null };
            b.Click += delegate { action(); };
            return b;
        }

        private static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), AutoSize = false };
        }

        private void BuildHeader()
        {
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), ColumnCount = 5, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brand.Size = new Size(52, 52);
            brand.Anchor = AnchorStyles.Left;
            brand.SizeMode = PictureBoxSizeMode.Zoom;
            header.Controls.Add(brand, 0, 0);
            header.Controls.Add(Caption(Product, 22, true), 1, 0);
            var version = Caption(Version, 9, false);
            version.Margin = new Padding(14, 8, 0, 0);
            header.Controls.Add(version, 2, 0);
            viewButton = ActionButton("大視窗模式", ToggleView);
            viewButton.Anchor = AnchorStyles.Right;
            tips.SetToolTip(viewButton, "切換播放檢視（F11）");
            header.Controls.Add(viewButton, 3, 0);
            themeButton = ActionButton("切換外觀", delegate {
                dark = !dark;
                SetTheme();
                try { Registry.SetValue(@"HKEY_CURRENT_USER\Software\CAI Player", "DarkMode", dark ? 1 : 0); } catch { }
            });
            themeButton.Anchor = AnchorStyles.Right;
            header.Controls.Add(themeButton, 4, 0);
            page.Controls.Add(header, 0, 0);
        }

        private void BuildQueue()
        {
            var actions = Row();
            actions.Controls.Add(ActionButton("匯入清單", Import, true));
            actions.Controls.Add(ActionButton("加入音檔", ChooseFiles));
            actions.Controls.Add(ActionButton("加入網址", AddUrl));
            actions.Controls.Add(ActionButton("音樂資料夾", ChooseRoot));
            actions.Controls.Add(ActionButton("匯出清單", delegate { Export(); }));
            page.Controls.Add(actions, 0, 1);
            queueTitle.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
            queueTitle.TextAlign = ContentAlignment.MiddleLeft;
            queueTitle.AutoEllipsis = true;
            queueTitle.Dock = DockStyle.Fill;
            page.Controls.Add(queueTitle, 0, 2);
            tracks.Dock = DockStyle.Fill;
            tracks.Margin = new Padding(0);
            tracks.View = View.Details;
            tracks.FullRowSelect = true;
            tracks.ShowItemToolTips = true;
            tracks.HideSelection = false;
            tracks.MultiSelect = false;
            tracks.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            tracks.Columns.Add("#", 52);
            tracks.Columns.Add("歌曲名稱", 310);
            tracks.Columns.Add("時長", 68);
            tracks.Columns.Add("檔案或網址", 370);
            tracks.Resize += delegate { SizeColumns(); };
            tracks.ItemActivate += delegate
            {
                var choice = Selected;
                if (choice == null) return;
                if (active == null) order.Reset(null);
                order.Select(choice);
                Start(choice);
            };
            tracks.SelectedIndexChanged += delegate { UpdateButtons(); };
            tracks.AllowDrop = true;
            tracks.ItemDrag += delegate(object sender, ItemDragEventArgs e)
            {
                dragItem = ((ListViewItem)e.Item).Tag as Track;
                try { tracks.DoDragDrop(e.Item, DragDropEffects.Move); } finally { dragItem = null; }
            };
            tracks.DragEnter += OnDrag;
            tracks.DragOver += OnDrag;
            tracks.DragDrop += delegate(object sender, DragEventArgs e)
            {
                if (dragItem != null)
                {
                    var point = tracks.PointToClient(new Point(e.X, e.Y));
                    var target = tracks.GetItemAt(point.X, point.Y);
                    MoveTrack(dragItem, target == null ? playlist.Tracks.Count - 1 : target.Index);
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop)) OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop));
            };
            var listArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            listArea.Controls.Add(tracks);
            emptyHint.Text = "將音檔或播放清單拖到這裡";
            emptyHint.AutoSize = true;
            emptyHint.Font = new Font(Font.FontFamily, 12);
            emptyHint.AllowDrop = true;
            emptyHint.DragEnter += OnDrag;
            emptyHint.DragDrop += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop));
            };
            listArea.Controls.Add(emptyHint);
            listArea.Controls.Add(stage);
            stage.Visible = false;
            emptyHint.BringToFront();
            listArea.Resize += delegate
            {
                emptyHint.Location = new Point((listArea.Width - emptyHint.PreferredWidth) / 2, (listArea.Height - emptyHint.PreferredHeight) / 2);
            };
            page.Controls.Add(listArea, 0, 3);
            var edit = Row();
            editActions = edit;
            edit.Controls.Add(editButton = ActionButton("編輯", Edit));
            edit.Controls.Add(upButton = ActionButton("上移", delegate { MoveSelection(-1); }));
            edit.Controls.Add(downButton = ActionButton("下移", delegate { MoveSelection(1); }));
            edit.Controls.Add(removeButton = ActionButton("移除", Remove));
            page.Controls.Add(edit, 0, 4);
        }

        private void OnDrag(object sender, DragEventArgs e)
        {
            e.Effect = dragItem != null ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void BuildDeck()
        {
            deck.Dock = DockStyle.Fill;
            deck.Padding = new Padding(14, 8, 14, 7);
            deck.Margin = new Padding(0, 6, 0, 0);
            deck.ColumnCount = 2;
            deck.RowCount = 3;
            deck.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            deck.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            deck.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            deck.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            deck.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            nowTitle.Text = "尚未播放";
            state.Text = "已停止";
            nowTitle.TextChanged += delegate { RefreshCoverDisplay(); };
            state.TextChanged += delegate { RefreshCoverDisplay(); };
            deck.Controls.Add(banner, 0, 0);
            deck.SetColumnSpan(banner, 2);
            banner.DoubleClick += delegate { ToggleView(); };
            stage.DoubleClick += delegate { ToggleView(); };
            tips.SetToolTip(banner, "按兩下開啟大視窗模式");
            RefreshCoverDisplay();
            progress.Dock = DockStyle.Fill;
            progress.AutoSize = false;
            progress.TickStyle = TickStyle.None;
            progress.Maximum = 1000;
            progress.Enabled = false;
            progress.MouseDown += delegate { seeking = true; };
            progress.MouseUp += delegate { if (seeking) Seek(); seeking = false; };
            progress.MouseCaptureChanged += delegate { if (seeking && !progress.Capture) { Seek(); seeking = false; } };
            progress.Scroll += delegate { if (!seeking) Seek(); };
            deck.Controls.Add(progress, 0, 1);
            time.AutoSize = true;
            time.Anchor = AnchorStyles.Right;
            time.Text = "0:00 / 0:00";
            deck.Controls.Add(time, 1, 1);
            var transport = Row();
            transport.Controls.Add(ActionButton("上一首", delegate { Skip(-1); }));
            transport.Controls.Add(playButton = ActionButton("播放", Toggle, true));
            transport.Controls.Add(ActionButton("下一首", delegate { Skip(1); }));
            shuffle.Text = "隨機播放";
            shuffle.AutoSize = true;
            shuffle.Margin = new Padding(7, 10, 8, 0);
            shuffle.CheckedChanged += delegate { order.SetShuffle(shuffle.Checked, active); };
            transport.Controls.Add(shuffle);
            repeat = ActionButton("循環關閉 ▾", delegate { repeatMenu.Show(repeat, new Point(0, repeat.Height)); });
            repeat.AccessibleDescription = "選擇歌曲播完時的循環模式";
            foreach (string modeName in new[] { "循環關閉", "單曲循環", "清單循環" })
            {
                var mode = (RepeatMode)repeatMenu.Items.Count;
                var item = new ToolStripMenuItem(modeName) { Checked = mode == order.Repeat };
                item.Click += delegate
                {
                    ((ToolStripMenuItem)repeatMenu.Items[(int)order.Repeat]).Checked = false;
                    order.Repeat = mode;
                    item.Checked = true;
                    repeat.Text = item.Text + " ▾";
                };
                repeatMenu.Items.Add(item);
            }
            transport.Controls.Add(repeat);
            deck.Controls.Add(transport, 0, 2);
            var volRow = Row();
            volRow.AutoSize = true;
            volRow.Anchor = AnchorStyles.Right;
            volRow.Dock = DockStyle.None;
            var label = Caption("音量", 10, false);
            label.Margin = new Padding(0, 2, 3, 0);
            volRow.Controls.Add(label);
            volume.Width = 110;
            volume.Height = 26;
            volume.AutoSize = false;
            volume.Maximum = 100;
            volume.Value = 75;
            volume.TickStyle = TickStyle.None;
            volume.ValueChanged += delegate { if (media != null) media.Volume = volume.Value / 100.0; tips.SetToolTip(volume, volume.Value + "%"); };
            volRow.Controls.Add(volume);
            deck.Controls.Add(volRow, 1, 2);
            page.Controls.Add(deck, 0, 5);
        }

        private void ToggleView()
        {
            expanded = !expanded;
            if (expanded) previousSize = WindowState;
            page.SuspendLayout();
            deck.SuspendLayout();
            queueTitle.Visible = editActions.Visible = tracks.Visible = !expanded;
            emptyHint.Visible = !expanded && playlist.Tracks.Count == 0;
            stage.Visible = expanded;
            if (expanded) stage.BringToFront();
            banner.Visible = !expanded;
            // AutoScaleMode.Dpi also scales the absolute row heights on initial layout.
            float dpi;
            using (var g = CreateGraphics()) dpi = g.DpiY / 96F;
            page.RowStyles[2].Height = expanded ? 0 : 40 * dpi;
            page.RowStyles[4].Height = expanded ? 0 : 44 * dpi;
            page.RowStyles[5].Height = (expanded ? 107 : 180) * dpi;
            viewButton.Text = expanded ? "返回清單" : "大視窗模式";
            viewButton.AccessibleDescription = expanded ? "離開大視窗模式" : "顯示歌曲封面與音訊波形";
            deck.ResumeLayout(true);
            page.ResumeLayout(true);
            WindowState = expanded ? FormWindowState.Maximized : previousSize;
            UpdateWaveform();
        }

        private void RefreshCoverDisplay()
        {
            banner.SetTrack(nowTitle.Text, state.Text, currentCover);
            stage.SetTrack(nowTitle.Text, state.Text, currentCover);
        }

        private void UpdateWaveform()
        {
            bool available = ready && media != null && signal != null &&
                signal.TryGetLevels(seeking ? duration * progress.Value / 1000 : media.Position.TotalSeconds, levels);
            (expanded ? stage : banner).SetSignal(available, levels);
        }

        private void SetTheme()
        {
            var background = dark ? Color.FromArgb(26, 29, 33) : Color.FromArgb(247, 248, 248);
            var surface = dark ? Color.FromArgb(38, 43, 47) : Color.White;
            var ink = dark ? Color.FromArgb(235, 238, 239) : Color.FromArgb(29, 37, 42);
            var line = dark ? Color.FromArgb(74, 83, 89) : Color.FromArgb(194, 203, 209);
            var accent = dark ? Color.FromArgb(57, 204, 238) : Color.FromArgb(172, 58, 12);
            BackColor = background;
            ForeColor = ink;
            deck.BackColor = surface;
            banner.SetTheme(dark, surface, ink);
            stage.SetTheme(dark, surface, ink);
            tracks.BackColor = surface;
            tracks.ForeColor = ink;
            emptyHint.BackColor = surface;
            emptyHint.ForeColor = dark ? Color.FromArgb(175, 190, 196) : Color.FromArgb(91, 105, 115);
            foreach (Control c in AllControls(this))
            {
                var b = c as Button;
                if (b == null) continue;
                bool primary = (string)b.Tag == "accent";
                b.UseVisualStyleBackColor = false;
                b.BackColor = primary ? accent : surface;
                b.ForeColor = primary ? (dark ? Color.Black : Color.White) : ink;
                b.FlatAppearance.BorderColor = primary ? accent : line;
            }
            themeButton.Text = dark ? "淺色模式" : "深色模式";
            var resource = dark ? "logo_dark.png" : "logo_light.png";
            using (var stream = typeof(PlayerWindow).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return;
                using (var image = Image.FromStream(stream))
                {
                    var previous = brand.Image;
                    brand.Image = new Bitmap(image);
                    if (previous != null) previous.Dispose();
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                yield return c;
                foreach (Control child in AllControls(c)) yield return child;
            }
        }

        private Track Selected { get { return tracks.SelectedItems.Count > 0 ? tracks.SelectedItems[0].Tag as Track : null; } }

        private void SizeColumns()
        {
            int available = Math.Max(380, tracks.ClientSize.Width - 148);
            tracks.Columns[1].Width = (int)(available * 0.49);
            tracks.Columns[3].Width = available - tracks.Columns[1].Width;
        }

        private void RefreshList(Track focus)
        {
            tracks.BeginUpdate();
            tracks.Items.Clear();
            foreach (var track in playlist.Tracks)
            {
                var item = new ListViewItem((tracks.Items.Count + 1).ToString("00"));
                item.SubItems.Add(track.Title);
                item.SubItems.Add(track.Duration < 0 ? "—" : Stamp(track.Duration));
                item.SubItems.Add(track.Path);
                if (track.Duration < 0)
                    item.ToolTipText = "時長尚未取得。本機歌曲請確認「音樂資料夾」位置；串流時長由來源提供。";
                item.Tag = track;
                tracks.Items.Add(item);
                if (track == focus) { item.Selected = true; item.Focused = true; }
            }
            tracks.EndUpdate();
            emptyHint.Visible = !expanded && playlist.Tracks.Count == 0;
            if (tracks.SelectedItems.Count != 0) tracks.SelectedItems[0].EnsureVisible();
            queueTitle.Text = (String.IsNullOrWhiteSpace(playlist.Name) ? "播放清單" : playlist.Name) + "   ·   " + playlist.Tracks.Count + " 首";
            UpdateButtons();
            QueueDurationScan();
        }

        private void UpdateButtons()
        {
            int index = tracks.SelectedIndices.Count == 0 ? -1 : tracks.SelectedIndices[0];
            if (editButton == null || downButton == null || removeButton == null) return;
            editButton.Enabled = removeButton.Enabled = index >= 0;
            upButton.Enabled = index > 0;
            downButton.Enabled = index >= 0 && index + 1 < playlist.Tracks.Count;
            playButton.Enabled = playlist.Tracks.Count > 0;
        }

        private bool MayReplace()
        {
            if (!dirty) return true;
            var choice = MessageBox.Show(this, "要先匯出清單的變更嗎？", Product, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return choice == DialogResult.No || (choice == DialogResult.Yes && Export());
        }

        private void Import()
        {
            using (var d = new OpenFileDialog { Title = "匯入播放清單", Filter = "播放清單|*.m3u8;*.m3u;*.json|所有檔案|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) ImportFile(d.FileName);
        }

        private void ImportFile(string filename)
        {
            try
            {
                var imported = Playlist.Load(filename);
                if (!MayReplace()) return;
                ResetPlayer();
                order.Reset(null);
                playlist = imported;
                dirty = false;
                RefreshList(playlist.Tracks.Count == 0 ? null : playlist.Tracks[0]);
            }
            catch (Exception e) { ShowError("無法匯入清單", e); }
        }

        private bool Export()
        {
            try
            {
                if (!ExportDialog.Export(this, playlist)) return false;
                dirty = false;
                return true;
            }
            catch (Exception e) { ShowError("無法匯出清單", e); return false; }
        }

        private void ChooseFiles()
        {
            using (var d = new OpenFileDialog { Title = "加入音樂檔案", Multiselect = true,
                Filter = "音樂檔案|*.mp3;*.flac;*.m4a;*.aac;*.wav;*.wma;*.aiff;*.ogg;*.opus|所有檔案|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
        }

        private void AddFiles(string[] paths)
        {
            Track first = null;
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var track = new Track(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path));
                playlist.Tracks.Add(track);
                if (first == null) first = track;
            }
            if (first != null) { dirty = true; RefreshList(first); }
        }

        private void OpenPaths(string[] paths)
        {
            if (paths.Length == 1)
            {
                string ext = Path.GetExtension(paths[0]).ToLowerInvariant();
                if (ext == ".m3u" || ext == ".m3u8" || ext == ".json")
                {
                    ImportFile(paths[0]);
                    return;
                }
            }
            AddFiles(paths);
        }

        private void AddUrl()
        {
            var track = new Track("", "");
            if (!TrackEditor.Update(this, track, true)) return;
            playlist.Tracks.Add(track);
            dirty = true;
            RefreshList(track);
        }

        private void ChooseRoot()
        {
            using (var d = new FolderBrowserDialog { Description = "選擇播放清單內相對路徑所指向的音樂資料夾",
                SelectedPath = playlist.MusicRoot ?? "", ShowNewFolderButton = false })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    playlist.MusicRoot = d.SelectedPath;
                    QueueDurationScan();
                    state.Text = "已設定音樂資料夾";
                    tips.SetToolTip(state, d.SelectedPath);
                }
        }

        private void Edit()
        {
            var track = Selected;
            if (track == null) return;
            string oldPath = track.Path, oldTitle = track.Title;
            if (!TrackEditor.Update(this, track, false)) return;
            if (track.Path == oldPath && track.Title == oldTitle) return;
            if (track == active && track.Path != oldPath) ResetPlayer();
            else if (track == active) nowTitle.Text = track.Title;
            if (oldPath != track.Path) track.Duration = -1;
            dirty = true;
            RefreshList(track);
        }

        private void MoveSelection(int delta)
        {
            var track = Selected;
            if (track != null) MoveTrack(track, playlist.Tracks.IndexOf(track) + delta);
        }

        private void MoveTrack(Track track, int index)
        {
            int old = playlist.Tracks.IndexOf(track);
            if (old < 0 || index < 0 || index >= playlist.Tracks.Count || old == index) return;
            playlist.Tracks.RemoveAt(old);
            playlist.Tracks.Insert(index, track);
            dirty = true;
            RefreshList(track);
        }

        private void Remove()
        {
            var track = Selected;
            if (track == null) return;
            int index = playlist.Tracks.IndexOf(track);
            if (track == active) ResetPlayer();
            playlist.Tracks.RemoveAt(index);
            order.Forget(track);
            dirty = true;
            RefreshList(playlist.Tracks.Count == 0 ? null : playlist.Tracks[Math.Min(index, playlist.Tracks.Count - 1)]);
        }

        private void Start(Track track)
        {
            ResetPlayer();
            try
            {
                string source = playlist.Resolve(track);
                Uri uri;
                if (!Uri.TryCreate(source, UriKind.Absolute, out uri)) throw new IOException("無法解析歌曲位置。");
                if (uri.IsFile && !File.Exists(uri.LocalPath)) throw new FileNotFoundException("找不到檔案，請檢查路徑或選擇音樂資料夾。\n" + uri.LocalPath);
                media = new MediaPlayer();
                media.MediaOpened += Opened;
                media.MediaEnded += Ended;
                media.MediaFailed += Failed;
                media.Volume = volume.Value / 100.0;
                active = track;
                LoadTrackArt(track, uri);
                signal = new TrackSignal(uri);
                nowTitle.Text = track.Title;
                state.Text = "載入中…";
                playing = true;
                playButton.Text = "暫停";
                media.Open(uri);
                media.Play();
                var item = tracks.Items[playlist.Tracks.IndexOf(track)];
                item.Selected = true;
                item.EnsureVisible();
            }
            catch (Exception e) { PlaybackError(e); }
        }

        private void Opened(object sender, EventArgs e)
        {
            if (sender != media) return;
            ready = true;
            duration = media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : 0;
            if (duration > 0) UpdateKnownDuration(active, duration);
            else if (active != null && active.Duration > 0) duration = active.Duration;
            progress.Enabled = duration > 0;
            state.Text = playing ? "播放中" : "已暫停";
            UpdateTime();
        }

        private void Ended(object sender, EventArgs e)
        {
            if (sender != media) return;
            var next = order.Next(playlist.Tracks, active, true);
            if (next != null) Start(next);
            else { ResetPlayer(); state.Text = "播放完畢"; }
        }

        private void Failed(object sender, System.Windows.Media.ExceptionEventArgs e)
        {
            if (sender == media) PlaybackError(e.ErrorException);
        }

        private void PlaybackError(Exception e)
        {
            ResetPlayer();
            state.Text = "播放失敗";
            ShowError("無法播放歌曲", e);
        }

        private void ResetPlayer()
        {
            if (signal != null) { signal.Dispose(); signal = null; }
            banner.SetSignal(false, null);
            stage.SetSignal(false, null);
            ClearTrackArt();
            var old = media;
            media = null;
            if (old != null)
            {
                old.MediaOpened -= Opened;
                old.MediaEnded -= Ended;
                old.MediaFailed -= Failed;
                old.Close();
            }
            playing = ready = seeking = false;
            active = null;
            duration = 0;
            progress.Value = 0;
            progress.Enabled = false;
            time.Text = "0:00 / 0:00";
            playButton.Text = "播放";
            nowTitle.Text = "尚未播放";
            state.Text = "已停止";
        }

        private void Toggle()
        {
            if (playlist.Tracks.Count == 0) return;
            if (media == null) { Start(order.Begin(playlist.Tracks, Selected)); return; }
            if (playing) media.Pause(); else media.Play();
            playing = !playing;
            playButton.Text = playing ? "暫停" : "播放";
            state.Text = playing ? (ready ? "播放中" : "載入中…") : "已暫停";
        }

        private void Skip(int delta)
        {
            if (playlist.Tracks.Count == 0) return;
            if (active == null) { Start(order.Begin(playlist.Tracks, Selected)); return; }
            var target = delta < 0 ? order.Previous(playlist.Tracks, active) : order.Next(playlist.Tracks, active, false);
            if (target != null) Start(target);
        }

        private void Seek()
        {
            if (media != null && ready && duration > 0)
                media.Position = TimeSpan.FromSeconds(duration * progress.Value / 1000);
        }

        private static string Stamp(double value)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, value));
            return span.TotalHours >= 1 ? String.Format("{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
                : String.Format("{0}:{1:00}", span.Minutes, span.Seconds);
        }

        private void UpdateTime()
        {
            if (!ready || media == null) return;
            if (duration <= 0)
            {
                if (media.NaturalDuration.HasTimeSpan)
                {
                    duration = media.NaturalDuration.TimeSpan.TotalSeconds;
                    if (duration > 0) UpdateKnownDuration(active, duration);
                }
                if (duration <= 0 && active != null && active.Duration > 0) duration = active.Duration;
                progress.Enabled = duration > 0;
            }
            double current = seeking ? duration * progress.Value / 1000 : media.Position.TotalSeconds;
            if (!seeking && duration > 0) progress.Value = (int)Math.Max(0, Math.Min(1000, 1000 * current / duration));
            time.Text = Stamp(current) + " / " + (duration > 0 ? Stamp(duration) : "—");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11 || (keyData == Keys.Escape && expanded)) { ToggleView(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { Import(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { Export(); return true; }
            if (tracks.Focused)
            {
                if (keyData == Keys.Space) { Toggle(); return true; }
                if (keyData == Keys.Delete) { Remove(); return true; }
                if (keyData == Keys.F2) { Edit(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowError(string title, Exception e)
        {
            MessageBox.Show(this, e == null ? title : e.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
