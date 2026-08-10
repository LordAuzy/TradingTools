using System;
using System.Drawing;
using System.Windows.Forms;

namespace TradeDataAnalysis // Make sure this matches your project's namespace
{
    // Simple modal input dialog for entering preset names
    public class PromptForm : Form
    {
        private TextBox txtInput;
        public string InputText => txtInput.Text;

        public PromptForm(string title, string prompt)
        {
            this.Text = title;
            this.Size = new Size(400, 160);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            var lbl = new Label { Left = 12, Top = 12, Width = 360, Text = prompt };
            txtInput = new TextBox { Left = 12, Top = 35, Width = 360 };
            var btnOk = new Button { Text = "OK", Left = 216, Top = 70, Width = 75, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", Left = 297, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
            this.Controls.AddRange(new Control[] { lbl, txtInput, btnOk, btnCancel });
        }
    }
}