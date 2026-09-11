using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>清理后校验结果：10 项检查，标出异常项。全程只读。</summary>
    internal sealed class VerifyForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);
        private static readonly Color ColOk = Color.FromArgb(24, 128, 56);
        private static readonly Color ColIssue = Color.FromArgb(186, 96, 12);

        private readonly List<VerifyItem> _items;
        private DataGridView _grid;
        private Label _summary;

        public VerifyForm(List<VerifyItem> items)
        {
            AppIcon.Apply(this);
            _items = items;
            BuildUi();
            Fill();
        }

        private void BuildUi()
        {
            Text = "清理后校验";
            ClientSize = new Size(960, 600);
            MinimumSize = new Size(720, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "清理后校验",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "只读检查 · 判断系统是否已具备干净重装条件 · "
                   + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
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
            var btnExport = new Button
            {
                Text = "导出结果",
                Location = new Point(0, 10),
                Width = 90,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 127, 249),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.Click += (s, e) => Export();
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
            buttons.Controls.Add(btnExport);
            buttons.Controls.Add(btnClose);
            footer.Controls.Add(buttons);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 6) };
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
                Font = new Font("Microsoft YaHei UI", 9f),
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", Width = 36, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "检查项", Width = 170, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 60, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            var d = new DataGridViewTextBoxColumn
            {
                HeaderText = "说明",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            _grid.Columns.Add(d);
            host.Controls.Add(_grid);

            Controls.Add(host);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private void Fill()
        {
            _grid.Rows.Clear();
            int issues = 0;

            foreach (VerifyItem v in _items)
            {
                int i = _grid.Rows.Add(v.Index, v.Name, v.Status, v.Detail ?? "");
                DataGridViewCell cell = _grid.Rows[i].Cells[2];
                if (v.IsIssue)
                {
                    cell.Style.ForeColor = ColIssue;
                    cell.Style.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
                    issues++;
                }
                else if (v.Status == "正常")
                {
                    cell.Style.ForeColor = ColOk;
                }
                else
                {
                    cell.Style.ForeColor = ColMuted;
                }
            }

            if (issues == 0)
            {
                _summary.Text = "共 " + _items.Count + " 项检查，未发现异常 —— 系统已具备干净重装条件。"
                              + "（若安装程序提示需重启，先重启一次再安装。）";
            }
            else
            {
                _summary.Text = "共 " + _items.Count + " 项检查，" + issues + " 项异常，建议先处理后再安装。";
            }
        }

        private void Export()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "导出校验结果",
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "清理后校验_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Autodesk 清理后校验结果");
                    sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sb.AppendLine(new string('=', 72));
                    sb.AppendLine();
                    foreach (VerifyItem v in _items)
                    {
                        sb.AppendLine("[" + v.Index + "] " + v.Name + "  →  " + v.Status);
                        if (!string.IsNullOrWhiteSpace(v.Detail))
                        {
                            sb.AppendLine("      " + v.Detail.Replace("\r\n", "\n      "));
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine(new string('=', 72));
                    sb.AppendLine(_summary.Text);

                    System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
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
