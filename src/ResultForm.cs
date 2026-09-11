using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutodeskFix
{
    /// <summary>
    /// 检测结果页。上方为环境体检项（可筛出需要注意的项），
    /// 下方为已安装产品（按产品分组、可折叠，每项仍各占一行）。
    /// 全程只读，不修改任何系统设置。
    /// </summary>
    internal sealed class ResultForm : Form
    {
        /// <summary>产品分组的分组行标记。</summary>
        private sealed class GroupHeader
        {
            public string Key;
            public bool Collapsed;
            public readonly List<DataGridViewRow> Children = new List<DataGridViewRow>();
        }

        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);
        private static readonly Color ColOk = Color.FromArgb(24, 128, 56);
        private static readonly Color ColWarn = Color.FromArgb(186, 96, 12);
        private static readonly Color ColInfo = Color.FromArgb(72, 84, 104);
        private static readonly Color ColGroupRow = Color.FromArgb(238, 242, 248);

        private readonly EnvironmentReport _report;
        private readonly List<GroupHeader> _groups = new List<GroupHeader>();

        private DataGridView _checkGrid;
        private DataGridView _productGrid;
        private Label _summary;
        private CheckBox _warnOnly;

        public ResultForm(EnvironmentReport report)
        {
            AppIcon.Apply(this);
            _report = report;
            BuildUi();
            FillChecks();
            FillProducts();
            UpdateSummary();
        }

        private void BuildUi()
        {
            Text = "检测结果";
            ClientSize = new Size(1060, 720);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            // ---- 顶栏 ----
            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "检测结果",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "只读检测 · 未修改任何设置 · 生成于 " + _report.GeneratedAt,
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(20, 33),
                AutoSize = true
            });

            // ---- 底栏 ----
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(238, 240, 244) };
            _summary = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                ForeColor = Color.FromArgb(90, 100, 116)
            };
            footer.Controls.Add(_summary);

            var buttons = new Panel { Dock = DockStyle.Right, Width = 260, Padding = new Padding(0, 11, 14, 11) };
            var btnExport = MakeButton("导出报告", 100, true);
            btnExport.Location = new Point(0, 11);
            btnExport.Click += (s, e) => Export();
            var btnClose = MakeButton("关闭", 80, false);
            btnClose.Location = new Point(110, 11);
            btnClose.Click += (s, e) => Close();
            buttons.Controls.Add(btnExport);
            buttons.Controls.Add(btnClose);
            footer.Controls.Add(buttons);

            // ---- 上：环境概览 ----
            var topHost = new Panel { Dock = DockStyle.Top, Height = 296, Padding = new Padding(14, 10, 14, 4) };

            var topTitle = new Panel { Dock = DockStyle.Top, Height = 26 };
            topTitle.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "环境概览",
                ForeColor = ColMuted,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            _warnOnly = new CheckBox
            {
                Dock = DockStyle.Right,
                Width = 190,
                Text = "只显示需要注意的项",
                ForeColor = ColMuted,
                TextAlign = ContentAlignment.MiddleRight,
                Cursor = Cursors.Hand
            };
            _warnOnly.CheckedChanged += (s, e) => FillChecks();
            topTitle.Controls.Add(_warnOnly);

            _checkGrid = MakeGrid();
            _checkGrid.Columns.Add(MakeCol("分组", 96, true));
            _checkGrid.Columns.Add(MakeCol("检查项", 186, true));
            _checkGrid.Columns.Add(MakeCol("状态 / 当前值", 300, true));
            var noteCol = MakeCol("说明", 360, true);
            noteCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _checkGrid.Columns.Add(noteCol);

            topHost.Controls.Add(_checkGrid);
            topHost.Controls.Add(topTitle);

            // ---- 下：已安装产品 ----
            var bottomHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 6) };

            var bottomTitle = new Panel { Dock = DockStyle.Top, Height = 26 };
            bottomTitle.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "已安装的 Autodesk 系列产品      （点击分组行可折叠 / 展开）",
                ForeColor = ColMuted,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            var btnToggleAll = MakeButton("折叠全部分组", 120, false);
            btnToggleAll.Dock = DockStyle.Right;
            btnToggleAll.Height = 24;
            btnToggleAll.Click += (s, e) =>
            {
                bool anyExpanded = false;
                foreach (GroupHeader g in _groups)
                {
                    if (!g.Collapsed)
                    {
                        anyExpanded = true;
                        break;
                    }
                }
                foreach (GroupHeader g in _groups)
                {
                    SetGroupCollapsed(g, anyExpanded);
                }
                btnToggleAll.Text = anyExpanded ? "展开全部分组" : "折叠全部分组";
            };
            bottomTitle.Controls.Add(btnToggleAll);

            _productGrid = MakeGrid();
            _productGrid.CellMouseClick += ProductGridClick;
            _productGrid.Columns.Add(MakeCol("名称", 250, false));
            _productGrid.Columns.Add(MakeCol("版本", 96, false));
            _productGrid.Columns.Add(MakeCol("发布者", 130, false));
            _productGrid.Columns.Add(MakeCol("安装位置", 220, false));
            _productGrid.Columns.Add(MakeCol("安装日期", 88, false));
            _productGrid.Columns.Add(MakeCol("架构", 56, false));
            var sizeCol = MakeCol("占用", 80, false);
            sizeCol.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _productGrid.Columns.Add(sizeCol);

            bottomHost.Controls.Add(_productGrid);
            bottomHost.Controls.Add(bottomTitle);

            Controls.Add(bottomHost);
            Controls.Add(topHost);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static DataGridView MakeGrid()
        {
            return new DataGridView
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
        }

        private static DataGridViewTextBoxColumn MakeCol(string header, int width, bool sortable)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                SortMode = sortable ? DataGridViewColumnSortMode.Automatic : DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };
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

        // ---------- 体检项 ----------

        private void FillChecks()
        {
            _checkGrid.Rows.Clear();

            bool warnOnly = _warnOnly != null && _warnOnly.Checked;
            int shown = 0;

            foreach (CheckItem c in _report.Checks)
            {
                if (warnOnly && c.Level != CheckLevel.Warn)
                {
                    continue;
                }

                int i = _checkGrid.Rows.Add(c.Group, c.Name, c.Value, c.Note ?? "");
                DataGridViewCell cell = _checkGrid.Rows[i].Cells[2];
                switch (c.Level)
                {
                    case CheckLevel.Ok:
                        cell.Style.ForeColor = ColOk;
                        break;
                    case CheckLevel.Warn:
                        cell.Style.ForeColor = ColWarn;
                        cell.Style.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
                        break;
                    default:
                        cell.Style.ForeColor = ColInfo;
                        break;
                }
                shown++;
            }

            if (warnOnly && shown == 0)
            {
                _checkGrid.Rows.Add("—", "无需要注意的项", "全部正常", "");
            }
        }

        // ---------- 产品（分组 + 折叠） ----------

        private void FillProducts()
        {
            _productGrid.Rows.Clear();
            _groups.Clear();

            if (_report.Products.Count == 0)
            {
                int i = _productGrid.Rows.Add("未检测到 Autodesk 系列产品", "", "", "", "", "", "");
                _productGrid.Rows[i].DefaultCellStyle.ForeColor = ColMuted;
                return;
            }

            // 按分组键切分（列表已按分组键排序）
            string currentKey = null;
            var buffer = new List<ProductInfo>();
            foreach (ProductInfo p in _report.Products)
            {
                if (currentKey != null && !string.Equals(currentKey, p.GroupKey, StringComparison.Ordinal))
                {
                    AddProductGroup(currentKey, buffer);
                    buffer = new List<ProductInfo>();
                }
                currentKey = p.GroupKey;
                buffer.Add(p);
            }
            if (buffer.Count > 0)
            {
                AddProductGroup(currentKey, buffer);
            }
        }

        private void AddProductGroup(string key, List<ProductInfo> items)
        {
            // 单条记录不建分组行，避免噪音
            if (items.Count == 1)
            {
                AddProductRow(items[0]);
                return;
            }

            var g = new GroupHeader { Key = key, Collapsed = false };

            int hi = _productGrid.Rows.Add(
                "▼  " + key, items.Count + " 条记录", "", "", "", "", "");
            DataGridViewRow headerRow = _productGrid.Rows[hi];
            headerRow.DefaultCellStyle.BackColor = ColGroupRow;
            headerRow.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            headerRow.DefaultCellStyle.ForeColor = Color.FromArgb(52, 60, 76);
            headerRow.Cells[0].Style.ForeColor = Color.FromArgb(45, 127, 249);
            headerRow.Tag = g;
            _groups.Add(g);

            foreach (ProductInfo p in items)
            {
                DataGridViewRow row = AddProductRow(p);
                g.Children.Add(row);
            }
        }

        private DataGridViewRow AddProductRow(ProductInfo p)
        {
            int i = _productGrid.Rows.Add(
                p.Name, p.Version, p.Publisher, p.InstallLocation, p.InstallDate,
                p.Architecture, FormatSize(p.EstimatedSizeKB));
            return _productGrid.Rows[i];
        }

        private void ProductGridClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _productGrid.Rows.Count)
            {
                return;
            }
            var g = _productGrid.Rows[e.RowIndex].Tag as GroupHeader;
            if (g == null)
            {
                return;
            }
            SetGroupCollapsed(g, !g.Collapsed);
        }

        private void SetGroupCollapsed(GroupHeader g, bool collapsed)
        {
            // 折叠前把当前单元格移到分组行，避免隐藏行时报错
            if (collapsed && _productGrid.Rows.Count > 0)
            {
                DataGridViewRow headerRow = FindHeaderRow(g);
                if (headerRow != null)
                {
                    try
                    {
                        _productGrid.CurrentCell = headerRow.Cells[0];
                    }
                    catch { }
                }
            }

            foreach (DataGridViewRow row in g.Children)
            {
                try
                {
                    row.Visible = !collapsed;
                }
                catch { }
            }
            g.Collapsed = collapsed;

            DataGridViewRow hr = FindHeaderRow(g);
            if (hr != null)
            {
                try
                {
                    hr.Cells[0].Value = (collapsed ? "▶  " : "▼  ") + g.Key;
                }
                catch { }
            }
        }

        private DataGridViewRow FindHeaderRow(GroupHeader g)
        {
            foreach (DataGridViewRow row in _productGrid.Rows)
            {
                if (ReferenceEquals(row.Tag, g))
                {
                    return row;
                }
            }
            return null;
        }

        // ---------- 汇总 / 导出 ----------

        private static string FormatSize(long kb)
        {
            if (kb <= 0)
            {
                return "";
            }
            double v = kb;
            string[] units = { "KB", "MB", "GB", "TB" };
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
            int warn = 0;
            foreach (CheckItem c in _report.Checks)
            {
                if (c.Level == CheckLevel.Warn)
                {
                    warn++;
                }
            }

            string products = _report.Products.Count == 0
                ? "未检测到 Autodesk 系列产品"
                : "检测到 " + _report.Products.Count + " 个产品（" + _groups.Count + " 个分组）";

            string checks = warn == 0
                ? "环境检查 " + _report.Checks.Count + " 项，未发现异常"
                : "环境检查 " + _report.Checks.Count + " 项，" + warn + " 项需要注意";

            _summary.Text = products + " · " + checks;
        }

        private void Export()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "导出检测报告",
                Filter = "文本文件 (*.txt)|*.txt",
                FileName = "Autodesk检测报告_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                try
                {
                    File.WriteAllText(dlg.FileName, BuildReportText(), Encoding.UTF8);
                    MessageBox.Show(this, "报告已导出：" + dlg.FileName, "导出成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "导出失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
        }

        private string BuildReportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Autodesk 修复工具箱 · 检测报告");
            sb.AppendLine("生成时间：" + _report.GeneratedAt);
            sb.AppendLine(new string('=', 72));
            sb.AppendLine();

            sb.AppendLine("【环境概览】");
            sb.AppendLine(new string('-', 72));
            string group = null;
            foreach (CheckItem c in _report.Checks)
            {
                if (!string.Equals(group, c.Group, StringComparison.Ordinal))
                {
                    group = c.Group;
                    sb.AppendLine();
                    sb.AppendLine("· " + group);
                }
                string mark = c.Level == CheckLevel.Warn ? "[注意]" :
                              (c.Level == CheckLevel.Ok ? "[正常]" : "[信息]");
                sb.AppendLine("   " + mark + " " + c.Name + "：" + c.Value);
                if (!string.IsNullOrWhiteSpace(c.Note))
                {
                    sb.AppendLine("          " + c.Note);
                }
            }

            sb.AppendLine();
            sb.AppendLine("【已安装的 Autodesk 系列产品】");
            sb.AppendLine(new string('-', 72));
            if (_report.Products.Count == 0)
            {
                sb.AppendLine("   未检测到。");
            }
            else
            {
                group = null;
                foreach (ProductInfo p in _report.Products)
                {
                    if (!string.Equals(group, p.GroupKey, StringComparison.Ordinal))
                    {
                        group = p.GroupKey;
                        sb.AppendLine();
                        sb.AppendLine("· " + group);
                    }
                    sb.AppendLine("   " + p.Name +
                                  (string.IsNullOrEmpty(p.Version) ? "" : "  " + p.Version) +
                                  (string.IsNullOrEmpty(p.Architecture) ? "" : "  (" + p.Architecture + ")"));
                    if (!string.IsNullOrEmpty(p.Publisher))
                    {
                        sb.AppendLine("      发布者：" + p.Publisher);
                    }
                    if (!string.IsNullOrEmpty(p.InstallLocation))
                    {
                        sb.AppendLine("      安装位置：" + p.InstallLocation);
                    }
                    if (!string.IsNullOrEmpty(p.InstallDate))
                    {
                        sb.AppendLine("      安装日期：" + p.InstallDate);
                    }
                    string size = FormatSize(p.EstimatedSizeKB);
                    if (!string.IsNullOrEmpty(size))
                    {
                        sb.AppendLine("      占用空间：" + size);
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 72));
            sb.AppendLine(_summary.Text);
            return sb.ToString();
        }
    }
}
