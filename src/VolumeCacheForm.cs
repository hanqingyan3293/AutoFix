using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutodeskFix
{
    /// <summary>Windows 卷缓存清理：勾选要清理的缓存项后执行（调用系统自带清理引擎）。</summary>
    internal sealed class VolumeCacheForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);

        private readonly List<RepairService.VolumeCacheEntry> _entries;
        private CheckedListBox _list;
        private Label _summary;
        private Button _purge;

        public IList<string> SelectedKeys { get; private set; }
        public bool Confirmed { get; private set; }

        public VolumeCacheForm(List<RepairService.VolumeCacheEntry> entries)
        {
            AppIcon.Apply(this);
            _entries = entries;
            BuildUi();
            Fill();
        }

        private void BuildUi()
        {
            Text = "Windows 卷缓存清理";
            ClientSize = new Size(620, 480);
            MinimumSize = new Size(520, 400);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "Windows 卷缓存清理",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold),
                Location = new Point(16, 8),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "扫描结果来自 Windows 自带的磁盘清理引擎 · 只读扫描，勾选后才清理",
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(18, 31),
                AutoSize = true
            });

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.FromArgb(238, 240, 244) };
            _summary = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                ForeColor = Color.FromArgb(90, 100, 116)
            };
            footer.Controls.Add(_summary);

            var buttons = new Panel { Dock = DockStyle.Right, Width = 250, Padding = new Padding(0, 10, 14, 10) };
            _purge = MakeButton("清理选中项", 110, true);
            _purge.Location = new Point(0, 10);
            _purge.Click += (s, e) => OnPurge();
            var btnCancel = MakeButton("取消", 80, false);
            btnCancel.Location = new Point(120, 10);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(_purge);
            buttons.Controls.Add(btnCancel);
            footer.Controls.Add(buttons);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6) };
            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _list.ItemCheck += (s, e) => BeginInvoke((Action)UpdateSummary);
            host.Controls.Add(_list);

            Controls.Add(host);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static Button MakeButton(string text, int width, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                b.BackColor = Color.FromArgb(45, 127, 249);
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = Color.FromArgb(45, 127, 249);
            }
            else
            {
                b.BackColor = Color.White;
                b.ForeColor = Color.FromArgb(52, 60, 76);
                b.FlatAppearance.BorderColor = Color.FromArgb(206, 214, 226);
            }
            return b;
        }

        private void Fill()
        {
            if (_entries.Count == 0)
            {
                _list.Items.Add("（未发现可清理的卷缓存）", false);
                _list.Enabled = false;
                _purge.Enabled = false;
                _summary.Text = "未发现可清理的卷缓存。";
                return;
            }

            foreach (RepairService.VolumeCacheEntry e in _entries)
            {
                _list.Items.Add(e.Description + "   —   " + SizeText(e.Size), false);
            }
            UpdateSummary();
        }

        private static string SizeText(long bytes)
        {
            double v = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024.0 && i < units.Length - 1)
            {
                v /= 1024.0;
                i++;
            }
            return v.ToString("0.##") + " " + units[i];
        }

        private void UpdateSummary()
        {
            long total = 0;
            int n = 0;
            for (int i = 0; i < _entries.Count && i < _list.Items.Count; i++)
            {
                if (_list.GetItemChecked(i))
                {
                    total += _entries[i].Size;
                    n++;
                }
            }
            _summary.Text = n == 0
                ? "已选 0 项 · 合计 " + SizeText(0)
                : "已选 " + n + " 项 · 预计释放 " + SizeText(total);
        }

        private void OnPurge()
        {
            var keys = new List<string>();
            for (int i = 0; i < _entries.Count && i < _list.Items.Count; i++)
            {
                if (_list.GetItemChecked(i))
                {
                    keys.Add(_entries[i].Key);
                }
            }

            if (keys.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要清理的缓存项。", "未选择",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("即将清理以下 " + keys.Count + " 项卷缓存：");
            sb.AppendLine();
            for (int i = 0; i < _entries.Count && i < _list.Items.Count; i++)
            {
                if (_list.GetItemChecked(i))
                {
                    sb.AppendLine("  · " + _entries[i].Description + "（" + SizeText(_entries[i].Size) + "）");
                }
            }
            sb.AppendLine();
            sb.Append("该操作会删除这些缓存中的文件，不可撤销。确认继续？");

            if (MessageBox.Show(this, sb.ToString(), "确认清理",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                return;
            }

            SelectedKeys = keys;
            Confirmed = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
