using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AutodeskFix
{
    /// <summary>
    /// 许可配置：为 Autodesk 官方的 AdskLicensingInstHelper.exe 提供图形界面。
    /// 支持切换网络 / 单机 / 用户许可，以及重置许可、查询产品密钥。
    /// </summary>
    internal sealed class LicenseForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);
        private static readonly Color ColAccent = Color.FromArgb(45, 127, 249);

        private readonly LicenseMethod _method;
        private readonly bool _lookupOnly;

        private ComboBox _year;
        private TextBox _search;
        private ListBox _products;
        private ComboBox _serverType;
        private TextBox _servers;
        private Label _serverTypeLabel;
        private Label _serversLabel;
        private TextBox _preview;
        private Button _copy;
        private Button _run;
        private Label _status;
        private Label _methodLabel;

        private List<KeyValuePair<string, string>> _filtered = new List<KeyValuePair<string, string>>();

        public LicenseForm(LicenseMethod method, bool lookupOnly)
        {
            AppIcon.Apply(this);
            _method = method;
            _lookupOnly = lookupOnly;
            BuildUi();
            ReloadProducts();
        }

        private void BuildUi()
        {
            Text = _lookupOnly ? "查询产品密钥" : "许可配置";
            ClientSize = new Size(760, 600);
            MinimumSize = new Size(680, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = _lookupOnly ? "查询产品密钥" : "Autodesk 许可配置",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = _lookupOnly
                    ? "按年份浏览 Autodesk 产品名称与产品密钥对照表 · 只读"
                    : "封装 Autodesk 官方 AdskLicensingInstHelper.exe · 需要已安装许可组件",
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(20, 33),
                AutoSize = true
            });

            // ---- 底部按钮 ----
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Color.FromArgb(238, 240, 244) };
            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 0, 0),
                ForeColor = Color.FromArgb(90, 100, 116)
            };
            footer.Controls.Add(_status);

            var buttons = new Panel { Dock = DockStyle.Right, Width = 300, Padding = new Padding(0, 10, 14, 10) };
            var btnClose = MakeButton("关闭", 70, false);
            btnClose.Location = new Point(216, 10);
            btnClose.Click += (s, e) => Close();
            buttons.Controls.Add(btnClose);

            if (!_lookupOnly)
            {
                _copy = MakeButton("复制命令", 88, false);
                _copy.Location = new Point(0, 10);
                _copy.Click += (s, e) => CopyCommand();
                buttons.Controls.Add(_copy);

                _run = MakeButton("执行", 88, true);
                _run.Location = new Point(110, 10);
                _run.Click += (s, e) => RunCommand();
                buttons.Controls.Add(_run);
            }
            footer.Controls.Add(buttons);

            // ---- 顶部：年份 + 搜索 ----
            var top = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(16, 10, 16, 0) };

            top.Controls.Add(new Label
            {
                Text = "版本年份",
                Location = new Point(16, 12),
                AutoSize = true,
                ForeColor = ColMuted
            });
            _year = new ComboBox
            {
                Location = new Point(16, 32),
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            foreach (int y in RepairService.LicenseYears)
            {
                _year.Items.Add(y);
            }
            _year.SelectedIndex = 0;
            _year.SelectedIndexChanged += (s, e) => ReloadProducts();
            top.Controls.Add(_year);

            top.Controls.Add(new Label
            {
                Text = "搜索产品",
                Location = new Point(146, 12),
                AutoSize = true,
                ForeColor = ColMuted
            });
            _search = new TextBox
            {
                Location = new Point(146, 32),
                Width = 560,
                BorderStyle = BorderStyle.FixedSingle
            };
            _search.TextChanged += (s, e) => ApplyFilter();
            top.Controls.Add(_search);

            // ---- 产品列表 ----
            var listHost = new Panel { Dock = DockStyle.Top, Height = 190, Padding = new Padding(16, 4, 16, 0) };
            _products = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                IntegralHeight = false,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _products.SelectedIndexChanged += (s, e) => UpdatePreview();
            listHost.Controls.Add(_products);

            Controls.Add(listHost);
            Controls.Add(top);
            Controls.Add(footer);
            Controls.Add(header);

            if (!_lookupOnly)
            {
                // ---- 许可参数 ----
                var cfg = new Panel { Dock = DockStyle.Top, Height = 132, Padding = new Padding(16, 4, 16, 0) };

                _methodLabel = new Label
                {
                    Location = new Point(16, 8),
                    AutoSize = true,
                    ForeColor = Color.FromArgb(52, 60, 76),
                    Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                    Text = "许可方式：" + RepairService.MethodDisplayName(_method)
                };
                cfg.Controls.Add(_methodLabel);

                _serverTypeLabel = new Label
                {
                    Text = "服务器类型",
                    Location = new Point(16, 40),
                    AutoSize = true,
                    ForeColor = ColMuted
                };
                cfg.Controls.Add(_serverTypeLabel);

                _serverType = new ComboBox
                {
                    Location = new Point(16, 60),
                    Width = 150,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat
                };
                _serverType.Items.AddRange(new object[] { "SINGLE", "REDUNDANT", "DISTRIBUTED" });
                _serverType.SelectedIndex = 0;
                _serverType.SelectedIndexChanged += (s, e) => UpdatePreview();
                cfg.Controls.Add(_serverType);

                _serversLabel = new Label
                {
                    Text = "许可服务器（多个用分号分隔）",
                    Location = new Point(184, 40),
                    AutoSize = true,
                    ForeColor = ColMuted
                };
                cfg.Controls.Add(_serversLabel);

                _servers = new TextBox
                {
                    Location = new Point(184, 60),
                    Width = 522,
                    BorderStyle = BorderStyle.FixedSingle
                };
                _servers.TextChanged += (s, e) => UpdatePreview();
                cfg.Controls.Add(_servers);

                Controls.Add(cfg);

                // ---- 命令预览 ----
                var prevHost = new Panel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(16, 0, 16, 8) };
                prevHost.Controls.Add(new Label
                {
                    Dock = DockStyle.Top,
                    Height = 22,
                    Text = "将执行的命令",
                    ForeColor = ColMuted
                });
                _preview = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    BackColor = Color.FromArgb(240, 243, 248),
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 9f),
                    ScrollBars = ScrollBars.Vertical
                };
                prevHost.Controls.Add(_preview);
                Controls.Add(prevHost);

                bool network = _method == LicenseMethod.Network;
                _serverType.Enabled = network;
                _servers.Enabled = network;
                _serverTypeLabel.ForeColor = network ? ColMuted : Color.FromArgb(190, 196, 206);
                _serversLabel.ForeColor = network ? ColMuted : Color.FromArgb(190, 196, 206);
            }
            else
            {
                Controls.SetChildIndex(listHost, 0);
                ClientSize = new Size(760, 520);
            }

            UpdateStatus();
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

        private int SelectedYear
        {
            get
            {
                if (_year.SelectedItem == null)
                {
                    return 2027;
                }
                int y;
                return int.TryParse(_year.SelectedItem.ToString(), out y) ? y : 2027;
            }
        }

        private void ReloadProducts()
        {
            _filtered = RepairService.ProductsForYear(SelectedYear);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = (_search.Text ?? "").Trim();
            _products.BeginUpdate();
            _products.Items.Clear();

            int shown = 0;
            foreach (var kv in _filtered)
            {
                if (q.Length > 0 && kv.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                _products.Items.Add(kv.Key + "    [" + kv.Value + "]");
                shown++;
            }
            _products.EndUpdate();

            if (_products.Items.Count > 0)
            {
                _products.SelectedIndex = 0;
            }
            UpdatePreview();
            UpdateStatus();
        }

        private KeyValuePair<string, string>? SelectedProduct
        {
            get
            {
                int i = _products.SelectedIndex;
                if (i < 0)
                {
                    return null;
                }
                // 列表项与 _filtered 的对应关系需要重新计算（搜索会过滤）
                string q = (_search.Text ?? "").Trim();
                int idx = 0;
                foreach (var kv in _filtered)
                {
                    if (q.Length > 0 && kv.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    if (idx == i)
                    {
                        return kv;
                    }
                    idx++;
                }
                return null;
            }
        }

        private void UpdatePreview()
        {
            if (_lookupOnly || _preview == null)
            {
                return;
            }
            var sel = SelectedProduct;
            if (sel == null)
            {
                _preview.Text = "（请先在上方选择一个产品）";
                return;
            }
            var st = _serverType.SelectedItem == null
                ? LicenseServerType.Single
                : (LicenseServerType)Enum.Parse(typeof(LicenseServerType), _serverType.SelectedItem.ToString(), true);

            _preview.Text = RepairService.BuildLicenseCommand(sel.Value.Value, SelectedYear,
                _method, st, _servers.Text);
        }

        private void UpdateStatus()
        {
            string helper = RepairService.IsLicensingHelperAvailable()
                ? "已检测到 AdskLicensingInstHelper"
                : "未检测到 AdskLicensingInstHelper（许可切换不可用）";

            if (_lookupOnly)
            {
                _status.Text = "共 " + _products.Items.Count + " 条产品密钥记录 · 只读查询";
                return;
            }
            _status.Text = helper + " · 共 " + _products.Items.Count + " 项"
                         + (RepairService.DryRun ? " · 预演模式" : "");
        }

        private void CopyCommand()
        {
            if (_preview == null || string.IsNullOrWhiteSpace(_preview.Text))
            {
                return;
            }
            try
            {
                Clipboard.SetText(_preview.Text);
                _status.Text = "命令已复制到剪贴板。";
            }
            catch { }
        }

        private void RunCommand()
        {
            var sel = SelectedProduct;
            if (sel == null)
            {
                MessageBox.Show(this, "请先选择一个产品。", "未选择",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var st = _serverType.SelectedItem == null
                ? LicenseServerType.Single
                : (LicenseServerType)Enum.Parse(typeof(LicenseServerType), _serverType.SelectedItem.ToString(), true);

            string what = sel.Value.Key + "（" + SelectedYear + "）";
            string extra = _method == LicenseMethod.Network
                ? Environment.NewLine + "      · 许可服务器：" + _servers.Text
                  + Environment.NewLine + "      · 服务器类型：" + st
                : "";

            string warn = "将把以下产品切换到「" + RepairService.MethodDisplayName(_method) + "」："
                        + Environment.NewLine + Environment.NewLine
                        + "      · 产品：" + what
                        + Environment.NewLine + "      · 产品密钥：" + sel.Value.Value + extra
                        + Environment.NewLine + Environment.NewLine
                        + "该操作会修改本机 Autodesk 许可配置。确认执行？";

            if (MessageBox.Show(this, warn, "确认许可配置",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                return;
            }

            if (!RepairService.DryRun && !RepairService.IsAdministrator())
            {
                MessageBox.Show(this,
                    "该操作需要管理员权限。\r\n\r\n请关闭本程序，右键选择「以管理员身份运行」后重试。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _run.Enabled = false;
            _copy.Enabled = false;
            _status.Text = "正在执行...";

            try
            {
                string result = RepairService.ApplyLicense(sel.Value.Value, SelectedYear, _method, st, _servers.Text, null);

                bool failed = result.Contains("失败") || result.Contains("异常");
                if (RepairService.DryRun)
                {
                    MessageBox.Show(this, result, "预演结果",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, result, failed ? "执行失败" : "操作完成",
                        MessageBoxButtons.OK, failed ? MessageBoxIcon.Hand : MessageBoxIcon.Information);
                }
                _status.Text = failed ? "执行失败。" : "执行完成。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "执行时发生异常：" + ex.Message, "执行失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Hand);
                _status.Text = "执行失败。";
            }
            finally
            {
                _run.Enabled = true;
                _copy.Enabled = true;
            }
        }
    }
}
