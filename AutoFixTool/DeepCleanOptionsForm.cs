using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>深度清理的可选项（多用户 / 安装包）。</summary>
    internal sealed class DeepCleanOptionsForm : Form
    {
        private CheckBox _multi;
        private CheckBox _installers;

        public bool MultiUser { get { return _multi.Checked; } }
        public bool CleanInstallers { get { return _installers.Checked; } }

        public DeepCleanOptionsForm()
        {
            AppIcon.Apply(this);
            Text = "深度清理选项";
            ClientSize = new Size(560, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(27, 33, 48) };
            header.Controls.Add(new Label
            {
                Text = "深度清理选项",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            });

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 6) };

            _multi = new CheckBox
            {
                Text = "同时清理其他用户配置文件中的 Autodesk 残留",
                Location = new Point(18, 14),
                Width = 500,
                Cursor = Cursors.Hand
            };
            var l1 = new Label
            {
                Text = "遍历 ProfileList，加载其他用户的 NTUSER.DAT 清除 Autodesk 注册表，并清理其 AppData。已登录用户会跳过。",
                Location = new Point(38, 36),
                Width = 490,
                Height = 32,
                ForeColor = Color.FromArgb(120, 130, 146)
            };

            _installers = new CheckBox
            {
                Text = "清理安装包与下载缓存",
                Location = new Point(18, 82),
                Width = 500,
                Cursor = Cursors.Hand
            };
            var l2 = new Label
            {
                Text = "删除 C:\\Autodesk、Uninstallers、ODIS metadata，以及「下载」目录中的 Autodesk 安装包。" + Environment.NewLine
                     + "注意：这会删除你自己下载保存的安装包，之后重装需要重新下载。",
                Location = new Point(38, 104),
                Width = 490,
                Height = 44,
                ForeColor = Color.FromArgb(120, 130, 146)
            };

            body.Controls.Add(_multi);
            body.Controls.Add(l1);
            body.Controls.Add(_installers);
            body.Controls.Add(l2);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(238, 240, 244) };
            var ok = new Button
            {
                Text = "下一步",
                Location = new Point(340, 11),
                Width = 96,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 127, 249),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(444, 11),
                Width = 88,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(52, 60, 76),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(206, 214, 226);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
