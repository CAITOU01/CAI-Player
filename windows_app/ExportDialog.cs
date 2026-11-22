using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CaiPlayer
{
    public sealed class ExportDialog : Form
    {
        private readonly Playlist playlist;
        private readonly ComboBox format = new ComboBox();
        private readonly ComboBox paths = new ComboBox();
        private readonly TextBox pathExample = new TextBox();
        private readonly TextBox extension = new TextBox();
        private readonly TextBox pattern = new TextBox();
        private readonly TextBox separator = new TextBox();
        private readonly TextBox preview = new TextBox();
        private readonly TableLayoutPanel grid = new TableLayoutPanel();

        public static bool Export(IWin32Window owner, Playlist playlist)
        {
            using (var form = new ExportDialog(playlist)) return form.ShowDialog(owner) == DialogResult.OK;
        }

        private ExportDialog(Playlist playlist)
        {
            this.playlist = playlist;
            Text = "匯出播放清單";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft JhengHei UI", 10f);
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            grid.AutoSize = true;
            grid.Padding = new Padding(16);
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            Controls.Add(grid);
            format.DropDownStyle = paths.DropDownStyle = ComboBoxStyle.DropDownList;
            format.Items.AddRange(new object[] { "VLC 播放清單（M3U8 / UTF-8）", "JSON", "自訂文字範本" });
            paths.Items.AddRange(new object[] { "相對於匯出檔案的位置", "保留清單原有路徑", "完整路徑" });
            paths.SelectedIndex = 0;
            AddRow("內容格式", format);
            AddRow("副檔名", extension);
            AddRow("歌曲路徑", paths);
            pathExample.ReadOnly = true;
            pathExample.Multiline = true;
            pathExample.Height = 76;
            pathExample.ScrollBars = ScrollBars.Vertical;
            AddRow("路徑格式範例", pathExample);
            pattern.Text = "{index}{sep}{title}{sep}{path}";
            AddRow("每首歌的範本", pattern);
            separator.Text = @"\t";
            AddRow("分隔符號", separator);
            var hint = new Label { Text = @"欄位可用 {index}、{title}、{duration}、{path}、{sep}。輸入 \t 可使用定位字元。", AutoSize = true, MaximumSize = new Size(350, 0) };
            AddRow("", hint);
            preview.ReadOnly = true;
            preview.Multiline = true;
            preview.Height = 65;
            preview.ScrollBars = ScrollBars.Vertical;
            AddRow("範本預覽", preview);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var save = new Button { Text = "另存新檔…", AutoSize = true };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            AddRow("", buttons);
            CancelButton = cancel;
            AcceptButton = save;
            format.SelectedIndexChanged += delegate {
                extension.Text = new string[] { "m3u8", "json", "txt" }[format.SelectedIndex];
                hint.Enabled = pattern.Enabled = separator.Enabled = preview.Enabled = format.SelectedIndex == 2;
                UpdatePreview();
            };
            pattern.TextChanged += delegate { UpdatePreview(); };
            separator.TextChanged += delegate { UpdatePreview(); };
            paths.SelectedIndexChanged += delegate { UpdatePreview(); };
            save.Click += delegate { SavePlaylist(); };
            format.SelectedIndex = 0;
        }

        private void AddRow(string label, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(6, 4, 3, 4);
            grid.Controls.Add(control, 1, row);
        }

        private string Separator { get { return separator.Text.Replace(@"\t", "\t"); } }

        private void UpdatePreview()
        {
            var style = (PathStyle)paths.SelectedIndex;
            var sample = new Playlist { MusicRoot = @"C:\Music" };
            var sampleTrack = new Track("歌曲名稱", "Album/song.mp3", 210);
            const string sampleFolder = @"C:\Playlists";
            pathExample.Text = "音樂資料夾  " + sample.MusicRoot + "　　匯出至  " + sampleFolder
                + Environment.NewLine + "原有路徑  " + sampleTrack.Path
                + Environment.NewLine + "寫入清單  " + sample.ExportPath(sampleTrack, sampleFolder, style);
            if (format.SelectedIndex != 2) { preview.Clear(); return; }
            if (playlist.Tracks.Count == 0)
            {
                preview.Text = Playlist.FormatRow(pattern.Text, Separator, 1, sampleTrack.Title,
                    sample.ExportPath(sampleTrack, sampleFolder, style), sampleTrack.Duration);
                return;
            }
            var track = playlist.Tracks[0];
            string basis = InitialDirectory;
            try
            {
                var path = playlist.ExportPath(track, basis, style);
                preview.Text = "以 " + basis + " 為匯出資料夾" + Environment.NewLine +
                    Playlist.FormatRow(pattern.Text, Separator, 1, track.Title, path, track.Duration);
            }
            catch (Exception e) { preview.Text = e.Message; }
        }

        private string InitialDirectory
        {
            get { return string.IsNullOrEmpty(playlist.SourcePath) ?
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(playlist.SourcePath); }
        }

        private void SavePlaylist()
        {
            try
            {
                string ext = extension.Text.Trim().TrimStart('.');
                if (ext.Length == 0 || ext.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ext.EndsWith(".") || ext.EndsWith(" "))
                    throw new FormatException("請輸入有效的副檔名，例如 m3u8 或 txt。");
                if (format.SelectedIndex == 2 && string.IsNullOrWhiteSpace(pattern.Text)) throw new FormatException("請輸入每首歌的範本。");
                string name = playlist.Name ?? "CAI Player";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "另存播放清單";
                    dialog.Filter = "播放清單 (*." + ext + ")|*." + ext + "|所有檔案 (*.*)|*.*";
                    dialog.FileName = name + " - CAI." + ext;
                    dialog.DefaultExt = ext;
                    dialog.AddExtension = dialog.OverwritePrompt = true;
                    dialog.InitialDirectory = InitialDirectory;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    playlist.Save(dialog.FileName, (PlaylistFormat)format.SelectedIndex, (PathStyle)paths.SelectedIndex, pattern.Text, Separator);
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "無法匯出播放清單", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
