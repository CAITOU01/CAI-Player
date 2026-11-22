using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CaiPlayer
{
    internal sealed class NowPlayingView : Control
    {
        private readonly bool expanded;
        private bool dark, hasSamples;
        private string title = "尚未播放", status = "已停止";
        private Image cover;
        private float[] levels;

        public NowPlayingView(bool expanded)
        {
            this.expanded = expanded;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "播放中歌曲";
        }

        public void SetTheme(bool isDark, Color surface, Color text)
        {
            dark = isDark;
            BackColor = surface;
            ForeColor = text;
            Invalidate();
        }

        public void SetTrack(string song, string playbackStatus, Image artwork)
        {
            title = song;
            status = playbackStatus;
            cover = artwork;
            AccessibleDescription = status + "　" + title;
            Invalidate();
        }

        public void SetSignal(bool available, float[] samples)
        {
            if (!available && !hasSamples) return;
            if (available)
            {
                if (levels == null || levels.Length != samples.Length) levels = new float[samples.Length];
                bool changed = !hasSamples;
                for (int i = 0; i < samples.Length; i++)
                {
                    if (levels[i] != samples[i]) changed = true;
                    levels[i] = samples[i];
                }
                if (!changed) return;
            }
            hasSamples = available;
            if (Visible) Invalidate();
        }

        private Color Accent { get { return dark ? Color.FromArgb(50, 202, 236) : Color.FromArgb(174, 64, 17); } }
        private Color Muted { get { return dark ? Color.FromArgb(177, 191, 197) : Color.FromArgb(83, 98, 104); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = g.DpiX / 96F;
            if (expanded) DrawExpanded(g, scale);
            else DrawCompact(g, scale);
        }

        private void DrawCompact(Graphics g, float scale)
        {
            int size = Math.Min(Height - 4, (int)(58 * scale));
            if (size < 6) return;
            DrawCover(g, new Rectangle(0, (Height - size) / 2, size, size));
            int gap = (int)(13 * scale);
            int chartWidth = Math.Min(Width / 4, (int)(170 * scale));
            int titleX = size + gap;
            int titleW = Math.Max(1, Width - titleX - chartWidth - gap * 2);
            int statusH = Math.Max(16, (int)(22 * scale));
            int nameH = Math.Max(20, (int)(29 * scale));
            int textY = Math.Max(0, (Height - statusH - nameH) / 2);
            DrawText(g, status, new Rectangle(titleX, textY, titleW, statusH), 9F, false, Muted, false);
            DrawText(g, title, new Rectangle(titleX, textY + statusH, titleW, nameH), 13F, true, ForeColor, false);
            DrawWave(g, new Rectangle(Width - chartWidth - gap, (Height - size) / 2 + 4, chartWidth, size - 8), 32);
        }

        private void DrawExpanded(Graphics g, float scale)
        {
            int pad = Math.Max((int)(16 * scale), Math.Min(Width / 18, Height / 10));
            int side = Math.Min((int)(480 * scale), Math.Min(Width * 40 / 100, Height - pad * 2));
            if (side < 10) return;
            int gap = Math.Max(pad / 2, (int)(24 * scale));
            int content = Math.Min(Width - 2 * pad, side + gap + (int)(580 * scale));
            int left = (Width - content) / 2;
            DrawCover(g, new Rectangle(left, (Height - side) / 2, side, side));
            int x = left + side + gap, w = content - side - gap;
            if (w < 20) return;
            int captionH = (int)(25 * scale);
            int nameH = Math.Min((int)(106 * scale), Math.Max((int)(62 * scale), Height / 5));
            int waveH = Math.Min((int)(158 * scale), Math.Max((int)(42 * scale), Height / 4));
            int spacer = Math.Min(pad, (int)(24 * scale));
            int y = (Height - (captionH * 2 + nameH + spacer + waveH)) / 2;
            DrawText(g, status, new Rectangle(x, y, w, captionH), 10F, true, Accent, false);
            y += captionH;
            DrawText(g, title, new Rectangle(x, y, w, nameH), w > 420 * scale ? 26F : 20F, true, ForeColor, true);
            y += nameH + spacer;
            DrawText(g, "音訊波形", new Rectangle(x, y, w, captionH), 9.5F, false, Muted, false);
            DrawWave(g, new Rectangle(x, y + captionH, w, waveH), 64);
        }

        private void DrawText(Graphics g, string value, Rectangle box, float size, bool bold, Color ink, bool wrap)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            flags |= wrap ? TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
                          : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            using (var font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, value, font, box, ink, flags);
        }

        private void DrawCover(Graphics g, Rectangle box)
        {
            using (var background = new SolidBrush(dark ? Color.FromArgb(30, 36, 41) : Color.FromArgb(235, 237, 235)))
                g.FillRectangle(background, box);
            if (cover != null && cover.Width > 0 && cover.Height > 0)
            {
                float factor = Math.Min((float)box.Width / cover.Width, (float)box.Height / cover.Height);
                float w = cover.Width * factor, h = cover.Height * factor;
                g.DrawImage(cover, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                return;
            }
            // A neutral record sleeve stands in for tracks without artwork.
            float disc = box.Width * .72F, x = box.X + (box.Width - disc) / 2, y = box.Y + (box.Height - disc) / 2;
            using (var vinyl = new SolidBrush(Color.FromArgb(47, 54, 58))) g.FillEllipse(vinyl, x, y, disc, disc);
            using (var ring = new Pen(Color.FromArgb(79, 89, 93)))
                for (int i = 1; i <= 3; i++) {
                    float inset = disc * .055F * i;
                    g.DrawEllipse(ring, x + inset, y + inset, disc - inset * 2, disc - inset * 2);
                }
            float label = disc * .29F;
            using (var brush = new SolidBrush(Accent)) g.FillEllipse(brush, x + (disc - label) / 2, y + (disc - label) / 2, label, label);
            float hole = Math.Max(1F, disc * .045F);
            using (var brush = new SolidBrush(Color.FromArgb(35, 39, 41)))
                g.FillEllipse(brush, x + (disc - hole) / 2, y + (disc - hole) / 2, hole, hole);
        }

        private void DrawWave(Graphics g, Rectangle box, int limit)
        {
            if (box.Width <= 2 || box.Height <= 2) return;
            float mid = box.Top + box.Height / 2F;
            using (var pen = new Pen(dark ? Color.FromArgb(70, 83, 88) : Color.FromArgb(195, 202, 200)))
                g.DrawLine(pen, box.Left, mid, box.Right - 1, mid);
            if (!hasSamples || levels == null || levels.Length == 0) return;
            int count = Math.Min(limit, Math.Min(levels.Length, Math.Max(1, box.Width / 3)));
            float step = (float)box.Width / count;
            using (var brush = new SolidBrush(Accent))
                for (int i = 0; i < count; i++)
                {
                    float peak = 0;
                    for (int j = i * levels.Length / count; j < (i + 1) * levels.Length / count; j++)
                        peak = Math.Max(peak, levels[j]);
                    float h = Math.Min(1F, peak) * (box.Height - 2);
                    if (h >= 1F) g.FillRectangle(brush, box.Left + i * step, mid - h / 2, Math.Max(1, step * .72F), h);
                }
        }
    }
}
