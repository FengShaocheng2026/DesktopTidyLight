using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using DesktopTidyLight;
internal static class UiRenderCheck
{
    private static void DrawChildren(Control parent, Graphics graphics, Point offset)
    {
        foreach (Control child in parent.Controls)
        {
            using (var bitmap = new Bitmap(child.Width, child.Height))
            { child.DrawToBitmap(bitmap, new Rectangle(Point.Empty, child.Size)); graphics.DrawImageUnscaled(bitmap, offset.X + child.Left, offset.Y + child.Top); }
            DrawChildren(child, graphics, new Point(offset.X + child.Left, offset.Y + child.Top));
        }
    }
    internal static int Run()
    {
        Application.EnableVisualStyles();
        Directory.CreateDirectory("build");
        using (var canvas = new Bitmap(1360, 780))
        using (var g = Graphics.FromImage(canvas))
        using (var wallpaper = new LinearGradientBrush(new Rectangle(0, 0, 1360, 780), Color.FromArgb(13, 25, 42), Color.FromArgb(40, 65, 73), 35f))
        {
            g.FillRectangle(wallpaper, 0, 0, 1360, 780);
            using (var title = new Font("Segoe UI", 24, FontStyle.Bold)) g.DrawString("Desktop Tidy", title, Brushes.White, 42, 30);
            g.DrawString("界面绘制预览 · 示例图标", Theme.BodyFont, new SolidBrush(Theme.Muted), 44, 82);
            using (var fence = new Bitmap(740, 560))
            using (var fg = Graphics.FromImage(fence))
            {
                fg.Clear(Color.Transparent);
                Theme.PaintContainer(fg, fence.Size, "工作空间", 6, false, false, null, true);
                var names = new[] { "项目资料", "设计参考", "工作文档", "应用工具", "本周计划", "待整理" };
                for (int i = 0; i < 6; i++)
                {
                    int x = 32 + (i % 4) * 160, y = 88 + (i / 4) * 180;
                    fg.DrawIcon(i % 2 == 0 ? SystemIcons.Application : SystemIcons.Information, new Rectangle(x + 44, y, 64, 64));
                    TextRenderer.DrawText(fg, names[i], Theme.BodyFont, new Rectangle(x, y + 76, 152, 46), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                }
                g.DrawImageUnscaled(fence, 40, 130);
            }
            using (var menu = Theme.Menu())
            {
                menu.Items.Add("重命名…"); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("删除分组…").Tag = "danger";
                menu.PerformLayout();
                using (var mb = new Bitmap(menu.Width, menu.Height)) { menu.DrawToBitmap(mb, new Rectangle(Point.Empty, mb.Size)); g.DrawImageUnscaled(mb, 820, 130); }
            }
            using (var settings = new SettingsForm(true, enabled => { }, false))
            {
                settings.CreateControl();
                using (var sb = new Bitmap(settings.ClientSize.Width, settings.ClientSize.Height))
                using (var sg = Graphics.FromImage(sb)) { sg.Clear(settings.BackColor); DrawChildren(settings, sg, Point.Empty); g.DrawImageUnscaled(sb, 820, 290); }
            }
            canvas.Save("build/ui-preview.png", ImageFormat.Png);
        }
        Console.WriteLine("Rendered build/ui-preview.png using application controls; no desktop capture.");
        return 0;
    }
}
