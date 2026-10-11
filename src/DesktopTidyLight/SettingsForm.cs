using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal sealed class SettingsForm : Form
    {
        internal SettingsForm(bool autoStart, Action<bool> setAutoStart, bool autoArrange)
        {
            Text = "DesktopTidyLight 设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(380, 160);
            var start = new CheckBox
            {
                Text = "登录 Windows 后自动启动",
                Checked = autoStart,
                AutoSize = true,
                Location = new Point(20, 24)
            };
            bool reverting = false;
            start.CheckedChanged += (sender, args) =>
            {
                if (reverting) return;
                try { setAutoStart(start.Checked); }
                catch (Exception error)
                {
                    reverting = true;
                    start.Checked = !start.Checked;
                    reverting = false;
                    Dialogs.Message(error.Message, "自启动设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            var arrange = new Label
            {
                Text = autoArrange ? "桌面图标当前由 Windows 自动排列。新建分组时会暂时关闭。" : "桌面图标当前可自由排列。",
                AutoSize = true,
                Location = new Point(20, 68)
            };
            var close = new Button { Text = "关闭", Location = new Point(280, 112), Width = 80 };
            close.Click += (sender, args) => Close();
            Controls.Add(start);
            Controls.Add(arrange);
            Controls.Add(close);
        }
    }
}
