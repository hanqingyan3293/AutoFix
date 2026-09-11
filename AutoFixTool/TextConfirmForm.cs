using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>
    /// 强确认对话框：必须手动输入指定文字（默认 YES）才能继续。
    /// 用于删除用户项目文件这类不可恢复的操作。
    /// </summary>
    internal sealed class TextConfirmForm : Form
    {
        private readonly string _expect;
        private TextBox _input;

        public TextConfirmForm(string title, string message, string expectWord)
        {
            _expect = string.IsNullOrEmpty(expectWord) ? "YES" : expectWord;

            Text = title;
            ClientSize = new Size(620, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(148, 44, 44) };
            header.Controls.Add(new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold),
                Location = new Point(16, 13),
                AutoSize = true
            });

            var body = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(247, 248, 250),
                ForeColor = Color.FromArgb(52, 60, 76),
                ScrollBars = ScrollBars.Vertical,
                Text = message
            };

            var bodyHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 6) };
            bodyHost.Controls.Add(body);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 108, BackColor = Color.FromArgb(238, 240, 244) };
            bottom.Controls.Add(new Label
            {
                Text = "此操作不可恢复。若确认继续，请在下方输入 " + _expect + " ：",
                Location = new Point(18, 12),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 44, 44),
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
            });

            _input = new TextBox
            {
                Location = new Point(18, 38),
                Width = 300,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f)
            };
            _input.TextChanged += (s, e) => UpdateOk();
            bottom.Controls.Add(_input);

            var lblHint = new Label
            {
                Location = new Point(330, 41),
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 130, 146),
                Text = "（区分大小写）"
            };
            bottom.Controls.Add(lblHint);

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(410, 68),
                Width = 88,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(52, 60, 76),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(206, 214, 226);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.Add(cancel);

            _ok = new Button
            {
                Text = "执行删除",
                Location = new Point(506, 68),
                Width = 96,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(148, 44, 44),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                Enabled = false
            };
            _ok.FlatAppearance.BorderSize = 0;
            _ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            bottom.Controls.Add(_ok);

            Controls.Add(bodyHost);
            Controls.Add(bottom);
            Controls.Add(header);

            AcceptButton = _ok;
            CancelButton = cancel;
        }

        private Button _ok;

        private void UpdateOk()
        {
            _ok.Enabled = string.Equals((_input.Text ?? "").Trim(), _expect, StringComparison.Ordinal);
        }
    }
}

