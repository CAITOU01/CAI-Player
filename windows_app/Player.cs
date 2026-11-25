using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
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
        private bool folderLibrary = true;
        private int folderScan;
        private readonly ListView tracks = new ListView();
        private readonly Label emptyHint = new Label();
        private readonly Label queueTitle = new Label(), nowTitle = new Label(), state = new Label(), time = new Label();
        private readonly PictureBox brand = new PictureBox();
        private readonly TrackBar progress = new TrackBar(), volume = new TrackBar();
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer timer = new Timer();
        private readonly TableLayoutPanel page = new TableLayoutPanel(), deck = new TableLayoutPanel();
        private readonly NowPlayingView banner = new NowPlayingView(false), stage = new NowPlayingView(true);
        private System.Windows.Forms.Integration.ElementHost videoHost;
        private Panel listArea;
        private readonly float[] levels = new float[64];
        private TrackSignal signal;
        private bool expanded;
        private FormWindowState previousSize;
        private FlowLayoutPanel editActions;
        private Button themeButton, playButton, viewButton;
        private readonly PlaybackOrder order = new PlaybackOrder();
        private Button shuffle, mute;
        private int audibleVolume = 75;
        private readonly Font symbolFont = new Font("Segoe MDL2 Assets", 13);
        private Button repeat;
        private readonly ContextMenuStrip repeatMenu = new ContextMenuStrip();
        private Button editButton, removeButton, upButton, downButton;
        private MediaPlayer media;
        private Track active, dragItem;
        private bool dark, playing, ready, dirty, seeking;
        private double duration;
        private int shownSecond = -1, shownDuration = -1;
        private double playbackSpeed = 1;
        private readonly ContextMenuStrip speedMenu = new ContextMenuStrip();
        private const string Product = "CAI Player";
        private const string Version = "V0.06";

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
                var savedGains = Registry.GetValue(MusicFolders.Settings, "Equalizer", null) as byte[];
                if (savedGains != null && savedGains.Length == gains.Length && Array.TrueForAll(savedGains, v => v <= 24))
                    gains = Array.ConvertAll(savedGains, v => v - 12);
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
            timer.Interval = 250;
            timer.Tick += delegate {
                bool mainVisible = Visible && WindowState != FormWindowState.Minimized;
                bool chartVisible = miniBanner != null || (mainVisible && !(expanded && videoHost != null));
                int interval = ready && playing && chartVisible ? 55 : (mainVisible && playing && captionText != null ? 80 : 250);
                if (timer.Interval != interval) timer.Interval = interval;
                if (miniPlay != null) {
                    if (miniPlay.AccessibleName != playButton.AccessibleName) Glyph(miniPlay, playButton.Text, playButton.AccessibleName);
                    miniPlay.Enabled = playButton.Enabled;
                }
                if (!ready || (!mainVisible && miniBanner == null)) return;
                double current = seeking ? duration * progress.Value / 1000 : Position;
                if (mainVisible) UpdateTime(current);
                if (chartVisible) UpdateSpectrum(current);
            };
            timer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !MayReplace()) e.Cancel = true; };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); StopMetadataScan(); ResetPlayer(); if (brand.Image != null) brand.Image.Dispose(); tips.Dispose(); repeatMenu.Dispose(); speedMenu.Dispose(); symbolFont.Dispose(); };
            Shown += delegate
            {
                ReloadSidebar();
                try { playlist.MusicRoots = MusicFolders.Load(); }
                catch (Exception e) { ShowError("無法讀取音樂資料夾設定", e); }
                if (args.Length != 0) OpenPaths(args);
                else if (playlist.MusicRoots.Length != 0) RefreshFolderLibrary();
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

        private void Glyph(Button button, string symbol, string name)
        {
            button.Text = symbol;
            button.AccessibleName = name;
            tips.SetToolTip(button, name);
            if (button == playButton) UpdateThumbnail();
        }

        private Button IconButton(string symbol, string name, Action action, bool accent = false)
        {
            var button = ActionButton("", action, accent);
            button.AutoSize = false;
            button.Size = new Size(40, 33);
            button.Padding = Padding.Empty;
            button.Font = symbolFont;
            Glyph(button, symbol, name);
            return button;
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
            actions.Controls.Add(ActionButton("我的播放清單", ManagePlaylists, true));
            actions.Controls.Add(ActionButton("音樂庫", BrowseLibrary));
            actions.Controls.Add(ActionButton("音樂資料夾", ChooseRoot));
            actions.Controls.Add(IconButton("\uE8B5", "匯入清單", Import));
            actions.Controls.Add(IconButton("\uE710", "加入音檔", ChooseFiles));
            actions.Controls.Add(IconButton("\uE8B2", "開啟影片", ChooseVideo));
            actions.Controls.Add(IconButton("\uE958", "音樂 CD", OpenDisc));
            actions.Controls.Add(IconButton("\uE71B", "加入網址", AddUrl));
            actions.Controls.Add(IconButton("\uE74E", "匯出清單", delegate { Export(); }));
            actions.Controls.Add(IconButton("\uE8A7", "迷你播放器", ShowMini));
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
            listArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
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
            EventHandler centerHint = delegate
            {
                emptyHint.Location = new Point((listArea.Width - emptyHint.PreferredWidth) / 2, (listArea.Height - emptyHint.PreferredHeight) / 2);
            };
            listArea.Resize += centerHint;
            emptyHint.SizeChanged += centerHint;
            var browser = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            browser.Controls.Add(listArea);
            BuildSidebar(browser);
            page.Controls.Add(browser, 0, 3);
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
            shuffle = IconButton("\uE8B1", "隨機播放已關閉", delegate {
                order.SetShuffle(!order.Shuffle, active);
                RefreshOrderControls();
            });
            transport.Controls.Add(shuffle);
            transport.Controls.Add(IconButton("\uE892", "上一首", delegate { Skip(-1); }));
            transport.Controls.Add(playButton = IconButton("\uE768", "播放", Toggle, true));
            transport.Controls.Add(IconButton("\uE893", "下一首", delegate { Skip(1); }));
            repeat = IconButton("\uE8EE", "循環關閉", delegate { repeatMenu.Show(repeat, new Point(0, repeat.Height)); });
            repeat.AccessibleDescription = "選擇歌曲播完時的循環模式";
            foreach (string modeName in new[] { "循環關閉", "單曲循環", "清單循環" })
            {
                var mode = (RepeatMode)repeatMenu.Items.Count;
                var item = new ToolStripMenuItem(modeName) { Checked = mode == order.Repeat };
                item.Click += delegate
                {
                    ((ToolStripMenuItem)repeatMenu.Items[(int)order.Repeat]).Checked = false;
                    order.Repeat = mode;
                    RefreshOrderControls();
                };
                repeatMenu.Items.Add(item);
            }
            transport.Controls.Add(repeat);
            var speed = IconButton("\uEC4A", "播放速度 1×", delegate {});
            foreach (double rate in new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 }) {
                var item = new ToolStripMenuItem(rate + "×") { Checked = rate == 1 };
                item.Click += delegate {
                    playbackSpeed = rate;
                    if (media != null) media.SpeedRatio = rate;
                    if (equalized != null) equalized.SetSpeed(rate);
                    foreach (ToolStripMenuItem other in speedMenu.Items) other.Checked = other == item;
                    Glyph(speed, "\uEC4A", "播放速度 " + rate + "×");
                };
                speedMenu.Items.Add(item);
            }
            speed.Click += delegate { speedMenu.Show(speed, new Point(0, speed.Height)); };
            transport.Controls.Add(speed);
            transport.Controls.Add(IconButton("\uE9E9", "等化器", EqualizerDialog));
            deck.Controls.Add(transport, 0, 2);
            var volRow = Row();
            volRow.AutoSize = true;
            volRow.Anchor = AnchorStyles.Right;
            volRow.Dock = DockStyle.None;
            mute = IconButton("\uE767", "靜音", delegate {
                if (volume.Value > 0) { audibleVolume = volume.Value; volume.Value = 0; }
                else volume.Value = audibleVolume;
            });
            volRow.Controls.Add(mute);
            volume.Width = 110;
            volume.Height = 26;
            volume.AutoSize = false;
            volume.Maximum = 100;
            volume.Value = 75;
            volume.TickStyle = TickStyle.None;
            volume.AccessibleName = "音量";
            volume.ValueChanged += delegate {
                if (media != null) media.Volume = volume.Value / 100.0;
                if (equalizerOutput != null) equalizerOutput.Volume = volume.Value / 100F;
                tips.SetToolTip(volume, volume.Value + "%");
                Glyph(mute, volume.Value == 0 ? "\uE74F" : "\uE767", volume.Value == 0 ? "取消靜音" : "靜音");
            };
            volRow.Controls.Add(volume);
            deck.Controls.Add(volRow, 1, 2);
            page.Controls.Add(deck, 0, 5);
        }

        private void RefreshOrderControls()
        {
            Glyph(shuffle, "\uE8B1", order.Shuffle ? "隨機播放已開啟" : "隨機播放已關閉");
            shuffle.Tag = order.Shuffle ? "accent" : null;
            for (int i = 0; i < repeatMenu.Items.Count; i++)
                ((ToolStripMenuItem)repeatMenu.Items[i]).Checked = i == (int)order.Repeat;
            string label = ((ToolStripMenuItem)repeatMenu.Items[(int)order.Repeat]).Text;
            Glyph(repeat, order.Repeat == RepeatMode.One ? "\uE8ED" : "\uE8EE", label);
            repeat.Tag = order.Repeat == RepeatMode.Off ? null : "accent";
            SetButtonTheme(shuffle);
            SetButtonTheme(repeat);
        }

        private void ToggleView()
        {
            expanded = !expanded;
            if (expanded) previousSize = WindowState;
            page.SuspendLayout();
            deck.SuspendLayout();
            queueTitle.Visible = editActions.Visible = tracks.Visible = !expanded;
            sidebar.Visible = !expanded;
            emptyHint.Visible = !expanded && playlist.Tracks.Count == 0;
            stage.Visible = expanded;
            if (expanded) stage.BringToFront();
            if (videoHost != null) {
                videoHost.Visible = expanded;
                if (expanded) videoHost.BringToFront();
            }
            banner.Visible = !expanded;
            // AutoScaleMode.Dpi also scales the absolute row heights on initial layout.
            float dpi;
            using (var g = CreateGraphics()) dpi = g.DpiY / 96F;
            page.RowStyles[2].Height = expanded ? 0 : 40 * dpi;
            page.RowStyles[4].Height = expanded ? 0 : 44 * dpi;
            page.RowStyles[5].Height = (expanded ? 107 : 180) * dpi;
            viewButton.Text = expanded ? "返回清單" : "大視窗模式";
            viewButton.AccessibleDescription = expanded ? "離開大視窗模式" : "顯示歌曲封面與頻譜";
            deck.ResumeLayout(true);
            page.ResumeLayout(true);
            WindowState = expanded ? FormWindowState.Maximized : previousSize;
            if (ready) UpdateSpectrum(Position);
        }

        private NowPlayingView miniBanner;
        private Button miniPlay;

        private void ShowMini()
        {
            using (var mini = new Form { Text = "CAI Player · 迷你播放器", Font = Font, TopMost = true,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow,
                MinimumSize = new Size(300, 170), ClientSize = new Size(400, 176), BackColor = deck.BackColor })
            {
                miniBanner = new NowPlayingView(false) { Dock = DockStyle.Fill };
                miniBanner.SetTheme(dark, deck.BackColor, tracks.ForeColor);
                miniBanner.SetTrack(nowTitle.Text, state.Text, currentCover);
                var buttons = Row(); buttons.Dock = DockStyle.Bottom; buttons.Height = 46;
                buttons.Controls.Add(IconButton("\uE892", "上一首", () => Skip(-1)));
                miniPlay = IconButton(playButton.Text, playButton.AccessibleName, Toggle, true);
                buttons.Controls.Add(miniPlay);
                buttons.Controls.Add(IconButton("\uE893", "下一首", () => Skip(1)));
                buttons.Controls.Add(IconButton("\uE8A7", "返回主視窗", mini.Close));
                miniBanner.DoubleClick += delegate { mini.Close(); };
                mini.Controls.Add(miniBanner); mini.Controls.Add(buttons);
                mini.StartPosition = FormStartPosition.Manual;
                mini.Location = new Point(Left + Math.Max(0, (Width - mini.Width) / 2), Top + 50);
                Hide();
                try { mini.ShowDialog(); }
                finally { miniBanner = null; miniPlay = null; Show(); Activate(); }
            }
        }

        private void RefreshCoverDisplay()
        {
            banner.SetTrack(nowTitle.Text, state.Text, currentCover);
            stage.SetTrack(nowTitle.Text, state.Text, currentCover);
            if (miniBanner != null) miniBanner.SetTrack(nowTitle.Text, state.Text, currentCover);
        }

        private void UpdateSpectrum(double current)
        {
            bool available = ready && signal != null &&
                signal.TryGetLevels(current, levels);
            if (available && miniBanner == null) (expanded ? stage : banner).SetSignal(true, levels);
            else if (!ready || signal == null) {
                banner.SetSignal(false, null);
                stage.SetSignal(false, null);
            }
            if (miniBanner != null) {
                if (available) miniBanner.SetSignal(true, levels);
                else if (!ready || signal == null) miniBanner.SetSignal(false, null);
            }
        }

        private void SetTheme()
        {
            var background = dark ? Color.FromArgb(26, 29, 33) : Color.FromArgb(247, 248, 248);
            var surface = dark ? Color.FromArgb(38, 43, 47) : Color.White;
            var ink = dark ? Color.FromArgb(235, 238, 239) : Color.FromArgb(29, 37, 42);
            BackColor = background;
            ForeColor = ink;
            deck.BackColor = surface;
            banner.SetTheme(dark, surface, ink);
            stage.SetTheme(dark, surface, ink);
            tracks.BackColor = surface;
            tracks.ForeColor = ink;
            sidebar.BackColor = surface;
            sidebar.ForeColor = ink;
            emptyHint.BackColor = surface;
            emptyHint.ForeColor = dark ? Color.FromArgb(175, 190, 196) : Color.FromArgb(91, 105, 115);
            foreach (Control c in AllControls(this))
            {
                var b = c as Button;
                if (b != null) SetButtonTheme(b);
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

        private void SetButtonTheme(Button button)
        {
            bool primary = (string)button.Tag == "accent";
            var accent = dark ? Color.FromArgb(57, 204, 238) : Color.FromArgb(172, 58, 12);
            button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? accent : deck.BackColor;
            button.ForeColor = primary ? (dark ? Color.Black : Color.White) : ForeColor;
            button.FlatAppearance.BorderColor = primary ? accent :
                (dark ? Color.FromArgb(74, 83, 89) : Color.FromArgb(194, 203, 209));
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
            emptyHint.Text = "將音檔或播放清單拖到這裡";
            emptyHint.Visible = !expanded && playlist.Tracks.Count == 0;
            if (tracks.SelectedItems.Count != 0) tracks.SelectedItems[0].EnsureVisible();
            queueTitle.Text = (String.IsNullOrWhiteSpace(playlist.Name) ? "播放清單" : playlist.Name) + "   ·   " + playlist.Tracks.Count + " 首";
            SyncSidebar();
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
            UpdateThumbnail();
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
                folderLibrary = false;
                savedListId = null;
                imported.MusicRoots = playlist.MusicRoots;
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
                Filter = "音樂檔案|" + MusicFolders.Extensions + "|所有檔案|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
        }

        private void ChooseVideo()
        {
            using (var picker = new OpenFileDialog { Title = "開啟影片", Filter = "影片|" + MusicFolders.VideoExtensions + "|所有檔案|*.*" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK || !MayReplace()) return;
                ResetPlayer(); order.Reset(null); ++folderScan;
                savedListId = null; folderLibrary = false; dirty = false;
                playlist = new Playlist { MusicRoots = playlist.MusicRoots, Name = "影片" };
                var track = new Track(Path.GetFileNameWithoutExtension(picker.FileName), picker.FileName);
                playlist.Tracks.Add(track); RefreshList(track); Start(track);
            }
        }

        private void ShowVideo()
        {
            var drawing = new System.Windows.Media.VideoDrawing { Player = media,
                Rect = new System.Windows.Rect(0, 0, Math.Max(1, media.NaturalVideoWidth), Math.Max(1, media.NaturalVideoHeight)) };
            var image = new System.Windows.Controls.Image { Source = new System.Windows.Media.DrawingImage(drawing),
                Stretch = System.Windows.Media.Stretch.Uniform };
            videoHost = new System.Windows.Forms.Integration.ElementHost { Dock = DockStyle.Fill, BackColor = Color.Black, Child = VideoSurface(image) };
            listArea.Controls.Add(videoHost);
            if (!expanded) ToggleView();
            videoHost.BringToFront();
        }

        private void AddFiles(string[] paths)
        {
            Track first = null;
            var added = new List<string>();
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var track = new Track(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path));
                added.Add(track.Path);
                playlist.Tracks.Add(track);
                if (first == null) first = track;
            }
            if (first != null) { RecentSongs.RememberAdded(added); MarkEdited(); RefreshList(first); }
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
            RecentSongs.RememberAdded(new[] { track.Path });
            MarkEdited();
            RefreshList(track);
        }

        private void MarkEdited()
        {
            smartQueue = null;
            dirty = true;
            folderLibrary = false;
            folderScan++;
            SaveNamedQueue();
        }

        private void ChooseRoot()
        {
            using (var dialog = new MusicFoldersDialog(playlist.MusicRoots, folderLibrary))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.OpenLibrary && !MayReplace()) return;
                try { MusicFolders.Save(dialog.Roots); }
                catch (Exception e) { ShowError("無法儲存音樂資料夾", e); return; }
                string oldSource = active == null ? null : LocalSource(active);
                playlist.MusicRoots = dialog.Roots;
                if (active != null && !String.Equals(oldSource, LocalSource(active), StringComparison.OrdinalIgnoreCase))
                    ResetPlayer();
                if (dialog.OpenLibrary)
                {
                    if (!folderLibrary)
                    {
                        ResetPlayer();
                        order.Reset(null);
                        playlist = new Playlist { MusicRoots = dialog.Roots };
                        savedListId = null;
                        dirty = false;
                        RefreshList(null);
                    }
                    folderLibrary = true;
                    RefreshFolderLibrary();
                }
                else
                {
                    QueueDurationScan();
                    state.Text = "已儲存音樂資料夾";
                }
            }
        }

        private async void RefreshFolderLibrary()
        {
            int request = ++folderScan;
            string[] roots = playlist.MusicRoots;
            if (active == null) state.Text = "正在掃描音樂資料夾…";
            var result = await Task.Run(() => MusicFolders.Scan(roots));
            if (request != folderScan || !folderLibrary || IsDisposed || Disposing) return;
            var previous = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in playlist.Tracks) previous[track.Path] = track;
            var focus = Selected;
            var songs = new List<Track>();
            foreach (string file in result.Files)
            {
                Track track;
                if (!previous.TryGetValue(file, out track)) track = new Track(Path.GetFileNameWithoutExtension(file), file);
                songs.Add(track);
                previous.Remove(file);
            }
            foreach (var removed in previous.Values) order.Forget(removed);
            if (active != null && previous.ContainsKey(active.Path)) ResetPlayer();
            playlist.Tracks = songs;
            playlist.Name = "資料夾歌庫";
            RefreshList(focus);
            if (result.Unavailable.Count != 0) queueTitle.Text += "   ·   " + result.Unavailable.Count + " 個資料夾無法讀取";
            if (active == null) state.Text = "歌庫已更新，共 " + songs.Count + " 首";
            tips.SetToolTip(banner, result.Unavailable.Count == 0 ? "按兩下開啟大視窗模式" : String.Join(Environment.NewLine, result.Unavailable));
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
            MarkEdited();
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
            MarkEdited();
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
            MarkEdited();
            RefreshList(playlist.Tracks.Count == 0 ? null : playlist.Tracks[Math.Min(index, playlist.Tracks.Count - 1)]);
        }

        private void Start(Track track)
        {
            StartAt(track, 0, true);
        }

        private void StartAt(Track track, double position, bool play)
        {
            ResetPlayer();
            try
            {
                string source = playlist.Resolve(track);
                Uri uri;
                if (!Uri.TryCreate(source, UriKind.Absolute, out uri)) throw new IOException("無法解析歌曲位置。");
                if (uri.IsFile && !File.Exists(uri.LocalPath)) throw new FileNotFoundException("找不到檔案，請檢查路徑或選擇音樂資料夾。\n" + uri.LocalPath);
                active = track;
                LoadTrackArt(track, uri);
                signal = new TrackSignal(uri);
                nowTitle.Text = track.Title;
                state.Text = "載入中…";
                playing = play;
                Glyph(playButton, play ? "\uE769" : "\uE768", play ? "暫停" : "播放");
                if (uri.IsFile && CanEqualize(uri.LocalPath) && Array.Exists(gains, v => v != 0)) StartEqualized(uri, position, play);
                else
                {
                    openedPosition = position;
                    media = new MediaPlayer();
                    media.MediaOpened += Opened; media.MediaEnded += Ended; media.MediaFailed += Failed;
                    media.Volume = volume.Value / 100.0;
                    media.Open(uri);
                    if (play) media.Play();
                }
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
            media.SpeedRatio = playbackSpeed;
            if (openedPosition > 0) media.Position = TimeSpan.FromSeconds(openedPosition);
            if (media.HasVideo && videoHost == null) ShowVideo();
            if (playing) RecordRecent(media.Source);
            duration = media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : 0;
            if (duration > 0) UpdateKnownDuration(active, duration);
            else if (active != null && active.Duration > 0) duration = active.Duration;
            progress.Enabled = duration > 0;
            state.Text = playing ? "播放中" : "已暫停";
            UpdateTime(Position);
        }

        private void Ended(object sender, EventArgs e)
        {
            if (sender != media) return;
            Advance();
        }

        private void Advance()
        {
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
            if (fullVideo) ToggleVideoScreen();
            captionText = null; subtitles.Clear();
            var output = equalizerOutput; equalizerOutput = null;
            if (output != null) { output.Stop(); output.Dispose(); }
            if (equalized != null) { equalized.Dispose(); equalized = null; }
            if (videoHost != null) { videoHost.Child = null; videoHost.Dispose(); videoHost = null; }
            if (signal != null) { signal.Dispose(); signal = null; }
            banner.SetSignal(false, null);
            stage.SetSignal(false, null);
            if (miniBanner != null) miniBanner.SetSignal(false, null);
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
            shownSecond = shownDuration = -1;
            Glyph(playButton, "\uE768", "播放");
            nowTitle.Text = "尚未播放";
            state.Text = "已停止";
        }

        private void Toggle()
        {
            if (playlist.Tracks.Count == 0) return;
            if (media == null && equalizerOutput == null) { Start(order.Begin(playlist.Tracks, Selected)); return; }
            if (equalizerOutput != null) { if (playing) equalizerOutput.Pause(); else equalizerOutput.Play(); }
            else if (playing) media.Pause(); else media.Play();
            playing = !playing;
            if (ready && playing) RecordRecent(media != null ? media.Source : new Uri(playlist.Resolve(active)));
            Glyph(playButton, playing ? "\uE769" : "\uE768", playing ? "暫停" : "播放");
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
            if (ready && duration > 0) Position = duration * progress.Value / 1000;
        }

        private static string Stamp(double value)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, value));
            return span.TotalHours >= 1 ? String.Format("{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds)
                : String.Format("{0}:{1:00}", span.Minutes, span.Seconds);
        }

        private void UpdateTime(double current)
        {
            if (!ready) return;
            if (duration <= 0)
            {
                if (media != null && media.NaturalDuration.HasTimeSpan)
                {
                    duration = media.NaturalDuration.TimeSpan.TotalSeconds;
                    if (duration > 0) UpdateKnownDuration(active, duration);
                }
                if (duration <= 0 && active != null && active.Duration > 0) duration = active.Duration;
                progress.Enabled = duration > 0;
            }
            UpdateCaptions(current);
            if (!seeking && duration > 0) progress.Value = (int)Math.Max(0, Math.Min(1000, 1000 * current / duration));
            int second = (int)current, total = duration > 0 ? (int)duration : -1;
            if (second != shownSecond || total != shownDuration) {
                shownSecond = second;
                shownDuration = total;
                time.Text = Stamp(current) + " / " + (duration > 0 ? Stamp(duration) : "—");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (fullVideo && (keyData == Keys.Escape || keyData == Keys.F11)) { ToggleVideoScreen(); return true; }
            if (keyData == Keys.MediaPlayPause) { Toggle(); return true; }
            if (keyData == Keys.MediaNextTrack) { Skip(1); return true; }
            if (keyData == Keys.MediaPreviousTrack) { Skip(-1); return true; }
            if (keyData == Keys.MediaStop) { ResetPlayer(); return true; }
            if (keyData == (Keys.Control | Keys.Right) || keyData == (Keys.Control | Keys.Left)) {
                if (ready && duration > 0) Position = Math.Max(0, Math.Min(duration,
                    Position + (keyData == (Keys.Control | Keys.Right) ? 10 : -10)));
                return true;
            }
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
