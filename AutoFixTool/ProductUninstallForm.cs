using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>
    /// 产品卸载清理：列出已安装的 Autodesk 产品，勾选后按阶段卸载。
    /// 阶段：还原点 → 停进程/服务 → 多轮卸载 → 深度清理 → 复查。
    /// </summary>
    internal sealed class ProductUninstallForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);
        private static readonly Color ColAccent = Color.FromArgb(45, 127, 249);

        private readonly List<ProductItem> _products;
        private CheckedListBox _list;
        private CheckBox _restorePoint;
        private CheckBox _deepClean;
        private CheckBox _multiUser;
        private CheckBox _installers;
        private Label _summary;
        private Button _run;

        public List<ProductItem> SelectedProducts { get; private set; }
        public bool CreateRestorePoint { get; private set; }
        public bool DeepClean { get; private set; }
        public bool MultiUser { get; private set; }
        public bool CleanInstallers { get; private set; }
        public bool Confirmed { get; private set; }

        public ProductUninstallForm(List<ProductItem> products)
        {
            _products = products;
            BuildUi();
            Fill();
        }

        private void BuildUi()
        {
            Text = "产品卸载清理";
            ClientSize = new Size(820, 620);
            MinimumSize = new Size(680, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "Autodesk 产品卸载清理",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "勾选要卸载的产品 · 按「还原点 → 停进程/服务 → 多轮卸载 → 深度清理 → 复查」执行",
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(20, 33),
                AutoSize = true
            });

            // ---- 底部 ----
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.FromArgb(238, 240, 244) };
            _summary = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                ForeColor = Color.FromArgb(90, 100, 116)
            };
            footer.Controls.Add(_summary);

            var buttons = new Panel { Dock = DockStyle.Right, Width = 240, Padding = new Padding(0, 12, 14, 12) };
            _run = MakeButton("开始卸载", 100, true);
            _run.Location = new Point(0, 12);
            _run.Click += (s, e) => OnRun();
            var cancel = MakeButton("取消", 80, false);
            cancel.Location = new Point(112, 12);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(_run);
            buttons.Controls.Add(cancel);
            footer.Controls.Add(buttons);

            // ---- 选项区 ----
            var options = new Panel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(16, 8, 16, 0) };
            _restorePoint = new CheckBox
            {
                Text = "卸载前创建系统还原点（建议开启，失败则继续）",
                Location = new Point(16, 10),
                Width = 460,
                Checked = true,
                Cursor = Cursors.Hand
            };
            _deepClean = new CheckBox
            {
                Text = "深度清理残留（目录 / 快捷方式 / 缓存 / 注册表 / 服务注册）",
                Location = new Point(16, 34),
                Width = 560,
                Cursor = Cursors.Hand
            };
            _multiUser = new CheckBox
            {
                Text = "同时清理其他用户配置文件中的 Autodesk 残留",
                Location = new Point(16, 58),
                Width = 560,
                Cursor = Cursors.Hand
            };
            _installers = new CheckBox
            {
                Text = "清理安装包与下载缓存（会删除「下载」目录中的 Autodesk 安装包）",
                Location = new Point(16, 82),
                Width = 620,
                Cursor = Cursors.Hand
            };
            options.Controls.Add(_restorePoint);
            options.Controls.Add(_deepClean);
            options.Controls.Add(_multiUser);
            options.Controls.Add(_installers);

            var toggles = new Panel { Dock = DockStyle.Right, Width = 200, Padding = new Padding(0, 8, 14, 0) };
            var all = MakeButton("全选", 80, false);
            all.Location = new Point(0, 10);
            all.Click += (s, e) => SetAll(true);
            var none = MakeButton("全不选", 80, false);
            none.Location = new Point(90, 10);
            none.Click += (s, e) => SetAll(false);
            toggles.Controls.Add(all);
            toggles.Controls.Add(none);
            options.Controls.Add(toggles);

            // ---- 产品列表 ----
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 16, 6) };
            listHost.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "已安装的 Autodesk 产品",
                ForeColor = ColMuted,
                TextAlign = ContentAlignment.MiddleLeft
            });
            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                IntegralHeight = false,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _list.ItemCheck += (s, e) => BeginInvoke((Action)UpdateSummary);
            listHost.Controls.Add(_list);

            Controls.Add(listHost);
            Controls.Add(options);
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
                b.BackColor = ColAccent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = ColAccent;
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
            if (_products.Count == 0)
            {
                _list.Items.Add("（未检测到已安装的 Autodesk 产品）");
                _list.Enabled = false;
                _run.Enabled = false;
                _summary.Text = "未检测到可卸载的 Autodesk 产品。";
                return;
            }

            foreach (ProductItem p in _products)
            {
                string ver = string.IsNullOrEmpty(p.Version) ? "" : "   " + p.Version;
                _list.Items.Add(p.Name + ver, false);
            }
            UpdateSummary();
        }

        private void SetAll(bool value)
        {
            for (int i = 0; i < _list.Items.Count; i++)
            {
                _list.SetItemChecked(i, value);
            }
            UpdateSummary();
        }

        private List<ProductItem> Checked()
        {
            var list = new List<ProductItem>();
            for (int i = 0; i < _products.Count && i < _list.Items.Count; i++)
            {
                if (_list.GetItemChecked(i))
                {
                    list.Add(_products[i]);
                }
            }
            return list;
        }

        private void UpdateSummary()
        {
            int n = Checked().Count;
            _summary.Text = "已选 " + n + " / " + _products.Count + " 项"
                          + (_deepClean != null && _deepClean.Checked ? " · 含深度清理" : "");
        }

        private void OnRun()
        {
            var sel = Checked();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要卸载的产品。", "未选择",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("即将卸载以下 " + sel.Count + " 个产品：");
            sb.AppendLine();
            foreach (ProductItem p in sel)
            {
                sb.AppendLine("  · " + p.Name + (string.IsNullOrEmpty(p.Version) ? "" : "  " + p.Version));
            }
            sb.AppendLine();
            sb.AppendLine("执行流程：");
            if (_restorePoint.Checked)
            {
                sb.AppendLine("  · 创建系统还原点");
            }
            sb.AppendLine("  · 结束 Autodesk 相关进程、停止相关服务");
            sb.AppendLine("  · 逐项卸载（最多重试 3 轮）");
            if (_deepClean.Checked)
            {
                sb.AppendLine("  · 深度清理：删除残留目录、快捷方式、缓存与注册表分支");
                sb.AppendLine();
                sb.AppendLine("⚠ 深度清理会删除 Autodesk 目录与注册表分支，不可撤销。");
            }
            if (_multiUser.Checked)
            {
                sb.AppendLine("  · 清理其他用户配置文件中的 Autodesk 注册表与 AppData（已登录用户会跳过）");
            }
            if (_installers.Checked)
            {
                sb.AppendLine("  · 清理安装包与下载缓存（含「下载」目录中的 Autodesk 安装包）");
            }
            sb.AppendLine();
            sb.Append("确认开始卸载？");

            if (MessageBox.Show(this, sb.ToString(), "确认卸载",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                return;
            }

            SelectedProducts = sel;
            CreateRestorePoint = _restorePoint.Checked;
            DeepClean = _deepClean.Checked;
            MultiUser = _multiUser.Checked;
            CleanInstallers = _installers.Checked;
            Confirmed = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
