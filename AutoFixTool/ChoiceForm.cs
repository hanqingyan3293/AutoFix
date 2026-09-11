using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>从若干选项中选择一个（用于选择 CAD 降级目标版本）。</summary>
    internal sealed class ChoiceForm : Form
    {
        public string SelectedChoice { get; private set; }

        public ChoiceForm(string title, string prompt, string[] choices)
        {
            Text = title;
            ClientSize = new Size(400, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(247, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9f);

            var lbl = new Label
            {
                Text = prompt,
                Location = new Point(18, 18),
                Size = new Size(364, 44),
                ForeColor = Color.FromArgb(52, 60, 76)
            };

            var combo = new ComboBox
            {
                Location = new Point(18, 70),
                Width = 364,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            combo.Items.AddRange(choices);
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            var ok = new Button
            {
                Text = "确定",
                Location = new Point(212, 120),
                Size = new Size(80, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 127, 249),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (s, e) =>
            {
                if (combo.SelectedItem == null)
                {
                    return;
                }
                SelectedChoice = combo.SelectedItem.ToString();
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(302, 120),
                Size = new Size(80, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(52, 60, 76),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(206, 214, 226);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(lbl);
            Controls.Add(combo);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}

