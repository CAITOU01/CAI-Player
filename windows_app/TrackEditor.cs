using System;
using System.Drawing;
using System.Windows.Forms;

namespace CaiPlayer
{
    internal static class TrackEditor
    {
        public static bool Update(IWin32Window owner, Track track, bool urlOnly)
        {
            using (var dialog = new Form())
            {
                dialog.Text = urlOnly ? "加入串流網址" : "編輯歌曲";
                dialog.Font = new Font("Microsoft JhengHei UI", 10);
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimizeBox = dialog.MaximizeBox = false;
                dialog.ClientSize = new Size(520, 210);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 5, ColumnCount = 1 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var name = new TextBox { Text = track.Title, Dock = DockStyle.Fill };
                var source = new TextBox { Text = track.Path, Dock = DockStyle.Fill };
                layout.Controls.Add(new Label { AutoSize = true, Text = urlOnly ? "顯示名稱（選填）" : "顯示名稱" }, 0, 0);
                layout.Controls.Add(name, 0, 1);
                layout.Controls.Add(new Label { AutoSize = true, Text = urlOnly ? "串流網址（HTTP 或 HTTPS）" : "檔案路徑或網址", Margin = new Padding(3, 12, 3, 3) }, 0, 2);
                layout.Controls.Add(source, 0, 3);
                var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, WrapContents = false };
                var save = new Button { Text = urlOnly ? "加入" : "儲存", AutoSize = true };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
                save.Click += delegate
                {
                    string path = source.Text.Trim();
                    Uri uri;
                    if (path.Length == 0 || (urlOnly && (!Uri.TryCreate(path, UriKind.Absolute, out uri)
                        || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || String.IsNullOrEmpty(uri.Host))))
                    {
                        MessageBox.Show(dialog, urlOnly ? "請輸入完整的 HTTP 或 HTTPS 網址。" : "請輸入檔案路徑或網址。", dialog.Text);
                        source.Focus();
                        return;
                    }
                    track.Title = String.IsNullOrWhiteSpace(name.Text) ? path : name.Text.Trim();
                    track.Path = path;
                    dialog.DialogResult = DialogResult.OK;
                };
                buttons.Controls.Add(save);
                buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 4);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;
                dialog.ActiveControl = urlOnly ? source : name;
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
