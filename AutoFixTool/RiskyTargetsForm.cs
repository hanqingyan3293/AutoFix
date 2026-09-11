using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>
    /// 有争议删除目标的逐项选择。默认全部「保留」，用户显式勾选才删除。
    /// </summary>
    internal sealed class RiskyTargetsForm : Form
    {
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);
        private static readonly Color ColWarn = Color.FromArgb(186, 96, 12);

        private readonly List<RiskyTarget> _items;
        private readonly List<CheckBox> _boxes = new List<CheckBox>();
        private Label _summary;

        public RiskyTargetsForm(List<RiskyTarget> items)
        {
            _items = items;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "确认有争议的删除项";
            ClientSize = new Size(720, 200 + _items.Count * 96);
            MinimumSize = new Size(640, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "以下删除项可能影响其他软件",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold),
                Location = new Point(18, 12),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "这些目录被多个厂商或多个 Autodesk 产品共用。默认全部「保留」，"
                   + "如需删除请逐项勾选。",
                ForeColor = Color.FromArgb(150, 162, 182),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(20, 42),
                AutoSize = true
            });

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Color.FromArgb(238, 240, 244) };
            _summary = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 0, 0),
                ForeColor = Color.FromArgb(90, 100, 116)
            };
            footer.Controls.Add(_summary);

            var buttons = new Panel { Dock = DockStyle.Right, Width = 260, Padding = new Padding(0, 12, 16, 12) };
            var ok = new Button
            {
                Text = "继续",
                Location = new Point(56, 12),
                Width = 90,
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
                Location = new Point(156, 12),
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
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            footer.Controls.Add(buttons);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 6), AutoScroll = true };

            int y = 12;
            foreach (RiskyTarget t in _items)
            {
                var card = new Panel
                {
                    Location = new Point(0, y),
                    Width = 660,
                    Height = 88,
                    BackColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };

                card.Controls.Add(new Label
                {
                    Text = t.Title,
                    Location = new Point(12, 8),
                    AutoSize = true,
                    Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(38, 44, 56)
                });
                card.Controls.Add(new Label
                {
                    Text = t.Path,
                    Location = new Point(12, 30),
                    AutoSize = true,
                    ForeColor = ColMuted,
                    Font = new Font("Consolas", 8.5f)
                });
                card.Controls.Add(new Label
                {
                    Text = t.Reason,
                    Location = new Point(12, 50),
                    Width = 630,
                    Height = 32,
                    ForeColor = ColWarn,
                    Font = new Font("Microsoft YaHei UI", 8.5f)
                });

                var box = new CheckBox
                {
                    Text = "删除",
                    Location = new Point(586, 8),
                    Width = 64,
                    Cursor = Cursors.Hand,
                    Checked = false
                };
                box.CheckedChanged += (s, e) => UpdateSummary();
                card.Controls.Add(box);
                _boxes.Add(box);

                body.Controls.Add(card);
                y += 96;
            }

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            AcceptButton = ok;
            CancelButton = cancel;

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int del = 0;
            foreach (CheckBox b in _boxes)
            {
                if (b.Checked)
                {
                    del++;
                }
            }
            int keep = _items.Count - del;

            if (del == 0)
            {
                _summary.Text = "全部保留（" + keep + " 项）—— 不会删除任何有争议的目录。";
            }
            else
            {
                _summary.Text = "保留 " + keep + " 项，删除 " + del + " 项。";
            }
        }

        /// <summary>把用户选择写回 RiskyTarget 列表。</summary>
        public void ApplyDecisions()
        {
            for (int i = 0; i < _items.Count && i < _boxes.Count; i++)
            {
                _items[i].Keep = !_boxes[i].Checked;
            }
        }
    }
}

