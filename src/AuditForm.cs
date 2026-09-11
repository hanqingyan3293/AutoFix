using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutodeskFix
{
    /// <summary>完整系统审计报告（只读预览）。</summary>
    internal sealed class AuditForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private readonly string _report;
        private TextBox _text;

        public AuditForm(string report)
        {
            AppIcon.Apply(this);
            _report = report;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "完整系统审计";
            ClientSize = new Size(900, 660);
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "完整系统审计",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "只读预览 · 列出完整清理会删除的全部内容 · 未修改任何设置",
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(20, 33),
                AutoSize = true
            });

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.FromArgb(238, 240, 244) };
            var buttons = new Panel { Dock = DockStyle.Right, Width = 250, Padding = new Padding(0, 10, 14, 10) };
            var btnSave = new Button
            {
                Text = "导出报告",
                Location = new Point(0, 10),
                Width = 90,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 127, 249),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) => Save();
            var btnClose = new Button
            {
                Text = "关闭",
                Location = new Point(100, 10),
                Width = 80,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(52, 60, 76),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(206, 214, 226);
            btnClose.Click += (s, e) => Close();
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnClose);
            footer.Controls.Add(buttons);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6) };
            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                Text = _report
            };
            host.Controls.Add(_text);

            Controls.Add(host);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private void Save()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "导出审计报告",
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "Autodesk系统审计_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    File.WriteAllText(dlg.FileName, _report, Encoding.UTF8);
                    MessageBox.Show(this, "已导出：" + dlg.FileName, "导出成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
        }
    }
}
