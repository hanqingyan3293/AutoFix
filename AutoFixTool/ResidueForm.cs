using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>残留检测结果页：按类型分组展示，支持筛选与导出。全程只读。</summary>
    internal sealed class ResidueForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);
        private static readonly Color ColTypes = Color.FromArgb(45, 127, 249);

        private readonly List<ResidueFinding> _all;
        private DataGridView _grid;
        private ComboBox _filter;
        private Label _summary;

        public ResidueForm(List<ResidueFinding> findings)
        {
            _all = findings;
            BuildUi();
            Fill();
        }

        private void BuildUi()
        {
            Text = "残留检测结果";
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "残留检测结果",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "只读扫描 · 未修改任何设置 · 生成于 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(20, 33),
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
            var btnExport = MakeButton("导出结果", 90, true);
            btnExport.Location = new Point(0, 10);
            btnExport.Click += (s, e) => Export();
            var btnClose = MakeButton("关闭", 80, false);
            btnClose.Location = new Point(100, 10);
            btnClose.Click += (s, e) => Close();
            buttons.Controls.Add(btnExport);
            buttons.Controls.Add(btnClose);
            footer.Controls.Add(buttons);

            var title = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(14, 6, 14, 0) };
            title.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "检测到的 Autodesk 残留",
                ForeColor = ColMuted,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            _filter = new ComboBox
            {
                Dock = DockStyle.Right,
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            _filter.Items.Add("全部类型");
            foreach (string t in new[]
            {
                "卸载项", "进程", "服务", "文件目录", "数据目录", "全盘搜索",
                "注册表", "COM 注册", "HKCU 类注册", "外壳扩展", "Installer 幽灵项",
                "快捷方式", "计划任务", "环境变量", "IFEO 劫持", "待处理重命名", "hosts 条目"
            })
            {
                _filter.Items.Add(t);
            }
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (s, e) => Fill();
            title.Controls.Add(_filter);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 6) };
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 30,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(232, 236, 242),
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "类型", Width = 80, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "路径 / 名称", Width = 420, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic
            });
            var d = new DataGridViewTextBoxColumn
            {
                HeaderText = "说明", ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            _grid.Columns.Add(d);
            host.Controls.Add(_grid);

            Controls.Add(host);
            Controls.Add(title);
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
            _grid.Rows.Clear();
            string want = _filter.SelectedIndex <= 0 ? null : _filter.SelectedItem.ToString();

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int shown = 0;

            foreach (ResidueFinding f in _all)
            {
                counts.TryGetValue(f.Type, out int c);
                counts[f.Type] = c + 1;

                if (want != null && !string.Equals(f.Type, want, StringComparison.Ordinal))
                {
                    continue;
                }
                int i = _grid.Rows.Add(f.Type, f.Path, f.Detail ?? "");
                _grid.Rows[i].Cells[0].Style.ForeColor = ColTypes;
                shown++;
            }

            if (shown == 0)
            {
                int i = _grid.Rows.Add("—", want == null ? "未发现任何 Autodesk 残留" : "该类型下无记录", "");
                _grid.Rows[i].DefaultCellStyle.ForeColor = ColMuted;
            }

            var parts = new List<string>();
            foreach (var kv in counts)
            {
                parts.Add(kv.Key + " " + kv.Value);
            }
            _summary.Text = _all.Count == 0
                ? "未发现 Autodesk 残留。"
                : "共 " + _all.Count + " 项" + (parts.Count > 0 ? "（" + string.Join(" · ", parts.ToArray()) + "）" : "");
        }

        private void Export()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "导出残留检测结果",
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "Autodesk残留检测_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    File.WriteAllText(dlg.FileName, BuildText(), Encoding.UTF8);
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

        private string BuildText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Autodesk 残留检测结果");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(new string('=', 72));
            sb.AppendLine();

            string type = null;
            foreach (ResidueFinding f in _all)
            {
                if (!string.Equals(type, f.Type, StringComparison.Ordinal))
                {
                    type = f.Type;
                    sb.AppendLine();
                    sb.AppendLine("【" + type + "】");
                    sb.AppendLine(new string('-', 72));
                }
                sb.AppendLine("   " + f.Path);
                if (!string.IsNullOrWhiteSpace(f.Detail))
                {
                    sb.AppendLine("       " + f.Detail);
                }
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 72));
            sb.AppendLine(_summary.Text);
            return sb.ToString();
        }
    }
}
