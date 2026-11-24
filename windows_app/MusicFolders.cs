using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaiPlayer
{
    internal static class MusicFolders
    {
        internal const string Settings = @"HKEY_CURRENT_USER\Software\CAI Player";
        internal const string Extensions = "*.mp3;*.flac;*.m4a;*.aac;*.wav;*.wma;*.aiff;*.ogg;*.opus";
        internal const string VideoExtensions = "*.mp4;*.m4v;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.mpg;*.mpeg";

        internal static string Normalize(string input)
        {
            string path = input.Trim().Trim('"').Replace('/', '\\');
            string root = Path.GetPathRoot(path);
            if (String.IsNullOrEmpty(root) || root == "\\" || root.EndsWith(":") ||
                (root.StartsWith(@"\\") && root.Trim('\\').Split('\\').Length < 2))
                throw new ArgumentException("請輸入完整的資料夾路徑。");
            string full = Path.GetFullPath(path);
            string trimmed = full.TrimEnd('\\');
            return trimmed.EndsWith(":") ? trimmed + "\\" : trimmed;
        }

        internal static string[] Load()
        {
            return Registry.GetValue(Settings, "MusicFolders", null) as string[] ?? new string[0];
        }

        internal static void Save(string[] roots)
        {
            Registry.SetValue(Settings, "MusicFolders", roots, RegistryValueKind.MultiString);
        }

        internal sealed class ScanResult
        {
            internal readonly List<string> Files = new List<string>();
            internal readonly List<string> Unavailable = new List<string>();
        }

        internal static ScanResult Scan(string[] roots, bool videos = false)
        {
            var extensions = new HashSet<string>((videos ? VideoExtensions : Extensions).Split(';')
                .Select(pattern => pattern.Substring(1)), StringComparer.OrdinalIgnoreCase);
            var result = new ScanResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                try
                {
                    var paths = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly);
                    Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                    foreach (var path in paths)
                        if (extensions.Contains(Path.GetExtension(path)) && seen.Add(path)) result.Files.Add(path);
                }
                catch (IOException) { result.Unavailable.Add(root); }
                catch (UnauthorizedAccessException) { result.Unavailable.Add(root); }
                catch (System.Security.SecurityException) { result.Unavailable.Add(root); }
            }
            return result;
        }
    }

    internal sealed class MusicFoldersDialog : Form
    {
        private readonly ListBox list = new ListBox();
        private readonly TextBox path = new TextBox();
        private readonly CheckBox openLibrary = new CheckBox();
        internal string[] Roots { get { return list.Items.Cast<string>().ToArray(); } }
        internal bool OpenLibrary { get { return openLibrary.Checked; } }

        internal MusicFoldersDialog(string[] roots, bool library, bool video = false)
        {
            Text = video ? "影片資料夾" : "音樂資料夾";
            Font = new Font("Microsoft JhengHei UI", 10F);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimizeBox = MaximizeBox = false;
            ClientSize = new Size(620, 355);
            MinimumSize = new Size(430, 340);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (int row in new[] { 0, 1, 2, 3, 4, 5 })
                panel.RowStyles.Add(new RowStyle(row == 2 ? SizeType.Percent : SizeType.AutoSize, row == 2 ? 100 : 0));
            Controls.Add(panel);
            var hint = new Label { AutoSize = true, Text = "只掃描各資料夾最外層的" + (video ? "影片" : "音檔") + "。子資料夾請另外新增。" };
            panel.Controls.Add(hint, 0, 0);
            panel.SetColumnSpan(hint, 2);
            path.Dock = DockStyle.Fill;
            path.AccessibleName = "完整資料夾路徑";
            panel.Controls.Add(path, 0, 1);
            var addRow = Buttons();
            addRow.Controls.Add(Button("新增", delegate { AddPath(path.Text); }));
            addRow.Controls.Add(Button("瀏覽…", delegate {
                using (var picker = new FolderBrowserDialog { Description = "新增" + Text, ShowNewFolderButton = false })
                    if (picker.ShowDialog(this) == DialogResult.OK) AddPath(picker.SelectedPath);
            }));
            panel.Controls.Add(addRow, 1, 1);
            path.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddPath(path.Text); }
            };
            list.Dock = DockStyle.Fill;
            list.HorizontalScrollbar = true;
            list.AccessibleName = "已設定的" + Text;
            list.Items.AddRange(roots);
            panel.Controls.Add(list, 0, 2);
            panel.SetColumnSpan(list, 2);
            var edits = Buttons();
            edits.Controls.Add(Button("移除", delegate {
                int index = list.SelectedIndex;
                if (index < 0) return;
                list.Items.RemoveAt(index);
                list.SelectedIndex = Math.Min(index, list.Items.Count - 1);
            }));
            edits.Controls.Add(Button("上移", delegate { MoveSelected(-1); }));
            edits.Controls.Add(Button("下移", delegate { MoveSelected(1); }));
            panel.Controls.Add(edits, 0, 3);
            panel.SetColumnSpan(edits, 2);
            openLibrary.Text = "儲存後開啟資料夾歌庫";
            openLibrary.AutoSize = true;
            openLibrary.Checked = library;
            openLibrary.Visible = !library;
            panel.Controls.Add(openLibrary, 0, 4);
            panel.SetColumnSpan(openLibrary, 2);
            var actions = Buttons();
            actions.Anchor = AnchorStyles.Right;
            actions.Controls.Add(Button("儲存", delegate {
                if (String.IsNullOrWhiteSpace(path.Text) || AddPath(path.Text)) DialogResult = DialogResult.OK;
            }));
            var cancel = Button("取消", delegate { DialogResult = DialogResult.Cancel; });
            actions.Controls.Add(cancel);
            CancelButton = cancel;
            panel.Controls.Add(actions, 0, 5);
            panel.SetColumnSpan(actions, 2);
        }

        private static FlowLayoutPanel Buttons()
        {
            return new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        }

        private static Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += delegate { action(); };
            return button;
        }

        private bool AddPath(string input)
        {
            try
            {
                string full = MusicFolders.Normalize(input);
                int index = Array.FindIndex(Roots, entry => String.Equals(entry, full, StringComparison.OrdinalIgnoreCase));
                list.SelectedIndex = index >= 0 ? index : list.Items.Add(full);
                path.Clear();
                return true;
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, Text); return false; }
        }

        private void MoveSelected(int delta)
        {
            int index = list.SelectedIndex, target = index + delta;
            if (index < 0 || target < 0 || target >= list.Items.Count) return;
            object entry = list.Items[index];
            list.Items.RemoveAt(index);
            list.Items.Insert(target, entry);
            list.SelectedIndex = target;
        }
    }
}
