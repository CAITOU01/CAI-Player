using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Wpf = System.Windows;
using Ui = System.Windows.Controls;
using Media = System.Windows.Media;

namespace CaiPlayer
{
    internal sealed class Subtitle
    {
        internal double Start, End;
        internal string Text;
        private static readonly Regex timing = new Regex(@"(?m)^(?:(\d+):)?(\d{2}):(\d{2})[.,](\d{3})\s*-->\s*(?:(\d+):)?(\d{2}):(\d{2})[.,](\d{3})[^\n]*\n");
        private static double Seconds(Match match, int offset)
        {
            return (match.Groups[offset].Success ? int.Parse(match.Groups[offset].Value) * 3600.0 : 0)
                + int.Parse(match.Groups[offset + 1].Value) * 60 + int.Parse(match.Groups[offset + 2].Value)
                + int.Parse(match.Groups[offset + 3].Value) / 1000.0;
        }
        internal static List<Subtitle> Read(string file)
        {
            if (new FileInfo(file).Length > 10 * 1024 * 1024) throw new IOException("字幕檔超過 10 MB。");
            var entries = new List<Subtitle>();
            foreach (string block in Regex.Split(File.ReadAllText(file).Replace("\r\n", "\n").Replace('\r', '\n'), @"\n[ \t]*\n"))
            {
                var match = timing.Match(block);
                if (!match.Success) continue;
                string text = WebUtility.HtmlDecode(Regex.Replace(block.Substring(match.Index + match.Length), @"<[^>\n]*>", "")).Trim();
                if (text.Length > 0) entries.Add(new Subtitle { Start = Seconds(match, 1), End = Seconds(match, 5), Text = text });
            }
            return entries.OrderBy(c => c.Start).ToList();
        }
    }

    internal sealed partial class PlayerWindow
    {
        private List<Subtitle> subtitles = new List<Subtitle>();
        private Ui.TextBlock captionText;
        private bool fullVideo;
        private FormBorderStyle normalBorder;

        private void UpdateCaptions(double seconds)
        {
            if (captionText == null) return;
            int lo = 0, hi = subtitles.Count;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (subtitles[mid].Start <= seconds) lo = mid + 1; else hi = mid; }
            string text = lo > 0 && seconds < subtitles[lo - 1].End ? subtitles[lo - 1].Text : "";
            if (captionText.Text != text) captionText.Text = text;
            captionText.Visibility = text.Length == 0 ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
        }

        private void ToggleVideoScreen()
        {
            if (videoHost == null) return;
            if (!expanded) ToggleView();
            fullVideo = !fullVideo;
            if (fullVideo) { normalBorder = FormBorderStyle; FormBorderStyle = FormBorderStyle.None; }
            else FormBorderStyle = normalBorder;
            foreach (Control control in page.Controls)
                if (page.GetRow(control) == 0 || page.GetRow(control) == 1) control.Visible = !fullVideo;
            float dpi; using (var g = CreateGraphics()) dpi = g.DpiY / 96F;
            page.RowStyles[0].Height = fullVideo ? 0 : 66 * dpi;
            page.RowStyles[1].Height = fullVideo ? 0 : 48 * dpi;
            WindowState = FormWindowState.Maximized;
        }

        private Ui.Grid VideoSurface(Ui.Image image)
        {
            var grid = new Ui.Grid { Background = Media.Brushes.Black };
            grid.Children.Add(image);
            captionText = new Ui.TextBlock { FontSize = 24, Foreground = Media.Brushes.White,
                TextWrapping = Wpf.TextWrapping.Wrap, TextAlignment = Wpf.TextAlignment.Center,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center, VerticalAlignment = Wpf.VerticalAlignment.Bottom,
                Padding = new Wpf.Thickness(10, 3, 10, 3), Margin = new Wpf.Thickness(20),
                Background = new Media.SolidColorBrush(Media.Color.FromArgb(185, 0, 0, 0)), Visibility = Wpf.Visibility.Collapsed };
            grid.Children.Add(captionText);
            var menu = new Ui.ContextMenu();
            var load = new Ui.MenuItem { Header = "開啟字幕檔…" };
            load.Click += delegate {
                using (var picker = new OpenFileDialog { Title = "選擇字幕", Filter = "字幕檔|*.srt;*.vtt" }) {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    try { var entries = Subtitle.Read(picker.FileName); if (entries.Count == 0) throw new IOException("找不到有效的字幕時間。");
                        subtitles = entries; UpdateCaptions(Position); }
                    catch (Exception e) { ShowError("無法載入字幕", e); }
                }
            };
            var off = new Ui.MenuItem { Header = "關閉字幕" }; off.Click += delegate { subtitles.Clear(); UpdateCaptions(Position); };
            var screen = new Ui.MenuItem { Header = "切換全螢幕" }; screen.Click += delegate { ToggleVideoScreen(); };
            menu.Items.Add(load); menu.Items.Add(off); menu.Items.Add(screen); grid.ContextMenu = menu;
            grid.MouseLeftButtonDown += (sender, args) => { if (args.ClickCount == 2) ToggleVideoScreen(); };
            grid.PreviewKeyDown += (sender, args) => {
                if (fullVideo && args.Key == System.Windows.Input.Key.Escape) { ToggleVideoScreen(); args.Handled = true; }
            };
            if (media.Source.IsFile) foreach (string ext in new[] { ".srt", ".vtt" }) {
                string path = Path.ChangeExtension(media.Source.LocalPath, ext);
                if (File.Exists(path)) try { subtitles = Subtitle.Read(path); if (subtitles.Count != 0) break; } catch (Exception) { }
            }
            return grid;
        }
    }
}
